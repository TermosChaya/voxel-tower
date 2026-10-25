using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace VoxelTower.Core;

public sealed partial class Sim
{
    /// <summary>Cells beside the citadel where raiders batter it.</summary>
    public readonly List<int> Goals;
    HashSet<int> _goalSet;

    /// <summary>The way to the citadel for each kind of raider; rebuilt in the background as the valley changes.</summary>
    public Dictionary<UnitType, FlowField> Fields { get; private set; } = new();
    Task<Dictionary<UnitType, FlowField>> _fieldTask;
    // a field still being built when the valley or the horde's stores changed for good is thrown away when it arrives
    int _fieldGen, _fieldTaskGen;
    int _fieldVersion = -1;
    bool _fieldWood, _fieldEarth;
    double _fieldAt = -99;
    public double LastFieldMs { get; private set; }

    bool HasWood => HordeStock[Stockpile.Wood] >= Siege.BridgeWood;
    bool HasEarth => HordeStock[Stockpile.Earth] >= Siege.FillEarth;

    /// <summary>
    /// Builds every raider's field now, on this thread (tests and staged scenes), for what the horde holds, or
    /// (<paramref name="coming"/>) for what the next wave will bring.
    /// </summary>
    public void ComputeFields(bool coming = false)
    {
        _fieldGen++;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        if (coming) Stores(out _fieldWood, out _fieldEarth);
        else { _fieldWood = HasWood; _fieldEarth = HasEarth; }
        Fields = BuildFields(_fieldWood, _fieldEarth);
        LastFieldMs = sw.Elapsed.TotalMilliseconds;
        _fieldVersion = Nav.Version; _fieldAt = Time;
    }

    Dictionary<UnitType, FlowField> BuildFields(bool wood, bool earth)
    {
        var d = new Dictionary<UnitType, FlowField>();
        foreach (var t in Kinds.HordeKinds) d[t] = FlowField.Compute(Nav, t.Move, Goals, 900, wood, earth);
        return d;
    }

    /// <summary>What the horde will have to build with: what it holds, or between waves what the next one brings (some of both).</summary>
    void Stores(out bool wood, out bool earth)
    {
        bool between = !Raids.Active && !AnyRaider();
        wood = HasWood || between;
        earth = HasEarth || between;
    }

    /// <summary>The fields are out of date (the horde's stores changed): build them again in the background soon.</summary>
    public void InvalidateFields()
    {
        _fieldGen++;
        _fieldVersion = -1;
        _fieldAt = -99;
    }

    void UpdateFields()
    {
        if (_fieldTask != null)
        {
            if (!_fieldTask.IsCompleted) return;
            if (_fieldTask.Status == TaskStatus.RanToCompletion && _fieldTaskGen == _fieldGen) Fields = _fieldTask.Result;
            _fieldTask = null;
        }
        // wanted while the horde is coming or here, once at the start so an early call finds a way at hand, and between
        // waves while the bands' ways are shown (every few seconds: the defences change as they are built)
        bool between = !Raids.Active && !RaidSoon(12) && !AnyRaider();
        bool needed = !between || Fields.Count == 0 || WantTrails;
        Stores(out bool wood, out bool earth);
        bool same = _fieldVersion == Nav.Version && _fieldWood == wood && _fieldEarth == earth;
        if (!needed || (same && Fields.Count > 0) || Time - _fieldAt < (between ? 4 : 2)) return;
        // the grid keeps changing while this runs; a field built from a slightly stale picture is still a good guide
        _fieldVersion = Nav.Version; _fieldAt = Time;
        _fieldWood = wood; _fieldEarth = earth;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _fieldTaskGen = _fieldGen;
        _fieldTask = Task.Run(() => { var f = BuildFields(wood, earth); LastFieldMs = sw.Elapsed.TotalMilliseconds; return f; });
    }

    // ------------------------------------------------------------ raiders

    public Unit SpawnRaider(UnitType t)
    {
        for (int tries = 0; tries < 12; tries++)
        {
            // in by the western edge, along its straight side
            int z = Site.Hex == null ? 24 + Rng.Next(World.SZ - 48) : Site.WestZ0 + Rng.Next(Math.Max(1, Site.WestZ1 - Site.WestZ0));
            int c = NearestStand(Site.EntryX, z, t.Move.Height);
            if (c < 0) continue;
            var u = Spawn(t, c);
            SendRaid(u);
            return u;
        }
        return null;
    }

