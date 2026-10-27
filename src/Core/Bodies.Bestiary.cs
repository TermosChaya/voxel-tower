using System;

namespace VoxelTower.Core;

/// <summary>
/// The creatures of the bestiary (Bestiary.cs), sculpted in half-voxels as the beasts and the people are: the people of
/// the wild on the frame of a man, the beasts on four legs, the winged ones with their wings on the body (they hover,
/// UnitView.cs), the great ones on the frame of a giant.
/// </summary>
public static partial class Bodies
{
    // ---------------------------------------------------------------- people of the wild

    /// <summary>A brigand: a hood pulled up, a scarf over the lower face, a patched jerkin, a cudgel.</summary>
    static BodyPlan Bandit(int look)
    {
        var f = new BodyPlan { Chop = .8f };
        byte cloth = new[] { Mat.ClothBrown, Mat.ClothGreen, Mat.ClothDark }[look % 3];
        var b = new Build
        {
            Skin = HumanSkins[look % 3], Hair = Hairs[(look + 1) % 4], HairStyle = 1, Shirt = Mat.Leather, Trousers = Mat.ClothDark, Boots = Mat.LeatherDark,
        };
        var s = Humanoid(b, f);
        Hood(s, b, cloth);
        // the scarf over the mouth and the nose, the eyes showing over it
        s.Paint(Body, -b.HeadW, b.Neck, b.HZ1 - 1, b.HeadW, b.Neck + 4, b.HZ1, cloth);
        s.Paint(Body, -b.Chest, b.Waist + 3, b.Z1 - 1, -1, b.Waist + 6, b.Z1, cloth);
        if (look == 1) Beard(s, b, b.Hair, false);
        Club(s, b);
        return Done(s, f, b);
    }

    /// <summary>A cultist: a red robe to the ground, a hood over a pale face, hands that glow with the fire it throws.</summary>
    static BodyPlan Cultist(int look)
    {
        var f = new BodyPlan { Chop = .9f };
        var b = new Build
        {
            Skin = Mat.SkinPale, Hair = Mat.Hair, HairStyle = 3, Shirt = Mat.ClothRed, Trousers = Mat.ClothRed, Boots = Mat.LeatherDark, Belt = Mat.Rag,
        };
        var s = Humanoid(b, f);
        Hood(s, b, look == 0 ? Mat.ClothRed : Mat.Robe);
        Skirt(s, b, Mat.ClothRed, Tissue.Skin, 8);
        s.Paint(Body, -1, b.Waist, b.Z1 - 1, 1, b.Neck - 2, b.Z1, Mat.RobeTrim);
        // the hands burning
        s.Paint(Main, b.ArmX0, b.HandY, b.ArmZ0, -b.Chest, b.HandY + b.Hand, b.ArmZ1, Mat.Ember);
        s.Paint(Off, b.Chest, b.HandY, b.ArmZ0, -b.ArmX0, b.HandY + b.Hand, b.ArmZ1, Mat.Ember);
        s.Pair(Body, Body, -2, b.Neck + 4, b.FaceZ, -1, b.Neck + 5, b.HZ1, Mat.Ember);
        Knife(s, b);
        return Done(s, f, b);
    }

    /// <summary>A ghoul: grey-green, gaunt, bald, long-armed, in grave rags, eyes like coals.</summary>
    static BodyPlan Ghoul(int look)
    {
        var f = new BodyPlan { Chop = .7f };
        byte skin = look == 0 ? Mat.SkinOrcGrey : Mat.SkinTrollDark;
        var b = new Build
        {
            Leg = 10, LegW = 2, Torso = 10, Chest = 3, ChestD = 3, Arm = 13, ArmW = 2, ArmD = 3, Head = 7, HeadW = 3,
            Skin = skin, Hair = skin, HairStyle = 3, Shirt = Mat.Rag, Trousers = Mat.Rag, Boots = skin, Belt = Mat.Rag, Eye = Mat.Ember, Mouth = Mat.Maw,
        };
        var s = Humanoid(b, f);
        // the ribs showing through the rags, the long teeth
        for (int y = b.Waist + 3; y < b.Neck - 2; y += 2) s.Paint(Body, -b.Chest, y, b.Z1 - 1, b.Chest, y + 1, b.Z1, skin);
        s.Pair(Body, Body, -2, b.Neck, b.HZ1, -1, b.Neck + 2, b.HZ1 + 1, Mat.Bone);
        // the claws past the fingers
        s.Box(Main, b.ArmX0, b.HandY - 2, b.ArmZ1 - 1, -b.Chest, b.HandY, b.ArmZ1, Mat.Bone);
        s.Box(Off, b.Chest, b.HandY - 2, b.ArmZ1 - 1, -b.ArmX0, b.HandY, b.ArmZ1, Mat.Bone);
        // the claws, long and black, out of the right hand
        Talons(s, b, Mat.Horn);
        return Done(s, f, b);
    }

