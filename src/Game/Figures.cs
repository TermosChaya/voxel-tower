using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The parts of a figure that move on their own: the body with the head, the arms with what they hold, the legs.
/// A beast's front legs take the arms' places.
/// </summary>
public enum Part { Body, Main, Off, LegR, LegL }

/// <summary>
/// A pawn, raider or beast cut from the same voxels as the world, in five parts that swing at their joints.
/// A voxel is 12.5 cm, so a man is 15 voxels tall with his hat, a goblin 10, a troll 24, a stag 16 to the antler tips:
/// chunky, big-headed figures that still read from above. Figure coordinates are voxels with the origin between the feet,
/// +y up and +z the way it faces, so its right side (the hand with the tool) is at −x.
/// </summary>
public sealed class Figure
{
    public const int Parts = 5;
    public readonly ArrayMesh[] Mesh = new ArrayMesh[Parts];
    /// <summary>Where each part turns: the body at the hips, the arms at the shoulders, the legs at the hips.</summary>
    public readonly Vector3[] Joint = new Vector3[Parts];
    /// <summary>Voxels walked in one full stride, both legs.</summary>
    public float Stride = 9;
    /// <summary>Seconds per blow when digging, building or hacking.</summary>
    public float Chop = .75f;
    /// <summary>How far forward the main hand is carried at rest (radians, negative is forward).</summary>
    public float Carry;
    /// <summary>A shield on the off arm, raised when fighting.</summary>
    public bool Shield;
    /// <summary>Four legs: the arms are front legs.</summary>
    public bool Beast;

    static readonly Dictionary<UnitType, Figure> Cache = new();

