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
/// A pawn or raider cut from the same micro-voxels as the world, in five parts that swing at their joints.
/// Figure coordinates are voxels with the origin between the feet, +y up and +z the way it faces,
/// so its right side (the hand with the tool) is at −x.
/// </summary>
public sealed class Figure
{
    public const int Parts = 5;
    public readonly ArrayMesh[] Mesh = new ArrayMesh[Parts];
    /// <summary>Where each part turns: the body at the hips, the arms at the shoulders, the legs at the hips.</summary>
    public readonly Vector3[] Joint = new Vector3[Parts];
    /// <summary>Voxels walked in one full stride, both legs.</summary>
    public float Stride = 18;
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
        public int Leg = 12, LegW = 3, LegD = 3, Hip = 4;
        public int Torso = 10, Chest = 4, ChestD = 4;
        public int Head = 6, HeadW = 3, HeadD = 5, HeadZ = -2;
        public int Arm = 10, ArmW = 2, ArmD = 2;
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

        public void Erase(Part p, int x, int y, int z) => _v.Remove(((int)p, x, y, z));

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

    /// <summary>Legs, boots, a torso with a belt, a head with eyes and hair, two arms with hands.</summary>
    static Sculpt Humanoid(Build b, Figure f)
    {
        var s = new Sculpt();
        int lz0 = -(b.LegD / 2), lz1 = b.LegD - b.LegD / 2;
        s.Pair(Part.LegR, Part.LegL, -b.Hip, 0, lz0, -b.Hip + b.LegW, b.Leg, lz1, b.Trousers);
        int boot = Math.Max(2, b.Leg / 4);
        s.Pair(Part.LegR, Part.LegL, -b.Hip, 0, lz0, -b.Hip + b.LegW, boot, lz1 + 1, b.Boots);

        s.Box(Part.Body, -b.Chest, b.Waist, b.Z0, b.Chest, b.Neck, b.Z1, b.Shirt);
        s.Box(Part.Body, -b.Chest, b.Waist, b.Z0, b.Chest, b.Waist + 1, b.Z1, b.Belt);

        s.Box(Part.Body, -b.HeadW, b.Neck, b.HeadZ, b.HeadW, b.Crown, b.HeadZ + b.HeadD, b.Skin);
        int eyeY = b.Neck + b.Head / 2;
        s.Dot(Part.Body, -b.HeadW + 1, eyeY, b.FaceZ, b.Eye);
        s.Dot(Part.Body, b.HeadW - 2, eyeY, b.FaceZ, b.Eye);
        s.Box(Part.Body, -b.HeadW, b.Crown - 1, b.HeadZ, b.HeadW, b.Crown, b.HeadZ + b.HeadD, b.Hair);
        s.Box(Part.Body, -b.HeadW, b.Neck + 2, b.HeadZ, b.HeadW, b.Crown, b.HeadZ + 1, b.Hair);

        byte sleeve = b.Sleeve != 0 ? b.Sleeve : b.Shirt;
        s.Pair(Part.Main, Part.Off, b.ArmX0, b.HandY + 2, b.ArmZ0, -b.Chest, b.Neck, b.ArmZ1, sleeve);
        s.Pair(Part.Main, Part.Off, b.ArmX0, b.HandY, b.ArmZ0, -b.Chest, b.HandY + 2, b.ArmZ1, b.Skin);

        float legX = -b.Hip + b.LegW / 2f, legZ = (lz0 + lz1) / 2f, armX = -b.Chest - b.ArmW / 2f;
        f.Joint[(int)Part.Body] = new Vector3(0, b.Waist, 0);
        f.Joint[(int)Part.LegR] = new Vector3(legX, b.Leg, legZ);
        f.Joint[(int)Part.LegL] = new Vector3(-legX, b.Leg, legZ);
        f.Joint[(int)Part.Main] = new Vector3(armX, b.Neck - 1, (b.ArmZ0 + b.ArmZ1) / 2f);
        f.Joint[(int)Part.Off] = new Vector3(-armX, b.Neck - 1, (b.ArmZ0 + b.ArmZ1) / 2f);
        return s;
    }

    // ---------------------------------------------------------------- what they carry (held forward from the right fist)

