using System;
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
    /// <summary>How far the off arm comes up in a fight (radians, up is negative): a round shield before the face, a tower shield hardly at all.</summary>
    public float Guard = -1.05f;
    /// <summary>The head's rows, in half-voxels; the top of the figure, in voxels.</summary>
    public int HeadY0, HeadY1, Top;
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
public static class Bodies
{
    static readonly Dictionary<(string, int), BodyPlan> Cache = new();

    /// <summary>How many looks a kind comes in.</summary>
    public static int Looks(UnitType t) => t.Key switch
    {
        "worker" => 8, "archer" => 6, "sword" => 6, "priest" => 3, "risen" => 2, "thrall" => 2, "shaman" => 2, "witch" => 2, "necro" => 2, "goblin" => 8, "sapper" => 4, "orc" => 6, "miner" => 4, "troll" => 3, "thief" => 3, "deer" => 2, "ram" => 2, "catapult" => 2, "mantlet" => 2, "tower" => 1, "stag" => 1, "bear" => 1,
        "spear" => 4, "shield" => 3, "halberd" => 3, "knight" => 3, "sling" => 4, "crossbow" => 3, "bombard" => 3, "hound" => 3, "rider" => 4, "horsearcher" => 3,
        "lancer" => 2, "tametroll" => 2, "giant" => 3, _ => 1,
    };

    /// <summary>A look for a unit, from its number, so it keeps it.</summary>
    public static int LookOf(UnitType t, int id) => (int)(Hash.H3(id, 7, 13) * Looks(t)) % Looks(t);

