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
/// The shape of one kind of body in one of its looks: the figure as it is drawn, sculpted in half-voxels, with flesh
/// inside and a skeleton in the flesh — a skull with the brain in it, a spine, ribs round the heart, bones in the limbs
/// thick enough to hide them. Figure coordinates have the origin between the feet, +y up and +z the way it faces, so its
/// right side (the hand with the tool) is at −x; the grids are in half-voxels, the joints in voxels.
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
    /// <summary>The head's rows, in half-voxels; the top of the figure, in voxels.</summary>
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

    /// <summary>Half-voxels per part, written in figure coordinates, with what each is.</summary>
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

        /// <summary>Takes out what is in a box (and in its mirror, when <paramref name="both"/>).</summary>
        public void Cut(BodyPart p, int x0, int y0, int z0, int x1, int y1, int z1, bool both = false)
        {
            for (int y = y0; y < y1; y++)
                for (int z = z0; z < z1; z++)
                    for (int x = x0; x < x1; x++)
                    {
                        _v.Remove(((int)p, x, y, z));
                        if (both) _v.Remove(((int)p, -1 - x, y, z));
                    }
        }

        /// <summary>Recolours what is already there in a box; its tissue stays unless another is given.</summary>
        public void Paint(BodyPart p, int x0, int y0, int z0, int x1, int y1, int z1, byte m, Tissue? t = null)
        {
            for (int y = y0; y < y1; y++)
                for (int z = z0; z < z1; z++)
                    for (int x = x0; x < x1; x++)
                        if (_v.TryGetValue(((int)p, x, y, z), out var v)) _v[((int)p, x, y, z)] = (m, t ?? v.t);
        }

        bool Has(BodyPart p, int x, int y, int z) => _v.ContainsKey(((int)p, x, y, z));

        /// <summary>
        /// Recolours only what shows inside a box — voxels with air beside them — as a hood or a helmet covers a head;
        /// what lies in <paramref name="but"/> (a face) is left as it is.
        /// </summary>
        public void Coat(BodyPart p, int x0, int y0, int z0, int x1, int y1, int z1, byte m, Tissue? t = null,
            (int x0, int y0, int z0, int x1, int y1, int z1)? but = null)
        {
            var shows = new List<(int, int, int)>();
            for (int y = y0; y < y1; y++)
                for (int z = z0; z < z1; z++)
                    for (int x = x0; x < x1; x++)
                    {
                        if (!Has(p, x, y, z)) continue;
                        if (but is { } e && x >= e.x0 && x < e.x1 && y >= e.y0 && y < e.y1 && z >= e.z0 && z < e.z1) continue;
                        if (!Has(p, x + 1, y, z) || !Has(p, x - 1, y, z) || !Has(p, x, y + 1, z) || !Has(p, x, y - 1, z) || !Has(p, x, y, z + 1) || !Has(p, x, y, z - 1))
                            shows.Add((x, y, z));
                    }
            foreach (var (x, y, z) in shows) _v[((int)p, x, y, z)] = (m, t ?? _v[((int)p, x, y, z)].t);
        }

        /// <summary>
        /// A disc of one voxel's thickness lying flat at row y (a hat's brim, the crown of a cap), round the middle of the
        /// figure, or standing in the x–y plane at depth z (a shield) round (cx, cy).
        /// </summary>
        public void Flat(BodyPart p, int y, float r, byte m, Tissue t = Tissue.Skin, float cz = 0, float cx = 0)
        {
            int n = (int)Math.Ceiling(r) + 1;
            for (int z = (int)cz - n; z <= cz + n; z++)
                for (int x = (int)cx - n; x <= cx + n; x++)
                    if ((x + .5f - cx) * (x + .5f - cx) + (z + .5f - cz) * (z + .5f - cz) <= r * r) _v[((int)p, x, y, z)] = (m, t);
        }

        public void Disc(BodyPart p, int z, float cx, float cy, float r, Func<int, int, float, byte> m, Tissue t)
        {
            int n = (int)Math.Ceiling(r) + 1;
            for (int y = (int)cy - n; y <= cy + n; y++)
                for (int x = (int)cx - n; x <= cx + n; x++)
                {
                    float d = MathF.Sqrt((x + .5f - cx) * (x + .5f - cx) + (y + .5f - cy) * (y + .5f - cy));
                    if (d <= r) _v[((int)p, x, y, z)] = (m(x, y, d), t);
                }
        }

        /// <summary>A ring one voxel wide round a box's outline at row y, standing out of it by one: a rim, a band.</summary>
        public void Rim(BodyPart p, int y, int x0, int z0, int x1, int z1, byte m, Tissue t = Tissue.Skin)
        {
            for (int z = z0 - 1; z <= z1; z++)
                for (int x = x0 - 1; x <= x1; x++)
                {
                    bool inside = x >= x0 && x < x1 && z >= z0 && z < z1, corner = (x == x0 - 1 || x == x1) && (z == z0 - 1 || z == z1);
                    if (!inside && !corner) _v[((int)p, x, y, z)] = (m, t);
                }
        }

        /// <summary>Cuts the four upright edges off a box, and the rim of its top: a head, rounded.</summary>
        public void Round(BodyPart p, int x0, int y0, int z0, int x1, int y1, int z1, bool top = true)
        {
            for (int y = y0; y < y1; y++)
                foreach (int x in new[] { x0, x1 - 1 })
                    foreach (int z in new[] { z0, z1 - 1 })
                        _v.Remove(((int)p, x, y, z));
            if (!top) return;
            for (int z = z0; z < z1; z++)
                for (int x = x0; x < x1; x++)
                    if (x == x0 || x == x1 - 1 || z == z0 || z == z1 - 1) _v.Remove(((int)p, x, y1 - 1, z));
        }

        /// <summary>Fills the figure in: flesh under the skin, bones, the brain and the heart.</summary>
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
                var g = new PartGrid { X0 = x0, Y0 = y0, Z0 = z0, SX = x1 - x0 + 1, SY = y1 - y0 + 1, SZ = z1 - z0 + 1 };
                g.M = new byte[g.SX * g.SY * g.SZ];
                g.T = new byte[g.M.Length];
                foreach (var (k, v) in _v)
                {
                    if (k.p != p || v.m == 0) continue;
                    int i = g.Index(k.x, k.y, k.z);
                    g.M[i] = v.m; g.T[i] = (byte)v.t;
                    g.Count++;
                }
                Inside(g, (BodyPart)p, f, waist, neck, chestX);
                f.Grid[p] = g;
                f.Top = Math.Max(f.Top, (y1 + 2) / 2);
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
            int headY0 = f.HeadY0, headY1 = f.HeadY1, w = waist, n = neck, chestY = w + (n - w) * 2 / 5;
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
        int hx = b.HeadW, hz0 = b.HeadZ, hz1 = b.HZ1, hy0 = b.Neck, hy1 = b.Crown, eye = hy0 + 3;
        s.Box(Body, -hx, hy0, hz0, hx, hy1, hz1, b.Skin);
        s.Round(Body, -hx, hy0, hz0, hx, hy1, hz1);
        s.Cut(Body, -hx, hy0, hz0, hx, hy0 + 1, hz0 + 2);
        s.Pair(Body, Body, -hx + 1, eye, b.FaceZ, -hx + 2, eye + 2, hz1, b.Eye);
        s.Pair(Body, Body, -hx + 1, eye + 2, b.FaceZ, -1, eye + 3, hz1, b.Brow != 0 ? b.Brow : b.Hair);
        s.Box(Body, -1, hy0 + 2, hz1, 1, hy0 + 4, hz1 + 1, b.Skin);
        s.Box(Body, -1, hy0 + 1, b.FaceZ, 1, hy0 + 2, hz1, b.Mouth);
        s.Pair(Body, Body, -hx - 1, eye, -1, -hx, eye + 2, 1, b.Skin);
        Hair(s, b);

        // the arms: sleeves, a cuff, the hands; the shoulders rounded on top
        byte sleeve = b.Sleeve != 0 ? b.Sleeve : b.Shirt;
        s.Pair(Main, Off, b.ArmX0, b.HandY + b.Hand, b.ArmZ0, -b.Chest, b.Neck, b.ArmZ1, sleeve);
        s.Pair(Main, Off, b.ArmX0, b.HandY, b.ArmZ0, -b.Chest, b.HandY + b.Hand, b.ArmZ1, b.Skin);
        if (b.Cuff != 0) s.Pair(Main, Off, b.ArmX0, b.HandY + b.Hand, b.ArmZ0, -b.Chest, b.HandY + b.Hand + 1, b.ArmZ1, b.Cuff);
        s.Cut(Main, b.ArmX0, b.Neck - 1, b.ArmZ0, b.ArmX0 + 1, b.Neck, b.ArmZ1);
        s.Cut(Off, -b.ArmX0 - 1, b.Neck - 1, b.ArmZ0, -b.ArmX0, b.Neck, b.ArmZ1);

        float legX = (b.LegX0 + b.LegX1) / 2f, armX = (b.ArmX0 - b.Chest) / 2f, armZ = (b.ArmZ0 + b.ArmZ1) / 2f;
        Joint(f, Body, 0, b.Waist / H, 0);
        Joint(f, LegR, legX / H, b.Leg / H, 0);
        Joint(f, LegL, -legX / H, b.Leg / H, 0);
        Joint(f, Main, armX / H, (b.Neck - 1) / H, armZ / H);
        Joint(f, Off, -armX / H, (b.Neck - 1) / H, armZ / H);
        f.HeadY0 = hy0; f.HeadY1 = hy1;
        return s;
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

    /// <summary>A pick: a haft through the fist, a head across its end, pointed below and broad above.</summary>
    static void Pick(Sculpt s, Build b, byte head, int len = 12)
    {
        int x = b.FistX, y = b.FistY, z0 = b.ArmZ1;
        s.Box(Main, x, y, b.ArmZ0 - 1, x + 1, y + 1, z0 + len, Mat.Handle, Tissue.Gear);
        s.Box(Main, x, y - 4, z0 + len - 2, x + 1, y + 4, z0 + len, head, Tissue.Gear);
        s.Box(Main, x, y - 6, z0 + len - 1, x + 1, y - 4, z0 + len, head, Tissue.Gear);
        s.Box(Main, x - 1, y + 2, z0 + len - 2, x + 2, y + 4, z0 + len, head, Tissue.Gear);
    }

    /// <summary>A sword: pommel behind the fist, a brass guard before it, a long blade narrowing to the point.</summary>
    static void Sword(Sculpt s, Build b, int len = 13)
    {
        int x = b.FistX, y = b.FistY, z0 = b.ArmZ1;
        s.Box(Main, x, y, b.ArmZ0 - 1, x + 1, y + 1, z0, Mat.Leather, Tissue.Gear);
        s.Dot(Main, x, y, b.ArmZ0 - 2, Mat.Brass, Tissue.Gear);
        s.Box(Main, x - 2, y, z0, x + 3, y + 1, z0 + 1, Mat.Brass, Tissue.Gear);
        s.Box(Main, x, y, z0 + 1, x + 1, y + 2, z0 + len, Mat.Steel, Tissue.Gear);
        s.Dot(Main, x, y, z0 + len, Mat.Steel, Tissue.Gear);
    }

    /// <summary>A bearded axe on a long haft: the blade hangs below the end, a spike behind.</summary>
    static void Axe(Sculpt s, Build b, int len = 15)
    {
        int x = b.FistX, y = b.FistY, z0 = b.ArmZ1;
        s.Box(Main, x, y, b.ArmZ0 - 1, x + 1, y + 1, z0 + len, Mat.Handle, Tissue.Gear);
        s.Box(Main, x, y - 3, z0 + len - 4, x + 1, y, z0 + len, Mat.Steel, Tissue.Gear);
        s.Box(Main, x, y - 6, z0 + len - 3, x + 1, y - 3, z0 + len + 1, Mat.Steel, Tissue.Gear);
        s.Box(Main, x, y - 7, z0 + len - 1, x + 1, y - 6, z0 + len + 1, Mat.Steel, Tissue.Gear);
        s.Box(Main, x, y + 1, z0 + len - 3, x + 1, y + 3, z0 + len - 2, Mat.Iron, Tissue.Gear);
    }

    /// <summary>A knife: a bound grip and a short blade.</summary>
    static void Knife(Sculpt s, Build b)
    {
        int x = b.FistX, y = b.FistY, z0 = b.ArmZ1;
        s.Box(Main, x, y, b.ArmZ0, x + 1, y + 1, z0 + 1, Mat.Leather, Tissue.Gear);
        s.Box(Main, x, y, z0 + 1, x + 1, y + 2, z0 + 5, Mat.Steel, Tissue.Gear);
        s.Dot(Main, x, y, z0 + 5, Mat.Steel, Tissue.Gear);
    }

    /// <summary>A mallet: a short handle and a big wooden head bound with iron.</summary>
    static void Mallet(Sculpt s, Build b)
    {
        int x = b.FistX, y = b.FistY, z0 = b.ArmZ1;
        s.Box(Main, x, y, b.ArmZ0, x + 1, y + 1, z0 + 6, Mat.Handle, Tissue.Gear);
        s.Box(Main, x - 1, y - 2, z0 + 6, x + 2, y + 3, z0 + 10, Mat.Wood, Tissue.Gear);
        s.Box(Main, x - 1, y - 2, z0 + 7, x + 2, y + 3, z0 + 8, Mat.Iron, Tissue.Gear);
    }

    /// <summary>A troll's club: a crooked log thickening to a knot studded with bones.</summary>
    static void Club(Sculpt s, Build b)
    {
        int x = b.FistX, y = b.FistY, z0 = b.ArmZ1;
        s.Box(Main, x - 1, y - 1, b.ArmZ0, x + 1, y + 2, z0 + 12, Mat.Trunk, Tissue.Gear);
        s.Box(Main, x - 2, y - 2, z0 + 11, x + 2, y + 4, z0 + 21, Mat.Trunk, Tissue.Gear);
        s.Box(Main, x - 3, y - 1, z0 + 14, x + 3, y + 3, z0 + 19, Mat.Trunk, Tissue.Gear);
        foreach (var (dx, dy, dz) in new[] { (-4, 1, 16), (3, 0, 17), (0, -3, 15), (0, 4, 18), (-3, 2, 19), (2, 3, 14) })
            s.Dot(Main, x + dx, y + dy, z0 + dz, Mat.Bone, Tissue.Gear);
    }

    /// <summary>
    /// A bow in the left fist, its limbs along z and bending back toward the archer at the tips, the string between them
    /// (held up, the arm forward, it stands upright).
    /// </summary>
    static void Bow(Sculpt s, Build b)
    {
        int x = -b.FistX - 1, y = b.FistY;
        s.Box(Off, x, y, b.ArmZ0, x + 1, y + 1, b.ArmZ1, Mat.Leather, Tissue.Gear);
        foreach (int sgn in new[] { 1, -1 })
        {
            void Run(int za, int zb, int dy)
            {
                int lo = sgn > 0 ? za : -zb, hi = sgn > 0 ? zb : -za;
                s.Box(Off, x, y + dy, lo, x + 1, y + dy + 1, hi, Mat.Handle, Tissue.Gear);
            }
            Run(b.ArmZ1, 5, 0);
            Run(4, 8, 1);
            Run(7, 9, 2);
        }
        s.Box(Off, x, y + 3, -8, x + 1, y + 4, b.ArmZ0, Mat.Linen, Tissue.Gear);
        s.Box(Off, x, y + 3, b.ArmZ1, x + 1, y + 4, 8, Mat.Linen, Tissue.Gear);
    }

    /// <summary>
    /// A round shield of planks on the left forearm, strapped to it: an iron rim, a band of the garrison's blue down the
    /// middle and a steel boss. It stands clear of the body in front of the arm.
    /// </summary>
    static void Shield(Sculpt s, Build b)
    {
        float cx = -b.ArmX0 - b.ArmW / 2f, cy = b.HandY + b.Hand + 1.5f, r = 4.7f;
        int z = b.Z1 + 2;
        s.Box(Off, (int)cx - 1, b.HandY + 1, b.ArmZ1, (int)cx + 1, b.HandY + 3, z, Mat.Leather, Tissue.Gear);
        s.Disc(Off, z, cx, cy, r, (x, y, d) => d > r - 1.1f ? Mat.Iron : Math.Abs(x + .5f - cx) < 1.1f ? Mat.ClothBlue : (x & 1) == 0 ? Mat.Board : Mat.BoardDark, Tissue.Armor);
        s.Box(Off, (int)cx - 1, (int)cy - 1, z + 1, (int)cx + 1, (int)cy + 1, z + 2, Mat.Steel, Tissue.Armor);
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
        s.Paint(Body, -b.Chest, b.Waist + 1, b.Z0, b.Chest, b.Neck, b.Z1, Mat.Mail, Tissue.Armor);
        s.Paint(Main, b.ArmX0, b.HandY + b.Hand, b.ArmZ0, -b.Chest, b.Neck, b.ArmZ1, Mat.Mail, Tissue.Armor);
        s.Paint(Off, b.Chest, b.HandY + b.Hand, b.ArmZ0, -b.ArmX0, b.Neck, b.ArmZ1, Mat.Mail, Tissue.Armor);
        s.Box(Body, -b.Chest - 1, b.Waist, b.Z0 - 1, b.Chest + 1, b.Waist + 1, b.Z1 + 1, Mat.Leather);
        for (int y = b.Waist - 3; y < b.Waist; y++)
        {
            s.Box(Body, -b.Chest - 1, y, b.Z0 - 1, b.Chest + 1, y + 1, b.Z0, Mat.Mail, Tissue.Armor);
            s.Box(Body, -b.Chest - 1, y, b.Z1, b.Chest + 1, y + 1, b.Z1 + 1, Mat.Mail, Tissue.Armor);
            s.Pair(Body, Body, -b.Chest - 1, y, b.Z0, -b.Chest, y + 1, b.Z1, Mat.Mail, Tissue.Armor);
        }
        s.Box(Body, -1, b.Waist, b.Z1 + 1, 1, b.Waist + 1, b.Z1 + 2, Mat.Brass, Tissue.Armor);
        // a blue tabard over it with a gold cross, falling below the belt to a gold hem
        s.Box(Body, -3, b.Waist + 1, b.Z1, 3, b.Neck - 1, b.Z1 + 1, Mat.ClothBlue);
        s.Box(Body, -3, b.Waist - 3, b.Z1 + 1, 3, b.Waist, b.Z1 + 2, Mat.ClothBlue);
        s.Box(Body, -3, b.Waist - 3, b.Z1 + 1, 3, b.Waist - 2, b.Z1 + 2, Mat.ClothOchre);
        s.Box(Body, -1, b.Waist + 3, b.Z1, 1, b.Neck - 2, b.Z1 + 1, Mat.ClothOchre);
        s.Box(Body, -2, b.Neck - 5, b.Z1, 2, b.Neck - 4, b.Z1 + 1, Mat.ClothOchre);
        // steel on the shoulders
        s.Box(Main, b.ArmX0 - 1, b.Neck - 3, b.ArmZ0 - 1, -b.Chest, b.Neck, b.ArmZ1 + 1, Mat.Steel, Tissue.Armor);
        s.Box(Off, b.Chest, b.Neck - 3, b.ArmZ0 - 1, -b.ArmX0 + 1, b.Neck, b.ArmZ1 + 1, Mat.Steel, Tissue.Armor);
        s.Cut(Main, b.ArmX0 - 1, b.Neck - 1, b.ArmZ0 - 1, b.ArmX0, b.Neck, b.ArmZ1 + 1);
        s.Cut(Off, -b.ArmX0, b.Neck - 1, b.ArmZ0 - 1, -b.ArmX0 + 1, b.Neck, b.ArmZ1 + 1);
        // a kettle hat, a nasal helm, or a mail coif over a bearded face
        switch (look % 3)
        {
            case 0:
                s.Paint(Body, -hx, hy1 - 3, hz0, hx, hy1, hz1, Mat.Steel, Tissue.Armor);
                s.Flat(Body, hy1 - 3, 6.2f, Mat.Steel, Tissue.Armor);
                s.Flat(Body, hy1, 2.2f, Mat.Steel, Tissue.Armor);
                break;
            case 1:
                s.Paint(Body, -hx, hy1 - 3, hz0, hx, hy1, hz1, Mat.Steel, Tissue.Armor);
                s.Rim(Body, hy1 - 3, -hx, hz0, hx, hz1, Mat.Steel, Tissue.Armor);
                s.Box(Body, -2, hy1, -2, 2, hy1 + 1, 2, Mat.Steel, Tissue.Armor);
                s.Box(Body, -1, hy1 + 1, -1, 1, hy1 + 2, 1, Mat.Steel, Tissue.Armor);
                s.Box(Body, -1, hy0 + 2, hz1, 1, hy1 - 3, hz1 + 1, Mat.Steel, Tissue.Armor);
                break;
            default:
                s.Coat(Body, -hx - 1, hy0, hz0 - 1, hx + 1, hy1 + 1, hz1 + 1, Mat.Mail, Tissue.Armor, (-hx + 1, hy0, hz1 - 2, hx - 1, hy1 - 2, hz1 + 2));
                s.Paint(Body, -b.Chest, b.Neck - 2, b.Z0, b.Chest, b.Neck, b.Z1, Mat.Mail, Tissue.Armor);
                Beard(s, b, b.Hair);
                break;
        }
        Sword(s, b);
        Shield(s, b);
        return Done(s, f, b);
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

    /// <summary>Long ears sweeping out and up (or one torn short), a hooked nose, yellow eyes with black pupils, a wide mouth with teeth.</summary>
    static void GoblinFace(Sculpt s, Build b, bool torn = false)
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
    }

    static BodyPlan Goblin(int look)
    {
        var f = new BodyPlan { Chop = .45f, Carry = -.3f, Stride = 6 };
        var b = GoblinBuild(Rags[look % 4], look % 3 == 2 ? Mat.Rag : Mat.Leather, GoblinSkins[look % 3]);
        var s = Humanoid(b, f);
        GoblinFace(s, b, look % 4 == 3);
        // a ragged loincloth, a tuft of hair on the crown of some, a bone through it on one
        s.Box(Body, -2, b.Waist - 3, b.Z1, 2, b.Waist + 1, b.Z1 + 1, Rags[(look + 1) % 4]);
        s.Cut(Body, -2, b.Waist - 3, b.Z1, -1, b.Waist - 2, b.Z1 + 1, both: true);
        if (look % 2 == 0) s.Box(Body, -1, b.Crown, -1, 1, b.Crown + 2, 1, Mat.Hair);
        if (look == 5) s.Box(Body, -4, b.Crown, 0, 4, b.Crown + 1, 1, Mat.Bone);
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
        s.Paint(Body, -b.HeadW, b.Crown - 2, b.HeadZ, b.HeadW, b.Crown, b.HZ1, cap);
        s.Box(Body, -3, b.Crown, b.HeadZ + 1, 3, b.Crown + 1, b.HZ1 - 1, cap);
        s.Box(Body, -3, b.Waist, b.Z0 - 1, -2, b.Neck + 1, b.Z0, Mat.Handle, Tissue.Gear);
        s.Box(Body, 2, b.Waist, b.Z0 - 1, 3, b.Neck + 1, b.Z0, Mat.Handle, Tissue.Gear);
        for (int y = b.Waist + 1; y < b.Neck + 1; y += 4) s.Box(Body, -2, y, b.Z0 - 1, 2, y + 1, b.Z0, Mat.Handle, Tissue.Gear);
        Mallet(s, b);
        return Done(s, f, b);
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
        Beard(s, b, new[] { Mat.Hair, Mat.HairGinger, Mat.HairGrey, Mat.HairBrown }[look % 4]);
        s.Box(Body, -3, b.Waist, b.Z0 - 3, 3, b.Neck, b.Z0, Mat.Linen, Tissue.Gear);
        s.Box(Body, -2, b.Neck, b.Z0 - 2, 2, b.Neck + 1, b.Z0 - 1, Mat.Leather, Tissue.Gear);
        Pick(s, b, Mat.Iron, 10);
        return Done(s, f, b);
    }

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
        // a sash across the chest, iron on the shoulders with spikes, a kilt of leather strips
        byte sash = look % 3 == 2 ? Mat.ClothOchre : Mat.ClothRed;
        for (int y = b.Waist + 1; y < b.Neck; y++)
        {
            int x = -b.Chest + (y - b.Waist - 1) * (2 * b.Chest - 2) / (b.Torso - 1);
            s.Paint(Body, x, y, b.Z0, x + 3, y + 1, b.Z1, sash);
        }
        s.Box(Main, b.ArmX0 - 1, b.Neck - 4, b.ArmZ0 - 1, -b.Chest, b.Neck, b.ArmZ1 + 1, Mat.Iron, Tissue.Armor);
        s.Box(Off, b.Chest, b.Neck - 4, b.ArmZ0 - 1, -b.ArmX0 + 1, b.Neck, b.ArmZ1 + 1, Mat.Iron, Tissue.Armor);
        s.Box(Main, b.ArmX0, b.Neck, -1, b.ArmX0 + 1, b.Neck + 2, 0, Mat.Bone, Tissue.Armor);
        s.Box(Off, -b.ArmX0 - 1, b.Neck, -1, -b.ArmX0, b.Neck + 2, 0, Mat.Bone, Tissue.Armor);
        for (int x = -b.Chest; x < 0; x += 2)
            s.Pair(Body, Body, x, b.Waist - 4, b.Z1, x + 1, b.Waist + 1, b.Z1 + 1, Mat.Leather);
        // a topknot, or an iron cap with horns of bone
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
        Axe(s, b);
        return Done(s, f, b);
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
        int hz1 = b.HZ1, hy0 = b.Neck, hy1 = b.Crown;
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

    static BodyPlan Deer(int look)
    {
        var f = new BodyPlan { Stride = 12 };
        var s = Quadruped(f, 12, 2, 3, 6, -9, Mat.FurDeer, Mat.Hoof);
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