    /// <summary>A wraith: a ragged dark shroud to the ground, the hood empty but for two cold fires, bony hands.</summary>
    static BodyPlan Wraith(int look)
    {
        var f = new BodyPlan { Chop = .9f };
        var b = new Build
        {
            Leg = 8, Torso = 12, Skin = Mat.Bone, Hair = Mat.Hair, HairStyle = 3, Shirt = Mat.ClothDark, Trousers = Mat.ClothDark, Boots = Mat.ClothDark,
            Belt = Mat.Rag, Eye = Mat.SoulFire, Mouth = Mat.Hair,
        };
        var s = Humanoid(b, f);
        Hood(s, b, look == 0 ? Mat.ClothDark : Mat.Rag);
        // the face a darkness, the eyes cold fire in it
        s.Paint(Body, -b.HeadW + 1, b.Neck + 1, b.HZ1 - 1, b.HeadW - 1, b.Crown - 1, b.HZ1, Mat.Hair);
        s.Pair(Body, Body, -2, b.Neck + 4, b.HZ1 - 1, -1, b.Neck + 5, b.HZ1, Mat.SoulFire);
        Skirt(s, b, Mat.Rag, Tissue.Skin, 8);
        // the fingers grown to claws of bone
        Talons(s, b, Mat.Bone);
        return Done(s, f, b);
    }

    /// <summary>A harpy: a hag's grey body to the waist, long grey hair, wings of brown feathers on the arms, a bird's legs and talons.</summary>
    static BodyPlan Harpy(int look)
    {
        var f = new BodyPlan { Chop = .6f };
        var b = new Build
        {
            Leg = 8, LegW = 2, Torso = 9, Chest = 3, ChestD = 3, Head = 7, HeadW = 3, Arm = 9, ArmW = 2, ArmD = 3,
            Skin = look == 0 ? Mat.SkinOrcGrey : Mat.SkinPale, Hair = Mat.HairGrey, HairStyle = 2, Shirt = Mat.Feather, Trousers = Mat.Feather, Boots = Mat.Horn,
            Belt = Mat.Feather, Eye = Mat.Ember,
        };
        var s = Humanoid(b, f);
        // the wings: feathers off the back of each arm, longer toward the hand, the tips pale
        s.Box(Main, b.ArmX0 - 8, b.HandY, b.ArmZ0 - 4, b.ArmX0, b.Neck - 1, b.ArmZ0 + 1, Mat.Feather);
        s.Paint(Main, b.ArmX0 - 8, b.HandY, b.ArmZ0 - 4, b.ArmX0 - 6, b.Neck - 1, b.ArmZ0 + 1, Mat.FeatherPale);
        s.Box(Off, -b.ArmX0, b.HandY, b.ArmZ0 - 4, -b.ArmX0 + 8, b.Neck - 1, b.ArmZ0 + 1, Mat.Feather);
        s.Paint(Off, -b.ArmX0 + 6, b.HandY, b.ArmZ0 - 4, -b.ArmX0 + 8, b.Neck - 1, b.ArmZ0 + 1, Mat.FeatherPale);
        // a tail of feathers behind, the talons forward on the feet
        s.Box(Body, -2, b.Waist - 3, b.Z0 - 3, 2, b.Waist + 1, b.Z0, Mat.Feather);
        s.Box(LegR, b.LegX0, 0, b.LegD / 2, b.LegX1, 1, b.LegD / 2 + 2, Mat.Horn);
        s.Box(LegL, -b.LegX1, 0, b.LegD / 2, -b.LegX0, 1, b.LegD / 2 + 2, Mat.Horn);
        s.Box(Body, -1, b.Neck + 2, b.HZ1, 1, b.Neck + 4, b.HZ1 + 1, Mat.Horn);
        // the talons of the hand at the wing's front
        Talons(s, b, Mat.Horn);
        return Done(s, f, b);
    }

    // ---------------------------------------------------------------- the great ones on two legs

    /// <summary>An ogre: fat and huge, bald with a topknot, tusks, a hide round the loins, a tree trunk for a club.</summary>
    static BodyPlan Ogre(int look)
    {
        var f = new BodyPlan { Chop = 1.5f, Carry = -.2f, Stride = 14 };
        byte skin = look == 0 ? Mat.SkinGiant : Mat.SkinGiantGrey;
        var b = new Build
        {
            Leg = 16, LegW = 6, LegD = 6, Gap = 2, Boot = 0, Torso = 18, Chest = 9, ChestD = 11, Head = 10, HeadW = 6, HeadD = 10, HeadZ = -3,
            Arm = 20, ArmW = 5, ArmD = 6, Hand = 5, FaceUp = 2, Skin = skin, Shirt = skin, Trousers = Mat.Hide, Boots = skin, Belt = Mat.Leather,
            Hair = Mat.Hair, Brow = Mat.Hair, HairStyle = 3, Mouth = Mat.Maw,
        };
        var s = Humanoid(b, f);
        // the belly hanging over the belt, the navel
        s.Box(Body, -b.Chest + 1, b.Waist - 2, b.Z1, b.Chest - 1, b.Waist + 9, b.Z1 + 3, skin);
        s.Round(Body, -b.Chest + 1, b.Waist - 2, b.Z1, b.Chest - 1, b.Waist + 9, b.Z1 + 3);
        s.Dot(Body, 0, b.Waist + 3, b.Z1 + 2, Mat.Maw);
        Skirt(s, b, Mat.Hide, Tissue.Skin, 5);
        // the topknot, the tusks out of the lower jaw
        s.Box(Body, -1, b.Crown, b.HeadZ + 3, 1, b.Crown + 3, b.HeadZ + 5, Mat.Hair);
        s.Pair(Body, Body, -3, b.Neck + 2, b.HZ1, -2, b.Neck + 5, b.HZ1 + 1, Mat.Tusk);
        Club(s, b, banded: look == 1);
        return Done(s, f, b);
    }

