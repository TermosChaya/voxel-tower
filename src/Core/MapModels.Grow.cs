using System;

namespace VoxelTower.Core;

/// <summary>
/// What grows on the big world map, two voxels to a unit: spruces in tiers of needles, pines with their crowns high
/// and flat, oaks broad and lumpy, birches white and slight, gold in autumn; dead trees grey, the horde's trees burnt
/// black; firs under snow, palms by the sand, willows over the swamps, apple trees by the farms; bushes, dry scrub,
/// stones bare or mossy or under snow, a clump of flowers, reeds and the pads of lilies. Each has six looks, its
/// height and its lean its own; every trunk goes a little into the ground, so a slope never shows under it.
/// </summary>
public static partial class MapModels
{
    static void Grow(ModelCanvas c, MapModel m, int v)
    {
        double R(int salt) => Hash.H3(v * 7 + 3, salt, (int)m * 11);
        float tall = 1 + (float)(R(1) - .5) * .35f;
        switch (m)
        {
            case MapModel.Spruce: Spruce(c, (9.5f + v % 3 * 1.4f) * tall, 2.7f + (float)R(2) * .6f, Mat.Leaf, Mat.NeedleLight, snow: false); break;
            case MapModel.SnowFir: Spruce(c, (8.5f + v % 3 * 1.2f) * tall, 2.8f + (float)R(2) * .5f, Mat.Leaf, Mat.NeedleLight, snow: true); break;
            case MapModel.Pine:
            {
                float h = (11 + v % 3 * 1.5f) * tall;
                float lean = (float)(R(3) - .5) * 1.2f;
                c.Rod(0, -2, 0, lean, h * .78f, lean * .6f, .42f, Mat.PineBark);
                // a branch or two bare under the crown
                c.Rod(lean * .7f, h * .55f, 0, lean * .7f + 1.6f, h * .66f, .4f, .16f, Mat.PineBark);
                for (int k = 0; k < 3; k++)
                {
                    float ox = lean + (k - 1) * 1.6f + (float)(R(10 + k) - .5f), oz = (float)(R(20 + k) - .5f) * 2;
                    float oy = h * (.8f + (k == 1 ? .08f : 0));
                    c.Ball(ox, oy, oz, 2.1f + (float)R(30 + k) * .6f, .95f, 1.9f, Mat.Leaf, Mat.NeedleLight, .3);
                }
                break;
            }
            case MapModel.Oak:
            {
                float h = (5.2f + v % 3 * .7f) * tall, r = 3.1f + (float)R(2) * .9f;
                c.Rod(0, -2, 0, 0, h, 0, .72f, Mat.Trunk);
                c.Rod(0, h * .6f, 0, 1.8f, h + .8f, .6f, .34f, Mat.Trunk);
                c.Rod(0, h * .65f, 0, -1.6f, h + .6f, -.8f, .32f, Mat.Trunk);
                // the crown in lumps: a big one and four about it, lighter on top
                c.Ball(0, h + 1.6f, 0, r, r * .78f, r * .95f, Mat.Foliage, Mat.FoliageLight, .25);
                for (int k = 0; k < 2; k++)
                {
                    double a = k * Math.PI + R(5) * 3;
                    c.Ball((float)Math.Cos(a) * r * .7f, h + 1.2f + (float)R(40 + k) * 1.2f, (float)Math.Sin(a) * r * .7f, r * .6f, r * .52f, r * .6f, Mat.Foliage, Mat.FoliageLight, .35);
                }
                break;
            }
            case MapModel.Birch:
            {
                float h = (8 + v % 3) * tall;
                bool gold = v % 3 == 2;
                float lean = (float)(R(3) - .5) * .9f;
                c.Rod(0, -2, 0, lean, h, 0, .3f, Mat.Birch);
                byte leaf = gold ? Mat.FoliageGold : Mat.FoliageLight, leaf2 = gold ? Mat.FoliageLight : Mat.Foliage;
                c.Ball(lean, h - .4f, 0, 1.6f, 2.6f, 1.5f, leaf, leaf2, .25);
                c.Ball(lean + .9f, h - 2.4f, .4f, 1.3f, 1.7f, 1.2f, leaf, leaf2, .3);
                c.Ball(lean - .8f, h - 2.9f, -.3f, 1.2f, 1.5f, 1.2f, leaf, leaf2, .3);
                break;
            }
            case MapModel.DeadTree:
            case MapModel.Charred:
            {
                byte wood = m == MapModel.Charred ? Mat.Char : Mat.Snag;
                float h = (6.5f + v % 3) * tall;
                c.Rod(0, -2, 0, .3f, h, .2f, m == MapModel.Charred ? .5f : .4f, wood);
                for (int k = 0; k < 4; k++)
                {
                    double a = k * 2.1 + R(5) * 3;
                    float y0 = h * (.45f + k * .13f), len = 1.6f + (float)R(50 + k) * 1.4f;
                    c.Rod(.2f, y0, .1f, (float)Math.Cos(a) * len, y0 + len * .75f, (float)Math.Sin(a) * len, .17f, wood);
                }
                // a few embers still in the horde's burnt trees
                if (m == MapModel.Charred) c.Paint(-.6f, 0, -.6f, .6f, h * .5f, .6f, Mat.Ember, .06, 5);
                break;
            }
            case MapModel.Palm:
            {
                float h = (7.5f + v % 3) * tall;
                float bend = 1.4f + (float)R(3) * 1.4f;
                float px = 0, py = -2;
                for (int k = 0; k < 6; k++)
                {
                    float t = (k + 1) / 6f, nx = bend * t * t, ny = -2 + (h + 2) * t;
                    c.Rod(px, py, 0, nx, ny, 0, .42f - k * .03f, k % 2 == 0 ? Mat.PineBark : Mat.Trunk);
                    px = nx; py = ny;
                }
                for (int k = 0; k < 7; k++)
                {
                    double a = k * Math.Tau / 7 + R(6);
                    float ex = px + (float)Math.Cos(a) * 3.4f, ez = (float)Math.Sin(a) * 3.4f;
                    c.Rod(px, py + .3f, 0, (px + ex) / 2, py + .9f, ez / 2, .22f, Mat.FoliageLight);
                    c.Rod((px + ex) / 2, py + .9f, ez / 2, ex, py - .9f, ez, .2f, Mat.Foliage);
                }
                c.Ball(px, py - .2f, 0, .5f, .4f, .5f, Mat.Cask, rough: 0);
                break;
            }
            case MapModel.Willow:
            {
                float h = (4.5f + v % 3 * .6f) * tall;
                c.Rod(0, -2, 0, .4f, h, 0, .6f, Mat.Trunk);
                c.Ball(.4f, h + 1, 0, 3.6f, 1.8f, 3.4f, Mat.FoliageLight, Mat.Foliage, .3);
                // its hair hanging down
                for (int k = 0; k < 10; k++)
                {
                    double a = k * Math.Tau / 10 + R(7);
                    float r = 2.9f + (float)R(60 + k) * .6f, x = .4f + (float)Math.Cos(a) * r, z = (float)Math.Sin(a) * r;
                    float drop = 1.6f + (float)R(80 + k) * 2.2f;
                    c.Box(x - .25f, h + .6f - drop, z - .25f, x + .25f, h + 1.2f, z + .25f, k % 3 == 0 ? Mat.Foliage : Mat.FoliageLight);
                }
                break;
            }
            case MapModel.FruitTree:
            {
                float h = (3.6f + v % 3 * .4f) * tall;
                c.Rod(0, -2, 0, 0, h, 0, .4f, Mat.Trunk);
                c.Ball(0, h + 1.2f, 0, 2.2f, 1.8f, 2.1f, Mat.Foliage, Mat.FoliageLight, .3);
                c.Paint(-2.3f, h - .5f, -2.3f, 2.3f, h + 3, 2.3f, v % 2 == 0 ? Mat.BerryRed : Mat.BloomYellow, .07, 9);
                break;
            }
            case MapModel.Bush:
            {
                float r = 1.2f + (float)R(2) * .7f;
                c.Ball(0, r * .55f, 0, r, r * .75f, r * .9f, Mat.Foliage, Mat.Juniper, .3);
                c.Ball(r * .6f, r * .4f, .3f, r * .7f, r * .55f, r * .7f, Mat.FoliageLight, Mat.Foliage, .3);
                if (v % 6 == 0) c.Paint(-r, 0, -r, r * 1.4f, r * 1.4f, r, Mat.BerryRed, .05, 4);
                break;
            }
            case MapModel.Scrub:
            {
                for (int k = 0; k < 7; k++)
                {
                    double a = k * Math.Tau / 7 + R(3);
                    c.Rod(0, 0, 0, (float)Math.Cos(a) * 1.3f, 1 + (float)R(20 + k), (float)Math.Sin(a) * 1.3f, .14f, Mat.Snag);
                }
                c.Ball(0, .7f, 0, 1.1f, .6f, 1, Mat.GrassDry, Mat.Juniper, .3);
                break;
            }
            case MapModel.Rock:
            case MapModel.MossRock:
            case MapModel.SnowRock:
            case MapModel.Boulder:
            {
                float s = m == MapModel.Boulder ? 2.4f : 1.2f + (float)R(2) * .6f;
                byte top = m == MapModel.MossRock ? Mat.Moss : m == MapModel.SnowRock ? Mat.Snow : Mat.StoneLight;
                c.Ball(0, s * .3f, 0, s * 1.15f, s * .85f, s, Mat.Rock, Mat.StoneDark, .35, .3f);
                c.Ball(s * .7f, s * .1f, s * .4f, s * .6f, s * .55f, s * .55f, Mat.Rock, Mat.StoneDark, .3, .3f);
                // whatever lies on its top: moss, snow, a lighter weathering
                c.Paint(-s * 2, s * .55f, -s * 2, s * 2, s * 2, s * 2, top, m == MapModel.Rock || m == MapModel.Boulder ? .4 : .9, 2);
                break;
            }
            case MapModel.Flowers:
            {
                byte[] blooms = { Mat.BloomRed, Mat.BloomYellow, Mat.BloomWhite, Mat.BloomBlue };
                byte b = blooms[v % 4], b2 = blooms[(v + 1 + (int)(R(3) * 2)) % 4];
                for (int k = 0; k < 14; k++)
                {
                    float x = (float)(R(10 + k) - .5) * 3.2f, z = (float)(R(30 + k) - .5) * 2.6f;
                    c.Box(x - .25f, 0, z - .25f, x + .25f, .5f, z + .25f, Mat.GrassLush);
                    c.Box(x - .25f, .5f, z - .25f, x + .25f, 1, z + .25f, k % 4 == 0 ? b2 : b);
                }
                break;
            }
            case MapModel.Reeds:
            {
                for (int k = 0; k < 13; k++)
                {
                    float x = (float)(R(10 + k) - .5) * 2.6f, z = (float)(R(30 + k) - .5) * 2.2f, h = 2 + (float)R(50 + k) * 1.8f;
                    c.Box(x - .25f, -1.5f, z - .25f, x + .25f, h, z + .25f, Mat.Reedbed);
                    if (k % 3 == 0) c.Box(x - .25f, h, z - .25f, x + .25f, h + .7f, z + .25f, Mat.Trunk);
                }
                break;
            }
            case MapModel.Lily:
            {
                for (int k = 0; k < 5; k++)
                {
                    float x = (float)(R(10 + k) - .5) * 3, z = (float)(R(30 + k) - .5) * 3;
                    c.Cyl(x, z, .55f + (float)R(40 + k) * .3f, 0, .5f, Mat.GrassLush);
                    if (k % 2 == 0) c.Box(x - .25f, .5f, z - .25f, x + .25f, 1, z + .25f, k % 4 == 0 ? Mat.BloomRed : Mat.BloomWhite);
                }
                break;
            }
            case MapModel.Stump:
            {
                c.Cyl(0, 0, .75f, -1.5f, .9f, Mat.Trunk);
                c.Cyl(0, 0, .55f, .9f, 1.1f, Mat.Lumber);
                c.Rod(.5f, 0, 0, 1.3f, -.2f, .4f, .22f, Mat.Trunk);
                break;
            }
        }
    }