    void RaiderTick(Unit u, float dt)
    {
        _goalSet ??= new HashSet<int>(Goals);
        // bound: it lies where it fell, or goes where it is led
        if (u.Bound) { BoundTick(u, dt); return; }
        // held fast in a spell's chains: it neither goes nor strikes
        if (Time < u.ChainedUntil) { u.State = UnitState.Idle; u.Path = null; return; }
        // a bone thrall's minute is up
        if (u.T == Kinds.Thrall && Time >= u.CrumbleAt) { Kill(u, null, "рассыпался"); return; }
        // a sorcerer's spells, cast as it goes
        if (u.T == Kinds.Shaman && !u.Routed) ShamanTick(u);
        else if (u.T == Kinds.Witch && !u.Routed) WitchTick(u);
        else if (u.T == Kinds.Necromancer && !u.Routed) NecromancerTick(u, dt);
        // an alchemist stops out of reach and throws its flasks while it has a mark and flasks to throw
        if (u.T == Kinds.Alchemist && !u.Routed && AlchemistTick(u, dt)) return;
        // hurt to a quarter of itself, a raider has had enough (a leader leads to the end, a guard keeps its camp)
        if (!u.Routed && !u.Leader && !u.Guard && u.Hp < u.MaxHp * .25f) u.Routed = true;
        // or hurt past bearing: it breaks (a leader holds on longer)
        if (!u.Routed && !u.Guard && u.Champion == 0 && u.Pain >= (u.Leader ? LeaderBreak : BreakPain) * FearOfUs) u.Routed = true;
        // a hill giant tears up a boulder and hurls it, or is at it (Sim.Giants.cs)
        if (u.T == Kinds.HillGiant && !u.Routed && GiantTick(u, dt)) return;
        // or a leg broken under it: it limps back west (Sim.Trauma.cs)
        if (!u.Routed && !u.Guard && u.Champion == 0 && Lame(u)) u.Routed = true;
        // the horde's envoy comes to bargain, and fights nobody
        if (u.Envoy && EnvoyTick(u, dt)) return;
        // the horde's engines go about their own work, and never run
        if (Kinds.IsEngine(u.T)) { EngineTick(u, dt); return; }
        // a thief goes about its own business near the citadel
        if (u.T == Kinds.Thief && !u.Routed && ThiefTick(u, dt)) return;
        var foe = Nearest(u, Team.Player, u.T.Reach + 8);
        // one running away bites only what has caught it
        if (foe != null && InReach(u, foe) && Armed(u) && (!u.Routed || Time - u.LastHurt < 1.5))
        {
            u.State = UnitState.Fight;
            u.Target = foe;
            u.FoughtAt = Time; u.FoughtWith = foe;
            u.Facing = MathF.Atan2(foe.Z - u.Z, foe.X - u.X);
            if (u.Cooldown <= 0) { Strike(u, foe); u.Cooldown = u.T.AttackTime * (.9f + (float)Rng.NextDouble() * .2f) / PainPace(u); }
            Footwork(u, foe, dt);
            return;
        }
        u.Target = null;
        // one of its own pinned close by: dug out before anything
        if (!u.Routed && Armed(u) && !u.Envoy && Rescue(u, dt, 12 * VoxelWorld.PerMetre)) return;
        // nothing left to fight with, or no heart left for it: back west and out of the valley
        if (!Armed(u) || u.Routed) { StopCarve(u); StopBuild(u); Retreat(u, dt); return; }
        KeepEscort(u);
        if (u.Guard) { GuardTick(u, dt); return; }
        // an engine of ours near, with nobody of ours at hand: it goes for it and its crew (Sim.OurEngines.cs)
        if (u.CarveCell < 0 && u.BuildCell < 0 && GoForEngine(u, dt)) return;
        // one of its own fighting close by: it goes to help
        if (u.CarveCell < 0 && u.BuildCell < 0 && u.Band is not { State: BandState.Forming } && Help(u, dt)) return;
        // a band gathers where it came in before it sets out
        if (u.Band is { State: BandState.Forming }) { u.State = UnitState.Idle; return; }
        if (u.Raid != null && RaidTick(u, dt)) return;
        March(u, dt);
    }