    static void Pick(Sculpt s, Build b, byte head, int len = 13)
    {
        int x = b.ArmX0 + b.ArmW / 2, y = b.HandY, z0 = b.ArmZ1;
        s.Box(Part.Main, x, y, z0, x + 1, y + 1, z0 + len, Mat.Handle);
        s.Box(Part.Main, x, y - 4, z0 + len - 2, x + 1, y + 6, z0 + len, head);
        s.Box(Part.Main, x, y - 5, z0 + len - 2, x + 1, y - 4, z0 + len - 1, head);
        s.Box(Part.Main, x, y + 6, z0 + len - 2, x + 1, y + 7, z0 + len - 1, head);
    }

    static void Sword(Sculpt s, Build b)
    {
        int x = b.ArmX0, y = b.HandY, z0 = b.ArmZ1;
        s.Box(Part.Main, x - 1, y - 1, z0, x + b.ArmW + 1, y + 2, z0 + 1, Mat.Brass);
        s.Box(Part.Main, x + b.ArmW / 2, y, z0 + 1, x + b.ArmW / 2 + 1, y + 2, z0 + 15, Mat.Steel);
        s.Box(Part.Main, x + b.ArmW / 2, y, b.ArmZ0 - 2, x + b.ArmW / 2 + 1, y + 1, b.ArmZ0, Mat.Brass);
    }

    static void Axe(Sculpt s, Build b)
    {
        int x = b.ArmX0 + b.ArmW / 2, y = b.HandY, z0 = b.ArmZ1;
        s.Box(Part.Main, x, y, z0, x + 1, y + 1, z0 + 14, Mat.Handle);
        s.Box(Part.Main, x, y - 6, z0 + 9, x + 1, y, z0 + 13, Mat.Steel);
        s.Box(Part.Main, x, y - 7, z0 + 8, x + 1, y - 6, z0 + 14, Mat.Steel);
        s.Box(Part.Main, x, y + 1, z0 + 10, x + 1, y + 3, z0 + 12, Mat.Iron);
    }

    static void Knife(Sculpt s, Build b)
    {
        int x = b.ArmX0 + b.ArmW / 2, y = b.HandY, z0 = b.ArmZ1;
        s.Box(Part.Main, x, y, z0, x + 1, y + 1, z0 + 2, Mat.Leather);
        s.Box(Part.Main, x, y, z0 + 2, x + 1, y + 2, z0 + 7, Mat.Steel);
    }

    static void Club(Sculpt s, Build b)
    {
        int x = b.ArmX0 + b.ArmW / 2 - 1, y = b.HandY, z0 = b.ArmZ1;
        s.Box(Part.Main, x, y, z0, x + 2, y + 2, z0 + 16, Mat.Trunk);
        s.Box(Part.Main, x - 2, y - 2, z0 + 11, x + 4, y + 4, z0 + 21, Mat.Trunk);
        foreach (var (dx, dy, dz) in new[] { (-3, 0, 14), (4, 1, 17), (0, -3, 16), (1, 4, 13), (-3, 2, 19), (4, -1, 12) })
            s.Dot(Part.Main, x + dx, y + dy, z0 + dz, Mat.Bone);
    }

    /// <summary>A bow in the left fist: its limbs run along z and bow back toward the archer.</summary>
    static void Bow(Sculpt s, Build b)
    {
        int x = -(b.ArmX0 + b.ArmW / 2) - 1, y = b.HandY;
        for (int z = -9; z <= 9; z++)
        {
            int bend = (int)MathF.Round(2.4f * (z / 9f) * (z / 9f));
            s.Dot(Part.Off, x, y + bend, z, Mat.Handle);
            if (Math.Abs(z) >= 8) s.Dot(Part.Off, x, y + bend + 1, z, Mat.Handle);
        }
        s.Box(Part.Off, x, y, -1, x + 1, y + 1, 1, Mat.Leather);
    }

    // ---------------------------------------------------------------- the kinds

