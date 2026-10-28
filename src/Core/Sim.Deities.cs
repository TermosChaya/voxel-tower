using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// The gods (Deities.cs): the last thing either side can come to, and the end of any fight it comes to. Called up, the
/// god breaks up out of the earth where it was chained: the ground heaves in a wide ring, those on it are thrown down or
/// up, slabs of the earth and the rock go up and out in bursts as it climbs, the chains that held it come up broken with
/// it, and a pit is left where it stood. Then it walks the valley a few minutes. Each foot comes down where it comes
/// down, crushing what is under it and throwing those about it off their feet, and leaves its print in the ground; a
/// foot goes over a wall as a man steps over a kerb. It sweeps its arm along the ground through the enemy, and what the
/// hand meets — people, engines, walls, trees — goes flying the way the hand went; it brings its fist down, and the
/// ground goes up in a crater and those about it are thrown out from it; it tears up a lump of the land and throws it
/// at those far off; it roars, and the enemy runs. The household's god spares what the household built: the old powers
/// know whose walls these are. Nothing of either side wounds a god but the other god. Its time over, it goes back under
/// the ground; struck down by the other, it falls to pieces of rock.
/// </summary>
public sealed partial class Sim
{
    /// <summary>The gods in the valley, and those gone this second (the view lets them go).</summary>
    public readonly List<Deity> Gods = new();
    int _nextGod = 1;

    /// <summary>A god came up out of the ground.</summary>
    public event Action<Deity> GodRose;
    /// <summary>A god shook the ground — a foot came down, a fist, the earth burst — where, and how hard (1 a step, more a blow).</summary>
    public event Action<Deity, float, float, float, float> GodShook;
    /// <summary>A god began an act (for its sound and the view).</summary>
    public event Action<Deity, DeityAct> GodActs;
    /// <summary>A god left the world.</summary>
    public event Action<Deity> GodGone;

    /// <summary>
    /// What the player in a god wants of it: which way to walk (voxels, x and z of a vector no longer than one), where
    /// it looks, the spot on the ground it looks at, and the blow asked for (a sweep's way: +1 the right hand, −1 the left).
    /// </summary>
    public float GodWishX, GodWishZ, GodLookYaw, GodAimX, GodAimY, GodAimZ;
    public bool GodWishSlam, GodWishLift, GodWishRoar, GodWishLeap;
    public int GodWishSweep;
    /// <summary>The god the player is in, or null.</summary>
    public Deity Ridden { get; private set; }

    public const string ColossusStoneCause = "глыба колосса";
    const string WakeCause = "пробуждение", HordeWakeCause = "призыв орды";

    /// <summary>What the earth a god broke out of is told as, by whose god it was.</summary>
    static string WakeOf(Deity d) => d.Team == Team.Player ? WakeCause : HordeWakeCause;

    /// <summary>How long it takes to come up out of the ground and to go back under, seconds.</summary>
    public const float GodRiseFor = 10, GodSinkFor = 8;
    /// <summary>The pit it breaks out of: how far across from its middle and how deep at the middle (voxels); how far the ground heaves about it.</summary>
    const float PitR = 8 * VoxelWorld.PerMetre, PitDeep = 7 * VoxelWorld.PerMetre, HeaveR = 34 * VoxelWorld.PerMetre;
    /// <summary>When the ground bursts as it climbs out, seconds into the rising; when the chains come up.</summary>
    static readonly float[] GodBursts = { .3f, 2.2f, 4.4f, 6.6f };
    const float ChainsAt = 7.2f;
    /// <summary>The acts' times, seconds: the sweep's wind-up, the stretch the hand goes through, its end; the fist raised,
    /// down, done; the hand down to the land, back up; the throw let go, done; the roar heard, done.</summary>
    public const float SweepFrom = .8f, SweepTo = 1.7f, SweepDone = 2.5f, SlamHit = 1.35f, SlamDone = 2.4f, LiftGrab = 1f, LiftDone = 1.9f,
        ThrowLoose = .85f, ThrowDone = 1.5f, RoarAt = .6f, RoarDone = 2.2f;
    /// <summary>How far round either side of its front a sweep goes, radians.</summary>
    public const float SweepSpread = 1.35f;
    /// <summary>How far the hand reaches about it in a sweep (voxels), and how far a fist's blast throws.</summary>
    const float HandR = 5 * VoxelWorld.PerMetre, BlastR = 26 * VoxelWorld.PerMetre;
    /// <summary>A lump torn up to throw: how far it reaches from its middle (voxels).</summary>
    const float LumpR = 16;

    /// <summary>Whether what struck was the household's god's work: the earth it broke out of, its blows, its lumps.</summary>
    static bool OurGodsDoing(string cause) => cause is WakeCause or ColossusStoneCause || cause == Deities.Colossus.Cause || cause == Deities.Archangel.Cause
        || cause == Deities.Cherub.Cause || cause == Deities.Destroyer.Cause || cause is HailCause or PillarCause;

    /// <summary>The household's priests, who pray the appearance down together.</summary>
    public int Prayers()
    {
        int n = 0;
        foreach (var u in Units) if (!u.Dead && u.Team == Team.Player && u.T.Prays) n++;
        return n;
    }

    /// <summary>The god of the household in the valley, if one is (not a lesser angel a spell sent).</summary>
    public Deity OurGod => Gods.Find(g => !g.Gone && g.Team == Team.Player && !g.T.Minor);

    // ------------------------------------------------------------ waking

    /// <summary>
    /// Calls a god up out of the ground at a spot (voxels): the ring heaves at once, and it climbs out over the next ten
    /// seconds. Null when it comes; otherwise why it cannot.
    /// </summary>
    public string Awaken(DeityKind kind, float x, float z, double lasts = -1) => Awaken(kind, x, z, lasts, out _);

    string Awaken(DeityKind kind, float x, float z, double lasts, out Deity made)
    {
        made = null;
        var t = Deities.Of(kind);
        if (!t.Minor && Gods.Exists(g => !g.Gone && g.T == t)) return $"{t.Name} уже в долине";
        int ix = (int)x, iz = (int)z;
        // (a lesser angel breaks no ground: it may come to the citadel's walls, and near the valley's edge)
        float edge = t.Minor ? 2 * VoxelWorld.PerMetre : PitR;
        if (ix < 8 || iz < 8 || ix >= World.SX - 8 || iz >= World.SZ - 8 || Site.Hex != null && Site.Hex.Edge(x, z) < edge) return "слишком близко к краю долины";
        if (!t.Minor && Site.HasKeep && Dist2(x, z, Site.KeepX, Site.KeepZ) < MathF.Pow(Site.KeepR + PitR + 6 * VoxelWorld.PerMetre, 2)) return "слишком близко к цитадели";
        float g = Ground(ix, iz);
        bool under = t.Arrives != Arrival.Sky;
        var d = new Deity
        {
            Id = _nextGod++, T = t, X = x, Y = under ? g - PitDeep * .6f : g, Z = z, Hp = t.Hp, Born = Time, Until = Time + t.RiseFor + (lasts >= 0 ? lasts : t.Lasts * (t.Team == Team.Player ? Might : 1)),
            Act = DeityAct.Rising, ActFrom = Time, Up = 0,
        };
        // it faces the enemy's side: the horde's god the citadel, ours the way the horde comes
        d.Facing = t.Team == Team.Player ? MathF.PI : (Site.HasKeep ? MathF.Atan2(Site.KeepZ - z, Site.KeepX - x) : 0);
        if (FoeOf(d) is { } foe) d.Facing = MathF.Atan2(foe.Z - z, foe.X - x);
        for (int i = 0; i < 2; i++)
        {
            var (rx, rz) = Right(d.Facing);
            float s = i == 0 ? 1 : -1;
            d.FootX[i] = x + rx * d.T.Stance * .5f * s; d.FootZ[i] = z + rz * d.T.Stance * .5f * s; d.FootY[i] = d.Y;
        }
        if (under) d.Pit = PitOf(x, z, PitR, t.Arrives == Arrival.Bog ? PitDeep * .7f : PitDeep);
        d.HandX = x; d.HandY = d.Y; d.HandZ = z;
        Gods.Add(d);
        made = d;
        if (t.Minor) { GodRose?.Invoke(d); return null; }
        // the ground heaves: those on it are thrown down, and those over the pit up; the light out of heaven on the spot
        // first blinds the horde about it
        if (under) Heave(d, x, z, HeaveR, 1);
        else Heralded(d);
        double stays = d.Until - Time - GodRiseFor;
        Say(t.Kind switch
        {
            DeityKind.Archangel => $"Небеса отверзлись: Архангел сходит в долину на {stays:0} секунд.",
            DeityKind.Toad => "Орда призвала Великую жабу. Земля уходит в трясину, и жаба выходит из болота.",
            _ => $"Колосс пробуждается. Земля расходится, и он выходит на {stays:0} секунд.",
        }, t.Team == Team.Player ? 1 : 3, x, z);
        GodRose?.Invoke(d);
        GodShook?.Invoke(d, x, g, z, under ? 4 : 1);
        return null;
    }

