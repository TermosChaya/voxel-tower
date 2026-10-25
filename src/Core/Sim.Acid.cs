using System;
using System.Collections.Generic;
using System.IO;

namespace VoxelTower.Core;

/// <summary>
/// Acid, out of the alchemists' flasks. A flask breaks where it comes down and lets out a splash of it, a dozen voxels
/// or so, and those it broke on or beside are burnt. Acid is a liquid like water and pitch (Sim.Liquids.cs): it runs off
/// whatever it lands on into the lowest place it can reach, down a wall's face to its foot, into a ditch. Lying there it
/// eats whatever it touches below it and to its sides, each voxel at its own pace: turf and earth quickly, timber a
/// little slower, laid stone slowly, a gate of oak bound with iron and iron itself slowest; the citadel's stone and the
/// bedrock not at all. A voxel eaten through is gone, and what stood on it comes down by the rules of what holds what
/// up; the acid that ate it is spent, often enough, the more the harder the voxel was. Water it runs into or that runs
/// into it takes it up and it is lost in it; rain thins what lies open to the sky; acid with nothing to eat goes flat
/// by itself in a couple of minutes. Whoever stands in it is burnt about the feet, and its fumes make those near it
/// cough and step back out of them; the burns eat at the skin, at cloth and mail, and at a shield, which gives way once
/// most of it is gone. The dead lying in it are eaten too.
/// </summary>
public sealed partial class Sim
{
    /// <summary>Every voxel of acid in the valley (packed).</summary>
    public readonly HashSet<int> Acids = new();
    /// <summary>How far the acid has eaten into each voxel it lies against, 0 to 1 (packed).</summary>
    public readonly Dictionary<int, float> Etched = new();
    /// <summary>Flasks broken since the start; voxels the acid ate through; voxels of acid lost in water.</summary>
    public int FlasksBroken, AcidAte, AcidDiluted;

    /// <summary>A flask broke and let out its acid: where.</summary>
    public event Action<float, float, float> FlaskBroke;
    /// <summary>Acid ate through a voxel: where, and what it was (water: the acid lost in water there).</summary>
    public event Action<int, int, int, byte> Dissolved;
    /// <summary>Someone coughed in the fumes.</summary>
    public event Action<Unit> Coughed;

    /// <summary>Acid is looked at so often, seconds.</summary>
    const float AcidEvery = .25f;
    /// <summary>Voxels of acid in a flask.</summary>
    public const int FlaskAcid = 16;
    /// <summary>How long acid with nothing to eat lasts, seconds on the whole; rain thins what is open to the sky so much sooner.</summary>
    const float AcidStale = 120, AcidRain = 14;
    /// <summary>How far a flask's splash burns those beside where it broke, voxels.</summary>
    const float SplashReach = 1.6f * VoxelWorld.PerMetre;

    float _acidDue;
    double _acidSaid = -99, _acidAteSaid = -99, _coughSaid = -99;
    readonly List<int> _acidNow = new();
    readonly Dictionary<int, (int n, float x, float y, float z)> _acidCells = new();
    static readonly (int, int, int)[] Touching = { (0, -1, 0), (1, 0, 0), (-1, 0, 0), (0, 0, 1), (0, 0, -1) };

    /// <summary>
    /// How fast acid eats a voxel of it, a share a second with one voxel of acid against it: 0 for what it cannot eat. Grass
    /// and leaves at once, earth and turf quickly, timber more slowly, laid stone and rock slowly, a gate of oak bound with
    /// iron (and the drawbridge's oak) slower, iron slowest.
    /// </summary>
    public static float AcidBite(byte m)
    {
        if (m == Mat.Air || m == Mat.Water || m == Mat.Acid || m == Mat.Pitch) return 0;
        if (Mat.IsLadder[m]) return 1 / 1.6f;
        if (Mat.IsSprig(m) || Mat.IsLeafy(m) || m is Mat.Banner or Mat.BannerTrim or Mat.Sack or Mat.Bedding) return 1 / .4f;
        if (m is Mat.Gate or Mat.GateShut or Mat.GateOpen or Mat.DrawDeck) return 1 / 12f;
        if (m == Mat.Iron) return 1 / 25f;
        // the valley's own ground drinks it in more than it is eaten by it
        if (Mat.IsNatural[m] && Mat.IsEarthy[m]) return 1 / 2.5f;
        return Mat.Def[m].Group switch { MatGroup.Earth => 1 / 1.4f, MatGroup.Wood => 1 / 2.2f, MatGroup.Stone or MatGroup.Rock => 1 / 5.5f, _ => 0 };
    }