    /// <summary>A troll of the woods: the horde's trolls' kin with no paint and no iron, mossed over, a club of root.</summary>
    static BodyPlan WildTroll(int look)
    {
        var f = new BodyPlan { Chop = 1.4f, Carry = -.2f, Stride = 14 };
        var b = new Build
        {
            Leg = 16, LegW = 6, LegD = 6, Gap = 2, Boot = 0, Torso = 20, Chest = 8, ChestD = 10, Head = 10, HeadW = 6, HeadD = 10, HeadZ = 0,
            Arm = 24, ArmW = 6, ArmD = 6, Hand = 5, Skin = look == 0 ? Mat.SkinTroll : Mat.SkinTrollDark, Shirt = Mat.SkinTroll, Trousers = Mat.SkinTroll,
            Boots = Mat.SkinTroll, Belt = Mat.Hide, Hair = Mat.Moss, Brow = Mat.SkinTroll, Eye = Mat.Ember, Mouth = Mat.Maw, HairStyle = 1,
        };
        var s = Humanoid(b, f);
        int hz1 = b.HZ1, hy0 = b.Neck, eye = hy0 + 3;
        // its ears set back along the sides of its head, where they hold on, as the horde's trolls have them
        s.Cut(Body, -b.HeadW - 1, eye, -1, -b.HeadW, eye + 2, 1, both: true);
        s.Pair(Body, Body, -b.HeadW - 1, eye, 3, -b.HeadW, eye + 3, 5, b.Skin);
        s.Box(Body, -4, hy0, hz1, 4, hy0 + 3, hz1 + 3, b.Skin);
        s.Pair(Body, Body, -4, hy0 + 3, hz1 + 1, -3, hy0 + 5, hz1 + 2, Mat.Bone);
        // moss over the shoulders and down the back, a hide round the loins
        s.Coat(Body, -b.Chest, b.Neck - 4, b.Z0, b.Chest, b.Neck, b.Z1, Mat.Moss);
        s.Paint(Body, -b.Chest, b.Waist + 4, b.Z0 - 1, b.Chest, b.Neck, b.Z0, Mat.Moss);
        Skirt(s, b, Mat.Hide, Tissue.Skin, 5);
        TrollDetail(s, b, look, tame: false);
        Club(s, b);
        return Done(s, f, b);
    }

    /// <summary>A minotaur: a bull's head with long horns and a ring in its nose on a giant's body furred brown, hooves, a double axe.</summary>
    static BodyPlan Minotaur(int look)
    {
        var f = new BodyPlan { Chop = 1.3f, Carry = -.2f, Stride = 15 };
        byte fur = look == 0 ? Mat.FurBoar : Mat.FurDeer;
        var b = new Build
        {
            Leg = 18, LegW = 5, LegD = 6, Gap = 2, Boot = 3, Torso = 18, Chest = 8, ChestD = 9, Head = 11, HeadW = 5, HeadD = 10, HeadZ = -4,
            Arm = 18, ArmW = 5, ArmD = 5, Hand = 5, FaceUp = 2, Skin = fur, Shirt = fur, Trousers = Mat.Leather, Boots = Mat.Hoof, Belt = Mat.Leather,
            Hair = fur, Brow = fur, HairStyle = 3, Eye = Mat.Ember, Mouth = Mat.Maw,
        };
        var s = Humanoid(b, f);
        int hy0 = b.Neck, hz1 = b.HZ1;
        // the bull's muzzle forward, the nostrils and the ring
        s.Box(Body, -3, hy0 + 1, hz1, 3, hy0 + 6, hz1 + 4, fur);
        s.Box(Body, -3, hy0 + 1, hz1 + 3, 3, hy0 + 4, hz1 + 4, Mat.Hide);
        s.Pair(Body, Body, -2, hy0 + 3, hz1 + 4, -1, hy0 + 4, hz1 + 5, Mat.Maw);
        s.Box(Body, -1, hy0, hz1 + 4, 1, hy0 + 2, hz1 + 5, Mat.Brass);
        // the horns out of the sides of the crown and up
        s.Pair(Body, Body, -b.HeadW - 5, b.Crown - 3, 0, -b.HeadW, b.Crown - 1, 3, Mat.Horn);
        s.Pair(Body, Body, -b.HeadW - 6, b.Crown - 1, 0, -b.HeadW - 4, b.Crown + 4, 3, Mat.Horn);
        s.Pair(Body, Body, -b.HeadW - 6, b.Crown + 4, 0, -b.HeadW - 4, b.Crown + 6, 3, Mat.Bone);
        Skirt(s, b, Mat.Leather, Tissue.Skin, 5);
        Axe(s, b, 20);
        return Done(s, f, b);
    }