    /// <summary>
    /// On along the horde's way: at the citadel, batter it; hack through what stands in the way, build what has to be
    /// built, walk the rest, with the band or by the field.
    /// </summary>
    void March(Unit u, float dt)
    {
        // a miner under the ground, or sending its band's miners under a wall of ours ahead (Sim.Tunnels.cs)
        if (u.T == Kinds.Miner && TunnelTick(u, dt)) return;
        if (_goalSet.Contains(u.Cell))
        {
            u.State = UnitState.Siege;
            u.Facing = MathF.Atan2(Site.KeepZ - u.Z, Site.KeepX - u.X);
            if (u.Cooldown <= 0) { KeepHp -= u.T.KeepDamage * KeepGuard * KeepHurt; u.Swing = 1; u.Cooldown = u.T.AttackTime; }
            return;
        }
        if (u.CarveCell >= 0) { Carve(u, dt); return; }
        if (u.BuildCell >= 0) { BuildTick(u, dt); return; }
        if (u.Path != null && u.PathI < u.Path.Count) { Walk(u, dt); return; }
        if (BandStep(u, dt)) return;
        if (FieldOf(u.T) is not { } f) { u.State = UnitState.Idle; return; }
        int next = f.Next(Nav, u.Cell, out float carve, out var works);
        if (next < 0) { u.State = UnitState.Idle; return; }
        if (works != Works.None)
        {
            // out of timber or earth a moment ago: wait for the way round that the next field will show
            if (Time < u.NoBuildUntil) { u.State = UnitState.Idle; return; }
            StartBuild(u, next, works);
            if (u.BuildList.Count == 0)
            {
                // nothing can go there after all: let the next field find another way
                StopBuild(u);
                u.NoBuildUntil = Time + 3;
                _fieldVersion = -1;
            }
            return;
        }
        // a sapper with a charge does not hack through a wall of ours: it blows it
        if (carve > 0 && u.Charge && PlantCharge(u, next)) return;
        if (carve > 0) { StartCarve(u, next); return; }
        u.Path ??= new List<int>();
        u.Path.Clear(); u.Path.Add(next); u.PathI = 0;
        Walk(u, dt);
    }

    /// <summary>
    /// A raider goes to the help of one of its own fighting ours close by, if it can walk there; true while it is on
    /// its way (once it is beside the fight, the fight itself takes it).
    /// </summary>
    bool Help(Unit u, float dt)
    {
        if (Climbing(u)) return false;
        Unit foe = null;
        float best = HelpRange;
        foreach (var o in Units)
        {
            if (o == u || o.Dead || o.Team != Team.Horde) continue;
            // fighting now, or a moment ago (a fight is all steps in and out of reach), or set on hand to hand and
            // knocked off its feet before it could strike back
            Unit t = Time - o.FoughtAt <= 3 && o.FoughtWith is { Dead: false, Team: Team.Player } f ? f
                : Time - o.LastHurt <= 3 && o.HurtBy is { Dead: false, Team: Team.Player } h && Dist(h, o) < 4 * VoxelWorld.PerMetre ? h : null;
            if (t == null) continue;
            float d = Dist(u, o);
            if (d < best) { best = d; foe = t; }
        }
        if (foe == null) return false;
        if (u.Path == null || u.PathI >= u.Path.Count || Time >= u.NextSearch)
        {
            u.NextSearch = Time + .8;
            if (!GoTo(u, foe.Cell, adjacent: true, budget: 2500)) u.Path = null;
        }
        if (u.Path == null || u.PathI >= u.Path.Count) return false;
        Walk(u, dt);
        return true;
    }

    /// <summary>How far off, in voxels, a raider sees one of its own in a fight and comes.</summary>
    const float HelpRange = 16 * VoxelWorld.PerMetre;

    /// <summary>Raiders that got away from this wave alive and able to fight, to come back with the next.</summary>
    public readonly List<Survivor> Escaped = new();

