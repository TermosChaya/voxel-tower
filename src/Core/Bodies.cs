using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// The parts of a body that move on their own: the trunk with the head, the arms with what they hold, the legs.
/// A beast's front legs take the arms' places.
/// </summary>
public enum BodyPart : byte { Body, Main, Off, LegR, LegL }

/// <summary>What a voxel of a body is: worn or carried, or the body itself, from the skin in.</summary>
public enum Tissue : byte { None, Skin, Armor, Gear, Flesh, Bone, Brain, Heart }

/// <summary>A block of one part's cells — half-voxels (6 cm) for the body the blows cut, finer for how it is drawn: what each is made of, and what it is.</summary>
public sealed class PartGrid
{
    public int X0, Y0, Z0, SX, SY, SZ;
    public byte[] M, T;
    /// <summary>Voxels there are.</summary>
    public int Count;

    public bool In(int x, int y, int z) => x >= X0 && y >= Y0 && z >= Z0 && x < X0 + SX && y < Y0 + SY && z < Z0 + SZ;
    public int Index(int x, int y, int z) => ((y - Y0) * SZ + (z - Z0)) * SX + (x - X0);
    public byte Mat(int x, int y, int z) => In(x, y, z) ? M[Index(x, y, z)] : (byte)0;
    public Tissue Kind(int x, int y, int z) => In(x, y, z) ? (Tissue)T[Index(x, y, z)] : Tissue.None;

    public void Clear(int x, int y, int z)
    {
        int i = Index(x, y, z);
        if (M[i] == 0) return;
        M[i] = 0; T[i] = 0;
        Count--;
    }

    public PartGrid Clone() => new()
    {
        X0 = X0, Y0 = Y0, Z0 = Z0, SX = SX, SY = SY, SZ = SZ, M = (byte[])M.Clone(), T = (byte[])T.Clone(), Count = Count,
    };

    /// <summary>The voxels, padded by one on every side, as the mesher wants them (x fastest, then z, then y).</summary>
    public byte[] Padded()
    {
        int px = SX + 2, pz = SZ + 2;
        var g = new byte[px * (SY + 2) * pz];
        for (int y = 0; y < SY; y++)
            for (int z = 0; z < SZ; z++)
                for (int x = 0; x < SX; x++)
                    g[((y + 1) * pz + (z + 1)) * px + (x + 1)] = M[(y * SZ + z) * SX + x];
        return g;
    }
}

/// <summary>What a piece of dress is: a weapon or a tool held, or a shield on the forearm.</summary>
public enum DressKind : byte { Weapon, Shield }

/// <summary>
/// Something carried drawn finer than the body (<see cref="BodyPlan.DressSub"/> cells to a voxel, 1.6 cm): a blade with
/// its fuller and its edges, a guard, a wrapped grip; a shield's planks, rim and boss. It hangs on a part and turns with
/// it; the body the blows cut has it only as the half-voxels it fills.
/// </summary>
public sealed class Dress
{
    public BodyPart On;
    public DressKind Kind;
    /// <summary>Its cells, in figure coordinates as the part stands at rest.</summary>
    public PartGrid Grid;
    /// <summary>Its cells to a voxel: eight for what is held in the hand, four (as the body near) for what is as big as a troll's club.</summary>
    public int Sub = BodyPlan.DressSub;
    /// <summary>
    /// What is held, in figure voxels at rest: where the fist closes on it, its point (or its head), and the way its edge
    /// faces (the way it cuts as the arm comes down). A shield's grip is its middle, its tip the front of its boss, its
    /// edge the way its face looks.
    /// </summary>
    public (float X, float Y, float Z) Grip, Tip, Edge;
}

