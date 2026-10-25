using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>The troops the lore opens: foot, shot, the riders, the tame troll, the giant and the hounds.</summary>
public static partial class Bodies
{
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
        f.NeckZ = 6; f.NeckY = 10;
        return s.Into(f, 7, 13, 3);
    }

    /// <summary>
    /// One of an engine's crew: a man in a short jerkin, a long apron of dark leather over it from the chest to the
    /// thighs, a padded leather cap tied close over the ears, rough gloves to the wrist, a pouch of pins and wedges at
    /// the belt, and a mallet for knocking them home.
    /// </summary>
    static BodyPlan Crewman(int look)
    {
        var f = new BodyPlan { Chop = .75f };
        byte hair = Hairs[(look * 3 + 1) % 4];
        var b = new Build
        {
            Skin = HumanSkins[look % 3], Hair = hair, HairStyle = 1,
            Shirt = new[] { Mat.ClothBrown, Mat.Linen, Mat.ClothGreen }[look % 3], Trousers = Mat.ClothDark, Belt = Mat.LeatherDark,
        };
        var s = Humanoid(b, f);
        int hx = b.HeadW, hz0 = b.HeadZ, hz1 = b.HZ1, hy1 = b.Crown;
        // the apron, darker and longer than a worker's, and its bib's straps up over the shoulders
        s.Box(Body, -3, b.Waist - 4, b.Z1, 3, b.Neck - 3, b.Z1 + 1, Mat.LeatherDark);
        s.Box(Body, -3, b.Neck - 3, b.Z1 - 1, -2, b.Neck, b.Z1, Mat.LeatherDark);
        s.Box(Body, 2, b.Neck - 3, b.Z1 - 1, 3, b.Neck, b.Z1, Mat.LeatherDark);
        // gloves of rough leather over the hands and the wrists
        s.Paint(Main, b.ArmX0, b.HandY, b.ArmZ0, -b.Chest, b.HandY + b.Hand + 1, b.ArmZ1, Mat.Leather);
        s.Paint(Off, b.Chest, b.HandY, b.ArmZ0, -b.ArmX0, b.HandY + b.Hand + 1, b.ArmZ1, Mat.Leather);
        // the padded cap: over the crown and down over the ears, the face open
        s.Paint(Body, -hx, hy1 - 4, hz0, hx, hy1, hz1 - 2, Mat.Leather);
        s.Paint(Body, -hx, hy1 - 2, hz1 - 2, hx, hy1, hz1, Mat.Leather);
        s.Box(Body, -hx + 1, hy1, hz0 + 1, hx - 1, hy1 + 1, hz1 - 1, Mat.Leather);
        if (look == 1) Beard(s, b, hair);
        Pouch(s, b, 1);
        Mallet(s, b);
        return Done(s, f, b);
    }

    /// <summary>
    /// A commander of ours: mail under a surcoat of its colour, steel on the shoulders, a long cloak down its back to the
    /// knees, a nasal helm with a plume of the cloak's colour standing up from its top, a sword, and a hunting horn at
    /// the left hip in brass mounts; no shield, the sword guards it. Three looks: red, blue and green.
    /// </summary>
    static BodyPlan Captain(int look)
    {
        var f = new BodyPlan { Chop = .8f, Carry = -.35f };
        byte cloak = new[] { Mat.ClothRed, Mat.ClothBlue, Mat.ClothGreen }[look % 3];
        var b = new Build
        {
            Shirt = Mat.Mail, Trousers = Mat.ClothDark, Sleeve = Mat.Mail, Belt = Mat.LeatherDark, Skin = HumanSkins[look % 3], Hair = Hairs[(look + 2) % 4], HairStyle = 1,
        };
        var s = Humanoid(b, f);
        int hy1 = b.Crown, hz0 = b.HeadZ;
        Mail(s, b);
        Skirt(s, b, Mat.Mail, Tissue.Armor);
        Surcoat(s, b, cloak, Mat.ClothOchre, drop: 4);
        Spaulders(s, b);
        Scabbard(s, b);
        // the cloak: from the shoulders down the back to the knees, a little wider than the body, a brass clasp at the neck
        s.Box(Body, -b.Chest - 1, b.Waist - 6, b.Z0 - 3, b.Chest + 1, b.Neck, b.Z0 - 2, cloak, Tissue.Armor);
        s.Box(Body, -b.Chest - 1, b.Neck - 1, b.Z0 - 2, b.Chest + 1, b.Neck, b.Z1 + 1, cloak, Tissue.Armor);
        s.Box(Body, -1, b.Neck - 2, b.Z1 + 1, 1, b.Neck - 1, b.Z1 + 2, Mat.Brass, Tissue.Gear);
        // the horn at the left hip, its bell forward, in brass mounts on a strap
        s.Box(Body, b.Chest, b.Waist - 2, -1, b.Chest + 1, b.Waist, 2, Mat.Horn, Tissue.Gear);
        s.Box(Body, b.Chest, b.Waist - 1, 2, b.Chest + 1, b.Waist + 1, 3, Mat.Brass, Tissue.Gear);
        s.Box(Body, b.Chest, b.Waist - 3, -2, b.Chest + 1, b.Waist - 2, -1, Mat.Brass, Tissue.Gear);
        // the helm, and the plume up from its top falling back
        NasalHelm(s, b, aventail: false);
        s.Box(Body, -1, hy1 + 1, -1, 1, hy1 + 5, 1, cloak, Tissue.Gear);
        s.Box(Body, -1, hy1 + 3, hz0, 1, hy1 + 6, -1, cloak, Tissue.Gear);
        s.Box(Body, -1, hy1 + 1, hz0 - 1, 1, hy1 + 4, hz0, cloak, Tissue.Gear);
        if (look != 1) Beard(s, b, b.Hair);
        Sword(s, b, longsword: true);
        return Done(s, f, b);
    }
}