    public static BodyPlan Plan(UnitType t, int look)
    {
        look = Math.Clamp(look, 0, Looks(t) - 1);
        if (Cache.TryGetValue((t.Key, look), out var p)) return p;
        p = Make(t, look);
        Cache[(t.Key, look)] = p;
        return p;
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

    static BodyPlan Make(UnitType t, int look) => t.Key switch
    {
        "worker" => Worker(look),
        "archer" => Archer(look),
        "sword" => Swordsman(look),
        "priest" => Priest(look),
        "spear" => Spearman(look),
        "shield" => Shieldbearer(look),
        "halberd" => Halberdier(look),
        "knight" => Knight(look),
        "sling" => Slinger(look),
        "crossbow" => Crossbowman(look),
        "bombard" => Bombardier(look),
        "hound" => Hound(look),
        "rider" => Rider(look),
        "horsearcher" => HorseArcher(look),
        "lancer" => Lancer(look),
        "tametroll" => TameTroll(look),
        "giant" => Giant(look),
        "risen" => Bones(look, Mat.SoulFire, Mat.Rag),
        "thrall" => Bones(look, Mat.Ember, Mat.ClothRed),
        "goblin" => Goblin(look),
        "orc" => Orc(look),
        "miner" => Miner(look),
        "troll" => Troll(look),
        "sapper" => Sapper(look),
        "thief" => Thief(look),
        "shaman" => Shaman(look),
        "witch" => Witch(look),
        "necro" => Necromancer(look),
        "ram" => RamPlan(look),
        "catapult" => CatapultPlan(look),
        "mantlet" => MantletPlan(look),
        "tower" => TowerPlan(),
        "deer" => Deer(look),
        "stag" => Deer(0, white: true),
        "bear" => BearPlan(),
        "boar" => Boar(),
        "hare" => Hare(),
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

    /// <summary>
    /// What is sculpted, part by part: the body in cells of 3 cm (K to a half-voxel each way) and its dress finer still.
    /// The plain calls in whole half-voxels (<see cref="Box"/>, <see cref="Pair"/>, <see cref="Dot"/>, <see cref="Cut"/>,
    /// <see cref="Paint"/>, <see cref="Coat"/>, <see cref="Rim"/>, <see cref="Round"/>, <see cref="Flat"/> and
    /// <see cref="Disc"/>) fill whole blocks of cells, as they always filled half-voxels; the finer work — the F… calls and
    /// the dress — is done only when <see cref="Detail"/> is on, and round shapes are then cut in cells, not half-voxels.
    /// </summary>
    sealed class Sculpt
    {
        /// <summary>Cells to a half-voxel each way.</summary>
        public readonly int K;
        /// <summary>The finer work is done: off, only the body as it was sculpted in half-voxels is.</summary>
        public readonly bool Detail;
        readonly Cells _c;
        readonly List<Gear> _gear = new();

        public Sculpt()
        {
            K = _k > 0 ? _k : BodyPlan.K;
            Detail = _k > 0 ? _detail : true;
            _c = new Cells(K);
        }

        public void Box(BodyPart p, int x0, int y0, int z0, int x1, int y1, int z1, byte m, Tissue t = Tissue.Skin) => _c.Box((int)p, x0, y0, z0, x1, y1, z1, m, t);

        /// <summary>A box on the right side (x below zero) and its mirror on the left.</summary>
        public void Pair(BodyPart right, BodyPart left, int x0, int y0, int z0, int x1, int y1, int z1, byte m, Tissue t = Tissue.Skin)
        {
            Box(right, x0, y0, z0, x1, y1, z1, m, t);
            Box(left, -x1, y0, z0, -x0, y1, z1, m, t);
        }

        public void Dot(BodyPart p, int x, int y, int z, byte m, Tissue t = Tissue.Skin) => _c.Box((int)p, x, y, z, x + 1, y + 1, z + 1, m, t);

        /// <summary>Takes out what is in a box (and in its mirror, when <paramref name="both"/>).</summary>
        public void Cut(BodyPart p, int x0, int y0, int z0, int x1, int y1, int z1, bool both = false) => _c.Cut((int)p, x0, y0, z0, x1, y1, z1, both);

        /// <summary>Recolours what is already there in a box; its tissue stays unless another is given.</summary>
        public void Paint(BodyPart p, int x0, int y0, int z0, int x1, int y1, int z1, byte m, Tissue? t = null) => _c.Paint((int)p, x0, y0, z0, x1, y1, z1, m, t);

        bool Has(BodyPart p, int x, int y, int z) => _c.Has((int)p, x, y, z);

        /// <summary>Takes away all of a part: a man's legs, for one who sits a horse.</summary>
        public void Clear(BodyPart p) => _c.Clear((int)p);

        /// <summary>
        /// Recolours only what shows inside a box — what lies within a half-voxel of the air — as a hood or a helmet covers
        /// a head; what lies in <paramref name="but"/> (a face) is left as it is.
        /// </summary>
        public void Coat(BodyPart p, int x0, int y0, int z0, int x1, int y1, int z1, byte m, Tissue? t = null,
            (int x0, int y0, int z0, int x1, int y1, int z1)? but = null)
            => _c.Coat((int)p, x0, y0, z0, x1, y1, z1, K, m, t, but is { } e ? (e.x0, e.y0, e.z0, e.x1, e.y1, e.z1) : null);

        /// <summary>
        /// A disc of a half-voxel's thickness lying flat at row y (a hat's brim, the crown of a cap), round the middle of
        /// the figure, or round (cx, cz).
        /// </summary>
        public void Flat(BodyPart p, int y, float r, byte m, Tissue t = Tissue.Skin, float cz = 0, float cx = 0)
        {
            int n = (int)Math.Ceiling(r) + 1;
            if (!Detail)
            {
                for (int z = (int)cz - n; z <= cz + n; z++)
                    for (int x = (int)cx - n; x <= cx + n; x++)
                        if ((x + .5f - cx) * (x + .5f - cx) + (z + .5f - cz) * (z + .5f - cz) <= r * r) Dot(p, x, y, z, m, t);
                return;
            }
            // cut round in cells, not in half-voxels
            for (int z = _c.Lo(cz - r), ze = _c.Hi(cz + r); z < ze; z++)
                for (int x = _c.Lo(cx - r), xe = _c.Hi(cx + r); x < xe; x++)
                {
                    float dx = _c.Mid(x) - cx, dz = _c.Mid(z) - cz;
                    if (dx * dx + dz * dz > r * r) continue;
                    for (int yy = y * K; yy < (y + 1) * K; yy++) _c.Set((int)p, x, yy, z, m, t);
                }
        }

        /// <summary>A disc of a half-voxel's thickness standing in the x–y plane at depth z, round (cx, cy), coloured by where in it a cell is.</summary>
        public void Disc(BodyPart p, int z, float cx, float cy, float r, Func<float, float, float, byte> m, Tissue t)
        {
            int n = (int)Math.Ceiling(r) + 1;
            if (!Detail)
            {
                for (int y = (int)cy - n; y <= cy + n; y++)
                    for (int x = (int)cx - n; x <= cx + n; x++)
                    {
                        float d = MathF.Sqrt((x + .5f - cx) * (x + .5f - cx) + (y + .5f - cy) * (y + .5f - cy));
                        if (d <= r) Dot(p, x, y, z, m(x + .5f, y + .5f, d), t);
                    }
                return;
            }
            for (int y = _c.Lo(cy - r), ye = _c.Hi(cy + r); y < ye; y++)
                for (int x = _c.Lo(cx - r), xe = _c.Hi(cx + r); x < xe; x++)
                {
                    float d = MathF.Sqrt((_c.Mid(x) - cx) * (_c.Mid(x) - cx) + (_c.Mid(y) - cy) * (_c.Mid(y) - cy));
                    if (d > r) continue;
                    for (int zz = z * K; zz < (z + 1) * K; zz++) _c.Set((int)p, x, y, zz, m(_c.Mid(x), _c.Mid(y), d), t);
                }
        }

        /// <summary>A ring one half-voxel wide round a box's outline at row y, standing out of it by one: a rim, a band.</summary>
        public void Rim(BodyPart p, int y, int x0, int z0, int x1, int z1, byte m, Tissue t = Tissue.Skin)
        {
            for (int z = z0 - 1; z <= z1; z++)
                for (int x = x0 - 1; x <= x1; x++)
                {
                    bool inside = x >= x0 && x < x1 && z >= z0 && z < z1, corner = (x == x0 - 1 || x == x1) && (z == z0 - 1 || z == z1);
                    if (!inside && !corner) Dot(p, x, y, z, m, t);
                }
        }

        /// <summary>Cuts the four upright edges off a box, and the rim of its top: a head, rounded.</summary>
        public void Round(BodyPart p, int x0, int y0, int z0, int x1, int y1, int z1, bool top = true)
        {
            for (int y = y0; y < y1; y++)
                foreach (int x in new[] { x0, x1 - 1 })
                    foreach (int z in new[] { z0, z1 - 1 })
                        Cut(p, x, y, z, x + 1, y + 1, z + 1);
            if (!top) return;
            for (int z = z0; z < z1; z++)
                for (int x = x0; x < x1; x++)
                    if (x == x0 || x == x1 - 1 || z == z0 || z == z1 - 1) Cut(p, x, y1 - 1, z, x + 1, y1, z + 1);
        }

        // ---------------------------------------------------------------- the finer work, in fractions of a half-voxel

        public void FBox(BodyPart p, float x0, float y0, float z0, float x1, float y1, float z1, byte m, Tissue t = Tissue.Skin)
        {
            if (Detail) Finer(() => _c.Box((int)p, x0, y0, z0, x1, y1, z1, m, t));
        }

        /// <summary>A box on the right side and its mirror on the left (the same part, or the other arm or leg).</summary>
        public void FPair(BodyPart right, BodyPart left, float x0, float y0, float z0, float x1, float y1, float z1, byte m, Tissue t = Tissue.Skin)
        {
            FBox(right, x0, y0, z0, x1, y1, z1, m, t);
            FBox(left, -x1, y0, z0, -x0, y1, z1, m, t);
        }

        public void FCut(BodyPart p, float x0, float y0, float z0, float x1, float y1, float z1, bool both = false)
        {
            if (Detail) _c.Cut((int)p, x0, y0, z0, x1, y1, z1, both);
        }

        public void FPaint(BodyPart p, float x0, float y0, float z0, float x1, float y1, float z1, byte m, Tissue? t = null)
        {
            if (Detail) Finer(() => _c.Paint((int)p, x0, y0, z0, x1, y1, z1, m, t));
        }

        public void FPaintPair(BodyPart right, BodyPart left, float x0, float y0, float z0, float x1, float y1, float z1, byte m, Tissue? t = null)
        {
            FPaint(right, x0, y0, z0, x1, y1, z1, m, t);
            FPaint(left, -x1, y0, z0, -x0, y1, z1, m, t);
        }

        /// <summary>Recolours only the cells open to the air in a box (paint on the skin, a pattern on cloth), one cell deep.</summary>
        public void FSkin(BodyPart p, float x0, float y0, float z0, float x1, float y1, float z1, byte m, Tissue? t = null)
        {
            if (Detail) Finer(() => _c.Coat((int)p, x0, y0, z0, x1, y1, z1, 1, m, t, null));
        }

        public void FBall(BodyPart p, float cx, float cy, float cz, float rx, float ry, float rz, byte m, Tissue t = Tissue.Skin, bool paint = false)
        {
            if (Detail) Finer(() => _c.Ball((int)p, cx, cy, cz, rx, ry, rz, m, t, paint));
        }

        public void FRod(BodyPart p, float ax, float ay, float az, float bx, float by, float bz, float r, byte m, Tissue t = Tissue.Skin)
        {
            if (Detail) Finer(() => _c.Rod((int)p, ax, ay, az, bx, by, bz, r, m, t));
        }

        public void FLine(BodyPart p, float ax, float ay, float az, float bx, float by, float bz, byte m, Tissue t = Tissue.Skin, bool paint = false)
        {
            if (Detail) Finer(() => _c.Line((int)p, ax, ay, az, bx, by, bz, m, t, paint));
        }

        /// <summary>A cell is there (fine coordinates).</summary>
        public bool FHas(BodyPart p, int x, int y, int z) => _c.Has((int)p, x, y, z);
        public bool FGet(BodyPart p, int x, int y, int z, out byte m, out Tissue t) => _c.Get((int)p, x, y, z, out m, out t);
        public void FSet(BodyPart p, int x, int y, int z, byte m, Tissue t = Tissue.Skin) { if (Detail) Finer(() => _c.Set((int)p, x, y, z, m, t)); }
        /// <summary>Recolours a cell that is there (fine coordinates), keeping what it is.</summary>
        public void FPut(BodyPart p, int x, int y, int z, byte m) { if (Detail && _c.Get((int)p, x, y, z, out _, out var t)) Finer(() => _c.Set((int)p, x, y, z, m, t)); }

        /// <summary>Does a piece of the finer work, its cells marked as such (<see cref="Cells.Marking"/>).</summary>
        void Finer(Action work)
        {
            _c.Marking = true;
            try { work(); }
            finally { _c.Marking = false; }
        }
        /// <summary>Takes a cell away (fine coordinates).</summary>
        public void FDel(BodyPart p, int x, int y, int z) { if (Detail) _c.Remove((int)p, x, y, z); }
        /// <summary>What a cell is made of (fine coordinates), 0 when it is not there.</summary>
        public byte FMat(BodyPart p, int x, int y, int z) => _c.Get((int)p, x, y, z, out var m, out _) ? m : (byte)0;
        /// <summary>
        /// Recolours the cells open to the air in a box (fine coordinates, the far corner excluded) for which a pattern
        /// gives a colour (0 leaves the cell as it is): lines of stitching, rows of mail, warpaint.
        /// </summary>
        public void FPattern(BodyPart p, int x0, int y0, int z0, int x1, int y1, int z1, Func<int, int, int, byte> m)
        {
            if (!Detail || !_c.Clip((int)p, ref x0, ref x1, ref y0, ref y1, ref z0, ref z1)) return;
            var put = new List<(int, int, int, byte)>();
            for (int y = y0; y < y1; y++)
                for (int z = z0; z < z1; z++)
                    for (int x = x0; x < x1; x++)
                    {
                        if (!_c.Has((int)p, x, y, z) || !_c.Shows((int)p, x, y, z, 1)) continue;
                        byte c = m(x, y, z);
                        if (c != 0) put.Add((x, y, z, c));
                    }
            foreach (var (x, y, z, c) in put) FPut(p, x, y, z, c);
        }
        /// <summary>The cell a coordinate in half-voxels falls in.</summary>
        public int FAt(float v) => (int)MathF.Floor(v * K);
        public float FMid(int c) => _c.Mid(c);

        /// <summary>
        /// Rounds the upright edges of a box (and the rim of its top) in cells: whatever lies beyond a quarter-circle of
        /// radius r (half-voxels) at each corner goes.
        /// </summary>
        public void FRound(BodyPart p, float x0, float y0, float z0, float x1, float y1, float z1, float r, bool top = true, float rTop = -1)
        {
            if (!Detail) return;
            if (rTop < 0) rTop = r;
            for (int y = _c.Lo(y0), ye = _c.Hi(y1); y < ye; y++)
                for (int z = _c.Lo(z0), ze = _c.Hi(z1); z < ze; z++)
                    for (int x = _c.Lo(x0), xe = _c.Hi(x1); x < xe; x++)
                    {
                        float mx = _c.Mid(x), my = _c.Mid(y), mz = _c.Mid(z);
                        float ex = MathF.Max(0, MathF.Max(x0 + r - mx, mx - (x1 - r))), ez = MathF.Max(0, MathF.Max(z0 + r - mz, mz - (z1 - r)));
                        bool gone = ex * ex + ez * ez > r * r;
                        if (top && !gone)
                        {
                            // the top's rim: a quarter-circle from each side up onto the top
                            float ey = MathF.Max(0, my - (y1 - rTop));
                            float ex2 = MathF.Max(0, MathF.Max(x0 + rTop - mx, mx - (x1 - rTop))), ez2 = MathF.Max(0, MathF.Max(z0 + rTop - mz, mz - (z1 - rTop)));
                            float side = MathF.Max(ex2, ez2);
                            gone = side * side + ey * ey > rTop * rTop;
                        }
                        if (gone) _c.Remove((int)p, x, y, z);
                    }
        }

        /// <summary>
        /// A piece of dress to sculpt, hung on a part (it is kept only with the finer work): in cells of 1.6 cm, or as big
        /// as the body's own (<paramref name="fine"/> off) for something the size of a troll's club.
        /// </summary>
        public Gear Hold(BodyPart on, DressKind kind, Tissue tissue = Tissue.Gear, bool fine = true)
        {
            var g = new Gear(fine ? BodyPlan.DressSub / BodyPlan.Sub : K) { On = on, Kind = kind, Tissue = tissue };
            if (Detail) _gear.Add(g);
            return g;
        }

        /// <summary>
        /// Fills the figure in: each half-voxel there where any of its cells is (or where a piece of dress fills it and the
        /// body does not), of what most of its cells show; then flesh under the skin, bones, the brain and the heart.
        /// </summary>
        public BodyPlan Into(BodyPlan f, int waist, int neck, int chestX)
        {
            var dressGrids = new PartGrid[_gear.Count];
            for (int d = 0; d < _gear.Count; d++)
            {
                var gear = _gear[d];
                dressGrids[d] = gear.C.Grid(0);
                if (dressGrids[d] == null) continue;
                f.Dress.Add(new Dress
                {
                    On = gear.On, Kind = gear.Kind, Grid = dressGrids[d], Sub = gear.C.R * BodyPlan.Sub,
                    Grip = (gear.Grip.X / H, gear.Grip.Y / H, gear.Grip.Z / H), Tip = (gear.Tip.X / H, gear.Tip.Y / H, gear.Tip.Z / H), Edge = gear.Edge,
                });
            }
            List<(byte m, byte t, int w)> mats = new(8), seen = new(8);
            for (int p = 0; p < BodyPlan.Parts; p++)
            {
                var fine = _c.Grid(p, out var finer);
                int x0 = int.MaxValue, y0 = int.MaxValue, z0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue, z1 = int.MinValue;
                void Span(PartGrid g, int r)
                {
                    x0 = Math.Min(x0, Down(g.X0, r)); y0 = Math.Min(y0, Down(g.Y0, r)); z0 = Math.Min(z0, Down(g.Z0, r));
                    x1 = Math.Max(x1, Down(g.X0 + g.SX - 1, r)); y1 = Math.Max(y1, Down(g.Y0 + g.SY - 1, r)); z1 = Math.Max(z1, Down(g.Z0 + g.SZ - 1, r));
                }
                if (fine != null) Span(fine, K);
                // the dress that hangs on this part, in the order it was sculpted (and so the index the plan keeps)
                var hung = new List<(int index, PartGrid grid, int r, (int, int, int) face)>();
                for (int d = 0, kept = 0; d < _gear.Count; d++)
                {
                    if (dressGrids[d] == null) continue;
                    var gear = _gear[d];
                    if (gear.On == (BodyPart)p) { hung.Add((kept, dressGrids[d], gear.C.R, gear.Kind == DressKind.Shield ? Facing(gear.Edge) : default)); Span(dressGrids[d], gear.C.R); }
                    kept++;
                }
                if (x0 == int.MaxValue) continue;
                var g = new PartGrid { X0 = x0, Y0 = y0, Z0 = z0, SX = x1 - x0 + 1, SY = y1 - y0 + 1, SZ = z1 - z0 + 1 };
                g.M = new byte[g.SX * g.SY * g.SZ];
                g.T = new byte[g.M.Length];
                var owner = new byte[g.M.Length];
                var solid = new bool[g.M.Length];
                var shown = new byte[g.M.Length];
                // cells a half-voxel's must be to be drawn at the middle distance: three of eight (one of one, unsculpted)
                int enough = Math.Max(1, (3 * K * K * K + 7) / 8);
                for (int y = y0; y <= y1; y++)
                    for (int z = z0; z <= z1; z++)
                        for (int x = x0; x <= x1; x++)
                        {
                            int i = g.Index(x, y, z);
                            // the body's own cells in it: what most of them show — a cell open to the air counts three times, one of
                            // the plain sculpting's four times one of the finer work's
                            mats.Clear();
                            int had = 0;
                            if (fine != null)
                                for (int dy = 0; dy < K; dy++)
                                    for (int dz = 0; dz < K; dz++)
                                        for (int dx = 0; dx < K; dx++)
                                        {
                                            int fx = x * K + dx, fy = y * K + dy, fz = z * K + dz;
                                            if (!fine.In(fx, fy, fz)) continue;
                                            int fi = fine.Index(fx, fy, fz);
                                            if (fine.M[fi] == 0) continue;
                                            int w = (fine.Mat(fx + 1, fy, fz) == 0 || fine.Mat(fx - 1, fy, fz) == 0 || fine.Mat(fx, fy + 1, fz) == 0
                                                || fine.Mat(fx, fy - 1, fz) == 0 || fine.Mat(fx, fy, fz + 1) == 0 || fine.Mat(fx, fy, fz - 1) == 0 ? 3 : 1) * (finer[fi] ? 1 : 4);
                                            Add(mats, fine.M[fi], fine.T[fi], w);
                                            had++;
                                        }
                            if (mats.Count > 0) { Most(mats, out g.M[i], out g.T[i]); g.Count++; solid[i] = had >= enough; continue; }
                            // else a piece of dress, where any of its cells is: a blade or a shaft however thin
                            foreach (var (index, dg, DR, face) in hung)
                            {
                                int n = DressCells(dg, DR, x, y, z, face, mats, seen);
                                if (n == 0) continue;
                                Most(mats, out g.M[i], out g.T[i]);
                                g.Count++;
                                owner[i] = (byte)(index + 1);
                                // a rivet or a stud alone in its half-voxel is not drawn as a block of one at the middle distance
                                solid[i] = n >= Math.Max(1, DR * DR * DR / 16);
                                Most(seen, out byte look, out _);
                                if (look != g.M[i]) shown[i] = look;
                                break;
                            }
                        }
                Inside(g, owner, solid, (BodyPart)p, f, waist, neck, chestX);
                // a half-voxel the body has too little of to be drawn (a knuckle of the fist) that a piece of dress fills
                // enough of is drawn with that piece — the haft going on into the hand, a crossguard whole before it —
                // though the blows find the knuckle there, as before (and the depths under the skin are already reckoned)
                for (int i = 0; i < g.M.Length && hung.Count > 0; i++)
                {
                    if (g.M[i] == 0 || owner[i] != 0 || solid[i]) continue;
                    int x = i % g.SX + g.X0, z = i / g.SX % g.SZ + g.Z0, y = i / (g.SX * g.SZ) + g.Y0;
                    foreach (var (index, dg, DR, face) in hung)
                    {
                        if (DressCells(dg, DR, x, y, z, face, mats, seen) < Math.Max(1, DR * DR * DR / 16)) continue;
                        owner[i] = (byte)(index + 1);
                        solid[i] = true;
                        Most(seen, out byte look, out _);
                        if (look != g.M[i]) shown[i] = look;
                        break;
                    }
                }
                f.Grid[p] = g;
                f.Fine[p] = fine;
                f.Owner[p] = owner;
                f.Solid[p] = solid;
                f.Shown[p] = Array.Exists(shown, v => v != 0) ? shown : null;
                f.Top = Math.Max(f.Top, (y1 + 2) / 2);
            }
            return f;
        }

        static int Down(int v, int r) => v >= 0 ? v / r : (v - r + 1) / r;

        /// <summary>The way a shield's face looks, along the axis nearest it (one step of a cell).</summary>
        static (int, int, int) Facing((float X, float Y, float Z) e)
        {
            float ax = MathF.Abs(e.X), ay = MathF.Abs(e.Y), az = MathF.Abs(e.Z);
            if (ax >= ay && ax >= az) return (Math.Sign(e.X), 0, 0);
            return ay >= az ? (0, Math.Sign(e.Y), 0) : (0, 0, Math.Sign(e.Z));
        }

        /// <summary>
        /// The cells of a piece of dress (<paramref name="r"/> to a half-voxel each way) in the half-voxel (x, y, z): how
        /// many there are; what they are, each counted once (<paramref name="mats"/>, what the blows find); and what they
        /// show (<paramref name="seen"/>) — a cell open to the air three times one inside, and on a shield one on the face
        /// it turns to the foe (<paramref name="face"/>) three times that again, so that it shows its paint and not the
        /// planks the paint is on.
        /// </summary>
        static int DressCells(PartGrid dg, int r, int x, int y, int z, (int x, int y, int z) face, List<(byte m, byte t, int w)> mats, List<(byte m, byte t, int w)> seen)
        {
            mats.Clear(); seen.Clear();
            int n = 0;
            bool shield = face != default;
            for (int dy = 0; dy < r; dy++)
                for (int dz = 0; dz < r; dz++)
                    for (int dx = 0; dx < r; dx++)
                    {
                        int fx = x * r + dx, fy = y * r + dy, fz = z * r + dz;
                        if (!dg.In(fx, fy, fz)) continue;
                        int fi = dg.Index(fx, fy, fz);
                        if (dg.M[fi] == 0) continue;
                        Add(mats, dg.M[fi], dg.T[fi], 1);
                        bool front = shield && dg.Mat(fx + face.x, fy + face.y, fz + face.z) == 0;
                        bool open = front || dg.Mat(fx + 1, fy, fz) == 0 || dg.Mat(fx - 1, fy, fz) == 0 || dg.Mat(fx, fy + 1, fz) == 0
                            || dg.Mat(fx, fy - 1, fz) == 0 || dg.Mat(fx, fy, fz + 1) == 0 || dg.Mat(fx, fy, fz - 1) == 0;
                        Add(seen, dg.M[fi], dg.T[fi], front ? 9 : open ? 3 : 1);
                        n++;
                    }
            return n;
        }

        static void Add(List<(byte m, byte t, int w)> mats, byte m, byte t, int w)
        {
            for (int k = 0; k < mats.Count; k++)
                if (mats[k].m == m) { mats[k] = (m, mats[k].t, mats[k].w + w); return; }
            mats.Add((m, t, w));
        }

        static void Most(List<(byte m, byte t, int w)> mats, out byte m, out byte t)
        {
            var best = mats[0];
            foreach (var e in mats) if (e.w > best.w) best = e;
            m = best.m; t = best.t;
        }

        /// <summary>
        /// How deep each voxel lies under the surface (1 for the surface), counting worn things as cover — but not a
        /// half-voxel too thinly filled to be drawn at the middle distance, which leaves what is under it showing.
        /// </summary>
        static int[] Depth(PartGrid g, bool[] solid)
        {
            var d = new int[g.M.Length];
            var q = new Queue<int>();
            for (int y = 0; y < g.SY; y++)
                for (int z = 0; z < g.SZ; z++)
                    for (int x = 0; x < g.SX; x++)
                    {
                        int i = (y * g.SZ + z) * g.SX + x;
                        if (g.M[i] == 0) continue;
                        bool edge = x == 0 || y == 0 || z == 0 || x == g.SX - 1 || y == g.SY - 1 || z == g.SZ - 1 || !solid[i]
                            || !solid[i - 1] || !solid[i + 1] || !solid[i - g.SX] || !solid[i + g.SX] || !solid[i - g.SX * g.SZ] || !solid[i + g.SX * g.SZ];
                        if (edge) { d[i] = 1; q.Enqueue(i); }
                    }
            while (q.Count > 0)
            {
                int i = q.Dequeue(), x = i % g.SX, z = i / g.SX % g.SZ, y = i / (g.SX * g.SZ);
                foreach (var (dx, dy, dz) in Around)
                {
                    int nx = x + dx, ny = y + dy, nz = z + dz;
                    if (nx < 0 || ny < 0 || nz < 0 || nx >= g.SX || ny >= g.SY || nz >= g.SZ) continue;
                    int n = (ny * g.SZ + nz) * g.SX + nx;
                    if (g.M[n] == 0 || d[n] != 0) continue;
                    d[n] = d[i] + 1;
                    q.Enqueue(n);
                }
            }
            return d;
        }

        static readonly (int, int, int)[] Around = { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) };

        static void Inside(PartGrid g, byte[] owner, bool[] solid, BodyPart p, BodyPlan f, int waist, int neck, int chestX)
        {
            var depth = Depth(g, solid);
            int headY0 = f.HeadY0, headY1 = f.HeadY1, w = waist, n = neck, chestY = w + (n - w) * 2 / 5;
            int back = int.MaxValue;
            for (int i = 0; i < g.M.Length; i++)
            {
                // under the skin (or the mail) is the body; what is carried, and the dress, stay what they are
                if (g.T[i] is not ((byte)Tissue.Skin or (byte)Tissue.Armor) || depth[i] < 2 || owner[i] != 0) continue;
                int x = i % g.SX + g.X0, z = i / g.SX % g.SZ + g.Z0, y = i / (g.SX * g.SZ) + g.Y0;
                Tissue t;
                if (p == BodyPart.Body && y >= headY0 && y < headY1) t = depth[i] == 2 ? Tissue.Bone : Tissue.Brain;
                else if (p == BodyPart.Body && !f.Beast && y >= w && y < n)
                {
                    t = Tissue.Flesh;
                    // ribs round the chest every other row, the heart a little left of the middle
                    if (y >= chestY && (y & 1) == 0 && Math.Abs(x + .5) >= 1.5) t = Tissue.Bone;
                    if (y >= chestY + 2 && y < chestY + 5 && x >= 0 && x <= chestX / 2) t = Tissue.Heart;
                }
                else if (p == BodyPart.Body) t = Tissue.Flesh;
                else t = Tissue.Bone;
                g.T[i] = (byte)t;
                g.M[i] = t switch { Tissue.Bone => Mat.Bone, Tissue.Brain => Mat.Brain, Tissue.Heart => Mat.Heart, _ => Mat.Flesh };
                if (t == Tissue.Brain) f.Brain++;
                if (t == Tissue.Heart) f.Heart++;
                if (p == BodyPart.Body && y >= w && y < n) back = Math.Min(back, z);
            }
            // the spine down the back of the trunk (or along the top of a beast)
            if (p != BodyPart.Body || back == int.MaxValue) return;
            for (int i = 0; i < g.M.Length; i++)
            {
                int x = i % g.SX + g.X0, z = i / g.SX % g.SZ + g.Z0, y = i / (g.SX * g.SZ) + g.Y0;
                if (f.Beast || y < w || y >= n || z != back || x < -1 || x > 0 || g.T[i] is not ((byte)Tissue.Flesh or (byte)Tissue.Heart)) continue;
                if (g.T[i] == (byte)Tissue.Heart) f.Heart--;
                g.T[i] = (byte)Tissue.Bone;
                g.M[i] = Mat.Bone;
            }
        }
    }

    /// <summary>
    /// Legs in boots with the toes forward, a trunk with rounded shoulders, a belt and its buckle, a round head with eyes,
    /// brows, a nose, a mouth and ears, hair as it is worn, and two arms with hands. Everything mirrors across the middle.
    /// </summary>
    static Sculpt Humanoid(Build b, BodyPlan f)
    {
        var s = new Sculpt();
        int lz0 = -(b.LegD / 2), lz1 = b.LegD - b.LegD / 2;
        s.Pair(LegR, LegL, b.LegX0, 0, lz0, b.LegX1, b.Leg, lz1, b.Trousers);
        if (b.Boot > 0)
        {
            s.Pair(LegR, LegL, b.LegX0, 0, lz0, b.LegX1, b.Boot, lz1, b.Boots);
            s.Pair(LegR, LegL, b.LegX0, 0, lz1, b.LegX1, Math.Max(1, b.Boot - 1), lz1 + 1, b.Boots);
        }
        else s.Pair(LegR, LegL, b.LegX0, 0, lz1, b.LegX1, 1, lz1 + 1, b.Skin);

        // the trunk, its shoulders rounded, a belt with a buckle, the throat showing at the collar
        s.Box(Body, -b.Chest, b.Waist, b.Z0, b.Chest, b.Neck, b.Z1, b.Shirt);
        s.Cut(Body, -b.Chest, b.Neck - 1, b.Z0, -b.Chest + 1, b.Neck, b.Z1, both: true);
        s.Box(Body, -b.Chest, b.Waist, b.Z0, b.Chest, b.Waist + 1, b.Z1, b.Belt);
        s.Box(Body, -1, b.Waist, b.Z1, 1, b.Waist + 1, b.Z1 + 1, Mat.Brass, Tissue.Armor);
        s.Paint(Body, -1, b.Neck - 2, b.Z1 - 1, 1, b.Neck, b.Z1, b.Skin);

        // the head
        int hx = b.HeadW, hz0 = b.HeadZ, hz1 = b.HZ1, hy0 = b.Neck, hy1 = b.Crown, up = b.FaceUp, eye = hy0 + 3 + up;
        s.Box(Body, -hx, hy0, hz0, hx, hy1, hz1, b.Skin);
        s.Round(Body, -hx, hy0, hz0, hx, hy1, hz1);
        s.FRound(Body, -hx, hy0, hz0, hx, hy1, hz1, Math.Min(1.25f, hx * .3f), true, 1);
        s.Cut(Body, -hx, hy0, hz0, hx, hy0 + 1, hz0 + 2);
        s.Pair(Body, Body, -hx + 1, eye, b.FaceZ, -hx + 2, eye + 2, hz1, b.Eye);
        s.Pair(Body, Body, -hx + 1, eye + 2, b.FaceZ, -1, eye + 3, hz1, b.Brow != 0 ? b.Brow : b.Hair);
        s.Box(Body, -1, hy0 + 2 + up, hz1, 1, hy0 + 4 + up, hz1 + 1, b.Skin);
        s.Box(Body, -1, hy0 + 1 + up, b.FaceZ, 1, hy0 + 2 + up, hz1, b.Mouth);
        s.Pair(Body, Body, -hx - 1, eye, -1, -hx, eye + 2, 1, b.Skin);
        Features(s, b);
        Hair(s, b);

        // the arms: sleeves, a cuff, the hands; the shoulders rounded on top
        byte sleeve = b.Sleeve != 0 ? b.Sleeve : b.Shirt;
        s.Pair(Main, Off, b.ArmX0, b.HandY + b.Hand, b.ArmZ0, -b.Chest, b.Neck, b.ArmZ1, sleeve);
        s.Pair(Main, Off, b.ArmX0, b.HandY, b.ArmZ0, -b.Chest, b.HandY + b.Hand, b.ArmZ1, b.Skin);
        if (b.Cuff != 0) s.Pair(Main, Off, b.ArmX0, b.HandY + b.Hand, b.ArmZ0, -b.Chest, b.HandY + b.Hand + 1, b.ArmZ1, b.Cuff);
        s.Cut(Main, b.ArmX0, b.Neck - 1, b.ArmZ0, b.ArmX0 + 1, b.Neck, b.ArmZ1);
        s.Cut(Off, -b.ArmX0 - 1, b.Neck - 1, b.ArmZ0, -b.ArmX0, b.Neck, b.ArmZ1);
        Fists(s, b);
        Feet(s, b);

        float legX = (b.LegX0 + b.LegX1) / 2f, armX = (b.ArmX0 - b.Chest) / 2f, armZ = (b.ArmZ0 + b.ArmZ1) / 2f;
        Joint(f, Body, 0, b.Waist / H, 0);
        Joint(f, LegR, legX / H, b.Leg / H, 0);
        Joint(f, LegL, -legX / H, b.Leg / H, 0);
        Joint(f, Main, armX / H, (b.Neck - 1) / H, armZ / H);
        Joint(f, Off, -armX / H, (b.Neck - 1) / H, armZ / H);
        f.HeadY0 = hy0; f.HeadY1 = hy1;
        return s;
    }

    /// <summary>
    /// The face drawn finer than its half-voxels: eyes three cells high with a glint at the upper corner and the lid
    /// under the brow, the brow a single row, the nose a bridge down to its tip, the mouth a line with the lip under it,
    /// the ears rounded at their corners with the hollow of the ear in them.
    /// </summary>
    static void Features(Sculpt s, Build b)
    {
        if (!s.Detail) return;
        int hx = b.HeadW, up = b.FaceUp, E = (b.Neck + 3 + up) * 2, F = b.HZ1 * 2 - 1, ex = (-hx + 1) * 2, N = (b.Neck + 2 + up) * 2, er = (-hx - 1) * 2;
        foreach (int sg in new[] { 1, -1 })
        {
            int X(int x) => sg > 0 ? x : -1 - x;
            for (int x = ex; x < ex + 4; x++)
            {
                if (x < ex + 2) s.FPut(Body, X(x), E + 3, F, b.Skin);
                s.FPut(Body, X(x), E + 5, F, b.Skin);
            }
            if (b.Eye == Mat.Eye) s.FPut(Body, sg > 0 ? ex : -ex - 2, E + 2, F, Mat.EyeWhite);
            s.FPut(Body, X(ex - 1), E + 4, F, b.Brow != 0 ? b.Brow : b.Hair);
            // the ear: its corners rounded, its hollow
            s.FDel(Body, X(er), E + 3, -2); s.FDel(Body, X(er), E, 1);
            s.FPut(Body, X(er), E + 1, 0, Mat.Lips); s.FPut(Body, X(er), E + 2, -1, Mat.Lips);
        }
        // the nose: a bridge two cells across, its tip standing out a cell more
        for (int y = N; y < N + 4; y++)
            for (int x = -2; x < 2; x++)
                if (!(y < N + 2 && x is -1 or 0)) s.FDel(Body, x, y, F + 2);
        // the mouth: the upper lip skin again over the line of it, a lower lip under it
        if (b.Mouth != b.Skin)
        {
            for (int x = -2; x < 2; x++) s.FPut(Body, x, N - 1, F, b.Skin);
            if (b.Eye == Mat.Eye) { s.FPut(Body, -1, N - 3, F, Mat.Lips); s.FPut(Body, 0, N - 3, F, Mat.Lips); }
        }
    }

    /// <summary>
    /// The hands drawn finer: the edges of the fist rounded under it, the fingers parted by grooves under it and up its
    /// outer side, the thumb laid along before it.
    /// </summary>
    static void Fists(Sculpt s, Build b)
    {
        if (!s.Detail) return;
        int x0 = b.ArmX0 * 2, x1 = -b.Chest * 2, y0 = b.HandY * 2, z0 = b.ArmZ0 * 2, z1 = b.ArmZ1 * 2, fw = Math.Max(2, (z1 - z0) / 4);
        foreach (var (p, sg) in new[] { (Main, 1), (Off, -1) })
        {
            int X(int x) => sg > 0 ? x : -1 - x;
            for (int z = z0; z < z1; z++) { s.FDel(p, X(x0), y0, z); s.FDel(p, X(x1 - 1), y0, z); }
            for (int x = x0; x < x1; x++) { s.FDel(p, X(x), y0, z0); s.FDel(p, X(x), y0, z1 - 1); }
            for (int z = z0 + fw; z < z1 - 1; z += fw)
            {
                for (int x = x0 + 1; x < x1 - 1; x++) s.FDel(p, X(x), y0, z);
                for (int y = y0 + 1; y < y0 + 3; y++) s.FDel(p, X(x0), y, z);
            }
            int ty = y0 + b.Hand * 2 - 3;
            s.FSet(p, X(x1 - 1), ty, z1, b.Skin);
            s.FSet(p, X(x1 - 1), ty + 1, z1, b.Skin);
        }
    }

    /// <summary>Boots drawn finer: a dark sole, the toe's upper edge rounded, a cuff rolled over at the top.</summary>
    static void Feet(Sculpt s, Build b)
    {
        if (!s.Detail || b.Boot <= 0) return;
        int lz0 = -(b.LegD / 2) * 2, lz1 = (b.LegD - b.LegD / 2) * 2, x0 = b.LegX0 * 2, x1 = b.LegX1 * 2, top = b.Boot * 2 - 1, toe = Math.Max(1, b.Boot - 1) * 2 - 1;
        byte sole = b.Boots == Mat.Steel ? Mat.SteelDark : b.Boots == Mat.Iron ? Mat.Iron : Mat.LeatherDark;
        foreach (var (p, sg) in new[] { (LegR, 1), (LegL, -1) })
        {
            int X(int x) => sg > 0 ? x : -1 - x;
            for (int z = lz0; z < lz1 + 2; z++) for (int x = x0; x < x1; x++) s.FPut(p, X(x), 0, z, sole);
            for (int x = x0; x < x1; x++) { s.FDel(p, X(x), toe, lz1 + 1); s.FSet(p, X(x), top, lz1, b.Boots); s.FSet(p, X(x), top, lz0 - 1, b.Boots); }
            for (int z = lz0 - 1; z <= lz1; z++) { s.FSet(p, X(x0 - 1), top, z, b.Boots); s.FSet(p, X(x1), top, z, b.Boots); }
        }
    }

    /// <summary>The hair over the top and the back of the head, down the sides above the ears, a fringe over the brow.</summary>
    static void Hair(Sculpt s, Build b)
    {
        if (b.HairStyle == 3) return;
        int hx = b.HeadW, hz0 = b.HeadZ, hz1 = b.HZ1, hy0 = b.Neck, hy1 = b.Crown;
        s.Paint(Body, -hx, hy1 - 1, hz0, hx, hy1, hz1, b.Hair);
        s.Paint(Body, -hx, hy0 + (b.HairStyle == 1 ? 4 : 2), hz0, hx, hy1, hz0 + 1, b.Hair);
        if (b.HairStyle == 1) return;
        s.Paint(Body, -hx, hy0 + 4, hz0, hx, hy1, hz0 + 2, b.Hair);
        s.Paint(Body, -hx, hy0 + 5, hz0, -hx + 1, hy1, hz1 - 2, b.Hair);
        s.Paint(Body, hx - 1, hy0 + 5, hz0, hx, hy1, hz1 - 2, b.Hair);
        s.Paint(Body, -hx + 1, hy1 - 2, b.FaceZ, hx - 1, hy1 - 1, hz1, b.Hair);
        // long: down the back of the neck and over the shoulders
        if (b.HairStyle == 2) s.Box(Body, -hx + 1, hy0 - 3, hz0 - 1, hx - 1, hy1 - 1, hz0, b.Hair);
    }

    /// <summary>A beard over the chin and the jaw and hanging a little below, a moustache, the sideburns.</summary>
    static void Beard(Sculpt s, Build b, byte hair, bool full = true)
    {
        int hx = b.HeadW, hz1 = b.HZ1, hy0 = b.Neck;
        s.Paint(Body, -hx, hy0, 0, hx, hy0 + 3, hz1, hair);
        s.Paint(Body, -hx, hy0 + 3, 0, -hx + 1, hy0 + 5, hz1 - 1, hair);
        s.Paint(Body, hx - 1, hy0 + 3, 0, hx, hy0 + 5, hz1 - 1, hair);
        s.Box(Body, -2, hy0 - (full ? 2 : 1), b.FaceZ, 2, hy0 + 1, hz1, hair);
        s.Box(Body, -2, hy0 + 1, hz1, 2, hy0 + 2, hz1 + 1, hair);
        if (full) s.Box(Body, -1, hy0 - 3, b.FaceZ, 1, hy0 - 2, hz1, hair);
    }

    /// <summary>A hood over the head and down to the shoulders, the face showing; a point hangs down at the back.</summary>
    static void Hood(Sculpt s, Build b, byte cloth)
    {
        int hx = b.HeadW, hz0 = b.HeadZ, hz1 = b.HZ1, hy0 = b.Neck, hy1 = b.Crown;
        s.Box(Body, -hx + 1, hy1, hz0 + 1, hx - 1, hy1 + 1, hz1 - 2, cloth);
        s.Box(Body, -hx + 1, hy0 + 1, hz0 - 1, hx - 1, hy1, hz0, cloth);
        s.Coat(Body, -hx - 1, hy0, hz0 - 1, hx + 1, hy1 + 1, hz1 + 1, cloth, null, (-hx + 1, hy0, hz1 - 2, hx - 1, hy1 - 1, hz1 + 2));
        s.Paint(Body, -b.Chest, b.Neck - 2, b.Z0, b.Chest, b.Neck, b.Z1, cloth);
        s.Box(Body, -1, hy0 - 2, hz0 - 2, 1, hy0 + 3, hz0 - 1, cloth);
    }

    static void Joint(BodyPlan f, BodyPart p, float x, float y, float z) { f.JX[(int)p] = x; f.JY[(int)p] = y; f.JZ[(int)p] = z; }

    static BodyPlan Done(Sculpt s, BodyPlan f, Build b) => s.Into(f, b.Waist, b.Neck, b.Chest);

    // ---------------------------------------------------------------- what they carry (held forward from the right fist)

    /// <summary>A stable 0..1 for a cell of something sculpted: where the paint is worn, where the rust is.</summary>
    static float Dice(int x, int y, int z, int salt = 0) => (float)Hash.H3(x * 7 + salt, y * 13 - salt, z * 5 + salt * 3);

    /// <summary>
    /// Something held in the right fist, to be sculpted in its own cells: the cell just short of the middle of the fist
    /// each way (a haft two cells square fills it from there), and the fist's front. The grip is the fist's middle.
    /// </summary>
    static Gear InFist(Sculpt s, Build b, out int x, out int y, out int z0, bool fine = true)
    {
        var g = s.Hold(Main, DressKind.Weapon, Tissue.Gear, fine);
        int q = g.C.R;
        x = b.FistX * q + q / 2 - 1; y = b.FistY * q + q / 2 - 1; z0 = b.ArmZ1 * q;
        g.Grip = (b.FistX + .5f, b.FistY + .5f, (b.ArmZ0 + b.ArmZ1) / 2f);
        return g;
    }

    /// <summary>Where a cell of a piece of dress is, in half-voxels (its middle).</summary>
    static (float, float, float) Half(Gear g, float x, float y, float z) => (x / g.C.R, y / g.C.R, z / g.C.R);

    /// <summary>
    /// A pick: an ash haft through the fist, a leather grip behind it, and across its end an iron head in an eye round the
    /// haft, curving back toward the hand, drawn out to a point below and a chisel above, both ends honed bright.
    /// </summary>
    static void Pick(Sculpt s, Build b, byte head, int len = 12)
    {
        var g = InFist(s, b, out int x, out int y, out int z0);
        int e = z0 + len * 4, back = b.ArmZ0 * 4 - 3;
        g.Fill(x, y, back, x + 2, y + 2, e + 1, Mat.Handle);
        g.Wound(x, y, back, b.ArmZ0 * 4 + 2, 2, Mat.LeatherDark, Mat.Leather);
        for (int v = -16; v < 16; v++)
        {
            float a = MathF.Abs(v + .5f) / 16;
            int sweep = (int)MathF.Round(5 * a * a);
            bool point = v < 0;
            // the head thins toward its ends: four cells deep at the eye, one at the point, two at the chisel
            int deep = Math.Max(point ? 1 : 2, (int)MathF.Round(4.6f * (1 - a)));
            int thick = point && a > .7f ? 1 : 2;
            int zc = e - 2 - sweep;
            byte m = a > .8f ? Mat.SteelEdge : a > .62f ? Mat.Steel : head;
            for (int k = 0; k < deep; k++)
                for (int dx = 0; dx < thick; dx++) g.Dot(x + dx, y + 1 + v, zc - deep / 2 + k, m);
        }
        // the chisel's end flares a little; the eye round the haft
        g.Fill(x - 1, y + 15, e - 9, x + 3, y + 17, e - 6, Mat.SteelEdge);
        g.Fill(x - 1, y - 1, e - 5, x + 3, y + 3, e + 1, head);
        g.Tip = Half(g, x + 1, y - 15, e - 7);
    }

    /// <summary>
    /// A sword: a grip wound in two leathers through the fist, a wheel of a pommel behind it with a boss through it, a
    /// guard across the blade with its ends turned toward the point, and a blade narrowing to the point, its edges
    /// honed bright, a fuller dark down the first part of it; the edge below leads a cut as the arm comes down. A
    /// longsword's grip is long enough for both hands, its guard longer and its fuller shorter.
    /// </summary>
    static void Sword(Sculpt s, Build b, int len = 13, byte guard = Mat.Brass, bool longsword = false)
    {
        var g = InFist(s, b, out int x, out int y, out int z0);
        int q = 4, back = b.ArmZ0 * q - (longsword ? 10 : 3);
        g.Wound(x, y, back, z0, 2, Mat.LeatherDark, Mat.Leather);
        g.Wheel(x, 2, y + 1, back - 3, longsword ? 3.3f : 2.8f, guard);
        g.Fill(x - 1, y, back - 4, x, y + 2, back - 2, guard);
        g.Fill(x + 2, y, back - 4, x + 3, y + 2, back - 2, guard);
        // the guard, its quillons bending toward the point over their last three cells
        int hl = longsword ? 12 : 9;
        for (int dy = -hl; dy < hl; dy++)
        {
            int bend = Math.Max(0, (int)(MathF.Abs(dy + .5f) - (hl - 3.5f)));
            g.Fill(x, y + 1 + dy, z0 + bend, x + 2, y + 2 + dy, z0 + 2 + bend, guard);
        }
        g.Fill(x - 1, y - 1, z0, x + 3, y + 3, z0 + 3, guard);
        Blade(g, x, y + 1, z0 + 3, z0 + len * q, 3.1f, longsword ? .5f : .62f, Mat.Steel, false, 0);
        g.Tip = Half(g, x + 1, y + 1, z0 + len * q);
    }

    /// <summary>
    /// A blade along z from z0 to its point at z1, centred on the row boundary yc, flat to x: as broad as twice hw at the
    /// guard, narrowing slowly and then to the point; its edges a cell of honed steel, the middle two cells of steel
    /// twice as thick (a fuller cut dark down the first part of it, to <paramref name="fuller"/> of its length). An old
    /// one is notched along its edges and spotted with rust.
    /// </summary>
    static void Blade(Gear g, int x, int yc, int z0, int z1, float hw, float fuller, byte steel, bool old, int salt)
    {
        for (int z = z0; z < z1; z++)
        {
            float t = (z - z0) / (float)(z1 - z0);
            float w = t < .72f ? hw - t / .72f * (hw - 2.1f) : 2.1f * (1 - (t - .72f) / .28f);
            if (w < .5f) w = t < .97f ? .5f : 0;
            for (int dy = -5; dy < 5; dy++)
            {
                float a = MathF.Abs(dy + .5f);
                if (a > w + .01f) continue;
                bool edge = a + 1 > w, mid = a < 1;
                if (old && edge && Dice(x, yc + dy, z, salt) < .16f) continue;
                byte m = edge ? Mat.SteelEdge : mid && t < fuller ? Mat.SteelDark : steel;
                if (old && Dice(z >> 2, x, (yc + dy) >> 1, salt + 5) < .3f) m = Mat.Rust;
                g.Dot(x, yc + dy, z, m);
                if (!edge && !(mid && t < fuller)) g.Dot(x + 1, yc + dy, z, m);
            }
        }
    }

    /// <summary>
    /// A bearded axe on a long haft: an iron cap on its butt, a grip bound in hide, the head in an eye round the haft
    /// with its blade hanging below — square off the eye, its beard sweeping back down the haft, the toe out before the
    /// end, the edge honed — and a spike behind; on an orc's, old iron spotted with rust and the edge notched.
    /// </summary>
    static void Axe(Sculpt s, Build b, int len = 15)
    {
        var g = InFist(s, b, out int x, out int y, out int z0);
        int q = 4, e = z0 + len * q, butt = b.ArmZ0 * q - 4;
        g.Fill(x, y, butt, x + 2, y + 2, e + 2, Mat.Handle);
        g.Wound(x, y, butt, butt + 5, 2, Mat.Hide, Mat.LeatherDark);
        g.Wound(x, y, z0, z0 + 5, 2, Mat.Hide, Mat.LeatherDark);
        g.Fill(x - 1, y - 1, butt - 2, x + 3, y + 3, butt, Mat.Iron);
        g.Fill(x - 1, y - 1, e - 6, x + 3, y + 3, e + 2, Mat.Iron);
        const int B = 24;
        for (int v = 1; v <= B; v++)
        {
            float t = v / (float)B;
            int front = e + 2 + (int)MathF.Round(4 * t * t), rear = e - 6 - (int)MathF.Round(17 * t * t * t);
            for (int z = rear; z <= front; z++)
            {
                bool edge = v >= B - 1 || v > B - 6 && z <= rear + 1 || v > B - 9 && z >= front - 1;
                // two notches out of the edge
                if (v >= B - 2 && (z - rear) / 3 is 2 or 5 && Dice(z >> 2, 0, 0, 3) < .9f) continue;
                byte m = edge ? Mat.SteelEdge : Dice(z >> 2, v >> 2, x, 4) < .3f ? Mat.Rust : Mat.Iron;
                g.Dot(x, y - v, z, m);
                if (v <= 5 && !edge) g.Dot(x + 1, y - v, z, m);
            }
        }
        // the spike behind, rising from the eye and narrowing
        for (int h = 0; h < 7; h++)
        {
            int z1 = e - 1 - h / 2, za = e - 5 + h / 2;
            for (int z = za; z <= z1; z++) { g.Dot(x, y + 2 + h, z, Mat.Iron); if (h < 3) g.Dot(x + 1, y + 2 + h, z, Mat.Iron); }
        }
        g.Tip = Half(g, x + 1, y - B / 2f, e - 6);
    }

    /// <summary>
    /// A knife: a grip bound in rag or leather, an iron bolster, and a blade straight along its back with the edge below
    /// bellying out and sweeping up to the point; an old one notched and rusty.
    /// </summary>
    static void Knife(Sculpt s, Build b, bool old = true)
    {
        var g = InFist(s, b, out int x, out int y, out int z0);
        int back = b.ArmZ0 * 4 - 2, L = 18;
        g.Wound(x, y, back, z0 + 1, 2, old ? Mat.Rag : Mat.LeatherDark, Mat.Leather);
        g.Fill(x - 1, y - 1, z0 + 1, x + 3, y + 3, z0 + 2, Mat.Iron);
        for (int k = 0; k < L; k++)
        {
            int z = z0 + 2 + k;
            float t = k / (float)L;
            int top = y + 2 - (t > .78f ? (int)MathF.Round((t - .78f) / .22f * 3) : 0);
            int bottom = y - 1 - (int)MathF.Round(1.9f * MathF.Sin(MathF.PI * t) - t * 1.4f);
            for (int yy = bottom; yy <= top; yy++)
            {
                bool edge = yy == bottom;
                if (old && edge && Dice(k, x, 1) < .18f) continue;
                byte m = edge ? Mat.SteelEdge : old ? (Dice(z >> 1, yy >> 1, x, 2) < .3f ? Mat.Rust : Mat.Iron) : Mat.Steel;
                g.Dot(x, yy, z, m);
                if (yy == top && t < .7f) g.Dot(x + 1, yy, z, m);
            }
        }
        g.Tip = Half(g, x + 1, y, z0 + 2 + L);
    }

    /// <summary>A mallet: a short handle and a round head of wood across its end, its end grain showing, bound with two iron hoops.</summary>
    static void Mallet(Sculpt s, Build b)
    {
        var g = InFist(s, b, out int x, out int y, out int z0);
        g.Fill(x, y, b.ArmZ0 * 4 - 2, x + 2, y + 2, z0 + 24, Mat.Handle);
        float cx = x + 1, cz = z0 + 28, r = 5.2f;
        for (int yy = y - 7; yy < y + 9; yy++)
        {
            int i = yy - (y - 7);
            bool hoop = i is 2 or 3 or 12 or 13, end = i is 0 or 15;
            float rr = hoop ? r + .7f : r;
            for (int z = (int)(cz - rr) - 1; z <= cz + rr; z++)
                for (int xx = (int)(cx - rr) - 1; xx <= cx + rr; xx++)
                    if ((xx + .5f - cx) * (xx + .5f - cx) + (z + .5f - cz) * (z + .5f - cz) <= rr * rr)
                        g.Dot(xx, yy, z, hoop ? Mat.Iron : end ? Mat.Board : Mat.Wood);
        }
        g.Tip = Half(g, cx, y - 7, cz);
    }

    /// <summary>
    /// A troll's club: a crooked young trunk with its bark on, thickening to a knot at its end, the stumps of boughs on
    /// it, iron nails driven through the knot and the shards of bones bound in; on a tamed troll's, iron bands round it.
    /// </summary>
    static void Club(Sculpt s, Build b, bool banded = false)
    {
        var g = InFist(s, b, out int x, out int y, out int z0, fine: false);
        // along its length (cells of 3 cm): where its middle is and how thick it is there
        float cx = x + 1, cy = y + 1;
        var line = new (float z, float dx, float dy, float r)[] { (b.ArmZ0 * 2 - 2, 0, 0, 2.4f), (z0 + 10, .6f, -.4f, 2.6f), (z0 + 20, -.6f, .5f, 3.4f), (z0 + 30, .5f, 1.2f, 5f), (z0 + 38, 0, 1.6f, 6f), (z0 + 43, -.4f, 1.4f, 4.6f) };
        for (int i = 0; i + 1 < line.Length; i++)
        {
            var (za, ax, ay, ra) = line[i]; var (zb, bx, by, rb) = line[i + 1];
            for (int z = (int)za; z < zb; z++)
            {
                float k = (z - za) / (zb - za), r = ra + (rb - ra) * k, mx = cx + ax + (bx - ax) * k, my = cy + ay + (by - ay) * k;
                for (int yy = (int)(my - r) - 1; yy <= my + r; yy++)
                    for (int xx = (int)(mx - r) - 1; xx <= mx + r; xx++)
                    {
                        float d = (xx + .5f - mx) * (xx + .5f - mx) + (yy + .5f - my) * (yy + .5f - my);
                        if (d > r * r) continue;
                        bool end = z >= zb - 1 && i == line.Length - 2;
                        g.Dot(xx, yy, z, end ? Mat.Snag : Dice(xx >> 1, yy >> 1, z >> 2, 9) < .2f ? Mat.PineBark : Mat.Trunk);
                    }
            }
        }
        // the stumps of two boughs
        g.Fill(x + 3, y, z0 + 18, x + 6, y + 2, z0 + 20, Mat.Trunk);
        g.Fill(x - 3, y + 3, z0 + 26, x - 1, y + 7, z0 + 28, Mat.Trunk);
        // nails through the knot, and shards of bone
        foreach (var (sx, sy, dz, m) in new[] { (-1, 0, 32, Mat.Iron), (1, 0, 35, Mat.Iron), (0, -1, 33, Mat.Iron), (0, 1, 36, Mat.Iron), (-1, 0, 38, Mat.Iron), (1, 0, 30, Mat.Iron),
            (0, -1, 37, Mat.Bone), (1, 0, 39, Mat.Bone), (0, 1, 40, Mat.Bone) })
        {
            // driven in from the side: out from the middle to the bark, and two cells standing out of it
            int px = x + 1, py = y + 2, z = z0 + dz;
            while (g.Has(px, py, z)) { px += sx; py += sy; }
            g.Dot(px, py, z, m); g.Dot(px + sx, py + sy, z, m);
            if (m == Mat.Bone) g.Dot(px + sx, py + sy, z + 1, m);
        }
        if (banded)
            foreach (int z in new[] { z0 + 28, z0 + 35 })
                for (int yy = y - 6; yy < y + 10; yy++)
                    for (int xx = x - 6; xx < x + 8; xx++)
                    {
                        if (!g.Has(xx, yy, z)) continue;
                        bool rim = !g.Has(xx + 1, yy, z) || !g.Has(xx - 1, yy, z) || !g.Has(xx, yy + 1, z) || !g.Has(xx, yy - 1, z);
                        if (rim) { g.Dot(xx, yy, z, Mat.Iron); g.Dot(xx, yy, z + 1, Mat.Iron); }
                    }
        g.Tip = Half(g, cx, cy + 1.5f, z0 + 38);
    }

    /// <summary>
    /// A bow in the left fist, its limbs along z and bending toward the string at the tips (held up, the arm forward, it
    /// stands upright): a two-toned stave, the grip wound in leather, horn at the nocks, the string tip to tip.
    /// </summary>
    static void Bow(Sculpt s, Build b)
    {
        var g = s.Hold(Off, DressKind.Weapon);
        int x = -b.FistX * 4 - 3, y = b.FistY * 4 + 1;
        g.Grip = (-b.FistX - .5f, b.FistY + .5f, (b.ArmZ0 + b.ArmZ1) / 2f);
        const int Half = 40, Brace = 11;
        for (int z = -Half; z < Half; z++)
        {
            float a = MathF.Abs(z + .5f) / Half;
            int yo = (int)MathF.Round(Brace * MathF.Pow(a, 1.8f));
            int th = a < .25f ? 3 : a < .65f ? 2 : 1, tx = a < .8f ? 2 : 1;
            for (int dy = 0; dy < th; dy++)
                for (int dx = 0; dx < tx; dx++)
                {
                    byte m = a > .94f ? Mat.Horn : MathF.Abs(z + .5f) < 9 ? (((z + dy) >> 1 & 1) == 0 ? Mat.LeatherDark : Mat.Leather) : dy == th - 1 ? Mat.Shaft : Mat.Handle;
                    g.Dot(x + dx, y + yo - (th - 1) / 2 + dy, z, m);
                }
        }
        for (int z = -Half + 1; z < Half - 1; z++) g.Dot(x, y + Brace, z, Mat.Linen);
        g.Tip = (-b.FistX - .5f, (y + Brace) / 4f, Half / 4f);
    }

    /// <summary>
    /// A round shield of planks on the left forearm, strapped to it: the planks with dark seams between them, painted in
    /// the garrison's blue with a gold cross worn through here and there to the wood, an iron rim standing proud of the
    /// face, a domed steel boss with a ring of brass rivets round it and more round the rim. It stands clear of the body
    /// in front of the arm.
    /// </summary>
    static void Shield(Sculpt s, Build b, byte field = Mat.ClothBlue, byte charge = Mat.ClothOchre)
    {
        var g = s.Hold(Off, DressKind.Shield, Tissue.Armor);
        const int q = 4;
        float cx = (-b.ArmX0 - b.ArmW / 2f) * q, cy = (b.HandY + b.Hand + 1.5f) * q, R = 4.7f * q;
        int zp = (b.Z1 + 2) * q + 1;
        Straps(g, b, (int)cx, zp);
        for (int yy = (int)(cy - R) - 1; yy <= cy + R; yy++)
            for (int xx = (int)(cx - R) - 1; xx <= cx + R; xx++)
            {
                float dx = xx + .5f - cx, dy = yy + .5f - cy, r = MathF.Sqrt(dx * dx + dy * dy);
                if (r > R) continue;
                if (r > R - 2.2f) { g.Fill(xx, yy, zp, xx + 1, yy + 1, zp + 3, Mat.Iron); continue; }
                int seam = (int)MathF.Floor(dx + 100) % 5;
                byte plank = seam == 0 ? Mat.BoardDark : Mat.Board;
                bool cross = MathF.Abs(dx) < 2.6f || MathF.Abs(dy) < 2.6f;
                byte paint = r > R - 6 && Dice(xx >> 2, yy >> 2, 0, 11) < .12f ? plank : cross ? charge : field;
                g.Dot(xx, yy, zp, plank);
                g.Dot(xx, yy, zp + 1, paint);
            }
        Boss(g, cx, cy, zp + 2);
        for (int i = 0; i < 8; i++)
        {
            float an = (i + .5f) * MathF.PI / 4;
            g.Dot((int)MathF.Floor(cx + MathF.Cos(an) * 7.5f), (int)MathF.Floor(cy + MathF.Sin(an) * 7.5f), zp + 2, Mat.Brass);
        }
        for (int i = 0; i < 8; i++)
        {
            float an = i * MathF.PI / 4;
            g.Dot((int)MathF.Floor(cx + MathF.Cos(an) * (R - 1.1f)), (int)MathF.Floor(cy + MathF.Sin(an) * (R - 1.1f)), zp + 3, Mat.Brass);
        }
        g.Grip = (cx / q, cy / q, zp / (float)q);
        g.Tip = (cx / q, cy / q, (zp + 6) / (float)q);
        g.Edge = (0, 0, 1);
    }

    /// <summary>A shield's boss: a steel dome in layers before its face, a flange round it.</summary>
    static void Boss(Gear g, float cx, float cy, int z)
    {
        g.Plate(z, 1, cx, cy, 5.6f, Mat.Steel);
        g.Plate(z + 1, 1, cx, cy, 4.6f, Mat.Steel);
        g.Plate(z + 2, 1, cx, cy, 3.6f, Mat.Steel);
        g.Plate(z + 3, 1, cx, cy, 2.1f, Mat.Steel);
    }

    /// <summary>The leather straps a shield is borne by, from the forearm's front to the shield's back at zp.</summary>
    static void Straps(Gear g, Build b, int cx, int zp)
    {
        int y0 = (b.HandY + 1) * 4;
        g.Fill(cx - 3, y0, b.ArmZ1 * 4, cx + 3, y0 + 3, zp, Mat.Leather);
        g.Fill(cx - 3, y0 + 6, b.ArmZ1 * 4, cx + 3, y0 + 9, zp, Mat.Leather);
    }

    // ---------------------------------------------------------------- people

    static readonly byte[] HumanSkins = { Mat.SkinHuman, Mat.SkinTan, Mat.SkinPale };
    static readonly byte[] Hairs = { Mat.Hair, Mat.HairBrown, Mat.HairGinger, Mat.HairGrey };

    static BodyPlan Worker(int look)
    {
        var f = new BodyPlan { Chop = .75f };
        byte hair = Hairs[(look + look / 4) % 4];
        var b = new Build
        {
            Skin = HumanSkins[look % 3 == 2 ? 1 : look % 5 == 4 ? 2 : 0], Hair = hair, HairStyle = look % 3 == 2 ? 2 : look % 2,
            Shirt = new[] { Mat.Linen, Mat.ClothBrown, Mat.Linen, Mat.ClothGreen }[look % 4], Trousers = look % 3 == 1 ? Mat.ClothBrown : Mat.ClothDark,
            Leg = look == 5 ? 11 : 10,
        };
        var s = Humanoid(b, f);
        int hx = b.HeadW, hz0 = b.HeadZ, hz1 = b.HZ1, hy1 = b.Crown;
        // sleeves rolled to the elbow on some; a leather apron from the chest over the thighs
        if (look % 2 == 1) s.Paint(Main, b.ArmX0, b.HandY, b.ArmZ0, -b.Chest, b.HandY + b.Hand + 3, b.ArmZ1, b.Skin);
        if (look % 2 == 1) s.Paint(Off, b.Chest, b.HandY, b.ArmZ0, -b.ArmX0, b.HandY + b.Hand + 3, b.ArmZ1, b.Skin);
        s.Box(Body, -3, b.Waist - 3, b.Z1, 3, b.Neck - 3, b.Z1 + 1, Mat.Leather);
        s.Box(Body, -3, b.Neck - 3, b.Z1 - 1, -2, b.Neck, b.Z1, Mat.Leather);
        s.Box(Body, 2, b.Neck - 3, b.Z1 - 1, 3, b.Neck, b.Z1, Mat.Leather);
        // a felt cap with a peak, a straw hat with a wide brim, the bare head, or a hood
        switch (look % 4)
        {
            case 0:
                s.Paint(Body, -hx, hy1 - 2, hz0, hx, hy1, hz1, Mat.Leather);
                s.Box(Body, -hx + 1, hy1, hz0 + 1, hx - 1, hy1 + 1, hz1 - 1, Mat.Leather);
                s.Box(Body, -3, hy1 - 1, hz1 - 1, 3, hy1, hz1 + 2, Mat.Leather);
                break;
            case 1:
                s.Flat(Body, hy1 - 1, 6.4f, Mat.Straw);
                s.Flat(Body, hy1, 3.3f, Mat.Leather);
                s.Flat(Body, hy1 + 1, 2.9f, Mat.Straw);
                break;
            case 3:
                Hood(s, b, Mat.ClothBrown);
                break;
        }
        if (look % 3 == 1 || look == 6) Beard(s, b, hair, look != 6);
        WorkerDetail(s, b, look);
        Pick(s, b, Mat.Iron);
        return Done(s, f, b);
    }

    static BodyPlan Archer(int look)
    {
        var f = new BodyPlan { Chop = .9f };
        byte hood = new[] { Mat.ClothDark, Mat.ClothGreen, Mat.ClothBrown }[look % 3];
        var b = new Build
        {
            Shirt = Mat.ClothBlue, Trousers = Mat.ClothDark, Sleeve = Mat.ClothBlue, Cuff = Mat.Leather, Skin = HumanSkins[look / 3 % 2 == 1 ? 1 : 0],
            Hair = Hairs[look % 4], HairStyle = 1,
        };
        var s = Humanoid(b, f);
        // a leather bracer on the bow arm, a strap across the chest to the quiver, the quiver on the back with its fletchings
        s.Paint(Off, b.Chest, b.HandY + b.Hand, b.ArmZ0, -b.ArmX0, b.HandY + b.Hand + 3, b.ArmZ1, Mat.Leather);
        for (int y = b.Waist + 1; y < b.Neck - 1; y++)
        {
            int x = b.Chest - 1 - (y - b.Waist - 1) * (2 * b.Chest - 2) / (b.Torso - 3);
            s.Paint(Body, x - 1, y, b.Z0, x + 1, y + 1, b.Z1, Mat.Leather);
        }
        Hood(s, b, hood);
        s.Box(Body, -3, b.Waist + 1, b.Z0 - 2, -1, b.Neck - 1, b.Z0, Mat.Leather, Tissue.Gear);
        foreach (var (x, y, c) in new[] { (-3, 0, Mat.Linen), (-2, 1, Mat.ClothRed), (-2, 0, Mat.Linen), (-3, 1, Mat.Linen) })
            s.Dot(Body, x, b.Neck - 1 + y, b.Z0 - 2 + (x + 3), c, Tissue.Gear);
        if (look == 4) Beard(s, b, b.Hair, false);
        ArcherDetail(s, b);
        Bow(s, b);
        return Done(s, f, b);
    }

    static BodyPlan Swordsman(int look)
    {
        var f = new BodyPlan { Chop = .8f, Carry = -.35f, Shield = true };
        var b = new Build
        {
            Shirt = Mat.Mail, Trousers = Mat.ClothDark, Sleeve = Mat.Mail, Belt = Mat.Leather, Skin = HumanSkins[look % 2], Hair = Hairs[(look + 1) % 4], HairStyle = 1,
        };
        var s = Humanoid(b, f);
        int hx = b.HeadW, hz0 = b.HeadZ, hz1 = b.HZ1, hy0 = b.Neck, hy1 = b.Crown;
        // mail on the body and the arms, its skirt hanging round the hips from a broad belt
        Mail(s, b);
        Skirt(s, b, Mat.Mail, Tissue.Armor);
        // a blue tabard over it with a gold cross, falling below the belt to a gold hem, gold along its edges near
        s.Box(Body, -3, b.Waist + 1, b.Z1, 3, b.Neck - 1, b.Z1 + 1, Mat.ClothBlue);
        s.Box(Body, -3, b.Waist - 3, b.Z1 + 1, 3, b.Waist, b.Z1 + 2, Mat.ClothBlue);
        s.Box(Body, -3, b.Waist - 3, b.Z1 + 1, 3, b.Waist - 2, b.Z1 + 2, Mat.ClothOchre);
        s.Box(Body, -1, b.Waist + 3, b.Z1, 1, b.Neck - 2, b.Z1 + 1, Mat.ClothOchre);
        s.Box(Body, -2, b.Neck - 5, b.Z1, 2, b.Neck - 4, b.Z1 + 1, Mat.ClothOchre);
        s.FPaint(Body, -3, b.Waist - 3, b.Z1, -2.5f, b.Neck - 1, b.Z1 + 2, Mat.ClothOchre);
        s.FPaint(Body, 2.5f, b.Waist - 3, b.Z1, 3, b.Neck - 1, b.Z1 + 2, Mat.ClothOchre);
        s.FPaint(Body, -.5f, b.Waist + 3, b.Z1, .5f, b.Neck - 2, b.Z1 + 1, Mat.ClothOchre);
        s.FPaint(Body, -1, b.Waist + 3, b.Z1, -.5f, b.Neck - 2, b.Z1 + 1, Mat.ClothBlue);
        s.FPaint(Body, .5f, b.Waist + 3, b.Z1, 1, b.Neck - 2, b.Z1 + 1, Mat.ClothBlue);
        s.FPaint(Body, -2, b.Neck - 5, b.Z1, 2, b.Neck - 4.5f, b.Z1 + 1, Mat.ClothBlue);
        s.FPaint(Body, -.5f, b.Neck - 5, b.Z1, .5f, b.Neck - 4.5f, b.Z1 + 1, Mat.ClothOchre);
        // steel on the shoulders; a scabbard at the left hip, a pouch at the right
        Spaulders(s, b);
        Scabbard(s, b);
        Pouch(s, b, -1);
        // a kettle hat, a nasal helm, or a mail coif over a bearded face
        switch (look % 3)
        {
            case 0: KettleHat(s, b); break;
            case 1: NasalHelm(s, b, aventail: true); break;
            default: Coif(s, b); Beard(s, b, b.Hair); break;
        }
        Sword(s, b);
        Shield(s, b);
        return Done(s, f, b);
    }

    /// <summary>
    /// A staff held upright in the right fist from the ground to over the head (its top <paramref name="top"/>
    /// half-voxels up): its cells, the column and the depth its shaft stands in.
    /// </summary>
    static Gear StaffIn(Sculpt s, Build b, out int x, out int zc)
    {
        var g = InFist(s, b, out x, out _, out _);
        zc = (b.ArmZ0 + b.ArmZ1) * 2 - 1;
        return g;
    }

    /// <summary>
    /// A skull a little bigger than a fist, facing +z with its middle at (cx, cy, cz) in cells: the dome of the cranium,
    /// dark sockets with a fire in their depths, the hole of the nose, a row of teeth and the jaw under them.
    /// </summary>
    static void Skull(Gear g, float cx, float cy, float cz, float r, byte fire)
    {
        for (int yy = (int)(cy - r * 1.3f); yy <= cy + r; yy++)
            for (int z = (int)(cz - r * 1.2f); z <= cz + r; z++)
                for (int xx = (int)(cx - r) - 1; xx <= cx + r; xx++)
                {
                    float dx = (xx + .5f - cx) / r, dy = (yy + .5f - cy) / (r * .95f), dz = (z + .5f - (cz - r * .12f)) / (r * 1.1f);
                    bool dome = dx * dx + dy * dy + dz * dz <= 1 && yy + .5f > cy - r * .55f;
                    // the jaw, narrower, under the cheekbones and forward
                    bool jaw = yy + .5f <= cy - r * .45f && yy + .5f > cy - r * 1.25f && MathF.Abs(dx) < .55f && z + .5f > cz - r * .2f && z + .5f < cz + r * .82f;
                    if (dome || jaw) g.Dot(xx, yy, z, Mat.Bone);
                }
        int front = (int)MathF.Floor(cz + r * .9f);
        foreach (int sgn in new[] { -1, 1 })
        {
            float ex = cx + sgn * r * .42f, ey = cy - r * .08f;
            for (int yy = (int)(ey - 2); yy <= ey + 1; yy++)
                for (int xx = (int)(ex - 2); xx <= ex + 1; xx++)
                    if ((xx + .5f - ex) * (xx + .5f - ex) + (yy + .5f - ey) * (yy + .5f - ey) <= 3.2f)
                        for (int z = front - 3; z <= front + 1; z++) if (g.Has(xx, yy, z)) g.Dot(xx, yy, z, z == front - 3 ? fire : Mat.Maw);
        }
        // the nose, and the teeth between the jaws
        for (int z = front - 1; z <= front + 1; z++) { g.Dot((int)MathF.Floor(cx), (int)(cy - r * .42f), z, Mat.Maw); g.Dot((int)MathF.Floor(cx) - 1, (int)(cy - r * .42f), z, Mat.Maw); }
        int ty = (int)MathF.Floor(cy - r * .62f);
        for (int xx = (int)(cx - r * .5f); xx < cx + r * .5f; xx++)
            for (int z = front - 4; z <= front + 1; z++)
                if (g.Has(xx, ty, z)) g.Dot(xx, ty, z, (xx & 1) == 0 ? Mat.Bone : Mat.Maw);
    }

    /// <summary>
    /// A priest's staff: a shaft with its knots through the fist from the ground to over the head, and bound at its top
    /// with a few turns of cord a skull with the pale fire in its eyes, a dark feather hanging from the cord.
    /// </summary>
    static void Staff(Sculpt s, Build b)
    {
        var g = StaffIn(s, b, out int x, out int zc);
        int top = (b.Crown + 3) * 4;
        g.Fill(x, 4, zc, x + 2, top, zc + 2, Mat.Handle);
        foreach (int k in new[] { 22, 49, 83 }) if (k < top - 8) g.Fill(x - 1, k, zc - 1, x + 3, k + 2, zc + 3, Mat.Handle);
        g.Fill(x, 2, zc, x + 2, 4, zc + 2, Mat.Iron);
        for (int yy = top - 5; yy < top; yy++) g.Fill(x - 1, yy, zc - 1, x + 3, yy + 1, zc + 3, (yy & 1) == 0 ? Mat.RobeTrim : Mat.Linen);
        Skull(g, x + 1, top + 5, zc + 1, 5.2f, Mat.SoulFire);
        for (int k = 0; k < 9; k++) g.Dot(x + 3, top - 4 - k, zc + 1 + k / 4, k < 6 ? Mat.ClothDark : Mat.Hair);
        g.Tip = Half(g, x + 1, top + 5, zc + 1);
        g.Edge = (0, 0, 1);
    }

    /// <summary>
    /// A rod of a staff bending as it goes: through (x, y, z) in cells, point to point, r cells thick either side.
    /// </summary>
    static void Crooked(Gear g, (float x, float y, float z)[] at, float r, byte m)
    {
        for (int i = 0; i + 1 < at.Length; i++)
            g.Rod(at[i].x / g.C.R, at[i].y / g.C.R, at[i].z / g.C.R, at[i + 1].x / g.C.R, at[i + 1].y / g.C.R, at[i + 1].z / g.C.R, r / g.C.R, m);
    }

    /// <summary>
    /// A goblin shaman's staff: a crooked shaft with a horn bound at its head, curving out and up, an ember glowing in
    /// its mouth; feathers of red and ochre hang from under it and a string of bone beads.
    /// </summary>
    static void HornStaff(Sculpt s, Build b)
    {
        var g = StaffIn(s, b, out int x, out int zc);
        float cx = x + 1, cz = zc + 1;
        int top = (b.Crown + 4) * 4;
        Crooked(g, new[] { (cx, 3f, cz), (cx + 1, top * .35f, cz - 1), (cx - 1, top * .7f, cz + 1), (cx, top, cz) }, 1.1f, Mat.Handle);
        // the horn: from the head of the staff, curving out and up, wide at its mouth
        for (int i = 0; i <= 12; i++)
        {
            float t = i / 12f, an = t * MathF.PI * .55f;
            float hx = cx - 7 * (1 - MathF.Cos(an)) * 1.4f, hy = top + 9 * MathF.Sin(an);
            g.Rod(hx / 4, hy / 4, cz / 4, hx / 4, (hy + .8f) / 4, cz / 4, (1.1f + 2.2f * t) / 4, i == 12 ? Mat.Ember : (i & 2) == 0 ? Mat.Horn : Mat.Bone);
        }
        g.Fill(x - 1, top - 3, zc - 1, x + 3, top, zc + 3, Mat.Leather);
        foreach (var (dx, len, m) in new[] { (-2, 9, Mat.ClothRed), (3, 7, Mat.ClothOchre), (1, 11, Mat.ClothRed) })
            for (int k = 0; k < len; k++) g.Dot(x + dx, top - 2 - k, zc + 1 + (k > len / 2 ? 1 : 0), m);
        for (int k = 0; k < 6; k++) g.Dot(x - 1 - k / 3, top - 3 - k, zc + 3, (k & 1) == 0 ? Mat.Bone : Mat.Hair);
        g.Tip = Half(g, cx - 9, top + 9, cz);
        g.Edge = (0, 0, 1);
    }

    /// <summary>A witch's staff: a crooked stick hooked over at its head, a bunch of herbs and flowers bound under the hook, a bone and a little pouch hung from it.</summary>
    static void HerbStaff(Sculpt s, Build b)
    {
        var g = StaffIn(s, b, out int x, out int zc);
        float cx = x + 1, cz = zc + 1;
        int top = (b.Crown + 2) * 4;
        Crooked(g, new[] { (cx, 3f, cz), (cx - 1, top * .4f, cz + 1), (cx + 1, top * .8f, cz), (cx, top, cz), (cx - 4, top + 4, cz), (cx - 7, top + 2, cz) }, 1f, Mat.Handle);
        for (int i = 0; i < 26; i++)
        {
            int hx = x - 7 + (int)(Dice(i, 1, 2, 61) * 6), hy = top - 4 + (int)(Dice(i, 3, 4, 62) * 7), hz = zc - 2 + (int)(Dice(i, 5, 6, 63) * 6);
            g.Dot(hx, hy, hz, i % 7 == 0 ? Mat.Flower : i % 5 == 0 ? Mat.Bloom : Mat.FoliageLight);
            g.Dot(hx, hy - 1, hz, Mat.Foliage);
        }
        for (int k = 0; k < 7; k++) g.Dot(x + 2, top - 3 - k, zc + 1, k < 4 ? Mat.Linen : Mat.Bone);
        g.Fill(x + 1, top - 13, zc, x + 4, top - 10, zc + 2, Mat.Bone);
        g.Fill(x - 2, top - 12, zc + 2, x + 1, top - 8, zc + 4, Mat.Leather);
        g.Tip = Half(g, cx - 5, top + 3, cz);
        g.Edge = (0, 0, 1);
    }

    /// <summary>A necromancer's staff: a dark shaft, a ring of small bones bound under a skull with the pale fire in its sockets, a black rag hanging.</summary>
    static void SkullStaff(Sculpt s, Build b)
    {
        var g = StaffIn(s, b, out int x, out int zc);
        int top = (b.Crown + 3) * 4;
        g.Fill(x, 4, zc, x + 2, top, zc + 2, Mat.Hair);
        foreach (int k in new[] { 30, 60, 90 }) if (k < top - 10) g.Fill(x - 1, k, zc - 1, x + 3, k + 1, zc + 3, Mat.Iron);
        for (int i = 0; i < 8; i++)
        {
            float an = i * MathF.PI / 4;
            int bx = (int)MathF.Floor(x + 1 + MathF.Cos(an) * 3.5f), bz = (int)MathF.Floor(zc + 1 + MathF.Sin(an) * 3.5f);
            g.Fill(bx, top - 5, bz, bx + 1, top - 1, bz + 1, Mat.Bone);
        }
        Skull(g, x + 1, top + 5, zc + 1, 5.6f, Mat.SoulFire);
        for (int k = 0; k < 12; k++) g.Fill(x - 2, top - 4 - k, zc + 2 + k / 5, x, top - 3 - k, zc + 3 + k / 5, Mat.ClothDark);
        g.Tip = Half(g, x + 1, top + 5, zc + 1);
        g.Edge = (0, 0, 1);
    }

    /// <summary>
    /// A priest of the old powers: a robe to the ankles dark as old blood (or black), its hem and a rope girdle in tarnished
    /// trim, the hood up over a pale face, a bone charm on the chest, and a staff taller than itself in the right hand.
    /// </summary>
    static BodyPlan Priest(int look)
    {
        var f = new BodyPlan { Chop = 1.2f, Stride = 7 };
        byte robe = look == 1 ? Mat.ClothDark : Mat.Robe;
        var b = new Build
        {
            Shirt = robe, Trousers = robe, Sleeve = robe, Boots = Mat.Leather, Belt = Mat.RobeTrim, Boot = 2,
            Skin = look == 2 ? Mat.SkinTan : Mat.SkinPale, Hair = Hairs[(look + 3) % 4], HairStyle = 3,
        };
        var s = Humanoid(b, f);
        // the skirt of the robe down to the ankles, a little wider than the legs, a slit at the front, the hem in trim
        s.Box(Body, -b.Chest, 3, b.Z0 - 1, b.Chest, b.Waist, b.Z1 + 1, robe);
        s.Cut(Body, -1, 3, b.Z1, 1, 7, b.Z1 + 1, both: false);
        s.Paint(Body, -b.Chest, 3, b.Z0 - 1, b.Chest, 4, b.Z1 + 1, Mat.RobeTrim);
        // the girdle's ends hanging, wide sleeves, the charm
        s.Box(Body, 1, b.Waist - 4, b.Z1 + 1, 2, b.Waist, b.Z1 + 2, Mat.RobeTrim);
        s.Box(Body, 2, b.Waist - 3, b.Z1 + 1, 3, b.Waist, b.Z1 + 2, Mat.RobeTrim);
        s.Pair(Main, Off, b.ArmX0 - 1, b.HandY + b.Hand, b.ArmZ0, b.ArmX0, b.HandY + b.Hand + 3, b.ArmZ1, robe);
        s.Box(Body, -1, b.Neck - 5, b.Z1, 1, b.Neck - 3, b.Z1 + 1, Mat.Bone, Tissue.Gear);
        s.Dot(Body, 0, b.Neck - 3, b.Z1, Mat.RobeTrim, Tissue.Gear);
        Hood(s, b, robe);
        // the trim round the face
        s.Paint(Body, -b.HeadW, b.Neck, b.HZ1 - 2, -b.HeadW + 1, b.Crown, b.HZ1, Mat.RobeTrim);
        s.Paint(Body, b.HeadW - 1, b.Neck, b.HZ1 - 2, b.HeadW, b.Crown, b.HZ1, Mat.RobeTrim);
        if (look == 0) Beard(s, b, Mat.HairGrey);
        PriestDetail(s, b);
        Staff(s, b);
        return Done(s, f, b);
    }

    /// <summary>
    /// One of the dead raised: bare bones held together by nothing that should hold them — the ribs showing in dark
    /// gaps, the belly gone to the spine — fire in its eye sockets (pale for ours, an ember's for the horde's thralls), a
    /// rag about its hips, a rusted blade; on one a dented helmet still.
    /// </summary>
    static BodyPlan Bones(int look, byte eye, byte rag)
    {
        var f = new BodyPlan { Chop = .7f, Carry = -.3f, Stride = 8 };
        var b = new Build
        {
            Skin = Mat.Bone, Shirt = Mat.Bone, Trousers = Mat.Bone, Sleeve = Mat.Bone, Boots = Mat.Bone, Belt = rag, Hair = Mat.Bone, Brow = Mat.Bone,
            Eye = eye, Mouth = Mat.Hair, HairStyle = 3, Boot = 0, Chest = 3, LegW = 2, Gap = 1,
        };
        var s = Humanoid(b, f);
        // the ribs across the chest, the belly gone to the spine
        for (int r = b.Waist + 4; r < b.Neck - 1; r += 2) s.Paint(Body, -b.Chest + 1, r, b.Z1 - 1, b.Chest - 1, r + 1, b.Z1, Mat.Hair);
        s.Cut(Body, -b.Chest, b.Waist + 1, b.Z0, -1, b.Waist + 3, b.Z1, both: true);
        // the eye sockets dark round the fire, the nose a hole, teeth
        s.Paint(Body, -b.HeadW + 1, b.Neck + 2, b.FaceZ, b.HeadW - 1, b.Neck + 3, b.HZ1, Mat.Hair);
        s.Pair(Body, Body, -2, b.Neck + 1, b.FaceZ, -1, b.Neck + 2, b.HZ1, Mat.Bone);
        // a rag about the hips
        s.Box(Body, -b.Chest, b.Waist - 2, b.Z0, b.Chest, b.Waist + 1, b.Z1, rag);
        s.Cut(Body, -1, b.Waist - 2, b.Z1 - 1, 1, b.Waist - 1, b.Z1, both: false);
        if (look == 1)
        {
            s.Paint(Body, -b.HeadW, b.Crown - 2, b.HeadZ, b.HeadW, b.Crown, b.HZ1, Mat.Iron, Tissue.Armor);
            s.Box(Body, -1, b.Crown, -1, 1, b.Crown + 1, 1, Mat.Iron, Tissue.Armor);
        }
        BonesDetail(s, b, look, eye);
        // a rusted blade, notched, its grip bound in rag, a bar of iron for its guard
        var g = InFist(s, b, out int x, out int y, out int z0);
        g.Wound(x, y, b.ArmZ0 * 4 - 3, z0, 2, Mat.Rag, rag);
        g.Fill(x - 1, y, b.ArmZ0 * 4 - 5, x + 3, y + 2, b.ArmZ0 * 4 - 3, Mat.Iron);
        g.Fill(x, y - 5, z0, x + 2, y + 7, z0 + 2, Mat.Iron);
        Blade(g, x, y + 1, z0 + 2, z0 + 44, 2.8f, .5f, Mat.Iron, true, look);
        g.Tip = Half(g, x + 1, y + 1, z0 + 44);
        return Done(s, f, b);
    }

    // ---------------------------------------------------------------- our troops: what they wear

    /// <summary>Mail over the body and down the arms to the wrists, its rows of rings showing near, a brass hem at the sleeves.</summary>
    static void Mail(Sculpt s, Build b, byte m = Mat.Mail)
    {
        s.Paint(Body, -b.Chest, b.Waist + 1, b.Z0, b.Chest, b.Neck, b.Z1, m, Tissue.Armor);
        s.Paint(Main, b.ArmX0, b.HandY + b.Hand, b.ArmZ0, -b.Chest, b.Neck, b.ArmZ1, m, Tissue.Armor);
        s.Paint(Off, b.Chest, b.HandY + b.Hand, b.ArmZ0, -b.ArmX0, b.Neck, b.ArmZ1, m, Tissue.Armor);
        if (m != Mat.Mail) return;
        int cuff = (b.HandY + b.Hand) * 2;
        s.FPattern(Main, -80, cuff, -80, 80, cuff + 1, 80, (x, y, z) => s.FMat(Main, x, y, z) == Mat.Mail ? Mat.Brass : (byte)0);
        s.FPattern(Off, -80, cuff, -80, 80, cuff + 1, 80, (x, y, z) => s.FMat(Off, x, y, z) == Mat.Mail ? Mat.Brass : (byte)0);
        Gauntlets(s, b);
    }

    /// <summary>
    /// Gauntlets of leather on the hands of one in mail; near, a cuff flared out a cell at the wrist, darker at its
    /// edge, and steel scales in rows over the back of the hand.
    /// </summary>
    static void Gauntlets(Sculpt s, Build b)
    {
        s.Paint(Main, b.ArmX0, b.HandY, b.ArmZ0, -b.Chest, b.HandY + b.Hand, b.ArmZ1, Mat.Leather, Tissue.Armor);
        s.Paint(Off, b.Chest, b.HandY, b.ArmZ0, -b.ArmX0, b.HandY + b.Hand, b.ArmZ1, Mat.Leather, Tissue.Armor);
        if (!s.Detail) return;
        int ax0 = b.ArmX0 * 2, ax1 = -b.Chest * 2, az0 = b.ArmZ0 * 2, az1 = b.ArmZ1 * 2, hand = b.HandY * 2, wrist = (b.HandY + b.Hand) * 2;
        foreach (var (p, sg) in new[] { (Main, 1), (Off, -1) })
        {
            int X(int x) => sg > 0 ? x : -1 - x;
            // the thumb laid along the fist, and whatever else of the hand still shows its skin
            s.FPattern(p, -80, hand - 1, -80, 80, wrist + 1, 80, (x, y, z) => s.FMat(p, x, y, z) == b.Skin ? Mat.Leather : (byte)0);
            for (int z = az0 - 1; z <= az1; z++)
                for (int x = ax0 - 1; x <= ax1; x++)
                    if (x == ax0 - 1 || x == ax1 || z == az0 - 1 || z == az1)
                    {
                        s.FSet(p, X(x), wrist - 1, z, Mat.Leather, Tissue.Armor);
                        s.FSet(p, X(x), wrist - 2, z, Mat.LeatherDark, Tissue.Armor);
                    }
            for (int y = hand + 1; y < wrist - 2; y += 2)
                for (int z = az0 + 1; z < az1 - 1; z++) s.FPut(p, X(ax0), y, z, Mat.Steel);
        }
    }

    /// <summary>
    /// A broad belt and a skirt hanging from it round the hips, of mail or of quilted cloth, a brass buckle before; near,
    /// the buckle is a frame with its tongue, the mail's hem is brass and the quilting is stitched in rows.
    /// </summary>
    static void Skirt(Sculpt s, Build b, byte m, Tissue t, int rows = 3)
    {
        s.Box(Body, -b.Chest - 1, b.Waist, b.Z0 - 1, b.Chest + 1, b.Waist + 1, b.Z1 + 1, Mat.Leather);
        for (int y = b.Waist - rows; y < b.Waist; y++)
        {
            s.Box(Body, -b.Chest - 1, y, b.Z0 - 1, b.Chest + 1, y + 1, b.Z0, m, t);
            s.Box(Body, -b.Chest - 1, y, b.Z1, b.Chest + 1, y + 1, b.Z1 + 1, m, t);
            s.Pair(Body, Body, -b.Chest - 1, y, b.Z0, -b.Chest, y + 1, b.Z1, m, t);
        }
        s.Box(Body, -1, b.Waist, b.Z1 + 1, 1, b.Waist + 1, b.Z1 + 2, Mat.Brass, Tissue.Armor);
        Buckle(s, b.Waist, b.Z1 + 1);
        int hem = (b.Waist - rows) * 2;
        if (m == Mat.Mail)
            s.FPattern(Body, -80, hem, -80, 80, hem + 1, 80, (x, y, z) => s.FMat(Body, x, y, z) == Mat.Mail ? Mat.Brass : (byte)0);
        else if (m == Mat.Gambeson)
            s.FPattern(Body, -80, hem, -80, 80, b.Waist * 2, 80, (x, y, z) => s.FMat(Body, x, y, z) == Mat.Gambeson && ((x + 80) % 3 == 0 || (y - hem) % 4 == 3) ? Mat.Linen : (byte)0);
    }

    /// <summary>
    /// A belt's buckle drawn finer, before the half-voxel at (y, z) the plain one fills: a brass frame standing out of the
    /// belt with the leather showing in it and the tongue across.
    /// </summary>
    static void Buckle(Sculpt s, int y, int z)
    {
        int y0 = y * 2, f = z * 2 + 1;
        for (int yy = y0 - 1; yy < y0 + 3; yy++)
            for (int x = -2; x < 2; x++)
            {
                bool frame = yy == y0 - 1 || yy == y0 + 2 || x == -2 || x == 1;
                if (frame) s.FSet(Body, x, yy, f, Mat.Brass, Tissue.Armor);
                else s.FSet(Body, x, yy, f, x == 0 ? Mat.Brass : Mat.LeatherDark, Tissue.Armor);
            }
    }

    /// <summary>A pouch on the belt at one hip (+1 the left, -1 the right): a leather bag, its flap darker, a brass stud.</summary>
    static void Pouch(Sculpt s, Build b, int side)
    {
        if (!s.Detail) return;
        int y = b.Waist * 2 - 5, x0 = side > 0 ? b.Chest * 2 - 6 : -b.Chest * 2 + 2, z = b.Z1 * 2;
        for (int yy = y; yy < y + 6; yy++)
            for (int x = x0; x < x0 + 4; x++)
                for (int k = 0; k < 2; k++)
                    s.FSet(Body, x, yy, z + k, yy >= y + 3 && k == 1 ? Mat.LeatherDark : Mat.Leather, Tissue.Gear);
        s.FSet(Body, x0 + 1, y + 3, z + 2, Mat.Brass, Tissue.Gear);
        s.FSet(Body, x0 + 2, y + 3, z + 2, Mat.Brass, Tissue.Gear);
    }

    /// <summary>
    /// A scabbard hung from the belt at the left hip, slanting back past the thigh: dark leather, a steel locket at its
    /// mouth and a chape at its tip.
    /// </summary>
    static void Scabbard(Sculpt s, Build b, int len = 22)
    {
        if (!s.Detail) return;
        int x = b.Chest * 2, y0 = b.Waist * 2 + 1, z0 = b.Z1 * 2 - 2;
        for (int k = 0; k < len; k++)
        {
            int y = y0 - k, z = z0 - k * 2 / 5;
            byte m = k < 2 ? Mat.Steel : k >= len - 3 ? Mat.Steel : Mat.LeatherDark;
            for (int dz = 0; dz < 3; dz++) { s.FSet(Body, x, y, z + dz, m, Tissue.Gear); s.FSet(Body, x + 1, y, z + dz, m, Tissue.Gear); }
        }
        // the frog that holds it to the belt
        s.FSet(Body, x, y0 + 1, z0 + 1, Mat.Leather, Tissue.Gear);
    }

    /// <summary>
    /// A kettle hat: a steel crown with a broad brim all round; near, the brim thins and slopes down to a rolled edge,
    /// a ridge runs over the crown, a band of rivets goes round it and a strap under the chin holds it.
    /// </summary>
    static void KettleHat(Sculpt s, Build b)
    {
        int hx = b.HeadW, hz0 = b.HeadZ, hz1 = b.HZ1, hy0 = b.Neck, hy1 = b.Crown;
        s.Paint(Body, -hx, hy1 - 3, hz0, hx, hy1, hz1, Mat.Steel, Tissue.Armor);
        s.Flat(Body, hy1 - 3, 6.2f, Mat.Steel, Tissue.Armor);
        s.Flat(Body, hy1, 2.2f, Mat.Steel, Tissue.Armor);
        if (!s.Detail) return;
        int by = (hy1 - 3) * 2;
        for (int z = -14; z < 14; z++)
            for (int x = -14; x < 14; x++)
            {
                float r = MathF.Sqrt((x + .5f) * (x + .5f) + (z + .5f) * (z + .5f)) / 2;
                if (r > 6.2f || r < hx + .4f) continue;
                s.FDel(Body, x, by + 1, z);
                if (r > 5.1f) { s.FDel(Body, x, by, z); s.FSet(Body, x, by - 1, z, Mat.Steel, Tissue.Armor); }
                if (r > 5.75f) s.FSet(Body, x, by, z, Mat.Steel, Tissue.Armor);
            }
        for (int z = -4; z < 4; z++) { s.FSet(Body, -1, hy1 * 2 + 2, z, Mat.Steel, Tissue.Armor); s.FSet(Body, 0, hy1 * 2 + 2, z, Mat.Steel, Tissue.Armor); }
        s.FPattern(Body, -2 * hx - 1, by + 3, 2 * hz0 - 1, 2 * hx + 1, by + 4, 2 * hz1 + 1, (x, y, z) => s.FMat(Body, x, y, z) == Mat.Steel && (x + z + 64) % 3 == 0 ? Mat.SteelDark : (byte)0);
        ChinStrap(s, b, by);
    }

    /// <summary>A leather strap from under a helmet's edge (row y, fine) down either cheek before the ears and under the chin.</summary>
    static void ChinStrap(Sculpt s, Build b, int y)
    {
        int hx = b.HeadW;
        for (int yy = b.Neck * 2; yy < y; yy++) { s.FPut(Body, -2 * hx, yy, 2, Mat.Leather); s.FPut(Body, 2 * hx - 1, yy, 2, Mat.Leather); }
        for (int x = -2 * hx; x < 2 * hx; x++) { s.FPut(Body, x, b.Neck * 2, 2, Mat.Leather); s.FPut(Body, x, b.Neck * 2, 3, Mat.Leather); }
    }

    /// <summary>A mail coif over the head and the shoulders, the face showing; near, its rows of rings and a leather lace across the brow.</summary>
    static void Coif(Sculpt s, Build b)
    {
        int hx = b.HeadW, hz0 = b.HeadZ, hz1 = b.HZ1, hy0 = b.Neck, hy1 = b.Crown;
        s.Coat(Body, -hx - 1, hy0, hz0 - 1, hx + 1, hy1 + 1, hz1 + 1, Mat.Mail, Tissue.Armor, (-hx + 1, hy0, hz1 - 2, hx - 1, hy1 - 2, hz1 + 2));
        s.Paint(Body, -b.Chest, b.Neck - 2, b.Z0, b.Chest, b.Neck, b.Z1, Mat.Mail, Tissue.Armor);
        int lace = (hy1 - 2) * 2;
        s.FPattern(Body, -2 * hx - 2, lace, -2 * hz0, 2 * hx + 2, lace + 1, 2 * hz1 + 2, (x, y, z) => s.FMat(Body, x, y, z) is Mat.Mail or Mat.SteelDark ? Mat.Leather : (byte)0);
    }

    /// <summary>
    /// A nasal helm: a steel cap with a rim and a nasal down over the nose; near, the cap is drawn up to a point, four
    /// brass bands run from the rim to the top, the rim is riveted, the nasal narrow over the bridge of the nose; with an
    /// aventail, mail hangs from the rim round the sides and the back to the shoulders, over the ears.
    /// </summary>
    static void NasalHelm(Sculpt s, Build b, bool aventail)
    {
        int hx = b.HeadW, hz0 = b.HeadZ, hz1 = b.HZ1, hy0 = b.Neck, hy1 = b.Crown, up = b.FaceUp;
        s.Paint(Body, -hx, hy1 - 3, hz0, hx, hy1, hz1, Mat.Steel, Tissue.Armor);
        s.Rim(Body, hy1 - 3, -hx, hz0, hx, hz1, Mat.Steel, Tissue.Armor);
        s.Box(Body, -2, hy1, -2, 2, hy1 + 1, 2, Mat.Steel, Tissue.Armor);
        s.Box(Body, -1, hy1 + 1, -1, 1, hy1 + 2, 1, Mat.Steel, Tissue.Armor);
        s.Box(Body, -1, hy0 + 2 + up, hz1, 1, hy1 - 3, hz1 + 1, Mat.Steel, Tissue.Armor);
        if (!s.Detail) return;
        int top = hy1 * 2, rim = (hy1 - 3) * 2, F = hz1 * 2, N = (hy0 + 2 + up) * 2;
        // the cap drawn up to a point
        s.FCut(Body, -2, hy1, -2, 2, hy1 + 2, 2);
        for (int k = 0; k < 6; k++)
        {
            float r = 6.6f - k * 1.15f;
            for (int z = -8; z < 8; z++)
                for (int x = -8; x < 8; x++)
                    if ((x + .5f) * (x + .5f) + (z + .5f) * (z + .5f) <= r * r) s.FSet(Body, x, top + k, z, Mat.Steel, Tissue.Armor);
        }
        // the rim a cell proud of the cap, not two, riveted; the bands from it to the top
        for (int z = 2 * hz0 - 2; z < 2 * hz1 + 2; z++)
            for (int x = -2 * hx - 2; x < 2 * hx + 2; x++)
            {
                bool outer = x < -2 * hx - 1 || x >= 2 * hx + 1 || z < 2 * hz0 - 1 || z >= 2 * hz1 + 1;
                if (outer && !(x is -1 or 0 && z >= F)) { s.FDel(Body, x, rim, z); s.FDel(Body, x, rim + 1, z); }
            }
        s.FPattern(Body, -2 * hx - 1, rim, 2 * hz0 - 1, 2 * hx + 1, rim + 2, 2 * hz1 + 1, (x, y, z) => s.FMat(Body, x, y, z) == Mat.Steel && y == rim + 1 && (x + z + 64) % 4 == 0 ? Mat.Brass : (byte)0);
        s.FPattern(Body, -2 * hx, rim + 2, 2 * hz0, 2 * hx, top + 6, 2 * hz1, (x, y, z) => s.FMat(Body, x, y, z) == Mat.Steel && (x is -1 or 0 || z is -1 or 0) ? Mat.Brass : (byte)0);
        // the nasal two cells across, the nose under its end
        for (int y = N; y < rim; y++)
        {
            foreach (int x in new[] { -2, 1 }) { s.FSet(Body, x, y, F, b.Skin); s.FDel(Body, x, y, F + 1); }
            foreach (int x in new[] { -1, 0 })
            {
                s.FSet(Body, x, y, F, y < N + 4 ? b.Skin : Mat.Steel, y < N + 4 ? Tissue.Skin : Tissue.Armor);
                if (y < N + 2) s.FSet(Body, x, y, F + 1, b.Skin); else s.FSet(Body, x, y, F + 1, Mat.Steel, Tissue.Armor);
            }
        }
        for (int y = N; y < N + 2; y++) for (int x = -1; x < 1; x++) s.FDel(Body, x, y, F + 2);
        if (aventail) Aventail(s, b, rim, false);
    }

    /// <summary>
    /// Mail hung from a helmet's lower edge (row y, fine) round the sides and the back of the head to the shoulders,
    /// over the ears, its hem in brass; a bascinet's comes round under the chin as well.
    /// </summary>
    static void Aventail(Sculpt s, Build b, int y, bool chin)
    {
        int hx = b.HeadW, x0 = -2 * hx - 1, x1 = 2 * hx, z0 = 2 * b.HeadZ - 1, z1 = chin ? 2 * b.HZ1 : 3, low = b.Neck * 2 - 3;
        for (int yy = low; yy < y; yy++)
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    bool shell = x == x0 || x == x1 || z == z0 || chin && z == z1 && yy < b.Neck * 2 + 2;
                    if (!shell) continue;
                    if (s.FHas(Body, x, yy, z) && s.FMat(Body, x, yy, z) != b.Skin) continue;
                    s.FSet(Body, x, yy, z, yy == low ? Mat.Brass : Mat.Mail, Tissue.Armor);
                }
        // the ears under it
        for (int yy = low; yy < y; yy++) for (int z = -2; z < 2; z++) { s.FDel(Body, x0 - 1, yy, z); s.FDel(Body, x1 + 1, yy, z); }
    }