/// <summary>
/// The shape of one kind of body in one of its looks, with flesh inside and a skeleton in the flesh — a skull with the
/// brain in it, a spine, ribs round the heart, bones in the limbs thick enough to hide them. It is sculpted in cells of
/// 3 cm (<see cref="Fine"/>, as it is drawn near) and what it holds finer still (<see cref="Dress"/>); the body the blows
/// cut is in half-voxels (<see cref="Grid"/>), each one there where any of its cells is, of what most of them show.
/// Figure coordinates have the origin between the feet, +y up and +z the way it faces, so its right side (the hand with
/// the tool) is at −x; the joints are in voxels.
/// </summary>
public sealed class BodyPlan
{
    public const int Parts = 5, Sub = 2;
    /// <summary>Cells a half-voxel is sculpted in each way, and cells to a voxel: the body's, and its dress's.</summary>
    public const int K = 2, FineSub = Sub * K, DressSub = 8;
    /// <summary>The body the blows cut, the wounds carve and the game saves, in half-voxels.</summary>
    public readonly PartGrid[] Grid = new PartGrid[Parts];
    /// <summary>The same body as it is drawn near, in cells of 3 cm.</summary>
    public readonly PartGrid[] Fine = new PartGrid[Parts];
    /// <summary>Whose each half-voxel of <see cref="Grid"/> is: 0 the body's own, else 1 + the index of a piece of its dress.</summary>
    public readonly byte[][] Owner = new byte[Parts][];
    /// <summary>
    /// The half-voxels drawn at the middle distance: those the body's cells fill at least three eighths of (its dress's,
    /// a sixteenth), so that a rivet or a strand of hair a cell across is not drawn there as a block of a half-voxel.
    /// The blows cut all of <see cref="Grid"/>, filled however little.
    /// </summary>
    public readonly bool[][] Solid = new bool[Parts][];
    /// <summary>
    /// What a half-voxel shows drawn in half-voxels or in voxels, where that is not what the blows find in it (0 where
    /// it is): a shield's paint over the planks it is painted on, the haft going on into a fist where a knuckle has the
    /// half-voxel but too little of it to be drawn.
    /// </summary>
    public readonly byte[][] Shown = new byte[Parts][];
    public readonly List<Dress> Dress = new();
    /// <summary>Where each part turns, in figure voxels: the body at the hips, the arms at the shoulders, the legs at the hips.</summary>
    public readonly float[] JX = new float[Parts], JY = new float[Parts], JZ = new float[Parts];
    /// <summary>Voxels walked in one full stride; seconds per blow when working; how far forward the tool is carried at rest.</summary>
    public float Stride = 9, Chop = .75f, Carry;
    /// <summary>A shield on the off arm; four legs (the arms are front legs).</summary>
    public bool Shield, Beast;
    /// <summary>
    /// A rider on his horse: the trunk is the horse with the man in the saddle, the arms are the rider's, the right leg
    /// is both the horse's forelegs (turning at the shoulder) and the left both its hind legs.
    /// </summary>
    public bool Mounted;
    /// <summary>An engine of ours: its parts are timbers, ropes and what it throws, each moved by the view as the engine
    /// works (the beam, the counterweight, the sling, the stone), not a body with knees, elbows and a neck.</summary>
    public bool Machine;
    /// <summary>How far the off arm comes up in a fight (radians, up is negative): a round shield before the face, a tower shield hardly at all.</summary>
    public float Guard = -1.05f;
    /// <summary>The head's rows, in half-voxels; the top of the figure, in voxels.</summary>
    public int HeadY0, HeadY1, Top;
    /// <summary>
    /// A beast's neck, in half-voxels: what of its body lies before <see cref="NeckZ"/> and over <see cref="NeckY"/> is
    /// its neck with the head, and turns there, at the neck's root over the shoulders (none: int.MinValue).
    /// </summary>
    public int NeckZ = int.MinValue, NeckY;
    /// <summary>Half-voxels of brain and of heart it starts with.</summary>
    public int Brain, Heart;

    /// <summary>What it strikes, shoots or works with — in the right hand, or a bow in the left (null for a beast or an engine): its grip, point and edge.</summary>
    public Dress Weapon => Dress.Find(d => d.On == BodyPart.Main && d.Kind == DressKind.Weapon) ?? Dress.Find(d => d.Kind == DressKind.Weapon);
    /// <summary>The shield on the left forearm, if it has one.</summary>
    public Dress ShieldDress => Dress.Find(d => d.Kind == DressKind.Shield);

    /// <summary>
    /// A part as it is drawn in half-voxels or in voxels: <paramref name="now"/> (the part whole, or as its wounds have
    /// left it) with what each half-voxel shows (<see cref="Shown"/>) wherever a wound has not changed it.
    /// </summary>
    public PartGrid Seen(int part, PartGrid now)
    {
        var shown = Shown[part]; var was = Grid[part];
        if (shown == null || now == null || was == null || now.M.Length != shown.Length) return now;
        var g = new PartGrid { X0 = now.X0, Y0 = now.Y0, Z0 = now.Z0, SX = now.SX, SY = now.SY, SZ = now.SZ, M = (byte[])now.M.Clone(), T = now.T, Count = now.Count };
        for (int i = 0; i < shown.Length; i++)
            if (shown[i] != 0 && g.M[i] != 0 && g.M[i] == was.M[i]) g.M[i] = shown[i];
        return g;
    }
}