    /// <summary>
    /// A raider that can no longer fight (or will not) goes back west and is gone from the valley; one that can still
    /// fight comes back with the next wave, so it is worth running down.
    /// </summary>
    void Retreat(Unit u, float dt)
    {
        u.Raid = null;
        // one come in by another side than the west goes out by where it came in
        int home = u.Band is { Edge: >= 0 } hb && hb.Edge != WestEdge && hb.Entry >= 0 ? hb.Entry : -1;
        var (hx, _, hz) = home >= 0 ? Nav.Feet(home) : (0f, 0f, 0f);
        bool gone = home >= 0 ? (u.X - hx) * (u.X - hx) + (u.Z - hz) * (u.Z - hz) < 24 * 24 : u.X < Site.SpawnStrip;
        if (gone)
        {
            if (u.T == Kinds.Thief) ThiefGone(u);
            if (u.Envoy) { u.Dead = true; u.DiedAt = -999; u.State = UnitState.Dead; return; }
            if (Armed(u) && !u.Guard)
            {
                if (u.Wounds != null) { u.Wounds.Bleed = 0; u.Wounds.Arrows.Clear(); }
                Escaped.Add(new Survivor { Kind = u.T, Look = u.Look, Hp = u.Hp, Wounds = u.Wounds, Champion = u.Champion, Title = u.Title });
                Say($"{u.T.Name} сбежал и вернётся со следующим набегом.", 2, u);
            }
            u.Dead = true; u.DiedAt = -999; u.State = UnitState.Dead;
            return;
        }
        if (u.Path == null || u.PathI >= u.Path.Count)
        {
            u.State = UnitState.Idle;
            if (Time < u.NextSearch) return;
            u.NextSearch = Time + 2;
            int c = home >= 0 ? home : NearestStand(Site.EntryX, Site.EntryZ((int)u.Z), u.T.Move.Height);
            if (c < 0 || !GoTo(u, c, adjacent: true, budget: 40000)) return;
        }
        Walk(u, dt);
        u.State = UnitState.Flee;
    }

    /// <summary>Lists the voxels in the way into the next cell (and above its head when climbing), nearest first.</summary>
    void StartCarve(Unit u, int next)
    {
        u.CarveCell = next; u.CarveList.Clear(); u.CarveI = 0; u.CarveEffort = 0;
        int h = u.T.Move.Height, qi = Nav.I(next), qy = Nav.Y(next), qk = Nav.K(next), py = Nav.Y(u.Cell);
        var cells = new List<int>();
        for (int y = qy; y < qy + h && y < Nav.NY; y++) cells.Add(Nav.Id(qi, y, qk));
        for (int y = py + h; y < py + h + (qy - py) && y < Nav.NY; y++) cells.Add(Nav.Id(Nav.I(u.Cell), y, Nav.K(u.Cell)));
        foreach (int c in cells)
        {
            if (Nav.Fill[c] < NavGrid.SolidT) continue;
            int x0 = Nav.I(c) << NavGrid.Shift, y0 = Nav.Y(c) << NavGrid.Shift, z0 = Nav.K(c) << NavGrid.Shift;
            for (int y = y0; y < y0 + NavGrid.CS; y++)
                for (int z = z0; z < z0 + NavGrid.CS; z++)
                    for (int x = x0; x < x0 + NavGrid.CS; x++)
                        if (Blocks(World.Get(x, y, z))) u.CarveList.Add((x, y, z));
        }
        float hx = u.X, hy = u.Y + u.T.BodyH * .6f, hz = u.Z;
        u.CarveList.Sort((a, b) =>
            ((a.x - hx) * (a.x - hx) + (a.y - hy) * (a.y - hy) + (a.z - hz) * (a.z - hz)).CompareTo((b.x - hx) * (b.x - hx) + (b.y - hy) * (b.y - hy) + (b.z - hz) * (b.z - hz)));
        u.State = UnitState.Carve;
        // a miner up against a wall of ours digs the ground out from under it instead
        if (u.T == Kinds.Miner) Sap(u);
    }

    static bool Blocks(byte m) => m != 0 && !Mat.Passable[m];

    static int Group(byte m) => Mat.Def[m].Group switch { MatGroup.Earth => 0, MatGroup.Wood => 1, MatGroup.Stone => 2, MatGroup.Rock => 3, _ => -1 };