    /// <summary>
    /// The chance that a voxel of acid is spent eating through one of these: next to none on grass, soon on the ground
    /// (it soaks in), less on what is built of earth or timber, more on stone, all of it on iron.
    /// </summary>
    static float SpentOn(byte m)
    {
        if (Mat.IsSprig(m) || Mat.IsLeafy(m)) return .05f;
        if (m is Mat.Gate or Mat.GateShut or Mat.GateOpen or Mat.DrawDeck or Mat.Iron) return .9f;
        if (Mat.IsNatural[m] && Mat.IsEarthy[m]) return .45f;
        return Mat.Def[m].Group switch { MatGroup.Earth => .3f, MatGroup.Wood => .3f, _ => .35f };
    }

    /// <summary>Every voxel written: the acid in the valley kept count of.</summary>
    void OnAcidEdit(int x, int y, int z, byte old)
    {
        byte now = World.Get(x, y, z);
        int p = WaterPlanner.Pack(x, y, z);
        if (now == Mat.Acid) { Acids.Add(p); return; }
        if (old != Mat.Acid) return;
        // spent or lost where it lay: what lay over it or beside it higher up runs into its place as the liquids run
        Acids.Remove(p);
    }

    /// <summary>Acid at work, four times a second: eating, spent, thinned, lost in water; and the people in it and about it.</summary>
    void TickAcid(float dt)
    {
        _acidDue += dt;
        if (_acidDue < AcidEvery) return;
        float step = _acidDue;
        _acidDue = 0;
        if (Acids.Count == 0) { if (Etched.Count > 0) Etched.Clear(); return; }
        bool rain = Weather == Weather.Rain;
        _acidNow.Clear();
        _acidNow.AddRange(Acids);
        foreach (int p in _acidNow)
        {
            if (!Acids.Contains(p)) continue;
            var (x, y, z) = WaterPlanner.Unpack(p);
            // water beside it or under it takes it up
            if (Wet(x, y, z)) { World.Set(x, y, z, Mat.Air); AcidDiluted++; Dissolved?.Invoke(x, y, z, Mat.Water); continue; }
            if (rain && World.ColTop(x, z) <= y + 1 && Rng.NextDouble() < step / AcidRain) { World.Set(x, y, z, Mat.Air); continue; }
            // it eats all it touches below and to the sides at once, each at its own pace
            bool ate = false, spent = false;
            foreach (var (dx, dy, dz) in Touching)
            {
                int tx = x + dx, ty = y + dy, tz = z + dz;
                if (ty < 2 || !World.InBounds(tx, ty, tz)) continue;
                byte m = World.Get(tx, ty, tz);
                float bite = AcidBite(m);
                if (bite <= 0) continue;
                ate = true;
                int tp = WaterPlanner.Pack(tx, ty, tz);
                float e = Etched.GetValueOrDefault(tp) + step * bite;
                if (e < 1) { Etched[tp] = e; if (BearsDown(m) > 0) Pit(tp, e); continue; }
                Etched.Remove(tp);
                EatThrough(tx, ty, tz, m);
                if (Rng.NextDouble() < SpentOn(m)) { spent = true; break; }
            }
            if (spent || !ate && Rng.NextDouble() < step / AcidStale) World.Set(x, y, z, Mat.Air);
        }
        // what no acid lies against any more keeps the pits eaten into it, but is not eaten on
        if (Etched.Count > 3 * Acids.Count + 64)
        {
            _acidNow.Clear();
            foreach (var (tp, _) in Etched)
            {
                var (x, y, z) = WaterPlanner.Unpack(tp);
                bool touched = false;
                foreach (var (dx, dy, dz) in Touching) if (World.Get(x - dx, y - dy, z - dz) == Mat.Acid) { touched = true; break; }
                if (!touched || AcidBite(World.Get(x, y, z)) <= 0) _acidNow.Add(tp);
            }
            foreach (int tp in _acidNow) Etched.Remove(tp);
        }
        AcidOnPeople(step);
    }