/// <summary>
/// The bodies of everyone in the valley: men, the horde's kinds and the beasts, each in a few looks (hair, skin, hats,
/// rags, helmets), so a crowd is not one figure repeated. A man is 15 voxels tall with his hat, a goblin 10, a troll 24.
/// </summary>
public static partial class Bodies
{
    /// <summary>The bodies sculpted so far: a body is sculpted once, on whatever thread first wants it (<see cref="Warm"/>).</summary>
    static readonly ConcurrentDictionary<(string, int), BodyPlan> Cache = new();

    /// <summary>How many looks a kind comes in.</summary>
    public static int Looks(UnitType t) => BodyKey(t) switch
    {
        "worker" => 8, "archer" => 6, "sword" => 6, "priest" => 3, "risen" => 2, "thrall" => 2, "angel" => 2, "shaman" => 2, "witch" => 2, "necro" => 2, "goblin" => 8, "sapper" => 4, "orc" => 6, "miner" => 4, "troll" => 3, "thief" => 3, "deer" => 2, "ram" => 2, "catapult" => 2, "mantlet" => 2, "tower" => 1, "stag" => 1, "bear" => 1,
        "spear" => 4, "shield" => 3, "halberd" => 3, "knight" => 3, "sling" => 4, "crossbow" => 3, "bombard" => 3, "hound" => 3, "rider" => 4, "horsearcher" => 3,
        "lancer" => 2, "tametroll" => 2, "giant" => 3, "alchemist" => 3, "hillgiant" => 2, "ouralchemist" => 3,
        "mangonel" => 2, "ballista" => 2, "trebuchet" => 2, "crew" => 3, "captain" => 3, "architect" => 2, "skeleton" => 2, "wolf" => 2,
        "bandit" => 3, "spider" => 2, "warg" => 2, "harpy" => 2, "cultist" => 2, "centaur" => 3, "ghoul" => 2, "ogre" => 2, "wildtroll" => 2, "wraith" => 2,
        "minotaur" => 2, "griffin" => 2, "basilisk" => 2, "treant" => 2, "golem" => 2, "firespirit" => 1, "wyvern" => 2, "dragon" => 2, _ => 1,
    };

    /// <summary>A look for a unit, from its number, so it keeps it.</summary>
    public static int LookOf(UnitType t, int id) => (int)(Hash.H3(id, 7, 13) * Looks(t)) % Looks(t);

    public static BodyPlan Plan(UnitType t, int look)
    {
        look = Math.Clamp(look, 0, Looks(t) - 1);
        if (Cache.TryGetValue((t.Key, look), out var p)) return p;
        // a body sculpted on two threads at once is kept the once: the one first in
        return Cache.GetOrAdd((t.Key, look), Make(t, look));
    }

    /// <summary>
    /// Every look of these kinds sculpted ahead (from a worker thread while the valley is first shown): a body takes some
    /// tens of milliseconds to sculpt, and a kind first seen, or a worker first come to its work, held up its frame for it.
    /// </summary>
    public static void Warm(IEnumerable<UnitType> kinds)
    {
        foreach (var t in kinds)
            for (int l = 0; l < Looks(t); l++) Plan(t, l);
    }

    /// <summary>
    /// A body built afresh, in <paramref name="k"/> cells to a half-voxel, with the finer work or without it: built
    /// without it, the half-voxels of a body sculpted in two cells to one are the very half-voxels of the same body
    /// sculpted in one (the bench holds them to that), so the body the blows cut is what it always was.
    /// </summary>
    public static BodyPlan Sculpted(UnitType t, int look, int k, bool detail)
    {
        int k0 = _k; bool d0 = _detail;
        _k = k; _detail = detail;
        try { return Make(t, Math.Clamp(look, 0, Looks(t) - 1)); }
        finally { _k = k0; _detail = d0; }
    }

    [ThreadStatic] static int _k;
    [ThreadStatic] static bool _detail;

    /// <summary>The key a kind's body is sculpted by: a creature gone over to us is the creature still.</summary>
    static string BodyKey(UnitType t) => t.Key.EndsWith("_ours") ? t.Key[..^5] : t.Key;