    /// <summary>
    /// A sallet: a steel bowl down over the brow, its tail flaring out behind over the nape, a low ridge along the top;
    /// on some a visor down to the eyes with a slit to look through. Near, the tail sweeps back in steps, the ridge is
    /// narrow, and the visor has its breaths on the right and a bevor under it over the chin.
    /// </summary>
    static void Sallet(Sculpt s, Build b, bool visor)
    {
        int hx = b.HeadW, hz0 = b.HeadZ, hz1 = b.HZ1, hy0 = b.Neck, hy1 = b.Crown;
        s.Paint(Body, -hx, hy1 - 4, hz0, hx, hy1, hz1, Mat.Steel, Tissue.Armor);
        s.Box(Body, -hx + 1, hy1 - 5, hz0 - 2, hx - 1, hy1 - 3, hz0, Mat.Steel, Tissue.Armor);
        s.Box(Body, -1, hy1, hz0 + 1, 1, hy1 + 1, hz1 - 1, Mat.Steel, Tissue.Armor);
        if (visor)
        {
            s.Paint(Body, -hx, hy0 + 2, hz1 - 1, hx, hy1, hz1, Mat.Steel, Tissue.Armor);
            s.Paint(Body, -hx + 1, hy0 + 4, hz1 - 1, hx - 1, hy0 + 5, hz1, Mat.Hair);
        }
        if (!s.Detail) return;
        int bowl = (hy1 - 4) * 2, back = 2 * hz0;
        s.FCut(Body, -hx + 1, hy1 - 5, hz0 - 2, hx - 1, hy1 - 3, hz0);
        for (int k = 0; k < 7; k++)
        {
            int y = bowl + 1 - k, reach = 1 + k * 4 / 6, w = 2 * hx - 1 - k / 3;
            for (int x = -w; x < w; x++) for (int d = 1; d <= reach; d++) s.FSet(Body, x, y, back - d, Mat.Steel, Tissue.Armor);
        }
        for (int z = 2 * hz0 + 2; z < 2 * hz1 - 2; z++) { s.FDel(Body, -2, hy1 * 2, z); s.FDel(Body, 1, hy1 * 2, z); s.FDel(Body, -2, hy1 * 2 + 1, z); s.FDel(Body, 1, hy1 * 2 + 1, z); }
        if (!visor) return;
        int F = hz1 * 2 - 1, slit = (hy0 + 4) * 2;
        s.FPut(Body, -2 * hx + 2, slit + 1, F, Mat.Steel);
        for (int x = -2 * hx + 2; x < 2 * hx - 2; x++) s.FPut(Body, x, slit + 1, F, Mat.Steel);
        foreach (var (x, y) in new[] { (2, slit - 4), (4, slit - 4), (3, slit - 6), (5, slit - 6), (2, slit - 8), (4, slit - 8) }) s.FPut(Body, x, y, F, Mat.Hair);
        // the bevor over the chin and the throat
        for (int y = hy0 * 2 - 3; y < (hy0 + 2) * 2; y++)
            for (int x = -6; x < 6; x++) s.FSet(Body, x, y, F + 1, Mat.Steel, Tissue.Armor);
    }

    /// <summary>
    /// A bascinet with a pig-faced visor: a steel skull drawn up to a point at the back, a visor thrust out before the
    /// face in a snout with two slits over it to look through and rows of breaths in it, a boss on each side where it is
    /// hinged, and mail hung from its edge all round, over the shoulders and under the chin.
    /// </summary>
    static void Bascinet(Sculpt s, Build b)
    {
        int hx = b.HeadW, hz0 = b.HeadZ, hz1 = b.HZ1, hy0 = b.Neck, hy1 = b.Crown, eye = hy0 + 3;
        s.Paint(Body, -hx, hy0 + 1, hz0, hx, hy1, hz1, Mat.Steel, Tissue.Armor);
        s.Cut(Body, -hx - 1, eye, -1, -hx, eye + 2, 1, both: true);
        s.Box(Body, -3, eye - 2, hz1, 3, eye + 1, hz1 + 2, Mat.Steel, Tissue.Armor);
        s.Box(Body, -2, eye - 2, hz1 + 2, 2, eye, hz1 + 3, Mat.Steel, Tissue.Armor);
        s.Box(Body, -1, eye - 2, hz1 + 3, 1, eye - 1, hz1 + 4, Mat.Steel, Tissue.Armor);
        s.Box(Body, -1, hy1, hz0 + 1, 1, hy1 + 1, 1, Mat.Steel, Tissue.Armor);
        s.Paint(Body, -hx + 1, eye + 1, hz1 - 1, -1, eye + 2, hz1, Mat.Hair);
        s.Paint(Body, 1, eye + 1, hz1 - 1, hx - 1, eye + 2, hz1, Mat.Hair);
        s.Box(Body, -b.Chest, hy0 - 2, b.Z0 - 1, b.Chest, hy0, b.Z1 + 1, Mat.Mail, Tissue.Armor);
        if (!s.Detail) return;
        int F = hz1 * 2, cy = eye * 2 - 1;
        // the snout rounded into a cone, drawn down a little to its end
        for (int y = (eye - 2) * 2; y < (eye + 1) * 2; y++)
            for (int z = F; z < F + 8; z++)
                for (int x = -6; x < 6; x++)
                {
                    float k = (z - F) / 8f, r = 6.4f * (1 - k) + .9f, dx = x + .5f, dy = (y + .5f - (cy - k * 1.5f)) * 1.15f;
                    if (dx * dx + dy * dy > r * r) s.FDel(Body, x, y, z);
                    else s.FSet(Body, x, y, z, Mat.Steel, Tissue.Armor);
                }
        // the breaths: two rows of holes down each side of the snout
        for (int i = 0; i < 6; i++)
            foreach (int sg in new[] { 1, -1 })
            {
                int by = cy - 2 + i / 2 * 2 - (i & 1), bz = F + 1 + (i & 1) * 2 + i / 2;
                for (int k = 7; k >= 0; k--) { int bx = sg > 0 ? k : -1 - k; if (s.FHas(Body, bx, by, bz)) { s.FPut(Body, bx, by, bz, Mat.Hair); break; } }
            }
        // the slits one cell high under a brow standing out a cell
        int slit = (eye + 1) * 2;
        for (int x = -2 * hx + 2; x < 2 * hx - 2; x++)
        {
            s.FPut(Body, x, slit + 1, F - 1, Mat.Steel);
            if (x is < -1 or > 0) s.FSet(Body, x, slit + 2, F, Mat.Steel, Tissue.Armor);
        }
        // the point at the back of the skull, the hinges
        for (int k = 0; k < 4; k++) for (int x = -1; x < 1; x++) for (int z = 2 * hz0 + 2; z < 2 * hz0 + 6 - k; z++) s.FSet(Body, x, hy1 * 2 + 2 + k, z, Mat.Steel, Tissue.Armor);
        foreach (int x in new[] { -2 * hx - 1, 2 * hx }) { s.FSet(Body, x, slit, 2, Mat.Steel, Tissue.Armor); s.FSet(Body, x, slit + 1, 2, Mat.Steel, Tissue.Armor); }
        s.FPattern(Body, -80, (hy0 - 2) * 2, -80, 80, (hy0 - 2) * 2 + 1, 80, (x, y, z) => s.FMat(Body, x, y, z) is Mat.Mail or Mat.SteelDark ? Mat.Brass : (byte)0);
    }