    static readonly (int dx, int dz)[] SpillWays = { (1, 0), (0, 1), (-1, 0), (0, -1) };

    /// <summary>
    /// Acid running down from here, beside whatever face is there, to the first thing under it: it eats a little into
    /// every face it runs down, so a wall it went down is streaked where it went. Where it comes to rest.
    /// </summary>
    int Trickle(int x, int y, int z)
    {
        while (y > 2 && World.Get(x, y - 1, z) == Mat.Air)
        {
            y--;
            foreach (var (dx, dz) in SpillWays)
            {
                int fx = x + dx, fz = z + dz;
                byte m = World.Get(fx, y, fz);
                float bite = AcidBite(m);
                if (bite <= 0) continue;
                int fp = WaterPlanner.Pack(fx, y, fz);
                float e = Etched.GetValueOrDefault(fp) + MathF.Min(.5f, bite * 1.5f);
                if (e < 1) Etched[fp] = e;
                else { Etched.Remove(fp); EatThrough(fx, y, fz, m); }
            }
        }
        return y;
    }

    /// <summary>The acid has eaten through a voxel: it is gone, and whatever was marked or burning there with it.</summary>
    void EatThrough(int x, int y, int z, byte m)
    {
        World.Set(x, y, z, Mat.Air);
        if (Orders.IsRemove(Orders.At(x, y, z))) Orders.Clear(x, y, z);
        Burning.Remove(WaterPlanner.Pack(x, y, z));
        AcidAte++;
        Dissolved?.Invoke(x, y, z, m);
        if (Mat.IsPlayer[m] && !Mat.IsSprig(m) && Time - _acidAteSaid > 45)
        {
            _acidAteSaid = Time;
            Say("Кислота проедает нашу постройку. Отремонтируйте её, когда кислота уйдёт.", 3, x, z);
        }
    }