    static Figure Worker()
    {
        var f = new Figure { Chop = .75f };
        var b = new Build { Shirt = Mat.Linen, Trousers = Mat.ClothDark };
        var s = Humanoid(b, f);
        // a leather apron and a felt cap
        s.Box(Part.Body, -b.Chest + 1, b.Waist - 3, b.Z1, b.Chest - 1, b.Waist + 6, b.Z1 + 1, Mat.Leather);
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
        // a dark hood over the head and shoulders
        s.Box(Part.Body, -b.HeadW - 1, b.Neck + 1, b.HeadZ - 1, b.HeadW + 1, b.Crown + 1, b.HeadZ + 2, Mat.ClothDark);
        s.Box(Part.Body, -b.HeadW - 1, b.Crown - 1, b.HeadZ - 1, b.HeadW + 1, b.Crown + 1, b.FaceZ + 1, Mat.ClothDark);
        s.Box(Part.Body, -b.Chest, b.Neck - 2, b.Z0, b.Chest, b.Neck, b.Z1, Mat.ClothDark);
        // a quiver across the back, fletchings over the shoulder
        s.Box(Part.Body, 0, b.Waist + 2, b.Z0 - 2, 3, b.Neck + 1, b.Z0, Mat.Leather);
        s.Box(Part.Body, 0, b.Neck + 1, b.Z0 - 2, 3, b.Neck + 3, b.Z0, Mat.Linen);
        Bow(s, b);
        return s.Into(f);
    }

    static Figure Swordsman()
    {
        var f = new Figure { Chop = .8f, Carry = -.35f, Shield = true };
        var b = new Build { Shirt = Mat.Mail, Trousers = Mat.ClothDark, Sleeve = Mat.Mail };
        var s = Humanoid(b, f);
        // a blue tabard over the mail, a steel cap with a nasal
        s.Box(Part.Body, -b.Chest + 1, b.Waist - 3, b.Z1, b.Chest - 1, b.Neck - 1, b.Z1 + 1, Mat.ClothBlue);
        s.Box(Part.Body, -1, b.Waist + 3, b.Z1 + 1, 1, b.Neck - 3, b.Z1 + 2, Mat.Brass);
        s.Box(Part.Body, -b.HeadW - 1, b.Crown - 2, b.HeadZ - 1, b.HeadW + 1, b.Crown + 1, b.HeadZ + b.HeadD + 1, Mat.Steel);
        s.Box(Part.Body, -1, b.Neck + 1, b.FaceZ + 1, 1, b.Crown - 2, b.FaceZ + 2, Mat.Steel);
        s.Pair(Part.Main, Part.Off, b.ArmX0 - 1, b.Neck - 3, b.ArmZ0 - 1, -b.Chest, b.Neck + 1, b.ArmZ1 + 1, Mat.Steel);
        Sword(s, b);
        // a round-ish shield before the left forearm, a brass boss in the middle
        int sx = b.Chest - 2, sy = b.HandY + 1, sz = b.ArmZ1 + 1;
        for (int y = 0; y < 9; y++)
            for (int x = 0; x < 9; x++)
            {
                if ((x == 0 || x == 8) && (y == 0 || y == 8)) continue;
                bool rim = x == 0 || x == 8 || y == 0 || y == 8;
                s.Dot(Part.Off, sx + x, sy + y, sz, rim ? Mat.Iron : Mat.ClothBlue);
            }
        s.Box(Part.Off, sx + 3, sy + 3, sz + 1, sx + 6, sy + 6, sz + 2, Mat.Brass);
        s.Box(Part.Off, sx + 3, sy + 3, b.ArmZ1, sx + 6, sy + 6, sz, Mat.Leather);
        return s.Into(f);
    }

    static Figure Goblin()
    {
        var f = new Figure { Chop = .45f, Carry = -.3f, Stride = 12 };
        var b = new Build
        {
            Leg = 7, LegW = 2, LegD = 2, Hip = 3, Torso = 7, Chest = 3, ChestD = 3, Head = 7, HeadW = 3, HeadD = 6, HeadZ = -2,
            Arm = 8, ArmW = 2, ArmD = 2, Skin = Mat.SkinGoblin, Shirt = Mat.Rag, Trousers = Mat.Leather, Boots = Mat.SkinGoblin,
            Hair = Mat.SkinGoblin, Eye = Mat.Brass, Sleeve = Mat.SkinGoblin,
        };
        var s = Humanoid(b, f);
        // long ears, a long nose, a tuft of hair
        s.Pair(Part.Body, Part.Body, -b.HeadW - 3, b.Neck + 3, 0, -b.HeadW, b.Neck + 5, 1, Mat.SkinGoblin);
        s.Dot(Part.Body, -b.HeadW - 4, b.Neck + 5, 0, Mat.SkinGoblin);
        s.Dot(Part.Body, b.HeadW + 3, b.Neck + 5, 0, Mat.SkinGoblin);
        s.Box(Part.Body, -1, b.Neck + 2, b.FaceZ + 1, 1, b.Neck + 4, b.FaceZ + 3, Mat.SkinGoblin);
        s.Box(Part.Body, -1, b.Crown, -1, 1, b.Crown + 1, 2, Mat.Hair);
        Knife(s, b);
        return s.Into(f);
    }