    /// <summary>
    /// A great helm: a pot of steel over the whole head, flat on top, a slit across it to see through either side of a
    /// ridge down the face, holes below to breathe; on top a plume of feathers falling back, or a fan of a crest. Near,
    /// the slits are a cell high, the breaths are crosses on the right side, rows of brass rivets hold its plates, and
    /// the plume falls in separate feathers.
    /// </summary>
    static void GreatHelm(Sculpt s, Build b, byte plume, bool crest)
    {
        int hx = b.HeadW, hz0 = b.HeadZ, hz1 = b.HZ1, hy0 = b.Neck, hy1 = b.Crown, eye = hy0 + 4;
        s.Cut(Body, -hx - 1, hy0, hz0, -hx, hy1, hz1, both: true);
        s.Box(Body, -hx, hy0, hz0, hx, hy1, hz1, Mat.Steel, Tissue.Armor);
        s.Box(Body, -hx + 1, hy1, hz0 + 1, hx - 1, hy1 + 1, hz1 - 1, Mat.Steel, Tissue.Armor);
        s.Paint(Body, -hx + 1, eye, hz1 - 1, -1, eye + 1, hz1, Mat.Hair);
        s.Paint(Body, 1, eye, hz1 - 1, hx - 1, eye + 1, hz1, Mat.Hair);
        s.Box(Body, -1, hy0 + 1, hz1, 1, hy1, hz1 + 1, Mat.Steel, Tissue.Armor);
        foreach (int x in new[] { -3, 2 })
            for (int y = hy0 + 1; y < eye - 1; y++) s.Paint(Body, x, y, hz1 - 1, x + 1, y + 1, hz1, Mat.Hair);
        if (crest)
        {
            // a fan of a crest standing along the top, front to back
            s.Box(Body, 0, hy1 + 1, hz0 + 2, 1, hy1 + 4, hz1 - 2, plume, Tissue.Gear);
            s.Box(Body, 0, hy1 + 4, hz0 + 3, 1, hy1 + 5, hz1 - 3, plume, Tissue.Gear);
        }
        else
        {
            // a plume up from the top and falling back over the helm
            s.Box(Body, -1, hy1 + 1, -1, 1, hy1 + 4, 1, plume, Tissue.Gear);
            s.Box(Body, -1, hy1 + 3, hz0, 1, hy1 + 5, -1, plume, Tissue.Gear);
            s.Box(Body, -1, hy1 + 1, hz0 - 1, 1, hy1 + 4, hz0, plume, Tissue.Gear);
        }
        if (!s.Detail) return;
        int F = hz1 * 2 - 1, E = eye * 2;
        // the slits a cell high, the ridge narrower, the breaths in crosses on the right side only
        for (int x = -2 * hx + 2; x < 2 * hx - 2; x++) s.FPut(Body, x, E + 1, F, Mat.Steel);
        for (int y = hy0 * 2 + 2; y < E - 2; y++) foreach (int x in new[] { -6, -5, 4, 5 }) s.FPut(Body, x, y, F, Mat.Steel);
        for (int y = hy0 * 2 + 2; y < hy1 * 2; y++) { s.FDel(Body, -2, y, F + 2); s.FDel(Body, 1, y, F + 2); }
        foreach (var (cx, cy) in new[] { (4, E - 5), (4, E - 10), (1 + 2, E - 15) })
            foreach (var (dx, dy) in new[] { (0, 0), (1, 0), (-1, 0), (0, 1), (0, -1) }) s.FPut(Body, cx + dx, cy + dy, F, Mat.Hair);
        // the rivets along the band round the top and down the seam behind
        s.FPattern(Body, -2 * hx - 1, (hy1 - 2) * 2, 2 * hz0 - 1, 2 * hx + 1, (hy1 - 2) * 2 + 1, 2 * hz1 + 2, (x, y, z) => s.FMat(Body, x, y, z) == Mat.Steel && (x + z + 64) % 3 == 0 ? Mat.Brass : (byte)0);
        for (int y = hy0 * 2 + 1; y < hy1 * 2; y += 3) s.FPut(Body, 0, y, 2 * hz0, Mat.Brass);
        if (crest)
        {
            // the crest's edge cut in a fan of points
            for (int z = 2 * hz0 + 4; z < 2 * hz1 - 4; z++)
                if (((z + 64) % 3) == 0) { s.FDel(Body, 0, hy1 * 2 + 9, z); s.FDel(Body, 1, hy1 * 2 + 9, z); }
            return;
        }
        // the plume in feathers: every other column of it taken back a cell, their ends ragged
        for (int y = hy1 * 2 + 2; y < hy1 * 2 + 10; y++)
            for (int z = 2 * hz0 - 2; z < 2; z++)
                if (((z + y) & 1) == 0 && s.FHas(Body, -2, y, z) && Dice(y, z, 0, 71) < .5f) s.FDel(Body, -2, y, z);
    }

    /// <summary>
    /// Plate all over: the body, the arms with their gauntlets, the legs to the steel shoes; pauldrons on the shoulders.
    /// Near: the pauldrons in lames, a couter with its wing at each elbow, the gauntlets flared at the wrist with the
    /// plates of their fingers, a poleyn with its wing at each knee, the sabatons in lames.
    /// </summary>
    static void Plate(Sculpt s, Build b, bool legs = true)
    {
        s.Paint(Body, -b.Chest, b.Waist + 1, b.Z0, b.Chest, b.Neck, b.Z1, Mat.Steel, Tissue.Armor);
        s.Paint(Main, -40, -40, -40, 0, 120, 40, Mat.Steel, Tissue.Armor);
        s.Paint(Off, 0, -40, -40, 40, 120, 40, Mat.Steel, Tissue.Armor);
        if (legs)
        {
            s.Paint(LegR, -40, 0, -40, 40, 120, 40, Mat.Steel, Tissue.Armor);
            s.Paint(LegL, -40, 0, -40, 40, 120, 40, Mat.Steel, Tissue.Armor);
        }
        Spaulders(s, b);
        // the steel shoes' points; the plates of the belly in rows
        for (int y = b.Waist + 2; y < b.Waist + 6; y += 2) s.Paint(Body, -b.Chest, y, b.Z1 - 1, b.Chest, y + 1, b.Z1, Mat.Iron);
        if (!s.Detail) return;
        int ax0 = b.ArmX0 * 2, ax1 = -b.Chest * 2, az0 = b.ArmZ0 * 2, az1 = b.ArmZ1 * 2, elbow = (b.Neck + b.HandY + b.Hand) - 1, wrist = (b.HandY + b.Hand) * 2;
        foreach (var (p, sg) in new[] { (Main, 1), (Off, -1) })
        {
            int X(int x) => sg > 0 ? x : -1 - x;
            // the couter: a cap at the back of the elbow, a wing out on its outer side
            for (int y = elbow - 2; y < elbow + 3; y++)
                for (int x = ax0; x < ax1; x++) s.FSet(p, X(x), y, az0 - 1, Mat.Steel, Tissue.Armor);
            for (int y = elbow - 3; y < elbow + 4; y++)
                for (int z = az0; z < az0 + 5; z++)
                    if ((y - elbow) * (y - elbow) + (z - az0 - 1) * (z - az0 - 1) <= 9) s.FSet(p, X(ax0 - 1), y, z, Mat.Steel, Tissue.Armor);
            foreach (int y in new[] { elbow - 3, elbow + 3 }) s.FPattern(p, -80, y, -80, 80, y + 1, 80, (x, yy, z) => s.FMat(p, x, yy, z) == Mat.Steel ? Mat.SteelDark : (byte)0);
            // the gauntlet: its cuff flared out a cell, the plates over the fingers in rows
            for (int z = az0 - 1; z <= az1; z++) for (int x = ax0 - 1; x <= ax1; x++)
                    if (x == ax0 - 1 || x == ax1 || z == az0 - 1 || z == az1) { s.FSet(p, X(x), wrist - 1, z, Mat.Steel, Tissue.Armor); s.FSet(p, X(x), wrist - 2, z, Mat.Steel, Tissue.Armor); }
            for (int y = b.HandY * 2 + 1; y < wrist - 3; y += 2) s.FPattern(p, -80, y, -80, 80, y + 1, 80, (x, yy, z) => s.FMat(p, x, yy, z) == Mat.Steel ? Mat.SteelDark : (byte)0);
        }
        if (!legs) return;
        int lx0 = b.LegX0 * 2, lx1 = b.LegX1 * 2, lz1 = (b.LegD - b.LegD / 2) * 2, knee = b.Leg - 1;
        foreach (var (p, sg) in new[] { (LegR, 1), (LegL, -1) })
        {
            int X(int x) => sg > 0 ? x : -1 - x;
            for (int y = knee - 2; y < knee + 3; y++) for (int x = lx0; x < lx1; x++) s.FSet(p, X(x), y, lz1, Mat.Steel, Tissue.Armor);
            s.FSet(p, X(lx0 + 1), knee, lz1 + 1, Mat.Steel, Tissue.Armor); s.FSet(p, X(lx0 + 2), knee, lz1 + 1, Mat.Steel, Tissue.Armor);
            for (int y = knee - 3; y < knee + 4; y++)
                for (int z = lz1 - 5; z < lz1; z++)
                    if ((y - knee) * (y - knee) + (z - lz1 + 2) * (z - lz1 + 2) <= 8) s.FSet(p, X(lx0 - 1), y, z, Mat.Steel, Tissue.Armor);
            foreach (int y in new[] { knee - 3, knee + 3, 2, 4 }) s.FPattern(p, -80, y, -80, 80, y + 1, 80, (x, yy, z) => s.FMat(p, x, yy, z) == Mat.Steel ? Mat.SteelDark : (byte)0);
        }
    }

    /// <summary>Steel over the shoulders and down the upper arms, the top edge rounded off; near, in lames, its edges rolled.</summary>
    static void Spaulders(Sculpt s, Build b)
    {
        s.Box(Main, b.ArmX0 - 1, b.Neck - 3, b.ArmZ0 - 1, -b.Chest, b.Neck, b.ArmZ1 + 1, Mat.Steel, Tissue.Armor);
        s.Box(Off, b.Chest, b.Neck - 3, b.ArmZ0 - 1, -b.ArmX0 + 1, b.Neck, b.ArmZ1 + 1, Mat.Steel, Tissue.Armor);
        s.Cut(Main, b.ArmX0 - 1, b.Neck - 1, b.ArmZ0 - 1, b.ArmX0, b.Neck, b.ArmZ1 + 1);
        s.Cut(Off, -b.ArmX0, b.Neck - 1, b.ArmZ0 - 1, -b.ArmX0 + 1, b.Neck, b.ArmZ1 + 1);
        if (!s.Detail) return;
        int x0 = (b.ArmX0 - 1) * 2, z0 = (b.ArmZ0 - 1) * 2, z1 = (b.ArmZ1 + 1) * 2;
        foreach (var (p, sg) in new[] { (Main, 1), (Off, -1) })
        {
            int X(int x) => sg > 0 ? x : -1 - x;
            s.FRound(p, sg > 0 ? b.ArmX0 - 1 : b.Chest, b.Neck - 3, b.ArmZ0 - 1, sg > 0 ? -b.Chest : -b.ArmX0 + 1, b.Neck, b.ArmZ1 + 1, .9f, true, 1);
            for (int y = (b.Neck - 3) * 2; y < b.Neck * 2; y += 2)
                for (int z = z0; z < z1; z++)
                    for (int x = x0; x < -b.Chest * 2; x++)
                        if ((x == x0 || z == z0 || z == z1 - 1) && s.FMat(p, X(x), y, z) == Mat.Steel) s.FPut(p, X(x), y, z, Mat.SteelDark);
            // the lowest lame's edge rolled out a cell
            for (int z = z0; z < z1; z++) s.FSet(p, X(x0 - 1), (b.Neck - 3) * 2, z, Mat.Steel, Tissue.Armor);
        }
    }

    /// <summary>
    /// A surcoat over the plate: a panel of cloth before and behind, hung from the shoulders free of the steel and
    /// falling below the belt (a rider's only to the saddle), girt at the waist, a cross on the breast; near, its hem
    /// cut in dags.
    /// </summary>
    static void Surcoat(Sculpt s, Build b, byte cloth, byte cross, int drop = 5)
    {
        foreach (int z in new[] { b.Z1 + 1, b.Z0 - 2 })
            s.Box(Body, -b.Chest + 1, b.Waist - drop, z, b.Chest - 1, b.Neck - 1, z + 1, cloth, Tissue.Armor);
        s.Box(Body, -b.Chest + 1, b.Neck - 1, b.Z0 - 2, b.Chest - 1, b.Neck, b.Z1 + 2, cloth, Tissue.Armor);
        s.Box(Body, -b.Chest, b.Waist, b.Z0 - 2, b.Chest, b.Waist + 1, b.Z1 + 2, Mat.Leather);
        s.Paint(Body, -1, b.Waist + 1, b.Z1 + 1, 1, b.Neck - 1, b.Z1 + 2, cross);
        s.Paint(Body, -b.Chest + 1, b.Neck - 5, b.Z1 + 1, b.Chest - 1, b.Neck - 4, b.Z1 + 2, cross);
        if (!s.Detail || drop == 0) return;
        int hem = (b.Waist - drop) * 2;
        for (int x = (-b.Chest + 1) * 2; x < (b.Chest - 1) * 2; x++)
            if (((x + 64) / 2 & 1) == 0)
                foreach (int z in new[] { (b.Z1 + 1) * 2, (b.Z1 + 1) * 2 + 1, (b.Z0 - 2) * 2, (b.Z0 - 2) * 2 + 1 }) s.FDel(Body, x, hem, z);
        Buckle(s, b.Waist, b.Z1 + 1);
    }

    // ---------------------------------------------------------------- our troops: what they carry

    /// <summary>
    /// A spear: an ash shaft through the fist with its butt well behind in an iron shoe, a socket with two brass rivets,
    /// and a leaf of steel, broadest a third of the way along, a raised rib down its middle and its edges honed.
    /// </summary>
    static void Spear(Sculpt s, Build b, int len = 26, int back = 6)
    {
        var g = InFist(s, b, out int x, out int y, out int z0);
        int q = 4, e = z0 + len * q, butt = b.ArmZ0 * q - back * q;
        g.Fill(x, y, butt + 3, x + 2, y + 2, e, Mat.Shaft);
        g.Fill(x, y, butt, x + 2, y + 2, butt + 3, Mat.Iron);
        g.Dot(x, y, butt - 1, Mat.Iron);
        Socket(g, x, y, e - 8, e);
        g.Dot(x - 2, y + 1, e - 5, Mat.Brass); g.Dot(x + 3, y, e - 5, Mat.Brass);
        const int L = 24;
        for (int k = 0; k < L; k++)
        {
            float t = (k + .5f) / L, w = 3.4f * MathF.Pow(MathF.Sin(MathF.PI * MathF.Pow(t, .62f)), .9f);
            for (int dy = -4; dy < 4; dy++)
            {
                float a = MathF.Abs(dy + .5f);
                bool rib = a < 1 && t < .93f;
                if (a > w + .01f && !rib) continue;
                byte m = !rib && a + 1 > w ? Mat.SteelEdge : Mat.Steel;
                g.Dot(x, y + 1 + dy, e + k, m);
                if (rib) g.Dot(x + 1, y + 1 + dy, e + k, m);
            }
        }
        g.Tip = Half(g, x + 1, y + 1, e + L);
    }

    /// <summary>The iron socket of a head on its shaft, four cells square with its corners taken off, from z0 up to z1.</summary>
    static void Socket(Gear g, int x, int y, int z0, int z1)
    {
        for (int z = z0; z < z1; z++)
            for (int dy = -1; dy < 3; dy++)
                for (int dx = -1; dx < 3; dx++)
                    if (!((dx == -1 || dx == 2) && (dy == -1 || dy == 2))) g.Dot(x + dx, y + dy, z, Mat.Iron);
    }

    /// <summary>
    /// A flanged mace: a haft with a wound grip and an iron knob behind, and at its end an iron head with steel flanges
    /// standing out eight ways, each rising to a point and falling again, a knob on top.
    /// </summary>
    static void Mace(Sculpt s, Build b)
    {
        var g = InFist(s, b, out int x, out int y, out int z0);
        int q = 4, e = z0 + 30;
        g.Fill(x, y, b.ArmZ0 * q - 3, x + 2, y + 2, e, Mat.Handle);
        g.Wound(x, y, b.ArmZ0 * q - 3, z0 + 3, 2, Mat.LeatherDark, Mat.Leather);
        g.Fill(x - 1, y - 1, b.ArmZ0 * q - 5, x + 3, y + 3, b.ArmZ0 * q - 3, Mat.Iron);
        float cx = x + 1, cy = y + 1;
        const int H = 15;
        for (int k = 0; k < H; k++)
        {
            int z = e - 1 + k;
            g.Plate(z, 1, cx, cy, 2.7f, Mat.Iron);
            float t = (k + .5f) / H, reach = 2.5f + 4.6f * MathF.Pow(MathF.Sin(MathF.PI * t), .7f);
            // four flanges square to the faces, two cells thick, and four between them along the diagonals
            for (int r = 2; r < reach; r++)
            {
                g.Fill(x, y + 2 + r - 2, z, x + 2, y + 3 + r - 2, z + 1, Mat.Steel);
                g.Fill(x, y - 1 - (r - 2), z, x + 2, y - (r - 2), z + 1, Mat.Steel);
                g.Fill(x + 2 + r - 2, y, z, x + 3 + r - 2, y + 2, z + 1, Mat.Steel);
                g.Fill(x - 1 - (r - 2), y, z, x - (r - 2), y + 2, z + 1, Mat.Steel);
            }
            for (int d = 1; d < reach * .72f; d++)
                foreach (var (sx, sy) in new[] { (1, 1), (-1, 1), (1, -1), (-1, -1) })
                {
                    int px = sx > 0 ? x + 1 + d : x - d, py = sy > 0 ? y + 1 + d : y - d;
                    g.Dot(px, py, z, Mat.Steel);
                    g.Dot(px - sx, py, z, Mat.Steel);
                }
        }
        g.Fill(x - 1, y - 1, e + H - 1, x + 3, y + 3, e + H + 1, Mat.Iron);
        g.Fill(x, y, e + H + 1, x + 2, y + 2, e + H + 3, Mat.Steel);
        g.Tip = Half(g, cx, cy, e + H / 2);
    }

    /// <summary>
    /// A halberd: an ash haft of two metres with iron langets down it from the head, riveted; at the head a socket, an
    /// axe blade hanging below with its edge honed, a hooked beak above curving forward, a long square spike on beyond a
    /// steel rondel, and a red tassel under the head.
    /// </summary>
    static void Halberd(Sculpt s, Build b, int len = 20)
    {
        var g = InFist(s, b, out int x, out int y, out int z0);
        int q = 4, e = z0 + len * q, butt = b.ArmZ0 * q - 24;
        g.Fill(x, y, butt + 3, x + 2, y + 2, e + 2, Mat.Shaft);
        g.Fill(x, y, butt, x + 2, y + 2, butt + 3, Mat.Iron);
        g.Fill(x - 1, y, e - 28, x, y + 2, e, Mat.Iron);
        g.Fill(x + 2, y, e - 28, x + 3, y + 2, e, Mat.Iron);
        for (int z = e - 26; z < e; z += 8) { g.Dot(x - 2, y, z, Mat.Brass); g.Dot(x + 3, y + 1, z, Mat.Brass); }
        Socket(g, x, y, e - 7, e + 3);
        // the blade below
        const int B = 16;
        for (int v = 1; v <= B; v++)
        {
            float t = v / (float)B;
            int front = e + 6 - (int)MathF.Round(2.5f * t), rear = e - 5 - (int)MathF.Round(9 * t * t);
            for (int z = rear; z <= front; z++)
            {
                bool edge = v >= B - 1 || v > B - 4 && z <= rear + 1;
                g.Dot(x, y - v, z, edge ? Mat.SteelEdge : Mat.Steel);
                if (v <= 2) g.Dot(x + 1, y - v, z, Mat.Steel);
            }
        }
        // the beak above, curving forward to its point
        const int P = 12;
        for (int v = 0; v < P; v++)
        {
            float t = v / (float)P;
            int zc = e - 1 + (int)MathF.Round(6 * t * t), w = Math.Max(1, (int)MathF.Round(4 * (1 - t)));
            for (int k = 0; k < w; k++) { g.Dot(x, y + 2 + v, zc - k, Mat.Steel); if (v < 3) g.Dot(x + 1, y + 2 + v, zc - k, Mat.Steel); }
        }
        // the rondel and the spike
        g.Plate(e + 3, 1, x + 1, y + 1, 3.6f, Mat.Steel);
        for (int z = e + 4; z < e + 30; z++)
        {
            bool thin = z >= e + 24;
            g.Fill(x, y, z, thin ? x + 1 : x + 2, thin ? y + 1 : y + 2, z + 1, z >= e + 27 ? Mat.SteelEdge : Mat.Steel);
        }
        // the tassel hanging from under the socket
        for (int z = e - 11; z < e - 7; z++)
            for (int xx = x; xx < x + 2; xx++)
            {
                int hang = 6 + (int)(Dice(xx, z, 0, 21) * 4);
                for (int v = 1; v <= hang; v++) g.Dot(xx, y - v, z, v > hang - 2 ? Mat.ClothOchre : Mat.ClothRed);
            }
        g.Tip = Half(g, x + .5f, y + .5f, e + 30);
    }

    /// <summary>
    /// A lance: a long shaft couched under the arm, painted with a spiral of the house's colour, a steel vamplate in a
    /// cone before the hand, an iron ring under the head and a steel point; a pennon of the house's colour hangs from it
    /// under the head, a swallowtail at its fly and a gold edge along the shaft.
    /// </summary>
    static void Lance(Sculpt s, Build b, byte pennon, int len = 38)
    {
        var g = InFist(s, b, out int x, out int y, out int z0);
        int q = 4, e = z0 + len * q, butt = b.ArmZ0 * q - 32;
        for (int z = butt; z < e; z++)
            for (int dy = 0; dy < 2; dy++)
                for (int dx = 0; dx < 2; dx++)
                {
                    int face = dy == 0 ? dx : 3 - dx;
                    g.Dot(x + dx, y + dy, z, (z + face * 3 + 1200) % 12 < 4 ? pennon : Mat.Shaft);
                }
        for (int k = 0; k < 9; k++) g.Plate(z0 + k, 1, x + 1, y + 1, 7.4f - k * .6f, Mat.Steel);
        Socket(g, x, y, e - 4, e);
        for (int z = e; z < e + 12; z++)
        {
            bool thin = z >= e + 7;
            g.Fill(x, y, z, thin ? x + 1 : x + 2, thin ? y + 1 : y + 2, z + 1, z >= e + 10 ? Mat.SteelEdge : Mat.Steel);
        }
        // the pennon: below the shaft, its fly toward the hand cut in a swallowtail
        const int Hh = 14, Ll = 28;
        for (int v = 1; v <= Hh; v++)
            for (int k = 0; k < Ll; k++)
            {
                float cut = 7 * (1 - MathF.Abs(v - Hh / 2f - .5f) / (Hh / 2f));
                if (k < cut) continue;
                g.Dot(x, y - v, e - 6 - Ll + k, v == 1 ? Mat.ClothOchre : pennon);
            }
        g.Tip = Half(g, x + .5f, y + .5f, e + 12);
    }

    /// <summary>
    /// A giant's maul: a young trunk for a haft, bound in leather where he grips it, and on its end a block of stone
    /// with its corners broken, bound round with two riveted bands of iron.
    /// </summary>
    static void Maul(Sculpt s, Build b, int len = 18)
    {
        var g = InFist(s, b, out int x, out int y, out int z0, fine: false);
        int q = 2, e = z0 + len * q;
        g.Fill(x - 1, y - 1, b.ArmZ0 * q - 6, x + 3, y + 3, e, Mat.Trunk);
        for (int z = b.ArmZ0 * q - 6; z < b.ArmZ0 * q - 1; z++)
            for (int dy = -1; dy < 3; dy++) for (int dx = -1; dx < 3; dx++) if (dx is -1 or 2 || dy is -1 or 2) g.Dot(x + dx, y + dy, z, ((z + dx) & 1) == 0 ? Mat.Leather : Mat.LeatherDark);
        int hx0 = x - 5, hx1 = x + 7, hy0 = y - 5, hy1 = y + 7, hz0 = e, hz1 = e + 14;
        for (int z = hz0; z < hz1; z++)
            for (int yy = hy0; yy < hy1; yy++)
                for (int xx = hx0; xx < hx1; xx++)
                {
                    // the corners and the edges broken off here and there
                    int ex = Math.Min(xx - hx0, hx1 - 1 - xx), ey = Math.Min(yy - hy0, hy1 - 1 - yy), ez = Math.Min(z - hz0, hz1 - 1 - z);
                    int edges = (ex == 0 ? 1 : 0) + (ey == 0 ? 1 : 0) + (ez == 0 ? 1 : 0);
                    if (edges >= 2 && Dice(xx, yy, z, 31) < .45f || edges == 3) continue;
                    if (edges >= 2 && ex + ey + ez <= 1 && Dice(xx / 2, yy / 2, z / 2, 32) < .5f) continue;
                    g.Dot(xx, yy, z, Dice(xx >> 1, yy >> 1, z >> 1, 33) < .25f ? Mat.StoneDark : Mat.Rock);
                }
        foreach (int z in new[] { hz0 + 2, hz1 - 4 })
            for (int yy = hy0 - 1; yy <= hy1; yy++)
                for (int xx = hx0 - 1; xx <= hx1; xx++)
                {
                    bool rim = xx == hx0 - 1 || xx == hx1 || yy == hy0 - 1 || yy == hy1;
                    if (!rim) continue;
                    g.Fill(xx, yy, z, xx + 1, yy + 1, z + 2, Mat.Iron);
                    if ((xx + yy) % 4 == 0)
                    {
                        int ox = xx == hx0 - 1 ? -1 : xx == hx1 ? 1 : 0, oy = ox != 0 ? 0 : yy == hy0 - 1 ? -1 : 1;
                        g.Dot(xx + ox, yy + oy, z, Mat.Steel);
                    }
                }
        g.Tip = Half(g, x + 1, y + 1, (hz0 + hz1) / 2f);
    }

    /// <summary>A sling hanging from the right fist: two cords, a leather pouch at their end and a stone in the pouch.</summary>
    static void Sling(Sculpt s, Build b)
    {
        var g = InFist(s, b, out int x, out _, out _);
        int q = 4, zc = (b.ArmZ0 + b.ArmZ1) * q / 2, top = b.HandY * q + 2, py = top - 26;
        for (int yy = py + 2; yy < top; yy++) { g.Dot(x, yy, zc - 3, Mat.Linen); g.Dot(x, yy, zc + 2, Mat.Linen); }
        for (int z = zc - 4; z < zc + 4; z++)
            for (int xx = x - 2; xx < x + 4; xx++)
            {
                bool side = z == zc - 4 || z == zc + 3 || xx == x - 2 || xx == x + 3;
                g.Dot(xx, py, z, Mat.Leather);
                if (side) g.Fill(xx, py + 1, z, xx + 1, py + 3, z + 1, Mat.Leather);
            }
        g.Fill(x - 1, py + 1, zc - 2, x + 3, py + 4, zc + 2, Mat.Rock);
        g.Tip = Half(g, x + 1, py + 2, zc);
    }

    /// <summary>
    /// A crossbow in the right fist, its stock down along the arm, so that raised to the shoulder it points ahead: a
    /// steel prod across its far end bending back at the tips, the string drawn back from them to the bone nut, a bolt
    /// laid in the groove, the long iron trigger lever under the stock and an iron stirrup to span it with the foot.
    /// </summary>
    static void Crossbow(Sculpt s, Build b)
    {
        var g = InFist(s, b, out int x, out int y, out int z0);
        int q = 4, top = (b.FistY + 1) * q + 2, end = (b.FistY - 7) * q, zf = z0 + 3;
        float cx = x + 1;
        // the stock, deeper at its butt
        g.Fill(x - 1, end, z0, x + 3, top, zf, Mat.Shaft);
        g.Fill(x - 1, top - 8, z0 - 2, x + 3, top, z0, Mat.Shaft);
        // the prod across the far end, bending back toward the shooter at the tips
        const int Hw = 20;
        for (int dx = -Hw; dx < Hw; dx++)
        {
            float a = MathF.Abs(dx + .5f) / Hw;
            int yo = (int)MathF.Round(4 * a * a), th = a < .5f ? 2 : 1;
            for (int k = 0; k < th; k++) { g.Dot(x + 1 + dx, end + 1 + yo + k, zf, Mat.Steel); g.Dot(x + 1 + dx, end + 1 + yo + k, zf - 1, a < .3f ? Mat.Steel : Mat.SteelDark); }
        }
        // the string from the tips back to the nut, the nut, the bolt in its groove
        int nut = end + 15;
        for (int sgn = -1; sgn <= 1; sgn += 2)
        {
            float ax = cx + sgn * (Hw - .5f), ay = end + 5.5f;
            int n = 40;
            int lx = int.MinValue, ly = 0;
            for (int i = 0; i <= n; i++)
            {
                float k = i / (float)n;
                int px = (int)MathF.Floor(ax + (cx - ax) * k), py = (int)MathF.Floor(ay + (nut - ay) * k);
                if (lx != int.MinValue) while (lx != px || ly != py) { if (lx != px) lx += Math.Sign(px - lx); else ly += Math.Sign(py - ly); g.Dot(lx, ly, zf, Mat.Linen); }
                else { lx = px; ly = py; g.Dot(px, py, zf, Mat.Linen); }
            }
        }
        g.Fill(x, nut - 1, zf, x + 2, nut + 2, zf + 2, Mat.Bone);
        g.Fill(x, end - 3, zf, x + 1, nut - 1, zf + 1, Mat.Handle);
        g.Fill(x, end - 5, zf, x + 1, end - 3, zf + 1, Mat.Iron);
        g.Dot(x - 1, nut - 3, zf, Mat.ClothRed); g.Dot(x + 1, nut - 3, zf, Mat.ClothRed);
        g.Dot(x - 1, nut - 4, zf, Mat.Linen); g.Dot(x + 1, nut - 4, zf, Mat.Linen);
        // the trigger lever under the stock, and the stirrup beyond the prod
        g.Fill(x, nut - 2, z0 - 1, x + 2, nut + 10, z0, Mat.Iron);
        for (int yy = end - 7; yy <= end; yy++)
            for (int xx = x - 3; xx <= x + 4; xx++)
                if (xx == x - 3 || xx == x + 4 || yy == end - 7) g.Dot(xx, yy, z0 + 1, Mat.Iron);
        g.Tip = Half(g, cx, end - 5, zf);
        g.Edge = (0, 0, 1);
    }

    /// <summary>A round pot of powder held out in the right fist, a neck stopped with a rag, its fuse alight.</summary>
    static void PotInHand(Sculpt s, Build b)
    {
        var g = InFist(s, b, out int x, out int y, out int z0);
        float cx = x + 1, cy = y + 1, cz = z0 + 6;
        g.Ball(cx / 4, cy / 4, cz / 4, 1.55f, 1.4f, 1.55f, Mat.Clay);
        for (int xx = x - 6; xx < x + 8; xx++) for (int z = z0; z < z0 + 13; z++) if (g.Has(xx, y - 2, z)) g.Dot(xx, y - 2, z, Mat.Char);
        g.Fill(x - 1, y + 6, (int)cz - 2, x + 3, y + 8, (int)cz + 2, Mat.Clay);
        g.Fill(x, y + 8, (int)cz - 1, x + 2, y + 10, (int)cz + 1, Mat.Rag);
        g.Fill(x, y + 10, (int)cz - 1, x + 1, y + 13, (int)cz, Mat.Ember);
        g.Tip = Half(g, cx, cy, cz);
    }

    /// <summary>
    /// A tower shield of planks strapped to the left forearm, from a man's chest to his knees: upright planks with dark
    /// seams, a rib standing out down the middle, an iron rim and iron at the corners, riveted; painted the garrison's
    /// blue with a gold tower on it, its battlements, two windows and the arch of its gate.
    /// </summary>
    static void Pavise(Sculpt s, Build b)
    {
        var g = s.Hold(Off, DressKind.Shield, Tissue.Armor);
        const int q = 4;
        int cxh = -b.ArmX0 - b.ArmW / 2, cx = cxh * q;
        int x0 = (cxh - 5) * q, x1 = (cxh + 5) * q, y0 = (b.HandY - 5) * q, y1 = (b.HandY + b.Hand + 9) * q, zp = (b.Z1 + 3) * q;
        Straps(g, b, cx, zp);
        int ty = (y0 + y1) / 2 - 10;
        for (int yy = y0; yy < y1; yy++)
            for (int xx = x0; xx < x1; xx++)
            {
                // the top corners rounded off
                float rx = Math.Max(0, Math.Max(x0 + 4 - xx - .5f, xx + .5f - (x1 - 4))), ry = Math.Max(0, yy + .5f - (y1 - 4));
                if (rx * rx + ry * ry > 16) continue;
                bool rim = xx < x0 + 2 || xx >= x1 - 2 || yy < y0 + 2 || yy >= y1 - 2 || rx * rx + ry * ry > 5;
                bool corner = (xx < x0 + 7 || xx >= x1 - 7) && (yy < y0 + 7 || yy >= y1 - 7) && (xx < x0 + 4 || xx >= x1 - 4 || yy < y0 + 4 || yy >= y1 - 4);
                bool rib = Math.Abs(xx + .5f - cx) < 4;
                int seam = (xx - x0) % 5;
                byte plank = seam == 0 ? Mat.BoardDark : Mat.Board;
                g.Dot(xx, yy, zp, plank);
                if (rim || corner) { g.Fill(xx, yy, zp - 1, xx + 1, yy + 1, zp + (rib ? 4 : 3), Mat.Iron); continue; }
                // the tower: its wall, the battlements, two windows, the arch of the gate
                int u = xx - cx, v = yy - ty;
                bool wall = u >= -8 && u < 8 && v >= 0 && v < 22, merlon = v >= 22 && v < 26 && u >= -8 && u < 8 && (u + 8) % 6 < 4;
                bool window = v >= 13 && v < 17 && (u is -5 or -4 || u is 3 or 4);
                bool gate = u >= -3 && u < 3 && v >= 0 && (v < 6 || v < 8 && Math.Abs(u + .5f) < 2);
                byte paint = (wall || merlon) && !window && !gate ? Mat.ClothOchre : window || gate ? Mat.Hair : Mat.ClothBlue;
                g.Dot(xx, yy, zp + 1, paint);
                if (rib) { g.Dot(xx, yy, zp + 2, paint); if (Math.Abs(xx + .5f - cx) < 2) g.Dot(xx, yy, zp + 3, paint); }
            }
        for (int yy = y0 + 3; yy < y1 - 3; yy += 8) { g.Dot(x0 + 1, yy, zp + 3, Mat.Steel); g.Dot(x1 - 2, yy, zp + 3, Mat.Steel); }
        g.Grip = (cx / (float)q, (y0 + y1) / 2f / q, zp / (float)q);
        g.Tip = (cx / (float)q, (y0 + y1) / 2f / q, (zp + 4) / (float)q);
        g.Edge = (0, 0, 1);
    }