    static BodyPlan Make(UnitType t, int look) => BodyKey(t) switch
    {
        "worker" => Worker(look),
        "archer" => Archer(look),
        "sword" => Swordsman(look),
        "priest" => Priest(look),
        "architect" => Architect(look),
        "spear" => Spearman(look),
        "shield" => Shieldbearer(look),
        "halberd" => Halberdier(look),
        "knight" => Knight(look),
        "sling" => Slinger(look),
        "crossbow" => Crossbowman(look),
        "bombard" => Bombardier(look),
        "ouralchemist" => OurAlchemist(look),
        "hound" => Hound(look),
        "rider" => Rider(look),
        "horsearcher" => HorseArcher(look),
        "lancer" => Lancer(look),
        "tametroll" => TameTroll(look),
        "giant" => Giant(look),
        "risen" => Bones(look, Mat.SoulFire, Mat.Rag),
        "angel" => HostAngel(look),
        "thrall" => Bones(look, Mat.Ember, Mat.ClothRed),
        "skeleton" => Bones(look, Mat.Brew, Mat.Rag),
        "wolf" => Wolf(look),
        "goblin" => Goblin(look),
        "orc" => Orc(look),
        "miner" => Miner(look),
        "troll" => Troll(look),
        "hillgiant" => HillGiant(look),
        "sapper" => Sapper(look),
        "thief" => Thief(look),
        "shaman" => Shaman(look),
        "witch" => Witch(look),
        "necro" => Necromancer(look),
        "alchemist" => Alchemist(look),
        "ram" => RamPlan(look),
        "catapult" => CatapultPlan(look),
        "mantlet" => MantletPlan(look),
        "tower" => TowerPlan(),
        "mangonel" => MangonelPlan(look),
        "ballista" => BallistaPlan(look),
        "trebuchet" => TrebuchetPlan(look),
        "crew" => Crewman(look),
        "captain" => Captain(look),
        "deer" => Deer(look),
        "stag" => Deer(0, white: true),
        "bear" => BearPlan(),
        "boar" => Boar(),
        "hare" => Hare(),
        "bandit" => Bandit(look),
        "spider" => Spider(look),
        "warg" => Warg(look),
        "harpy" => Harpy(look),
        "cultist" => Cultist(look),
        "centaur" => Centaur(look),
        "ghoul" => Ghoul(look),
        "ogre" => Ogre(look),
        "wildtroll" => WildTroll(look),
        "wraith" => Wraith(look),
        "minotaur" => Minotaur(look),
        "griffin" => Griffin(look),
        "basilisk" => Basilisk(look),
        "treant" => Treant(look),
        "golem" => Golem(look),
        "firespirit" => FireSpirit(look),
        "wyvern" => Wyvern(look),
        "dragon" => Dragon(look),
        _ => Worker(0),
    };

    // ---------------------------------------------------------------- proportions

    const BodyPart Body = BodyPart.Body, Main = BodyPart.Main, Off = BodyPart.Off, LegR = BodyPart.LegR, LegL = BodyPart.LegL;
    const float H = BodyPlan.Sub;

    /// <summary>Sizes of a body in half-voxels; widths are halves, measured from the middle.</summary>
    sealed class Build
    {
        public int Leg = 10, LegW = 3, LegD = 4, Gap = 1, Boot = 3;
        public int Torso = 10, Chest = 4, ChestD = 4;
        public int Head = 8, HeadW = 4, HeadD = 8, HeadZ = -4;
        public int Arm = 10, ArmW = 2, ArmD = 4, Hand = 3;
        /// <summary>0 hair all over, 1 cropped (top and back), 2 long down the back, 3 none.</summary>
        public int HairStyle;
        /// <summary>Half-voxels the eyes, nose, mouth and ears sit higher than on a man's head: a giant's is taller.</summary>
        public int FaceUp;
        public byte Skin = Mat.SkinHuman, Shirt = Mat.Linen, Trousers = Mat.ClothDark, Boots = Mat.Leather, Belt = Mat.Leather, Hair = Mat.Hair,
            Eye = Mat.Eye, Brow, Mouth = Mat.Hair, Sleeve, Cuff;

        public int Waist => Leg;
        public int Neck => Leg + Torso;
        public int Crown => Leg + Torso + Head;
        public int Z0 => -(ChestD / 2);
        public int Z1 => ChestD - ChestD / 2;
        public int HZ1 => HeadZ + HeadD;
        public int FaceZ => HZ1 - 1;
        public int LegX0 => -(Gap + LegW);
        public int LegX1 => -Gap;
        /// <summary>Right arm columns, and where its hand is.</summary>
        public int ArmX0 => -Chest - ArmW;
        public int HandY => Neck - Arm;
        public int ArmZ0 => -(ArmD / 2);
        public int ArmZ1 => ArmD - ArmD / 2;
        /// <summary>The column and the row a one-voxel tool sits in, in the middle of the right fist.</summary>
        public int FistX => ArmX0 + ArmW / 2;
        public int FistY => HandY + Hand / 2;
    }