    /// <summary>A treant: an oak walking, bark for skin, a crown of leaves, a face in the bark with green light in it, branches for arms.</summary>
    static BodyPlan Treant(int look)
    {
        var f = new BodyPlan { Chop = 2f, Carry = -.2f, Stride = 16 };
        byte bark = look == 0 ? Mat.Trunk : Mat.Snag, leaves = look == 0 ? Mat.Foliage : Mat.FoliageGold;
        var b = new Build
        {
            Leg = 22, LegW = 7, LegD = 7, Gap = 2, Boot = 0, Torso = 24, Chest = 9, ChestD = 10, Head = 14, HeadW = 6, HeadD = 10, HeadZ = -3,
            Arm = 26, ArmW = 5, ArmD = 5, Hand = 6, FaceUp = 3, Skin = bark, Shirt = bark, Trousers = bark, Boots = bark, Belt = bark,
            Hair = leaves, Brow = bark, HairStyle = 3, Eye = Mat.Brew, Mouth = Mat.Maw,
        };
        var s = Humanoid(b, f);
        // the crown: clumps of leaves wider than the shoulders over the head, a higher one on top, branches up out of the
        // shoulders into clumps of their own
        s.Box(Body, -b.Chest - 3, b.Crown - 3, b.HeadZ - 4, b.Chest + 3, b.Crown + 4, b.HZ1 + 1, leaves);
        s.Round(Body, -b.Chest - 3, b.Crown - 3, b.HeadZ - 4, b.Chest + 3, b.Crown + 4, b.HZ1 + 1);
        s.Box(Body, -b.HeadW - 2, b.Crown + 3, b.HeadZ - 3, b.HeadW + 3, b.Crown + 10, b.HZ1 - 1, leaves);
        s.Round(Body, -b.HeadW - 2, b.Crown + 3, b.HeadZ - 3, b.HeadW + 3, b.Crown + 10, b.HZ1 - 1);
        s.Pair(Body, Body, -b.Chest - 1, b.Neck - 2, b.HeadZ, -b.Chest + 2, b.Crown, b.HeadZ + 3, bark);
        s.Box(Body, -b.Chest - 8, b.Crown - 1, b.HeadZ - 2, -b.Chest - 1, b.Crown + 6, b.HZ1 - 3, leaves);
        s.Round(Body, -b.Chest - 8, b.Crown - 1, b.HeadZ - 2, -b.Chest - 1, b.Crown + 6, b.HZ1 - 3);
        s.Box(Body, b.Chest + 1, b.Crown + 1, b.HeadZ - 4, b.Chest + 7, b.Crown + 7, b.HZ1 - 4, leaves);
        s.Round(Body, b.Chest + 1, b.Crown + 1, b.HeadZ - 4, b.Chest + 7, b.Crown + 7, b.HZ1 - 4);
        s.Coat(Body, -b.Chest, b.Neck - 5, b.Z0, b.Chest, b.Neck, b.Z1, leaves);
        // the face in the bark: the eyes' light, a hollow for the mouth, ridges of bark down the trunk
        s.Pair(Body, Body, -3, b.Neck + 7, b.HZ1 - 1, -1, b.Neck + 9, b.HZ1, Mat.Brew);
        s.Paint(Body, -2, b.Neck + 2, b.HZ1 - 1, 2, b.Neck + 4, b.HZ1, Mat.Maw);
        for (int x = -b.Chest + 1; x < b.Chest; x += 3) s.Paint(Body, x, b.Waist, b.Z1 - 1, x + 1, b.Neck - 5, b.Z1, Mat.PineBark);
        // twigs with leaves at the ends of the branches, roots at the feet
        s.Box(Main, b.ArmX0 - 2, b.HandY - 3, b.ArmZ0 - 1, b.ArmX0 + b.ArmW + 2, b.HandY + 2, b.ArmZ1 + 1, leaves);
        s.Box(Off, -b.ArmX0 - b.ArmW - 2, b.HandY - 3, b.ArmZ0 - 1, -b.ArmX0 + 2, b.HandY + 2, b.ArmZ1 + 1, leaves);
        s.Box(LegR, b.LegX0 - 2, 0, -1, b.LegX1 + 1, 2, b.LegD / 2 + 3, bark);
        s.Box(LegL, -b.LegX1 - 1, 0, -1, -b.LegX0 + 2, 2, b.LegD / 2 + 3, bark);
        // the twigs of its hand, grown long and hard
        Talons(s, b, Mat.Snag, 12);
        return Done(s, f, b);
    }

