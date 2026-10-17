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

/// <summary>A block of one part's voxels, in half-voxels (6 cm): what each is made of, and what it is.</summary>
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

/// <summary>
/// The shape of one kind of body in one of its looks: the figure as it is drawn, cut in half-voxels, with flesh inside
/// and a skeleton in the flesh — a skull with the brain in it, a spine, ribs round the heart, bones in the limbs thick
/// enough to hide them. Figure coordinates are voxels with the origin between the feet, +y up and +z the way it faces,
/// so its right side (the hand with the tool) is at −x; the grids are in half-voxels of the same.
/// </summary>
public sealed class BodyPlan
{
    public const int Parts = 5, Sub = 2;
    public readonly PartGrid[] Grid = new PartGrid[Parts];
    /// <summary>Where each part turns, in figure voxels: the body at the hips, the arms at the shoulders, the legs at the hips.</summary>
    public readonly float[] JX = new float[Parts], JY = new float[Parts], JZ = new float[Parts];
    /// <summary>Voxels walked in one full stride; seconds per blow when working; how far forward the tool is carried at rest.</summary>
    public float Stride = 9, Chop = .75f, Carry;
    /// <summary>A shield on the off arm; four legs (the arms are front legs).</summary>
    public bool Shield, Beast;
    /// <summary>The head's rows, in figure voxels; the top of the figure.</summary>
    public int HeadY0, HeadY1, Top;
    /// <summary>Half-voxels of brain and of heart it starts with.</summary>
    public int Brain, Heart;
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
        "worker" => 8, "archer" => 6, "sword" => 6, "goblin" => 8, "sapper" => 4, "orc" => 6, "miner" => 4, "troll" => 3, "deer" => 2, _ => 1,
    };

    /// <summary>A look for a unit, from its number, so it keeps it.</summary>
    public static int LookOf(UnitType t, int id) => (int)(Hash.H3(id, 7, 13) * Looks(t)) % Looks(t);

    public static BodyPlan Plan(UnitType t, int look)
    {
        look = Math.Clamp(look, 0, Looks(t) - 1);
        if (Cache.TryGetValue((t.Key, look), out var p)) return p;
        p = t.Key switch
        {
            "worker" => Worker(look),
            "archer" => Archer(look),
            "sword" => Swordsman(look),
            "goblin" => Goblin(look),
            "orc" => Orc(look),
            "miner" => Miner(look),
            "troll" => Troll(look),
            "sapper" => Sapper(look),
            "deer" => Deer(look),
            "boar" => Boar(),
            "hare" => Hare(),
            _ => Worker(0),
        };
        Cache[(t.Key, look)] = p;
        return p;
    }

    // ---------------------------------------------------------------- proportions

    /// <summary>Sizes of a body in voxels; widths are halves, measured from the middle.</summary>
    sealed class Build
    {
        public int Leg = 5, LegW = 2, LegD = 2, Hip = 2;
        public int Torso = 5, Chest = 2, ChestD = 2;
        public int Head = 4, HeadW = 2, HeadD = 4, HeadZ = -2;
        public int Arm = 5, ArmW = 1, ArmD = 2;
        public byte Skin = Mat.SkinHuman, Shirt = Mat.Linen, Trousers = Mat.ClothDark, Boots = Mat.Leather, Belt = Mat.Leather, Hair = Mat.Hair, Eye = Mat.Eye, Sleeve;

        public int Waist => Leg;
        public int Neck => Leg + Torso;
        public int Crown => Leg + Torso + Head;
        public int Z0 => -(ChestD / 2);
        public int Z1 => ChestD - ChestD / 2;
        public int FaceZ => HeadZ + HeadD - 1;
        /// <summary>Right arm column, and the row the hand closes on.</summary>
        public int ArmX0 => -Chest - ArmW;
        public int HandY => Neck - Arm;
        public int ArmZ0 => -(ArmD / 2);
        public int ArmZ1 => ArmD - ArmD / 2;
        /// <summary>The column a one-voxel tool sits in, in the middle of the fist.</summary>
        public int FistX => ArmX0 + ArmW / 2;
    }

    /// <summary>Voxels per part, written in figure coordinates, with what each is.</summary>
    sealed class Sculpt
    {
        readonly Dictionary<(int p, int x, int y, int z), (byte m, Tissue t)> _v = new();

        public void Box(BodyPart p, int x0, int y0, int z0, int x1, int y1, int z1, byte m, Tissue t = Tissue.Skin)
        {
            for (int y = y0; y < y1; y++)
                for (int z = z0; z < z1; z++)
                    for (int x = x0; x < x1; x++) _v[((int)p, x, y, z)] = (m, t);
        }

        /// <summary>A box on the right side (x below zero) and its mirror on the left.</summary>
        public void Pair(BodyPart right, BodyPart left, int x0, int y0, int z0, int x1, int y1, int z1, byte m, Tissue t = Tissue.Skin)
        {
            Box(right, x0, y0, z0, x1, y1, z1, m, t);
            Box(left, -x1, y0, z0, -x0, y1, z1, m, t);
        }

        public void Dot(BodyPart p, int x, int y, int z, byte m, Tissue t = Tissue.Skin) => _v[((int)p, x, y, z)] = (m, t);

        /// <summary>Cuts the figure into half-voxels and fills it in: flesh under the skin, bones, the brain and the heart.</summary>
        public BodyPlan Into(BodyPlan f, int waist, int neck, int chestX)
        {
            for (int p = 0; p < BodyPlan.Parts; p++)
            {
                int x0 = int.MaxValue, y0 = int.MaxValue, z0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue, z1 = int.MinValue;
                foreach (var k in _v.Keys)
                {
                    if (k.p != p) continue;
                    x0 = Math.Min(x0, k.x); y0 = Math.Min(y0, k.y); z0 = Math.Min(z0, k.z);
                    x1 = Math.Max(x1, k.x); y1 = Math.Max(y1, k.y); z1 = Math.Max(z1, k.z);
                }
                if (x0 == int.MaxValue) continue;
                const int S = BodyPlan.Sub;
                var g = new PartGrid { X0 = x0 * S, Y0 = y0 * S, Z0 = z0 * S, SX = (x1 - x0 + 1) * S, SY = (y1 - y0 + 1) * S, SZ = (z1 - z0 + 1) * S };
                g.M = new byte[g.SX * g.SY * g.SZ];
                g.T = new byte[g.M.Length];
                foreach (var (k, v) in _v)
                {
                    if (k.p != p) continue;
                    for (int dy = 0; dy < S; dy++)
                        for (int dz = 0; dz < S; dz++)
                            for (int dx = 0; dx < S; dx++)
                            {
                                int i = g.Index(k.x * S + dx, k.y * S + dy, k.z * S + dz);
                                g.M[i] = v.m; g.T[i] = (byte)v.t;
                                g.Count++;
                            }
                }
                Inside(g, (BodyPart)p, f, waist, neck, chestX);
                f.Grid[p] = g;
                f.Top = Math.Max(f.Top, y1 + 1);
            }
            return f;
        }

        /// <summary>How deep each voxel lies under the surface (1 for the surface), counting worn things as cover.</summary>
        static int[] Depth(PartGrid g)
        {
            var d = new int[g.M.Length];
            var q = new Queue<int>();
            for (int y = 0; y < g.SY; y++)
                for (int z = 0; z < g.SZ; z++)
                    for (int x = 0; x < g.SX; x++)
                    {
                        int i = (y * g.SZ + z) * g.SX + x;
                        if (g.M[i] == 0) continue;
                        bool edge = x == 0 || y == 0 || z == 0 || x == g.SX - 1 || y == g.SY - 1 || z == g.SZ - 1
                            || g.M[i - 1] == 0 || g.M[i + 1] == 0 || g.M[i - g.SX] == 0 || g.M[i + g.SX] == 0 || g.M[i - g.SX * g.SZ] == 0 || g.M[i + g.SX * g.SZ] == 0;
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

        static void Inside(PartGrid g, BodyPart p, BodyPlan f, int waist, int neck, int chestX)
        {
            var depth = Depth(g);
            const int S = BodyPlan.Sub;
            int headY0 = f.HeadY0 * S, headY1 = f.HeadY1 * S, w = waist * S, n = neck * S, chestY = w + (n - w) * 2 / 5;
            int back = int.MaxValue;
            for (int i = 0; i < g.M.Length; i++)
            {
                // under the skin (or the mail) is the body; what is carried stays what it is
                if (g.T[i] is not ((byte)Tissue.Skin or (byte)Tissue.Armor) || depth[i] < 2) continue;
                int x = i % g.SX + g.X0, z = i / g.SX % g.SZ + g.Z0, y = i / (g.SX * g.SZ) + g.Y0;
                Tissue t;
                if (p == BodyPart.Body && y >= headY0 && y < headY1) t = depth[i] == 2 ? Tissue.Bone : Tissue.Brain;
                else if (p == BodyPart.Body && !f.Beast && y >= w && y < n)
                {
                    t = Tissue.Flesh;
                    // ribs round the chest every other row, the heart a little left of the middle
                    if (y >= chestY && (y & 1) == 0 && Math.Abs(x + .5) >= 1.5) t = Tissue.Bone;
                    if (y >= chestY + 2 && y < chestY + 5 && x >= 0 && x <= chestX * S / 2) t = Tissue.Heart;
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

    /// <summary>Legs with boots, a torso with a belt, a big head with eyes and hair, two arms with hands.</summary>
    static Sculpt Humanoid(Build b, BodyPlan f)
    {
        var s = new Sculpt();
        int lz0 = -(b.LegD / 2), lz1 = b.LegD - b.LegD / 2;
        s.Pair(BodyPart.LegR, BodyPart.LegL, -b.Hip, 0, lz0, -b.Hip + b.LegW, b.Leg, lz1, b.Trousers);
        int boot = Math.Max(1, b.Leg / 4);
        s.Pair(BodyPart.LegR, BodyPart.LegL, -b.Hip, 0, lz0, -b.Hip + b.LegW, boot, lz1 + (b.Leg >= 6 ? 1 : 0), b.Boots);

        s.Box(BodyPart.Body, -b.Chest, b.Waist, b.Z0, b.Chest, b.Neck, b.Z1, b.Shirt);
        s.Box(BodyPart.Body, -b.Chest, b.Waist, b.Z0, b.Chest, b.Waist + 1, b.Z1, b.Belt);

        s.Box(BodyPart.Body, -b.HeadW, b.Neck, b.HeadZ, b.HeadW, b.Crown, b.HeadZ + b.HeadD, b.Skin);
        int eyeY = b.Neck + b.Head / 2, inset = b.HeadW >= 3 ? 1 : 0;
        s.Dot(BodyPart.Body, -b.HeadW + inset, eyeY, b.FaceZ, b.Eye);
        s.Dot(BodyPart.Body, b.HeadW - 1 - inset, eyeY, b.FaceZ, b.Eye);
        s.Box(BodyPart.Body, -b.HeadW, b.Crown - 1, b.HeadZ, b.HeadW, b.Crown, b.HeadZ + b.HeadD, b.Hair);
        s.Box(BodyPart.Body, -b.HeadW, b.Neck + 1, b.HeadZ, b.HeadW, b.Crown, b.HeadZ + 1, b.Hair);

        byte sleeve = b.Sleeve != 0 ? b.Sleeve : b.Shirt;
        int hand = b.Arm >= 8 ? 2 : 1;
        s.Pair(BodyPart.Main, BodyPart.Off, b.ArmX0, b.HandY + hand, b.ArmZ0, -b.Chest, b.Neck, b.ArmZ1, sleeve);
        s.Pair(BodyPart.Main, BodyPart.Off, b.ArmX0, b.HandY, b.ArmZ0, -b.Chest, b.HandY + hand, b.ArmZ1, b.Skin);

        float legX = -b.Hip + b.LegW / 2f, legZ = (lz0 + lz1) / 2f, armX = -b.Chest - b.ArmW / 2f;
        Joint(f, BodyPart.Body, 0, b.Waist, 0);
        Joint(f, BodyPart.LegR, legX, b.Leg, legZ);
        Joint(f, BodyPart.LegL, -legX, b.Leg, legZ);
        Joint(f, BodyPart.Main, armX, b.Neck - .5f, (b.ArmZ0 + b.ArmZ1) / 2f);
        Joint(f, BodyPart.Off, -armX, b.Neck - .5f, (b.ArmZ0 + b.ArmZ1) / 2f);
        f.HeadY0 = b.Neck; f.HeadY1 = b.Crown;
        return s;
    }

    static void Joint(BodyPlan f, BodyPart p, float x, float y, float z) { f.JX[(int)p] = x; f.JY[(int)p] = y; f.JZ[(int)p] = z; }

    static BodyPlan Done(Sculpt s, BodyPlan f, Build b) => s.Into(f, b.Waist, b.Neck, b.Chest);

    // ---------------------------------------------------------------- what they carry (held forward from the right fist)

    static void Pick(Sculpt s, Build b, byte head, int len = 6)
    {
        int x = b.FistX, y = b.HandY, z0 = b.ArmZ1;
        s.Box(BodyPart.Main, x, y, z0, x + 1, y + 1, z0 + len, Mat.Handle, Tissue.Gear);
        s.Box(BodyPart.Main, x, y - 2, z0 + len - 1, x + 1, y + 3, z0 + len, head, Tissue.Gear);
    }

    static void Sword(Sculpt s, Build b)
    {
        int x = b.FistX, y = b.HandY, z0 = b.ArmZ1;
        s.Box(BodyPart.Main, x - 1, y, z0, x + 2, y + 1, z0 + 1, Mat.Brass, Tissue.Gear);
        s.Box(BodyPart.Main, x, y, z0 + 1, x + 1, y + 1, z0 + 8, Mat.Steel, Tissue.Gear);
    }

    static void Axe(Sculpt s, Build b)
    {
        int x = b.FistX, y = b.HandY, z0 = b.ArmZ1;
        s.Box(BodyPart.Main, x, y, z0, x + 1, y + 1, z0 + 7, Mat.Handle, Tissue.Gear);
        s.Box(BodyPart.Main, x, y - 3, z0 + 4, x + 1, y, z0 + 7, Mat.Steel, Tissue.Gear);
    }

    static void Knife(Sculpt s, Build b)
    {
        int x = b.FistX, y = b.HandY, z0 = b.ArmZ1;
        s.Box(BodyPart.Main, x, y, z0, x + 1, y + 1, z0 + 1, Mat.Leather, Tissue.Gear);
        s.Box(BodyPart.Main, x, y, z0 + 1, x + 1, y + 1, z0 + 3, Mat.Steel, Tissue.Gear);
    }

    static void Mallet(Sculpt s, Build b)
    {
        int x = b.FistX, y = b.HandY, z0 = b.ArmZ1;
        s.Box(BodyPart.Main, x, y, z0, x + 1, y + 1, z0 + 3, Mat.Handle, Tissue.Gear);
        s.Box(BodyPart.Main, x, y - 1, z0 + 3, x + 1, y + 2, z0 + 5, Mat.Wood, Tissue.Gear);
    }

    static void Club(Sculpt s, Build b)
    {
        int x = b.FistX, y = b.HandY, z0 = b.ArmZ1;
        s.Box(BodyPart.Main, x, y, z0, x + 1, y + 2, z0 + 7, Mat.Trunk, Tissue.Gear);
        s.Box(BodyPart.Main, x - 1, y - 1, z0 + 6, x + 2, y + 3, z0 + 11, Mat.Trunk, Tissue.Gear);
        foreach (var (dx, dy, dz) in new[] { (-2, 1, 8), (2, 0, 9), (0, -2, 8), (0, 3, 9), (-2, 0, 10) })
            s.Dot(BodyPart.Main, x + dx, y + dy, z0 + dz, Mat.Bone, Tissue.Gear);
    }

    /// <summary>A bow in the left fist: its limbs run along z and bend back toward the archer at the tips.</summary>
    static void Bow(Sculpt s, Build b)
    {
        int x = -b.FistX - 1, y = b.HandY;
        for (int z = -4; z <= 4; z++) s.Dot(BodyPart.Off, x, y + (Math.Abs(z) >= 3 ? 1 : 0), z, Mat.Handle, Tissue.Gear);
        s.Dot(BodyPart.Off, x, y, 0, Mat.Leather, Tissue.Gear);
    }

    // ---------------------------------------------------------------- people

    static readonly byte[] HumanSkins = { Mat.SkinHuman, Mat.SkinTan, Mat.SkinPale };
    static readonly byte[] Hairs = { Mat.Hair, Mat.HairBrown, Mat.HairGinger, Mat.HairGrey };

    /// <summary>A beard under the face, in the hair's colour.</summary>
    static void Beard(Sculpt s, Build b, byte hair)
    {
        s.Box(BodyPart.Body, -1, b.Neck, b.FaceZ, 1, b.Neck + 2, b.FaceZ + 1, hair);
        s.Box(BodyPart.Body, -b.HeadW, b.Neck, b.FaceZ - 1, -b.HeadW + 1, b.Neck + 2, b.FaceZ, hair);
        s.Box(BodyPart.Body, b.HeadW - 1, b.Neck, b.FaceZ - 1, b.HeadW, b.Neck + 2, b.FaceZ, hair);
    }

    static BodyPlan Worker(int look)
    {
        var f = new BodyPlan { Chop = .75f };
        byte hair = Hairs[(look + look / 4) % 4];
        var b = new Build
        {
            Skin = HumanSkins[look % 3 == 2 ? 1 : look % 5 == 4 ? 2 : 0], Hair = hair,
            Shirt = new[] { Mat.Linen, Mat.ClothBrown, Mat.Linen, Mat.ClothGreen }[look % 4], Trousers = look % 3 == 1 ? Mat.ClothBrown : Mat.ClothDark,
            Leg = look == 5 ? 6 : 5,
        };
        var s = Humanoid(b, f);
        // a leather apron, and a felt cap with a brim, a straw hat, a hood or nothing on the head
        s.Box(BodyPart.Body, -1, b.Waist - 1, b.Z1, 1, b.Waist + 3, b.Z1 + 1, Mat.Leather);
        switch (look % 4)
        {
            case 0:
                s.Box(BodyPart.Body, -b.HeadW, b.Crown, b.HeadZ, b.HeadW, b.Crown + 1, b.HeadZ + b.HeadD, Mat.Leather);
                s.Box(BodyPart.Body, -b.HeadW, b.Crown - 1, b.FaceZ + 1, b.HeadW, b.Crown, b.FaceZ + 2, Mat.Leather);
                break;
            case 1:
                s.Box(BodyPart.Body, -b.HeadW - 1, b.Crown, b.HeadZ - 1, b.HeadW + 1, b.Crown + 1, b.HeadZ + b.HeadD + 1, Mat.Straw);
                s.Box(BodyPart.Body, -b.HeadW + 1, b.Crown + 1, b.HeadZ + 1, b.HeadW - 1, b.Crown + 2, b.HeadZ + b.HeadD - 1, Mat.Straw);
                break;
            case 3:
                s.Box(BodyPart.Body, -b.HeadW, b.Neck, b.HeadZ, b.HeadW, b.Crown + 1, b.HeadZ + 1, Mat.ClothBrown);
                s.Box(BodyPart.Body, -b.HeadW, b.Crown, b.HeadZ, b.HeadW, b.Crown + 1, b.FaceZ + 1, Mat.ClothBrown);
                break;
        }
        if (look % 3 == 1 || look == 6) Beard(s, b, hair);
        Pick(s, b, Mat.Iron);
        return Done(s, f, b);
    }

    static BodyPlan Archer(int look)
    {
        var f = new BodyPlan { Chop = .9f };
        byte hood = new[] { Mat.ClothDark, Mat.ClothGreen, Mat.ClothBrown }[look % 3];
        var b = new Build { Shirt = Mat.ClothBlue, Trousers = Mat.ClothDark, Sleeve = Mat.Leather, Skin = HumanSkins[look / 3 % 2 == 1 ? 1 : 0], Hair = Hairs[look % 4] };
        var s = Humanoid(b, f);
        // a hood over the head and down the back, a quiver with fletchings over the shoulder
        s.Box(BodyPart.Body, -b.HeadW, b.Neck, b.HeadZ, b.HeadW, b.Crown + 1, b.HeadZ + 1, hood);
        s.Box(BodyPart.Body, -b.HeadW, b.Crown, b.HeadZ, b.HeadW, b.Crown + 1, b.FaceZ + 1, hood);
        s.Box(BodyPart.Body, -b.Chest, b.Neck - 1, b.Z0, b.Chest, b.Neck, b.Z1, hood);
        s.Box(BodyPart.Body, 0, b.Waist + 1, b.Z0 - 1, 1, b.Neck + 1, b.Z0, Mat.Leather, Tissue.Gear);
        s.Dot(BodyPart.Body, 0, b.Neck + 1, b.Z0 - 1, Mat.Linen, Tissue.Gear);
        if (look == 4) Beard(s, b, b.Hair);
        Bow(s, b);
        return Done(s, f, b);
    }

    static BodyPlan Swordsman(int look)
    {
        var f = new BodyPlan { Chop = .8f, Carry = -.35f, Shield = true };
        var b = new Build { Shirt = Mat.Mail, Trousers = Mat.ClothDark, Sleeve = Mat.Mail, Skin = HumanSkins[look % 2], Hair = Hairs[(look + 1) % 4] };
        var s = Humanoid(b, f);
        // mail on the body and the arms
        s.Box(BodyPart.Body, -b.Chest, b.Waist + 1, b.Z0, b.Chest, b.Neck, b.Z1, Mat.Mail, Tissue.Armor);
        s.Pair(BodyPart.Main, BodyPart.Off, b.ArmX0, b.HandY + 1, b.ArmZ0, -b.Chest, b.Neck, b.ArmZ1, Mat.Mail, Tissue.Armor);
        // a blue tabard over the mail, steel on the shoulders, and a steel cap, a nasal helm, or a bare head and a beard
        s.Box(BodyPart.Body, -1, b.Waist - 1, b.Z1, 1, b.Neck - 1, b.Z1 + 1, Mat.ClothBlue);
        s.Pair(BodyPart.Main, BodyPart.Off, b.ArmX0, b.Neck - 1, b.ArmZ0, -b.Chest, b.Neck, b.ArmZ1, Mat.Steel, Tissue.Armor);
        switch (look % 3)
        {
            case 0:
                s.Box(BodyPart.Body, -b.HeadW, b.Crown - 1, b.HeadZ, b.HeadW, b.Crown + 1, b.HeadZ + b.HeadD, Mat.Steel, Tissue.Armor);
                break;
            case 1:
                s.Box(BodyPart.Body, -b.HeadW, b.Crown - 1, b.HeadZ, b.HeadW, b.Crown + 1, b.HeadZ + b.HeadD, Mat.Steel, Tissue.Armor);
                s.Box(BodyPart.Body, -b.HeadW, b.Neck + 1, b.HeadZ, b.HeadW, b.Crown, b.HeadZ + 1, Mat.Steel, Tissue.Armor);
                s.Dot(BodyPart.Body, 0, b.Neck + 2, b.FaceZ + 1, Mat.Steel, Tissue.Armor);
                s.Dot(BodyPart.Body, 0, b.Neck + 3, b.FaceZ + 1, Mat.Steel, Tissue.Armor);
                break;
            default:
                Beard(s, b, b.Hair);
                break;
        }
        Sword(s, b);
        // a shield before the left forearm, a brass boss in the middle
        int sx = b.Chest - 2, sy = b.HandY, sz = b.ArmZ1;
        s.Box(BodyPart.Off, sx, sy, sz, sx + 4, sy + 4, sz + 1, Mat.ClothBlue, Tissue.Armor);
        s.Box(BodyPart.Off, sx + 1, sy + 1, sz + 1, sx + 3, sy + 3, sz + 2, Mat.Brass, Tissue.Armor);
        return Done(s, f, b);
    }

    // ---------------------------------------------------------------- the horde

    static readonly byte[] GoblinSkins = { Mat.SkinGoblin, Mat.SkinGoblinDark, Mat.SkinGoblinPale };
    static readonly byte[] Rags = { Mat.Rag, Mat.ClothBrown, Mat.ClothOchre, Mat.Rag };

    /// <summary>A goblin's build: bandy legs, a narrow body and a big head.</summary>
    static Build GoblinBuild(byte shirt, byte trousers, byte skin) => new()
    {
        Leg = 3, LegW = 1, LegD = 1, Hip = 2, Torso = 3, Chest = 2, ChestD = 2, Head = 4, HeadW = 2, HeadD = 4, HeadZ = -2,
        Arm = 3, ArmW = 1, ArmD = 1, Skin = skin, Shirt = shirt, Trousers = trousers, Boots = skin, Hair = skin, Eye = Mat.Brass, Sleeve = skin,
    };

    static void GoblinFace(Sculpt s, Build b, int ears = 2)
    {
        // long ears out to the sides (or one torn short), a long nose
        s.Box(BodyPart.Body, -b.HeadW - ears, b.Neck + 2, -1, -b.HeadW, b.Neck + 3, 0, b.Skin);
        s.Box(BodyPart.Body, b.HeadW, b.Neck + 2, -1, b.HeadW + 2, b.Neck + 3, 0, b.Skin);
        s.Box(BodyPart.Body, -1, b.Neck + 1, b.FaceZ + 1, 1, b.Neck + 2, b.FaceZ + 2, b.Skin);
    }

    static BodyPlan Goblin(int look)
    {
        var f = new BodyPlan { Chop = .45f, Carry = -.3f, Stride = 6 };
        var b = GoblinBuild(Rags[look % 4], look % 3 == 2 ? Mat.Rag : Mat.Leather, GoblinSkins[look % 3]);
        var s = Humanoid(b, f);
        GoblinFace(s, b, look % 4 == 3 ? 1 : 2);
        if (look % 2 == 0) s.Dot(BodyPart.Body, 0, b.Crown, 0, Mat.Hair);
        // a bone through the hair of some
        if (look == 5) s.Box(BodyPart.Body, -2, b.Crown, 0, 2, b.Crown + 1, 1, Mat.Bone);
        Knife(s, b);
        return Done(s, f, b);
    }

    static BodyPlan Sapper(int look)
    {
        var f = new BodyPlan { Chop = .55f, Stride = 6 };
        var b = GoblinBuild(Mat.Leather, Mat.Rag, GoblinSkins[look % 3]);
        var s = Humanoid(b, f);
        GoblinFace(s, b);
        // a cap, a length of ladder on the back, a mallet in the fist
        byte cap = new[] { Mat.Leather, Mat.ClothBrown, Mat.ClothGreen, Mat.ClothOchre }[look % 4];
        s.Box(BodyPart.Body, -b.HeadW, b.Crown, b.HeadZ, b.HeadW, b.Crown + 1, b.HeadZ + b.HeadD, cap);
        s.Box(BodyPart.Body, -2, b.Waist, b.Z0 - 1, -1, b.Crown + 2, b.Z0, Mat.Handle, Tissue.Gear);
        s.Box(BodyPart.Body, 1, b.Waist, b.Z0 - 1, 2, b.Crown + 2, b.Z0, Mat.Handle, Tissue.Gear);
        foreach (int y in new[] { b.Waist + 1, b.Neck, b.Crown }) s.Box(BodyPart.Body, -1, y, b.Z0 - 1, 1, y + 1, b.Z0, Mat.Handle, Tissue.Gear);
        Mallet(s, b);
        return Done(s, f, b);
    }

    static BodyPlan Miner(int look)
    {
        var f = new BodyPlan { Chop = .8f, Stride = 6 };
        var b = new Build
        {
            Leg = 3, LegW = 2, LegD = 2, Hip = 2, Torso = 4, Chest = 2, ChestD = 2, Head = 4, HeadW = 2, HeadD = 4, HeadZ = -2,
            Arm = 4, ArmW = 1, ArmD = 2, Skin = look % 2 == 1 ? Mat.SkinOrcGrey : Mat.SkinOrc, Shirt = Mat.Leather, Trousers = Mat.Rag, Boots = Mat.Iron,
            Eye = Mat.Brass, Sleeve = Mat.Rag,
        };
        var s = Humanoid(b, f);
        // a brass helmet with a candle burning on its brow, a beard, a sack of ore on the back
        s.Box(BodyPart.Body, -b.HeadW, b.Crown - 1, b.HeadZ, b.HeadW, b.Crown + 1, b.HeadZ + b.HeadD, Mat.Brass, Tissue.Armor);
        s.Dot(BodyPart.Body, -1, b.Crown, b.FaceZ, Mat.Ember, Tissue.Gear);
        s.Box(BodyPart.Body, -1, b.Neck, b.FaceZ, 1, b.Neck + 2, b.FaceZ + 1, new[] { Mat.Hair, Mat.HairGinger, Mat.HairGrey, Mat.HairBrown }[look % 4]);
        s.Box(BodyPart.Body, -2, b.Waist, b.Z0 - 2, 2, b.Neck, b.Z0, Mat.Linen, Tissue.Gear);
        Pick(s, b, Mat.Iron, 5);
        return Done(s, f, b);
    }

    static BodyPlan Orc(int look)
    {
        var f = new BodyPlan { Chop = .95f, Carry = -.3f, Stride = 10 };
        var b = new Build
        {
            Leg = 6, LegW = 2, LegD = 2, Hip = 3, Torso = 6, Chest = 3, ChestD = 4, Head = 4, HeadW = 2, HeadD = 4, HeadZ = -1,
            Arm = 6, ArmW = 2, ArmD = 2, Skin = look % 2 == 1 ? Mat.SkinOrcGrey : Mat.SkinOrc, Shirt = Mat.Leather, Trousers = Mat.ClothDark, Boots = Mat.Leather,
            Eye = Mat.ClothRed, Sleeve = look % 2 == 1 ? Mat.SkinOrcGrey : Mat.SkinOrc,
        };
        var s = Humanoid(b, f);
        // a sash across the chest, iron on the shoulders, tusks, and a topknot or an iron cap
        byte sash = look % 3 == 2 ? Mat.ClothOchre : Mat.ClothRed;
        for (int y = b.Waist + 1; y < b.Neck; y++)
        {
            int x = -b.Chest + (y - b.Waist - 1);
            s.Box(BodyPart.Body, Math.Clamp(x, -b.Chest, b.Chest - 2), y, b.Z1 - 1, Math.Clamp(x + 2, -b.Chest + 2, b.Chest), y + 1, b.Z1, sash);
        }
        s.Pair(BodyPart.Main, BodyPart.Off, b.ArmX0, b.Neck - 2, b.ArmZ0, -b.Chest, b.Neck, b.ArmZ1, Mat.Iron, Tissue.Armor);
        s.Dot(BodyPart.Body, -2, b.Neck + 1, b.FaceZ + 1, Mat.Bone);
        s.Dot(BodyPart.Body, 1, b.Neck + 1, b.FaceZ + 1, Mat.Bone);
        if (look >= 3)
        {
            s.Box(BodyPart.Body, -b.HeadW, b.Crown - 1, b.HeadZ, b.HeadW, b.Crown + 1, b.HeadZ + b.HeadD, Mat.Iron, Tissue.Armor);
            s.Dot(BodyPart.Body, -b.HeadW - 1, b.Crown, b.HeadZ + 1, Mat.Bone, Tissue.Armor);
            s.Dot(BodyPart.Body, b.HeadW, b.Crown, b.HeadZ + 1, Mat.Bone, Tissue.Armor);
        }
        else s.Box(BodyPart.Body, -1, b.Crown, 0, 1, b.Crown + 1, 1, Mat.Hair);
        Axe(s, b);
        return Done(s, f, b);
    }

    static BodyPlan Troll(int look)
    {
        var f = new BodyPlan { Chop = 1.4f, Carry = -.2f, Stride = 14 };
        var b = new Build
        {
            Leg = 8, LegW = 3, LegD = 3, Hip = 4, Torso = 10, Chest = 4, ChestD = 5, Head = 5, HeadW = 3, HeadD = 5, HeadZ = 0,
            Arm = 12, ArmW = 3, ArmD = 3, Skin = Mat.SkinTroll, Shirt = Mat.SkinTroll, Trousers = Mat.SkinTroll, Boots = Mat.SkinTroll,
            Belt = Mat.Leather, Hair = look == 2 ? Mat.SkinTroll : Mat.Moss, Eye = Mat.Ember,
        };
        var s = Humanoid(b, f);
        // a loincloth, moss on the shoulders and the crown, a jaw that juts with teeth
        s.Box(BodyPart.Body, -b.Chest, b.Waist - 3, b.Z0 - 1, b.Chest, b.Waist + 1, b.Z1 + 1, Mat.Leather);
        if (look != 1) s.Box(BodyPart.Body, -b.Chest, b.Neck - 1, b.Z0, -b.Chest + 3, b.Neck + 1, b.Z1, Mat.Moss);
        s.Box(BodyPart.Body, b.Chest - 2, b.Neck - 1, b.Z0, b.Chest, b.Neck + 1, b.Z1 - 1, Mat.Moss);
        s.Box(BodyPart.Body, -2, b.Neck, b.FaceZ, 2, b.Neck + 2, b.FaceZ + 2, Mat.SkinTroll);
        s.Dot(BodyPart.Body, -2, b.Neck + 2, b.FaceZ + 1, Mat.Bone);
        s.Dot(BodyPart.Body, 1, b.Neck + 2, b.FaceZ + 1, Mat.Bone);
        s.Box(BodyPart.Body, -1, b.Crown, 1, 1, b.Crown + 1, 3, Mat.Moss);
        Club(s, b);
        return Done(s, f, b);
    }

    // ---------------------------------------------------------------- beasts: Main and Off are the front legs

    /// <summary>Four legs at the corners of a body, with hooves (or paws) at the bottom.</summary>
    static Sculpt Quadruped(BodyPlan f, int legH, int legW, int halfX, int frontZ, int backZ, byte fur, byte foot)
    {
        var s = new Sculpt();
        foreach (var (p, x0, z0) in new[] { (BodyPart.Main, -halfX, frontZ), (BodyPart.Off, halfX - legW, frontZ), (BodyPart.LegR, -halfX, backZ), (BodyPart.LegL, halfX - legW, backZ) })
        {
            s.Box(p, x0, 0, z0, x0 + legW, legH, z0 + legW, fur);
            s.Box(p, x0, 0, z0, x0 + legW, 1, z0 + legW, foot);
            Joint(f, p, x0 + legW / 2f, legH, z0 + legW / 2f);
        }
        f.Beast = true;
        return s;
    }

    static BodyPlan Deer(int look)
    {
        var f = new BodyPlan { Stride = 12 };
        var s = Quadruped(f, 6, 1, 2, 3, -4, Mat.FurDeer, Mat.Hoof);
        // a long body with a pale belly and a white rump, a neck held high, ears and (a stag's) branching antlers
        s.Box(BodyPart.Body, -2, 6, -5, 2, 10, 5, Mat.FurDeer);
        s.Box(BodyPart.Body, -1, 6, -4, 1, 7, 4, Mat.Belly);
        s.Box(BodyPart.Body, -1, 8, -6, 1, 10, -5, Mat.Belly);
        s.Box(BodyPart.Body, -1, 9, 3, 1, 12, 6, Mat.FurDeer);
        s.Box(BodyPart.Body, -1, 11, 5, 1, 13, 9, Mat.FurDeer);
        s.Box(BodyPart.Body, -1, 11, 9, 1, 12, 10, Mat.Hoof);
        s.Dot(BodyPart.Body, -1, 12, 7, Mat.Eye); s.Dot(BodyPart.Body, 0, 12, 7, Mat.Eye);
        s.Pair(BodyPart.Body, BodyPart.Body, -2, 12, 6, -1, 13, 7, Mat.FurDeer);
        if (look == 0)
            foreach (var (bx, tx) in new[] { (-1, -2), (0, 1) })
            {
                s.Box(BodyPart.Body, bx, 13, 6, bx + 1, 17, 7, Mat.Bone);
                s.Box(BodyPart.Body, tx, 14, 6, tx + 1, 16, 7, Mat.Bone);
                s.Dot(BodyPart.Body, bx, 16, 5, Mat.Bone);
            }
        Joint(f, BodyPart.Body, 0, 6, 3);
        f.HeadY0 = 11; f.HeadY1 = 13;
        return s.Into(f, 6, 10, 2);
    }

    static BodyPlan Boar()
    {
        var f = new BodyPlan { Stride = 7 };
        var s = Quadruped(f, 3, 1, 2, 2, -3, Mat.FurBoar, Mat.Hoof);
        // a heavy dark body with a bristled ridge, a wedge of a head, a pale snout and tusks
        s.Box(BodyPart.Body, -2, 2, -4, 2, 7, 3, Mat.FurBoar);
        s.Box(BodyPart.Body, -1, 7, -3, 1, 8, 2, Mat.Hair);
        s.Box(BodyPart.Body, -2, 2, 3, 2, 6, 6, Mat.FurBoar);
        s.Box(BodyPart.Body, -1, 2, 6, 1, 4, 7, Mat.SkinHuman);
        s.Dot(BodyPart.Body, -2, 4, 6, Mat.Bone); s.Dot(BodyPart.Body, 1, 4, 6, Mat.Bone);
        s.Dot(BodyPart.Body, -2, 5, 5, Mat.Eye); s.Dot(BodyPart.Body, 1, 5, 5, Mat.Eye);
        s.Pair(BodyPart.Body, BodyPart.Body, -2, 6, 4, -1, 7, 5, Mat.FurBoar);
        s.Dot(BodyPart.Body, 0, 5, -5, Mat.Hair);
        Joint(f, BodyPart.Body, 0, 3, 2);
        f.HeadY0 = 3; f.HeadY1 = 6;
        return s.Into(f, 2, 7, 2);
    }

    static BodyPlan Hare()
    {
        var f = new BodyPlan { Stride = 4 };
        var s = Quadruped(f, 1, 1, 1, 1, -2, Mat.FurHare, Mat.FurHare);
        // a round crouched body, a white scut, long ears tipped dark
        s.Box(BodyPart.Body, -1, 1, -2, 1, 3, 2, Mat.FurHare);
        s.Box(BodyPart.Body, -1, 1, -2, 1, 2, 1, Mat.Belly);
        s.Box(BodyPart.Body, -1, 2, -3, 1, 3, -2, Mat.Belly);
        s.Box(BodyPart.Body, -1, 2, 2, 1, 4, 4, Mat.FurHare);
        s.Dot(BodyPart.Body, -1, 3, 3, Mat.Eye); s.Dot(BodyPart.Body, 0, 3, 3, Mat.Eye);
        s.Box(BodyPart.Body, -1, 4, 2, 0, 6, 3, Mat.FurHare); s.Dot(BodyPart.Body, -1, 5, 2, Mat.Hoof);
        s.Box(BodyPart.Body, 0, 4, 2, 1, 6, 3, Mat.FurHare); s.Dot(BodyPart.Body, 0, 5, 2, Mat.Hoof);
        Joint(f, BodyPart.Body, 0, 1, 1);
        f.HeadY0 = 2; f.HeadY1 = 4;
        return s.Into(f, 1, 3, 1);
    }
}