    /// <summary>
    /// A heater shield on the left forearm: flat along the top, its sides curving in to a point below and bending back
    /// round the arm; a field of the house's colour, a chevron across it with a line of the field's colour along it, an
    /// iron rim.
    /// </summary>
    static void Heater(Sculpt s, Build b, byte field, byte charge)
    {
        var g = s.Hold(Off, DressKind.Shield, Tissue.Armor);
        const int q = 4;
        float cx = (-b.ArmX0 - b.ArmW / 2f) * q, half = 4.6f * q;
        int top = (b.HandY + b.Hand + 6) * q, H = 13 * q, zp = (b.Z1 + 2) * q + 1;
        Straps(g, b, (int)cx, zp);
        bool In(int xx, int i)
        {
            if (i < 0 || i >= H) return false;
            float k = i < H * .4f ? 1 : 1 - (i - H * .4f) / (H - 1 - H * .4f);
            return MathF.Abs(xx + .5f - cx) <= half * MathF.Sqrt(MathF.Max(0, k)) + .01f;
        }
        for (int i = 0; i < H; i++)
            for (int xx = (int)(cx - half) - 1; xx <= cx + half; xx++)
            {
                if (!In(xx, i)) continue;
                int yy = top - 1 - i;
                float d = (xx + .5f - cx) / half;
                int back = (int)MathF.Round(2.6f * d * d);
                bool rim = !In(xx - 2, i) || !In(xx + 2, i) || !In(xx, i - 2) || !In(xx, i + 2) || !In(xx - 1, i + 1) || !In(xx + 1, i + 1);
                g.Dot(xx, yy, zp - back, Mat.Board);
                if (rim) { g.Fill(xx, yy, zp - back, xx + 1, yy + 1, zp + 3 - back, Mat.Iron); continue; }
                // a chevron: a broad V upside down across the middle, a line of the field's colour along its edges
                float up = top - 20 - MathF.Abs(xx + .5f - cx) * .8f;
                bool chevron = yy <= up && yy > up - 8, line = chevron && (yy > up - 1.2f || yy <= up - 7);
                byte paint = chevron && !line ? charge : field;
                g.Dot(xx, yy, zp + 1 - back, paint);
            }
        g.Grip = (cx / q, (top - H / 2f) / q, zp / (float)q);
        g.Tip = (cx / q, (top - H / 2f) / q, (zp + 2) / (float)q);
        g.Edge = (0, 0, 1);
    }

    // ---------------------------------------------------------------- our troops

    static BodyPlan Spearman(int look)
    {
        var f = new BodyPlan { Chop = .85f, Carry = -.15f };
        var b = new Build
        {
            Shirt = Mat.Gambeson, Sleeve = Mat.Gambeson, Trousers = look % 2 == 0 ? Mat.ClothBrown : Mat.ClothDark, Skin = HumanSkins[look % 3],
            Hair = Hairs[(look + 2) % 4], HairStyle = 1,
        };
        var s = Humanoid(b, f);
        // the quilted coat is his armour: stitched in rows, its skirts to the middle of the thigh
        s.Paint(Body, -b.Chest, b.Waist + 1, b.Z0, b.Chest, b.Neck, b.Z1, Mat.Gambeson, Tissue.Armor);
        s.Paint(Main, b.ArmX0, b.HandY + b.Hand, b.ArmZ0, -b.Chest, b.Neck, b.ArmZ1, Mat.Gambeson, Tissue.Armor);
        s.Paint(Off, b.Chest, b.HandY + b.Hand, b.ArmZ0, -b.ArmX0, b.Neck, b.ArmZ1, Mat.Gambeson, Tissue.Armor);
        for (int y = b.Waist + 3; y < b.Neck - 1; y += 3) s.Paint(Body, -b.Chest, y, b.Z0, b.Chest, y + 1, b.Z1, Mat.Linen);
        Skirt(s, b, Mat.Gambeson, Tissue.Armor, 4);
        // a kettle hat, a padded cap, a coif of mail, or a kettle hat over a beard
        switch (look)
        {
            case 1:
                s.Paint(Body, -b.HeadW, b.Crown - 3, b.HeadZ, b.HeadW, b.Crown, b.HZ1, Mat.Gambeson, Tissue.Armor);
                s.Box(Body, -b.HeadW + 1, b.Crown, b.HeadZ + 1, b.HeadW - 1, b.Crown + 1, b.HZ1 - 1, Mat.Gambeson, Tissue.Armor);
                break;
            case 2: Coif(s, b); break;
            default: KettleHat(s, b); break;
        }
        if (look == 3) Beard(s, b, b.Hair, false);
        if (s.Detail) Quilting(s);
        Spear(s, b);
        return Done(s, f, b);
    }

    static BodyPlan Shieldbearer(int look)
    {
        var f = new BodyPlan { Chop = .9f, Carry = -.3f, Shield = true, Guard = -.35f, Stride = 8 };
        var b = new Build { Shirt = Mat.Mail, Sleeve = Mat.Mail, Trousers = Mat.ClothDark, Skin = HumanSkins[(look + 1) % 3], Hair = Hairs[(look + 3) % 4], HairStyle = 1 };
        var s = Humanoid(b, f);
        Mail(s, b);
        Skirt(s, b, Mat.Mail, Tissue.Armor);
        // a blue gambeson's collar showing at the neck; a kettle hat, a coif, or a nasal helm
        s.Paint(Body, -b.Chest + 1, b.Neck - 1, b.Z0, b.Chest - 1, b.Neck, b.Z1, Mat.ClothBlue);
        switch (look)
        {
            case 0: KettleHat(s, b); break;
            case 1: Coif(s, b); Beard(s, b, b.Hair); break;
            default: NasalHelm(s, b, aventail: true); break;
        }
        Mace(s, b);
        Pavise(s, b);
        return Done(s, f, b);
    }

    static BodyPlan Halberdier(int look)
    {
        var f = new BodyPlan { Chop = 1.1f, Carry = -.25f };
        var b = new Build { Shirt = Mat.Mail, Sleeve = Mat.Mail, Trousers = look == 1 ? Mat.ClothRed : Mat.ClothDark, Skin = HumanSkins[look % 3], Hair = Hairs[look % 4], HairStyle = 1 };
        var s = Humanoid(b, f);
        Mail(s, b);
        Skirt(s, b, Mat.Mail, Tissue.Armor);
        // a red tabard over the mail with a white band across, to below the belt
        s.Box(Body, -3, b.Waist - 3, b.Z1, 3, b.Neck - 1, b.Z1 + 1, Mat.ClothRed);
        s.Box(Body, -3, b.Waist - 3, b.Z0 - 1, 3, b.Neck - 1, b.Z0, Mat.ClothRed);
        s.Box(Body, -3, b.Waist, b.Z1 + 1, 3, b.Waist + 1, b.Z1 + 2, Mat.Leather);
        for (int y = b.Waist + 1; y < b.Neck - 1; y++)
        {
            int x = 2 - (y - b.Waist - 1) * 5 / (b.Torso - 2);
            s.Paint(Body, x - 1, y, b.Z1, x + 1, y + 1, b.Z1 + 1, Mat.ClothWhite);
        }
        // a sallet over a bearded face, a bascinet with its pig-faced visor, or a sallet with its visor down
        if (look == 1) Bascinet(s, b); else Sallet(s, b, look == 2);
        if (look == 0) Beard(s, b, b.Hair, false);
        Halberd(s, b);
        return Done(s, f, b);
    }

    static BodyPlan Knight(int look)
    {
        var f = new BodyPlan { Chop = .8f, Carry = -.35f, Shield = true, Stride = 8 };
        var b = new Build { Shirt = Mat.Steel, Sleeve = Mat.Steel, Trousers = Mat.Steel, Boots = Mat.Steel, Belt = Mat.Leather, Skin = Mat.SkinPale, HairStyle = 3 };
        var s = Humanoid(b, f);
        Plate(s, b);
        byte cloth = new[] { Mat.ClothWhite, Mat.ClothBlue, Mat.ClothRed }[look], cross = new[] { Mat.ClothRed, Mat.ClothOchre, Mat.ClothWhite }[look];
        Surcoat(s, b, cloth, cross);
        GreatHelm(s, b, new[] { Mat.ClothRed, Mat.ClothWhite, Mat.ClothOchre }[look], look == 2);
        Sword(s, b, 17, Mat.Steel, longsword: true);
        Heater(s, b, look == 1 ? Mat.ClothWhite : Mat.ClothBlue, look == 1 ? Mat.ClothBlue : Mat.ClothOchre);
        return Done(s, f, b);
    }

    static BodyPlan Slinger(int look)
    {
        var f = new BodyPlan { Chop = .7f, Stride = 10 };
        var b = new Build
        {
            Chest = 3, Shirt = new[] { Mat.Linen, Mat.ClothGreen, Mat.ClothBrown, Mat.Linen }[look], Trousers = look % 2 == 0 ? Mat.ClothBrown : Mat.ClothDark,
            Boot = 2, Skin = HumanSkins[(look + 1) % 3], Hair = Hairs[(look + 1) % 4], HairStyle = look % 2 == 0 ? 0 : 2,
        };
        var s = Humanoid(b, f);
        // sleeves rolled to the elbow, a leather jerkin open over the shirt, a headband or a felt cap
        s.Paint(Main, b.ArmX0, b.HandY, b.ArmZ0, -b.Chest, b.HandY + b.Hand + 3, b.ArmZ1, b.Skin);
        s.Paint(Off, b.Chest, b.HandY, b.ArmZ0, -b.ArmX0, b.HandY + b.Hand + 3, b.ArmZ1, b.Skin);
        s.Paint(Body, -b.Chest, b.Waist + 1, b.Z0, b.Chest, b.Neck - 1, b.Z1, Mat.Leather);
        s.Paint(Body, -1, b.Waist + 1, b.Z1 - 1, 1, b.Neck, b.Z1, b.Shirt);
        if (look % 2 == 0) s.Paint(Body, -b.HeadW, b.Crown - 3, b.HeadZ, b.HeadW, b.Crown - 2, b.HZ1, Mat.ClothRed);
        else
        {
            s.Paint(Body, -b.HeadW, b.Crown - 2, b.HeadZ, b.HeadW, b.Crown, b.HZ1, Mat.ClothBrown);
            s.Box(Body, -2, b.Crown, b.HeadZ + 1, 2, b.Crown + 1, b.HZ1 - 2, Mat.ClothBrown);
        }
        // the pouch of stones at the left hip, hung from the belt
        s.Box(Body, -b.Chest - 1, b.Waist, b.Z0 - 1, b.Chest + 1, b.Waist + 1, b.Z1 + 1, Mat.Leather);
        s.Box(Body, b.Chest - 1, b.Waist - 3, b.Z1, b.Chest + 1, b.Waist, b.Z1 + 2, Mat.Leather, Tissue.Gear);
        s.Dot(Body, b.Chest - 1, b.Waist - 1, b.Z1 + 1, Mat.Rock, Tissue.Gear);
        if (look == 2) Beard(s, b, b.Hair, false);
        SlingerDetail(s, b, look);
        Sling(s, b);
        return Done(s, f, b);
    }

    static BodyPlan Crossbowman(int look)
    {
        var f = new BodyPlan { Chop = .85f };
        byte cloth = new[] { Mat.ClothRed, Mat.ClothBlue, Mat.ClothGreen }[look];
        var b = new Build { Shirt = cloth, Sleeve = Mat.Mail, Trousers = Mat.ClothDark, Skin = HumanSkins[(look + 2) % 3], Hair = Hairs[(look + 1) % 4], HairStyle = 1 };
        var s = Humanoid(b, f);
        // the brigandine: cloth over plates riveted through, the rivets in rows; mail sleeves
        s.Paint(Body, -b.Chest, b.Waist + 1, b.Z0, b.Chest, b.Neck, b.Z1, cloth, Tissue.Armor);
        for (int y = b.Waist + 2; y < b.Neck - 1; y += 2)
            for (int x = -b.Chest + 1; x < b.Chest - 1; x += 2) s.Paint(Body, x, y, b.Z1 - 1, x + 1, y + 1, b.Z1, Mat.Brass);
        s.Paint(Main, b.ArmX0, b.HandY + b.Hand, b.ArmZ0, -b.Chest, b.Neck, b.ArmZ1, Mat.Mail, Tissue.Armor);
        s.Paint(Off, b.Chest, b.HandY + b.Hand, b.ArmZ0, -b.ArmX0, b.Neck, b.ArmZ1, Mat.Mail, Tissue.Armor);
        // a quiver of bolts on the left hip, hung from the belt, their fletchings showing
        s.Box(Body, -b.Chest - 1, b.Waist, b.Z0 - 1, b.Chest + 1, b.Waist + 1, b.Z1 + 1, Mat.Leather);
        s.Box(Body, b.Chest, b.Waist - 4, b.Z0, b.Chest + 2, b.Waist, b.Z0 + 3, Mat.Leather, Tissue.Gear);
        s.Box(Body, b.Chest, b.Waist, b.Z0, b.Chest + 2, b.Waist + 2, b.Z0 + 1, Mat.Linen, Tissue.Gear);
        if (look == 1) Coif(s, b); else KettleHat(s, b);
        if (look == 2) Beard(s, b, b.Hair, false);
        Crossbow(s, b);
        return Done(s, f, b);
    }

    static BodyPlan Bombardier(int look)
    {
        var f = new BodyPlan { Chop = .8f, Carry = -.1f };
        var b = new Build
        {
            Shirt = look == 1 ? Mat.ClothBrown : Mat.Linen, Trousers = Mat.ClothDark, Skin = HumanSkins[look % 3], Hair = Hairs[(look + 2) % 4],
            HairStyle = look == 2 ? 3 : 1,
        };
        var s = Humanoid(b, f);
        // a leather apron from the chest to the knees, scorched at its hem, its straps over the shoulders
        s.Box(Body, -3, b.Waist - 5, b.Z1, 3, b.Neck - 2, b.Z1 + 1, Mat.Leather);
        s.Paint(Body, -3, b.Waist - 5, b.Z1, 3, b.Waist - 4, b.Z1 + 1, Mat.Char);
        s.Box(Body, -3, b.Neck - 2, b.Z1 - 1, -2, b.Neck, b.Z1, Mat.Leather);
        s.Box(Body, 2, b.Neck - 2, b.Z1 - 1, 3, b.Neck, b.Z1, Mat.Leather);
        // a leather cap with flaps down over the ears against the noise, or a hood
        if (look == 1) Hood(s, b, Mat.ClothDark);
        else
        {
            s.Paint(Body, -b.HeadW, b.Crown - 3, b.HeadZ, b.HeadW, b.Crown, b.HZ1, Mat.Leather);
            s.Box(Body, -b.HeadW + 1, b.Crown, b.HeadZ + 1, b.HeadW - 1, b.Crown + 1, b.HZ1 - 1, Mat.Leather);
            s.Pair(Body, Body, -b.HeadW - 1, b.Neck + 2, -2, -b.HeadW, b.Crown - 2, 2, Mat.Leather);
        }
        if (look == 0) Beard(s, b, b.Hair);
        // the belt with pots of powder hung at either hip
        s.Box(Body, -b.Chest - 1, b.Waist, b.Z0 - 1, b.Chest + 1, b.Waist + 1, b.Z1 + 1, Mat.Leather);
        foreach (int x in new[] { -b.Chest - 1, b.Chest - 1 })
        {
            s.Box(Body, x, b.Waist - 3, b.Z0 - 1, x + 2, b.Waist, b.Z0 + 1, Mat.Clay, Tissue.Gear);
            s.Dot(Body, x, b.Waist - 1, b.Z0 + 1, Mat.Rag, Tissue.Gear);
        }
        s.Box(Body, -1, b.Waist - 3, b.Z0 - 2, 1, b.Waist, b.Z0, Mat.Clay, Tissue.Gear);
        BombardierDetail(s, b, look);
        PotInHand(s, b);
        return Done(s, f, b);
    }

    /// <summary>
    /// A man in the saddle of a horse a metre and a half at the withers: the horse's barrel, its chest and rump, the neck
    /// arched up to the head with the mane down its crest and a forelock, ears, eyes, a dark muzzle, the tail; a saddle
    /// cloth, the saddle with its pommel and cantle, a bridle and the reins; and the man made as any is, sitting, his
    /// thighs along the horse's sides and his boots in the stirrups. The horse's forelegs are one part, its hind legs
    /// another, each pair joined at the top and turning there.
    /// </summary>
    static Sculpt Horseman(Build b, BodyPlan f, byte coat, byte mane, byte cloth, byte trim)
    {
        const int Leg = 13, Seat = 25;
        b.Leg = Seat;
        var s = Humanoid(b, f);
        s.Clear(LegR); s.Clear(LegL);
        f.Mounted = true;
        f.Stride = 22;
        // the legs: a forearm (or a gaskin) over a cannon and a hoof, a pair joined across the top
        foreach (var (p, z0) in new[] { (LegR, 6), (LegL, -12) })
        {
            foreach (int x0 in new[] { -4, 2 })
            {
                s.Box(p, x0, 6, z0, x0 + 2, Leg, z0 + 3, coat);
                s.Box(p, x0, 1, z0, x0 + 2, 6, z0 + 2, coat);
                s.Box(p, x0, 0, z0, x0 + 2, 1, z0 + 2, Mat.Hoof);
            }
            s.Box(p, -4, Leg - 2, z0, 4, Leg, z0 + 3, coat);
            Joint(f, p, 0, Leg / H, (z0 + 1.5f) / H);
        }
        // the barrel, rounded along its edges, the chest before it, the rump over the hind legs, the withers
        s.Box(Body, -5, Leg, -13, 5, Leg + 10, 10, coat);
        s.Cut(Body, -5, Leg, -13, -4, Leg + 1, 10, both: true);
        s.Cut(Body, -5, Leg + 9, -13, -4, Leg + 10, 10, both: true);
        s.Box(Body, -4, Leg + 2, 10, 4, Leg + 9, 12, coat);
        s.Box(Body, -4, Leg + 2, -15, 4, Leg + 9, -13, coat);
        // the neck arched up and forward, a step at a time, the mane along its crest
        for (int k = 0; k < 5; k++)
        {
            s.Box(Body, -2, Leg + 6 + 2 * k, 9 + k, 2, Leg + 12 + 2 * k, 14 + k, coat);
            s.Box(Body, -1, Leg + 12 + 2 * k, 9 + k, 1, Leg + 14 + 2 * k, 10 + k, mane);
        }
        // the head, long and bowed: the brow, the face, the muzzle, dark at the nose; the eyes, the ears, the forelock
        s.Box(Body, -2, 28, 15, 2, 33, 20, coat);
        s.Box(Body, -2, 26, 19, 2, 31, 23, coat);
        s.Box(Body, -2, 24, 22, 2, 29, 26, coat);
        s.Paint(Body, -2, 24, 25, 2, 27, 26, Mat.Hair);
        s.Pair(Body, Body, -2, 30, 18, -1, 31, 19, Mat.Eye);
        s.Pair(Body, Body, -2, 33, 16, -1, 35, 17, coat);
        s.Box(Body, -1, 33, 17, 1, 34, 19, mane);
        // the bridle round the nose and up the cheeks, the reins back along the neck to the saddle
        s.Paint(Body, -2, 26, 22, 2, 27, 26, Mat.Leather);
        s.Paint(Body, -2, 27, 19, 2, 32, 20, Mat.Leather);
        s.Pair(Body, Body, -3, 27, 4, -2, 28, 21, Mat.Leather);
        // the tail from the top of the rump, hanging
        s.Box(Body, -1, Leg + 7, -16, 1, Leg + 9, -15, mane);
        s.Box(Body, -2, Leg - 1, -17, 2, Leg + 8, -16, mane);
        // the saddle cloth over the barrel and down its sides, its hem in a trim; the saddle, the pommel, the cantle
        s.Box(Body, -6, Leg + 4, -6, 6, Leg + 11, 5, cloth);
        s.Paint(Body, -6, Leg + 4, -6, 6, Leg + 5, 5, trim);
        s.Box(Body, -4, Seat - 1, -5, 4, Seat, 3, Mat.Leather);
        s.Box(Body, -3, Seat, -5, 3, Seat + 2, -3, Mat.Leather);
        s.Box(Body, -2, Seat, 2, 2, Seat + 2, 4, Mat.Leather);
        // the rider's thighs along the horse's sides, his shins down to the stirrups, his boots in them
        s.Pair(Body, Body, -7, Seat - 4, -2, -4, Seat + 1, 3, b.Trousers);
        s.Pair(Body, Body, -7, Seat - 11, 1, -5, Seat - 4, 4, b.Boots);
        s.Pair(Body, Body, -7, Seat - 12, 1, -5, Seat - 11, 5, b.Boots);
        s.Pair(Body, Body, -8, Seat - 13, 1, -4, Seat - 12, 4, Mat.Iron);
        HorseDetail(s, coat, mane, Seat);
        return s;
    }

    /// <summary>
    /// A horse and its rider filled in: only the rider's chest has ribs and a heart in it; the horse's neck, which rises
    /// as high as the rider's chest, is flesh through.
    /// </summary>
    static BodyPlan Ridden(Sculpt s, BodyPlan f, Build b)
    {
        s.Into(f, b.Waist, b.Neck, b.Chest);
        var g = f.Grid[(int)Body];
        for (int i = 0; i < g.M.Length; i++)
        {
            if (g.T[i] is not ((byte)Tissue.Heart or (byte)Tissue.Bone)) continue;
            int x = i % g.SX + g.X0, z = i / g.SX % g.SZ + g.Z0, y = i / (g.SX * g.SZ) + g.Y0;
            if (y >= f.HeadY0 || y < b.Waist || z >= b.Z0 && z < b.Z1 && x >= -b.Chest && x < b.Chest) continue;
            if (g.T[i] == (byte)Tissue.Heart) f.Heart--;
            g.T[i] = (byte)Tissue.Flesh;
            g.M[i] = Mat.Flesh;
        }
        return f;
    }

    static readonly byte[] Coats = { Mat.HorseBay, Mat.HorseBlack, Mat.HorseGrey, Mat.HorseChestnut };
    static byte ManeOf(byte coat) => coat == Mat.HorseGrey ? Mat.HairGrey : coat == Mat.HorseChestnut ? Mat.HairGinger : Mat.Hair;

    static BodyPlan Rider(int look)
    {
        var f = new BodyPlan { Chop = .9f, Carry = -.35f, Shield = true };
        var b = new Build { Shirt = Mat.Mail, Sleeve = Mat.Mail, Trousers = Mat.ClothDark, Boots = Mat.Leather, Skin = HumanSkins[look % 3], Hair = Hairs[(look + 1) % 4], HairStyle = 1 };
        byte coat = Coats[look];
        var s = Horseman(b, f, coat, ManeOf(coat), Mat.ClothBlue, Mat.ClothOchre);
        Mail(s, b);
        // a blue tabard over the mail, a kettle hat or a nasal helm
        s.Box(Body, -3, b.Waist + 1, b.Z1, 3, b.Neck - 1, b.Z1 + 1, Mat.ClothBlue);
        s.Box(Body, -1, b.Waist + 3, b.Z1, 1, b.Neck - 2, b.Z1 + 1, Mat.ClothOchre);
        if (look % 2 == 0) KettleHat(s, b);
        else
        {
            NasalHelm(s, b, aventail: false);
            Beard(s, b, b.Hair, false);
        }
        Sword(s, b, 15, Mat.Steel);
        Shield(s, b);
        return Ridden(s, f, b);
    }

    static BodyPlan HorseArcher(int look)
    {
        var f = new BodyPlan { Chop = .9f };
        var b = new Build
        {
            Shirt = Mat.Leather, Sleeve = Mat.ClothBrown, Cuff = Mat.Leather, Trousers = Mat.ClothBrown, Boots = Mat.Leather, Skin = HumanSkins[(look + 1) % 3],
            Hair = Hairs[look % 4], HairStyle = 2,
        };
        byte coat = Coats[(look + 2) % 4];
        var s = Horseman(b, f, coat, ManeOf(coat), look == 1 ? Mat.ClothRed : Mat.ClothGreen, Mat.ClothOchre);
        // a jerkin of leather laced at the front, a cap trimmed with fur and a point to its crown
        s.Paint(Body, -b.Chest, b.Waist + 1, b.Z0, b.Chest, b.Neck, b.Z1, Mat.Leather, Tissue.Armor);
        s.Paint(Body, -1, b.Waist + 1, b.Z1 - 1, 1, b.Neck, b.Z1, Mat.ClothBrown);
        s.Paint(Body, -b.HeadW, b.Crown - 2, b.HeadZ, b.HeadW, b.Crown, b.HZ1, look == 1 ? Mat.ClothRed : Mat.ClothGreen);
        s.Rim(Body, b.Crown - 3, -b.HeadW, b.HeadZ, b.HeadW, b.HZ1, Mat.FurBoar);
        s.Box(Body, -2, b.Crown, -2, 2, b.Crown + 2, 2, look == 1 ? Mat.ClothRed : Mat.ClothGreen);
        s.Box(Body, -1, b.Crown + 2, -1, 1, b.Crown + 3, 1, look == 1 ? Mat.ClothRed : Mat.ClothGreen);
        if (look != 1) Beard(s, b, b.Hair, false);
        // the quiver on his back
        s.Box(Body, -3, b.Waist + 1, b.Z0 - 2, -1, b.Neck - 1, b.Z0, Mat.Leather, Tissue.Gear);
        foreach (var (x, y, c) in new[] { (-3, 0, Mat.Linen), (-2, 1, Mat.ClothRed), (-2, 0, Mat.Linen), (-3, 1, Mat.Linen) })
            s.Dot(Body, x, b.Neck - 1 + y, b.Z0 - 2 + (x + 3), c, Tissue.Gear);
        Bow(s, b);
        return Ridden(s, f, b);
    }

    static BodyPlan Lancer(int look)
    {
        var f = new BodyPlan { Chop = 1f, Carry = -.1f, Shield = true };
        var b = new Build { Shirt = Mat.Steel, Sleeve = Mat.Steel, Trousers = Mat.Steel, Boots = Mat.Steel, Skin = Mat.SkinPale, HairStyle = 3 };
        byte coat = look == 0 ? Mat.HorseGrey : Mat.HorseBlack, field = look == 0 ? Mat.ClothBlue : Mat.ClothRed;
        var s = Horseman(b, f, coat, ManeOf(coat), field, Mat.ClothOchre);
        // the horse barded: a caparison of the house's colour over all its body, a gold hem, a steel chamfron on its face
        s.Box(Body, -6, 13, -15, 6, 23, 13, field);
        s.Paint(Body, -6, 13, -15, 6, 14, 13, Mat.ClothOchre);
        s.Coat(Body, -3, 26, 19, 3, 34, 26, Mat.Steel, Tissue.Armor);
        s.Dot(Body, 0, 32, 20, Mat.Steel, Tissue.Armor);
        // and the man in plate under a great helm, his legs in steel along the horse's sides, a surcoat to the saddle
        Plate(s, b, legs: false);
        s.Pair(Body, Body, -7, b.Waist - 4, -2, -4, b.Waist + 1, 3, Mat.Steel, Tissue.Armor);
        s.Pair(Body, Body, -7, b.Waist - 11, 1, -5, b.Waist - 4, 4, Mat.Steel, Tissue.Armor);
        s.Pair(Body, Body, -7, b.Waist - 12, 1, -5, b.Waist - 11, 5, Mat.Steel, Tissue.Armor);
        s.Pair(Body, Body, -8, b.Waist - 13, 1, -4, b.Waist - 12, 4, Mat.Iron);
        Surcoat(s, b, field, Mat.ClothOchre, 0);
        GreatHelm(s, b, look == 0 ? Mat.ClothWhite : Mat.ClothOchre, false);
        Lance(s, b, field);
        Heater(s, b, field, Mat.ClothOchre);
        return Ridden(s, f, b);
    }

    /// <summary>
    /// A troll tamed and bought from the hill folk: the troll's own hulk, a loincloth of our blue with a gold hem and a
    /// blue sash across its chest, an iron collar with a length of broken chain hanging from it, irons on its wrists, and
    /// an iron-bound club.
    /// </summary>
    static BodyPlan TameTroll(int look)
    {
        var f = new BodyPlan { Chop = 1.4f, Carry = -.2f, Stride = 14 };
        var b = new Build
        {
            Leg = 16, LegW = 6, LegD = 6, Gap = 2, Boot = 0, Torso = 20, Chest = 8, ChestD = 10, Head = 10, HeadW = 6, HeadD = 10, HeadZ = 0,
            Arm = 24, ArmW = 6, ArmD = 6, Hand = 5, Skin = Mat.SkinTroll, Shirt = Mat.SkinTroll, Trousers = Mat.SkinTroll, Boots = Mat.SkinTroll,
            Belt = Mat.Leather, Hair = look == 1 ? Mat.SkinTroll : Mat.Moss, Brow = Mat.SkinTroll, Eye = Mat.Eye, Mouth = Mat.Hair, HairStyle = 1,
        };
        var s = Humanoid(b, f);
        int hz1 = b.HZ1, hy0 = b.Neck, hy1 = b.Crown, eye = hy0 + 3;
        // its ears set back along the sides of its head, where they hold on (a man's would stand off its jutting skull)
        s.Cut(Body, -b.HeadW - 1, eye, -1, -b.HeadW, eye + 2, 1, both: true);
        s.Pair(Body, Body, -b.HeadW - 1, eye, 3, -b.HeadW, eye + 3, 5, Mat.SkinTroll);
        s.Box(Body, -b.Chest, b.Waist - 6, b.Z0 - 1, b.Chest, b.Waist + 1, b.Z1 + 1, Mat.ClothBlue);
        s.Cut(Body, -b.Chest, b.Waist - 6, b.Z0 - 1, -b.Chest + 2, b.Waist - 2, b.Z1 + 1, both: true);
        s.Paint(Body, -b.Chest, b.Waist - 6, b.Z0 - 1, b.Chest, b.Waist - 5, b.Z1 + 1, Mat.ClothOchre);
        s.Paint(Body, -b.Chest, b.Waist, b.Z0 - 1, b.Chest, b.Waist + 1, b.Z1 + 1, Mat.Leather);
        // moss on a shoulder, the jutting jaw and its teeth
        s.Coat(Body, b.Chest - 4, b.Neck - 3, b.Z0, b.Chest, b.Neck, b.Z1, Mat.Moss);
        s.Box(Body, -4, hy0, hz1, 4, hy0 + 3, hz1 + 3, Mat.SkinTroll);
        s.Pair(Body, Body, -4, hy0 + 3, hz1 + 1, -3, hy0 + 5, hz1 + 2, Mat.Bone);
        s.Pair(Body, Body, -2, hy0 + 3, hz1 + 2, -1, hy0 + 4, hz1 + 3, Mat.Bone);
        if (look == 0) s.Box(Body, -2, hy1, 2, 2, hy1 + 1, 6, Mat.Moss);
        // the blue sash from the right shoulder to the left hip
        for (int y = b.Waist + 1; y < b.Neck; y++)
        {
            int x = -b.Chest + (y - b.Waist - 1) * (2 * b.Chest - 4) / (b.Torso - 1);
            s.Paint(Body, x, y, b.Z0, x + 4, y + 1, b.Z1, Mat.ClothBlue);
        }
        // the collar round the foot of the head and the chain hanging from it, broken off
        s.Rim(Body, hy0, -b.HeadW, b.HeadZ, b.HeadW, hz1, Mat.Iron, Tissue.Armor);
        s.Box(Body, 0, hy0 - 9, hz1, 1, hy0, hz1 + 1, Mat.Iron, Tissue.Gear);
        for (int y = hy0 - 8; y < hy0; y += 3) s.Dot(Body, (y & 1) == 0 ? -1 : 1, y, hz1, Mat.Iron, Tissue.Gear);
        // irons on the wrists
        s.Paint(Main, b.ArmX0, b.HandY + b.Hand, b.ArmZ0, -b.Chest, b.HandY + b.Hand + 3, b.ArmZ1, Mat.Iron, Tissue.Armor);
        s.Paint(Off, b.Chest, b.HandY + b.Hand, b.ArmZ0, -b.ArmX0, b.HandY + b.Hand + 3, b.ArmZ1, Mat.Iron, Tissue.Armor);
        TrollDetail(s, b, look, tame: true);
        Club(s, b, banded: true);
        return Done(s, f, b);
    }

    /// <summary>
    /// A giant: a man three metres tall, broad and heavy-limbed, his hair and beard long and wild, a jerkin of hide with
    /// the arms bare, a mantle of fur over the shoulders and a kilt of it, his feet wrapped in hides; a maul of stone.
    /// </summary>
    static BodyPlan Giant(int look)
    {
        var f = new BodyPlan { Chop = 1.5f, Carry = -.2f, Stride = 15 };
        byte hair = new[] { Mat.HairGinger, Mat.Hair, Mat.HairGrey }[look], skin = HumanSkins[look == 1 ? 1 : look == 2 ? 2 : 0];
        var b = new Build
        {
            Leg = 20, LegW = 5, LegD = 6, Gap = 1, Boot = 4, Torso = 16, Chest = 7, ChestD = 8, Head = 12, HeadW = 5, HeadD = 10, HeadZ = -5,
            Arm = 18, ArmW = 4, ArmD = 5, Hand = 5, FaceUp = 3, Skin = skin, Shirt = Mat.Leather, Sleeve = skin, Trousers = Mat.ClothBrown,
            Boots = Mat.FurBoar, Belt = Mat.Leather, Hair = hair, Brow = hair, HairStyle = 2,
        };
        var s = Humanoid(b, f);
        int hx = b.HeadW, hz1 = b.HZ1, hy0 = b.Neck;
        // the fur mantle over the shoulders and the kilt of fur round the hips
        byte fur = look == 1 ? Mat.FurDeer : Mat.FurBoar;
        s.Box(Body, -b.Chest, b.Neck - 4, b.Z0 - 1, b.Chest, b.Neck, b.Z1 + 1, fur);
        s.Cut(Body, -2, b.Neck - 4, b.Z1, 2, b.Neck, b.Z1 + 1, both: false);
        Skirt(s, b, fur, Tissue.Skin, 6);
        for (int x = -b.Chest - 1; x < b.Chest + 1; x += 3) s.Cut(Body, x, b.Waist - 6, b.Z1, x + 1, b.Waist - 4, b.Z1 + 1, both: false);
        s.Paint(Body, -1, b.Waist, b.Z1 + 1, 1, b.Waist + 1, b.Z1 + 2, Mat.Iron);
        // the bands of hide wound round his shins
        for (int y = b.Boot + 1; y < b.Leg - 6; y += 3) s.Paint(LegR, -40, y, -40, 40, y + 1, 40, Mat.Leather);
        for (int y = b.Boot + 1; y < b.Leg - 6; y += 3) s.Paint(LegL, -40, y, -40, 40, y + 1, 40, Mat.Leather);
        // the beard: over the jaw and the cheeks, hanging down onto the chest, a great moustache over the mouth
        s.Paint(Body, -hx, hy0, 0, hx, hy0 + 4, hz1, hair);
        s.Box(Body, -3, hy0 - 5, b.FaceZ, 3, hy0, hz1, hair);
        s.Box(Body, -2, hy0 - 7, b.FaceZ, 2, hy0 - 5, hz1, hair);
        s.Box(Body, -3, hy0 + 4, hz1, 3, hy0 + 5, hz1 + 1, hair);
        // an iron ring on one arm
        if (look != 2) s.Paint(Off, b.Chest, b.Neck - 6, b.ArmZ0, -b.ArmX0, b.Neck - 5, b.ArmZ1, Mat.Brass, Tissue.Armor);
        GiantDetail(s, b, hair, fur);
        Maul(s, b);
        return Done(s, f, b);
    }

    // ---------------------------------------------------------------- our hounds

    /// <summary>
    /// A war hound: a mastiff as high as a man's hip, broad in the chest, a heavy head with a dark muzzle and hanging
    /// ears, a curled tail, and a collar of leather studded with iron spikes.
    /// </summary>
    static BodyPlan Hound(int look)
    {
        var f = new BodyPlan { Stride = 9, Chop = .6f };
        byte fur = new[] { Mat.Fawn, Mat.FurBoar, Mat.HairGrey }[look], dark = look == 1 ? Mat.Hair : Mat.FurBoar;
        var s = Quadruped(f, 7, 2, 3, 4, -8, fur, Mat.Hoof);
        // the barrel, deep at the chest, rounded along its back
        s.Box(Body, -3, 7, -9, 3, 13, 6, fur);
        s.Cut(Body, -3, 12, -9, -2, 13, 6, both: true);
        s.Paint(Body, -2, 7, -3, 2, 8, 5, look == 2 ? Mat.Belly : fur);
        // the neck and the heavy head, the muzzle dark, the nose, the eyes, the ears hanging
        s.Box(Body, -2, 10, 5, 2, 15, 9, fur);
        s.Box(Body, -3, 13, 7, 3, 18, 12, fur);
        s.Box(Body, -2, 13, 12, 2, 16, 15, dark);
        s.Box(Body, -1, 15, 15, 1, 16, 16, Mat.Hoof);
        s.Pair(Body, Body, -2, 13, 13, -1, 14, 15, Mat.Bone);
        s.Pair(Body, Body, -3, 16, 11, -2, 17, 12, Mat.Eye);
        s.Pair(Body, Body, -4, 15, 8, -3, 18, 10, dark);
        // the tail up and curling over
        s.Box(Body, -1, 11, -11, 1, 13, -9, fur);
        s.Box(Body, -1, 13, -11, 1, 15, -10, fur);
        // the collar round the neck and its spikes
        foreach (int y in new[] { 11, 12 }) s.Rim(Body, y, -2, 5, 2, 9, Mat.Leather);
        foreach (var (x, z) in new[] { (-4, 7), (3, 7), (0, 10), (-1, 10), (-4, 5), (3, 5) }) s.Dot(Body, x, 12, z, Mat.Iron, Tissue.Gear);
        HoundDetail(s);
        Joint(f, Body, 0, 7 / H, 2 / H);
        f.HeadY0 = 13; f.HeadY1 = 18;
        return s.Into(f, 7, 13, 3);
    }

