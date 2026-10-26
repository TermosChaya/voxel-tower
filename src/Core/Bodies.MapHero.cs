namespace VoxelTower.Core;

public static partial class Bodies
{
    /// <summary>
    /// A commander of ours in the saddle, as the map of valleys shows a hero walking it: the commander's mail, his tabard
    /// and the cloak of his look over the horse's rump, his helm with its plume, his longsword; the horse of a coat of its
    /// own. Sculpted once a look, as the kinds' bodies are.
    /// </summary>
    public static BodyPlan HeroRider(int look)
    {
        look = ((look % 3) + 3) % 3;
        if (Cache.TryGetValue(("herorider", look), out var p)) return p;
        return Cache.GetOrAdd(("herorider", look), MountedCaptain(look));
    }

    static BodyPlan MountedCaptain(int look)
    {
        var f = new BodyPlan { Chop = .8f, Carry = -.35f };
        byte cloak = new[] { Mat.ClothBlue, Mat.ClothRed, Mat.ClothGreen }[look];
        var b = new Build
        {
            Shirt = Mat.Mail, Sleeve = Mat.Mail, Trousers = Mat.ClothDark, Boots = Mat.Leather, Belt = Mat.LeatherDark, Skin = HumanSkins[look],
            Hair = Hairs[(look + 2) % 4], HairStyle = 1,
        };
        byte coat = Coats[(look + 2) % 4];
        var s = Horseman(b, f, coat, ManeOf(coat), cloak, Mat.ClothOchre);
        int hy1 = b.Crown, hz0 = b.HeadZ;
        Mail(s, b);
        // the tabard of his colour with its ochre stripe, the pauldrons, the cloak from the shoulders down over the rump
        s.Box(Body, -3, b.Waist + 1, b.Z1, 3, b.Neck - 1, b.Z1 + 1, cloak);
        s.Box(Body, -1, b.Waist + 3, b.Z1, 1, b.Neck - 2, b.Z1 + 1, Mat.ClothOchre);
        Spaulders(s, b);
        s.Box(Body, -b.Chest - 1, b.Waist - 4, b.Z0 - 3, b.Chest + 1, b.Neck, b.Z0 - 2, cloak, Tissue.Armor);
        s.Box(Body, -b.Chest - 1, b.Neck - 1, b.Z0 - 2, b.Chest + 1, b.Neck, b.Z1 + 1, cloak, Tissue.Armor);
        s.Box(Body, -1, b.Neck - 2, b.Z1 + 1, 1, b.Neck - 1, b.Z1 + 2, Mat.Brass, Tissue.Gear);
        // the helm, and the plume up from its top falling back
        NasalHelm(s, b, aventail: false);
        s.Box(Body, -1, hy1 + 1, -1, 1, hy1 + 5, 1, cloak, Tissue.Gear);
        s.Box(Body, -1, hy1 + 3, hz0, 1, hy1 + 6, -1, cloak, Tissue.Gear);
        s.Box(Body, -1, hy1 + 1, hz0 - 1, 1, hy1 + 4, hz0, cloak, Tissue.Gear);
        if (look != 1) Beard(s, b, b.Hair);
        Sword(s, b, 15, Mat.Brass, longsword: true);
        return Ridden(s, f, b);
    }
}
