using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>A bone broken: a leg, an arm (the one that holds, the other), the ribs. A beast's forelegs are its arms.</summary>
[Flags]
public enum Break : byte { None = 0, LegR = 1, LegL = 2, Main = 4, Off = 8, Ribs = 16 }

/// <summary>Where on a body a blunt thing lands first: the legs, the trunk, an arm, the head.</summary>
public enum OnBody : byte { Legs, Trunk, Arm, Head }

/// <summary>
/// What a blunt thing does to a body, beyond the health it costs. Whatever strikes it — rubble and stones coming down,
/// a log, a beam, a piece of a wall, the chips of a blast, a rolling ball, a stone swung by the hand — brings so much
/// energy (half its mass times its speed squared: a voxel of stone is 4.5 kg), and does its harm where it lands: on the
/// head it dazes, knocks one senseless or breaks the skull; on an arm or a leg it breaks the bone; on the trunk it
/// breaks ribs, or crushes the chest. A helmet and armour soften it, and big bones bear more: a troll shrugs off what
/// lays a goblin low. A fall is taken on the feet: from three metres a leg may go, from ten one may die of it, from
/// twenty nobody gets up.
/// A broken leg limps (a third of the pace), climbs no ladder and lets go of one it is on; both broken, it lies where it
/// is. A broken arm drops what it holds — the weapon, the shield, the bow — and the one that holds the weapon fights no
/// more; broken ribs slow the work and the walk. The bones knit in a quarter of an hour, three times as fast resting by
/// the citadel or in it, twice as fast splinted by a comrade.
/// What lies on a body it cannot get up from: pinned under a heap of rubble, a log, a piece of a wall, it calls out, and
/// a comrade comes and digs it out (ours, or the horde for its own); pinned under more than it can bear it is slowly
/// crushed, and under little it works itself free.
/// </summary>
public sealed partial class Sim
{
    /// <summary>A voxel's weight, kg: stone 4.6, rock 5, earth 3.1, timber 1.2, water 2, leaves next to nothing.</summary>
    public static float Kg(byte m)
    {
        if (m == Mat.Air || Mat.Passable[m] && !Mat.IsLadder[m]) return 0;
        if (m == Mat.Water) return 1.95f;
        if (Mat.IsLeafy(m) || Mat.IsSprig(m)) return .15f;
        if (m is Mat.Iron or Mat.Steel) return 15;
        return Mat.Def[m].Group switch { MatGroup.Wood => 1.2f, MatGroup.Earth => 3.1f, MatGroup.Rock => 5, _ => 4.6f };
    }

    /// <summary>What a body weighs, kg, by its size: a man 70, a goblin 26, an orc 117, a troll 340.</summary>
    public static float BodyKg(UnitType t) => 1.17f * t.BodyH * t.BodyR * t.BodyR;

    /// <summary>How much more its bones bear than a man's: as their thickness, the two-thirds power of its weight.</summary>
    static float Bones(UnitType t) => MathF.Max(.45f, MathF.Pow(BodyKg(t) / 70, 2 / 3f));

    /// <summary>The health a blunt blow costs by its energy: as every falling thing has always struck (<see cref="Blow(Flier, float)"/>).</summary>
    public static float BlowOf(float joules) => Math.Min(320, .24f * MathF.Sqrt(Math.Max(0, joules)));

    // the joules, through armour and on a man's bones, past which each harm begins (and is sure at about twice that)
    public const float DazeJ = 25, KnockJ = 80, SkullJ = 300, ArmJ = 100, RibsJ = 180, ChestJ = 1500, LegJ = 150, LegsJ = 2400;

    double _boneSaid = -99, _pinSaid = -99;
    int _traumaDraws, _pinnedNow;
    float _pinLook;

    /// <summary>A bone broke: whose and which (for the sound of it).</summary>
    public Action<Unit, Break> BoneBroke;
    /// <summary>One of ours pinned called out for help (for its voice, and the «!» over it).</summary>
    public Action<Unit> CalledOut;

    /// <summary>How far, in voxels, the call of one pinned is heard by those free to come.</summary>
    const float HeardFrom = 70 * VoxelWorld.PerMetre;

    /// <summary>A draw of its own for the harm, so that the valley's other chances run as they did.</summary>
    float Roll(Unit u, int salt) => (float)Hash.H3(u.Id * 131 + salt, (int)(Time * 64), ++_traumaDraws);

    static float Ramp(float v, float lo, float hi) => Math.Clamp((v - lo) / (hi - lo), 0, 1);