    static Figure Orc()
    {
        var f = new Figure { Chop = .95f, Carry = -.3f, Stride = 18 };
        var b = new Build
        {
            Leg = 12, LegW = 4, LegD = 4, Hip = 5, Torso = 12, Chest = 5, ChestD = 6, Head = 7, HeadW = 3, HeadD = 6, HeadZ = -2,
            Arm = 12, ArmW = 3, ArmD = 3, Skin = Mat.SkinOrc, Shirt = Mat.Leather, Trousers = Mat.ClothDark, Boots = Mat.Leather,
            Eye = Mat.ClothRed, Sleeve = Mat.SkinOrc,
        };
        var s = Humanoid(b, f);
        // a red sash across the chest, iron on the shoulders, tusks and a topknot
        for (int y = b.Waist + 1; y < b.Neck; y++)
        {
            int x = -b.Chest + (y - b.Waist - 1) * 2 * b.Chest / Math.Max(1, b.Torso - 1);
            s.Box(Part.Body, Math.Clamp(x - 1, -b.Chest, b.Chest - 1), y, b.Z1 - 1, Math.Clamp(x + 2, -b.Chest + 1, b.Chest), y + 1, b.Z1 + 1, Mat.ClothRed);
        }
        s.Pair(Part.Main, Part.Off, b.ArmX0 - 1, b.Neck - 3, b.ArmZ0 - 1, -b.Chest + 1, b.Neck + 1, b.ArmZ1 + 1, Mat.Iron);
        s.Dot(Part.Body, -2, b.Neck + 1, b.FaceZ + 1, Mat.Bone);
        s.Dot(Part.Body, 1, b.Neck + 1, b.FaceZ + 1, Mat.Bone);
        s.Dot(Part.Body, -2, b.Neck + 2, b.FaceZ + 1, Mat.Bone);
        s.Dot(Part.Body, 1, b.Neck + 2, b.FaceZ + 1, Mat.Bone);
        s.Box(Part.Body, -1, b.Crown, -1, 1, b.Crown + 2, 1, Mat.Hair);
        Axe(s, b);
        return s.Into(f);
    }

    static Figure Miner()
    {
        var f = new Figure { Chop = .8f, Stride = 12 };
        var b = new Build
        {
            Leg = 7, LegW = 3, LegD = 3, Hip = 4, Torso = 9, Chest = 4, ChestD = 5, Head = 6, HeadW = 3, HeadD = 5, HeadZ = -2,
            Arm = 9, ArmW = 2, ArmD = 2, Skin = Mat.SkinOrc, Shirt = Mat.Leather, Trousers = Mat.Rag, Boots = Mat.Iron,
            Eye = Mat.Brass, Sleeve = Mat.Rag,
        };
        var s = Humanoid(b, f);
        // a brass helmet with a candle burning on its brow, a beard, a sack of ore on the back
        s.Box(Part.Body, -b.HeadW - 1, b.Crown - 2, b.HeadZ - 1, b.HeadW + 1, b.Crown + 1, b.HeadZ + b.HeadD + 1, Mat.Brass);
        s.Dot(Part.Body, -1, b.Crown, b.FaceZ + 1, Mat.Ember);
        s.Dot(Part.Body, 0, b.Crown, b.FaceZ + 1, Mat.Ember);
        s.Box(Part.Body, -2, b.Neck - 2, b.FaceZ, 2, b.Neck + 2, b.FaceZ + 1, Mat.Hair);
        s.Box(Part.Body, -3, b.Waist, b.Z0 - 3, 3, b.Neck, b.Z0, Mat.Linen);
        Pick(s, b, Mat.Iron, 11);
        return s.Into(f);
    }

