using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// Water as a weapon (the flow itself is Sim.Liquids.cs). A current strong enough at a body's legs knocks it off its
/// feet, a stronger one sweeps it away to come down where the water leaves it, against whatever is in the way; how deep
/// and how fast the water runs against how heavy the body is decides which. Deep water drowns whoever is weighed down by
/// armour, while the rest swim ashore. A fast current washes loose rubble, boards and ash along with it and scours a
/// little of the earth it runs over. What burns beside water goes out (Sim.Fire.cs), and pitch afloat on running water
/// drifts with it, burning or not.
/// </summary>
public sealed partial class Sim
{
    double _floodAt;
    readonly List<(int x, int y, int z, byte m, int tx, int tz)> _scoured = new();

    /// <summary>How deep times how fast (m²/s) water must run against a man of 70 kg to knock him off his feet, and to sweep him away.</summary>
    const float FellsAt = .4f, SweepsAt = .7f;
    /// <summary>One with this much of itself under armour, or more (a knight in plate), goes under in deep water, and drowns in so many seconds.</summary>
    const float SinksArmoured = .6f, DrownsIn = 6;
    /// <summary>The current (m/s) that washes loose rubble along, and that scours earth.</summary>
    const float WashesAt = 1.2f, ScoursAt = 2.4f;

    /// <summary>How deep (voxels) and how fast (voxels a second, along x and z) the water runs at a column, if it runs there.</summary>
    bool RunsAt(int x, int z, out float depth, out float vx, out float vz)
    {
        depth = vx = vz = 0;
        if (!Current.TryGetValue(WaterPlanner.Pack(x, 0, z), out var f) || f.Depth < .3f) return false;
        depth = f.Depth;
        vx = f.X / f.Depth * LiquidTurns;
        vz = f.Z / f.Depth * LiquidTurns;
        return true;
    }

    /// <summary>Ten times a second: the current at work on bodies and on what lies loose in it.</summary>
    void TickFlood()
    {
        if (Time - _floodAt < .1) return;
        float dt = (float)Math.Min(.5, Time - _floodAt);
        _floodAt = Time;
        foreach (var u in Units)
        {
            if (u.UnderSince >= 0 && !Nav.Deep(u.Cell)) u.UnderSince = -1;
            if (Current.Count == 0 || u.Dead || u.Inside || u.Airborne != null || u.PinKg > 0 || Kinds.IsEngine(u.T)) continue;
            int x = (int)MathF.Floor(u.X), z = (int)MathF.Floor(u.Z), y = (int)MathF.Floor(u.Y);
            if (!RunsAt(x, z, out float depth, out float vx, out float vz)) continue;
            // the water against it: how deep over its feet, how fast
            float over = Math.Clamp(Current[WaterPlanner.Pack(x, 0, z)].Y0 + depth - y, 0, u.T.BodyH);
            if (over < 1) continue;
            float speed = MathF.Sqrt(vx * vx + vz * vz) / VoxelWorld.PerMetre;
            // one already down lies across the water and is carried off the sooner
            float hazard = over / VoxelWorld.PerMetre * speed / MathF.Sqrt(BodyKg(u.T) / 70) * (u.Down ? 1.6f : 1);
            if (hazard >= SweepsAt)
            {
                // off its feet and away with the water, tumbling along in it a little at a time while it runs this hard, to
                // be dashed against what it meets
                Fling(u, vx, 1.2f * VoxelWorld.PerMetre, vz, "вода");
            }
            else if (hazard >= FellsAt && !u.Down)
            {
                GoDown(u, 1.5 + hazard * 2);
                u.StaggerUntil = Time + .8;
                u.StagX = vx * .4f; u.StagZ = vz * .4f;
            }
        }
        Scour(dt);
    }

    /// <summary>How much of a body is under armour, 0…1: its trunk, legs and head together.</summary>
    public static float Armoured(UnitType t, int look)
    {
        var plan = Bodies.Plan(t, look);
        return (Cover(plan, OnBody.Trunk) + Cover(plan, OnBody.Legs) + Cover(plan, OnBody.Head)) / 3;
    }

    /// <summary>
    /// Under deep water: one weighed down by its armour does not swim but goes under, and drowns there unless the water
    /// goes down from over it in time. True while it is under (it stays where it is).
    /// </summary>
    bool Sinks(Unit u)
    {
        // lava is no water to swim in or go under
        if (Lavas.Count > 0 && World.Get((int)MathF.Floor(u.X), (int)MathF.Floor(u.Y), (int)MathF.Floor(u.Z)) == Mat.Lava) return false;
        if (Kinds.IsEngine(u.T) || u.T.Key is "risen" or "thrall") return false;
        if (Armoured(u.T, u.Look) < SinksArmoured) return false;
        if (u.UnderSince < 0) { u.UnderSince = Time; if (u.Team == Team.Player) Say($"{u.Name} тонет: доспех тянет на дно.", 3, u); }
        if (Time - u.UnderSince >= DrownsIn) { Fatal(u, "утонул", "вода", null); u.UnderSince = -1; }
        return true;
    }

    /// <summary>
    /// A fast current washes loose things along: rubble, boards, ash, a little at a time, set down again a few voxels on
    /// where the water slackens; faster still it scours away the earth it runs over, a voxel now and then.
    /// </summary>
    void Scour(float dt)
    {
        if (Current.Count == 0) return;
        _scoured.Clear();
        foreach (var (key, f) in Current)
        {
            if (_scoured.Count >= 16) break;
            if (f.Depth < .5f) continue;
            float vx = f.X / f.Depth * LiquidTurns, vz = f.Z / f.Depth * LiquidTurns;
            float speed = MathF.Sqrt(vx * vx + vz * vz) / VoxelWorld.PerMetre;
            if (speed < WashesAt) continue;
            var (x, _, z) = WaterPlanner.Unpack(key);
            int y = f.Y0 - 1;
            byte m = World.Get(x, y, z);
            bool loose = m is Mat.Rubble or Mat.Lumber or Mat.Char or Mat.Board, earth = m is Mat.Soil or Mat.Grass or Mat.Clay;
            if (!loose && !(earth && speed >= ScoursAt)) continue;
            // what lies loose goes often; earth now and then
            float chance = (loose ? 2f : .08f) * (speed - WashesAt) * dt;
            if (Rng.NextDouble() >= chance) continue;
            float run = MathF.Sqrt(vx * vx + vz * vz);
            int tx = x + (int)MathF.Round(vx / run * (3 + Rng.Next(4))), tz = z + (int)MathF.Round(vz / run * (3 + Rng.Next(4)));
            _scoured.Add((x, y, z, m, tx, tz));
        }
        foreach (var (x, y, z, m, tx, tz) in _scoured)
        {
            if (World.Get(x, y, z) != m) continue;
            World.Set(x, y, z, Mat.Air);
            if (m is Mat.Soil or Mat.Grass or Mat.Clay) { Scoured++; continue; }
            // set down again downstream, on the first thing under the water there
            if (!World.InBounds(tx, y, tz)) continue;
            int ty = Math.Min(World.SY - 2, y + 2);
            while (ty > 1 && Open(World.Get(tx, ty - 1, tz))) ty--;
            if (Open(World.Get(tx, ty, tz))) { World.Set(tx, ty, tz, m); Washed++; }
        }
    }

    /// <summary>Loose voxels washed along by the water, and voxels of earth it scoured away, since the start.</summary>
    public int Washed { get; private set; }
    public int Scoured { get; private set; }
}