    /// <summary>
    /// A blunt thing strikes a body: so many kilograms at so many metres a second, coming along (dx, dy, dz), meeting it
    /// at a height on it (0 the feet, 1 the top of the head; NaN where a thing coming that way would). It costs the health
    /// <paramref name="blow"/> (by the energy when not given) and does its harm where it lands. Returns the energy, joules.
    /// </summary>
    public float Strike(Unit u, float kg, float speed, float dx, float dy, float dz, float at, string cause, float blow = float.NaN, Unit by = null)
    {
        if (u.Dead) return 0;
        float e = .5f * kg * speed * speed;
        float len = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        if (len > 1e-4f) { dx /= len; dy /= len; dz /= len; } else { dx = 0; dy = -1; dz = 0; }
        // a weight many times its own meets all of it at once: from above the head and shoulders first, from the side the trunk
        if (kg > BodyKg(u.T) * 3 && !Kinds.IsEngine(u.T))
        {
            float[] parts = dy < -.6f ? new[] { e * .15f, e * .35f, e * .15f, e * .35f } : new[] { e * .3f, e * .4f, e * .2f, e * .1f };
            StrikeHeap(u, parts, cause, float.IsNaN(blow) ? BlowOf(e) : blow, by);
            return e;
        }
        if (float.IsNaN(at))
            at = dy < -.6f ? .78f + .22f * Roll(u, 1) : dy > .4f ? .1f + .5f * Roll(u, 1) : .1f + .85f * Roll(u, 1);
        var where = Where(u, at, MathF.Abs(dy) < .6f);
        u.StruckAt = Time;
        if (Injure(u, where, SideOf(u, dx, dz), e, cause, by)) return e;
        if (float.IsNaN(blow)) blow = BlowOf(e);
        u.HitHead = where == OnBody.Head;
        if (blow > 0) Hurt(u, by, blow, cause);
        Knocked(u, e, where);
        return e;
    }

    /// <summary>
    /// A heap comes down on a body: the joules it brought to each of the legs, the trunk, the arms and the head
    /// (<see cref="OnBody"/>), and the health it costs. Each part takes its own; the trunk and the head together, more
    /// than a chest bears, crush it.
    /// </summary>
    public void StrikeHeap(Unit u, float[] joules, string cause, float blow, Unit by = null)
    {
        if (u.Dead) return;
        u.StruckAt = Time;
        float all = 0;
        foreach (float j in joules) all += j;
        if (all < 1) { if (blow > 0) Hurt(u, by, blow, cause); return; }
        int side = Roll(u, 2) < .5f ? 1 : -1;
        for (int s = 3; s >= 0; s--)
            if (joules[s] > 1 && Injure(u, (OnBody)s, s == (int)OnBody.Arm ? side : side * -1, joules[s], cause, by)) return;
        // what came down on the trunk and the head together, more than the chest bears
        if (!Kinds.IsEngine(u.T) && (joules[(int)OnBody.Trunk] + joules[(int)OnBody.Head]) * Guard(u, OnBody.Trunk) / Bones(u.T) >= ChestJ * 1.6f)
        {
            Fatal(u, "раздавлен", cause, by);
            return;
        }
        u.HitHead = joules[(int)OnBody.Head] > joules[(int)OnBody.Trunk];
        if (blow > 0) Hurt(u, by, blow, cause);
        Knocked(u, all, OnBody.Trunk);
    }

    /// <summary>
    /// One comes down from a height — off a wall, a ladder, a bridge, thrown by a blast — and lands on its feet as it
    /// can: the metres it fell (into water, as a third of them), what it costs in health.
    /// </summary>
    public void Tumble(Unit u, float metres, string cause, float blow)
    {
        if (u.Dead) return;
        if (Nav.Water[u.Cell] > 0) metres *= .3f;
        // a big body falls harder on its own weight
        float h = metres * MathF.Pow(BodyKg(u.T) / 70, .15f);
        if (Kinds.IsEngine(u.T) || h < 2.5f) { if (blow > 0) Hurt(u, null, blow, cause); return; }
        u.StruckAt = Time;
        if (Roll(u, 3) < Ramp(h, 10, 20)) { Fatal(u, "разбился", cause, null); return; }
        int side = Roll(u, 4) < .5f ? 1 : -1;
        if (Roll(u, 5) < Ramp(h, 3, 9)) BreakBone(u, Leg(side));
        if (Roll(u, 6) < Ramp(h, 6, 14)) BreakBone(u, Leg(-side));
        // it went over: the hand put out, the head struck
        if (Roll(u, 7) < Ramp(h, 5, 14)) BreakBone(u, Roll(u, 8) < .5f ? Break.Main : Break.Off);
        if (Roll(u, 9) < Ramp(h, 5, 12)) Injure(u, OnBody.Head, side, (KnockJ * .7f + (h - 5) * 25) * Bones(u.T) / Guard(u, OnBody.Head), cause, null);
        if (u.Dead) return;
        if (blow > 0) Hurt(u, null, blow, cause);
        if (!u.Down && h >= 3) GoDown(u, .8 + h * .25);
    }

    /// <summary>The leg on a side: 1 the right, −1 the left.</summary>
    static Break Leg(int side) => side > 0 ? Break.LegR : Break.LegL;