    /// <summary>A stone golem: rock heaped into the shape of a man, small head sunk in the shoulders, fire in its eyes, moss in its cracks.</summary>
    static BodyPlan Golem(int look)
    {
        var f = new BodyPlan { Chop = 1.8f, Carry = -.2f, Stride = 15 };
        byte rock = look == 0 ? Mat.StoneDark : Mat.Crag;
        var b = new Build
        {
            Leg = 18, LegW = 7, LegD = 7, Gap = 2, Boot = 0, Torso = 20, Chest = 10, ChestD = 10, Head = 9, HeadW = 5, HeadD = 8, HeadZ = -2,
            Arm = 22, ArmW = 7, ArmD = 7, Hand = 6, Skin = rock, Shirt = rock, Trousers = rock, Boots = rock, Belt = rock,
            Hair = rock, Brow = rock, HairStyle = 3, Eye = Mat.Ember, Mouth = Mat.Maw,
        };
        var s = Humanoid(b, f);
        // the shoulders up round the head, boulders on them, the cracks lit from within, moss in the joins
        s.Pair(Body, Body, -b.Chest - 2, b.Neck - 3, b.Z0, -b.Chest + 4, b.Neck + 4, b.Z1, rock);
        s.Pair(Body, Body, -b.Chest - 1, b.Neck + 4, b.Z0 + 1, -b.Chest + 2, b.Neck + 6, b.Z1 - 1, Mat.Rock);
        s.Paint(Body, -1, b.Waist + 4, b.Z1 - 1, 1, b.Neck - 4, b.Z1, Mat.Ember);
        s.Paint(Body, -b.Chest, b.Waist + 1, b.Z0, -b.Chest + 2, b.Waist + 4, b.Z1, Mat.Moss);
        s.Paint(Main, b.ArmX0, b.HandY, b.ArmZ0, -b.Chest, b.HandY + 3, b.ArmZ1, Mat.Rock);
        s.Paint(Off, b.Chest, b.HandY, b.ArmZ0, -b.ArmX0, b.HandY + 3, b.ArmZ1, Mat.Rock);
        // the fist ending in shards of rock
        Talons(s, b, Mat.Crag, 7);
        return Done(s, f, b);
    }

    /// <summary>A fire spirit: a man-shaped blaze, the core of embers, the flames standing off it, no feet under it but fire.</summary>
    static BodyPlan FireSpirit(int look)
    {
        var f = new BodyPlan { Chop = .8f, Stride = 11 };
        var b = new Build
        {
            Leg = 10, LegW = 3, Torso = 13, Chest = 5, ChestD = 5, Head = 8, HeadW = 4, Arm = 13, ArmW = 3, ArmD = 4,
            Skin = Mat.Ember, Shirt = Mat.Ember, Trousers = Mat.Lava, Boots = Mat.Flame, Belt = Mat.Ember, Hair = Mat.Flame, HairStyle = 2,
            Brow = Mat.Lava, Eye = Mat.Flame, Mouth = Mat.Lava,
        };
        var s = Humanoid(b, f);
        // the flames standing up off the head and the shoulders, the legs a flame narrowing to the ground
        s.Box(Body, -b.HeadW + 1, b.Crown, b.HeadZ + 2, b.HeadW - 1, b.Crown + 5, b.HZ1 - 2, Mat.Flame);
        s.Box(Body, -1, b.Crown + 5, b.HeadZ + 3, 1, b.Crown + 8, b.HZ1 - 3, Mat.Flame);
        s.Pair(Body, Body, -b.Chest - 1, b.Neck - 1, b.Z0, -b.Chest + 2, b.Neck + 3, b.Z1, Mat.Flame);
        s.Paint(Body, -b.Chest + 1, b.Waist + 3, b.Z1 - 1, b.Chest - 1, b.Neck - 3, b.Z1, Mat.Lava);
        Skirt(s, b, Mat.Flame, Tissue.Skin, 6);
        // the hand's flames drawn out to claws
        Talons(s, b, Mat.Ember);
        return Done(s, f, b);
    }

    // ---------------------------------------------------------------- on four legs

    /// <summary>A warg: a wolf as big as a pony, black-grey, a mane of stiff dark hair, yellow eyes, the lips drawn back.</summary>
    static BodyPlan Warg(int look)
    {
        var f = new BodyPlan { Stride = 14, Chop = .6f };
        byte fur = look == 0 ? Mat.HairGrey : Mat.FurBoar, dark = Mat.Hair, pale = look == 0 ? Mat.Belly : Mat.HairGrey;
        var s = Quadruped(f, 11, 3, 4, 6, -13, fur, dark);
        s.Box(Body, -4, 11, -14, 4, 18, 9, fur);
        s.Round(Body, -4, 11, -14, 4, 18, 9);
        s.Paint(Body, -3, 11, -8, 3, 12, 7, pale);
        // the mane up over the shoulders, the head long, the jaws open on the teeth
        s.Box(Body, -4, 14, 4, 4, 21, 11, dark);
        s.Box(Body, -3, 16, 9, 3, 22, 15, fur);
        s.Box(Body, -2, 15, 15, 2, 19, 21, fur);
        s.Box(Body, -2, 15, 19, 2, 16, 22, dark);
        s.Pair(Body, Body, -2, 16, 19, -1, 18, 20, Mat.Bone);
        s.Pair(Body, Body, -3, 19, 14, -2, 20, 15, Mat.Ember);
        s.Pair(Body, Body, -3, 22, 11, -1, 25, 13, fur);
        s.Box(Body, -1, 13, -18, 1, 17, -14, fur);
        Joint(f, Body, 0, 11 / H, 3 / H);
        f.HeadY0 = 15; f.HeadY1 = 22;
        f.NeckZ = 9; f.NeckY = 15;
        return s.Into(f, 11, 18, 4);
    }