    /// <summary>A spruce: a trunk under tiers of needles, each wider than the one over it and its rim hanging a little, a spike at its top; snow on every tier's top if it stands in snow.</summary>
    static void Spruce(ModelCanvas c, float h, float r, byte needle, byte light, bool snow)
    {
        c.Rod(0, -2, 0, 0, h * .4f, 0, .38f, Mat.PineBark);
        int tiers = 4 + (int)(h / 6);
        float y = h * .16f;
        for (int k = 0; k < tiers; k++)
        {
            float t = k / (float)(tiers - 1), rr = r * (1 - t * .82f) + .35f, th = (h - y) / (tiers - k) * 1.55f;
            c.Cone(0, 0, rr, y, th, needle, .2f, light, .22);
            y += (h - h * .16f) / tiers;
        }
        c.Box(-.25f, h - .2f, -.25f, .25f, h + 1.2f, .25f, needle);
        if (snow)
        {
            // snow lies on the tops of the tiers: the highest voxel of every column of needles white
            var (ox, oy, oz) = c.Origin;
            for (int l = -(int)(r * c.K) - 2; l <= (int)(r * c.K) + 2; l++)
                for (int i = -(int)(r * c.K) - 2; i <= (int)(r * c.K) + 2; i++)
                {
                    bool under = false;
                    for (int j = (int)((h + 2) * c.K); j > 0; j--)
                    {
                        byte b = c.Get(i, j, l);
                        if (b == Mat.Air) { under = false; continue; }
                        if (!under && (b == needle || b == light)) c.Set(i, j, l, Mat.Snow);
                        under = true;
                    }
                }
        }
    }
}