    /// <summary>The part a thing meets at a height on the body; one coming from the side meets an arm more often.</summary>
    OnBody Where(Unit u, float at, bool aside)
    {
        if (at >= .86f) return OnBody.Head;
        if (at >= .47f) return Roll(u, 10) < (aside ? .5f : .3f) ? OnBody.Arm : OnBody.Trunk;
        return OnBody.Legs;
    }

    /// <summary>The part of one lying down a thing coming down at a spot lands on: the head at the end it faces, the legs at the other, the trunk between.</summary>
    static OnBody Lying(Unit u, float x, float z)
    {
        float along = ((x - u.X) * MathF.Cos(u.Facing) + (z - u.Z) * MathF.Sin(u.Facing)) / MathF.Max(1, u.T.BodyH);
        return along > .3f ? OnBody.Head : along < -.15f ? OnBody.Legs : OnBody.Trunk;
    }

    /// <summary>The side a thing coming along (dx, dz) strikes: 1 the right (the hand with the weapon), −1 the left.</summary>
    int SideOf(Unit u, float dx, float dz)
    {
        // the body's right, in the world, and where the thing came from
        float rx = -MathF.Sin(u.Facing), rz = MathF.Cos(u.Facing), d = -(dx * rx + dz * rz);
        if (MathF.Abs(d) < .15f) return Roll(u, 11) < .5f ? 1 : -1;
        return d > 0 ? 1 : -1;
    }

    static readonly Dictionary<BodyPlan, float[]> _cover = new();

    /// <summary>
    /// How much of a part's outside armour covers, 0…1, by the half-voxels of its surface: the head, the trunk, the arms
    /// (the shield not counted), the legs.
    /// </summary>
    static float Cover(BodyPlan plan, OnBody where)
    {
        float[] c;
        lock (_cover)
            if (!_cover.TryGetValue(plan, out c))
            {
                var armour = new int[4]; var outside = new int[4];
                for (int p = 0; p < BodyPlan.Parts; p++)
                {
                    var g = plan.Grid[p];
                    if (g == null) continue;
                    for (int i = 0; i < g.M.Length; i++)
                    {
                        if (g.M[i] == 0) continue;
                        var t = (Tissue)g.T[i];
                        if (t is not (Tissue.Skin or Tissue.Armor)) continue;
                        if (plan.Owner[p] != null && plan.Owner[p][i] != 0) continue;
                        int x = i % g.SX, z = i / g.SX % g.SZ, y = i / (g.SX * g.SZ);
                        bool open = x == 0 || z == 0 || y == 0 || x == g.SX - 1 || z == g.SZ - 1 || y == g.SY - 1
                            || g.M[i - 1] == 0 || g.M[i + 1] == 0 || g.M[i - g.SX] == 0 || g.M[i + g.SX] == 0 || g.M[i - g.SX * g.SZ] == 0 || g.M[i + g.SX * g.SZ] == 0;
                        if (!open) continue;
                        int s = p switch
                        {
                            0 => y + g.Y0 >= plan.HeadY0 ? (int)OnBody.Head : (int)OnBody.Trunk,
                            1 or 2 => (int)OnBody.Arm,
                            _ => (int)OnBody.Legs,
                        };
                        outside[s]++;
                        if (t == Tissue.Armor) armour[s]++;
                    }
                }
                c = new float[4];
                for (int s = 0; s < 4; s++) c[s] = outside[s] == 0 ? 0 : armour[s] / (float)outside[s];
                _cover[plan] = c;
            }
        return c[(int)where];
    }

    /// <summary>The share of a blunt blow that gets through what it wears there: a helmet takes up to two thirds, armour elsewhere up to nearly half.</summary>
    float Guard(Unit u, OnBody where) => 1 - (where == OnBody.Head ? .65f : .45f) * Cover(Bodies.Plan(u.T, u.Look), where);