    /// <summary>
    /// Cells of one fineness, part by part (a piece of dress keeps its own, all as part 0): what each is made of and what
    /// it is. Boxes, balls and rods are given in half-voxels, fractions of them for the finer work; a cell is in a box when
    /// its middle is.
    /// </summary>
    sealed class Cells
    {
        /// <summary>Cells to a half-voxel each way.</summary>
        public readonly int R;
        readonly Dictionary<long, ushort> _v = new();
        public Cells(int r) { R = r; }

        const int Off = 1 << 15;
        static long Key(int p, int x, int y, int z) => (long)p << 48 | (long)(x + Off) << 32 | (long)(y + Off) << 16 | (long)(z + Off);
        static void Unkey(long k, out int p, out int x, out int y, out int z)
        {
            p = (int)(k >> 48); x = (int)(k >> 32 & 0xFFFF) - Off; y = (int)(k >> 16 & 0xFFFF) - Off; z = (int)(k & 0xFFFF) - Off;
        }

        // how far each part's cells reach (what was taken away included), so that recolouring a box keeps to them
        readonly int[] _lo = { int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue },
            _hi = { int.MinValue, int.MinValue, int.MinValue, int.MinValue, int.MinValue, int.MinValue, int.MinValue, int.MinValue, int.MinValue, int.MinValue, int.MinValue, int.MinValue, int.MinValue, int.MinValue, int.MinValue };

        /// <summary>Narrows a box's cells (lo inclusive, hi exclusive) to where a part has any: false when it has none there.</summary>
        public bool Clip(int p, ref int x0, ref int x1, ref int y0, ref int y1, ref int z0, ref int z1)
        {
            int b = p * 3;
            if (_lo[b] == int.MaxValue) return false;
            x0 = Math.Max(x0, _lo[b]); x1 = Math.Min(x1, _hi[b] + 1);
            y0 = Math.Max(y0, _lo[b + 1]); y1 = Math.Min(y1, _hi[b + 1] + 1);
            z0 = Math.Max(z0, _lo[b + 2]); z1 = Math.Min(z1, _hi[b + 2] + 1);
            return x0 < x1 && y0 < y1 && z0 < z1;
        }

        public bool Has(int p, int x, int y, int z) => _v.ContainsKey(Key(p, x, y, z));
        public bool Get(int p, int x, int y, int z, out byte m, out Tissue t)
        {
            bool has = _v.TryGetValue(Key(p, x, y, z), out ushort v);
            m = (byte)v; t = (Tissue)(v >> 8 & 0x7F);
            return has;
        }

        /// <summary>
        /// Cells set while this is on are the finer work's (a rivet, a stripe of paint, the glint in an eye): a half-voxel
        /// takes its colour from the plain sculpting's cells in it before theirs, so the middle distance shows what it
        /// always showed and no stray spots of the finer work.
        /// </summary>
        public bool Marking;
        const int Finer = 0x8000;
        public void Set(int p, int x, int y, int z, byte m, Tissue t)
        {
            if (m == 0) { _v.Remove(Key(p, x, y, z)); return; }
            _v[Key(p, x, y, z)] = (ushort)(m | (int)t << 8 | (Marking ? Finer : 0));
            int b = p * 3;
            if (x < _lo[b]) _lo[b] = x; if (x > _hi[b]) _hi[b] = x;
            if (y < _lo[b + 1]) _lo[b + 1] = y; if (y > _hi[b + 1]) _hi[b + 1] = y;
            if (z < _lo[b + 2]) _lo[b + 2] = z; if (z > _hi[b + 2]) _hi[b + 2] = z;
        }
        public void Remove(int p, int x, int y, int z) => _v.Remove(Key(p, x, y, z));

        /// <summary>The first and the one past the last cell whose middles lie from a to b (half-voxels) along an axis.</summary>
        public int Lo(float a) => (int)MathF.Ceiling(a * R - .5f - 1e-4f);
        public int Hi(float b) => (int)MathF.Ceiling(b * R - .5f - 1e-4f);
        /// <summary>A cell's middle in half-voxels.</summary>
        public float Mid(int c) => (c + .5f) / R;

        public void Box(int p, float x0, float y0, float z0, float x1, float y1, float z1, byte m, Tissue t)
        {
            for (int y = Lo(y0), ye = Hi(y1); y < ye; y++)
                for (int z = Lo(z0), ze = Hi(z1); z < ze; z++)
                    for (int x = Lo(x0), xe = Hi(x1); x < xe; x++) Set(p, x, y, z, m, t);
        }