    // ---------------------------------------------------------------- our people, finer

    /// <summary>
    /// A worker drawn finer: the felt cap's seam and its peak thinned, the straw hat woven in rings with a band round
    /// it, the rolled sleeves' roll, a pocket on the apron with a mallet's handle in it, a pouch on the belt.
    /// </summary>
    static void WorkerDetail(Sculpt s, Build b, int look)
    {
        if (!s.Detail) return;
        int hy1 = b.Crown, hz1 = b.HZ1;
        switch (look % 4)
        {
            case 0:
                for (int z = 2 * b.HeadZ; z < 2 * hz1; z++) s.FPut(Body, 0, hy1 * 2 + 1, z, Mat.LeatherDark);
                for (int x = -6; x < 6; x++) { s.FDel(Body, x, hy1 * 2 - 1, hz1 * 2 + 3); s.FDel(Body, x, hy1 * 2 - 2, hz1 * 2 + 2); s.FDel(Body, x, hy1 * 2 - 2, hz1 * 2 + 3); }
                break;
            case 1:
                s.FPattern(Body, -14, (hy1 - 1) * 2, -14, 14, (hy1 + 2) * 2, 14, (x, y, z) =>
                {
                    if (s.FMat(Body, x, y, z) != Mat.Straw) return 0;
                    float r = MathF.Sqrt((x + .5f) * (x + .5f) + (z + .5f) * (z + .5f));
                    return ((int)(r / 1.6f) & 1) == 0 ? Mat.ClothOchre : (byte)0;
                });
                break;
        }
        if (look % 2 == 1)
        {
            int roll = (b.HandY + b.Hand + 3) * 2;
            foreach (var (p, sg) in new[] { (Main, 1), (Off, -1) })
                for (int z = b.ArmZ0 * 2 - 1; z <= b.ArmZ1 * 2; z++)
                    for (int x = b.ArmX0 * 2 - 1; x <= -b.Chest * 2; x++)
                        if (x == b.ArmX0 * 2 - 1 || z == b.ArmZ0 * 2 - 1 || z == b.ArmZ1 * 2) s.FSet(p, sg > 0 ? x : -1 - x, roll, z, b.Shirt);
        }
        // the pocket on the apron and the handle of a mallet standing out of it
        int py = (b.Waist + 1) * 2, pz = (b.Z1 + 1) * 2;
        for (int y = py; y < py + 5; y++) for (int x = -4; x < 2; x++) s.FSet(Body, x, y, pz, y == py + 4 ? Mat.LeatherDark : Mat.Leather);
        for (int y = py + 3; y < py + 9; y++) s.FSet(Body, 0, y, pz + 1, Mat.Handle, Tissue.Gear);
        Pouch(s, b, 1);
    }

    /// <summary>
    /// An archer drawn finer: arrows standing out of the quiver's mouth with their fletchings, its rim dark, a buckle
    /// on the strap across the chest, the bracer laced.
    /// </summary>
    static void ArcherDetail(Sculpt s, Build b)
    {
        if (!s.Detail) return;
        int top = (b.Neck - 1) * 2, qz = (b.Z0 - 2) * 2;
        for (int x = -6; x < -2; x++) for (int z = qz; z < qz + 4; z++) s.FPut(Body, x, top - 1, z, Mat.LeatherDark);
        foreach (var (x, z, h, f) in new[] { (-6, qz, 5, Mat.ClothRed), (-4, qz + 1, 7, Mat.Linen), (-5, qz + 3, 6, Mat.Linen), (-3, qz + 2, 4, Mat.ClothRed), (-4, qz + 3, 5, Mat.Linen) })
            for (int k = 0; k < h; k++) s.FSet(Body, x, top + k, z, k >= h - 3 ? f : Mat.Handle, Tissue.Gear);
        int by = (b.Waist + 5) * 2;
        s.FSet(Body, -1, by, b.Z1 * 2, Mat.Brass); s.FSet(Body, 0, by, b.Z1 * 2, Mat.Brass); s.FSet(Body, -1, by + 1, b.Z1 * 2, Mat.Brass);
        int ly = (b.HandY + b.Hand) * 2;
        for (int y = ly; y < ly + 6; y++) s.FPattern(Off, -80, y, -80, 80, y + 1, 80, (x, yy, z) => s.FMat(Off, x, yy, z) == Mat.Leather && (x + yy) % 3 == 0 ? Mat.LeatherDark : (byte)0);
    }

    /// <summary>A priest drawn finer: the knots of the rope girdle, a little skull for the charm, a zigzag worked into the hem.</summary>
    static void PriestDetail(Sculpt s, Build b)
    {
        if (!s.Detail) return;
        int cz = (b.Z1 + 1) * 2;
        foreach (int y in new[] { (b.Waist - 4) * 2, (b.Waist - 2) * 2 + 1 }) { s.FSet(Body, 2, y, cz, Mat.RobeTrim); s.FSet(Body, 3, y, cz, Mat.RobeTrim); s.FSet(Body, 2, y, cz + 1, Mat.Linen); }
        int sy = (b.Neck - 5) * 2, sz = b.Z1 * 2;
        s.FCut(Body, -1, b.Neck - 5, b.Z1, 1, b.Neck - 3, b.Z1 + 1);
        s.FBall(Body, 0, b.Neck - 4f, b.Z1 + .4f, .9f, .85f, .5f, Mat.Bone, Tissue.Gear);
        s.FPut(Body, -1, sy + 2, sz, Mat.Hair); s.FPut(Body, 0, sy + 2, sz, Mat.Hair); s.FPut(Body, -1, sy + 3, sz, Mat.Bone);
        s.FPattern(Body, -80, 6, -80, 80, 8, 80, (x, y, z) => s.FMat(Body, x, y, z) is var m && m != Mat.RobeTrim && m != 0 && ((x + z + 64) % 4 == (y - 6) * 2 || (x + z + 64) % 4 == 3 - (y - 6) * 2) ? Mat.RobeTrim : (byte)0);
    }

    /// <summary>The quilting of a gambeson drawn finer: its channels stitched upright, three cells apart.</summary>
    static void Quilting(Sculpt s)
    {
        foreach (var p in new[] { Body, Main, Off })
            s.FPattern(p, -80, 0, -80, 80, 200, 80, (x, y, z) => s.FMat(p, x, y, z) == Mat.Gambeson && (x + z + 64) % 3 == 0 ? Mat.Linen : (byte)0);
    }

    /// <summary>A slinger drawn finer: the jerkin laced across its opening, the ends of the headband hanging behind.</summary>
    static void SlingerDetail(Sculpt s, Build b, int look)
    {
        if (!s.Detail) return;
        int z = b.Z1 * 2;
        for (int y = (b.Waist + 1) * 2 + 1; y < b.Neck * 2 - 2; y += 3) { s.FSet(Body, -2, y, z, Mat.LeatherDark); s.FSet(Body, 1, y, z, Mat.LeatherDark); s.FSet(Body, -1, y + 1, z, Mat.LeatherDark); s.FSet(Body, 0, y + 1, z, Mat.LeatherDark); }
        if (look % 2 == 0)
            for (int k = 0; k < 6; k++) { s.FSet(Body, -1 - k / 3, (b.Crown - 3) * 2 - k, 2 * b.HeadZ - 1, Mat.ClothRed); s.FSet(Body, 1 + k / 4, (b.Crown - 3) * 2 - k, 2 * b.HeadZ - 1, Mat.ClothRed); }
    }

    /// <summary>A bombardier drawn finer: the apron scorched in patches, the strings of the cap's flaps hanging, rags stopping the pots at his belt.</summary>
    static void BombardierDetail(Sculpt s, Build b, int look)
    {
        if (!s.Detail) return;
        s.FPattern(Body, -80, (b.Waist - 5) * 2, -80, 80, (b.Neck - 2) * 2, 80, (x, y, z) => s.FMat(Body, x, y, z) == Mat.Leather && z >= b.Z1 * 2 && Dice(x >> 1, y >> 1, look, 141) < .14f ? Mat.Char : (byte)0);
        if (look != 1)
            foreach (int sg in new[] { 1, -1 })
                for (int k = 0; k < 5; k++) s.FSet(Body, sg > 0 ? -2 * b.HeadW - 1 : 2 * b.HeadW, (b.Neck + 2) * 2 - 1 - k, 0, Mat.LeatherDark);
    }

    /// <summary>
    /// A horse drawn finer: its eyes with a glint, dark nostrils at the end of a muzzle a shade darker, the mane and the
    /// tail hanging in strands, feathering over the hooves, brass studs on the bridle, the stirrups' irons.
    /// </summary>
    static void HorseDetail(Sculpt s, byte coat, byte mane, int seat)
    {
        if (!s.Detail) return;
        foreach (int sg in new[] { 1, -1 })
        {
            int X(int x) => sg > 0 ? x : -1 - x;
            s.FPut(Body, X(-4), 61, 37, Mat.EyeWhite); s.FPut(Body, X(-4), 60, 36, Mat.Eye);
            foreach (int z in new[] { 44, 47, 50 }) s.FPut(Body, X(-4), 52, z, Mat.Brass);
        }
        // the mane in strands down the crest, the tail's end ragged
        for (int k = 0; k < 5; k++)
            for (int x = -2; x < 2; x++)
                for (int z = (9 + k) * 2; z < (10 + k) * 2; z++)
                    if (Dice(x, z, k, 151) < .5f)
                        for (int y = (25 + 2 * k) * 2 + 2; y > (25 + 2 * k) * 2 - 1 - (int)(Dice(z, x, k, 152) * 4); y--) s.FSet(Body, x < 0 ? -3 : 2, y, z, mane);
        for (int x = -4; x < 4; x++) for (int z = -34; z < -31; z++) if (Dice(x, z, 0, 153) < .45f) s.FDel(Body, x, 24, z);
        // feathering over the hooves
        foreach (var p in new[] { LegR, LegL })
            s.FPattern(p, -80, 2, -80, 80, 4, 80, (x, y, z) => s.FMat(p, x, y, z) != 0 && ((x + z) & 1) == 0 ? mane : (byte)0);
    }

    /// <summary>A hound drawn finer: a glint in its eyes, its nose wet and dark, the spikes of its collar standing out.</summary>
    static void HoundDetail(Sculpt s)
    {
        if (!s.Detail) return;
        foreach (int sg in new[] { 1, -1 })
        {
            int X(int x) => sg > 0 ? x : -1 - x;
            s.FPut(Body, X(-5), 33, 23, Mat.EyeWhite);
        }
        foreach (var (x, z) in new[] { (-9, 14), (8, 14), (-9, 10), (8, 10), (-3, 22), (2, 22) }) s.FSet(Body, x, 24, z, Mat.Steel, Tissue.Gear);
    }

    /// <summary>A giant drawn finer: his beard and hair ragged in strands at their ends, the fur of the mantle and the kilt shaggy at its edges.</summary>
    static void GiantDetail(Sculpt s, Build b, byte hair, byte fur)
    {
        if (!s.Detail) return;
        int hy0 = b.Neck;
        for (int x = -4; x < 4; x++) for (int z = b.FaceZ * 2; z < b.HZ1 * 2; z++) if (Dice(x, z, 0, 161) < .5f) s.FSet(Body, x, (hy0 - 7) * 2 - 1, z, hair);
        for (int x = -b.Chest * 2; x < b.Chest * 2; x++)
            foreach (int z in new[] { b.Z0 * 2 - 2, b.Z1 * 2 + 1 })
            {
                if (Dice(x, z, 1, 162) < .5f) s.FSet(Body, x, (b.Neck - 4) * 2 - 1, z, fur);
                if (Dice(x, z, 2, 163) < .5f) s.FDel(Body, x, (b.Waist - 6) * 2, z);
            }
    }

    // ---------------------------------------------------------------- the horde's engines, finer

    /// <summary>
    /// A ram drawn finer: the iron head on its log a ram's head, a long muzzle with its eyes and nostrils and a horn
    /// curling round on either side; the hides over the roof in pieces with dark seams; skulls nailed to the front
    /// posts; a red hand daubed on the boards of its sides.
    /// </summary>
    static void RamDetail(Sculpt s, int look)
    {
        if (!s.Detail) return;
        s.FCut(Main, -4, 13, 30, 4, 21, 35);
        s.FBall(Main, 0, 17, 32.5f, 3.1f, 3.4f, 3.4f, Mat.Iron, Tissue.Gear);
        s.FBall(Main, 0, 16, 35f, 2.2f, 2.4f, 1.8f, Mat.Iron, Tissue.Gear);
        foreach (int sg in new[] { 1, -1 })
        {
            for (int i = 0; i <= 24; i++)
            {
                float k = i / 24f, an = k * MathF.PI * 1.7f, r = 3.2f - k * 1.6f;
                float y = 18.5f + r * MathF.Cos(an), z = 31.5f - r * MathF.Sin(an) + k * 1.5f;
                s.FBall(Main, sg * (3.4f + k * 1.2f), y, z, 1.1f - k * .45f, 1.1f - k * .45f, 1.1f - k * .45f, (i & 3) == 0 ? Mat.Rust : Mat.Iron, Tissue.Gear);
            }
            // an eye glowing in each side of the head, a nostril at the end of the muzzle
            for (int k = 10; k >= 0; k--) { int ex = sg > 0 ? k : -1 - k; if (s.FHas(Main, ex, 37, 68)) { s.FPut(Main, ex, 37, 68, Mat.Ember); break; } }
            for (int k = 80; k >= 60; k--) { int nx = sg > 0 ? 1 : -2; if (s.FHas(Main, nx, 31, k)) { s.FPut(Main, nx, 31, k, Mat.Hair); break; } }
        }
        // the hides in pieces, sewn with dark seams
        s.FPattern(Body, -80, 56, -80, 80, 80, 80, (x, y, z) => s.FMat(Body, x, y, z) is Mat.Leather or Mat.FurBoar && (z + 64) % 12 == 0 ? Mat.LeatherDark : (byte)0);
        s.FPattern(Body, -80, 56, -80, 80, 80, 80, (x, y, z) => s.FMat(Body, x, y, z) == (look == 0 ? Mat.Leather : Mat.FurBoar) && ((z + 64) / 12 & 1) == 1 ? Mat.Hide : (byte)0);
        // skulls on the front posts; a red hand on either side
        foreach (int x in new[] { -23, 22 }) Trophy(s, x, 40, 43);
        foreach (int sg in new[] { 1, -1 })
            foreach (var (dy, dz) in new[] { (0, 0), (1, 0), (0, 1), (1, 1), (2, -1), (3, -1), (2, 0), (3, 0), (2, 1), (3, 1), (2, 2), (3, 2), (4, 2), (4, 0), (5, 0), (4, -1), (4, 1), (5, 1), (5, 2) })
                s.FPut(Body, sg > 0 ? 27 : -28, 34 + dy, 6 + dz, Mat.WarpaintRed);
    }

    /// <summary>A skull nailed up, facing +z, its middle at (x, y, z) in fine cells: the dome, dark sockets, the teeth.</summary>
    static void Trophy(Sculpt s, int x, int y, int z)
    {
        s.FBall(Body, (x + .5f) / 2, (y + .5f) / 2, (z + .5f) / 2, 1.2f, 1.2f, 1.1f, Mat.Bone, Tissue.Gear);
        s.FPut(Body, x - 1, y, z + 2, Mat.Maw); s.FPut(Body, x + 1, y, z + 2, Mat.Maw);
        s.FPut(Body, x, y - 2, z + 2, Mat.Maw);
        s.FSet(Body, x, y + 3, z, Mat.Iron, Tissue.Gear);
    }

    /// <summary>A stone-thrower drawn finer: the joints of its frame bound in rope, iron bolts through them, a round stone in the cup.</summary>
    static void CatapultDetail(Sculpt s)
    {
        if (!s.Detail) return;
        foreach (int y in new[] { 30, 31, 50, 51 })
            s.FPattern(Body, -80, y, 14, 80, y + 1, 26, (x, yy, z) => s.FMat(Body, x, yy, z) == Mat.Wood && Math.Abs(x + .5f) > 17 ? Mat.Linen : (byte)0);
        foreach (var (x, y, z) in new[] { (-24, 15, -40), (23, 15, -40), (-24, 15, 40), (23, 15, 40), (-24, 54, 22), (23, 54, 22) }) s.FSet(Body, x, y, z, Mat.Iron);
        s.FCut(Main, -2, 14, -30, 2, 18, -26);
        s.FBall(Main, 0, 16, -28, 2.2f, 2.1f, 2.2f, Mat.Stone, Tissue.Gear);
        for (int z = -50; z < -2; z += 8) for (int x = -4; x < 4; x++) s.FPut(Main, x, 23, z, Mat.Linen);
    }

    /// <summary>A mantlet drawn finer: the seams between its planks, the iron band riveted, a skull hung on its front, a red daub.</summary>
    static void MantletDetail(Sculpt s)
    {
        if (!s.Detail) return;
        s.FPattern(Body, -80, 0, 15, 80, 80, 16, (x, y, z) => s.FMat(Body, x, y, z) is Mat.BoardDark or Mat.Wood && (x + 64) % 5 == 0 ? Mat.Char : (byte)0);
        s.FPattern(Body, -80, 60, -80, 80, 64, 80, (x, y, z) => s.FMat(Body, x, y, z) == Mat.Iron && (x + 64) % 4 == 0 && y == 61 ? Mat.Steel : (byte)0);
        Trophy(s, -12, 50, 19);
        for (int k = 0; k < 9; k++) foreach (int z in new[] { 15, 17 }) { s.FPut(Body, 8 + k / 2, 30 + k, z, Mat.WarpaintRed); s.FPut(Body, 9 + k / 2, 30 + k, z, Mat.WarpaintRed); }
    }

    /// <summary>A siege tower drawn finer: its hides in pieces with dark seams, skulls hung under the fighting top, red daubs on its front.</summary>
    static void TowerDetail(Sculpt s)
    {
        if (!s.Detail) return;
        s.FPattern(Body, -80, 16, -80, 80, 160, 80, (x, y, z) => s.FMat(Body, x, y, z) == Mat.Leather && ((y % 14 == 0) || (x + z + 64) % 16 == 0) ? Mat.LeatherDark : (byte)0);
        foreach (int x in new[] { -20, -6, 8, 22 }) Trophy(s, x, 154, 35);
        for (int k = 0; k < 12; k++) for (int w = 0; w < 2; w++) { s.FPut(Body, -10 + k, 100 + k / 2 + w, 31, Mat.WarpaintRed); s.FPut(Body, 10 - k, 100 + k / 2 + w, 31, Mat.WarpaintRed); }
    }

    // ---------------------------------------------------------------- the horde

    static readonly byte[] GoblinSkins = { Mat.SkinGoblin, Mat.SkinGoblinDark, Mat.SkinGoblinPale };
    static readonly byte[] Rags = { Mat.Rag, Mat.ClothBrown, Mat.ClothOchre, Mat.Rag };

    /// <summary>A goblin's build: short bandy legs and bare feet, a narrow body and a big head.</summary>
    static Build GoblinBuild(byte shirt, byte trousers, byte skin) => new()
    {
        Leg = 6, LegW = 2, LegD = 3, Gap = 1, Boot = 0, Torso = 6, Chest = 3, ChestD = 4, Head = 8, HeadW = 4, HeadD = 7, HeadZ = -3,
        Arm = 6, ArmW = 2, ArmD = 2, Hand = 2, Skin = skin, Shirt = shirt, Trousers = trousers, Boots = skin, Hair = skin, Brow = skin,
        Eye = Mat.EyeGlint, Sleeve = skin, HairStyle = 3,
    };

    /// <summary>A shade darker than a skin, for the hollows of it: inside an ear, between the ribs.</summary>
    static byte Shade(byte skin) => skin switch
    {
        Mat.SkinGoblinPale => Mat.SkinGoblin, Mat.SkinGoblin => Mat.SkinGoblinDark, Mat.SkinOrc or Mat.SkinOrcGrey => Mat.SkinTrollDark,
        Mat.SkinTroll => Mat.SkinTrollDark, _ => Mat.Lips,
    };

    /// <summary>
    /// Long ears sweeping out and up (or one torn short), a hooked nose, yellow eyes with black pupils, a wide mouth with
    /// teeth. Near: the ears pointed, their hollows dark, notched and ringed with brass on some; the eyes sunk in dark
    /// sockets under a brow sloping down to the nose, glowing, with slits for pupils; the nose long and hooked down
    /// over the lip; the grin full of needle teeth with its corners turned up, the jaw narrowing to the chin; and on the
    /// horde's footmen warpaint — chalk across the eyes, ochre down the cheeks, a chalk skull — or old scars.
    /// </summary>
    static void GoblinFace(Sculpt s, Build b, bool torn = false, int look = -1)
    {
        int hx = b.HeadW, hz1 = b.HZ1, hy0 = b.Neck, eye = hy0 + 4;
        s.Box(Body, -hx - (torn ? 2 : 4), eye, -1, -hx, eye + 1, 1, b.Skin);
        if (!torn) s.Box(Body, -hx - 5, eye + 1, -1, -hx - 3, eye + 2, 0, b.Skin);
        s.Box(Body, hx, eye, -1, hx + 4, eye + 1, 1, b.Skin);
        s.Box(Body, hx + 3, eye + 1, -1, hx + 5, eye + 2, 0, b.Skin);
        s.Paint(Body, -hx, eye - 1, -1, hx, eye + 3, hz1, b.Skin);
        s.Pair(Body, Body, -3, eye, b.FaceZ, -1, eye + 1, hz1, Mat.Brass);
        s.Pair(Body, Body, -2, eye, b.FaceZ, -1, eye + 1, hz1, Mat.Eye);
        s.Pair(Body, Body, -3, eye + 1, b.FaceZ, -1, eye + 2, hz1, b.Skin);
        s.Box(Body, -1, hy0 + 2, hz1, 1, eye + 1, hz1 + 1, b.Skin);
        s.Box(Body, -1, hy0 + 2, hz1 + 1, 1, hy0 + 3, hz1 + 2, b.Skin);
        s.Box(Body, -3, hy0 + 1, b.FaceZ, 3, hy0 + 2, hz1, Mat.Hair);
        s.Pair(Body, Body, -2, hy0 + 1, b.FaceZ, -1, hy0 + 2, hz1, Mat.Bone);
        if (!s.Detail) return;
        int F = hz1 * 2 - 1, E = eye * 2, H0 = hy0 * 2, W = hx * 2, M = (hy0 + 1) * 2;
        byte shade = Shade(b.Skin);
        // the jaw narrowing to the chin
        for (int z = -16; z < 16; z++)
            for (int x = -W; x < W; x++)
            {
                if (MathF.Abs(x + .5f) > W - 2.5f) s.FDel(Body, x, H0, z);
                if (MathF.Abs(x + .5f) > W - 1.5f) s.FDel(Body, x, H0 + 1, z);
            }
        // the ears
        s.FCut(Body, -hx - 6, eye, -1, -hx, eye + 2, 1, both: true);
        foreach (int sg in new[] { 1, -1 })
        {
            int X(int x) => sg > 0 ? x : -1 - x;
            int L = torn && sg > 0 ? 5 : 12;
            for (int d = 0; d <= L; d++)
            {
                int top = E + 2 + (int)(d * .32f), bottom = E - 1 + (int)(d * .6f);
                if (bottom > top) bottom = top;
                for (int y = bottom; y <= top; y++)
                {
                    bool edge = y == top || y == bottom || d == L, notch = !torn && look % 3 == 1 && y == top && d is 6 or 7;
                    if (notch) continue;
                    s.FSet(Body, X(-W - 1 - d), y, -1, b.Skin);
                    s.FSet(Body, X(-W - 1 - d), y, 0, !edge && d > 1 && d < L - 2 ? shade : b.Skin);
                }
            }
            // a brass ring through the lobe
            if (look >= 0 && look % 2 == 0 && !(torn && sg > 0))
            {
                int ry = E - 2;
                foreach (var (dx, dy) in new[] { (2, 0), (3, -1), (3, -2), (2, -3), (1, -2), (1, -1) }) s.FSet(Body, X(-W - 1 - dx), ry + dy, -1, Mat.Brass);
            }
            // the eye in its socket, the brow over it
            for (int y = E - 1; y < E + 3; y++) for (int x = -7; x < -2; x++) s.FPut(Body, X(x), y, F, Mat.Maw);
            for (int y = E; y < E + 2; y++)
                for (int x = -6; x < -3; x++) { s.FDel(Body, X(x), y, F); s.FSet(Body, X(x), y, F - 1, x == -5 ? Mat.Eye : Mat.EyeGlint); }
            for (int x = -8; x < -2; x++) s.FSet(Body, X(x), E + 3, F + 1, b.Skin);
            s.FSet(Body, X(-3), E + 2, F + 1, b.Skin); s.FSet(Body, X(-2), E + 2, F + 1, b.Skin);
        }
        // the nose, long and hooked down over the lip
        for (int y = M + 2; y < E + 2; y++) for (int x = -2; x < 2; x++) for (int z = F + 1; z < F + 5; z++) s.FDel(Body, x, y, z);
        for (int y = M + 3; y < E + 2; y++) { s.FSet(Body, -1, y, F + 1, b.Skin); s.FSet(Body, 0, y, F + 1, b.Skin); }
        for (int y = M + 3; y < M + 6; y++) { s.FSet(Body, -1, y, F + 2, b.Skin); s.FSet(Body, 0, y, F + 2, b.Skin); }
        for (int y = M + 2; y < M + 5; y++) { s.FSet(Body, -1, y, F + 3, b.Skin); s.FSet(Body, 0, y, F + 3, b.Skin); }
        s.FSet(Body, -1, M + 2, F + 4, b.Skin); s.FSet(Body, 0, M + 2, F + 4, b.Skin);
        s.FSet(Body, -2, M + 3, F + 1, shade); s.FSet(Body, 1, M + 3, F + 1, shade);
        // the grin: needle teeth top and bottom in a dark mouth, its corners turned up
        for (int x = -7; x < 7; x++) for (int y = M - 1; y < M + 3; y++) s.FPut(Body, x, y, F, b.Skin);
        for (int x = -5; x < 5; x++)
        {
            s.FPut(Body, x, M + 1, F, (x & 1) == 1 ? Mat.Bone : Mat.Maw);
            s.FPut(Body, x, M, F, Mat.Maw);
        }
        foreach (int sg in new[] { 1, -1 })
        {
            int X(int x) => sg > 0 ? x : -1 - x;
            s.FPut(Body, X(-6), M + 1, F, Mat.Maw); s.FPut(Body, X(-7), M + 2, F, Mat.Maw);
            // a fang up out of the lower jaw over the lip
            s.FPut(Body, X(-3), M, F, Mat.Bone); s.FSet(Body, X(-3), M + 1, F + 1, Mat.Bone);
        }
        if (look < 0) return;
        // warpaint, or scars
        var skin = b.Skin;
        bool Bare(int x, int y, int z) => s.FMat(Body, x, y, z) == skin;
        switch (look % 4)
        {
            case 0:
                s.FPattern(Body, -W, E - 2, 0, W, E + 3, F + 2, (x, y, z) => Bare(x, y, z) ? Mat.Warpaint : (byte)0);
                break;
            case 1:
                s.FPattern(Body, -W, M + 2, 0, W, E - 1, F + 2, (x, y, z) => Bare(x, y, z) && Math.Abs(x + .5f) is 3.5f or 5.5f ? Mat.WarpaintRed : (byte)0);
                break;
            case 2:
                s.FPattern(Body, -W, E - 3, 0, W, E + 4, F + 2, (x, y, z) => Bare(x, y, z) && Math.Abs(x + .5f) is > 1.5f and < 7.5f ? Mat.Warpaint : (byte)0);
                s.FPattern(Body, -W, H0 + 1, 0, W, M, F + 2, (x, y, z) => Bare(x, y, z) ? Mat.Warpaint : (byte)0);
                break;
            default:
                for (int k = 0; k < 9; k++) s.FPut(Body, -7 + k, E + 4 - k, F, Mat.Scar);
                break;
        }
    }

    /// <summary>
    /// Bare feet drawn finer: toes parted by grooves along the front of the foot, with dark claws at their tips.
    /// </summary>
    static void Claws(Sculpt s, Build b, byte claw)
    {
        if (!s.Detail) return;
        int lz1 = (b.LegD - b.LegD / 2) * 2, x0 = b.LegX0 * 2, x1 = b.LegX1 * 2;
        foreach (var (p, sg) in new[] { (LegR, 1), (LegL, -1) })
        {
            int X(int x) => sg > 0 ? x : -1 - x;
            for (int x = x0; x < x1; x++)
            {
                bool groove = (x - x0) % 2 == 1 && x < x1 - 1;
                if (groove) { s.FDel(p, X(x), 1, lz1 + 1); continue; }
                s.FSet(p, X(x), 0, lz1 + 2, claw);
            }
        }
    }

    /// <summary>
    /// A goblin of the horde in its rags: a loincloth, a tuft of hair on the crown of some and a bone through it on one;
    /// near, its shirt torn at the hem, a string of finger bones round the neck, the ribs showing on one that goes
    /// bare, a little skull hung at the hip of some, claws on its bare feet; one wears a dented pot of a helmet far too
    /// big for it, one carries a pot lid for a shield.
    /// </summary>
    static BodyPlan Goblin(int look)
    {
        var f = new BodyPlan { Chop = .45f, Carry = -.3f, Stride = 6 };
        byte skin = GoblinSkins[look % 3];
        var b = GoblinBuild(look % 4 == 3 ? skin : Rags[look % 4], look % 3 == 2 ? Mat.Rag : Mat.Leather, skin);
        var s = Humanoid(b, f);
        GoblinFace(s, b, look % 4 == 3, look);
        // a ragged loincloth, a tuft of hair on the crown of some, a bone through it on one
        s.Box(Body, -2, b.Waist - 3, b.Z1, 2, b.Waist + 1, b.Z1 + 1, Rags[(look + 1) % 4]);
        s.Cut(Body, -2, b.Waist - 3, b.Z1, -1, b.Waist - 2, b.Z1 + 1, both: true);
        if (look % 2 == 0 && look != 6) s.Box(Body, -1, b.Crown, -1, 1, b.Crown + 2, 1, Mat.Hair);
        if (look == 5) s.Box(Body, -4, b.Crown, 0, 4, b.Crown + 1, 1, Mat.Bone);
        Claws(s, b, Mat.Horn);
        if (s.Detail)
        {
            int W = b.Chest * 2, waist = b.Waist * 2, neck = b.Neck * 2, z1 = b.Z1 * 2;
            // the hem of the shirt torn, the loincloth's too
            if (look % 4 != 3)
                for (int x = -W; x < W; x++)
                    if (Dice(x, look, 0, 81) < .45f) for (int z = -16; z < 16; z++) s.FPut(Body, x, waist + 2, z, b.Trousers);
            for (int x = -4; x < 4; x++) if (Dice(x, look, 1, 82) < .5f) { s.FDel(Body, x, (b.Waist - 3) * 2, z1); s.FDel(Body, x, (b.Waist - 3) * 2, z1 + 1); }
            // the tuft in strands, ragged
            if (look % 2 == 0 && look != 6)
                for (int x = -2; x < 2; x++) for (int z = -2; z < 2; z++) if (Dice(x, z, look, 83) < .4f) s.FDel(Body, x, b.Crown * 2 + 3, z);
            // a string of finger bones round the neck, worn on the skin and cut with it like the shirt: were it carried
            // gear, it would stop a blade at the throat as a haft does and, never cut, hold the head on
            for (int x = -W + 1; x < W - 1; x += 2)
            {
                int y = neck - 2 - (int)(MathF.Abs(x + .5f) < 3 ? 1 : 0);
                s.FSet(Body, x, y, z1, Mat.Bone); s.FSet(Body, x, y - 1, z1, Mat.Bone);
                s.FSet(Body, x + 1, y, z1, Mat.Linen);
            }
            // the ribs of one that goes bare
            if (look % 4 == 3)
                for (int y = waist + 4; y < neck - 3; y += 2)
                    for (int x = -W + 1; x < W - 1; x++) if (Math.Abs(x + .5f) > 1) s.FPut(Body, x, y, z1 - 1, Shade(skin));
            // a little skull at the hip
            if (look % 4 == 2)
            {
                int hx = W - 2, hy = waist - 3;
                s.FBall(Body, (hx + 1) / 2f, (hy + 1) / 2f, (z1 + 1.5f) / 2f, 1.4f, 1.2f, 1.2f, Mat.Bone, Tissue.Gear);
                s.FPut(Body, hx, hy + 1, z1 + 3, Mat.Maw); s.FPut(Body, hx + 2, hy + 1, z1 + 3, Mat.Maw);
                s.FSet(Body, hx + 1, hy + 4, z1 + 1, Mat.Linen, Tissue.Gear);
            }
        }
        // a dented pot of a helmet too big for it
        if (look == 6)
        {
            s.Paint(Body, -b.HeadW, b.Crown - 3, b.HeadZ, b.HeadW, b.Crown, b.HZ1, Mat.Iron, Tissue.Armor);
            s.Flat(Body, b.Crown - 3, 5.3f, Mat.Iron, Tissue.Armor);
            s.Flat(Body, b.Crown, 3.6f, Mat.Iron, Tissue.Armor);
            if (s.Detail)
            {
                for (int x = 1; x < 6; x++) for (int z = -4; z < 2; z++) { s.FDel(Body, x, b.Crown * 2 + 1, z); if (x > 2 && z > -3) s.FDel(Body, x, b.Crown * 2, z); }
                s.FPattern(Body, -14, (b.Crown - 3) * 2 - 1, -14, 14, b.Crown * 2 + 2, 14, (x, y, z) => s.FMat(Body, x, y, z) == Mat.Iron && Dice(x >> 1, y >> 1, z >> 1, 84) < .3f ? Mat.Rust : (byte)0);
            }
        }
        // a pot lid on the left forearm
        if (look == 4)
        {
            var g = s.Hold(Off, DressKind.Shield, Tissue.Armor);
            float cx = (-b.ArmX0 - b.ArmW / 2f) * 4, cy = (b.HandY + b.Hand) * 4 + 2;
            int zp = b.ArmZ1 * 4 + 2;
            g.Fill((int)cx - 2, (int)cy - 6, b.ArmZ1 * 4, (int)cx + 2, (int)cy - 3, zp, Mat.Leather);
            for (int yy = (int)(cy - 11); yy <= cy + 11; yy++)
                for (int xx = (int)(cx - 11); xx <= cx + 11; xx++)
                {
                    float r = MathF.Sqrt((xx + .5f - cx) * (xx + .5f - cx) + (yy + .5f - cy) * (yy + .5f - cy));
                    if (r > 10.6f) continue;
                    byte m = Dice(xx >> 2, yy >> 2, 0, 85) < .3f ? Mat.Rust : Mat.Iron;
                    g.Dot(xx, yy, zp, m);
                    if (r > 9.4f || r < 6) g.Dot(xx, yy, zp + 1, m);
                    if (r < 2.2f) { g.Dot(xx, yy, zp + 2, Mat.Iron); g.Dot(xx, yy, zp + 3, Mat.Iron); }
                }
            g.Grip = (cx / 4, cy / 4, zp / 4f); g.Tip = (cx / 4, cy / 4, (zp + 3) / 4f); g.Edge = (0, 0, 1);
        }
        Knife(s, b);
        return Done(s, f, b);
    }

    static BodyPlan Sapper(int look)
    {
        var f = new BodyPlan { Chop = .55f, Stride = 6 };
        var b = GoblinBuild(Mat.Leather, Mat.Rag, GoblinSkins[look % 3]);
        var s = Humanoid(b, f);
        GoblinFace(s, b, false, look);
        Claws(s, b, Mat.Horn);
        // a cap, a length of ladder on the back, a mallet in the fist
        byte cap = new[] { Mat.Leather, Mat.ClothBrown, Mat.ClothGreen, Mat.ClothOchre }[look % 4];
        s.Paint(Body, -b.HeadW, b.Crown - 2, b.HeadZ, b.HeadW, b.Crown, b.HZ1, cap);
        s.Box(Body, -3, b.Crown, b.HeadZ + 1, 3, b.Crown + 1, b.HZ1 - 1, cap);
        s.Box(Body, -3, b.Waist, b.Z0 - 1, -2, b.Neck + 1, b.Z0, Mat.Handle, Tissue.Gear);
        s.Box(Body, 2, b.Waist, b.Z0 - 1, 3, b.Neck + 1, b.Z0, Mat.Handle, Tissue.Gear);
        for (int y = b.Waist + 1; y < b.Neck + 1; y += 4) s.Box(Body, -2, y, b.Z0 - 1, 2, y + 1, b.Z0, Mat.Handle, Tissue.Gear);
        Mallet(s, b);
        return Done(s, f, b);
    }