    /// <summary>The harm a blow of so many joules does where it lands; true if it killed.</summary>
    bool Injure(Unit u, OnBody where, int side, float e, string cause, Unit by)
    {
        if (u.Dead || Kinds.IsEngine(u.T) || e < DazeJ * .5f) return false;
        float j = e * Guard(u, where) / Bones(u.T);
        switch (where)
        {
            case OnBody.Head:
                if (j >= SkullJ && Roll(u, 20) < Ramp(j, SkullJ, SkullJ * 1.8f) + .25f) { Fatal(u, "проломлен череп", cause, by); return true; }
                if (Sense(u.T) <= 0) break;
                if (j >= KnockJ && Roll(u, 21) < Ramp(j, KnockJ, KnockJ * 2) + .3f)
                {
                    double out_ = Math.Min(30, 4 + j / 25);
                    u.DazedUntil = Math.Max(u.DazedUntil, Time + out_ + 4);
                    GoDown(u, out_);
                    var body = u.Wounds ??= new BodyState(Bodies.Plan(u.T, u.Look));
                    Note(body, "сотрясение");
                    body.Rev++;
                    if (u.Team == Team.Player && Time - _boneSaid > 4) { _boneSaid = Time; Say($"{u.Name}: удар по голове, без сознания.", 3, u); }
                }
                else if (j >= DazeJ)
                {
                    u.DazedUntil = Math.Max(u.DazedUntil, Time + Math.Min(10, 1.5 + j / 20));
                    u.Cooldown = Math.Max(u.Cooldown, Math.Min(3, .5f + j / 60));
                }
                break;
            case OnBody.Trunk:
                if (j >= ChestJ && Roll(u, 22) < Ramp(j, ChestJ, ChestJ * 2)) { Fatal(u, "раздавлена грудь", cause, by); return true; }
                if (Roll(u, 23) < Ramp(j, RibsJ, RibsJ * 2.2f)) BreakBone(u, Break.Ribs);
                break;
            case OnBody.Arm:
                if (Roll(u, 24) < Ramp(j, ArmJ, ArmJ * 2.2f)) BreakBone(u, side > 0 ? Break.Main : Break.Off);
                break;
            case OnBody.Legs:
                if (j >= LegsJ) { BreakBone(u, Break.LegR); BreakBone(u, Break.LegL); }
                else if (Roll(u, 25) < Ramp(j, LegJ, LegJ * 2.1f)) BreakBone(u, Leg(side));
                break;
        }
        return false;
    }

    /// <summary>
    /// A blast's harm on one near it, coming out from it along (dx, dy, dz): the health it costs, and a chip of stone or
    /// a stave flung at it that meets it somewhere, the harder the nearer.
    /// </summary>
    public void Splinter(Unit u, float harm, float dx, float dy, float dz, string cause, Unit by = null)
    {
        if (u.Dead) return;
        if (Kinds.IsEngine(u.T) || harm < 4) { Hurt(u, by, harm, cause); return; }
        u.StruckAt = Time;
        float at = .05f + .95f * Roll(u, 12);
        if (Injure(u, Where(u, at, true), SideOf(u, dx, dz), harm * 6, cause, by)) return;
        u.HitHead = at >= .86f;
        Hurt(u, by, harm, cause);
    }

    /// <summary>A blow heavy for the body it falls on knocks it off its feet a moment (one swept off the legs the more readily).</summary>
    void Knocked(Unit u, float e, OnBody where)
    {
        if (u.Dead || u.Down || Kinds.IsEngine(u.T) || u.Airborne != null) return;
        float perKg = e / BodyKg(u.T);
        if (perKg > (where == OnBody.Legs ? 2.5f : 4)) GoDown(u, Math.Min(4, .8 + perKg * .12));
    }

    /// <summary>Dead of what it took: the harm written on the body, the death put down to what did it.</summary>
    void Fatal(Unit u, string harm, string cause, Unit by)
    {
        var body = u.Wounds ??= new BodyState(Bodies.Plan(u.T, u.Look));
        Note(body, harm);
        body.Rev++;
        Hurt(u, by, u.Hp + 1, cause);
    }

    /// <summary>"сломана правая нога", "сломаны рёбра", a beast's "сломана передняя нога", the horse's under a rider.</summary>
    static string BoneName(BodyPlan plan, Break b) => b switch
    {
        Break.Ribs => "сломаны рёбра",
        Break.Main or Break.Off when plan.Beast => "сломана передняя нога",
        Break.LegR or Break.LegL when plan.Beast => "сломана задняя нога",
        Break.LegR when plan.Mounted => "коню сломана передняя нога",
        Break.LegL when plan.Mounted => "коню сломана задняя нога",
        Break.Main => "сломана правая рука",
        Break.Off => "сломана левая рука",
        Break.LegR => "сломана правая нога",
        _ => "сломана левая нога",
    };

    const string Splint = ", в лубке";

    static int Slot(Break b) => b switch { Break.LegR => 0, Break.LegL => 1, Break.Main => 2, Break.Off => 3, _ => 4 };
    static readonly Break[] Bones5 = { Break.LegR, Break.LegL, Break.Main, Break.Off, Break.Ribs };