    /// <summary>
    /// A giant spider: eight legs (the two front pairs on the front and the hind legs, the middle pairs off the body),
    /// a black cephalothorax and a great round abdomen with a red mark, a cluster of small red eyes and the fangs.
    /// </summary>
    static BodyPlan Spider(int look)
    {
        var f = new BodyPlan { Stride = 8, Chop = .5f };
        byte shell = Mat.Chitin, mark = look == 0 ? Mat.ClothRed : Mat.ClothOchre;
        var s = Quadruped(f, 5, 1, 7, 4, -4, shell, shell);
        // the legs' upper joints out and up from the body, so it stands spread and low
        foreach (var (leg, x0, z0) in new[] { (Main, -9, 4), (Off, 7, 4), (LegR, -9, -4), (LegL, 7, -4) })
            s.Box(leg, x0, 4, z0, x0 + 2, 7, z0 + 1, shell);
        // the middle pairs, off the body
        foreach (int z in new[] { -1, 1 })
        {
            s.Box(Body, -10, 5, z, -3, 7, z + 1, shell);
            s.Box(Body, -11, 0, z, -9, 6, z + 1, shell);
            s.Box(Body, 3, 5, z, 10, 7, z + 1, shell);
            s.Box(Body, 9, 0, z, 11, 6, z + 1, shell);
        }
        // the body and the abdomen, the mark on its back; the eyes and the fangs
        s.Box(Body, -3, 4, -2, 3, 8, 5, shell);
        s.Round(Body, -3, 4, -2, 3, 8, 5);
        s.Box(Body, -5, 4, -12, 5, 12, -2, shell);
        s.Round(Body, -5, 4, -12, 5, 12, -2);
        s.Paint(Body, -1, 11, -9, 1, 12, -5, mark);
        s.Paint(Body, -2, 11, -8, 2, 12, -6, mark);
        for (int x = -2; x <= 1; x++) s.Dot(Body, x, 6, 5, Mat.Ember);
        s.Pair(Body, Body, -2, 3, 5, -1, 5, 7, Mat.Bone);
        Joint(f, Body, 0, 5 / H, 0);
        f.HeadY0 = 4; f.HeadY1 = 8;
        f.NeckZ = 4; f.NeckY = 6;
        return s.Into(f, 5, 12, 5);
    }

    /// <summary>A centaur: a bay horse's body, a man's bare torso out of its withers, a beard, a quiver and a bow in hand.</summary>
    static BodyPlan Centaur(int look)
    {
        var f = new BodyPlan { Stride = 14 };
        byte coat = new[] { Mat.HorseBay, Mat.HorseChestnut, Mat.HorseGrey }[look % 3], skin = HumanSkins[look % 3], hair = Hairs[look % 4];
        var s = Quadruped(f, 12, 2, 3, 6, -11, coat, Mat.Hoof);
        foreach (var (leg, x0) in new[] { (Main, -3), (Off, 1) }) s.Box(leg, x0, 7, 5, x0 + 2, 12, 8, coat);
        foreach (var (leg, x0) in new[] { (LegR, -3), (LegL, 1) }) s.Box(leg, x0, 6, -12, x0 + 2, 12, -8, coat);
        // the horse's barrel and its tail
        s.Box(Body, -4, 12, -12, 4, 20, 8, coat);
        s.Round(Body, -4, 12, -12, 4, 20, 8);
        s.Box(Body, -1, 15, -15, 1, 20, -12, hair);
        // the man's torso up out of the withers, the arms down its sides holding the bow before it
        s.Box(Body, -4, 20, 2, 4, 31, 8, skin);
        s.Paint(Body, -4, 20, 2, 4, 22, 8, Mat.Leather);
        s.Box(Body, -6, 22, 3, -4, 30, 6, skin);
        s.Box(Body, 4, 22, 3, 6, 30, 6, skin);
        s.Box(Body, -3, 31, 2, 3, 38, 8, skin);
        s.Round(Body, -3, 31, 2, 3, 38, 8);
        s.Paint(Body, -3, 35, 2, 3, 38, 8, hair);
        s.Box(Body, -2, 30, 7, 2, 33, 9, hair);
        s.Pair(Body, Body, -2, 35, 7, -1, 36, 8, Mat.Eye);
        // the quiver on the back, the bow held out
        s.Box(Body, 1, 24, 0, 3, 33, 2, Mat.Leather);
        s.Box(Body, -1, 21, 9, 0, 33, 10, Mat.Handle);
        s.Box(Body, -1, 22, 10, 0, 32, 11, Mat.Linen);
        Joint(f, Body, 0, 12 / H, 4 / H);
        f.HeadY0 = 31; f.HeadY1 = 38;
        f.NeckZ = 5; f.NeckY = 30;
        return s.Into(f, 12, 30, 4);
    }