    public static Figure For(UnitType t)
    {
        if (Cache.TryGetValue(t, out var f)) return f;
        f = t.Key switch
        {
            "worker" => Worker(),
            "archer" => Archer(),
            "sword" => Swordsman(),
            "goblin" => Goblin(),
            "orc" => Orc(),
            "miner" => Miner(),
            "troll" => Troll(),
            "sapper" => Sapper(),
            "deer" => Deer(),
            "boar" => Boar(),
            "hare" => Hare(),
            _ => Worker(),
        };
        Cache[t] = f;
        return f;
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

    /// <summary>Voxels per part, written in figure coordinates.</summary>
    sealed class Sculpt
    {
        readonly Dictionary<(int p, int x, int y, int z), byte> _v = new();

        public void Box(Part p, int x0, int y0, int z0, int x1, int y1, int z1, byte m)
        {
            for (int y = y0; y < y1; y++)
                for (int z = z0; z < z1; z++)
                    for (int x = x0; x < x1; x++) _v[((int)p, x, y, z)] = m;
        }

        /// <summary>A box on the right side (x below zero) and its mirror on the left.</summary>
        public void Pair(Part right, Part left, int x0, int y0, int z0, int x1, int y1, int z1, byte m)
        {
            Box(right, x0, y0, z0, x1, y1, z1, m);
            Box(left, -x1, y0, z0, -x0, y1, z1, m);
        }

        public void Dot(Part p, int x, int y, int z, byte m) => _v[((int)p, x, y, z)] = m;

        public Figure Into(Figure f)
        {
            var mesher = new GreedyMesher();
            for (int p = 0; p < Parts; p++)
            {
                int x0 = int.MaxValue, y0 = int.MaxValue, z0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue, z1 = int.MinValue;
                foreach (var k in _v.Keys)
                {
                    if (k.p != p) continue;
                    x0 = Math.Min(x0, k.x); y0 = Math.Min(y0, k.y); z0 = Math.Min(z0, k.z);
                    x1 = Math.Max(x1, k.x); y1 = Math.Max(y1, k.y); z1 = Math.Max(z1, k.z);
                }
                if (x0 == int.MaxValue) continue;
                int dx = x1 - x0 + 1, dy = y1 - y0 + 1, dz = z1 - z0 + 1, px = dx + 2, pz = dz + 2;
                var grid = new byte[px * (dy + 2) * pz];
                foreach (var (k, m) in _v)
                    if (k.p == p) grid[((k.y - y0 + 1) * pz + (k.z - z0 + 1)) * px + (k.x - x0 + 1)] = m;
                f.Mesh[p] = GodotMesh.From(mesher.Mesh(grid, dx, dy, dz, x0, y0, z0));
            }
            return f;
        }
    }

    /// <summary>Legs with boots, a torso with a belt, a big head with eyes and hair, two arms with hands.</summary>
    static Sculpt Humanoid(Build b, Figure f)
    {
        var s = new Sculpt();
        int lz0 = -(b.LegD / 2), lz1 = b.LegD - b.LegD / 2;
        s.Pair(Part.LegR, Part.LegL, -b.Hip, 0, lz0, -b.Hip + b.LegW, b.Leg, lz1, b.Trousers);
        int boot = Math.Max(1, b.Leg / 4);
        s.Pair(Part.LegR, Part.LegL, -b.Hip, 0, lz0, -b.Hip + b.LegW, boot, lz1 + (b.Leg >= 6 ? 1 : 0), b.Boots);

        s.Box(Part.Body, -b.Chest, b.Waist, b.Z0, b.Chest, b.Neck, b.Z1, b.Shirt);
        s.Box(Part.Body, -b.Chest, b.Waist, b.Z0, b.Chest, b.Waist + 1, b.Z1, b.Belt);

        s.Box(Part.Body, -b.HeadW, b.Neck, b.HeadZ, b.HeadW, b.Crown, b.HeadZ + b.HeadD, b.Skin);
        int eyeY = b.Neck + b.Head / 2, inset = b.HeadW >= 3 ? 1 : 0;
        s.Dot(Part.Body, -b.HeadW + inset, eyeY, b.FaceZ, b.Eye);
        s.Dot(Part.Body, b.HeadW - 1 - inset, eyeY, b.FaceZ, b.Eye);
        s.Box(Part.Body, -b.HeadW, b.Crown - 1, b.HeadZ, b.HeadW, b.Crown, b.HeadZ + b.HeadD, b.Hair);
        s.Box(Part.Body, -b.HeadW, b.Neck + 1, b.HeadZ, b.HeadW, b.Crown, b.HeadZ + 1, b.Hair);

        byte sleeve = b.Sleeve != 0 ? b.Sleeve : b.Shirt;
        int hand = b.Arm >= 8 ? 2 : 1;
        s.Pair(Part.Main, Part.Off, b.ArmX0, b.HandY + hand, b.ArmZ0, -b.Chest, b.Neck, b.ArmZ1, sleeve);
        s.Pair(Part.Main, Part.Off, b.ArmX0, b.HandY, b.ArmZ0, -b.Chest, b.HandY + hand, b.ArmZ1, b.Skin);

        float legX = -b.Hip + b.LegW / 2f, legZ = (lz0 + lz1) / 2f, armX = -b.Chest - b.ArmW / 2f;
        f.Joint[(int)Part.Body] = new Vector3(0, b.Waist, 0);
        f.Joint[(int)Part.LegR] = new Vector3(legX, b.Leg, legZ);
        f.Joint[(int)Part.LegL] = new Vector3(-legX, b.Leg, legZ);
        f.Joint[(int)Part.Main] = new Vector3(armX, b.Neck - .5f, (b.ArmZ0 + b.ArmZ1) / 2f);
        f.Joint[(int)Part.Off] = new Vector3(-armX, b.Neck - .5f, (b.ArmZ0 + b.ArmZ1) / 2f);
        return s;
    }

    // ---------------------------------------------------------------- what they carry (held forward from the right fist)

    static void Pick(Sculpt s, Build b, byte head, int len = 6)
    {
        int x = b.FistX, y = b.HandY, z0 = b.ArmZ1;
        s.Box(Part.Main, x, y, z0, x + 1, y + 1, z0 + len, Mat.Handle);
        s.Box(Part.Main, x, y - 2, z0 + len - 1, x + 1, y + 3, z0 + len, head);
    }

    static void Sword(Sculpt s, Build b)
    {
        int x = b.FistX, y = b.HandY, z0 = b.ArmZ1;
        s.Box(Part.Main, x - 1, y, z0, x + 2, y + 1, z0 + 1, Mat.Brass);
        s.Box(Part.Main, x, y, z0 + 1, x + 1, y + 1, z0 + 8, Mat.Steel);
    }

    static void Axe(Sculpt s, Build b)
    {
        int x = b.FistX, y = b.HandY, z0 = b.ArmZ1;
        s.Box(Part.Main, x, y, z0, x + 1, y + 1, z0 + 7, Mat.Handle);
        s.Box(Part.Main, x, y - 3, z0 + 4, x + 1, y, z0 + 7, Mat.Steel);
    }

    static void Knife(Sculpt s, Build b)
    {
        int x = b.FistX, y = b.HandY, z0 = b.ArmZ1;
        s.Box(Part.Main, x, y, z0, x + 1, y + 1, z0 + 1, Mat.Leather);
        s.Box(Part.Main, x, y, z0 + 1, x + 1, y + 1, z0 + 3, Mat.Steel);
    }

    static void Mallet(Sculpt s, Build b)
    {
        int x = b.FistX, y = b.HandY, z0 = b.ArmZ1;
        s.Box(Part.Main, x, y, z0, x + 1, y + 1, z0 + 3, Mat.Handle);
        s.Box(Part.Main, x, y - 1, z0 + 3, x + 1, y + 2, z0 + 5, Mat.Wood);
    }

    static void Club(Sculpt s, Build b)
    {
        int x = b.FistX, y = b.HandY, z0 = b.ArmZ1;
        s.Box(Part.Main, x, y, z0, x + 1, y + 2, z0 + 7, Mat.Trunk);
        s.Box(Part.Main, x - 1, y - 1, z0 + 6, x + 2, y + 3, z0 + 11, Mat.Trunk);
        foreach (var (dx, dy, dz) in new[] { (-2, 1, 8), (2, 0, 9), (0, -2, 8), (0, 3, 9), (-2, 0, 10) })
            s.Dot(Part.Main, x + dx, y + dy, z0 + dz, Mat.Bone);
    }

    /// <summary>A bow in the left fist: its limbs run along z and bend back toward the archer at the tips.</summary>
    static void Bow(Sculpt s, Build b)
    {
        int x = -b.FistX - 1, y = b.HandY;
        for (int z = -4; z <= 4; z++) s.Dot(Part.Off, x, y + (Math.Abs(z) >= 3 ? 1 : 0), z, Mat.Handle);
        s.Dot(Part.Off, x, y, 0, Mat.Leather);
    }

    // ---------------------------------------------------------------- people

    static Figure Worker()
    {
        var f = new Figure { Chop = .75f };
        var b = new Build { Shirt = Mat.Linen, Trousers = Mat.ClothDark };
        var s = Humanoid(b, f);
        // a leather apron and a felt cap with a brim
        s.Box(Part.Body, -1, b.Waist - 1, b.Z1, 1, b.Waist + 3, b.Z1 + 1, Mat.Leather);
        s.Box(Part.Body, -b.HeadW, b.Crown, b.HeadZ, b.HeadW, b.Crown + 1, b.HeadZ + b.HeadD, Mat.Leather);
        s.Box(Part.Body, -b.HeadW, b.Crown - 1, b.FaceZ + 1, b.HeadW, b.Crown, b.FaceZ + 2, Mat.Leather);
        Pick(s, b, Mat.Iron);
        return s.Into(f);
    }

    static Figure Archer()
    {
        var f = new Figure { Chop = .9f };
        var b = new Build { Shirt = Mat.ClothBlue, Trousers = Mat.ClothDark, Sleeve = Mat.Leather };
        var s = Humanoid(b, f);
        // a dark hood over the head and down the back, a quiver with fletchings over the shoulder
        s.Box(Part.Body, -b.HeadW, b.Neck, b.HeadZ, b.HeadW, b.Crown + 1, b.HeadZ + 1, Mat.ClothDark);
        s.Box(Part.Body, -b.HeadW, b.Crown, b.HeadZ, b.HeadW, b.Crown + 1, b.FaceZ + 1, Mat.ClothDark);
        s.Box(Part.Body, -b.Chest, b.Neck - 1, b.Z0, b.Chest, b.Neck, b.Z1, Mat.ClothDark);
        s.Box(Part.Body, 0, b.Waist + 1, b.Z0 - 1, 1, b.Neck + 1, b.Z0, Mat.Leather);
        s.Dot(Part.Body, 0, b.Neck + 1, b.Z0 - 1, Mat.Linen);
        Bow(s, b);
        return s.Into(f);
    }

    static Figure Swordsman()
    {
        var f = new Figure { Chop = .8f, Carry = -.35f, Shield = true };
        var b = new Build { Shirt = Mat.Mail, Trousers = Mat.ClothDark, Sleeve = Mat.Mail };
        var s = Humanoid(b, f);
        // a blue tabard over the mail, a steel cap, steel on the shoulders
        s.Box(Part.Body, -1, b.Waist - 1, b.Z1, 1, b.Neck - 1, b.Z1 + 1, Mat.ClothBlue);
        s.Box(Part.Body, -b.HeadW, b.Crown - 1, b.HeadZ, b.HeadW, b.Crown + 1, b.HeadZ + b.HeadD, Mat.Steel);
        s.Pair(Part.Main, Part.Off, b.ArmX0, b.Neck - 1, b.ArmZ0, -b.Chest, b.Neck, b.ArmZ1, Mat.Steel);
        Sword(s, b);
        // a shield before the left forearm, a brass boss in the middle
        int sx = b.Chest - 2, sy = b.HandY, sz = b.ArmZ1;
        s.Box(Part.Off, sx, sy, sz, sx + 4, sy + 4, sz + 1, Mat.ClothBlue);
        s.Box(Part.Off, sx + 1, sy + 1, sz + 1, sx + 3, sy + 3, sz + 2, Mat.Brass);
        return s.Into(f);
    }

    // ---------------------------------------------------------------- the horde

    /// <summary>A goblin's build: bandy legs, a narrow body and a big head.</summary>
    static Build GoblinBuild(byte shirt, byte trousers) => new()
    {
        Leg = 3, LegW = 1, LegD = 1, Hip = 2, Torso = 3, Chest = 2, ChestD = 2, Head = 4, HeadW = 2, HeadD = 4, HeadZ = -2,
        Arm = 3, ArmW = 1, ArmD = 1, Skin = Mat.SkinGoblin, Shirt = shirt, Trousers = trousers, Boots = Mat.SkinGoblin,
        Hair = Mat.SkinGoblin, Eye = Mat.Brass, Sleeve = Mat.SkinGoblin,
    };

    static void GoblinFace(Sculpt s, Build b)
    {
        // long ears out to the sides, a long nose
        s.Pair(Part.Body, Part.Body, -b.HeadW - 2, b.Neck + 2, -1, -b.HeadW, b.Neck + 3, 0, Mat.SkinGoblin);
        s.Box(Part.Body, -1, b.Neck + 1, b.FaceZ + 1, 1, b.Neck + 2, b.FaceZ + 2, Mat.SkinGoblin);
    }

    static Figure Goblin()
    {
        var f = new Figure { Chop = .45f, Carry = -.3f, Stride = 6 };
        var b = GoblinBuild(Mat.Rag, Mat.Leather);
        var s = Humanoid(b, f);
        GoblinFace(s, b);
        s.Dot(Part.Body, 0, b.Crown, 0, Mat.Hair);
        Knife(s, b);
        return s.Into(f);
    }

    static Figure Sapper()
    {
        var f = new Figure { Chop = .55f, Stride = 6 };
        var b = GoblinBuild(Mat.Leather, Mat.Rag);
        var s = Humanoid(b, f);
        GoblinFace(s, b);
        // a leather cap, a length of ladder on the back, a mallet in the fist
        s.Box(Part.Body, -b.HeadW, b.Crown, b.HeadZ, b.HeadW, b.Crown + 1, b.HeadZ + b.HeadD, Mat.Leather);
        s.Box(Part.Body, -2, b.Waist, b.Z0 - 1, -1, b.Crown + 2, b.Z0, Mat.Handle);
        s.Box(Part.Body, 1, b.Waist, b.Z0 - 1, 2, b.Crown + 2, b.Z0, Mat.Handle);
        foreach (int y in new[] { b.Waist + 1, b.Neck, b.Crown }) s.Box(Part.Body, -1, y, b.Z0 - 1, 1, y + 1, b.Z0, Mat.Handle);
        Mallet(s, b);
        return s.Into(f);
    }

    static Figure Miner()
    {
        var f = new Figure { Chop = .8f, Stride = 6 };
        var b = new Build
        {
            Leg = 3, LegW = 2, LegD = 2, Hip = 2, Torso = 4, Chest = 2, ChestD = 2, Head = 4, HeadW = 2, HeadD = 4, HeadZ = -2,
            Arm = 4, ArmW = 1, ArmD = 2, Skin = Mat.SkinOrc, Shirt = Mat.Leather, Trousers = Mat.Rag, Boots = Mat.Iron,
            Eye = Mat.Brass, Sleeve = Mat.Rag,
        };
        var s = Humanoid(b, f);
        // a brass helmet with a candle burning on its brow, a beard, a sack of ore on the back
        s.Box(Part.Body, -b.HeadW, b.Crown - 1, b.HeadZ, b.HeadW, b.Crown + 1, b.HeadZ + b.HeadD, Mat.Brass);
        s.Dot(Part.Body, -1, b.Crown, b.FaceZ, Mat.Ember);
        s.Box(Part.Body, -1, b.Neck, b.FaceZ, 1, b.Neck + 2, b.FaceZ + 1, Mat.Hair);
        s.Box(Part.Body, -2, b.Waist, b.Z0 - 2, 2, b.Neck, b.Z0, Mat.Linen);
        Pick(s, b, Mat.Iron, 5);
        return s.Into(f);
    }

    static Figure Orc()
    {
        var f = new Figure { Chop = .95f, Carry = -.3f, Stride = 10 };
        var b = new Build
        {
            Leg = 6, LegW = 2, LegD = 2, Hip = 3, Torso = 6, Chest = 3, ChestD = 4, Head = 4, HeadW = 2, HeadD = 4, HeadZ = -1,
            Arm = 6, ArmW = 2, ArmD = 2, Skin = Mat.SkinOrc, Shirt = Mat.Leather, Trousers = Mat.ClothDark, Boots = Mat.Leather,
            Eye = Mat.ClothRed, Sleeve = Mat.SkinOrc,
        };
        var s = Humanoid(b, f);
        // a red sash across the chest, iron on the shoulders, tusks and a topknot
        for (int y = b.Waist + 1; y < b.Neck; y++)
        {
            int x = -b.Chest + (y - b.Waist - 1);
            s.Box(Part.Body, Math.Clamp(x, -b.Chest, b.Chest - 2), y, b.Z1 - 1, Math.Clamp(x + 2, -b.Chest + 2, b.Chest), y + 1, b.Z1, Mat.ClothRed);
        }
        s.Pair(Part.Main, Part.Off, b.ArmX0, b.Neck - 2, b.ArmZ0, -b.Chest, b.Neck, b.ArmZ1, Mat.Iron);
        s.Dot(Part.Body, -2, b.Neck + 1, b.FaceZ + 1, Mat.Bone);
        s.Dot(Part.Body, 1, b.Neck + 1, b.FaceZ + 1, Mat.Bone);
        s.Box(Part.Body, -1, b.Crown, 0, 1, b.Crown + 1, 1, Mat.Hair);
        Axe(s, b);
        return s.Into(f);
    }

    static Figure Troll()
    {
        var f = new Figure { Chop = 1.4f, Carry = -.2f, Stride = 14 };
        var b = new Build
        {
            Leg = 8, LegW = 3, LegD = 3, Hip = 4, Torso = 10, Chest = 4, ChestD = 5, Head = 5, HeadW = 3, HeadD = 5, HeadZ = 0,
            Arm = 12, ArmW = 3, ArmD = 3, Skin = Mat.SkinTroll, Shirt = Mat.SkinTroll, Trousers = Mat.SkinTroll, Boots = Mat.SkinTroll,
            Belt = Mat.Leather, Hair = Mat.Moss, Eye = Mat.Ember,
        };
        var s = Humanoid(b, f);
        // a loincloth, moss on the shoulders and the crown, a jaw that juts with teeth
        s.Box(Part.Body, -b.Chest, b.Waist - 3, b.Z0 - 1, b.Chest, b.Waist + 1, b.Z1 + 1, Mat.Leather);
        s.Box(Part.Body, -b.Chest, b.Neck - 1, b.Z0, -b.Chest + 3, b.Neck + 1, b.Z1, Mat.Moss);
        s.Box(Part.Body, b.Chest - 2, b.Neck - 1, b.Z0, b.Chest, b.Neck + 1, b.Z1 - 1, Mat.Moss);
        s.Box(Part.Body, -2, b.Neck, b.FaceZ, 2, b.Neck + 2, b.FaceZ + 2, Mat.SkinTroll);
        s.Dot(Part.Body, -2, b.Neck + 2, b.FaceZ + 1, Mat.Bone);
        s.Dot(Part.Body, 1, b.Neck + 2, b.FaceZ + 1, Mat.Bone);
        s.Box(Part.Body, -1, b.Crown, 1, 1, b.Crown + 1, 3, Mat.Moss);
        Club(s, b);
        return s.Into(f);
    }

    // ---------------------------------------------------------------- beasts: Main and Off are the front legs

    /// <summary>Four legs at the corners of a body, with hooves (or paws) at the bottom.</summary>
    static Sculpt Quadruped(Figure f, int legH, int legW, int halfX, int frontZ, int backZ, byte fur, byte foot)
    {
        var s = new Sculpt();
        foreach (var (p, x0, z0) in new[] { (Part.Main, -halfX, frontZ), (Part.Off, halfX - legW, frontZ), (Part.LegR, -halfX, backZ), (Part.LegL, halfX - legW, backZ) })
        {
            s.Box(p, x0, 0, z0, x0 + legW, legH, z0 + legW, fur);
            s.Box(p, x0, 0, z0, x0 + legW, 1, z0 + legW, foot);
            f.Joint[(int)p] = new Vector3(x0 + legW / 2f, legH, z0 + legW / 2f);
        }
        f.Beast = true;
        return s;
    }

    static Figure Deer()
    {
        var f = new Figure { Stride = 12 };
        var s = Quadruped(f, 6, 1, 2, 3, -4, Mat.FurDeer, Mat.Hoof);
        // a long body with a pale belly and a white rump, a neck held high, ears and branching antlers
        s.Box(Part.Body, -2, 6, -5, 2, 10, 5, Mat.FurDeer);
        s.Box(Part.Body, -1, 6, -4, 1, 7, 4, Mat.Belly);
        s.Box(Part.Body, -1, 8, -6, 1, 10, -5, Mat.Belly);
        s.Box(Part.Body, -1, 9, 3, 1, 12, 6, Mat.FurDeer);
        s.Box(Part.Body, -1, 11, 5, 1, 13, 9, Mat.FurDeer);
        s.Box(Part.Body, -1, 11, 9, 1, 12, 10, Mat.Hoof);
        s.Dot(Part.Body, -1, 12, 7, Mat.Eye); s.Dot(Part.Body, 0, 12, 7, Mat.Eye);
        s.Pair(Part.Body, Part.Body, -2, 12, 6, -1, 13, 7, Mat.FurDeer);
        foreach (var (bx, tx) in new[] { (-1, -2), (0, 1) })
        {
            s.Box(Part.Body, bx, 13, 6, bx + 1, 17, 7, Mat.Bone);
            s.Box(Part.Body, tx, 14, 6, tx + 1, 16, 7, Mat.Bone);
            s.Dot(Part.Body, bx, 16, 5, Mat.Bone);
        }
        f.Joint[(int)Part.Body] = new Vector3(0, 6, 3);
        return s.Into(f);
    }

    static Figure Boar()
    {
        var f = new Figure { Stride = 7 };
        var s = Quadruped(f, 3, 1, 2, 2, -3, Mat.FurBoar, Mat.Hoof);
        // a heavy dark body with a bristled ridge, a wedge of a head, a pale snout and tusks
        s.Box(Part.Body, -2, 2, -4, 2, 7, 3, Mat.FurBoar);
        s.Box(Part.Body, -1, 7, -3, 1, 8, 2, Mat.Hair);
        s.Box(Part.Body, -2, 2, 3, 2, 6, 6, Mat.FurBoar);
        s.Box(Part.Body, -1, 2, 6, 1, 4, 7, Mat.SkinHuman);
        s.Dot(Part.Body, -2, 4, 6, Mat.Bone); s.Dot(Part.Body, 1, 4, 6, Mat.Bone);
        s.Dot(Part.Body, -2, 5, 5, Mat.Eye); s.Dot(Part.Body, 1, 5, 5, Mat.Eye);
        s.Pair(Part.Body, Part.Body, -2, 6, 4, -1, 7, 5, Mat.FurBoar);
        s.Dot(Part.Body, 0, 5, -5, Mat.Hair);
        f.Joint[(int)Part.Body] = new Vector3(0, 3, 2);
        return s.Into(f);
    }

    static Figure Hare()
    {
        var f = new Figure { Stride = 4 };
        var s = Quadruped(f, 1, 1, 1, 1, -2, Mat.FurHare, Mat.FurHare);
        // a round crouched body, a white scut, long ears tipped dark
        s.Box(Part.Body, -1, 1, -2, 1, 3, 2, Mat.FurHare);
        s.Box(Part.Body, -1, 1, -2, 1, 2, 1, Mat.Belly);
        s.Box(Part.Body, -1, 2, -3, 1, 3, -2, Mat.Belly);
        s.Box(Part.Body, -1, 2, 2, 1, 4, 4, Mat.FurHare);
        s.Dot(Part.Body, -1, 3, 3, Mat.Eye); s.Dot(Part.Body, 0, 3, 3, Mat.Eye);
        s.Box(Part.Body, -1, 4, 2, 0, 6, 3, Mat.FurHare); s.Dot(Part.Body, -1, 5, 2, Mat.Hoof);
        s.Box(Part.Body, 0, 4, 2, 1, 6, 3, Mat.FurHare); s.Dot(Part.Body, 0, 5, 2, Mat.Hoof);
        f.Joint[(int)Part.Body] = new Vector3(0, 1, 1);
        return s.Into(f);
    }
}