    /// <summary>
    /// A bone breaks: the pain of it, the hand to it, and what it holds falls from an arm broken (the weapon, the shield,
    /// the bow), to lie on the ground. Of ours, the chronicle says so. False if it was broken already, or the limb is gone.
    /// </summary>
    public bool BreakBone(Unit u, Break b)
    {
        if (u.Dead || Kinds.IsEngine(u.T)) return false;
        var body = u.Wounds ??= new BodyState(Bodies.Plan(u.T, u.Look));
        if ((body.Broken & b) != 0) return false;
        if (b == Break.Main && !body.MainHand || b == Break.Off && !body.OffHand) return false;
        body.Broken |= b;
        body.Splinted &= ~b;
        body.Knit[Slot(b)] = 0;
        string name = BoneName(body.Plan, b);
        Note(body, name);
        body.Rev++;
        u.Pain = Math.Min(1.6f, u.Pain + .3f * Sense(u.T));
        u.Clutch = b switch { Break.Main => (byte)BodyPart.Main, Break.Off => (byte)BodyPart.Off, Break.LegR => (byte)BodyPart.LegR, Break.LegL => (byte)BodyPart.LegL, _ => (byte)BodyPart.Body };
        u.ClutchUntil = Time + 5;
        if (b is Break.Main or Break.Off && !body.Plan.Beast) Drop(u, body, b == Break.Main ? BodyPart.Main : BodyPart.Off);
        // one with an arm gone that held its weapon gives up the ladder it was throwing down, and the work it was at
        if (b == Break.Main) { u.PushCell = -1; StopCarve(u); StopBuild(u); }
        BoneBroke?.Invoke(u, b);
        if (u.Team == Team.Player && Time - _boneSaid > 4) { _boneSaid = Time; Say($"{u.Name}: {name}.", 3, u); }
        return true;
    }

    /// <summary>What an arm held falls from it: its half-voxels taken off the body as a piece that lies on the ground.</summary>
    void Drop(Unit u, BodyState body, BodyPart part)
    {
        var plan = body.Plan;
        var g = body.Grid[(int)part];
        var owner = plan.Owner[(int)part];
        if (g == null || owner == null || owner.Length != g.M.Length) return;
        var piece = new PartGrid { X0 = g.X0, Y0 = g.Y0, Z0 = g.Z0, SX = g.SX, SY = g.SY, SZ = g.SZ, M = new byte[g.M.Length], T = new byte[g.M.Length] };
        for (int d = 0; d < plan.Dress.Count; d++)
        {
            if (plan.Dress[d].On != part) continue;
            for (int i = 0; i < g.M.Length; i++)
            {
                if (owner[i] != d + 1 || g.M[i] == 0) continue;
                piece.M[i] = g.M[i]; piece.T[i] = g.T[i]; piece.Count++;
                g.M[i] = 0; g.T[i] = 0; g.Count--;
            }
        }
        if (piece.Count == 0) return;
        if (part == BodyPart.Off) body.Shield = false;
        var rem = new Remnant { Grid = piece, From = part, X = u.X, Y = u.Y, Z = u.Z, Facing = u.Facing, Scale = ScaleOf(u), At = Time, Of = u };
        Remnants.Add(rem);
        Severed?.Invoke(rem);
    }

    /// <summary>The bones it has broken now (none for one never hurt).</summary>
    public static Break Broken(Unit u) => u.Wounds?.Broken ?? Break.None;

    /// <summary>A leg broken (a beast's foreleg too): it limps, and climbs no ladder.</summary>
    public static bool Lame(Unit u)
    {
        var b = Broken(u);
        return (b & (Break.LegR | Break.LegL)) != 0 || u.Wounds is { Plan.Beast: true } && (b & (Break.Main | Break.Off)) != 0;
    }

    /// <summary>Both legs broken: it cannot stand.</summary>
    static bool Legless(Unit u) => (Broken(u) & (Break.LegR | Break.LegL)) == (Break.LegR | Break.LegL);

    /// <summary>Kept off its feet by its hurts: pinned, or both legs broken.</summary>
    public static bool Grounded(Unit u) => u.PinKg > 0 || Legless(u);

    /// <summary>Goes up and down ladders and hangs on them: its kind does, no leg broken, an arm to hold on with, its wits about it.</summary>
    public bool Holds(Unit u) => u.T.Move.Ladders && !Lame(u) && (Broken(u) & (Break.Main | Break.Off)) != (Break.Main | Break.Off) && !u.Down && Time >= u.DazedUntil;

    /// <summary>How much of its work it does with its hurts: one hand the less, a third; ribs broken, three fifths.</summary>
    public static float Hands(Unit u)
    {
        var b = Broken(u);
        if (b == Break.None) return 1;
        float k = 1;
        if ((b & Break.Main) != 0) k *= .3f;
        if ((b & Break.Off) != 0) k *= .6f;
        if ((b & Break.Ribs) != 0) k *= .6f;
        return k;
    }

    /// <summary>A weapon swung with both hands, swung with one: half as hard with the other arm broken.</summary>
    static float Grip(Unit u) => (Broken(u) & Break.Off) != 0 && u.T.Tool is "halberd" or "maul" or "longsword" or "spear" or "pick" ? .5f : 1;

    /// <summary>The way one lame walks: for the planning of its ways, a mover of its kind that takes no ladder.</summary>
    static readonly Dictionary<Mover, Mover> _lame = new();
    static Mover MoverOf(Unit u)
    {
        var s = u.T.Move;
        // a broken leg, or a comrade on the shoulder: no ladder (Sim.Carry.cs)
        if (!Lame(u) && u.Carrying is not { Aloft: true } || !s.Ladders) return s;
        lock (_lame)
        {
            if (!_lame.TryGetValue(s, out var m))
                _lame[s] = m = new Mover { Height = s.Height, Climb = s.Climb, Drop = Math.Min(s.Drop, 2), Speed = s.Speed, Carve = s.Carve, Build = s.Build, Ladders = false };
            return m;
        }
    }

