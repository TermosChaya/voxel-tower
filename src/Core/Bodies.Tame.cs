namespace VoxelTower.Core;

/// <summary>The town's beasts (Units.Tame.cs): a hen, the yard dog, a horse of the stables, a goat.</summary>
public static partial class Bodies
{
    /// <summary>
    /// A hen a hand and a half high: two yellow legs under the middle of her, toes forward; a round body full at the
    /// breast, the folded wings a shade darker along her sides, the tail feathers up behind; the neck and a small head with
    /// a yellow beak, a red comb and wattles, the eyes. White, brown or black. Her two legs are the forelegs' parts; she
    /// has no hind ones, so she steps and pecks as a beast with no knees does.
    /// </summary>
    static BodyPlan Hen(int look)
    {
        var f = new BodyPlan { Stride = 3 };
        byte plume = look switch { 0 => Mat.FeatherPale, 1 => Mat.Feather, _ => Mat.Hair };
        byte wing = look switch { 0 => Mat.Linen, 1 => Mat.FurBoar, _ => Mat.HairGrey };
        var s = new Sculpt();
        foreach (var (leg, x0) in new[] { (Main, -1), (Off, 0) })
        {
            s.Box(leg, x0, 0, 0, x0 + 1, 2, 1, Mat.Straw);
            Joint(f, leg, (x0 + .5f) / H, 2 / H, .5f / H);
        }
        Joint(f, LegR, -.5f / H, 2 / H, -.5f / H);
        Joint(f, LegL, .5f / H, 2 / H, -.5f / H);
        f.Beast = true;
        // the body, its edges off, the breast forward; the wings along the sides
        s.Box(Body, -2, 2, -3, 2, 5, 2, plume);
        s.Cut(Body, -2, 4, -3, -1, 5, 2, both: true);
        s.Cut(Body, -2, 2, -3, -1, 3, 2, both: true);
        s.Paint(Body, -2, 3, -2, -1, 5, 1, wing);
        s.Paint(Body, 1, 3, -2, 2, 5, 1, wing);
        // the tail feathers up behind, the neck, the head
        s.Box(Body, -1, 4, -4, 1, 7, -3, plume);
        s.Box(Body, -1, 4, 1, 1, 6, 3, plume);
        s.Box(Body, -1, 5, 2, 1, 8, 4, plume);
        // the beak, the comb, the wattles, the eyes (the finer work)
        s.FRod(Body, 0, 6.3f, 4, 0, 6, 4.9f, .32f, Mat.Straw);
        s.FBall(Body, 0, 8.1f, 3, .3f, .45f, .75f, Mat.ClothRed);
        s.FBall(Body, 0, 5.4f, 4.1f, .25f, .4f, .25f, Mat.ClothRed);
        s.FBall(Body, -.95f, 6.8f, 3.4f, .15f, .15f, .15f, Mat.Eye, paint: true);
        s.FBall(Body, .95f, 6.8f, 3.4f, .15f, .15f, .15f, Mat.Eye, paint: true);
        Joint(f, Body, 0, 2 / H, 0);
        f.HeadY0 = 5; f.HeadY1 = 9;
        f.NeckZ = 1; f.NeckY = 4;
        return s.Into(f, 2, 5, 2);
    }

    /// <summary>
    /// A yard dog knee-high: a scruffy cur on slim legs, a narrow body with a tucked belly, the head with a pointed muzzle,
    /// its ears up (or hanging, look 1), the tail curled over its back, a rope for a collar. Ginger, black with a white
    /// chest, or grey.
    /// </summary>
    static BodyPlan Cur(int look)
    {
        var f = new BodyPlan { Stride = 7, Chop = .6f };
        byte fur = look switch { 0 => Mat.Fawn, 1 => Mat.Hair, _ => Mat.HairGrey }, pale = look == 1 ? Mat.Linen : Mat.Belly;
        var s = Quadruped(f, 4, 1, 2, 3, -5, fur, fur);
        // the body, its back rounded, the belly tucked up behind the chest, the chest paler
        s.Box(Body, -2, 4, -6, 2, 8, 4, fur);
        s.Cut(Body, -2, 7, -6, -1, 8, 4, both: true);
        s.Cut(Body, -2, 4, -6, 2, 5, -1);
        s.Paint(Body, -1, 4, 1, 1, 7, 4, pale);
        // the neck up and forward, the head, the muzzle, the dark nose, the eyes
        s.Box(Body, -1, 6, 3, 1, 9, 5, fur);
        s.Box(Body, -2, 8, 4, 2, 11, 7, fur);
        s.Box(Body, -1, 8, 7, 1, 10, 9, look == 1 ? fur : pale);
        s.Box(Body, -1, 9, 9, 1, 10, 10, Mat.Hoof);
        s.Pair(Body, Body, -2, 10, 6, -1, 11, 7, Mat.Eye);
        // the ears: up, or hanging by the cheeks
        if (look == 1) s.Pair(Body, Body, -3, 8, 5, -2, 11, 6, fur);
        else s.Pair(Body, Body, -2, 11, 4, -1, 13, 5, fur);
        // the tail up and curled over the rump
        s.Box(Body, -1, 7, -7, 1, 9, -6, fur);
        s.Box(Body, -1, 9, -7, 1, 10, -4, fur);
        // the rope round its neck
        s.Rim(Body, 7, -1, 3, 1, 5, Mat.Straw);
        Joint(f, Body, 0, 4 / H, 1 / H);
        f.HeadY0 = 8; f.HeadY1 = 13;
        f.NeckZ = 4; f.NeckY = 6;
        return s.Into(f, 4, 8, 2);
    }