    /// <summary>
    /// A flask breaks here: on whoever it was thrown at and struck (or null), and on those beside, who are burnt the less
    /// the farther; and its acid let out, laid down about where it broke onto whatever stops it under there, to run on
    /// from there. In water it is lost at once.
    /// </summary>
    public void FlaskBreaks(float x, float y, float z, Unit by, Unit on)
    {
        FlasksBroken++;
        FlaskBroke?.Invoke(x, y, z);
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside || u.Airborne != null) continue;
            float dx = u.X - x, dz = u.Z - z, d = MathF.Sqrt(dx * dx + dz * dz);
            if (u != on && (d > SplashReach + u.T.BodyR || y < u.Y - 4 || y > u.Y + u.T.BodyH + 6)) continue;
            float k = u == on ? 1 : Math.Clamp(1 - d / (SplashReach + u.T.BodyR), .15f, 1);
            Hurt(u, by, (by?.T.Damage ?? 14) * k * (Kinds.IsEngine(u.T) ? .6f : 1), "кислота");
            // where it struck: the head and shoulders of the one it broke on, the legs of those beside
            Etch(u, u == on ? .45f : 0, u == on ? 1 : .5f, (int)(4 + 10 * k));
        }
        int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y), iz = (int)MathF.Floor(z);
        if (!World.InBounds(ix, iy, iz)) return;
        if (World.Get(ix, iy, iz) == Mat.Water) { AcidDiluted += FlaskAcid; Dissolved?.Invoke(ix, iy, iz, Mat.Water); return; }
        int laid = 0;
        for (int tries = 0; tries < FlaskAcid * 4 && laid < FlaskAcid; tries++)
        {
            // a splash a little wider than a metre, thickest in the middle
            float a = (float)(Rng.NextDouble() * MathF.Tau), r = (float)Rng.NextDouble() * (float)Rng.NextDouble() * 6;
            int ax = ix + (int)MathF.Round(MathF.Cos(a) * r), az = iz + (int)MathF.Round(MathF.Sin(a) * r);
            if (!World.InBounds(ax, iy, az)) continue;
            // down to whatever stops it there, or onto the acid already lying there
            int ay = Math.Min(World.SY - 2, iy + 2);
            if (Blocks(World.Get(ax, ay, az))) continue;
            ay = Trickle(ax, ay, az);
            while (ay > 2 && !Blocks(World.Get(ax, ay - 1, az)) && World.Get(ax, ay - 1, az) != Mat.Acid) ay--;
            while (ay < World.SY - 2 && World.Get(ax, ay, az) == Mat.Acid) ay++;
            byte here = World.Get(ax, ay, az);
            if (here == Mat.Water) { AcidDiluted++; continue; }
            if (here != Mat.Air && !Mat.IsSprig(here) && !Mat.IsLeafy(here)) continue;
            World.Set(ax, ay, az, Mat.Acid);
            laid++;
        }
        if (Time - _acidSaid > 90)
        {
            _acidSaid = Time;
            Say("Алхимик орды бросает склянки с кислотой. Она разъедает стены и обжигает людей. Убейте алхимика стрелами, вода смывает кислоту.", 3, x, z);
        }

        // what a falling splash stops on: anything solid, a ladder, water; not the air, grass or leaves
        static bool Blocks(byte m) => m != Mat.Air && m != Mat.Acid && (!Mat.Passable[m] || Mat.IsLadder[m] || m == Mat.Water || m == Mat.Pitch || m == Mat.Stakes);
    }

    /// <summary>An alchemist's flask comes down: on the one it was thrown at, if it struck, or where it was thrown.</summary>
    void FlaskLands(Arrow a)
    {
        if (a.Hits && a.Target is { Dead: false } t)
        {
            ShotHits++;
            FlaskBreaks(t.X, t.Y + t.T.BodyH * .7f, t.Z, a.From, t);
            return;
        }
        // thrown at a wall, it breaks on the first of it in its way down the last of its flight
        float x = a.X1, y = a.Y1, z = a.Z1, dx = a.X1 - a.X0, dz = a.Z1 - a.Z0, l = MathF.Max(1, MathF.Sqrt(dx * dx + dz * dz));
        for (int k = 0; k < 24 && World.InBounds((int)x, (int)y, (int)z) && Solid(World.Get((int)x, (int)y, (int)z)); k++)
        {
            x -= dx / l; z -= dz / l; y += .6f;
        }
        FlaskBreaks(x, y, z, a.From, null);

        static bool Solid(byte m) => m != Mat.Air && !Mat.Passable[m];
    }

    /// <summary>
    /// The people in the acid and about it: those standing in it burnt about the feet, the dead lying in it eaten; those
    /// near enough to breathe its fumes coughing and stepping back out of them, the arm down a moment.
    /// </summary>
    void AcidOnPeople(float step)
    {
        // the acid by the cubes of a metre it lies in, for those near it
        _acidCells.Clear();
        foreach (int p in Acids)
        {
            var (x, y, z) = WaterPlanner.Unpack(p);
            int key = (x >> 3) | (z >> 3) << 8 | (y >> 3) << 16;
            var c = _acidCells.GetValueOrDefault(key);
            _acidCells[key] = (c.n + 1, c.x + x, c.y + y, c.z + z);
        }
        foreach (var u in Units)
        {
            if (u.Inside || u.Airborne != null || u.Dead && Time - u.DiedAt > 25) continue;
            int cx = (int)u.X >> 3, cy = (int)u.Y >> 3, cz = (int)u.Z >> 3, near = 0;
            float sx = 0, sz = 0;
            for (int dy = -1; dy <= 1; dy++)
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int kx = cx + dx, ky = cy + dy, kz = cz + dz;
                        if (kx < 0 || kz < 0 || ky < 0 || !_acidCells.TryGetValue(kx | kz << 8 | ky << 16, out var c)) continue;
                        near += c.n; sx += c.x; sz += c.z;
                    }
            if (near == 0) continue;
            // in it: the voxels its feet are in, and those just under them
            int under = 0, r = (int)MathF.Ceiling(u.T.BodyR), fx = (int)u.X, fy = (int)u.Y, fz = (int)u.Z;
            for (int dy = -1; dy <= 1; dy++)
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                        if (World.Get(fx + dx, fy + dy, fz + dz) == Mat.Acid) under++;
            if (u.Dead)
            {
                if (under > 0) Etch(u, 0, 1, 2 + under / 3);
                continue;
            }
            if (under > 0)
            {
                Hurt(u, null, step * (4 + 1.5f * Math.Min(under, 8)) * (Kinds.IsEngine(u.T) ? .5f : 1), "кислота");
                if (u.Dead) continue;
                if (Rng.NextDouble() < step * 3) Etch(u, 0, .35f, 1 + under / 4);
            }
            if (Kinds.IsEngine(u.T) || Sense(u.T) <= 0 || near < 4 || Time - u.CoughAt < 1.6) continue;
            // the fumes: a cough, the hand to the face, and a step back away from where most of it lies
            u.CoughAt = Time;
            u.Clutch = 9; u.ClutchUntil = Time + 1.4;
            u.Cooldown = Math.Max(u.Cooldown, .9f);
            u.Pain = Math.Min(1.6f, u.Pain + .015f * Math.Min(near, 30) * Sense(u.T));
            if (u != Possessed && Time >= u.StaggerUntil)
            {
                float ax = u.X - sx / near, az = u.Z - sz / near, al = MathF.Max(.5f, MathF.Sqrt(ax * ax + az * az));
                u.StaggerUntil = Time + .6;
                u.StagX = ax / al * 9; u.StagZ = az / al * 9;
            }
            Coughed?.Invoke(u);
            if (u.Team == Team.Player && Time - _coughSaid > 60)
            {
                _coughSaid = Time;
                Say($"{u.Name} кашляет от паров кислоты и отходит от неё.", 1, u);
            }
        }
    }

    /// <summary>
    /// Acid eats at a body between these heights (shares of it, from the feet): so many of the cells of its surface there,
    /// picked at random — mail, plate and gear eaten away, cloth burnt through or to rags, skin burnt raw. A shield with
    /// most of it eaten is a shield no longer.
    /// </summary>
    void Etch(Unit u, float lo, float hi, int cells)
    {
        if (Kinds.IsEngine(u.T) || cells <= 0) return;
        var body = u.Wounds ??= new BodyState(Bodies.Plan(u.T, u.Look));
        var plan = body.Plan;
        int shield = plan.Dress.FindIndex(d => d.Kind == DressKind.Shield);
        bool changed = false, shieldHit = false;
        for (int tries = 0; tries < cells * 24 && cells > 0; tries++)
        {
            int part = Rng.Next(BodyPlan.Parts);
            var g = body.Grid[part];
            if (g == null || g.Count == 0) continue;
            int y0 = Math.Max(g.Y0, (int)(lo * u.T.BodyH * BodyPlan.Sub)), y1 = Math.Min(g.Y0 + g.SY - 1, (int)(hi * u.T.BodyH * BodyPlan.Sub));
            if (y1 < y0) continue;
            int x = g.X0 + Rng.Next(g.SX), y = y0 + Rng.Next(y1 - y0 + 1), z = g.Z0 + Rng.Next(g.SZ);
            int i = g.Index(x, y, z);
            byte m = g.M[i];
            if (m == 0) continue;
            var t = (Tissue)g.T[i];
            if (t is not (Tissue.Skin or Tissue.Armor or Tissue.Gear)) continue;
            // on the surface: open to the air on some side
            if (body.Mat(x + 1, y, z, out _) != 0 && body.Mat(x - 1, y, z, out _) != 0 && body.Mat(x, y + 1, z, out _) != 0
                && body.Mat(x, y, z + 1, out _) != 0 && body.Mat(x, y, z - 1, out _) != 0) continue;
            cells--;
            changed = true;
            if (t is Tissue.Armor or Tissue.Gear)
            {
                if (shield >= 0 && plan.Owner[part] is { } own && own[i] == shield + 1) shieldHit = true;
                g.Clear(x, y, z);
            }
            else if (IsSkin(m)) g.M[i] = u.Dead ? Mat.Flesh : Mat.Scar;
            else if (u.Dead || Rng.NextDouble() < .4) g.Clear(x, y, z);
            else g.M[i] = Mat.Rag;
        }
        if (!changed) return;
        body.Rev++;
        Note(body, "ожоги кислотой");
        if (shieldHit && body.Shield && ShieldLeft(body, shield) < .55f)
        {
            body.Shield = false;
            Note(body, "щит разъеден кислотой");
        }

        static bool IsSkin(byte m) => m is Mat.SkinHuman or Mat.SkinGoblin or Mat.SkinOrc or Mat.SkinTroll or Mat.SkinTan or Mat.SkinPale
            or Mat.SkinGoblinDark or Mat.SkinGoblinPale or Mat.SkinOrcGrey or Mat.SkinTrollDark or Mat.Lips or Mat.SkinGiant or Mat.SkinGiantGrey;
    }

    /// <summary>How much of a body's shield is left, as a share of it whole.</summary>
    static float ShieldLeft(BodyState body, int shield)
    {
        int whole = 0, left = 0;
        for (int p = 0; p < BodyPlan.Parts; p++)
        {
            var own = body.Plan.Owner[p]; var g = body.Grid[p];
            if (own == null || g == null) continue;
            for (int i = 0; i < own.Length && i < g.M.Length; i++)
                if (own[i] == shield + 1) { whole++; if (g.M[i] != 0) left++; }
        }
        return whole == 0 ? 1 : left / (float)whole;
    }

    // ------------------------------------------------------------ the save

    void WriteAcid(BinaryWriter w)
    {
        w.Write(Acids.Count); foreach (int p in Acids) w.Write(p);
        w.Write(Etched.Count); foreach (var (p, e) in Etched) { w.Write(p); w.Write(e); }
        w.Write(FlasksBroken); w.Write(AcidAte); w.Write(AcidDiluted);
        // the flasks each alchemist still carries
        var throwers = Units.FindAll(u => !u.Dead && u.Thrown > 0);
        w.Write(throwers.Count); foreach (var u in throwers) { w.Write(u.Id); w.Write(u.Thrown); }
    }

    void ReadAcid(BinaryReader r)
    {
        // the acid in the valley as it was read back, and the pour it may still have had to run
        Acids.Clear(); Etched.Clear();
        for (int i = r.ReadInt32(); i > 0; i--)
        {
            int p = r.ReadInt32();
            var (x, y, z) = WaterPlanner.Unpack(p);
            if (World.Get(x, y, z) != Mat.Acid) continue;
            Acids.Add(p);
            Due(x, y, z);
        }
        for (int i = r.ReadInt32(); i > 0; i--) { int p = r.ReadInt32(); Etched[p] = r.ReadSingle(); }
        FlasksBroken = r.ReadInt32(); AcidAte = r.ReadInt32(); AcidDiluted = r.ReadInt32();
        for (int i = r.ReadInt32(); i > 0; i--)
        {
            int id = r.ReadInt32(), thrown = r.ReadInt32();
            var u = Units.Find(o => o.Id == id);
            if (u != null) u.Thrown = thrown;
        }
    }
}