    /// <summary>Hurt so that it is out of the work and the fight till it knits: a leg or an arm broken.</summary>
    public static bool Disabled(Unit u) => (Broken(u) & (Break.LegR | Break.LegL | Break.Main | Break.Off)) != 0;

    // ------------------------------------------------------------ knitting

    /// <summary>Seconds a bone takes to knit out in the valley; resting by the citadel thrice as fast, splinted twice.</summary>
    public const float KnitFor = 900;

    /// <summary>The bones knitting; one knocked senseless coming round; the pinned weighed again and crushed; who was struck lately looked at for being pinned.</summary>
    void TickTrauma(float dt)
    {
        _pinLook += dt;
        bool look = _pinLook >= .25f;
        if (look) _pinLook = 0;
        _pinnedNow = 0;
        foreach (var u in Units)
        {
            if (u.Dead) continue;
            if (u.PinKg > 0) { _pinnedNow++; Pinned(u, dt, look); continue; }
            // (at once, before one buried under it walks out from under what it could not get up from)
            if (!u.Inside && u.Airborne == null && Time - u.StruckAt < 2) TryPin(u);
            var w = u.Wounds;
            if (w == null) continue;
            if (w.Broken != Break.None) Knit(u, w, dt);
            if (Time > u.DazedUntil + 60 && w.Wounds.Remove("сотрясение")) w.Rev++;
            if (Legless(u) && !u.Down && u.Airborne == null && !u.Inside) GoDown(u, double.PositiveInfinity);
        }
    }

    void Knit(Unit u, BodyState w, float dt)
    {
        float dx = u.X - Site.KeepX, dz = u.Z - Site.KeepZ;
        bool home = u.Inside || dx * dx + dz * dz < MendBy * MendBy || InDwelling(u);
        foreach (var b in Bones5)
        {
            if ((w.Broken & b) == 0) continue;
            int s = Slot(b);
            w.Knit[s] += dt / KnitFor * (home ? 3 : 1) * ((w.Splinted & b) != 0 ? 2 : 1);
            if (w.Knit[s] < 1) continue;
            string name = BoneName(w.Plan, b);
            w.Wounds.Remove(name);
            w.Wounds.Remove(name + Splint);
            w.Broken &= ~b; w.Splinted &= ~b; w.Knit[s] = 0;
            w.Rev++;
            if (u.Team == Team.Player) Say($"{u.Name}: кость срослась.", 1, u);
        }
    }

    /// <summary>Its broken bones splinted by a comrade: they knit twice as fast.</summary>
    public void SplintBones(Unit u)
    {
        var w = u.Wounds;
        if (w == null) return;
        foreach (var b in Bones5)
        {
            if ((w.Broken & b) == 0 || (w.Splinted & b) != 0 || b == Break.Ribs) continue;
            w.Splinted |= b;
            string name = BoneName(w.Plan, b);
            int i = w.Wounds.IndexOf(name);
            if (i >= 0) w.Wounds[i] = name + Splint;
        }
        w.Rev++;
    }

    /// <summary>A bone of its that a comrade could splint: an arm or a leg broken and not splinted yet.</summary>
    static bool WantsSplint(Unit u) => u.Wounds is { } w && (w.Broken & ~w.Splinted & ~Break.Ribs) != 0;

    // ------------------------------------------------------------ pinned

    /// <summary>Kilograms on a body past which it cannot get up from under them: half its own weight on it, lying.</summary>
    static float PinAt(Unit u) => BodyKg(u.T) * .6f;

    /// <summary>
    /// What lies on a body where it is, kg: in each column of its round, the run of what came down that begins where the
    /// body is (lying, as high as it is broad; standing, its height) and goes on up unbroken, a metre over its head at
    /// most. A roof or a deck over it with air between is not on it; nor is what stands as it was built (a wall it lies
    /// against), nor the living ground and rock, only loose earth and stone and what fell.
    /// </summary>
    public float Load(Unit u) => Load(u, null);