    /// <summary>A goblin in a dark hood and a cloak to the knees, an empty sack slung on its back, a knife in the fist.</summary>
    static BodyPlan Thief(int look)
    {
        var f = new BodyPlan { Chop = .45f, Carry = -.3f, Stride = 6 };
        byte cloak = new[] { Mat.ClothDark, Mat.ClothBrown, Mat.ClothGreen }[look % 3];
        var b = GoblinBuild(cloak, Mat.Leather, GoblinSkins[(look + 1) % 3]);
        var s = Humanoid(b, f);
        GoblinFace(s, b, look == 2);
        // the hood over the crown and down the back of the head, its peak behind
        s.Paint(Body, -b.HeadW, b.Crown - 3, b.HeadZ, b.HeadW, b.Crown, b.HZ1 - 1, cloak);
        s.Box(Body, -b.HeadW + 1, b.Crown, b.HeadZ, b.HeadW - 1, b.Crown + 1, b.HZ1 - 2, cloak);
        s.Box(Body, -1, b.Crown - 1, b.HeadZ - 1, 1, b.Crown + 1, b.HeadZ, cloak);
        // the cloak's skirt down over the legs, a little apart at the front
        s.Box(Body, -3, b.Waist - 3, b.Z0, 3, b.Waist, b.Z1, cloak);
        s.Cut(Body, -1, b.Waist - 3, b.Z1 - 1, 1, b.Waist - 1, b.Z1, both: true);
        // the sack on the back
        s.Box(Body, -3, b.Waist, b.Z0 - 3, 3, b.Neck, b.Z0, Mat.Sack, Tissue.Gear);
        s.Box(Body, -2, b.Neck, b.Z0 - 2, 2, b.Neck + 2, b.Z0, Mat.Sack, Tissue.Gear);
        Knife(s, b, old: false);
        return Done(s, f, b);
    }

    /// <summary>
    /// A goblin shaman: a skull worn over its face with the glint showing through the eye holes, a crest of red and
    /// ochre feathers over it, a cloak of hides, a necklace of teeth, and a crooked staff with a horn at its head and an
    /// ember glowing in the horn.
    /// </summary>
    static BodyPlan Shaman(int look)
    {
        var f = new BodyPlan { Chop = .9f, Carry = -.2f, Stride = 6 };
        var b = GoblinBuild(look == 0 ? Mat.ClothBrown : Mat.Rag, Mat.Leather, GoblinSkins[(look + 2) % 3]);
        var s = Humanoid(b, f);
        GoblinFace(s, b);
        int hx = b.HeadW, hz1 = b.HZ1, hy0 = b.Neck, eye = hy0 + 4;
        s.Box(Body, -hx + 1, hy0 + 1, hz1, hx - 1, b.Crown - 1, hz1 + 1, Mat.Bone, Tissue.Gear);
        s.Pair(Body, Body, -3, eye, hz1, -1, eye + 1, hz1 + 1, Mat.EyeGlint, Tissue.Gear);
        s.Box(Body, -1, hy0 + 1, hz1 + 1, 1, hy0 + 3, hz1 + 2, Mat.Bone, Tissue.Gear);
        for (int i = -2; i <= 2; i++) s.Box(Body, i, b.Crown, -1, i + 1, b.Crown + 3 - Math.Abs(i), 0, (i & 1) == 0 ? Mat.ClothRed : Mat.ClothOchre, Tissue.Gear);
        s.Box(Body, -b.Chest - 1, b.Waist - 2, b.Z0 - 1, b.Chest + 1, b.Neck + 1, b.Z0, Mat.FurBoar, Tissue.Gear);
        for (int x = -2; x <= 1; x++) s.Dot(Body, x, b.Neck - 1, b.Z1, Mat.Bone, Tissue.Gear);
        ShamanDetail(s, b, look);
        HornStaff(s, b);
        return Done(s, f, b);
    }

    /// <summary>
    /// A witch of the horde: an old orc woman in a long skirt of sacking and a shawl over her head and shoulders, grey
    /// hair hanging out from under it, a hooked nose and a tusk; a crooked staff with a bunch of herbs and a bone hung
    /// from its head.
    /// </summary>
    static BodyPlan Witch(int look)
    {
        var f = new BodyPlan { Chop = 1.1f, Carry = -.2f, Stride = 6 };
        byte skin = look == 1 ? Mat.SkinOrcGrey : Mat.SkinOrc, shawl = look == 1 ? Mat.ClothDark : Mat.ClothBrown;
        var b = new Build
        {
            Leg = 9, LegW = 3, LegD = 4, Gap = 1, Boot = 0, Torso = 9, Chest = 4, ChestD = 5, Head = 8, HeadW = 4, HeadD = 8, HeadZ = -3,
            Arm = 9, ArmW = 2, ArmD = 3, Hand = 3, Skin = skin, Shirt = shawl, Trousers = Mat.Rag, Boots = skin, Sleeve = shawl,
            Hair = Mat.HairGrey, HairStyle = 2, Eye = Mat.Ember, Brow = Mat.HairGrey, Mouth = Mat.Hair,
        };
        var s = Humanoid(b, f);
        int hx = b.HeadW, hz1 = b.HZ1, hy0 = b.Neck;
        // a hooked nose, and a tusk out of the lower lip
        s.Box(Body, -1, hy0 + 3, hz1, 1, hy0 + 5, hz1 + 1, skin);
        s.Box(Body, -1, hy0 + 2, hz1 + 1, 0, hy0 + 3, hz1 + 2, skin);
        s.Box(Body, 1, hy0 + 1, hz1, 2, hy0 + 3, hz1 + 1, Mat.Bone);
        // the skirt to the ground, wider than the legs, and its ragged hem
        s.Box(Body, -b.Chest - 1, 1, b.Z0 - 1, b.Chest + 1, b.Waist, b.Z1 + 1, Mat.Rag);
        for (int x = -b.Chest - 1; x < b.Chest + 1; x += 3) s.Cut(Body, x, 1, b.Z1, x + 1, 2, b.Z1 + 1, both: false);
        // the shawl over the head and down over the shoulders, grey hair hanging out of it either side of the face
        Hood(s, b, shawl);
        s.Box(Body, -b.Chest - 1, b.Neck - 4, b.Z0 - 1, b.Chest + 1, b.Neck, b.Z1 + 1, shawl);
        s.Pair(Body, Body, -hx - 1, hy0 - 3, hz1 - 3, -hx, hy0 + 3, hz1 - 1, Mat.HairGrey);
        WitchDetail(s, b, look);
        HerbStaff(s, b);
        return Done(s, f, b);
    }

    /// <summary>
    /// A necromancer of the horde: a tall orc in a black robe to the ground and a hood drawn low, pale fire for eyes under
    /// it, small bones strung across its chest, and a staff topped with a skull with the same fire in its sockets.
    /// </summary>
    static BodyPlan Necromancer(int look)
    {
        var f = new BodyPlan { Chop = 1.2f, Carry = -.25f, Stride = 9 };
        byte skin = look == 1 ? Mat.SkinOrcGrey : Mat.SkinOrc, robe = Mat.ClothDark;
        var b = new Build
        {
            Leg = 12, LegW = 3, LegD = 5, Gap = 1, Boot = 2, Torso = 12, Chest = 5, ChestD = 6, Head = 8, HeadW = 4, HeadD = 8, HeadZ = -2,
            Arm = 12, ArmW = 3, ArmD = 4, Hand = 3, Skin = skin, Shirt = robe, Trousers = robe, Sleeve = robe, Boots = Mat.Leather, Belt = Mat.Bone,
            Eye = Mat.SoulFire, Brow = skin, Mouth = Mat.Hair, HairStyle = 3,
        };
        var s = Humanoid(b, f);
        int hy0 = b.Neck, hz1 = b.HZ1;
        // tusks out of the jaw
        s.Pair(Body, Body, -3, hy0 + 1, hz1, -2, hy0 + 3, hz1 + 1, Mat.Bone);
        // the robe to the ground with its hem dark as old blood, wide sleeves, the hood
        s.Box(Body, -b.Chest - 1, 1, b.Z0 - 1, b.Chest + 1, b.Waist, b.Z1 + 1, robe);
        s.Paint(Body, -b.Chest - 1, 1, b.Z0 - 1, b.Chest + 1, 2, b.Z1 + 1, Mat.Robe);
        s.Pair(Main, Off, b.ArmX0 - 1, b.HandY + b.Hand, b.ArmZ0 - 1, b.ArmX0, b.HandY + b.Hand + 4, b.ArmZ1 + 1, robe);
        Hood(s, b, robe);
        // a string of small bones across the chest
        for (int x = -b.Chest + 1; x < b.Chest - 1; x += 2) s.Dot(Body, x, b.Neck - 3 - Math.Abs(x) / 2, b.Z1, Mat.Bone, Tissue.Gear);
        NecroDetail(s, b, look);
        SkullStaff(s, b);
        return Done(s, f, b);
    }

    // ---------------------------------------------------------------- the horde's engines (in half-voxels, facing +z)

    /// <summary>Four wheels on axles under a frame of beams, the timber of an engine's base.</summary>
    static void Wheels(Sculpt s, int halfX, int frontZ, int backZ)
    {
        foreach (int x in new[] { -halfX - 2, halfX })
            foreach (int z in new[] { backZ, frontZ - 8 })
            {
                s.Box(Body, x, 0, z, x + 2, 8, z + 8, Mat.Wood);
                s.Box(Body, x, 3, z + 3, x + 2, 5, z + 5, Mat.Iron);
                s.Cut(Body, x, 0, z, x + 2, 1, z + 1, both: true);
                s.Cut(Body, x, 7, z, x + 2, 8, z + 1, both: true);
                s.Cut(Body, x, 0, z + 7, x + 2, 1, z + 8, both: true);
                s.Cut(Body, x, 7, z + 7, x + 2, 8, z + 8, both: true);
            }
        s.Box(Body, -halfX, 3, backZ + 3, halfX, 5, backZ + 5, Mat.Handle);
        s.Box(Body, -halfX, 3, frontZ - 5, halfX, 5, frontZ - 3, Mat.Handle);
    }

    /// <summary>A battering ram: a shed of posts and boards under a ridge of wet hides, on four wheels, and slung from its roof a log with an iron head that swings out before it.</summary>
    static BodyPlan RamPlan(int look)
    {
        var f = new BodyPlan { Chop = 1.2f, Stride = 40 };
        var s = new Sculpt();
        Wheels(s, 12, 22, -22);
        s.Box(Body, -12, 5, -22, 12, 7, 22, Mat.Wood);
        foreach (int x in new[] { -12, 10 })
            foreach (int z in new[] { -22, -2, 18 })
                s.Box(Body, x, 7, z, x + 2, 26, z + 3, Mat.Wood);
        // the roof: boards, and hides over them against fire
        byte hide = look == 0 ? Mat.Leather : Mat.FurBoar;
        s.Box(Body, -14, 26, -23, 14, 28, 23, Mat.Board);
        s.Box(Body, -11, 28, -23, 11, 31, 23, hide);
        s.Box(Body, -6, 31, -23, 6, 34, 23, hide);
        s.Box(Body, -2, 34, -23, 2, 36, 23, hide);
        // boards down the sides, a gap at the foot
        s.Box(Body, -14, 12, -20, -12, 26, 20, Mat.BoardDark);
        s.Box(Body, 12, 12, -20, 14, 26, 20, Mat.BoardDark);
        // the log on its chains, its iron head out before the shed
        s.Box(Main, -1, 19, -12, 1, 26, -10, Mat.Iron, Tissue.Gear);
        s.Box(Main, -1, 19, 10, 1, 26, 12, Mat.Iron, Tissue.Gear);
        s.Box(Main, -3, 14, -22, 3, 20, 30, Mat.Trunk, Tissue.Gear);
        s.Box(Main, -4, 13, 30, 4, 21, 35, Mat.Iron, Tissue.Gear);
        RamDetail(s, look);
        Joint(f, Main, 0, 26 / H, 0);
        f.HeadY0 = 26; f.HeadY1 = 36;
        return s.Into(f, 8, 26, 12);
    }

    /// <summary>A stone-thrower: a frame of beams on wheels, two posts with a padded beam across them before, and between them the arm, cocked back, a sling-cup of hide at its end with a stone in it.</summary>
    static BodyPlan CatapultPlan(int look)
    {
        var f = new BodyPlan { Chop = 1, Stride = 40 };
        var s = new Sculpt();
        Wheels(s, 12, 22, -22);
        s.Box(Body, -12, 5, -22, -8, 9, 22, Mat.Wood);
        s.Box(Body, 8, 5, -22, 12, 9, 22, Mat.Wood);
        s.Box(Body, -12, 5, -22, 12, 9, -18, Mat.Wood);
        s.Box(Body, -12, 5, 18, 12, 9, 22, Mat.Wood);
        // the posts and the beam the arm strikes, padded with straw
        s.Box(Body, -12, 9, 8, -9, 30, 12, Mat.Wood);
        s.Box(Body, 9, 9, 8, 12, 30, 12, Mat.Wood);
        s.Box(Body, -12, 27, 8, 12, 30, 12, Mat.Board);
        s.Box(Body, -6, 25, 8, 6, 27, 12, look == 0 ? Mat.Straw : Mat.Sack);
        // the winch at the back, the skein of rope round the axle at the arm's foot
        s.Box(Body, -8, 9, -20, 8, 11, -17, Mat.Handle);
        s.Box(Body, -8, 8, -6, 8, 12, -2, Mat.Rag);
        // the arm, lying back from its axle, the cup and the stone at its end
        s.Box(Main, -2, 9, -26, 2, 12, -2, Mat.Trunk, Tissue.Gear);
        s.Box(Main, -4, 10, -32, 4, 14, -24, Mat.Leather, Tissue.Gear);
        s.Box(Main, -2, 14, -30, 2, 18, -26, Mat.Stone, Tissue.Gear);
        CatapultDetail(s);
        Joint(f, Main, 0, 10 / H, -4 / H);
        f.HeadY0 = 24; f.HeadY1 = 30;
        return s.Into(f, 8, 26, 12);
    }

    /// <summary>A mantlet: a wall of upright planks a little over a man's height, an iron band along its top and a slit to look through, on two wheels, the handles to push it by behind.</summary>
    static BodyPlan MantletPlan(int look)
    {
        var f = new BodyPlan { Chop = 1, Stride = 40 };
        var s = new Sculpt();
        byte plank = look == 0 ? Mat.BoardDark : Mat.Wood;
        s.Box(Body, -18, 3, 4, 18, 32, 8, plank);
        for (int x = -16; x < 18; x += 5) s.Box(Body, x, 3, 8, x + 1, 32, 9, Mat.Wood);
        s.Box(Body, -18, 30, 3, 18, 32, 9, Mat.Iron);
        s.Cut(Body, -6, 22, 4, 6, 23, 8, both: true);
        foreach (int x in new[] { -20, 18 })
        {
            s.Box(Body, x, 0, 0, x + 2, 8, 8, Mat.Wood);
            s.Box(Body, x, 3, 3, x + 2, 5, 5, Mat.Iron);
        }
        s.Box(Body, -18, 3, 2, 18, 5, 4, Mat.Handle);
        s.Box(Body, -12, 12, -10, -10, 14, 4, Mat.Handle);
        s.Box(Body, 10, 12, -10, 12, 14, 4, Mat.Handle);
        MantletDetail(s);
        f.HeadY0 = 22; f.HeadY1 = 32;
        return s.Into(f, 8, 26, 12);
    }

    /// <summary>A siege tower: posts and boards five metres up on a frame with six wheels, hides hung over its front and sides against fire, a fighting top with merlons, and a flap at the top to drop onto a wall.</summary>
    static BodyPlan TowerPlan()
    {
        var f = new BodyPlan { Chop = 1, Stride = 40 };
        var s = new Sculpt();
        foreach (int x in new[] { -18, 16 })
            foreach (int z in new[] { -18, -4, 10 })
            {
                s.Box(Body, x, 0, z, x + 2, 8, z + 8, Mat.Wood);
                s.Box(Body, x, 3, z + 3, x + 2, 5, z + 5, Mat.Iron);
            }
        s.Box(Body, -16, 4, -18, 16, 8, 18, Mat.Wood);
        // the corner posts and the boarded sides, hides over the front and the flanks
        foreach (int x in new[] { -16, 13 })
            foreach (int z in new[] { -16, 13 })
                s.Box(Body, x, 8, z, x + 3, 84, z + 3, Mat.Wood);
        s.Box(Body, -16, 8, -16, 16, 80, -14, Mat.BoardDark);
        s.Box(Body, -16, 8, -16, -14, 80, 16, Mat.Leather);
        s.Box(Body, 14, 8, -16, 16, 80, 16, Mat.Leather);
        s.Box(Body, -16, 8, 14, 16, 70, 16, Mat.Leather);
        for (int y = 20; y < 80; y += 14) s.Box(Body, -16, y, 14, 16, y + 2, 17, Mat.Wood);
        // the fighting top: a floor, merlons round it, the flap hinged at its front edge
        s.Box(Body, -17, 80, -17, 17, 82, 17, Mat.Board);
        for (int x = -17; x < 17; x += 8)
        {
            s.Box(Body, x, 82, -17, x + 4, 88, -15, Mat.Board);
            s.Box(Body, -17, 82, x, -15, 88, x + 4, Mat.Board);
            s.Box(Body, 15, 82, x, 17, 88, x + 4, Mat.Board);
        }
        s.Box(Body, -12, 70, 16, 12, 72, 30, Mat.Board);
        TowerDetail(s);
        f.HeadY0 = 80; f.HeadY1 = 88;
        return s.Into(f, 8, 26, 12);
    }

    static BodyPlan Miner(int look)
    {
        var f = new BodyPlan { Chop = .8f, Stride = 6 };
        byte skin = look % 2 == 1 ? Mat.SkinOrcGrey : Mat.SkinOrc;
        var b = new Build
        {
            Leg = 6, LegW = 3, LegD = 4, Gap = 1, Boot = 2, Torso = 8, Chest = 4, ChestD = 4, Head = 8, HeadW = 4, HeadD = 8, HeadZ = -4,
            Arm = 8, ArmW = 2, ArmD = 3, Hand = 3, Skin = skin, Shirt = Mat.Leather, Trousers = Mat.Rag, Boots = Mat.Iron, Eye = Mat.EyeGlint, Brow = skin,
            Sleeve = Mat.Rag, HairStyle = 3,
        };
        var s = Humanoid(b, f);
        int hx = b.HeadW, hz0 = b.HeadZ, hz1 = b.HZ1, hy1 = b.Crown;
        // a brass helmet with a candle burning on its brow, a great beard, a sack of ore on the back
        s.Paint(Body, -hx, hy1 - 3, hz0, hx, hy1, hz1, Mat.Brass, Tissue.Armor);
        s.Rim(Body, hy1 - 3, -hx, hz0, hx, hz1, Mat.Brass, Tissue.Armor);
        s.Box(Body, -1, hy1 - 2, hz1, 1, hy1, hz1 + 1, Mat.Iron, Tissue.Gear);
        s.Box(Body, -1, hy1, hz1 - 1, 1, hy1 + 1, hz1 + 1, Mat.Ember, Tissue.Gear);
        MinerDetail(s, b);
        Beard(s, b, new[] { Mat.Hair, Mat.HairGinger, Mat.HairGrey, Mat.HairBrown }[look % 4]);
        s.Box(Body, -3, b.Waist, b.Z0 - 3, 3, b.Neck, b.Z0, Mat.Linen, Tissue.Gear);
        s.Box(Body, -2, b.Neck, b.Z0 - 2, 2, b.Neck + 1, b.Z0 - 1, Mat.Leather, Tissue.Gear);
        Pick(s, b, Mat.Iron, 10);
        return Done(s, f, b);
    }

    /// <summary>
    /// Horns curving out of a head's sides and up, from the cell (x, y) at its left or right side (fine coordinates),
    /// tapering, their tips bone: an orc's helm's, a shaman's mask's.
    /// </summary>
    static void Horns(Sculpt s, float x, float y, float z, float len, float rise, float thick, Tissue t)
    {
        int n = (int)MathF.Ceiling(len);
        foreach (int sg in new[] { 1, -1 })
            for (int i = 0; i <= n; i++)
            {
                float k = i / (float)n, an = k * MathF.PI * .62f;
                float hx = x - len * .55f * MathF.Sin(an) * 1.1f, hy = y + rise * (1 - MathF.Cos(an)) * 1.4f, r = thick * (1 - k * .8f) / 2 + .26f;
                s.FBall(Body, (sg > 0 ? hx : -hx) / 2, hy / 2, z / 2, r, r, r, k > .8f ? Mat.Bone : Mat.Horn, t);
            }
    }

    /// <summary>
    /// A shaman's mask and finery drawn finer: the mask a skull, its corners rounded, the glint deep in dark eye holes,
    /// a hole for the nose, cheekbones, a row of teeth; horns out of its brow (antlers on the other look); the crest's
    /// feathers apart with dark tips; strings of beads on the chest over a rune that glows; the hides' hem ragged.
    /// </summary>
    static void ShamanDetail(Sculpt s, Build b, int look)
    {
        if (!s.Detail) return;
        int hx = b.HeadW, hz1 = b.HZ1, hy0 = b.Neck, E = (hy0 + 4) * 2, z = hz1 * 2, x0 = (-hx + 1) * 2, y0 = (hy0 + 1) * 2, y1 = (b.Crown - 1) * 2;
        foreach (int sg in new[] { 1, -1 })
        {
            int X(int x) => sg > 0 ? x : -1 - x;
            foreach (int y in new[] { y0, y1 - 1 }) { s.FDel(Body, X(x0), y, z); s.FDel(Body, X(x0), y, z + 1); }
            for (int y = E - 1; y < E + 3; y++) for (int x = x0; x < x0 + 4; x++) { s.FPut(Body, X(x), y, z + 1, Mat.Maw); s.FPut(Body, X(x), y, z, Mat.Maw); }
            s.FDel(Body, X(x0 + 1), E, z + 1); s.FDel(Body, X(x0 + 2), E, z + 1); s.FDel(Body, X(x0 + 1), E + 1, z + 1); s.FDel(Body, X(x0 + 2), E + 1, z + 1);
            s.FPut(Body, X(x0 + 1), E, z, Mat.EyeGlint); s.FPut(Body, X(x0 + 2), E + 1, z, Mat.EyeGlint);
            for (int x = x0; x < x0 + 3; x++) s.FSet(Body, X(x), E - 3, z + 2, Mat.Bone, Tissue.Gear);
        }
        s.FPut(Body, -1, E - 3, z + 1, Mat.Maw); s.FPut(Body, 0, E - 3, z + 1, Mat.Maw); s.FPut(Body, -1, E - 2, z + 1, Mat.Maw); s.FPut(Body, 0, E - 2, z + 1, Mat.Maw);
        for (int x = -5; x < 5; x++) s.FPut(Body, x, y0 + 1, z + 1, (x & 1) == 0 ? Mat.Bone : Mat.Maw);
        if (look == 0) Horns(s, x0 - .5f, y1 - 2, z + 1, 9, 5, 2.4f, Tissue.Gear);
        else
            foreach (int sg in new[] { 1, -1 })
            {
                int X(int x) => sg > 0 ? x : -1 - x;
                float bx = X(x0) + .5f, by = y1 - .5f, bz = z + .5f, o = sg > 0 ? -1 : 1;
                s.FLine(Body, bx / 2, by / 2, bz / 2, (bx + o * 3) / 2, (by + 9) / 2, bz / 2, Mat.Bone, Tissue.Gear);
                s.FLine(Body, (bx + o * 1) / 2, (by + 4) / 2, bz / 2, (bx + o * 5) / 2, (by + 6) / 2, bz / 2, Mat.Bone, Tissue.Gear);
                s.FLine(Body, (bx + o * 3) / 2, (by + 9) / 2, bz / 2, (bx + o * 2) / 2, (by + 13) / 2, (bz - 2) / 2, Mat.Bone, Tissue.Gear);
                s.FLine(Body, (bx + o * 3) / 2, (by + 9) / 2, bz / 2, (bx + o * 6) / 2, (by + 11) / 2, bz / 2, Mat.Bone, Tissue.Gear);
            }
        // the feathers of the crest apart, dark at their tips
        int top = b.Crown * 2;
        for (int x = -6; x < 6; x++)
        {
            if ((x & 1) == 1) { for (int y = top; y < top + 8; y++) { s.FDel(Body, x, y, -2); s.FDel(Body, x, y, -1); } continue; }
            int h = 7 - (int)MathF.Abs(x + .5f) + 1;
            for (int y = top + h - 1; y < top + h + 1; y++) { s.FPut(Body, x, y, -2, Mat.Hair); s.FPut(Body, x, y, -1, Mat.Hair); }
        }
        // the strings of beads, the rune under them
        int cz = b.Z1 * 2, neck = b.Neck * 2;
        for (int k = 0; k < 10; k++)
        {
            int bx = -5 + k, by = neck - 3 - (int)(4 * MathF.Sin(MathF.PI * k / 9f));
            s.FSet(Body, bx, by, cz, k % 3 == 0 ? Mat.ClothRed : k % 3 == 1 ? Mat.Bone : Mat.Brass, Tissue.Gear);
        }
        foreach (var (x, y) in new[] { (0, 0), (0, 1), (0, 2), (0, 3), (0, 4), (-1, 3), (-2, 4), (1, 3), (2, 4), (-1, 1), (1, 1) })
            s.FPut(Body, x - 1 + 1, b.Waist * 2 + 3 + y, cz - 1, Mat.EyeGlint);
        for (int x = -b.Chest * 2 - 2; x < b.Chest * 2 + 2; x++) if (Dice(x, look, 0, 111) < .5f) { s.FDel(Body, x, (b.Waist - 2) * 2, b.Z0 * 2 - 1); s.FDel(Body, x, (b.Waist - 2) * 2, b.Z0 * 2 - 2); }
    }

    /// <summary>
    /// A witch drawn finer: the eyes glowing in their sockets, a wart on the hooked nose, grey hair hanging in strands
    /// from under the shawl, the shawl fringed, bunches of herbs and a pouch at her belt, her skirt's hem in tatters.
    /// </summary>
    static void WitchDetail(Sculpt s, Build b, int look)
    {
        if (!s.Detail) return;
        int hy0 = b.Neck, F = b.HZ1 * 2 - 1, E = (hy0 + 3) * 2, W = b.HeadW * 2;
        foreach (int sg in new[] { 1, -1 })
        {
            int X(int x) => sg > 0 ? x : -1 - x;
            for (int y = E; y < E + 3; y++) for (int x = -7; x < -3; x++) s.FPut(Body, X(x), y, F, Mat.Maw);
            s.FDel(Body, X(-6), E + 1, F); s.FDel(Body, X(-5), E + 1, F); s.FSet(Body, X(-6), E + 1, F - 1, Mat.Ember); s.FSet(Body, X(-5), E + 1, F - 1, Mat.Ember);
            // strands of grey hair out of the shawl
            for (int z = F - 8; z < F - 1; z += 2)
            {
                int len = 4 + (int)(Dice(z, sg, look, 121) * 6);
                for (int k = 0; k < len; k++) s.FSet(Body, X(-W - 1 - (k > len - 3 ? 1 : 0)), hy0 * 2 - 6 - k + 6, z, Mat.HairGrey);
            }
        }
        // the nose drawn out and hooked down, a wart on it
        for (int k = 0; k < 4; k++) { s.FSet(Body, -1, (hy0 + 3) * 2 + 1 - k, F + 3 + k / 2, b.Skin); s.FSet(Body, 0, (hy0 + 3) * 2 + 1 - k, F + 3 + k / 2, b.Skin); }
        s.FSet(Body, -1, (hy0 + 3) * 2 - 3, F + 4, b.Skin); s.FSet(Body, 0, (hy0 + 3) * 2 - 3, F + 4, b.Skin);
        s.FSet(Body, 1, (hy0 + 4) * 2, F + 2, Mat.Crag);
        // the grey hair beside the face in strands
        foreach (int sg in new[] { 1, -1 })
            for (int y = (hy0 - 3) * 2; y < (hy0 + 3) * 2; y++)
                for (int z = (b.HZ1 - 3) * 2; z < (b.HZ1 - 1) * 2; z++)
                    if ((z & 1) == 1 && y < (hy0 + 1) * 2 || y < (hy0 - 3) * 2 + 1 + (int)(Dice(z, sg, look, 126) * 4))
                    {
                        s.FDel(Body, sg > 0 ? -2 * b.HeadW - 2 : 2 * b.HeadW + 1, y, z);
                        if (y < (hy0 - 1) * 2) s.FDel(Body, sg > 0 ? -2 * b.HeadW - 1 : 2 * b.HeadW, y, z);
                    }
        // her back bowed under the shawl
        s.FBall(Body, 0, b.Neck - 2.5f, b.Z0 - .6f, b.Chest - .5f, 2.4f, 1.4f, b.Shirt);
        // the shawl's fringe, the skirt's tatters
        int sy = (b.Neck - 4) * 2;
        for (int x = -b.Chest * 2 - 2; x < b.Chest * 2 + 2; x++)
            foreach (int z in new[] { (b.Z1 + 1) * 2 - 1, (b.Z0 - 1) * 2 })
            {
                if ((x & 1) == 0) s.FSet(Body, x, sy - 1, z, b.Shirt); else s.FDel(Body, x, sy, z);
                if (Dice(x, z, look, 122) < .4f) s.FDel(Body, x, 2, z);
            }
        // herbs at the belt on the right, a pouch on the left
        int wy = b.Waist * 2, cz = (b.Z1 + 1) * 2;
        for (int i = 0; i < 14; i++)
        {
            int hx = -b.Chest * 2 + (int)(Dice(i, 1, look, 123) * 5), hy = wy - 1 - (int)(Dice(i, 2, look, 124) * 7), hz = cz - 1 + (int)(Dice(i, 3, look, 125) * 2);
            s.FSet(Body, hx, hy, hz, i % 5 == 0 ? Mat.Bloom : i % 4 == 0 ? Mat.Flower : Mat.FoliageLight, Tissue.Gear);
        }
        for (int y = wy - 6; y < wy; y++) for (int x = b.Chest * 2 - 4; x < b.Chest * 2; x++) { s.FSet(Body, x, y, cz, Mat.Leather, Tissue.Gear); if (y > wy - 3) s.FSet(Body, x, y, cz + 1, Mat.LeatherDark, Tissue.Gear); }
        for (int x = -b.Chest * 2 - 2; x < b.Chest * 2 + 2; x++) s.FSet(Body, x, wy, cz, Mat.Linen, Tissue.Gear);
    }

    /// <summary>
    /// A necromancer drawn finer: the face lost in the shadow of the hood but for the pale fire of its eyes and its
    /// tusks, a mantle of bones over the shoulders, runes burning down the front of the robe and round its hem, the
    /// hands gone to bone, and small skulls strung at its girdle.
    /// </summary>
    static void NecroDetail(Sculpt s, Build b, int look)
    {
        if (!s.Detail) return;
        int hy0 = b.Neck, F = b.HZ1 * 2 - 1, E = (hy0 + 3) * 2, W = b.HeadW * 2;
        byte skin = b.Skin;
        s.FPattern(Body, -W, hy0 * 2, F - 2, W, b.Crown * 2, F + 3, (x, y, z) => s.FMat(Body, x, y, z) == skin ? (y >= E - 1 ? Mat.Hair : Shade(skin)) : (byte)0);
        // the mantle of bones over the shoulders, worn and cut like the robe (as carried gear it would hold the head on)
        int ny = (b.Neck - 1) * 2, cz0 = b.Z0 * 2 - 1, cz1 = b.Z1 * 2;
        for (int x = -b.Chest * 2 - 1; x <= b.Chest * 2; x++)
            for (int z = cz0; z <= cz1; z++)
            {
                bool rim = x == -b.Chest * 2 - 1 || x == b.Chest * 2 || z == cz0 || z == cz1;
                if (!rim || ((x + z) & 1) == 1) continue;
                s.FSet(Body, x, ny, z, Mat.Bone);
                if (((x + z) & 3) == 0) s.FSet(Body, x, ny - 1, z, Mat.Bone);
            }
        // runes burning down the front and round the hem
        var glyphs = new[] { new[] { (0, 0), (0, 1), (0, 2), (0, 3), (1, 2), (1, 1) }, new[] { (0, 0), (0, 1), (0, 2), (0, 3), (-1, 3), (1, 3) }, new[] { (0, 0), (1, 1), (0, 2), (1, 3), (0, 1) } };
        int front = (b.Z1 + 1) * 2 - 1;
        for (int i = 0; i < 4; i++)
            foreach (var (gx, gy) in glyphs[i % 3]) s.FPut(Body, gx - 1 + (i & 1), 6 + i * 6 + gy, front, Mat.SoulFire);
        for (int x = -b.Chest * 2; x < b.Chest * 2; x += 5)
            foreach (var (gx, gy) in glyphs[(x + 20) % 3]) s.FPut(Body, x + gx, 4 + gy, front, Mat.SoulFire);
        // the hands of bone, the fingers apart
        int hx0 = b.ArmX0 * 2, hx1 = -b.Chest * 2, hy = b.HandY * 2, hz0 = b.ArmZ0 * 2, hz1 = b.ArmZ1 * 2;
        foreach (var p in new[] { Main, Off })
            s.FPattern(p, -80, hy, -80, 80, hy + b.Hand * 2, 80, (x, y, z) => s.FMat(p, x, y, z) == skin ? (((z - hz0) % 2 == 1 && y < hy + 3) ? Mat.Maw : Mat.Bone) : (byte)0);
        // small skulls at the girdle
        int wy = b.Waist * 2;
        foreach (int x in new[] { -6, -1, 4 })
        {
            s.FBall(Body, (x + 1) / 2f, (wy - 2.5f) / 2, (front + 1.5f) / 2, 1.1f, 1f, 1f, Mat.Bone, Tissue.Gear);
            s.FPut(Body, x, wy - 2, front + 3, Mat.Maw); s.FPut(Body, x + 2, wy - 2, front + 3, Mat.Maw);
        }
    }

    /// <summary>
    /// The dead raised, drawn finer: the skull with its sockets deep and the fire in them, the hole of the nose, the jaw
    /// apart from the teeth, the ribs with the dark between them, the spine down the back in its knuckles, the limbs
    /// thinned to bones with knobs at the elbows and knees; and on the one with a helmet, rusted mail left on its shoulders.
    /// </summary>
    static void BonesDetail(Sculpt s, Build b, int look, byte fire)
    {
        if (!s.Detail) return;
        int hy0 = b.Neck, F = b.HZ1 * 2 - 1, E = (hy0 + 3) * 2, H0 = hy0 * 2;
        foreach (int sg in new[] { 1, -1 })
        {
            int X(int x) => sg > 0 ? x : -1 - x;
            for (int y = E; y < E + 4; y++) for (int x = -6; x < -2; x++) { s.FDel(Body, X(x), y, F); s.FPut(Body, X(x), y, F - 1, x is -5 or -4 && y > E && y < E + 3 ? fire : Mat.Maw); }
            s.FSet(Body, X(-7), E - 1, F + 1, Mat.Bone); s.FSet(Body, X(-6), E - 1, F + 1, Mat.Bone);
        }
        s.FPut(Body, -1, E - 2, F, Mat.Maw); s.FPut(Body, 0, E - 2, F, Mat.Maw); s.FPut(Body, -1, E - 3, F, Mat.Maw); s.FPut(Body, 0, E - 3, F, Mat.Maw);
        for (int x = -5; x < 5; x++) { s.FPut(Body, x, H0 + 4, F, (x & 1) == 0 ? Mat.Bone : Mat.Maw); s.FPut(Body, x, H0 + 3, F, Mat.Maw); s.FPut(Body, x, H0 + 2, F, Mat.Bone); }
        // the ribs and the dark between them, front and sides; the spine's knuckles behind
        int w = b.Chest * 2, z0 = b.Z0 * 2, z1 = b.Z1 * 2;
        for (int y = (b.Waist + 3) * 2; y < (b.Neck - 1) * 2; y++)
        {
            if ((y & 1) == 0) continue;
            for (int x = -w; x < w; x++)
            {
                if (Math.Abs(x + .5f) < 1) continue;
                s.FDel(Body, x, y, z1 - 1); s.FPut(Body, x, y, z1 - 2, Mat.Maw);
            }
            for (int z = z0 + 1; z < z1 - 1; z++) foreach (int x in new[] { -w, w - 1 }) { s.FDel(Body, x, y, z); s.FPut(Body, x + (x < 0 ? 1 : -1), y, z, Mat.Maw); }
        }
        for (int y = b.Waist * 2; y < b.Neck * 2; y += 2) { s.FSet(Body, -1, y, z0 - 1, Mat.Bone); s.FSet(Body, 0, y, z0 - 1, Mat.Bone); }
        // the limbs thinned to bones, knobs left at the elbows and the knees
        int ax0 = b.ArmX0 * 2, ax1 = -b.Chest * 2, az0 = b.ArmZ0 * 2, az1 = b.ArmZ1 * 2, elbow = b.Neck + b.HandY + b.Hand;
        foreach (var (p, sg) in new[] { (Main, 1), (Off, -1) })
        {
            int X(int x) => sg > 0 ? x : -1 - x;
            for (int y = (b.HandY + b.Hand) * 2; y < (b.Neck - 1) * 2; y++)
            {
                if (Math.Abs(y - elbow) < 2) continue;
                for (int z = az0; z < az1; z++) for (int x = ax0; x < ax1; x++)
                        if (x == ax0 || z == az0 || z == az1 - 1) s.FDel(p, X(x), y, z);
            }
        }
        int lx0 = b.LegX0 * 2, lx1 = b.LegX1 * 2, lz0 = -(b.LegD / 2) * 2, lz1 = (b.LegD - b.LegD / 2) * 2, knee = b.Leg;
        foreach (var (p, sg) in new[] { (LegR, 1), (LegL, -1) })
        {
            int X(int x) => sg > 0 ? x : -1 - x;
            for (int y = 2; y < b.Leg * 2 - 2; y++)
            {
                if (Math.Abs(y - knee) < 2) continue;
                for (int z = lz0; z < lz1; z++) for (int x = lx0; x < lx1; x++)
                        if (x == lx0 || z == lz0 || z == lz1 - 1) s.FDel(p, X(x), y, z);
            }
        }
        if (look == 1)
            s.FPattern(Body, -80, (b.Neck - 3) * 2, -80, 80, b.Neck * 2, 80, (x, y, z) => s.FMat(Body, x, y, z) == Mat.Bone && Dice(x >> 1, y, z >> 1, 131) < .6f ? (Dice(x, y, z, 132) < .3f ? Mat.Rust : Mat.Mail) : (byte)0);
    }