    static float Dist2(float ax, float az, float bx, float bz) => (ax - bx) * (ax - bx) + (az - bz) * (az - bz);

    /// <summary>The god's right hand side: the way to its right, as a unit's facing is the way before it.</summary>
    public static (float x, float z) Right(float facing) => (-MathF.Sin(facing), MathF.Cos(facing));

    /// <summary>
    /// The ground a god stands on at a column: the land as the valley grew it (what is built on it it crushes), lower
    /// where the land was dug away; for the household's god, on top of what the household built, which it does not break.
    /// </summary>
    int Ground(int x, int z) => Ground(x, z, true);

    int Ground(int x, int z, bool spare)
    {
        x = Math.Clamp(x, 0, World.SX - 1); z = Math.Clamp(z, 0, World.SZ - 1);
        int surf = World.SurfAt(x, z), nat = Site.HTop != null ? Site.HTop[z * World.SX + x] : surf;
        int g = Math.Min(surf, nat);
        if (spare)
            for (int y = surf - 1; y > g; y--)
                if (Mat.IsPlayer[World.Get(x, y, z)]) { g = y + 1; break; }
        return Math.Max(1, g);
    }

    /// <summary>The voxels of a bowl in the ground (and whatever stands over it), the topmost first.</summary>
    List<(int x, int y, int z)> PitOf(float cx, float cz, float r, float deep)
    {
        var all = new List<(int x, int y, int z)>();
        int ri = (int)r, x0 = (int)cx, z0 = (int)cz;
        for (int dz = -ri; dz <= ri; dz++)
            for (int dx = -ri; dx <= ri; dx++)
            {
                float k = (dx * dx + dz * dz) / (r * r);
                int x = x0 + dx, z = z0 + dz;
                if (k > 1 || x < 2 || z < 2 || x >= World.SX - 2 || z >= World.SZ - 2) continue;
                int top = World.ColTop(x, z), bottom = Math.Max(4, Ground(x, z, false) - (int)(deep * (1 - k)));
                for (int y = top; y >= bottom; y--)
                    if (Breakable(World.Get(x, y, z))) all.Add((x, y, z));
            }
        all.Sort((a, b) => b.y.CompareTo(a.y));
        return all;
    }