    /// <summary>A griffin: a lion's tawny body and tufted tail, an eagle's white head and hooked yellow beak, great brown wings spread.</summary>
    static BodyPlan Griffin(int look)
    {
        var f = new BodyPlan { Stride = 14, Chop = .7f };
        byte coat = look == 0 ? Mat.Fawn : Mat.FurDeer;
        var s = Quadruped(f, 10, 3, 4, 6, -12, coat, Mat.Horn);
        s.Box(Body, -4, 10, -14, 4, 18, 9, coat);
        s.Round(Body, -4, 10, -14, 4, 18, 9);
        s.Paint(Body, -3, 10, -10, 3, 11, 6, Mat.Belly);
        // the eagle's neck and head, white, the beak hooked down
        s.Box(Body, -3, 15, 6, 3, 23, 12, Mat.FeatherPale);
        s.Box(Body, -3, 21, 9, 3, 27, 15, Mat.FeatherPale);
        s.Round(Body, -3, 21, 9, 3, 27, 15);
        s.Box(Body, -1, 22, 15, 1, 25, 18, Mat.Brass);
        s.Box(Body, -1, 21, 17, 1, 23, 18, Mat.Brass);
        s.Pair(Body, Body, -3, 24, 12, -2, 25, 13, Mat.Ember);
        // the wings, spread up and out, the long feathers at their ends darker
        Wings(s, -4, -22, 16, 6, -9, 4, Mat.Feather, Mat.FeatherPale, look == 0 ? Mat.FurBoar : Mat.Hair);
        // the lion's tail with its tuft
        s.Box(Body, -1, 13, -19, 1, 15, -14, coat);
        s.Box(Body, -1, 12, -21, 1, 15, -19, Mat.FurBoar);
        Joint(f, Body, 0, 10 / H, 3 / H);
        f.HeadY0 = 21; f.HeadY1 = 27;
        f.NeckZ = 9; f.NeckY = 18;
        return s.Into(f, 10, 18, 4);
    }

    /// <summary>A basilisk: a long, low lizard, green, with a pale belly, a red crest down its back, a heavy head and eyes of a sickly green.</summary>
    static BodyPlan Basilisk(int look)
    {
        var f = new BodyPlan { Stride = 10, Chop = .9f };
        byte scale = look == 0 ? Mat.ScaleGreen : Mat.SkinTrollDark;
        var s = Quadruped(f, 5, 3, 6, 6, -10, scale, Mat.Horn);
        s.Box(Body, -5, 5, -14, 5, 11, 10, scale);
        s.Round(Body, -5, 5, -14, 5, 11, 10);
        s.Paint(Body, -4, 5, -12, 4, 6, 9, Mat.ScaleBelly);
        // the middle legs off the body
        s.Pair(Body, Body, -8, 0, -3, -5, 6, 0, scale);
        // the crest down the back, the head forward, the eyes
        for (int z = -12; z < 8; z += 3) s.Box(Body, -1, 11, z, 1, 14, z + 2, Mat.ClothRed);
        s.Box(Body, -4, 5, 10, 4, 11, 17, scale);
        s.Round(Body, -4, 5, 10, 4, 11, 17);
        s.Box(Body, -3, 5, 17, 3, 8, 19, scale);
        s.Pair(Body, Body, -4, 9, 14, -3, 10, 15, Mat.Brew);
        s.Box(Body, -1, 11, 11, 1, 14, 14, Mat.ClothRed);
        // the tail, long and tapering
        s.Box(Body, -3, 6, -20, 3, 10, -14, scale);
        s.Box(Body, -2, 6, -26, 2, 9, -20, scale);
        s.Box(Body, -1, 6, -31, 1, 8, -26, scale);
        Joint(f, Body, 0, 5 / H, 0);
        f.HeadY0 = 5; f.HeadY1 = 11;
        f.NeckZ = 10; f.NeckY = 8;
        return s.Into(f, 5, 11, 5);
    }

    /// <summary>A wyvern: a dragon's lesser kin, green-grey scales, a long neck and a horned head, leathery wings, a tail ending in a sting.</summary>
    static BodyPlan Wyvern(int look)
    {
        var f = new BodyPlan { Stride = 14, Chop = .8f };
        byte scale = look == 0 ? Mat.ScaleGreen : Mat.SkinTrollDark;
        var s = Quadruped(f, 9, 3, 4, 5, -8, scale, Mat.Horn);
        s.Box(Body, -5, 9, -11, 5, 18, 8, scale);
        s.Round(Body, -5, 9, -11, 5, 18, 8);
        s.Paint(Body, -4, 9, -9, 4, 10, 7, Mat.ScaleBelly);
        // the neck up and forward, the head with its horns
        s.Box(Body, -2, 15, 6, 2, 22, 11, scale);
        s.Box(Body, -2, 20, 10, 2, 26, 14, scale);
        s.Box(Body, -3, 23, 13, 3, 28, 21, scale);
        s.Round(Body, -3, 23, 13, 3, 28, 21);
        s.Pair(Body, Body, -2, 27, 14, -1, 32, 16, Mat.Horn);
        s.Pair(Body, Body, -3, 26, 18, -2, 27, 19, Mat.Ember);
        // the wings, leathery, on bones
        Wings(s, -5, -25, 16, 6, -8, 4, Mat.Membrane, scale, Mat.Bone);
        // the tail and its sting
        s.Box(Body, -2, 11, -19, 2, 15, -11, scale);
        s.Box(Body, -1, 12, -27, 1, 14, -19, scale);
        s.Box(Body, -1, 11, -31, 1, 15, -27, Mat.Bone);
        Joint(f, Body, 0, 9 / H, 0);
        f.HeadY0 = 23; f.HeadY1 = 28;
        f.NeckZ = 8; f.NeckY = 17;
        return s.Into(f, 9, 18, 5);
    }