    static Figure Sapper()
    {
        var f = new Figure { Chop = .55f, Stride = 12 };
        var b = new Build
        {
            Leg = 7, LegW = 2, LegD = 2, Hip = 3, Torso = 7, Chest = 3, ChestD = 3, Head = 7, HeadW = 3, HeadD = 6, HeadZ = -2,
            Arm = 8, ArmW = 2, ArmD = 2, Skin = Mat.SkinGoblin, Shirt = Mat.Leather, Trousers = Mat.Rag, Boots = Mat.Leather,
            Hair = Mat.SkinGoblin, Eye = Mat.Brass, Sleeve = Mat.Rag,
        };
        var s = Humanoid(b, f);
        // goblin ears, a leather cap, a bundle of poles for ladders across the back and a mallet in the fist
        s.Pair(Part.Body, Part.Body, -b.HeadW - 3, b.Neck + 3, 0, -b.HeadW, b.Neck + 5, 1, Mat.SkinGoblin);
        s.Box(Part.Body, -1, b.Neck + 2, b.FaceZ + 1, 1, b.Neck + 4, b.FaceZ + 2, Mat.SkinGoblin);
        s.Box(Part.Body, -b.HeadW, b.Crown, b.HeadZ, b.HeadW, b.Crown + 1, b.HeadZ + b.HeadD, Mat.Leather);
        s.Box(Part.Body, -5, b.Waist + 1, b.Z0 - 2, 5, b.Waist + 2, b.Z0 - 1, Mat.Handle);
        s.Box(Part.Body, -5, b.Neck - 1, b.Z0 - 2, 5, b.Neck, b.Z0 - 1, Mat.Handle);
        s.Box(Part.Body, -4, b.Waist + 1, b.Z0 - 3, -3, b.Neck + 6, b.Z0 - 2, Mat.Handle);
        s.Box(Part.Body, 3, b.Waist + 1, b.Z0 - 3, 4, b.Neck + 6, b.Z0 - 2, Mat.Handle);
        s.Box(Part.Body, -4, b.Neck + 2, b.Z0 - 3, 4, b.Neck + 3, b.Z0 - 2, Mat.Handle);
        int x = b.ArmX0 + b.ArmW / 2, y = b.HandY, z0 = b.ArmZ1;
        s.Box(Part.Main, x, y, z0, x + 1, y + 1, z0 + 7, Mat.Handle);
        s.Box(Part.Main, x - 1, y - 2, z0 + 6, x + 2, y + 3, z0 + 9, Mat.Wood);
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
        var f = new Figure { Stride = 26 };
        var s = Quadruped(f, 11, 2, 3, 5, -8, Mat.FurDeer, Mat.Hoof);
        // a long body with a pale belly and a white scut, a neck held high, ears and antlers
        s.Box(Part.Body, -3, 10, -9, 3, 17, 8, Mat.FurDeer);
        s.Box(Part.Body, -2, 10, -8, 2, 11, 7, Mat.Belly);
        s.Box(Part.Body, -1, 14, -10, 1, 17, -9, Mat.Belly);
        s.Box(Part.Body, -2, 15, 5, 2, 22, 9, Mat.FurDeer);
        s.Box(Part.Body, -2, 20, 8, 2, 24, 14, Mat.FurDeer);
        s.Box(Part.Body, -1, 20, 14, 1, 22, 15, Mat.Hoof);
        s.Dot(Part.Body, -2, 22, 12, Mat.Eye); s.Dot(Part.Body, 1, 22, 12, Mat.Eye);
        s.Pair(Part.Body, Part.Body, -4, 23, 9, -2, 25, 10, Mat.FurDeer);
        foreach (int sx in new[] { -1, 1 })
        {
            int bx = sx < 0 ? -2 : 1;
            s.Box(Part.Body, bx, 24, 10, bx + 1, 30, 11, Mat.Bone);
            s.Box(Part.Body, bx + sx, 27, 10, bx + sx + 1, 28, 11, Mat.Bone);
            s.Box(Part.Body, bx + 2 * sx, 28, 10, bx + 2 * sx + 1, 31, 11, Mat.Bone);
            s.Box(Part.Body, bx, 29, 11, bx + 1, 32, 12, Mat.Bone);
        }
        f.Joint[(int)Part.Body] = new Vector3(0, 11, 5);
        return s.Into(f);
    }