    float Load(Unit u, List<(int x, int y, int z)> into)
    {
        float r = MathF.Max(1, u.T.BodyR * .75f), kg = 0;
        int x0 = (int)MathF.Floor(u.X - r), x1 = (int)MathF.Floor(u.X + r), z0 = (int)MathF.Floor(u.Z - r), z1 = (int)MathF.Floor(u.Z + r);
        int y0 = (int)MathF.Floor(u.Y + .01f), band = u.Down ? (int)(u.T.BodyR * 2) + 1 : (int)u.T.BodyH, y1 = y0 + (int)u.T.BodyH + VoxelWorld.PerMetre;
        int from = into?.Count ?? 0;
        for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                int ys = -1;
                for (int y = y0; y <= y0 + band && ys < 0; y++) if (Fallen(x, y, z)) ys = y;
                if (ys < 0) continue;
                for (int y = ys; y < y1 && Fallen(x, y, z); y++)
                {
                    kg += Kg(World.Get(x, y, z));
                    into?.Add((x, y, z));
                }
            }
        // the top first, so what is lifted off leaves nothing hanging over it
        if (into != null && into.Count - from > 1) into.Sort(from, into.Count - from, Comparer<(int x, int y, int z)>.Create((a, b) => b.y.CompareTo(a.y)));
        return kg;
    }

    /// <summary>A voxel that may be lying on someone: something solid with weight that can be shifted, not standing as it was built, not the living ground.</summary>
    bool Fallen(int x, int y, int z)
    {
        if (!World.InBounds(x, y, z)) return false;
        byte m = World.Get(x, y, z);
        // (a gate's leaf swung open stands where it was not built, and lies on nobody)
        if (m == Mat.Air || m == Mat.Water || Mat.Swings(m) || !Breakable(m) || Kg(m) <= 0) return false;
        if (Mat.IsNatural[m] && m is not (Mat.Soil or Mat.Rubble or Mat.Lumber)) return false;
        return MeantAt(x, y, z) != m;
    }

    /// <summary>One struck lately with something lying on it: pinned if it is off its feet under enough, or buried whatever it was.</summary>
    bool TryPin(Unit u)
    {
        if (u.Dead || u.Inside || u.Airborne != null || u.PinKg > 0 || Kinds.IsEngine(u.T) || u.Captor != null) return false;
        float kg = Load(u);
        if (kg < PinAt(u) || !u.Down && kg < BodyKg(u.T) * 2.5f) return false;
        u.PinKg = kg;
        u.PinnedSince = Time;
        if (!u.Down) GoDown(u, double.PositiveInfinity);
        u.DownUntil = double.PositiveInfinity;
        if (u.Team == Team.Player && Time - _pinSaid > 4)
        {
            _pinSaid = Time;
            Say($"{Cap(Whom(u.T))} придавило обломками.", 3, u);
        }
        return true;
    }

    static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>Pinned: weighed again now and then, freed when what lies on it is gone; slowly crushed under more than it bears; working itself out from under a little.</summary>
    void Pinned(Unit u, float dt, bool look)
    {
        if (u.Inside || u.Airborne != null) { Unpin(u, said: false); return; }
        if (look)
        {
            u.PinKg = Load(u);
            if (u.PinKg < PinAt(u) * .4f) { Unpin(u, said: true); return; }
        }
        u.Down = true;
        u.DownUntil = double.PositiveInfinity;
        u.State = UnitState.Idle;
        u.Path = null;
        // one who went to dig it out and was called off by something more pressing (the horde, the alarm) leaves it to another
        if (u.Digger != null && (u.Digger.Dead || u.Digger.Freeing != u || Time - u.DugAt > 3)) { if (u.Digger.Freeing == u) u.Digger.Freeing = null; u.Digger = null; }
        // it calls out now and then while nobody digs, more often at first: heard further than it is seen
        if (u.Team == Team.Player && u.Digger == null && Time >= u.NextCall)
        {
            u.CalledAt = Time;
            u.NextCall = Time + (Time - u.PinnedSince < 20 ? 4 : 9) + Hash.H3(u.Id, (int)Time, 31) * 3;
            CalledOut?.Invoke(u);
        }
        float kg = BodyKg(u.T), bear = kg * 1.2f;
        if (u.PinKg > bear)
        {
            u.Hp -= u.MaxHp * (u.PinKg - bear) / (u.PinKg + 4 * bear) * .05f * dt;
            u.LastHurt = Time;
            if (u.Hp <= 0) { Fatal(u, "задавлен", "задавлен обломками", null); return; }
        }
        // a little on it, its arms free and nobody coming: it works itself out from under it
        if (u.PinKg < kg * .9f && (Broken(u) & (Break.Main | Break.Off)) != (Break.Main | Break.Off) && Time - u.PinnedSince > 6 && u.Digger == null
            && Roll(u, 30) < dt * .25f)
        {
            var on = new List<(int x, int y, int z)>();
            Load(u, on);
            foreach (var (x, y, z) in on) { byte m = World.Get(x, y, z); World.Set(x, y, z, Mat.Air); Hacked?.Invoke(x, y, z, m); }
            Unpin(u, said: true);
        }
    }

    void Unpin(Unit u, bool said)
    {
        u.PinKg = 0;
        if (u.Digger != null) { u.Digger.Freeing = null; u.Digger = null; }
        if (u.Down && !Legless(u)) u.DownUntil = Time + 1.2;
        if (said && u.Team == Team.Player && !u.Dead) Say($"{u.Name} выбрался из-под обломков.", 1, u);
    }

    /// <summary>Voxels a second a body digs a comrade out at: a worker 30, a soldier half that; the horde by its size.</summary>
    static float DigOut(Unit u) => u.Team == Team.Player ? 30 * Math.Max(.5f, u.T.Work) * Hands(u) : 20 * MathF.Sqrt(BodyKg(u.T) / 70) * Hands(u);

    /// <summary>
    /// One with nothing more pressing sees a comrade pinned close by — ours as far as 40 m and no raider over it, a
    /// raider its own as far as 12 m — goes to it and digs it out, from the top down: what it lifts off ours goes to the
    /// stores as dug stone or timber. True while at that.
    /// </summary>
    bool Rescue(Unit u, float dt, float reach)
    {
        var p = u.Freeing;
        // (one the player sent digs beside whoever else is at it)
        if (p == null || p.Dead || p.PinKg <= 0 || p.Digger != u && !(u.FreeOrdered && p.Digger is { Dead: false }))
        {
            if (p != null && p.Digger == u) p.Digger = null;
            u.Freeing = p = null;
            u.FreeOrdered = false;
            if (_pinnedNow == 0 || Time < u.NextLookForPinned || u.T.NoHands || Kinds.IsEngine(u.T) || Hands(u) < .2f) return false;
            u.NextLookForPinned = Time + 1 + u.Id % 5 * .1;
            float best = reach;
            foreach (var o in Units)
            {
                if (o == u || o.Dead || o.PinKg <= 0 || o.Team != u.Team || o.Digger is { Dead: false } && o.Digger != u) continue;
                float d = Dist(u, o);
                // one that called out lately is heard further than it is seen (not by soldiers kept at their posts)
                float far = Time - o.CalledAt < 8 && reach >= 40 * VoxelWorld.PerMetre ? HeardFrom : best;
                if (d < Math.Max(best, far) && (u.Team != Team.Player || Nearest(o, Team.Horde, 10 * VoxelWorld.PerMetre) == null)) { best = d; p = o; }
            }
            if (p == null) return false;
            u.Freeing = p; p.Digger = u; u.FreeEffort = 0; p.DugAt = Time;
            if (u.Team == Team.Player) ReleaseSite(u);
            StopCarve(u); StopBuild(u);
            u.Path = null;
        }
        p.DugAt = Time;
        // beside it: two metres off at most (the heap over it may fill the cells next to it), or within arm's reach
        bool beside = Math.Abs(Nav.I(u.Cell) - Nav.I(p.Cell)) <= 2 && Math.Abs(Nav.K(u.Cell) - Nav.K(p.Cell)) <= 2 && Math.Abs(Nav.Y(u.Cell) - Nav.Y(p.Cell)) <= 2;
        if (!beside && Dist(u, p) > u.T.BodyR + p.T.BodyR + 10)
        {
            if (u.Path == null || u.PathI >= u.Path.Count || Time >= u.NextSearch)
            {
                u.NextSearch = Time + .8;
                if (!GoTo(u, p.Cell, adjacent: true, budget: u.FreeOrdered ? 60000 : 8000, reach: 2)) { if (p.Digger == u) p.Digger = null; u.Freeing = null; u.FreeOrdered = false; u.NextLookForPinned = Time + 5; return false; }
            }
            if (u.Path != null && u.PathI < u.Path.Count) Walk(u, dt);
            return true;
        }
        u.Path = null;
        u.State = UnitState.Work;
        u.Facing = MathF.Atan2(p.Z - u.Z, p.X - u.X);
        u.FreeEffort += dt * DigOut(u);
        if (u.FreeEffort < 1) return true;
        var on = new List<(int x, int y, int z)>();
        Load(p, on);
        // from the top down, so what is left does not come down on it again
        int n = 0;
        foreach (var (x, y, z) in on)
        {
            if (u.FreeEffort < 1) break;
            byte m = World.Get(x, y, z);
            World.Set(x, y, z, Mat.Air);
            if (Orders.At(x, y, z) != 0) Orders.Clear(x, y, z);
            if (u.Team == Team.Player)
            {
                int kind = Labour.YieldKind(m);
                if (kind != Stockpile.None) Stock[kind] += Labour.YieldShare(m) * Math.Max(.5f, u.T.Yield);
            }
            if (n++ % 3 == 0) Hacked?.Invoke(x, y, z, m);
            u.FreeEffort -= 1;
            u.Swing = 1;
        }
        u.FreeEffort = Math.Min(u.FreeEffort, 2);
        if (on.Count == 0 || Load(p) < PinAt(p) * .4f)
        {
            Unpin(p, said: false);
            if (u.Team == Team.Player) Say($"{u.Name} откопал {Whom(p.T)}.", 1, p);
            u.Freeing = null; u.FreeOrdered = false;
        }
        return true;
    }
}