    void Carve(Unit u, float dt)
    {
        // with a torch, what burns is set alight first; a miner under a wall keeps at it till all the ground is out
        if (u.Torch && Kindle(u, dt)) return;
        if (Sapping(u, dt)) return;
        // a hill giant smashes through a heap at a blow
        if (u.T == Kinds.HillGiant) { GiantSmash(u); return; }
        u.State = UnitState.Carve;
        u.CarveEffort += dt;
        var rate = u.T.Move.Carve;
        var target = u.CarveCell;
        u.Facing = MathF.Atan2((Nav.K(target) << NavGrid.Shift) + 4 - u.Z, (Nav.I(target) << NavGrid.Shift) + 4 - u.X);
        int removed = 0;
        while (u.CarveI < u.CarveList.Count)
        {
            var (x, y, z) = u.CarveList[u.CarveI];
            byte m = World.Get(x, y, z);
            if (!Blocks(m)) { u.CarveI++; continue; }
            int g = Group(m);
            if (g < 0 || rate == null || rate[g] <= 0) { StopCarve(u); return; }
            // stone in lime mortar holds out a third longer; what a shaman cursed gives twice as fast
            // under a stone skin three times as long
            float cost = 1f / (rate[g] * (g == 2 ? StoneHack * HideHack : 1) * (Curses.Count > 0 ? CurseHack(x, y, z) : 1) * (Skins.Count > 0 ? SkinHack(x, y, z) : 1));
            if (u.CarveEffort < cost) break;
            u.CarveEffort -= cost;
            u.CarveI++;
            World.Set(x, y, z, Mat.Air);
            if (Orders.IsRemove(Orders.At(x, y, z))) Orders.Clear(x, y, z);
            // half of what is hacked out is fit to build with
            int kind = Labour.YieldKind(m);
            if (kind != Stockpile.None) HordeStock[kind] += Labour.YieldShare(m) * .5;
            Hacked?.Invoke(x, y, z, m);
            removed++;
            u.Swing = 1;
        }
        if (u.CarveI >= u.CarveList.Count || (removed > 0 && WayOpen(u))) StopCarve(u);
    }

    /// <summary>Called for every voxel the horde hacks out, for chips.</summary>
    public Action<int, int, int, byte> Hacked;

    /// <summary>Called for every voxel the horde puts down in a siege work.</summary>
    public Action<int, int, int, byte> Raised;

    bool WayOpen(Unit u)
    {
        int h = u.T.Move.Height, c = u.CarveCell, qi = Nav.I(c), qy = Nav.Y(c), qk = Nav.K(c);
        for (int y = qy; y < qy + h && y < Nav.NY; y++)
        {
            int x0 = qi << NavGrid.Shift, y0 = y << NavGrid.Shift, z0 = qk << NavGrid.Shift, solid = 0;
            for (int yy = y0; yy < y0 + NavGrid.CS; yy += 2)
                for (int z = z0; z < z0 + NavGrid.CS; z += 2)
                    for (int x = x0; x < x0 + NavGrid.CS; x += 2)
                        if (Blocks(World.Get(x, yy, z))) solid++;
            if (solid * 8 >= NavGrid.SolidT) return false;
        }
        return true;
    }

    void StopCarve(Unit u)
    {
        u.CarveCell = -1;
        u.CarveList.Clear();
        u.CarveI = 0;
    }

    // ------------------------------------------------------------ siege works

    /// <summary>Lists what is to be put down before stepping into the next cell: a length of ladder, a deck, a fill.</summary>
    void StartBuild(Unit u, int next, Works works)
    {
        u.BuildCell = next; u.Building = works; u.BuildList.Clear(); u.BuildI = 0; u.BuildEffort = 0;
        int qi = Nav.I(next), qy = Nav.Y(next), qk = Nav.K(next);
        if (works == Works.Ladder)
        {
            // against whatever it leans on, from the feet if it starts on the ground
            int from = Nav.Ladder[u.Cell] != 0 || !Nav.Ground(Nav.I(u.Cell), Nav.Y(u.Cell) - 1, Nav.K(u.Cell)) ? qy << NavGrid.Shift : Nav.Floor[u.Cell];
            LadderVoxels(qi, qy, qk, from, (qy + 1) << NavGrid.Shift, Mat.SiegeLadder, u.BuildList);
        }
        else
        {
            int x0 = qi << NavGrid.Shift, z0 = qk << NavGrid.Shift, top = qy << NavGrid.Shift;
            int y0 = works == Works.Bridge ? top - 2 : top - NavGrid.CS * Math.Clamp(FlowField.GapBelow(Nav, qi, qy, qk), 1, FlowField.MaxFill);
            byte m = works == Works.Bridge ? Mat.Deck : Mat.Soil;
            for (int y = y0; y < top; y++)
                for (int z = z0; z < z0 + NavGrid.CS; z++)
                    for (int x = x0; x < x0 + NavGrid.CS; x++)
                        if (Brushes.Free(World.Get(x, y, z))) u.BuildList.Add((x, y, z, m));
        }
        u.State = UnitState.Work;
    }