    static Figure Boar()
    {
        var f = new Figure { Stride = 16 };
        var s = Quadruped(f, 5, 2, 4, 3, -7, Mat.FurBoar, Mat.Hoof);
        // a heavy dark body with a bristled ridge, a wedge of a head, a pale snout and tusks
        s.Box(Part.Body, -4, 4, -8, 4, 13, 6, Mat.FurBoar);
        s.Box(Part.Body, -1, 13, -6, 1, 15, 5, Mat.Hair);
        s.Box(Part.Body, -3, 5, 6, 3, 12, 11, Mat.FurBoar);
        s.Box(Part.Body, -2, 5, 11, 2, 9, 14, Mat.SkinHuman);
        s.Box(Part.Body, -2, 8, 11, -1, 11, 12, Mat.Bone);
        s.Box(Part.Body, 1, 8, 11, 2, 11, 12, Mat.Bone);
        s.Dot(Part.Body, -2, 10, 10, Mat.Eye); s.Dot(Part.Body, 1, 10, 10, Mat.Eye);
        s.Pair(Part.Body, Part.Body, -3, 12, 7, -1, 14, 8, Mat.FurBoar);
        s.Box(Part.Body, 0, 9, -9, 1, 12, -8, Mat.Hair);
        f.Joint[(int)Part.Body] = new Vector3(0, 5, 3);
        return s.Into(f);
    }

    static Figure Hare()
    {
        var f = new Figure { Stride = 9 };
        var s = Quadruped(f, 3, 1, 2, 2, -3, Mat.FurHare, Mat.FurHare);
        // a round crouched body, strong haunches, a white tail and long ears tipped dark
        s.Box(Part.Body, -2, 2, -4, 2, 7, 3, Mat.FurHare);
        s.Box(Part.Body, -2, 2, -4, 2, 3, 1, Mat.Belly);
        s.Box(Part.Body, -1, 4, -5, 1, 6, -4, Mat.Belly);
        s.Box(Part.Body, -2, 5, 2, 2, 9, 6, Mat.FurHare);
        s.Dot(Part.Body, -2, 7, 5, Mat.Eye); s.Dot(Part.Body, 1, 7, 5, Mat.Eye);
        s.Box(Part.Body, -2, 9, 3, -1, 14, 4, Mat.FurHare); s.Dot(Part.Body, -2, 14, 3, Mat.Hoof);
        s.Box(Part.Body, 1, 9, 3, 2, 14, 4, Mat.FurHare); s.Dot(Part.Body, 1, 14, 3, Mat.Hoof);
        f.Joint[(int)Part.Body] = new Vector3(0, 3, 2);
        return s.Into(f);
    }

    static Figure Troll()
    {
        var f = new Figure { Chop = 1.4f, Carry = -.2f, Stride = 28 };
        var b = new Build
        {
            Leg = 16, LegW = 6, LegD = 6, Hip = 7, Torso = 20, Chest = 8, ChestD = 9, Head = 11, HeadW = 4, HeadD = 8, HeadZ = -1,
            Arm = 22, ArmW = 4, ArmD = 4, Skin = Mat.SkinTroll, Shirt = Mat.SkinTroll, Trousers = Mat.SkinTroll, Boots = Mat.SkinTroll,
            Belt = Mat.Leather, Hair = Mat.Moss, Eye = Mat.Ember,
        };
        var s = Humanoid(b, f);
        // a loincloth, moss on the shoulders and the crown, a heavy brow and a jaw that juts
        s.Box(Part.Body, -b.Chest, b.Waist - 6, b.Z0 - 1, b.Chest, b.Waist + 2, b.Z1 + 1, Mat.Leather);
        s.Box(Part.Body, -b.Chest, b.Neck - 2, b.Z0, -b.Chest + 5, b.Neck + 1, b.Z1, Mat.Moss);
        s.Box(Part.Body, b.Chest - 4, b.Neck - 1, b.Z0, b.Chest, b.Neck + 1, b.Z1 - 2, Mat.Moss);
        s.Box(Part.Body, -b.HeadW - 1, b.Neck + 6, b.FaceZ, b.HeadW + 1, b.Neck + 7, b.FaceZ + 2, Mat.SkinTroll);
        s.Box(Part.Body, -b.HeadW + 1, b.Neck, b.FaceZ, b.HeadW - 1, b.Neck + 3, b.FaceZ + 2, Mat.SkinTroll);
        s.Dot(Part.Body, -2, b.Neck + 3, b.FaceZ + 1, Mat.Bone);
        s.Dot(Part.Body, 1, b.Neck + 3, b.FaceZ + 1, Mat.Bone);
        Club(s, b);
        return s.Into(f);
    }
}