    /// <summary>
    /// The ground heaving under a god come or going: those over the pit thrown up out of it (ours let down unhurt), the
    /// horde about it thrown off its feet and hurt a little, ours knocked down, the nearer the harder.
    /// </summary>
    void Heave(Deity d, float x, float z, float r, float k)
    {
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside || u.Airborne != null) continue;
            float d2 = Dist2(u.X, u.Z, x, z);
            if (d2 > r * r) continue;
            float dist = MathF.Sqrt(d2), near = 1 - dist / r, ox = dist > .5f ? (u.X - x) / dist : 1, oz = dist > .5f ? (u.Z - z) / dist : 0;
            bool foe = Hostile(d, u);
            if (dist < PitR + 2 * VoxelWorld.PerMetre)
            {
                float up = (12 + 6 * near) * VoxelWorld.PerMetre * k, outw = (5 + 4 * near) * VoxelWorld.PerMetre * k;
                if (Fling(u, ox * outw, up, oz * outw, WakeOf(d)) is { } f && !foe) f.Gentle = true;
                if (foe) Hurt(u, null, 60 * k, d.T.Cause);
                continue;
            }
            if (!foe) { if (near > .3f) GoDown(u, 1.5 * near); continue; }
            float lift = MathF.Min(1, 15 / u.T.BodyH);
            Fling(u, ox * 2 * VoxelWorld.PerMetre * near * k, (3 + 4 * near) * VoxelWorld.PerMetre * k * lift, oz * 2 * VoxelWorld.PerMetre * near * k, WakeOf(d));
            Hurt(u, null, 15 * near * k, d.T.Cause);
        }
    }

    /// <summary>Whether a god goes for this one: the other side's people and engines, and the beasts that fight.</summary>
    static bool Hostile(Deity d, Unit u) => d.Team == Team.Player ? u.Team == Team.Horde || u.Team == Team.Wild && u.T.Damage > 0 : u.Team != Team.Horde;

    // ------------------------------------------------------------ each tick

    void TickDeities(float dt)
    {
        TickGodPieces(dt);
        if (Gods.Count == 0) return;
        for (int i = Gods.Count - 1; i >= 0; i--)
        {
            var d = Gods[i];
            if (d.Gone) { Gods.RemoveAt(i); continue; }
            d.Foe = FoeOf(d);
            switch (d.Act)
            {
                case DeityAct.Rising: Rising(d); break;
                case DeityAct.Sinking: Sinking(d); break;
                case DeityAct.Pass: Passing(d, dt); break;
                case DeityAct.Fallen: Gone(d); break;
                default:
                    if (d.Hp <= 0) { FallApart(d); break; }
                    if (Time >= d.Until && d.Act is DeityAct.Standing or DeityAct.Walking && d.Lifted < 0) { BeginSink(d); break; }
                    if (d.Act is DeityAct.Standing or DeityAct.Walking)
                    {
                        if (d.Ridden) Obey(d, dt);
                        else if (d.T.Minor) ThinkAngel(d, dt);
                        else if (d.T.Kind == DeityKind.Archangel) ThinkHeaven(d, dt);
                        else if (d.T.Hops) ThinkSwamp(d, dt);
                        else Think(d, dt);
                    }
                    else Acting(d, dt);
                    Step(d, dt);
                    break;
            }
            PlaceHand(d);
        }
    }

    Deity FoeOf(Deity d) => d.T.Minor ? null
        : Gods.Find(g => g != d && !g.Gone && !g.T.Minor && g.Team != d.Team && g.Act is not (DeityAct.Rising or DeityAct.Sinking or DeityAct.Fallen));

    // ------------------------------------------------------------ rising and going under

    /// <summary>Coming up: the body climbing, the ground bursting about it at its times, the chains coming up after.</summary>
    void Rising(Deity d)
    {
        float t = d.Into(Time), rise = d.T.RiseFor;
        d.Up = Smooth(t / rise);
        if (d.T.Arrives == Arrival.Sky)
        {
            // coming down in the light: as it touches the ground, the ground shakes and the horde is thrown off it (a
            // lesser angel only comes down)
            if (!d.Landed && t >= rise * .92f) { d.Landed = true; if (!d.T.Minor) TouchDown(d); }
            if (t < rise) return;
            d.Up = 1;
            Begin(d, DeityAct.Standing);
            return;
        }
        bool bog = d.T.Arrives == Arrival.Bog;
        while (d.Bursts < GodBursts.Length && t >= GodBursts[d.Bursts])
        {
            int b = d.Bursts++;
            // (out of a bog the mud goes up the lower)
            Burst(d, (b + 1f) / GodBursts.Length, (b == 0 ? 1 : .7f) * (bog ? .7f : 1));
            if (b > 0) Heave(d, d.X, d.Z, HeaveR * .6f, .5f);
            GodShook?.Invoke(d, d.X, d.Y, d.Z, 3);
        }
        if (!bog && t >= ChainsAt && d.Bursts == GodBursts.Length) { d.Bursts++; Chains(d); }
        if (t < rise) return;
        // what is left of the pit goes to dust: it is out (and out of a bog, the pit fills and the ground about goes to mire)
        ClearPit(d);
        if (bog) Bogged(d, d.X, d.Z, PitR * 2.5f, true);
        d.Up = 1;
        Begin(d, DeityAct.Standing);
    }

    /// <summary>
    /// A share of the pit goes up: what lies on top of what is left of it, broken into slabs that fly up and out from
    /// the god's middle, the nearer it the higher; what the sky has no room for goes to dust.
    /// </summary>
    void Burst(Deity d, float upTo, float k)
    {
        var pit = d.Pit;
        if (pit == null) return;
        int end = Math.Min(pit.Count, (int)(pit.Count * upTo));
        var take = new List<(int x, int y, int z)>();
        for (; d.PitAt < end; d.PitAt++)
        {
            var v = pit[d.PitAt];
            if (Breakable(World.Get(v.x, v.y, v.z))) take.Add(v);
        }
        if (take.Count == 0) return;
        int room = MaxAloft - Aloft - 2000;
        foreach (var piece in Split(take, 420))
        {
            if (piece.Count == 0) continue;
            float cx = 0, cz = 0;
            foreach (var (px, _, pz) in piece) { cx += px; cz += pz; }
            cx = cx / piece.Count - d.X; cz = cz / piece.Count - d.Z;
            float od = MathF.Max(1, MathF.Sqrt(cx * cx + cz * cz)), near = 1 - MathF.Min(1, od / PitR);
            float r = (float)Rng.NextDouble();
            // (up high, and out only so far that they come down within the ring that heaves)
            float up = (13 + 11 * near + 4 * r) * VoxelWorld.PerMetre * k, outw = (3 + 4 * (1 - near) + 2 * r) * VoxelWorld.PerMetre * k;
            var f = piece.Count <= room ? Loose(piece, cx / od * outw, up, cz / od * outw, WakeOf(d), room) : null;
            if (f != null) { room -= f.Voxels.Count; d.Broke += f.Voxels.Count; continue; }
            foreach (var (px, py, pz) in piece) Dust(px, py, pz);
            d.Broke += piece.Count;
        }
        InvalidateFields();
    }

    /// <summary>A voxel gone to dust where it stood.</summary>
    void Dust(int x, int y, int z)
    {
        World.Set(x, y, z, Mat.Air);
        if (Orders.At(x, y, z) != 0) Orders.Clear(x, y, z);
        Burning.Remove(WaterPlanner.Pack(x, y, z));
    }

    void ClearPit(Deity d)
    {
        if (d.Pit == null) return;
        for (; d.PitAt < d.Pit.Count; d.PitAt++)
        {
            var (x, y, z) = d.Pit[d.PitAt];
            if (Breakable(World.Get(x, y, z))) { Dust(x, y, z); d.Broke++; }
        }
        d.Pit = null;
        if (d.T.Fiery) LetLava((int)d.X, (int)d.Z, 160);
        InvalidateFields();
    }

    /// <summary>The chains it was held by, come up out of the pit broken: lengths of iron links flung out every way.</summary>
    void Chains(Deity d)
    {
        for (int c = 0; c < 6; c++)
        {
            float a = d.Id * 1.7f + c * MathF.Tau / 6 + (float)Rng.NextDouble() * .5f;
            var links = new List<(sbyte dx, sbyte dy, sbyte dz, byte m)>();
            int n = 14 + c % 3 * 3;
            for (int i = 0; i < n; i++)
            {
                int at = (i - n / 2) * 3;
                // each link a ring of iron, every other one turned a quarter
                for (int s = -1; s <= 1; s++)
                    for (int h = -1; h <= 1; h++)
                    {
                        if (s == 0 && h == 0) continue;
                        links.Add(i % 2 == 0 ? ((sbyte)(at + s), (sbyte)h, (sbyte)0, Mat.Iron) : ((sbyte)(at + s), (sbyte)0, (sbyte)h, Mat.Iron));
                    }
            }
            float s0 = (14 + c % 3 * 4) * VoxelWorld.PerMetre;
            var f = Hurl(links, d.X + MathF.Cos(a) * 3 * VoxelWorld.PerMetre, d.Y + d.T.Height * .3f, d.Z + MathF.Sin(a) * 3 * VoxelWorld.PerMetre,
                MathF.Cos(a) * s0 * .6f, s0, MathF.Sin(a) * s0 * .6f, WakeOf(d));
            if (f != null) { f.AX = -MathF.Sin(a); f.AZ = MathF.Cos(a); f.Spin = 4 + c; }
        }
    }

    void BeginSink(Deity d)
    {
        if (d.Held != null) { d.Held.Bearer = null; d.Held = null; }
        LetCaughtGo(d);
        bool sky = d.T.Arrives == Arrival.Sky;
        d.Pit = sky ? null : PitOf(d.X, d.Z, PitR * .8f, PitDeep * .7f);
        d.PitAt = 0; d.Bursts = 0;
        Begin(d, DeityAct.Sinking);
        string going = sky ? "возносится на небо" : d.T.Arrives == Arrival.Bog ? "уходит в трясину" : "уходит под землю";
        if (!d.T.Minor) Say($"{d.T.Name} {going}. Убито {d.Kills}, разбито {d.Broke / 512f:0} м³.", d.Team == Team.Player ? 1 : 2, d.X, d.Z);
    }

    /// <summary>Going back under: the ground opening about its feet a little at a time, the body going down into it.</summary>
    void Sinking(Deity d)
    {
        float t = d.Into(Time);
        d.Up = 1 - Smooth(t / d.T.SinkFor);
        // up into heaven: nothing breaks
        if (d.T.Arrives == Arrival.Sky) { if (t >= d.T.SinkFor) Gone(d); return; }
        int b = Math.Min(3, (int)(t / GodSinkFor * 4));
        while (d.Bursts <= b && d.Bursts < 4) { d.Bursts++; Burst(d, d.Bursts / 4f, .45f); GodShook?.Invoke(d, d.X, d.Y, d.Z, 1.5f); }
        if (t >= GodSinkFor) { ClearPit(d); Gone(d); }
    }

    /// <summary>Struck down by the other god: it comes apart into great pieces of rock that fall where it stood.</summary>
    void FallApart(Deity d)
    {
        if (d.Held != null) { d.Held.Bearer = null; d.Held = null; }
        LetCaughtGo(d);
        // the archangel struck down goes up into heaven, the toad down into its bog; the colossus falls to pieces
        if (d.T.Kind != DeityKind.Colossus)
        {
            Say(d.T.Kind == DeityKind.Archangel ? "Архангел повержен и возносится." : "Великая жаба повержена и уходит в трясину.", d.Team == Team.Player ? 3 : 1, d.X, d.Z);
            BeginSink(d);
            return;
        }
        const byte rock = Mat.Rock;
        for (int k = 0; k < 12; k++)
        {
            float h = (.15f + k / 12f * .8f) * d.T.Height, a = k * 2.39996f;
            float r = 9 + k % 3 * 2, off = (2 + k % 4) * VoxelWorld.PerMetre;
            var ball = Ball(r, rock);
            Hurl(ball, d.X + MathF.Cos(a) * off, d.Y + h * d.Up, d.Z + MathF.Sin(a) * off, MathF.Cos(a) * 4 * VoxelWorld.PerMetre, 2 * VoxelWorld.PerMetre, MathF.Sin(a) * 4 * VoxelWorld.PerMetre,
                ColossusStoneCause);
        }
        Say($"{d.T.Name} повержен и рассыпался камнем.", d.Team == Team.Player ? 3 : 1, d.X, d.Z);
        Begin(d, DeityAct.Fallen);
        GodShook?.Invoke(d, d.X, d.Y, d.Z, 4);
    }

    void Gone(Deity d)
    {
        d.Gone = true;
        if (d.Ridden) Unride();
        GodGone?.Invoke(d);
    }

    void Begin(Deity d, DeityAct act)
    {
        d.Act = act; d.ActFrom = Time; d.Landed = false; d.FoeStruck = false;
        GodActs?.Invoke(d, act);
    }

    // ------------------------------------------------------------ walking

    /// <summary>
    /// Its feet: the one in the air goes on along its arc and comes down; with none in the air and somewhere to go, the
    /// one behind is lifted toward a spot ahead on its own side. The body is always between its feet.
    /// </summary>
    void Step(Deity d, float dt)
    {
        // a leap carries it bodily (Acting), a toad goes only by hops, and the archangel glides over the ground on its
        // wings: their feet are where their body is
        if (d.Act == DeityAct.Leap || d.T.Hops) { FeetUnder(d, d.Act != DeityAct.Leap); return; }
        if (d.T.Winged) { Glide(d, dt); return; }
        if (d.Lifted >= 0)
        {
            int i = d.Lifted;
            float p = (float)((Time - d.StepFrom) / d.StepFor), s = Smooth(p);
            d.FootX[i] = Lerp(d.FromX, d.ToX, s);
            d.FootZ[i] = Lerp(d.FromZ, d.ToZ, s);
            d.FootY[i] = Lerp(d.FromY, d.ToY, s) + d.Lift * MathF.Sin(MathF.PI * Math.Clamp(p, 0, 1));
            if (p >= 1) { d.FootY[i] = d.ToY; d.Lifted = -1; FootDown(d, i); }
        }
        else if (d.Act == DeityAct.Walking && !float.IsNaN(d.GoX) && Dist2(d.GoX, d.GoZ, d.X, d.Z) > 1)
        {
            float gx = d.GoX - d.X, gz = d.GoZ - d.Z, gl = MathF.Sqrt(gx * gx + gz * gz), fx = gx / gl, fz = gz / gl;
            // the one further behind goes
            float b0 = (d.FootX[0] - d.X) * fx + (d.FootZ[0] - d.Z) * fz, b1 = (d.FootX[1] - d.X) * fx + (d.FootZ[1] - d.Z) * fz;
            int i = b0 <= b1 ? 0 : 1;
            var (rx, rz) = Right(d.Facing);
            float side = i == 0 ? 1 : -1, ahead = d.T.Stride * .5f;
            float rest = MathF.Sqrt(Dist2(d.GoX, d.GoZ, d.X, d.Z));
            ahead = MathF.Min(ahead, rest + d.T.Stride * .25f);
            float tx = d.X + fx * ahead + rx * d.T.Stance * .5f * side, tz = d.Z + fz * ahead + rz * d.T.Stance * .5f * side;
            if (!d.Ridden) (tx, tz) = Footing(d, tx, tz);
            d.Lifted = i;
            d.FromX = d.FootX[i]; d.FromY = d.FootY[i]; d.FromZ = d.FootZ[i];
            d.ToX = tx; d.ToZ = tz; d.ToY = Ground((int)tx, (int)tz, d.Team == Team.Player);
            float far = MathF.Sqrt(Dist2(tx, tz, d.FromX, d.FromZ));
            d.StepFor = Math.Clamp(far / (2 * d.T.Speed) * 2, .9f, 2.2f);
            d.StepFrom = Time;
            // high enough to go over what stands between
            d.Lift = MathF.Max(3 * VoxelWorld.PerMetre, Highest(d.FromX, d.FromZ, tx, tz) - MathF.Max(d.FromY, d.ToY) + 2 * VoxelWorld.PerMetre);
            d.Lift = MathF.Min(d.Lift, 16 * VoxelWorld.PerMetre);
        }
        // the body between its feet, a little forward of them as it walks
        float mx = (d.FootX[0] + d.FootX[1]) * .5f, mz = (d.FootZ[0] + d.FootZ[1]) * .5f;
        float k = MathF.Min(1, dt * 3);
        d.X += (mx - d.X) * k; d.Z += (mz - d.Z) * k;
        // (the foot in the air counts where it is going along the ground, not how high it is lifted)
        float s0 = d.Lifted >= 0 ? Smooth((float)((Time - d.StepFrom) / d.StepFor)) : 0;
        float y0 = d.Lifted == 0 ? Lerp(d.FromY, d.ToY, s0) : d.FootY[0], y1 = d.Lifted == 1 ? Lerp(d.FromY, d.ToY, s0) : d.FootY[1];
        d.Y += ((y0 + y1) * .5f - d.Y) * k;
    }

    /// <summary>
    /// Where a foot comes down near a spot: the household's god sets it down off ours and off what the household
    /// built if it can, within a few metres; the horde's god where it likes.
    /// </summary>
    (float x, float z) Footing(Deity d, float x, float z)
    {
        if (d.Team != Team.Player || Clear(d, x, z)) return (x, z);
        for (int ring = 1; ring <= 3; ring++)
            for (int a = 0; a < 8; a++)
            {
                float ang = a * MathF.Tau / 8 + ring, r = ring * 1.5f * VoxelWorld.PerMetre;
                float nx = x + MathF.Cos(ang) * r, nz = z + MathF.Sin(ang) * r;
                if (Clear(d, nx, nz)) return (nx, nz);
            }
        return (x, z);
    }

    /// <summary>Whether a foot of ours can come down here without coming down on ours or on what the household built.</summary>
    bool Clear(Deity d, float x, float z)
    {
        float r = d.T.Foot + 1.5f * VoxelWorld.PerMetre;
        foreach (var u in Units)
            if (!u.Dead && !u.Inside && u.Team == Team.Player && Dist2(u.X, u.Z, x, z) < r * r) return false;
        int ix = (int)x, iz = (int)z;
        if (ix < 2 || iz < 2 || ix >= World.SX - 2 || iz >= World.SZ - 2) return false;
        return Ground(ix, iz, true) <= Ground(ix, iz, false) + 1;
    }

    /// <summary>The highest thing standing along a line on the ground (voxels): what a foot must be lifted over.</summary>
    float Highest(float ax, float az, float bx, float bz)
    {
        float hi = 0, len = MathF.Sqrt(Dist2(ax, az, bx, bz));
        int n = Math.Max(1, (int)(len / 4));
        for (int i = 0; i <= n; i++)
        {
            int x = (int)Lerp(ax, bx, i / (float)n), z = (int)Lerp(az, bz, i / (float)n);
            if (x < 0 || z < 0 || x >= World.SX || z >= World.SZ) continue;
            hi = MathF.Max(hi, World.ColTop(x, z));
        }
        return hi;
    }

    /// <summary>
    /// A foot comes down: whoever is under it is crushed, what was built there breaks and bursts out from under it, its
    /// print is pressed into the ground, and those about it are thrown off their feet.
    /// </summary>
    void FootDown(Deity d, int i)
    {
        float x = d.FootX[i], z = d.FootZ[i], y = d.FootY[i], r = d.T.Foot;
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside || u.Airborne != null) continue;
            float d2 = Dist2(u.X, u.Z, x, z);
            if (d2 < (r + u.T.BodyR) * (r + u.T.BodyR) && u.Y < y + 3 * VoxelWorld.PerMetre)
            {
                // ours under the household's god's foot are swept aside by it unhurt; anyone else is crushed
                if (d.Team == Team.Player && !Hostile(d, u))
                {
                    float ax = u.X - x, az = u.Z - z, al = MathF.Max(.5f, MathF.Sqrt(ax * ax + az * az));
                    if (Fling(u, ax / al * 5 * VoxelWorld.PerMetre, 3 * VoxelWorld.PerMetre, az / al * 5 * VoxelWorld.PerMetre, d.T.Cause) is { } f) f.Gentle = true;
                }
                else Strike(d, u, 99999);
                continue;
            }
            float near = 1 - MathF.Sqrt(d2) / (9 * VoxelWorld.PerMetre);
            if (near <= 0) continue;
            if (!Hostile(d, u)) { if (near > .4f) GoDown(u, 1); continue; }
            float lift = MathF.Min(1, 15 / u.T.BodyH);
            Fling(u, 0, (2 + 4 * near) * VoxelWorld.PerMetre * lift, 0, d.T.Cause);
        }
        // what stands under it bursts out from under it
        var under = Above(d, x, z, x, z, r, (int)(y + 5 * VoxelWorld.PerMetre), d.Team == Team.Player);
        Scatter(d, under, x, y, z, 0, 0, 3 * VoxelWorld.PerMetre, 5 * VoxelWorld.PerMetre, 140);
        // its print: the turf and the earth pressed down a little
        int ri = (int)r, ix = (int)x, iz = (int)z, pressed = 0;
        for (int dz = -ri; dz <= ri; dz++)
            for (int dx = -ri; dx <= ri; dx++)
            {
                int vx = ix + dx, vz = iz + dz;
                if (dx * dx + dz * dz > r * r || vx < 2 || vz < 2 || vx >= World.SX - 2 || vz >= World.SZ - 2) continue;
                int top = World.SurfAt(vx, vz) - 1;
                for (int k = 0; k < 2 && top - k > 4; k++)
                {
                    byte m = World.Get(vx, top - k, vz);
                    if (Mat.Def[m].Group != MatGroup.Earth && !Mat.IsVeg[m] && m != Mat.Tuft) break;
                    if (d.Team == Team.Player && Mat.IsPlayer[m]) break;
                    Dust(vx, top - k, vz); pressed++;
                }
            }
        if (pressed > 0 || under.Count > 0) InvalidateFields();
        GodShook?.Invoke(d, x, y, z, 1);
    }

    /// <summary>Sets alight a few things that burn about a spot.</summary>
    void Kindle(float x, float y, float z, float r, int most)
    {
        int lit = 0, ri = (int)r;
        for (int i = 0; i < most * 6 && lit < most; i++)
        {
            int vx = (int)x + Rng.Next(-ri, ri + 1), vz = (int)z + Rng.Next(-ri, ri + 1);
            if (vx < 2 || vz < 2 || vx >= World.SX - 2 || vz >= World.SZ - 2) continue;
            int st = World.SurfAt(vx, vz);
            for (int vy = st - 1; vy >= st - 3; vy--)
                if (Mat.Burns(World.Get(vx, vy, vz)) && Ignite(vx, vy, vz)) { lit++; break; }
        }
    }

    // ------------------------------------------------------------ what it does of itself

    /// <summary>
    /// A god left to itself: it fights the other god if one is up; else it picks out the thickest knot of the enemy and
    /// sweeps it if it is in reach before it, brings the fist down on it a little off, tears up a lump to throw at it if
    /// it is far and nothing is near, or walks at it; with no enemy in the valley it stands, and now and then roars.
    /// The household's god will not strike where more of ours stand than of the enemy.
    /// </summary>
    void Think(Deity d, float dt)
    {
        if (d.Act == DeityAct.Walking && !float.IsNaN(d.GoX))
        {
            Turn(d, MathF.Atan2(d.GoZ - d.Z, d.GoX - d.X), dt);
            if (Dist2(d.GoX, d.GoZ, d.X, d.Z) < MathF.Pow(d.T.Stride * .35f, 2)) { d.GoX = float.NaN; Begin(d, DeityAct.Standing); }
        }
        if (Time < d.NextThink) return;
        d.NextThink = Time + .4;
        if (d.Foe is { } foe)
        {
            // the other god: at the length of its arm, never so near that the hand goes round behind it
            float fd = MathF.Sqrt(Dist2(foe.X, foe.Z, d.X, d.Z)), fa = Off(d, foe.X, foe.Z), ux = (foe.X - d.X) / MathF.Max(1, fd), uz = (foe.Z - d.Z) / MathF.Max(1, fd);
            if (fd < d.T.Reach * .4f) { GoTo(d, foe.X - ux * d.T.Reach * .7f, foe.Z - uz * d.T.Reach * .7f); return; }
            if (fd < d.T.Reach * 1.1f && MathF.Abs(fa) < SweepSpread * .8f && Rng.NextDouble() < .6) { BeginSweep(d, fa > 0 ? -1 : 1); return; }
            if (fd < d.T.SlamFar && fd > d.T.SlamNear * .7f && MathF.Abs(fa) < .5f) { BeginSlam(d, foe.X, foe.Z); return; }
            GoTo(d, foe.X - ux * d.T.Reach * .7f, foe.Z - uz * d.T.Reach * .7f);
            return;
        }
        var (cx, cz, score, ours) = Thickest(d);
        if (score <= 0)
        {
            if (d.Act == DeityAct.Walking) return;
            if (Time - d.ActFrom > 18 && Rng.NextDouble() < .1) BeginRoar(d);
            return;
        }
        float dist = MathF.Sqrt(Dist2(cx, cz, d.X, d.Z)), off = Off(d, cx, cz);
        bool spare = d.Team == Team.Player;
        if (dist < d.T.Reach + HandR * .6f && dist > d.T.Reach * .45f && MathF.Abs(off) < SweepSpread && score >= 2 && (!spare || OursInSweep(d, off > 0 ? -1 : 1) * 2 <= score))
        {
            BeginSweep(d, off > 0 ? -1 : 1);
            return;
        }
        if (dist >= d.T.SlamNear && dist <= d.T.SlamFar && MathF.Abs(off) < .55f && (!spare || ours == 0)) { BeginSlam(d, cx, cz); return; }
        if (dist > 45 * VoxelWorld.PerMetre && score >= 3 && d.Held == null && Nearest(d, 30 * VoxelWorld.PerMetre) == null && MathF.Abs(off) < .8f)
        {
            BeginLift(d, cx, cz);
            return;
        }
        // too near for its blows: it steps back from it; else on at it, to stop with it at the sweep's reach
        if (dist < d.T.SlamNear * .8f)
        {
            float bx = d.X - (cx - d.X) / MathF.Max(1, dist) * d.T.Stride, bz = d.Z - (cz - d.Z) / MathF.Max(1, dist) * d.T.Stride;
            GoTo(d, bx, bz);
            return;
        }
        float stop = d.T.Reach * .75f, gx = cx + (d.X - cx) / MathF.Max(1, dist) * stop, gz = cz + (d.Z - cz) / MathF.Max(1, dist) * stop;
        GoTo(d, gx, gz);
    }

    void GoTo(Deity d, float x, float z)
    {
        x = Math.Clamp(x, 12, World.SX - 13); z = Math.Clamp(z, 12, World.SZ - 13);
        if (Site.Hex != null && Site.Hex.Edge(x, z) < 6 * VoxelWorld.PerMetre)
        {
            // not over the valley's rim: toward its middle till it is inside
            float mx = World.SX * .5f, mz = World.SZ * .5f;
            for (int i = 0; i < 20 && Site.Hex.Edge(x, z) < 6 * VoxelWorld.PerMetre; i++) { x += (mx - x) * .1f; z += (mz - z) * .1f; }
        }
        d.GoX = x; d.GoZ = z;
        if (d.Act != DeityAct.Walking) Begin(d, DeityAct.Walking);
    }

    /// <summary>Turns it toward a heading, as fast as it turns.</summary>
    static void Turn(Deity d, float heading, float dt)
    {
        float diff = MathF.IEEERemainder(heading - d.Facing, MathF.Tau), most = d.T.Turn * dt;
        d.Facing += Math.Clamp(diff, -most, most);
    }

    /// <summary>How far round from its front a spot is, radians: positive to its left.</summary>
    static float Off(Deity d, float x, float z) => MathF.IEEERemainder(MathF.Atan2(z - d.Z, x - d.X) - d.Facing, MathF.Tau);

    /// <summary>
    /// The thickest knot of its enemy in the valley: the middle of the one with most about it within nine metres
    /// (an engine counting as three), how many, and how many of ours stand within eight metres of that middle.
    /// </summary>
    (float x, float z, int score, int ours) Thickest(Deity d)
    {
        var foes = new List<Unit>();
        foreach (var u in Units)
            if (!u.Dead && !u.Inside && u.Airborne == null && Hostile(d, u) && !u.Envoy && (Site.Hex == null || Site.Hex.Edge(u.X, u.Z) > 2 * VoxelWorld.PerMetre)) foes.Add(u);
        if (foes.Count == 0) return (0, 0, 0, 0);
        const float Knot = 9 * VoxelWorld.PerMetre;
        float bx = 0, bz = 0; int best = 0; float bestD = float.MaxValue;
        int stride = Math.Max(1, foes.Count / 120);
        for (int a = 0; a < foes.Count; a += stride)
        {
            var u = foes[a];
            int n = 0; float sx = 0, sz = 0;
            foreach (var o in foes)
                if (Dist2(o.X, o.Z, u.X, u.Z) < Knot * Knot) { int w = Kinds.IsEngine(o.T) ? 3 : 1; n += w; sx += o.X * w; sz += o.Z * w; }
            float dd = Dist2(u.X, u.Z, d.X, d.Z);
            if (n > best || n == best && dd < bestD) { best = n; bestD = dd; bx = sx / n; bz = sz / n; }
        }
        int ours = 0;
        foreach (var u in Units)
            if (!u.Dead && !u.Inside && u.Team == Team.Player && Dist2(u.X, u.Z, bx, bz) < 64 * VoxelWorld.PerMetre * VoxelWorld.PerMetre) ours++;
        return (bx, bz, best, ours);
    }

    /// <summary>The nearest of its enemy within a reach, or null.</summary>
    Unit Nearest(Deity d, float reach)
    {
        Unit best = null; float bd = reach * reach;
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside || !Hostile(d, u)) continue;
            float d2 = Dist2(u.X, u.Z, d.X, d.Z);
            if (d2 < bd) { bd = d2; best = u; }
        }
        return best;
    }

    /// <summary>Ours a sweep that way would go through: within the band the hand goes along, before it.</summary>
    int OursInSweep(Deity d, int side)
    {
        int n = 0;
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside || u.Team != Team.Player) continue;
            float dist = MathF.Sqrt(Dist2(u.X, u.Z, d.X, d.Z));
            if (MathF.Abs(dist - d.T.Reach) < HandR + 2 * VoxelWorld.PerMetre && MathF.Abs(Off(d, u.X, u.Z)) < SweepSpread) n++;
        }
        return n;
    }

    // ------------------------------------------------------------ what the player in it does

    /// <summary>The god the player is in, by what the player wants: it turns to the look, walks the way asked, strikes as asked.</summary>
    void Obey(Deity d, float dt)
    {
        Turn(d, GodLookYaw, dt * 1.6f);
        if (GodWishLeap && CanFly(d))
        {
            GodWishLeap = false;
            BeginLeap(d, GodAimX, GodAimZ);
            return;
        }
        GodWishLeap = false;
        if (GodWishSweep != 0) { BeginSweep(d, GodWishSweep); GodWishSweep = 0; return; }
        if (GodWishSlam)
        {
            GodWishSlam = false;
            float dist = MathF.Sqrt(Dist2(GodAimX, GodAimZ, d.X, d.Z));
            // a fist comes down where it is looked at, as near and as far as the arm goes
            float k = Math.Clamp(dist, d.T.SlamNear, d.T.SlamFar) / MathF.Max(1, dist);
            BeginSlam(d, d.X + (GodAimX - d.X) * k, d.Z + (GodAimZ - d.Z) * k);
            return;
        }
        if (GodWishLift)
        {
            GodWishLift = false;
            // the archangel's wings beat at what it looks at; the colossus tears up a lump, and throws it
            if (d.T.Winged) { BeginGust(d); return; }
            if (d.Held != null) { BeginThrow(d, GodAimX, GodAimY, GodAimZ); return; }
            BeginLift(d, GodAimX, GodAimZ);
            return;
        }
        if (GodWishRoar) { GodWishRoar = false; BeginRoar(d); return; }
        float wl = MathF.Sqrt(GodWishX * GodWishX + GodWishZ * GodWishZ);
        if (wl > .1f)
        {
            float ahead = d.T.Stride;
            d.GoX = d.X + GodWishX / wl * ahead; d.GoZ = d.Z + GodWishZ / wl * ahead;
            if (d.Act != DeityAct.Walking) Begin(d, DeityAct.Walking);
        }
        else if (d.Act == DeityAct.Walking) { d.GoX = float.NaN; Begin(d, DeityAct.Standing); }
    }

    /// <summary>Goes into the household's god: its own wits set aside while the player is in it.</summary>
    public bool Ride(Deity d)
    {
        if (d == null || d.Gone || d.Team != Team.Player || d.Act is DeityAct.Sinking or DeityAct.Fallen) return false;
        if (Possessed != null) Unpossess(null);
        if (Ridden != null) Ridden.Ridden = false;
        Ridden = d; d.Ridden = true;
        GodLookYaw = d.Facing;
        GodWishX = GodWishZ = 0; GodWishSweep = 0; GodWishSlam = GodWishLift = GodWishRoar = GodWishLeap = false;
        return true;
    }

    /// <summary>Leaves the god: it minds itself again.</summary>
    public void Unride()
    {
        if (Ridden != null) Ridden.Ridden = false;
        Ridden = null;
        GodWishX = GodWishZ = 0; GodWishSweep = 0; GodWishSlam = GodWishLift = GodWishRoar = GodWishLeap = false;
    }

    // ------------------------------------------------------------ the blows

    void BeginSweep(Deity d, int side)
    {
        if (d.Held != null) side = -1;
        d.Side = side;
        d.GoX = float.NaN;
        Begin(d, DeityAct.Sweep);
    }

    void BeginSlam(Deity d, float x, float z)
    {
        d.AimX = x; d.AimZ = z; d.AimY = Ground((int)x, (int)z, false);
        d.Facing = MathF.Atan2(z - d.Z, x - d.X) * .3f + d.Facing * .7f;
        d.GoX = float.NaN;
        Begin(d, DeityAct.Slam);
    }

    void BeginLift(Deity d, float tx, float tz)
    {
        // the lump is torn up a dozen metres before it, toward what it is for
        float a = MathF.Atan2(tz - d.Z, tx - d.X);
        float far = MathF.Min(MathF.Sqrt(Dist2(tx, tz, d.X, d.Z)), 13 * VoxelWorld.PerMetre);
        d.AimX = d.X + MathF.Cos(a) * far; d.AimZ = d.Z + MathF.Sin(a) * far; d.AimY = Ground((int)d.AimX, (int)d.AimZ, false);
        d.ThrowX = tx; d.ThrowZ = tz;
        d.GoX = float.NaN;
        Begin(d, DeityAct.Lift);
    }

    void BeginThrow(Deity d, float x, float y, float z)
    {
        d.AimX = x; d.AimY = y; d.AimZ = z;
        d.GoX = float.NaN;
        Begin(d, DeityAct.Throw);
    }

    void BeginRoar(Deity d) { d.GoX = float.NaN; d.LastRoar = Time; Begin(d, DeityAct.Roar); }

    /// <summary>An act going on: the blow lands at its moment, and the act ends at its end.</summary>
    void Acting(Deity d, float dt)
    {
        float t = d.Into(Time);
        switch (d.Act)
        {
            case DeityAct.Sweep:
                if (t >= SweepFrom && t <= SweepTo + .1f) Sweeping(d);
                if (t >= SweepDone) Begin(d, DeityAct.Standing);
                break;
            case DeityAct.Slam:
                if (!d.Landed && t >= SlamHit) { d.Landed = true; if (d.T.Winged) Pillar(d); else Slammed(d); }
                if (t >= SlamDone) Begin(d, DeityAct.Standing);
                break;
            case DeityAct.Lift:
                if (!d.Landed && t >= LiftGrab) { d.Landed = true; Grab(d); }
                if (t >= LiftDone)
                {
                    // left to itself, it throws at once at what it tore the lump up for
                    if (d.Held != null && !d.Ridden) { BeginThrow(d, d.ThrowX, Ground((int)d.ThrowX, (int)d.ThrowZ, false), d.ThrowZ); break; }
                    Begin(d, DeityAct.Standing);
                }
                break;
            case DeityAct.Throw:
                Turn(d, MathF.Atan2(d.AimZ - d.Z, d.AimX - d.X), dt * 2);
                if (!d.Landed && t >= ThrowLoose) { d.Landed = true; Throw(d); }
                if (t >= ThrowDone) Begin(d, DeityAct.Standing);
                break;
            case DeityAct.Roar:
                if (!d.Landed && t >= RoarAt) { d.Landed = true; if (d.T.Winged) Trumpet(d); else if (d.T.Hops) Croak(d); else Roared(d); }
                if (t >= RoarDone) Begin(d, DeityAct.Standing);
                break;
            case DeityAct.Gust:
                if (!d.Landed && t >= GustAt) { d.Landed = true; Gusted(d); }
                if (t >= GustDone) Begin(d, DeityAct.Standing);
                break;
            case DeityAct.Leap: Leaping(d, t); break;
            case DeityAct.Tongue: Tonguing(d, t); break;
            case DeityAct.Spit:
                Turn(d, MathF.Atan2(d.AimZ - d.Z, d.AimX - d.X), dt * 2);
                if (!d.Landed && t >= SpitAt) { d.Landed = true; Spat(d); }
                if (t >= SpitDone) Begin(d, DeityAct.Standing);
                break;
        }
    }

    /// <summary>
    /// Where the striking hand is, by the act and how far through it: hanging at its side; in a sweep, raised out on
    /// the side it starts from, then along the ground round before it to the other side; over the spot and down on it
    /// for the fist; down to the land and up to the shoulder with a lump; back over the head and out for a throw.
    /// </summary>
    void PlaceHand(Deity d)
    {
        float t = d.Into(Time), h = d.T.Height * d.Up, fx = MathF.Cos(d.Facing), fz = MathF.Sin(d.Facing);
        var (rx, rz) = Right(d.Facing);
        float hx = d.X + rx * d.T.Stance * .9f, hy = d.Y + h * .42f, hz = d.Z + rz * d.T.Stance * .9f;
        switch (d.Act)
        {
            case DeityAct.Sweep:
            {
                float p = Math.Clamp((t - SweepFrom) / (SweepTo - SweepFrom), 0, 1);
                float a = d.Facing + d.Side * SweepSpread * (1 - 2 * p);
                // raised out at the side it starts from, then low along the ground
                float raise = t < SweepFrom ? Smooth(t / SweepFrom) : 1 - Smooth((t - SweepTo) / (SweepDone - SweepTo));
                float r = d.T.Reach * (t < SweepFrom ? .6f + .4f * raise : 1);
                hx = d.X + MathF.Cos(a) * r; hz = d.Z + MathF.Sin(a) * r;
                float g = Ground((int)hx, (int)hz, false);
                hy = t < SweepFrom ? Lerp(d.Y + h * .42f, g + h * .45f, raise) : t <= SweepTo ? g + 2.5f * VoxelWorld.PerMetre : Lerp(d.Y + h * .42f, g + 2.5f * VoxelWorld.PerMetre, raise);
                break;
            }
            case DeityAct.Slam:
            {
                float up = t < 1.1f ? Smooth(t / 1.1f) : t < SlamHit ? 1 - Smooth((t - 1.1f) / (SlamHit - 1.1f)) : 0;
                float back = t < SlamHit ? 0 : Smooth((t - SlamHit) / (SlamDone - SlamHit));
                float sx = Lerp(d.X + fx * d.T.SlamNear * .5f, d.AimX, t < 1.1f ? up : 1), sz = Lerp(d.Z + fz * d.T.SlamNear * .5f, d.AimZ, t < 1.1f ? up : 1);
                hx = Lerp(sx, d.X + rx * d.T.Stance, back); hz = Lerp(sz, d.Z + rz * d.T.Stance, back);
                hy = Lerp(d.AimY + 1.5f * VoxelWorld.PerMetre + up * h * .55f, d.Y + h * .42f, back);
                break;
            }
            case DeityAct.Lift:
            {
                float down = t < LiftGrab ? Smooth(t / LiftGrab) : 1 - Smooth((t - LiftGrab) / (LiftDone - LiftGrab));
                hx = Lerp(d.X + rx * d.T.Stance * .6f + fx * 4 * VoxelWorld.PerMetre, d.AimX, down);
                hz = Lerp(d.Z + rz * d.T.Stance * .6f + fz * 4 * VoxelWorld.PerMetre, d.AimZ, down);
                hy = Lerp(d.Y + h * .7f, d.AimY + LumpR, down);
                break;
            }
            case DeityAct.Throw:
            {
                float back = t < ThrowLoose ? Smooth(t / ThrowLoose) : 0, on = t < ThrowLoose ? 0 : Smooth((t - ThrowLoose) / (ThrowDone - ThrowLoose));
                hx = d.X + rx * d.T.Stance * .6f - fx * 6 * VoxelWorld.PerMetre * back + fx * 10 * VoxelWorld.PerMetre * on;
                hz = d.Z + rz * d.T.Stance * .6f - fz * 6 * VoxelWorld.PerMetre * back + fz * 10 * VoxelWorld.PerMetre * on;
                hy = d.Y + h * (.7f + .25f * back - .25f * on);
                break;
            }
            case DeityAct.Tongue:
            {
                // the tongue's tip: out of the mouth to the spot, and back with what it caught
                var (mx, my, mz) = Mouth(d);
                float o = TongueOut(t);
                hx = Lerp(mx, d.AimX, o); hy = Lerp(my, d.AimY + 1.5f * VoxelWorld.PerMetre, o); hz = Lerp(mz, d.AimZ, o);
                break;
            }
            default:
                if (d.T.Hops) { (hx, hy, hz) = Mouth(d); break; }
                if (d.Held != null) { hx = d.X + rx * d.T.Stance * .6f + fx * 4 * VoxelWorld.PerMetre; hz = d.Z + rz * d.T.Stance * .6f + fz * 4 * VoxelWorld.PerMetre; hy = d.Y + h * .7f; }
                break;
        }
        d.HandX = hx; d.HandY = hy; d.HandZ = hz;
    }

    /// <summary>
    /// The hand going along the ground: whoever of the enemy it meets on its way since the tick before goes flying the
    /// way it goes, hard, and up; what stands in its way breaks and goes with it; the other god, met, is struck.
    /// </summary>
    void Sweeping(Deity d)
    {
        float ax = d.LastHandX, az = d.LastHandZ;
        if (!d.Landed) { d.Landed = true; ax = d.HandX; az = d.HandZ; }
        float bx = d.HandX, bz = d.HandZ, by = d.HandY;
        d.LastHandX = bx; d.LastHandZ = bz;
        float mx = bx - ax, mz = bz - az, ml = MathF.Sqrt(mx * mx + mz * mz);
        if (ml < .5f) return;
        float tx = mx / ml, tz = mz / ml;
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside || u.Airborne != null) continue;
            if (SegDist2(u.X, u.Z, ax, az, bx, bz) > MathF.Pow(HandR + u.T.BodyR, 2) || u.Y > by + HandR) continue;
            if (d.Team == Team.Player && !Hostile(d, u))
            {
                // ours the hand passes over: knocked flat, not thrown
                GoDown(u, 1.5);
                continue;
            }
            float lift = MathF.Min(1, 15 / u.T.BodyH), cx = u.X - d.X, cz = u.Z - d.Z, cl = MathF.Max(1, MathF.Sqrt(cx * cx + cz * cz));
            // (a lesser angel's sword throws them less far)
            float on = (22 + 8 * (float)Rng.NextDouble()) * VoxelWorld.PerMetre * lift * (d.T.Minor ? .55f : 1), outw = 7 * VoxelWorld.PerMetre * lift, up = (8 + 5 * (float)Rng.NextDouble()) * VoxelWorld.PerMetre * MathF.Sqrt(lift);
            Strike(d, u, Kinds.IsEngine(u.T) ? 500 : 120);
            // (the dead go flying too)
            Fling(u, tx * on + cx / cl * outw, up, tz * on + cz / cl * outw, d.T.Cause);
        }
        // what stands in its way
        var hit = Above(d, ax, az, bx, bz, HandR, (int)(by + HandR), d.Team == Team.Player);
        Scatter(d, hit, bx, by, bz, tx * 20 * VoxelWorld.PerMetre, tz * 20 * VoxelWorld.PerMetre, 8 * VoxelWorld.PerMetre, 4 * VoxelWorld.PerMetre, 220);
        if (hit.Count > 0) InvalidateFields();
        // the other god, if the hand goes through where it stands
        if (d.Foe is { } foe && !d.FoeStruck && SegDist2(foe.X, foe.Z, ax, az, bx, bz) < MathF.Pow(foe.T.Stance + HandR, 2))
        {
            d.FoeStruck = true;
            Smite(d, foe, 3200, tx, tz);
            // struck low enough, the sword takes a leg off it (Sim.Kaiju.cs)
            Sever(d, foe);
        }
        if (d.T.Fiery) Kindle(bx, by, bz, HandR, 3);
    }

    /// <summary>The fist comes down: a crater, the ground thrown up out of it, those about it thrown out from it, the other god struck if it was there.</summary>
    void Slammed(Deity d)
    {
        float x = d.AimX, z = d.AimZ, y = d.AimY;
        const float r = 5.5f * VoxelWorld.PerMetre;
        var bowl = new List<(int x, int y, int z)>();
        int ri = (int)r;
        for (int dz = -ri; dz <= ri; dz++)
            for (int dx = -ri; dx <= ri; dx++)
            {
                float k = (dx * dx + dz * dz) / (r * r);
                int vx = (int)x + dx, vz = (int)z + dz;
                if (k > 1 || vx < 2 || vz < 2 || vx >= World.SX - 2 || vz >= World.SZ - 2) continue;
                int top = World.ColTop(vx, vz), bottom = Math.Max(4, (int)y - (int)(r * .55f * (1 - k)));
                for (int vy = top; vy >= bottom; vy--)
                {
                    byte m = World.Get(vx, vy, vz);
                    if (!Breakable(m) || d.Team == Team.Player && Mat.IsPlayer[m]) continue;
                    bowl.Add((vx, vy, vz));
                }
            }
        Scatter(d, bowl, x, y - r * .5f, z, 0, 0, 12 * VoxelWorld.PerMetre, 9 * VoxelWorld.PerMetre, 260);
        if (d.Team != Team.Player) Jar(x, y, z, r * 1.6f, .7f);
        // the blast through the ground
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside || u.Airborne != null) continue;
            float d2 = Dist2(u.X, u.Z, x, z);
            if (d2 > BlastR * BlastR) continue;
            float dist = MathF.Sqrt(d2), k = 1 - dist / BlastR, ox = dist > .5f ? (u.X - x) / dist : 1, oz = dist > .5f ? (u.Z - z) / dist : 0;
            if (dist < 3 * VoxelWorld.PerMetre + u.T.BodyR) { Strike(d, u, 99999); continue; }
            if (d.Team == Team.Player && !Hostile(d, u)) { if (k > .35f) GoDown(u, 2 * k); continue; }
            float lift = MathF.Min(1, 15 / u.T.BodyH);
            Strike(d, u, (Kinds.IsEngine(u.T) ? 600 : 70) * k);
            Fling(u, ox * (8 + 16 * k) * VoxelWorld.PerMetre * lift, (5 + 11 * k) * VoxelWorld.PerMetre * MathF.Sqrt(lift), oz * (8 + 16 * k) * VoxelWorld.PerMetre * lift, d.T.Cause);
        }
        if (d.Foe is { } foe && Dist2(foe.X, foe.Z, x, z) < MathF.Pow(foe.T.Stance + r, 2)) Smite(d, foe, 4200, x - d.X, z - d.Z);
        if (d.T.Fiery) { LetLava((int)x, (int)z, 70); Kindle(x, y, z, r * 2, 14); }
        InvalidateFields();
        Quaked?.Invoke(x, z, BlastR);
        GodShook?.Invoke(d, x, y, z, 3);
    }

    /// <summary>The hand closes on the land: a lump of it, and whatever stands on it, torn up into the hand.</summary>
    void Grab(Deity d)
    {
        float x = d.AimX, z = d.AimZ, y = d.AimY;
        var lump = new List<(int x, int y, int z)>();
        int ri = (int)LumpR;
        for (int dy = -ri; dy <= ri; dy++)
            for (int dz = -ri; dz <= ri; dz++)
                for (int dx = -ri; dx <= ri; dx++)
                {
                    if (dx * dx + dy * dy + dz * dz > LumpR * LumpR) continue;
                    int vx = (int)x + dx, vy = (int)y + dy - ri / 3, vz = (int)z + dz;
                    if (!World.InBounds(vx, vy, vz) || vy < 4) continue;
                    byte m = World.Get(vx, vy, vz);
                    if (Breakable(m) && !(d.Team == Team.Player && Mat.IsPlayer[m])) lump.Add((vx, vy, vz));
                }
        // those who stood on it are thrown off as it comes up
        foreach (var u in Units)
            if (!u.Dead && !u.Inside && u.Airborne == null && Dist2(u.X, u.Z, x, z) < MathF.Pow(LumpR + 2 * VoxelWorld.PerMetre, 2))
                Fling(u, 0, 6 * VoxelWorld.PerMetre, 0, d.T.Cause);
        var f = Loose(lump, 0, 0, 0, ColossusStoneCause);
        if (f == null) return;
        d.Broke += f.Voxels.Count;
        f.Bearer = d;
        d.Held = f;
        InvalidateFields();
    }

    /// <summary>A lump in a god's hand goes where the hand goes.</summary>
    void InHand(Flier f, float dt)
    {
        var d = f.Bearer;
        if (d == null || d.Gone || !InGrip(d, f)) { f.Bearer = null; return; }
        float x = d.HandX, y = d.HandY + f.R * .3f, z = d.HandZ;
        if (dt > 0) { f.VX = (x - f.X) / dt; f.VY = (y - f.Y) / dt; f.VZ = (z - f.Z) / dt; }
        f.X = x; f.Y = y; f.Z = z;
        f.Turn += .4f * dt;
    }

    /// <summary>The lump let go at the spot: high on its arc and down on it.</summary>
    void Throw(Deity d)
    {
        var f = d.Held;
        d.Held = null;
        if (f == null) return;
        f.Bearer = null;
        float dx = d.AimX - f.X, dz = d.AimZ - f.Z, dist = MathF.Sqrt(dx * dx + dz * dz);
        float t = Math.Clamp(.9f + dist / (40 * VoxelWorld.PerMetre), .9f, 3);
        f.VX = dx / t; f.VZ = dz / t; f.VY = (d.AimY + f.R - f.Y + .5f * Gravity * t * t) / t;
        float hs = MathF.Sqrt(f.VX * f.VX + f.VZ * f.VZ);
        if (hs > .1f) { f.AX = -f.VZ / hs; f.AY = 0; f.AZ = f.VX / hs; }
        f.Spin = 1.6f;
        f.Since = Time;
        Heaved?.Invoke(f.X, f.Y, f.Z, f.Voxels.Count);
    }

    /// <summary>The roar: the enemy for sixty metres about it runs, the hardest of them one in two; the bands lose heart.</summary>
    void Roared(Deity d)
    {
        const float r = 60 * VoxelWorld.PerMetre;
        int fled = 0;
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside || !Hostile(d, u) || Kinds.IsEngine(u.T) || Dist2(u.X, u.Z, d.X, d.Z) > r * r) continue;
            if (u.Team == Team.Horde)
            {
                if ((u.Leader || u.T == Kinds.Troll) && Rng.NextDouble() < .5) continue;
                u.Routed = true; u.Raid = null; u.Path = null; u.Target = null;
                StopCarve(u); StopBuild(u);
                fled++;
            }
            else GoDown(u, 3);
        }
        if (d.Team == Team.Player) DismayAbout(d.X, d.Z, r, .3f);
        if (fled > 0) Say($"Рёв колосса: бегут {fled}.", 1, d.X, d.Z);
        GodShook?.Invoke(d, d.X, d.Y + d.T.Height * .8f, d.Z, .6f);
    }

    /// <summary>A god's blow on one: hurt as hard as the blow, and told of as the god's work if it dies of it.</summary>
    void Strike(Deity d, Unit u, float harm)
    {
        bool was = u.Dead;
        Hurt(u, null, harm, d.T.Cause);
        if (!was && u.Dead) d.Kills++;
    }

    /// <summary>A god pushed so far along a way, its feet with it (and a leap it is on, the whole arc).</summary>
    void Shove(Deity g, float dx, float dz, float by)
    {
        float l = MathF.Max(.01f, MathF.Sqrt(dx * dx + dz * dz)), mx = dx / l * by, mz = dz / l * by;
        g.X += mx; g.Z += mz;
        for (int i = 0; i < 2; i++) { g.FootX[i] += mx; g.FootZ[i] += mz; }
        if (g.Act == DeityAct.Leap) { g.LeapFromX += mx; g.LeapFromZ += mz; g.AimX += mx; g.AimZ += mz; }
        g.Lifted = -1;
    }

    /// <summary>One god struck by the other: hurt, and staggered back from it, out of whatever it was at.</summary>
    void Smite(Deity d, Deity foe, float harm, float dx, float dz)
    {
        foe.Hp -= harm;
        Shove(foe, dx, dz, 5 * VoxelWorld.PerMetre);
        // a heavy blow (the sword, the pillar, the colossus's fist) breaks off whatever blow it was at, and a leap not yet
        // off the ground; a hop or the venom does not
        if (harm >= 3000 && (foe.Act is DeityAct.Sweep or DeityAct.Slam or DeityAct.Lift or DeityAct.Throw or DeityAct.Roar or DeityAct.Gust or DeityAct.Tongue or DeityAct.Spit
            || foe.Act == DeityAct.Leap && foe.Into(Time) < LeapCrouch))
        {
            LetCaughtGo(foe);
            Begin(foe, DeityAct.Standing);
        }
        string whom = foe.T.Kind switch { DeityKind.Archangel => "Архангела", DeityKind.Toad => "Великую жабу", _ => "Колосса" };
        Say($"{d.T.Name} бьёт {whom}: осталось {Math.Max(0, foe.Hp) / foe.T.Hp * 100:0} %.", 2, foe.X, foe.Z);
        GodShook?.Invoke(foe, foe.X, foe.Y + foe.T.Height * .5f, foe.Z, 2.5f);
    }

    /// <summary>
    /// What stands over the ground about a stretch of it, to the height given: all that is not the land as it grew —
    /// walls, houses, trees, the horde's works. The household's god leaves what the household built.
    /// </summary>
    List<(int x, int y, int z)> Above(Deity d, float ax, float az, float bx, float bz, float r, int top, bool spare)
    {
        var list = new List<(int x, int y, int z)>();
        int x0 = (int)(MathF.Min(ax, bx) - r), x1 = (int)(MathF.Max(ax, bx) + r), z0 = (int)(MathF.Min(az, bz) - r), z1 = (int)(MathF.Max(az, bz) + r);
        x0 = Math.Max(2, x0); z0 = Math.Max(2, z0); x1 = Math.Min(World.SX - 3, x1); z1 = Math.Min(World.SZ - 3, z1);
        top = Math.Min(top, World.SY - 2);
        for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                if (SegDist2(x + .5f, z + .5f, ax, az, bx, bz) > r * r) continue;
                int from = Ground(x, z, false), to = Math.Min(top, World.ColTop(x, z));
                for (int y = from; y <= to; y++)
                {
                    byte m = World.Get(x, y, z);
                    if (!Breakable(m) || spare && Mat.IsPlayer[m]) continue;
                    list.Add((x, y, z));
                    if (list.Count > 60000) return list;
                }
            }
        return list;
    }

    /// <summary>
    /// What a god broke, sent flying in pieces: each on the way given, out from the spot and up, a little every way;
    /// what the sky has no room for goes to dust.
    /// </summary>
    void Scatter(Deity d, List<(int x, int y, int z)> vox, float x, float y, float z, float vx, float vz, float outw, float up, int around)
    {
        if (vox.Count == 0) return;
        int room = MaxAloft - Aloft - 1000;
        foreach (var piece in Split(vox, around))
        {
            if (piece.Count == 0) continue;
            float cx = 0, cy = 0, cz = 0;
            foreach (var (px, py, pz) in piece) { cx += px; cy += py; cz += pz; }
            cx = cx / piece.Count - x; cy = cy / piece.Count - y; cz = cz / piece.Count - z;
            float od = MathF.Max(1, MathF.Sqrt(cx * cx + cz * cz)), j = .7f + .6f * (float)Rng.NextDouble();
            var f = piece.Count <= room ? Loose(piece, (vx + cx / od * outw) * j, (up + MathF.Max(0, cy) * .3f) * j, (vz + cz / od * outw) * j, d.T.Cause, room) : null;
            if (f != null) room -= f.Voxels.Count;
            else foreach (var (px, py, pz) in piece) Dust(px, py, pz);
            d.Broke += piece.Count;
        }
    }

    /// <summary>The square of the distance on the ground from a point to a stretch.</summary>
    static float SegDist2(float px, float pz, float ax, float az, float bx, float bz)
    {
        float vx = bx - ax, vz = bz - az, l2 = vx * vx + vz * vz;
        float t = l2 < 1e-4f ? 0 : Math.Clamp(((px - ax) * vx + (pz - az) * vz) / l2, 0, 1);
        float cx = ax + vx * t - px, cz = az + vz * t - pz;
        return cx * cx + cz * cz;
    }
}