    /// <summary>
    /// A horse of the stables, as the riders' horses are made but with no saddle or rider: four legs with their forearms
    /// and gaskins full, the barrel rounded along its edges, the chest and the rump, the neck arched up with the mane on
    /// its crest, the long head, a dark muzzle, a halter of rope, the tail. Bay, black, grey or chestnut.
    /// </summary>
    static BodyPlan Horse(int look)
    {
        const int Leg = 13;
        var f = new BodyPlan { Stride = 20 };
        byte coat = Coats[look], mane = ManeOf(coat);
        var s = Quadruped(f, Leg, 2, 4, 6, -12, coat, Mat.Hoof);
        foreach (var (leg, x0) in new[] { (Main, -4), (Off, 2) }) s.Box(leg, x0, 6, 6, x0 + 2, Leg, 9, coat);
        foreach (var (leg, x0) in new[] { (LegR, -4), (LegL, 2) }) s.Box(leg, x0, 6, -13, x0 + 2, Leg, -10, coat);
        // the barrel, its edges off, the chest before it and the rump over the hind legs, the withers
        s.Box(Body, -5, Leg, -13, 5, Leg + 10, 10, coat);
        s.Cut(Body, -5, Leg, -13, -4, Leg + 1, 10, both: true);
        s.Cut(Body, -5, Leg + 9, -13, -4, Leg + 10, 10, both: true);
        s.Box(Body, -4, Leg + 2, 10, 4, Leg + 9, 12, coat);
        s.Box(Body, -4, Leg + 2, -15, 4, Leg + 9, -13, coat);
        // the neck arched up and forward a step at a time, the mane along its crest
        for (int k = 0; k < 5; k++)
        {
            s.Box(Body, -2, Leg + 6 + 2 * k, 9 + k, 2, Leg + 12 + 2 * k, 14 + k, coat);
            s.Box(Body, -1, Leg + 12 + 2 * k, 9 + k, 1, Leg + 14 + 2 * k, 10 + k, mane);
        }
        // the head: the brow, the face, the muzzle dark at the nose; the eyes, the ears, the forelock
        s.Box(Body, -2, 28, 15, 2, 33, 20, coat);
        s.Box(Body, -2, 26, 19, 2, 31, 23, coat);
        s.Box(Body, -2, 24, 22, 2, 29, 26, coat);
        s.Paint(Body, -2, 24, 25, 2, 27, 26, Mat.Hair);
        s.Pair(Body, Body, -2, 30, 18, -1, 31, 19, Mat.Eye);
        s.Pair(Body, Body, -2, 33, 16, -1, 35, 17, coat);
        s.Box(Body, -1, 33, 17, 1, 34, 19, mane);
        // a halter of rope round the nose and up the cheeks
        s.Paint(Body, -2, 26, 22, 2, 27, 26, Mat.Straw);
        s.Paint(Body, -2, 27, 19, 2, 32, 20, Mat.Straw);
        // the tail from the top of the rump, hanging
        s.Box(Body, -1, Leg + 7, -16, 1, Leg + 9, -15, mane);
        s.Box(Body, -2, Leg - 1, -17, 2, Leg + 8, -16, mane);
        Joint(f, Body, 0, Leg / H, -1 / H);
        f.HeadY0 = 24; f.HeadY1 = 35;
        f.NeckZ = 10; f.NeckY = Leg + 7;
        return s.Into(f, Leg, Leg + 10, 5);
    }

    /// <summary>
    /// A goat: slim legs, a short body with a straight back, the tail standing up, the neck up, the head with a beard and
    /// two horns curving back, the ears out. White, brown or black, the belly paler.
    /// </summary>
    static BodyPlan Goat(int look)
    {
        var f = new BodyPlan { Stride = 7 };
        byte fur = look switch { 0 => Mat.Linen, 1 => Mat.FurDeer, _ => Mat.Hair }, pale = look == 2 ? Mat.HairGrey : Mat.Belly;
        var s = Quadruped(f, 6, 1, 2, 3, -5, fur, Mat.Hoof);
        s.Box(Body, -2, 6, -6, 2, 11, 4, fur);
        s.Cut(Body, -2, 10, -6, -1, 11, 4, both: true);
        s.Paint(Body, -1, 6, -4, 1, 7, 3, pale);
        s.Box(Body, -1, 10, -7, 1, 12, -6, fur);
        // the neck, the head long and narrow, the beard, the horns back over the crown, the ears out
        s.Box(Body, -1, 9, 3, 1, 13, 5, fur);
        s.Box(Body, -1, 12, 4, 1, 15, 8, fur);
        s.Box(Body, -1, 10, 6, 1, 12, 7, look == 0 ? Mat.HairGrey : pale);
        s.Pair(Body, Body, -2, 13, 6, -1, 14, 7, Mat.Eye);
        s.Pair(Body, Body, -2, 15, 4, -1, 16, 6, Mat.Bone);
        s.Pair(Body, Body, -2, 16, 3, -1, 17, 4, Mat.Bone);
        s.Pair(Body, Body, -3, 13, 4, -2, 14, 6, fur);
        Joint(f, Body, 0, 6 / H, 1 / H);
        f.HeadY0 = 12; f.HeadY1 = 17;
        f.NeckZ = 3; f.NeckY = 9;
        return s.Into(f, 6, 11, 2);
    }
}