    /// <summary>
    /// A dragon: red scales, a pale belly, a long neck stepping up to a great horned head with fire in its eyes, wings of
    /// dark skin on long bones spread wide, a spiked tail longer than its body.
    /// </summary>
    static BodyPlan Dragon(int look)
    {
        var f = new BodyPlan { Stride = 22, Chop = 1.2f };
        byte scale = look == 0 ? Mat.ScaleRed : Mat.StoneDark, wing = look == 0 ? Mat.Membrane : Mat.Hair;
        var s = Quadruped(f, 14, 5, 8, 10, -16, scale, Mat.Bone);
        foreach (var (leg, x0) in new[] { (Main, -8), (Off, 3) }) s.Box(leg, x0, 7, 9, x0 + 5, 14, 16, scale);
        foreach (var (leg, x0) in new[] { (LegR, -8), (LegL, 3) }) s.Box(leg, x0, 6, -18, x0 + 5, 14, -10, scale);
        s.Box(Body, -8, 14, -20, 8, 30, 14, scale);
        s.Round(Body, -8, 14, -20, 8, 30, 14);
        s.Paint(Body, -6, 14, -18, 6, 15, 13, Mat.ScaleBelly);
        // the spines along the back
        for (int z = -18; z < 12; z += 4) s.Box(Body, -1, 30, z, 1, 34, z + 2, Mat.Bone);
        // the neck stepping up and forward, the head, the jaw, the horns, the eyes
        s.Box(Body, -4, 24, 12, 4, 34, 19, scale);
        s.Box(Body, -3, 30, 17, 3, 42, 23, scale);
        s.Box(Body, -3, 38, 21, 3, 46, 26, scale);
        s.Box(Body, -5, 42, 24, 5, 50, 36, scale);
        s.Round(Body, -5, 42, 24, 5, 50, 36);
        s.Box(Body, -4, 40, 28, 4, 43, 37, scale);
        s.Paint(Body, -4, 42, 30, 4, 43, 37, Mat.Maw);
        s.Pair(Body, Body, -4, 43, 33, -3, 44, 36, Mat.Bone);
        s.Pair(Body, Body, -5, 46, 30, -4, 48, 32, Mat.Ember);
        s.Pair(Body, Body, -5, 49, 24, -3, 55, 27, Mat.Horn);
        s.Pair(Body, Body, -5, 54, 22, -3, 57, 25, Mat.Bone);
        // the wings: the long bone out and up, the skin under it back to the flank
        Wings(s, -7, -40, 26, 8, -17, 6, wing, scale, Mat.Bone);
        // the tail, tapering, the spikes at its end
        s.Box(Body, -5, 16, -30, 5, 24, -20, scale);
        s.Box(Body, -4, 15, -40, 4, 21, -30, scale);
        s.Box(Body, -2, 14, -50, 2, 18, -40, scale);
        s.Box(Body, -1, 14, -56, 1, 16, -50, scale);
        s.Pair(Body, Body, -4, 15, -56, -1, 17, -52, Mat.Bone);
        Joint(f, Body, 0, 14 / H, 0);
        f.HeadY0 = 42; f.HeadY1 = 50;
        f.NeckZ = 14; f.NeckY = 28;
        return s.Into(f, 14, 30, 8);
    }

    /// <summary>
    /// A pair of wings spread from the shoulders (x from root out to tip, below zero): steps rising outward and the
    /// chord narrowing toward the tip, so they stand up in a shallow V; a bone or the first feathers along the front
    /// edge and a claw or the dark long feathers at the tip.
    /// </summary>
    static void Wings(Sculpt s, int root, int tip, int y, int front, int back, int steps, byte skin, byte edge, byte end)
    {
        for (int i = 0; i < steps; i++)
        {
            int xa = root + (tip - root) * i / steps, xb = root + (tip - root) * (i + 1) / steps;
            int yy = y + 2 * i, zb = back + (front - back) * i / (steps + 1);
            s.Pair(Body, Body, xb, yy, zb, xa, yy + 3, front, skin);
            s.Pair(Body, Body, xb, yy + 1, front - 2, xa, yy + 4, front + 1, edge);
        }
        int top = y + 2 * steps;
        s.Pair(Body, Body, tip - 2, top - 2, front - 4, tip + 2, top + 1, front + 1, end);
    }

    /// <summary>
    /// Claws in place of a weapon, for those of the wild who fight with their hands: three hooks out of the front of the
    /// right fist, a cell apart, curving down to their points.
    /// </summary>
    static void Talons(Sculpt s, Build b, byte claw, int len = 10)
    {
        var g = InFist(s, b, out int x, out int y, out int z0);
        for (int c = 0; c < 3; c++)
            for (int k = 0; k < len; k++)
            {
                float t = k / (float)len;
                int yy = y + 1 - (int)MathF.Round(t * t * 4);
                // (two cells deep all along, so each step down holds on to the one before by a face)
                g.Dot(x - 1 + 2 * c, yy, z0 + k, claw);
                g.Dot(x - 1 + 2 * c, yy + 1, z0 + k, claw);
            }
        g.Tip = Half(g, x + 1, y - 3, z0 + len);
    }
}