        /// <summary>Takes out what is in a box (and in its mirror across the middle, when <paramref name="both"/>).</summary>
        public void Cut(int p, float x0, float y0, float z0, float x1, float y1, float z1, bool both = false)
        {
            int c0 = Lo(x0), c1 = Hi(x1), ya = Lo(y0), yb = Hi(y1), za = Lo(z0), zb = Hi(z1);
            // the box's columns, and with both its mirror's too: the cells -1 - x
            int xa = both ? Math.Min(c0, -c1) : c0, xb = both ? Math.Max(c1, 1 - c0) : c1;
            if (!Clip(p, ref xa, ref xb, ref ya, ref yb, ref za, ref zb)) return;
            for (int y = ya; y < yb; y++)
                for (int z = za; z < zb; z++)
                    for (int x = xa; x < xb; x++)
                        if (x >= c0 && x < c1 || both && -1 - x >= c0 && -1 - x < c1) _v.Remove(Key(p, x, y, z));
        }

        /// <summary>Recolours what is already there in a box; its tissue stays unless another is given.</summary>
        public void Paint(int p, float x0, float y0, float z0, float x1, float y1, float z1, byte m, Tissue? t = null)
        {
            int xa = Lo(x0), xb = Hi(x1), ya = Lo(y0), yb = Hi(y1), za = Lo(z0), zb = Hi(z1);
            if (!Clip(p, ref xa, ref xb, ref ya, ref yb, ref za, ref zb)) return;
            for (int y = ya; y < yb; y++)
                for (int z = za; z < zb; z++)
                    for (int x = xa; x < xb; x++)
                        if (Get(p, x, y, z, out _, out var was)) Set(p, x, y, z, m, t ?? was);
        }

        /// <summary>Is a cell open to the air within <paramref name="depth"/> cells of it, any of the six ways?</summary>
        public bool Shows(int p, int x, int y, int z, int depth)
        {
            for (int d = 1; d <= depth; d++)
                if (!Has(p, x + d, y, z) || !Has(p, x - d, y, z) || !Has(p, x, y + d, z) || !Has(p, x, y - d, z) || !Has(p, x, y, z + d) || !Has(p, x, y, z - d))
                    return true;
            return false;
        }

        /// <summary>
        /// Recolours only what shows inside a box — cells within <paramref name="depth"/> of the air — as a hood or a helmet
        /// covers a head; what lies in <paramref name="but"/> (a face) is left as it is.
        /// </summary>
        public void Coat(int p, float x0, float y0, float z0, float x1, float y1, float z1, int depth, byte m, Tissue? t,
            (float x0, float y0, float z0, float x1, float y1, float z1)? but)
        {
            var shows = new List<(int, int, int, Tissue)>();
            int xa = Lo(x0), xb = Hi(x1), ya = Lo(y0), yb = Hi(y1), za = Lo(z0), zb = Hi(z1);
            if (!Clip(p, ref xa, ref xb, ref ya, ref yb, ref za, ref zb)) return;
            for (int y = ya; y < yb; y++)
                for (int z = za; z < zb; z++)
                    for (int x = xa; x < xb; x++)
                    {
                        if (!Get(p, x, y, z, out _, out var was)) continue;
                        if (but is { } e && Mid(x) >= e.x0 && Mid(x) < e.x1 && Mid(y) >= e.y0 && Mid(y) < e.y1 && Mid(z) >= e.z0 && Mid(z) < e.z1) continue;
                        if (Shows(p, x, y, z, depth)) shows.Add((x, y, z, t ?? was));
                    }
            foreach (var (x, y, z, tt) in shows) Set(p, x, y, z, m, tt);
        }

        /// <summary>An ellipsoid round (cx, cy, cz) with these half-widths, filled (or only recoloured where it meets what is there).</summary>
        public void Ball(int p, float cx, float cy, float cz, float rx, float ry, float rz, byte m, Tissue t, bool paint = false)
        {
            for (int y = Lo(cy - ry), ye = Hi(cy + ry); y < ye; y++)
                for (int z = Lo(cz - rz), ze = Hi(cz + rz); z < ze; z++)
                    for (int x = Lo(cx - rx), xe = Hi(cx + rx); x < xe; x++)
                    {
                        float dx = (Mid(x) - cx) / rx, dy = (Mid(y) - cy) / ry, dz = (Mid(z) - cz) / rz;
                        if (dx * dx + dy * dy + dz * dz > 1) continue;
                        if (!paint) Set(p, x, y, z, m, t);
                        else if (Get(p, x, y, z, out _, out var was)) Set(p, x, y, z, m, was);
                    }
        }