    /// <summary>
    /// A ladder up the column of cell (i, k), from voxel height y0 to y1: two rails and rungs every third voxel, laid against
    /// the face of whatever solid stands beside it.
    /// </summary>
    public void LadderVoxels(int i, int cy, int k, int y0, int y1, byte mat, List<(int, int, int, byte)> into)
    {
        int best = 0, dir = 0;
        for (int d = 0; d < 4; d++)
        {
            int n = 0;
            for (int yy = cy - 1; yy <= cy + 1; yy++) if (Nav.Solid(i + DX4[d], yy, k + DK4[d])) n++;
            if (n > best) { best = n; dir = d; }
        }
        int x0 = i << NavGrid.Shift, z0 = k << NavGrid.Shift;
        for (int y = Math.Max(1, y0); y < y1 && y < World.SY; y++)
            for (int t = 1; t <= 6; t++)
            {
                bool rail = t == 1 || t == 6, rung = (y % 3) == 0;
                if (!rail && !rung) continue;
                // from the side it leans on inward, to the first free voxel: right against the face
                for (int s = 0; s < NavGrid.CS; s++)
                {
                    int x, z;
                    if (dir < 2) { x = dir == 0 ? x0 + NavGrid.CS - 1 - s : x0 + s; z = z0 + t; }
                    else { z = dir == 2 ? z0 + NavGrid.CS - 1 - s : z0 + s; x = x0 + t; }
                    byte m = World.Get(x, y, z);
                    if (m == Mat.Air) { into.Add((x, y, z, mat)); break; }
                    if (Mat.Passable[m]) break;
                }
            }
    }

    static readonly int[] DX4 = { 1, -1, 0, 0 }, DK4 = { 0, 0, 1, -1 };

    void BuildTick(Unit u, float dt)
    {
        u.State = UnitState.Work;
        var b = u.BuildCell;
        u.Facing = MathF.Atan2((Nav.K(b) << NavGrid.Shift) + 4 - u.Z, (Nav.I(b) << NavGrid.Shift) + 4 - u.X);
        if (u.Building != Works.Ladder) CountBodies();
        float perVoxel = u.Building switch
        {
            Works.Ladder => Siege.LadderWork / Siege.LadderWood,
            Works.Bridge => Siege.BridgeWork / Siege.BridgeWood,
            _ => Siege.FillWork / Siege.FillEarth,
        };
        int store = u.Building == Works.Fill ? Stockpile.Earth : Stockpile.Wood;
        // a fill goes in from the bottom up, so nothing hangs in the air while it rises
        u.BuildEffort += Siege.Rate * u.T.Move.Build * dt;
        while (u.BuildI < u.BuildList.Count && u.BuildEffort >= perVoxel)
        {
            var (x, y, z, m) = u.BuildList[u.BuildI];
            if (!Brushes.Free(World.Get(x, y, z))) { u.BuildI++; continue; }
            // nobody gets buried under a fill
            if (m == Mat.Soil && _bodies[Nav.CellOf(x, y, z)] > 0) break;
            if (!HordeStock.Take(store, 1))
            {
                u.NoBuildUntil = Time + 4;
                StopBuild(u);
                return;
            }
            u.BuildEffort -= perVoxel;
            u.BuildI++;
            World.Set(x, y, z, m);
            if (m is Mat.SiegeLadder or Mat.Deck) _works.Add(Nav.CellOf(x, y, z));
            Raised?.Invoke(x, y, z, m);
            u.Swing = 1;
        }
        u.BuildEffort = Math.Min(u.BuildEffort, 40);
        if (u.BuildI >= u.BuildList.Count) StopBuild(u);
    }

    void StopBuild(Unit u)
    {
        u.BuildCell = -1;
        u.Building = Works.None;
        u.BuildList.Clear();
        u.BuildI = 0;
    }
}

/// <summary>A raider that got away from a wave alive and able to fight: it comes back with the next one, its wounds on it.</summary>
public sealed class Survivor
{
    public UnitType Kind;
    public int Look;
    public float Hp;
    public BodyState Wounds;
    public byte Champion;
    public string Title;
}