    /// <summary>A miner's brass helmet drawn finer: its rim riveted, a candle of tallow in an iron cup on its brow, burning.</summary>
    static void MinerDetail(Sculpt s, Build b)
    {
        if (!s.Detail) return;
        int hz1 = b.HZ1, hy1 = b.Crown, rim = (hy1 - 3) * 2, F = hz1 * 2;
        s.FPattern(Body, -80, rim, -80, 80, rim + 2, 80, (x, y, z) => s.FMat(Body, x, y, z) == Mat.Brass && (x + z + 64) % 4 == 0 && y == rim + 1 ? Mat.Iron : (byte)0);
        s.FCut(Body, -1, hy1, hz1 - 1, 1, hy1 + 1, hz1 + 1);
        for (int y = hy1 * 2 - 3; y < hy1 * 2; y++) for (int x = -2; x < 2; x++) s.FSet(Body, x, y, F + 1, Mat.Iron, Tissue.Gear);
        for (int y = hy1 * 2; y < hy1 * 2 + 3; y++) { s.FSet(Body, -1, y, F + 1, Mat.Linen, Tissue.Gear); s.FSet(Body, 0, y, F + 1, Mat.Linen, Tissue.Gear); }
        s.FSet(Body, -1, hy1 * 2 + 3, F + 1, Mat.Ember, Tissue.Gear); s.FSet(Body, 0, hy1 * 2 + 3, F + 1, Mat.Ember, Tissue.Gear); s.FSet(Body, -1, hy1 * 2 + 4, F + 1, Mat.Ember, Tissue.Gear);
    }

    /// <summary>
    /// An orc of the horde: a heavy brow, a broad flat nose, a jaw thrust out with tusks rising from it, ears standing
    /// out; a sash or a chain across the chest, iron on the shoulders with spikes, a kilt of leather strips; a topknot,
    /// or an iron cap with horns. Near: eyes glowing deep under the scowling brow, the nostrils flared, a brass ring
    /// through the nose of some, the tusks curving up and out of the lower jaw past small teeth, ears pointed back and
    /// ringed; scars over the eye of some; a crest of hair over a shaved scalp inked with spirals, or the iron cap riveted
    /// with great horns curving out of it; a breastplate of rusted iron, its edges ragged, riveted over the jerkin; the
    /// pauldrons ragged and riveted, horn spikes on them; the kilt of hide, fur at its belt; ink round the bare arms.
    /// </summary>
    static BodyPlan Orc(int look)
    {
        var f = new BodyPlan { Chop = .95f, Carry = -.3f, Stride = 10 };
        byte skin = look % 2 == 1 ? Mat.SkinOrcGrey : Mat.SkinOrc;
        var b = new Build
        {
            Leg = 12, LegW = 4, LegD = 5, Gap = 1, Boot = 3, Torso = 12, Chest = 6, ChestD = 7, Head = 8, HeadW = 4, HeadD = 8, HeadZ = -2,
            Arm = 12, ArmW = 4, ArmD = 4, Hand = 4, Skin = skin, Shirt = Mat.Leather, Trousers = Mat.ClothDark, Boots = Mat.Leather,
            Eye = Mat.Ember, Brow = skin, Mouth = Mat.Hair, Sleeve = skin, HairStyle = 3,
        };
        var s = Humanoid(b, f);
        int hx = b.HeadW, hz0 = b.HeadZ, hz1 = b.HZ1, hy0 = b.Neck, hy1 = b.Crown;
        // a heavy brow, a broad flat nose, a jaw thrust out with tusks rising from it
        s.Box(Body, -hx + 1, hy0 + 5, hz1, hx - 1, hy0 + 6, hz1 + 1, skin);
        s.Box(Body, -2, hy0 + 2, hz1, 2, hy0 + 4, hz1 + 1, skin);
        s.Box(Body, -3, hy0, hz1, 3, hy0 + 2, hz1 + 1, skin);
        s.Pair(Body, Body, -3, hy0 + 1, hz1 + 1, -2, hy0 + 4, hz1 + 2, Mat.Bone);
        s.Box(Body, -2, hy0 + 1, hz1 + 1, 2, hy0 + 2, hz1 + 2, Mat.Hair);
        s.Pair(Body, Body, -hx - 2, hy0 + 4, 0, -hx, hy0 + 5, 1, skin);
        // a sash across the chest, or a chain; iron on the shoulders with spikes, a kilt of leather strips
        byte sash = look % 3 == 2 ? Mat.ClothOchre : Mat.ClothRed;
        bool chain = look % 2 == 1;
        for (int y = b.Waist + 1; y < b.Neck; y++)
        {
            int x = -b.Chest + (y - b.Waist - 1) * (2 * b.Chest - 2) / (b.Torso - 1);
            s.Paint(Body, x, y, b.Z0, x + 3, y + 1, b.Z1, chain ? Mat.Leather : sash);
        }
        s.Box(Main, b.ArmX0 - 1, b.Neck - 4, b.ArmZ0 - 1, -b.Chest, b.Neck, b.ArmZ1 + 1, Mat.Iron, Tissue.Armor);
        s.Box(Off, b.Chest, b.Neck - 4, b.ArmZ0 - 1, -b.ArmX0 + 1, b.Neck, b.ArmZ1 + 1, Mat.Iron, Tissue.Armor);
        s.Box(Main, b.ArmX0, b.Neck, -1, b.ArmX0 + 1, b.Neck + 2, 0, Mat.Bone, Tissue.Armor);
        s.Box(Off, -b.ArmX0 - 1, b.Neck, -1, -b.ArmX0, b.Neck + 2, 0, Mat.Bone, Tissue.Armor);
        for (int x = -b.Chest; x < 0; x += 2)
            s.Pair(Body, Body, x, b.Waist - 4, b.Z1, x + 1, b.Waist + 1, b.Z1 + 1, Mat.Leather);
        // a topknot, or an iron cap with horns
        if (look >= 3)
        {
            s.Paint(Body, -hx, hy1 - 3, hz0, hx, hy1, hz1, Mat.Iron, Tissue.Armor);
            s.Rim(Body, hy1 - 3, -hx, hz0, hx, hz1, Mat.Iron, Tissue.Armor);
            s.Pair(Body, Body, -hx - 1, hy1 - 2, 0, -hx, hy1 + 2, 2, Mat.Bone, Tissue.Armor);
            s.Pair(Body, Body, -hx - 2, hy1 + 1, 0, -hx - 1, hy1 + 3, 2, Mat.Bone, Tissue.Armor);
        }
        else
        {
            s.Paint(Body, -hx, hy1 - 1, hz0, hx, hy1, hz1, Mat.Hair);
            s.Box(Body, -1, hy1, -1, 1, hy1 + 3, 1, Mat.Hair);
            s.Box(Body, -1, hy1 + 1, -3, 1, hy1 + 2, -1, Mat.Hair);
        }
        OrcDetail(s, b, look, chain);
        Axe(s, b);
        return Done(s, f, b);
    }

    /// <summary>The finer work on an orc (see <see cref="Orc"/>).</summary>
    static void OrcDetail(Sculpt s, Build b, int look, bool chain)
    {
        if (!s.Detail) return;
        int hx = b.HeadW, hz0 = b.HeadZ, hz1 = b.HZ1, hy0 = b.Neck, hy1 = b.Crown;
        int F = hz1 * 2 - 1, H0 = hy0 * 2, W = hx * 2, E = (hy0 + 3) * 2;
        byte skin = b.Skin, shade = Shade(skin);
        // the brow scowling: low over the nose, the furrow between, its ends cut away
        for (int x = -3; x < 3; x++) s.FSet(Body, x, H0 + 9, F + 1, skin);
        s.FDel(Body, -1, H0 + 11, F + 2); s.FDel(Body, 0, H0 + 11, F + 2);
        foreach (int x in new[] { -6, 5 }) { s.FDel(Body, x, H0 + 11, F + 1); s.FDel(Body, x, H0 + 11, F + 2); }
        // the eyes: dark sockets under it, a slit of ember deep in each
        foreach (int sg in new[] { 1, -1 })
        {
            int X(int x) => sg > 0 ? x : -1 - x;
            for (int y = E; y < E + 4; y++) for (int x = -7; x < -3; x++) s.FPut(Body, X(x), y, F, y == E ? skin : Mat.Maw);
            for (int x = -6; x < -4; x++) { s.FDel(Body, X(x), E + 2, F); s.FSet(Body, X(x), E + 2, F - 1, Mat.Ember); }
        }
        // the nose flattened, its nostrils flared and dark
        foreach (int x in new[] { -4, -3, 2, 3 }) for (int y = H0 + 6; y < H0 + 8; y++) { s.FDel(Body, x, y, F + 1); s.FDel(Body, x, y, F + 2); }
        for (int y = H0 + 6; y < H0 + 8; y++) for (int x = -2; x < 2; x++) s.FDel(Body, x, y, F + 2);
        s.FPut(Body, -3, H0 + 4, F + 2, Mat.Maw); s.FPut(Body, 2, H0 + 4, F + 2, Mat.Maw);
        if (look % 3 == 1)
            foreach (var (x, y) in new[] { (-1, H0 + 3), (0, H0 + 3), (-2, H0 + 4), (1, H0 + 4) }) s.FSet(Body, x, y, F + 3, Mat.Brass);
        // the mouth a dark line with small teeth along the jutting lower lip
        for (int x = -4; x < 4; x++)
        {
            s.FDel(Body, x, H0 + 2, F + 4); s.FDel(Body, x, H0 + 3, F + 4);
            s.FSet(Body, x, H0 + 3, F + 3, (x + 8) % 3 == 0 ? Mat.Bone : Mat.Maw);
            s.FSet(Body, x, H0 + 2, F + 3, skin);
        }
        // the tusks curving up and out of the lower jaw
        foreach (int sg in new[] { 1, -1 })
        {
            int X(int x) => sg > 0 ? x : -1 - x;
            for (int y = H0 + 2; y < H0 + 8; y++) for (int z = F + 3; z < F + 5; z++) { s.FDel(Body, X(-6), y, z); s.FDel(Body, X(-5), y, z); }
            foreach (var (x, y, z) in new[] { (-6, 2, 3), (-5, 2, 3), (-6, 3, 3), (-5, 3, 3), (-6, 4, 3), (-6, 4, 4), (-6, 5, 4), (-7, 5, 4), (-7, 6, 4), (-7, 7, 4), (-7, 7, 3), (-7, 8, 3) })
                s.FSet(Body, X(x), H0 + y, F + z, y >= 7 ? Mat.Bone : Mat.Tusk);
        }
        // the ears pointed back, ringed on some
        s.FCut(Body, -hx - 2, hy0 + 4, 0, -hx, hy0 + 5, 1, both: true);
        foreach (int sg in new[] { 1, -1 })
        {
            int X(int x) => sg > 0 ? x : -1 - x;
            for (int d = 0; d < 5; d++)
                for (int y = E + 1 + d / 2; y < E + 5 - d / 3; y++)
                    for (int z = 2 - d; z < 4 - d; z++) s.FSet(Body, X(-W - 1 - d / 2), y, z, d > 0 && d < 4 && y > E + 1 + d / 2 ? shade : skin);
            if (look % 3 == 1) { s.FSet(Body, X(-W - 1), E, 3, Mat.Brass); s.FSet(Body, X(-W - 1), E - 1, 2, Mat.Brass); s.FSet(Body, X(-W - 1), E, 1, Mat.Brass); }
        }
        // a scar over the right eye on some
        if (look % 3 == 0)
            for (int k = 0; k < 8; k++) s.FPut(Body, -7 + k / 2, H0 + 13 - k, F, Mat.Scar);
        if (look < 3)
        {
            // the crest of hair over the shaved scalp, inked with spirals
            int top = hy1 * 2;
            s.FCut(Body, -1, hy1, -3, 1, hy1 + 3, 1);
            for (int z = 2 * hz0 + 1; z < 2 * hz1 - 2; z++)
                for (int x = -1; x < 1; x++)
                {
                    int h = 2 + (int)(Dice(z, x, look, 91) * 3) + (z > 0 ? 1 : 0);
                    for (int y = top - 2; y < top + h; y++) s.FSet(Body, x, y, z, Mat.Hair);
                }
            s.FPattern(Body, -W, top - 2, 2 * hz0, W, top, 2 * hz1, (x, y, z) => s.FMat(Body, x, y, z) == Mat.Hair && Math.Abs(x + .5f) > 1 ? skin : (byte)0);
            foreach (int sg in new[] { 1, -1 })
            {
                int X(int x) => sg > 0 ? x : -1 - x;
                foreach (var (y, z) in new[] { (0, 0), (0, 1), (0, 2), (1, 2), (2, 2), (2, 1), (2, 0), (2, -1), (1, -1), (-1, -1), (-1, 3), (3, 3), (3, -2) })
                    s.FPut(Body, X(-W), H0 + 11 + y, 2 + z, Mat.Ink);
            }
        }
        else
        {
            // the cap riveted, and horns curving out of it and up
            int rim = (hy1 - 3) * 2;
            s.FPattern(Body, -W - 2, rim, 2 * hz0 - 2, W + 2, rim + 2, 2 * hz1 + 2, (x, y, z) => s.FMat(Body, x, y, z) == Mat.Iron && (x + z + 64) % 4 == 0 && y == rim + 1 ? Mat.Steel : (byte)0);
            s.FPattern(Body, -W - 2, rim, 2 * hz0 - 2, W + 2, hy1 * 2 + 1, 2 * hz1 + 2, (x, y, z) => s.FMat(Body, x, y, z) == Mat.Iron && Dice(x >> 1, y >> 1, z >> 1, 92) < .18f ? Mat.Rust : (byte)0);
            s.FCut(Body, -hx - 2, hy1 - 2, 0, -hx, hy1 + 3, 2, both: true);
            bool chief = look == 5;
            Horns(s, -(W + .5f), (hy1 - 1) * 2, 1.5f, chief ? 15 : 10, chief ? 9 : 6, chief ? 4.8f : 3.6f, Tissue.Armor);
        }
        // the breastplate of rusted iron, ragged at its edges and riveted, or a chain across the chest
        int cw = b.Chest * 2, z1 = b.Z1 * 2, y0 = (b.Waist + 3) * 2, y1 = (b.Neck - 2) * 2;
        if (!chain)
            for (int y = y0; y < y1; y++)
                for (int x = -cw + 2; x < cw - 2; x++)
                {
                    bool edge = y < y0 + 2 || y >= y1 - 2 || x < -cw + 4 || x >= cw - 4;
                    if (edge && Dice(x, y, look, 93) < .35f) continue;
                    bool rivet = edge && (x + y) % 4 == 0;
                    byte m = rivet ? Mat.Steel : Dice(x >> 1, y >> 1, look, 94) < .18f ? Mat.Rust : Mat.Iron;
                    s.FSet(Body, x, y, z1, m, Tissue.Armor); s.FSet(Body, x, y, z1 + 1, m, Tissue.Armor);
                }
        else
            for (int y = (b.Waist + 1) * 2; y < b.Neck * 2; y++)
            {
                float k = (y - (b.Waist + 1) * 2) / (float)((b.Torso - 1) * 2);
                int x = (int)(-cw + 2 + k * (2 * cw - 4)), z = (y & 2) == 0 ? z1 : z1 + 1;
                s.FSet(Body, x, y, z, Mat.Iron, Tissue.Armor); s.FSet(Body, x + 1, y, z, Mat.Iron, Tissue.Armor);
                if ((y & 2) == 0) s.FSet(Body, x + 1, y, z1 + 1, Mat.Iron, Tissue.Armor);
            }
        // the pauldrons ragged and riveted, horn spikes on the left
        int ax0 = (b.ArmX0 - 1) * 2, az0 = (b.ArmZ0 - 1) * 2, az1 = (b.ArmZ1 + 1) * 2, py = (b.Neck - 4) * 2;
        foreach (var (p, sg) in new[] { (Main, 1), (Off, -1) })
        {
            int X(int x) => sg > 0 ? x : -1 - x;
            for (int z = az0; z < az1; z++)
                for (int x = ax0; x < -cw; x++)
                {
                    if (Dice(x, z, sg, 95) < .4f) s.FDel(p, X(x), py, z);
                    if ((x + z) % 3 == 0 && (x == ax0 || z == az0 || z == az1 - 1)) s.FPut(p, X(x), py + 3, z, Mat.Steel);
                }
            s.FPattern(p, -80, py, -80, 80, b.Neck * 2, 80, (x, y, z) => s.FMat(p, x, y, z) == Mat.Iron && Dice(x >> 1, y >> 1, z >> 1, 96 + sg) < .18f ? Mat.Rust : (byte)0);
            if (sg < 0)
                foreach (int z in new[] { az0 + 2, az1 - 3 })
                    for (int k = 0; k < 4; k++) { s.FSet(p, X(ax0 + 4), b.Neck * 2 + k, z, k == 3 ? Mat.Bone : Mat.Horn, Tissue.Armor); if (k < 2) s.FSet(p, X(ax0 + 3), b.Neck * 2 + k, z, Mat.Horn, Tissue.Armor); }
            // ink round the bare arm, a leather wrap at the wrist
            for (int y = (b.HandY + b.Hand) * 2; y < (b.HandY + b.Hand) * 2 + 3; y++) s.FPattern(p, -80, y, -80, 80, y + 1, 80, (x, yy, z) => s.FMat(p, x, yy, z) == skin ? Mat.Leather : (byte)0);
            for (int y = py - 8; y < py - 2; y++) s.FPattern(p, -80, y, -80, 80, y + 1, 80, (x, yy, z) => s.FMat(p, x, yy, z) == skin && ((x + z + yy) % 4 == 0 || yy == py - 8 || yy == py - 3) ? Mat.Ink : (byte)0);
        }
        // the kilt of hide in strips, ragged, fur at its belt; fur at the boots' tops
        for (int x = -cw; x < cw; x++)
            for (int z = -16; z < 20; z++)
            {
                if (s.FMat(Body, x, (b.Waist - 4) * 2, z) == Mat.Leather && (x & 1) == 0) s.FDel(Body, x, (b.Waist - 4) * 2, z);
                for (int y = (b.Waist - 4) * 2; y < b.Waist * 2; y++) if (s.FMat(Body, x, y, z) == Mat.Leather && z > z1 - 2) s.FPut(Body, x, y, z, Mat.Hide);
            }
        s.FPattern(Body, -80, b.Waist * 2, -80, 80, b.Waist * 2 + 2, 80, (x, y, z) => s.FMat(Body, x, y, z) == Mat.Leather ? Mat.FurBoar : (byte)0);
        foreach (var p in new[] { LegR, LegL }) s.FPattern(p, -80, b.Boot * 2 - 1, -80, 80, b.Boot * 2, 80, (x, y, z) => s.FHas(p, x, y, z) ? Mat.FurBoar : (byte)0);
    }

    /// <summary>
    /// A troll drawn finer: its hide in two shades, blotched; growths of stone on its shoulders, its back, its elbows and
    /// its knees and warts of it on its face; a heavy brow over small eyes glowing in their sockets, a long nose drooping
    /// to a bulb over the jaw, teeth of all sizes out of the jaw (one tusk broken), a beard of moss hanging from the jaw,
    /// claws on its fingers and toes; a wild one has a shackle on its left wrist with a length of broken chain.
    /// </summary>
    static void TrollDetail(Sculpt s, Build b, int look, bool tame)
    {
        if (!s.Detail) return;
        int hy0 = b.Neck, hz1 = b.HZ1, F = hz1 * 2 - 1, H0 = hy0 * 2, E = (hy0 + 3) * 2, W = b.HeadW * 2;
        byte skin = b.Skin;
        // the hide blotched darker
        foreach (var p in new[] { Body, Main, Off, LegR, LegL })
            s.FPattern(p, -80, 0, -80, 80, 200, 80, (x, y, z) => s.FMat(p, x, y, z) == skin && Dice(x >> 2, y >> 2, z >> 2, 100 + look) < .34f ? Mat.SkinTrollDark : (byte)0);
        // growths of stone
        float cz = (b.Z0 + b.Z1) / 2f;
        foreach (var (x, y, z, r) in new[] { (-b.Chest + 1.5f, b.Neck - .6f, cz - 1f, 1.5f), (b.Chest - 2.5f, b.Neck - .2f, cz + .5f, 1.2f), (-2f, b.Neck - 4f, b.Z0 - .2f, 1.4f),
            (3f, b.Neck - 8f, b.Z0 - .2f, 1.1f), (0f, b.Waist + 5f, b.Z0 - .2f, 1.3f), (-3f, b.Crown - .3f, 5f, 1.3f), (2.5f, b.Crown - .2f, 7f, 1f) })
            s.FBall(Body, x, y, z, r, r * .8f, r, Mat.Crag);
        float ex = b.ArmX0 + b.ArmW / 2f, ey = (b.Neck + b.HandY + b.Hand) / 2f, ez = b.ArmZ0 - .1f;
        s.FBall(Main, ex, ey, ez, 1.3f, 1.6f, 1.1f, Mat.Crag); s.FBall(Off, -ex, ey + 1, ez, 1.1f, 1.4f, 1f, Mat.Crag);
        float kx = (b.LegX0 + b.LegX1) / 2f, ky = b.Leg * .55f, kz = b.LegD - b.LegD / 2 + .1f;
        s.FBall(LegR, kx, ky, kz, 1.2f, 1.2f, 1f, Mat.Crag); s.FBall(LegL, -kx, ky - 1, kz, 1f, 1f, .9f, Mat.Crag);
        foreach (var (x, y) in new[] { (-9, E - 3), (8, E - 1), (5, E + 6) }) s.FSet(Body, x, y, F + 1, Mat.Crag);
        // the brow, and small eyes glowing in their sockets under it
        for (int x = -W + 2; x < W - 2; x++) { s.FSet(Body, x, E + 5, F + 1, skin); s.FSet(Body, x, E + 6, F + 1, skin); if (Math.Abs(x + .5f) > 2) s.FSet(Body, x, E + 5, F + 2, skin); }
        foreach (int sg in new[] { 1, -1 })
        {
            int X(int x) => sg > 0 ? x : -1 - x;
            for (int y = E + 1; y < E + 4; y++) for (int x = -11; x < -8; x++) s.FPut(Body, X(x), y, F, y == E + 3 || x == -11 ? Mat.Maw : skin);
            for (int x = -11; x < -9; x++) { s.FDel(Body, X(x), E + 2, F); s.FSet(Body, X(x), E + 2, F - 1, Mat.Ember); }
        }
        // the long nose drooping to its bulb
        for (int y = E - 2; y < E + 6; y++) for (int x = -2; x < 2; x++) for (int z = F + 1; z < F + 3; z++) s.FDel(Body, x, y, z);
        s.FRod(Body, 0, (E + 5) / 2f, (F + 1) / 2f, 0, (E - 3) / 2f, (F + 11) / 2f, 1.3f, skin);
        s.FBall(Body, 0, (E - 4f) / 2, (F + 11.5f) / 2, 2f, 1.8f, 1.8f, Mat.SkinTrollDark);
        for (int z = F + 16; z > F; z--) if (s.FHas(Body, -2, E - 8, z)) { s.FPut(Body, -2, E - 8, z, Mat.Maw); s.FPut(Body, 1, E - 8, z, Mat.Maw); break; }
        s.FSet(Body, 3, E - 3, F + 13, Mat.Crag);
        // the mouth along the top of the jaw, and teeth of all sizes up out of it, the right tusk broken
        int jaw = (hy0 + 3) * 2, front = (hz1 + 3) * 2 - 1;
        for (int x = -6; x < 6; x++) s.FPut(Body, x, jaw - 1, front, Mat.Maw);
        foreach (int sg in new[] { 1, -1 })
        {
            int X(int x) => sg > 0 ? x : -1 - x;
            for (int y = jaw; y < jaw + 4; y++) for (int z = (hz1 + 1) * 2; z < (hz1 + 3) * 2; z++) { s.FDel(Body, X(-8), y, z); s.FDel(Body, X(-7), y, z); s.FDel(Body, X(-4), y, z); s.FDel(Body, X(-3), y, z); }
            int tall = sg > 0 ? 6 : 2;
            for (int y = jaw - 1; y < jaw + tall; y++) { s.FSet(Body, X(-7), y, front - 2, Mat.Tusk); if (y < jaw + tall - 2) s.FSet(Body, X(-8), y, front - 2, Mat.Tusk); }
            s.FSet(Body, X(-4), jaw, front - 1, Mat.Bone); s.FSet(Body, X(-4), jaw + 1, front - 1, Mat.Bone); s.FSet(Body, X(-2), jaw, front - 1, Mat.Bone);
        }
        // a beard of moss hanging from the jaw
        if (look != 2)
            for (int x = -7; x < 7; x++)
                for (int z = front - 4; z <= front; z += 2)
                {
                    int len = 2 + (int)(Dice(x, z, look, 101) * 8);
                    for (int k = 1; k <= len; k++) s.FSet(Body, x, H0 - k, z, Mat.Moss);
                }
        // claws on the fingers and on the toes
        int hx0 = b.ArmX0 * 2, hx1 = -b.Chest * 2, hy = b.HandY * 2, hz0 = b.ArmZ0 * 2, hzz = b.ArmZ1 * 2;
        foreach (var (p, sg) in new[] { (Main, 1), (Off, -1) })
        {
            int X(int x) => sg > 0 ? x : -1 - x;
            for (int z = hz0 + 1; z < hzz - 1; z += 3) { s.FSet(p, X(hx1 - 1), hy, z, Mat.Horn); s.FSet(p, X(hx1 - 1), hy - 1, z, Mat.Horn); }
        }
        Claws(s, b, Mat.Horn);
        if (tame) return;
        // a shackle on the left wrist and a length of broken chain from it
        int wy = (b.HandY + b.Hand) * 2;
        for (int y = wy; y < wy + 4; y++)
            for (int z = hz0 - 1; z <= hzz; z++)
                for (int x = -hx1; x <= -hx0; x++)
                    if (x == -hx0 || z == hz0 - 1 || z == hzz) s.FSet(Off, x, y, z, y == wy + 1 || y == wy + 2 ? Mat.Iron : Mat.Rust, Tissue.Armor);
        for (int k = 0; k < 12; k++)
        {
            int y = wy - 1 - k, z = hzz - 1 - (k & 1);
            s.FSet(Off, -hx0, y, z, Mat.Iron, Tissue.Armor);
            if (k % 4 == 1) s.FSet(Off, -hx0 + 1, y, z, Mat.Iron, Tissue.Armor);
        }
    }

    static BodyPlan Troll(int look)
    {
        var f = new BodyPlan { Chop = 1.4f, Carry = -.2f, Stride = 14 };
        var b = new Build
        {
            Leg = 16, LegW = 6, LegD = 6, Gap = 2, Boot = 0, Torso = 20, Chest = 8, ChestD = 10, Head = 10, HeadW = 6, HeadD = 10, HeadZ = 0,
            Arm = 24, ArmW = 6, ArmD = 6, Hand = 5, Skin = Mat.SkinTroll, Shirt = Mat.SkinTroll, Trousers = Mat.SkinTroll, Boots = Mat.SkinTroll,
            Belt = Mat.Leather, Hair = look == 2 ? Mat.SkinTroll : Mat.Moss, Brow = Mat.SkinTroll, Eye = Mat.Ember, Mouth = Mat.Hair, HairStyle = 1,
        };
        var s = Humanoid(b, f);
        int hz1 = b.HZ1, hy0 = b.Neck, hy1 = b.Crown, eye = hy0 + 3;
        // its ears set back along the sides of its head, where they hold on
        s.Cut(Body, -b.HeadW - 1, eye, -1, -b.HeadW, eye + 2, 1, both: true);
        s.Pair(Body, Body, -b.HeadW - 1, eye, 3, -b.HeadW, eye + 3, 5, Mat.SkinTroll);
        // a loincloth, moss on the shoulders and the crown, warts, a jaw that juts with teeth
        s.Box(Body, -b.Chest, b.Waist - 6, b.Z0 - 1, b.Chest, b.Waist + 1, b.Z1 + 1, Mat.Leather);
        s.Cut(Body, -b.Chest, b.Waist - 6, b.Z0 - 1, -b.Chest + 2, b.Waist - 2, b.Z1 + 1, both: true);
        if (look != 1) s.Coat(Body, -b.Chest, b.Neck - 3, b.Z0, -b.Chest + 5, b.Neck, b.Z1, Mat.Moss);
        s.Coat(Body, b.Chest - 4, b.Neck - 3, b.Z0, b.Chest, b.Neck, b.Z1, Mat.Moss);
        foreach (var (x, y) in new[] { (-5, 14), (3, 20), (-2, 30), (6, 25) }) s.Paint(Body, x, b.Waist + y % b.Torso, b.Z1 - 1, x + 1, b.Waist + y % b.Torso + 1, b.Z1, Mat.Moss);
        s.Box(Body, -4, hy0, hz1, 4, hy0 + 3, hz1 + 3, Mat.SkinTroll);
        s.Pair(Body, Body, -4, hy0 + 3, hz1 + 1, -3, hy0 + 5, hz1 + 2, Mat.Bone);
        s.Pair(Body, Body, -2, hy0 + 3, hz1 + 2, -1, hy0 + 4, hz1 + 3, Mat.Bone);
        s.Box(Body, -2, hy1, 2, 2, hy1 + 1, 6, Mat.Moss);
        TrollDetail(s, b, look, tame: false);
        Club(s, b);
        return Done(s, f, b);
    }

    // ---------------------------------------------------------------- beasts: Main and Off are the front legs

    /// <summary>Four legs at the corners of a body, with hooves (or paws) at the bottom.</summary>
    static Sculpt Quadruped(BodyPlan f, int legH, int legW, int halfX, int frontZ, int backZ, byte fur, byte foot)
    {
        var s = new Sculpt();
        foreach (var (p, x0, z0) in new[] { (Main, -halfX, frontZ), (Off, halfX - legW, frontZ), (LegR, -halfX, backZ), (LegL, halfX - legW, backZ) })
        {
            s.Box(p, x0, 0, z0, x0 + legW, legH, z0 + legW, fur);
            s.Box(p, x0, 0, z0, x0 + legW, 1, z0 + legW, foot);
            Joint(f, p, (x0 + legW / 2f) / H, legH / H, (z0 + legW / 2f) / H);
        }
        f.Beast = true;
        return s;
    }

    static BodyPlan Deer(int look, bool white = false)
    {
        var f = new BodyPlan { Stride = 12 };
        byte fur = white ? Mat.Bone : Mat.FurDeer;
        var s = Quadruped(f, 12, 2, 3, 6, -9, fur, Mat.Hoof);
        // a long barrel of a body with a pale belly and a white rump with a short tail, a neck held high
        s.Box(Body, -3, 12, -10, 3, 20, 9, Mat.FurDeer);
        s.Cut(Body, -3, 12, -10, -2, 13, 9, both: true);
        s.Cut(Body, -3, 19, -10, -2, 20, 9, both: true);
        s.Paint(Body, -2, 12, -9, 2, 13, 8, Mat.Belly);
        s.Paint(Body, -2, 13, -10, 2, 19, -9, Mat.Belly);
        s.Box(Body, -1, 17, -11, 1, 19, -10, Mat.FurDeer);
        s.Box(Body, -2, 18, 6, 2, 24, 10, Mat.FurDeer);
        s.Paint(Body, -1, 18, 9, 1, 22, 10, Mat.Belly);
        // the head, a narrower muzzle with a dark nose, eyes at the sides, ears out
        s.Box(Body, -2, 23, 9, 2, 27, 14, Mat.FurDeer);
        s.Box(Body, -1, 23, 14, 1, 26, 17, Mat.FurDeer);
        s.Box(Body, -1, 24, 17, 1, 26, 18, Mat.Hoof);
        s.Pair(Body, Body, -2, 25, 12, -1, 26, 13, Mat.Eye);
        s.Pair(Body, Body, -4, 26, 10, -2, 27, 11, Mat.FurDeer);
        s.Pair(Body, Body, -5, 27, 10, -3, 28, 11, Mat.FurDeer);
        // a stag's antlers: two beams up from the crown, each with a brow tine forward, one out and one back
        if (look == 0)
            foreach (int m in new[] { 1, -1 })
            {
                int X(int x) => m > 0 ? x : -1 - x;
                void Tine(int x0, int y0, int z0, int x1, int y1, int z1) => s.Box(Body, Math.Min(X(x0), X(x1 - 1)), y0, z0, Math.Max(X(x0), X(x1 - 1)) + 1, y1, z1, Mat.Bone);
                Tine(-2, 27, 10, -1, 36, 11);
                Tine(-2, 29, 11, -1, 30, 13);
                Tine(-4, 32, 10, -2, 33, 11);
                Tine(-2, 34, 8, -1, 35, 10);
                Tine(-3, 35, 10, -2, 37, 11);
            }
        Joint(f, Body, 0, 12 / H, 6 / H);
        f.HeadY0 = 23; f.HeadY1 = 27;
        return s.Into(f, 12, 20, 4);
    }

    /// <summary>A cave bear: a great hump of dark fur over the shoulders, heavy legs, a broad head low on a short neck, small ears, eyes red in the dark.</summary>
    static BodyPlan BearPlan()
    {
        var f = new BodyPlan { Stride = 10 };
        var s = Quadruped(f, 10, 4, 6, 6, -11, Mat.FurBoar, Mat.Hoof);
        s.Box(Body, -6, 10, -13, 6, 24, 8, Mat.FurBoar);
        s.Box(Body, -5, 24, -4, 5, 28, 6, Mat.FurBoar);
        s.Cut(Body, -6, 10, -13, -5, 11, 8, both: true);
        s.Paint(Body, -4, 10, -12, 4, 11, 7, Mat.HairBrown);
        // the head, low and broad, a paler muzzle, the nose, small round ears, eyes that catch the light
        s.Box(Body, -4, 14, 8, 4, 22, 15, Mat.FurBoar);
        s.Box(Body, -2, 14, 15, 2, 18, 19, Mat.HairBrown);
        s.Box(Body, -1, 16, 19, 1, 18, 20, Mat.Hoof);
        s.Pair(Body, Body, -4, 22, 9, -2, 24, 11, Mat.FurBoar);
        s.Pair(Body, Body, -3, 19, 14, -2, 20, 15, Mat.Ember);
        s.Box(Body, -1, 22, -14, 1, 24, -12, Mat.FurBoar);
        Joint(f, Body, 0, 10 / H, 4 / H);
        f.HeadY0 = 14; f.HeadY1 = 24;
        return s.Into(f, 6, 24, 6);
    }

    static BodyPlan Boar()
    {
        var f = new BodyPlan { Stride = 7 };
        var s = Quadruped(f, 6, 2, 4, 4, -6, Mat.FurBoar, Mat.Hoof);
        // a heavy dark body, high at the shoulders, with a bristled ridge; a wedge of a head, a pale snout and tusks
        s.Box(Body, -4, 4, -8, 4, 13, 6, Mat.FurBoar);
        s.Cut(Body, -4, 12, -8, -3, 13, 6, both: true);
        s.Cut(Body, -4, 4, -8, -3, 5, 6, both: true);
        s.Box(Body, -1, 13, -6, 1, 15, 5, Mat.Hair);
        s.Box(Body, -3, 4, 6, 3, 11, 10, Mat.FurBoar);
        s.Box(Body, -2, 4, 10, 2, 8, 13, Mat.FurBoar);
        s.Box(Body, -2, 4, 13, 2, 7, 14, Mat.SkinHuman);
        s.Pair(Body, Body, -3, 6, 11, -2, 9, 12, Mat.Bone);
        s.Pair(Body, Body, -3, 9, 9, -2, 10, 10, Mat.Eye);
        s.Pair(Body, Body, -4, 10, 7, -2, 12, 8, Mat.FurBoar);
        s.Box(Body, -1, 9, -9, 1, 11, -8, Mat.Hair);
        Joint(f, Body, 0, 6 / H, 4 / H);
        f.HeadY0 = 6; f.HeadY1 = 11;
        return s.Into(f, 4, 13, 4);
    }

    static BodyPlan Hare()
    {
        var f = new BodyPlan { Stride = 4 };
        var s = Quadruped(f, 2, 2, 2, 2, -4, Mat.FurHare, Mat.FurHare);
        // a round crouched body, a white scut, long ears tipped dark
        s.Box(Body, -2, 2, -4, 2, 6, 4, Mat.FurHare);
        s.Cut(Body, -2, 5, -4, -1, 6, 4, both: true);
        s.Paint(Body, -2, 2, -3, 2, 3, 3, Mat.Belly);
        s.Box(Body, -1, 4, -5, 1, 6, -4, Mat.Belly);
        s.Box(Body, -2, 4, 3, 2, 8, 7, Mat.FurHare);
        s.Pair(Body, Body, -2, 6, 5, -1, 7, 6, Mat.Eye);
        s.Box(Body, -1, 4, 7, 1, 5, 8, Mat.SkinHuman);
        s.Pair(Body, Body, -2, 8, 3, -1, 13, 4, Mat.FurHare);
        s.Pair(Body, Body, -2, 12, 3, -1, 13, 4, Mat.Hoof);
        Joint(f, Body, 0, 2 / H, 2 / H);
        f.HeadY0 = 4; f.HeadY1 = 8;
        return s.Into(f, 2, 6, 2);
    }
}