        /// <summary>A rod from a to b, as thick as r either side of its line (a capsule), filled.</summary>
        public void Rod(int p, float ax, float ay, float az, float bx, float by, float bz, float r, byte m, Tissue t)
        {
            float dx = bx - ax, dy = by - ay, dz = bz - az, ll = dx * dx + dy * dy + dz * dz;
            for (int y = Lo(MathF.Min(ay, by) - r), ye = Hi(MathF.Max(ay, by) + r); y < ye; y++)
                for (int z = Lo(MathF.Min(az, bz) - r), ze = Hi(MathF.Max(az, bz) + r); z < ze; z++)
                    for (int x = Lo(MathF.Min(ax, bx) - r), xe = Hi(MathF.Max(ax, bx) + r); x < xe; x++)
                    {
                        float px = Mid(x) - ax, py = Mid(y) - ay, pz = Mid(z) - az;
                        float k = ll < 1e-6f ? 0 : Math.Clamp((px * dx + py * dy + pz * dz) / ll, 0, 1);
                        float qx = px - dx * k, qy = py - dy * k, qz = pz - dz * k;
                        if (qx * qx + qy * qy + qz * qz <= r * r) Set(p, x, y, z, m, t);
                    }
        }

        /// <summary>
        /// A line one cell thick from a to b, every cell of it touching the next by a face (a cord, a strap, a thin
        /// horn), filled — or only recoloured where it meets what is there.
        /// </summary>
        public void Line(int p, float ax, float ay, float az, float bx, float by, float bz, byte m, Tissue t, bool paint = false)
        {
            float len = MathF.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay) + (bz - az) * (bz - az));
            int n = Math.Max(1, (int)MathF.Ceiling(len * R * 4));
            int lx = int.MinValue, ly = 0, lz = 0;
            for (int i = 0; i <= n; i++)
            {
                float k = i / (float)n;
                int x = (int)MathF.Floor((ax + (bx - ax) * k) * R), y = (int)MathF.Floor((ay + (by - ay) * k) * R), z = (int)MathF.Floor((az + (bz - az) * k) * R);
                if (lx != int.MinValue)
                {
                    // one way at a time, so the cells hold together by their faces
                    while (lx != x || ly != y || lz != z)
                    {
                        if (lx != x) lx += Math.Sign(x - lx);
                        else if (ly != y) ly += Math.Sign(y - ly);
                        else lz += Math.Sign(z - lz);
                        Put(lx, ly, lz);
                    }
                }
                else { lx = x; ly = y; lz = z; Put(x, y, z); }
            }
            void Put(int x, int y, int z)
            {
                if (!paint) Set(p, x, y, z, m, t);
                else if (Get(p, x, y, z, out _, out var was)) Set(p, x, y, z, m, was);
            }
        }

        /// <summary>The cells of one part as a block (null when it has none), in its own fineness.</summary>
        public PartGrid Grid(int part) => Grid(part, out _);

        /// <summary>The same, and which of its cells are the finer work's.</summary>
        public PartGrid Grid(int part, out bool[] finer)
        {
            finer = null;
            int x0 = int.MaxValue, y0 = int.MaxValue, z0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue, z1 = int.MinValue;
            foreach (var k in _v.Keys)
            {
                Unkey(k, out int p, out int x, out int y, out int z);
                if (p != part) continue;
                x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); z0 = Math.Min(z0, z);
                x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); z1 = Math.Max(z1, z);
            }
            if (x0 == int.MaxValue) return null;
            var g = new PartGrid { X0 = x0, Y0 = y0, Z0 = z0, SX = x1 - x0 + 1, SY = y1 - y0 + 1, SZ = z1 - z0 + 1 };
            g.M = new byte[g.SX * g.SY * g.SZ];
            g.T = new byte[g.M.Length];
            finer = new bool[g.M.Length];
            foreach (var (k, v) in _v)
            {
                Unkey(k, out int p, out int x, out int y, out int z);
                if (p != part) continue;
                int i = g.Index(x, y, z);
                g.M[i] = (byte)v; g.T[i] = (byte)(v >> 8 & 0x7F); finer[i] = (v & Finer) != 0;
                g.Count++;
            }
            return g;
        }

        /// <summary>Takes away all of a part.</summary>
        public void Clear(int part)
        {
            var gone = new List<long>();
            foreach (var k in _v.Keys) if (k >> 48 == part) gone.Add(k);
            foreach (var k in gone) _v.Remove(k);
        }
    }

    /// <summary>
    /// A piece of dress in the sculpting: its cells (four to a half-voxel, or two), the part it hangs on, what it is to the blows
    /// (a weapon is not cut, a shield is armour), and for what is held its grip, its point and its edge, in half-voxels.
    /// Everything given in half-voxels, as for the body.
    /// </summary>
    sealed class Gear
    {
        public readonly Cells C;
        public Gear(int perHalf) { C = new Cells(perHalf); }
        public BodyPart On;
        public DressKind Kind;
        public Tissue Tissue;
        public (float X, float Y, float Z) Grip, Tip, Edge = (0, -1, 0);

        public void Box(float x0, float y0, float z0, float x1, float y1, float z1, byte m) => C.Box(0, x0, y0, z0, x1, y1, z1, m, Tissue);
        public void Cut(float x0, float y0, float z0, float x1, float y1, float z1) => C.Cut(0, x0, y0, z0, x1, y1, z1);
        public void Paint(float x0, float y0, float z0, float x1, float y1, float z1, byte m) => C.Paint(0, x0, y0, z0, x1, y1, z1, m);
        public void Ball(float cx, float cy, float cz, float rx, float ry, float rz, byte m, bool paint = false) => C.Ball(0, cx, cy, cz, rx, ry, rz, m, Tissue, paint);
        public void Rod(float ax, float ay, float az, float bx, float by, float bz, float r, byte m) => C.Rod(0, ax, ay, az, bx, by, bz, r, m, Tissue);
        public void Line(float ax, float ay, float az, float bx, float by, float bz, byte m, bool paint = false) => C.Line(0, ax, ay, az, bx, by, bz, m, Tissue, paint);
        /// <summary>One cell, by its own coordinates (<see cref="Cells.R"/> to a half-voxel).</summary>
        public void Dot(int x, int y, int z, byte m) => C.Set(0, x, y, z, m, Tissue);
        public void Remove(int x, int y, int z) => C.Remove(0, x, y, z);
        public bool Has(int x, int y, int z) => C.Has(0, x, y, z);
        /// <summary>A cell's own coordinate from half-voxels (the cell whose middle is nearest).</summary>
        public int At(float v) => (int)MathF.Floor(v * C.R);

        /// <summary>The cells from (x0, y0, z0) up to (x1, y1, z1), not including those, by their own coordinates.</summary>
        public void Fill(int x0, int y0, int z0, int x1, int y1, int z1, byte m)
        {
            for (int y = y0; y < y1; y++) for (int z = z0; z < z1; z++) for (int x = x0; x < x1; x++) Dot(x, y, z, m);
        }

        /// <summary>
        /// A grip or a haft along z, w cells square from (x, y), from z0 up to z1, wound with a strap in two shades that
        /// climbs the faces as it goes round.
        /// </summary>
        public void Wound(int x, int y, int z0, int z1, int w, byte a, byte b)
        {
            for (int z = z0; z < z1; z++) for (int dy = 0; dy < w; dy++) for (int dx = 0; dx < w; dx++)
                Dot(x + dx, y + dy, z, ((z + dx + dy) >> 1 & 1) == 0 ? a : b);
        }

        /// <summary>A disc r cells round (cy, cz) in the y–z plane, w cells thick from x: a wheel of a pommel, a rondel.</summary>
        public void Wheel(int x, int w, float cy, float cz, float r, byte m)
        {
            for (int z = (int)MathF.Floor(cz - r); z <= cz + r; z++)
                for (int y = (int)MathF.Floor(cy - r); y <= cy + r; y++)
                    if ((y + .5f - cy) * (y + .5f - cy) + (z + .5f - cz) * (z + .5f - cz) <= r * r)
                        for (int dx = 0; dx < w; dx++) Dot(x + dx, y, z, m);
        }

        /// <summary>A disc r cells round (cx, cy) in the x–y plane, w cells thick from z: a vamplate, a shield's boss in layers.</summary>
        public void Plate(int z, int w, float cx, float cy, float r, byte m)
        {
            for (int y = (int)MathF.Floor(cy - r); y <= cy + r; y++)
                for (int x = (int)MathF.Floor(cx - r); x <= cx + r; x++)
                    if ((x + .5f - cx) * (x + .5f - cx) + (y + .5f - cy) * (y + .5f - cy) <= r * r)
                        for (int dz = 0; dz < w; dz++) Dot(x, y, z + dz, m);
        }

        /// <summary>Is the cell there, and of this?</summary>
        public bool Is(int x, int y, int z, byte m) => C.Get(0, x, y, z, out var have, out _) && have == m;
    }
}
