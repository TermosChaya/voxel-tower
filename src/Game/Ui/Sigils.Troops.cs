using System;
using System.Collections.Generic;
using Godot;

namespace VoxelTower.Game;

/// <summary>
/// The sigils of the troop's page of the lore tree: the arms each kind of ours bears, the beasts they ride and run with,
/// the halls they sleep and drill in, and the great ones of the hills, in the same chased bronze as the rest.
/// </summary>
public static partial class Icons
{
    /// <summary>A leaf-shaped blade along a slant from (ox, oy): from its socket at u0 to its point at u1, w at its widest.</summary>
    static void Leaf(CanvasItem c, Rect2 r, Color ink, float ox, float oy, float ang, float u0, float u1, float w)
    {
        float l = u1 - u0;
        Fill(c, ink, A(r, ox, oy, ang, u0, 0), A(r, ox, oy, ang, u0 + l * .2f, w * .75f), A(r, ox, oy, ang, u0 + l * .42f, w),
            A(r, ox, oy, ang, u0 + l * .7f, w * .6f), A(r, ox, oy, ang, u1, 0), A(r, ox, oy, ang, u0 + l * .7f, -w * .6f),
            A(r, ox, oy, ang, u0 + l * .42f, -w), A(r, ox, oy, ang, u0 + l * .2f, -w * .75f));
        c.DrawLine(A(r, ox, oy, ang, u0 + l * .12f, 0), A(r, ox, oy, ang, u1 - l * .16f, 0), Hollow(ink, .45f), 1.1f, true);
    }

    /// <summary>A sword from its pommel at (ox, oy) along ang, len long: steel to a point with its fuller, or a waster of wood, broad and blunt.</summary>
    static void Blade(CanvasItem c, Rect2 r, Color ink, float ox, float oy, float ang, float len, bool wood)
    {
        Line(c, Shade(ink, .7f), S(r, .06f), A(r, ox, oy, ang, .04f, 0), A(r, ox, oy, ang, .22f, 0));
        Dot(c, A(r, ox, oy, ang, .04f, 0), S(r, .06f), ink);
        if (wood)
        {
            var b = new List<Vector2> { A(r, ox, oy, ang, .22f, -.075f), A(r, ox, oy, ang, len - .08f, -.075f) };
            for (int i = 1; i < 6; i++) { float a = -MathF.PI / 2 + i * MathF.PI / 6; b.Add(A(r, ox, oy, ang, len - .08f + MathF.Cos(a) * .075f, MathF.Sin(a) * .075f)); }
            b.Add(A(r, ox, oy, ang, len - .08f, .075f));
            b.Add(A(r, ox, oy, ang, .22f, .075f));
            Fill(c, Shade(ink, .78f), b.ToArray());
            c.DrawLine(A(r, ox, oy, ang, .28f, .02f), A(r, ox, oy, ang, len - .1f, .02f), Hollow(ink, .45f), 1.1f, true);
            Line(c, ink, S(r, .075f), A(r, ox, oy, ang, .22f, -.15f), A(r, ox, oy, ang, .22f, .15f));
        }
        else
        {
            Fill(c, ink, A(r, ox, oy, ang, .22f, -.06f), A(r, ox, oy, ang, len - .14f, -.055f), A(r, ox, oy, ang, len, 0),
                A(r, ox, oy, ang, len - .14f, .055f), A(r, ox, oy, ang, .22f, .06f));
            c.DrawLine(A(r, ox, oy, ang, .26f, 0), A(r, ox, oy, ang, len - .2f, 0), Hollow(ink, .5f), 1.1f, true);
            Line(c, Shade(ink, .9f), S(r, .06f), A(r, ox, oy, ang, .25f, -.17f), A(r, ox, oy, ang, .21f, -.1f), A(r, ox, oy, ang, .21f, .1f), A(r, ox, oy, ang, .25f, .17f));
        }
    }

    // ---- the foot ----

    /// <summary>A long spear slanting up, its leaf-shaped head on a bound socket and a spike at its butt.</summary>
    public static void Spear(CanvasItem c, Rect2 r, Color ink)
    {
        const float ang = -MathF.PI / 4, ox = .19f, oy = .81f;
        Line(c, Shade(ink, .78f), S(r, .065f), A(r, ox, oy, ang, .03f, 0), A(r, ox, oy, ang, .6f, 0));
        Fill(c, Shade(ink, .9f), A(r, ox, oy, ang, -.02f, 0), A(r, ox, oy, ang, .07f, .04f), A(r, ox, oy, ang, .07f, -.04f));
        Line(c, Shade(ink, .55f), S(r, .095f), A(r, ox, oy, ang, .52f, 0), A(r, ox, oy, ang, .59f, 0));
        Leaf(c, r, ink, ox, oy, ang, .58f, .9f, .1f);
    }

    /// <summary>A pavise, the tall shield a man stands behind: a cross on its face and a boss where the arms meet.</summary>
    public static void Pavise(CanvasItem c, Rect2 r, Color ink)
    {
        var edge = new[] { P(r, .3f, .88f), P(r, .26f, .22f), P(r, .37f, .14f), P(r, .5f, .11f), P(r, .63f, .14f), P(r, .74f, .22f), P(r, .7f, .88f) };
        Fill(c, Shade(ink, .5f), edge);
        Line(c, ink, S(r, .055f), edge[0], edge[1], edge[2], edge[3], edge[4], edge[5], edge[6], edge[0]);
        Line(c, Shade(ink, .9f), S(r, .085f), P(r, .5f, .17f), P(r, .5f, .83f));
        Line(c, Shade(ink, .9f), S(r, .085f), P(r, .3f, .42f), P(r, .7f, .42f));
        Dot(c, P(r, .5f, .42f), S(r, .085f), Shade(ink, 1.1f));
    }

    /// <summary>A halberd's head on its shaft: the axe blade, the spike over it and the hook behind.</summary>
    public static void Halberd(CanvasItem c, Rect2 r, Color ink)
    {
        Line(c, Shade(ink, .78f), S(r, .065f), P(r, .46f, .94f), P(r, .46f, .22f));
        Fill(c, ink, P(r, .42f, .27f), P(r, .46f, .05f), P(r, .5f, .27f));
        Fill(c, ink, P(r, .48f, .2f), P(r, .62f, .2f), P(r, .74f, .12f), P(r, .8f, .24f), P(r, .82f, .36f), P(r, .78f, .5f), P(r, .64f, .44f), P(r, .48f, .42f));
        c.DrawCircle(P(r, .63f, .31f), S(r, .045f), Hollow(ink), true, -1, true);
        Fill(c, Shade(ink, .85f), P(r, .45f, .25f), P(r, .32f, .27f), P(r, .17f, .4f), P(r, .3f, .34f), P(r, .45f, .35f));
        Line(c, Shade(ink, .5f), S(r, .09f), P(r, .46f, .46f), P(r, .46f, .51f));
    }

    /// <summary>A wall of shields: three round ones overlapping in a row, a spear's point over each.</summary>
    public static void ShieldWall(CanvasItem c, Rect2 r, Color ink)
    {
        foreach (var (x, top) in new[] { (.26f, .16f), (.5f, .08f), (.74f, .16f) })
        {
            Line(c, Shade(ink, .7f), S(r, .04f), P(r, x, .44f), P(r, x, top + .08f));
            Fill(c, Shade(ink, .9f), P(r, x - .04f, top + .1f), P(r, x, top), P(r, x + .04f, top + .1f));
        }
        foreach (float x in new[] { .26f, .74f, .5f })
        {
            var at = P(r, x, .58f);
            Dot(c, at, S(r, .19f), Shade(ink, x == .5f ? .62f : .5f));
            c.DrawArc(at, S(r, .16f), 0, MathF.Tau, 32, x == .5f ? ink : Shade(ink, .85f), S(r, .05f), true);
            Dot(c, at, S(r, .055f), Shade(ink, 1.1f));
        }
    }

    /// <summary>Two long spears crossed, heads up.</summary>
    public static void Pikes(CanvasItem c, Rect2 r, Color ink)
    {
        foreach (var (ox, ang, k) in new[] { (.81f, -3 * MathF.PI / 4, .8f), (.19f, -MathF.PI / 4, 1f) })
        {
            Line(c, Shade(ink, .75f * k), S(r, .055f), A(r, ox, .81f, ang, 0, 0), A(r, ox, .81f, ang, .62f, 0));
            Leaf(c, r, Shade(ink, k), ox, .81f, ang, .6f, .9f, .075f);
        }
    }

    /// <summary>A knight's great helm, flat on top: the slit to see through, the holes to breathe by, a crest above.</summary>
    public static void GreatHelm(CanvasItem c, Rect2 r, Color ink)
    {
        Fill(c, Shade(ink, .7f), P(r, .45f, .21f), P(r, .43f, .11f), P(r, .52f, .04f), P(r, .66f, .06f), P(r, .77f, .15f), P(r, .66f, .13f), P(r, .58f, .15f), P(r, .56f, .21f));
        Fill(c, ink, P(r, .27f, .2f), P(r, .73f, .2f), P(r, .77f, .3f), P(r, .76f, .62f), P(r, .72f, .84f), P(r, .5f, .9f), P(r, .28f, .84f), P(r, .24f, .62f), P(r, .23f, .3f));
        Fill(c, Shade(ink, .7f), P(r, .5f, .2f), P(r, .73f, .2f), P(r, .77f, .3f), P(r, .76f, .62f), P(r, .72f, .84f), P(r, .5f, .9f));
        c.DrawRect(new Rect2(P(r, .25f, .39f), new Vector2(S(r, .5f), S(r, .075f))), Hollow(ink), true);
        Line(c, Shade(ink, 1.05f), S(r, .07f), P(r, .5f, .2f), P(r, .5f, .89f));
        foreach (float y in new[] { .58f, .68f, .78f })
            foreach (float x in new[] { .59f, .67f })
                c.DrawCircle(P(r, x, y), S(r, .028f), Hollow(ink), true, -1, true);
    }

    // ---- the shot ----

    /// <summary>A sling at the height of its swing: the loop on the finger, two cords, the pouch with its stone, the arc it whirls along.</summary>
    public static void Sling(CanvasItem c, Rect2 r, Color ink)
    {
        const float ox = .25f, oy = .25f, ang = MathF.PI / 4;
        // the sweep of the swing behind the stone, thinning away
        var sweep = new List<Vector2>();
        for (int i = 0; i <= 8; i++) { float t = i / 8f, a = ang + .3f + .65f * t; sweep.Add(P(r, ox + MathF.Cos(a) * (.57f + .08f * (1 - t)), oy + MathF.Sin(a) * (.57f + .08f * (1 - t)))); }
        for (int i = 8; i >= 0; i--) { float t = i / 8f, a = ang + .3f + .65f * t; sweep.Add(P(r, ox + MathF.Cos(a) * (.57f - .08f * (1 - t)), oy + MathF.Sin(a) * (.57f - .08f * (1 - t)))); }
        Fill(c, Shade(ink, .62f), sweep.ToArray());
        Line(c, Shade(ink, .85f), S(r, .045f), A(r, ox, oy, ang, .02f, 0), A(r, ox, oy, ang, .5f, -.16f));
        Line(c, Shade(ink, .85f), S(r, .045f), A(r, ox, oy, ang, .02f, 0), A(r, ox, oy, ang, .5f, .16f));
        Dot(c, P(r, ox, oy), S(r, .05f), ink);
        Fill(c, Shade(ink, .7f), A(r, ox, oy, ang, .47f, -.18f), A(r, ox, oy, ang, .6f, -.15f), A(r, ox, oy, ang, .68f, 0), A(r, ox, oy, ang, .6f, .15f),
            A(r, ox, oy, ang, .47f, .18f), A(r, ox, oy, ang, .55f, 0));
        Dot(c, A(r, ox, oy, ang, .57f, 0), S(r, .11f), Shade(ink, 1.1f));
    }

    /// <summary>A crossbow pointing up: the stock, the bow across it drawn back to the nut, a bolt in the groove.</summary>
    public static void Crossbow(CanvasItem c, Rect2 r, Color ink)
    {
        Fill(c, Shade(ink, .62f), P(r, .45f, .24f), P(r, .55f, .24f), P(r, .56f, .64f), P(r, .61f, .9f), P(r, .39f, .9f), P(r, .44f, .64f));
        Line(c, Shade(ink, .55f), S(r, .045f), P(r, .55f, .66f), P(r, .64f, .78f));
        Line(c, Shade(ink, .8f), S(r, .03f), P(r, .12f, .42f), P(r, .5f, .58f), P(r, .88f, .42f));
        Line(c, ink, S(r, .075f), P(r, .12f, .42f), P(r, .3f, .3f), P(r, .5f, .26f), P(r, .7f, .3f), P(r, .88f, .42f));
        Line(c, Shade(ink, 1.05f), S(r, .04f), P(r, .5f, .58f), P(r, .5f, .16f));
        Fill(c, Shade(ink, 1.1f), P(r, .44f, .19f), P(r, .5f, .06f), P(r, .56f, .19f));
    }

    /// <summary>A sling's lead bullet, almond-shaped, the lines of its speed streaming behind it.</summary>
    public static void LeadShot(CanvasItem c, Rect2 r, Color ink)
    {
        const float ang = -MathF.PI / 4, ox = .6f, oy = .4f;
        foreach (var (v, u0, u1) in new[] { (-.12f, -.26f, -.46f), (0f, -.36f, -.56f), (.12f, -.26f, -.46f) })
            Line(c, Shade(ink, .6f), S(r, .05f), A(r, ox, oy, ang, u0, v), A(r, ox, oy, ang, u1, v));
        // the almond: its two points once each, its sides between them
        const int n = 14;
        var upper = new List<Vector2>();
        var lower = new List<Vector2>();
        for (int i = 1; i < n; i++)
        {
            float t = i / (float)n, u = -.3f + .6f * t, w = .16f * MathF.Pow(MathF.Sin(MathF.PI * t), .75f);
            upper.Add(A(r, ox, oy, ang, u, -w));
            lower.Add(A(r, ox, oy, ang, u, w));
        }
        var back = A(r, ox, oy, ang, -.3f, 0);
        var front = A(r, ox, oy, ang, .3f, 0);
        var shot = new List<Vector2> { back };
        shot.AddRange(upper);
        shot.Add(front);
        lower.Reverse();
        shot.AddRange(lower);
        Fill(c, Shade(ink, .62f), shot.ToArray());
        var lit = new List<Vector2> { back };
        lit.AddRange(upper);
        lit.Add(front);
        c.DrawColoredPolygon(lit.ToArray(), ink);
        Line(c, Shade(ink, 1.2f), S(r, .03f), A(r, ox, oy, ang, -.12f, -.09f), A(r, ox, oy, ang, .12f, -.09f));
    }

    /// <summary>A narrow bodkin bolt driven clean through two plates of armour.</summary>
    public static void Bodkin(CanvasItem c, Rect2 r, Color ink)
    {
        foreach (float x in new[] { .27f, .55f })
        {
            Fill(c, Shade(ink, .78f), P(r, x, .22f), P(r, x + .14f, .3f), P(r, x + .14f, .84f), P(r, x, .76f));
            Line(c, Shade(ink, .45f), S(r, .035f), P(r, x + .14f, .3f), P(r, x + .14f, .84f));
            var hole = Hollow(ink, .75f);
            c.DrawCircle(P(r, x + .07f, .54f), S(r, .05f), hole, true, -1, true);
            foreach (var (dx, dy) in new[] { (-.06f, -.1f), (.05f, -.12f), (-.05f, .1f), (.06f, .11f) })
                c.DrawLine(P(r, x + .07f, .54f), P(r, x + .07f + dx, .54f + dy), hole, Math.Max(1f, S(r, .018f)), true);
        }
        foreach (var (x0, x1) in new[] { (.1f, .31f), (.41f, .59f), (.69f, .79f) })
            Line(c, Shade(ink, 1.05f), S(r, .05f), P(r, x0, .54f), P(r, x1, .54f));
        Fill(c, Shade(ink, 1.1f), P(r, .76f, .505f), P(r, .94f, .54f), P(r, .76f, .575f));
        foreach (int s in new[] { -1, 1 })
            Fill(c, Shade(ink, .8f), P(r, .07f, .54f), P(r, .2f, .54f), P(r, .16f, .54f + s * .08f), P(r, .06f, .54f + s * .08f));
    }

    // ---- the fire ----

    /// <summary>A round clay pot of fire, a rag stuffed in its neck and burning.</summary>
    public static void FirePot(CanvasItem c, Rect2 r, Color ink)
    {
        Dot(c, P(r, .5f, .65f), S(r, .26f), Shade(ink, .6f));
        c.DrawCircle(P(r, .47f, .62f), S(r, .21f), Shade(ink, .82f), true, -1, true);
        c.DrawCircle(P(r, .41f, .56f), S(r, .06f), Shade(ink, 1.05f), true, -1, true);
        Line(c, Shade(ink, .45f), S(r, .03f), Ring(r, .5f, .66f, .25f, .07f, .1f, MathF.PI - .1f, 10));
        Fill(c, Shade(ink, .7f), P(r, .41f, .36f), P(r, .59f, .36f), P(r, .6f, .44f), P(r, .4f, .44f));
        Line(c, ink, S(r, .05f), P(r, .38f, .36f), P(r, .62f, .36f));
        Fill(c, Shade(ink, .55f), P(r, .5f, .33f), P(r, .58f, .34f), P(r, .66f, .42f), P(r, .66f, .54f), P(r, .6f, .46f), P(r, .53f, .4f));
        Flame(c, Sub(r, .3f, .02f, .4f), ink);
    }

    /// <summary>A round iron bomb, its fuse lit and spitting sparks.</summary>
    public static void Bomb(CanvasItem c, Rect2 r, Color ink)
    {
        Dot(c, P(r, .44f, .6f), S(r, .3f), Shade(ink, .55f));
        c.DrawCircle(P(r, .41f, .57f), S(r, .25f), Shade(ink, .78f), true, -1, true);
        c.DrawCircle(P(r, .34f, .49f), S(r, .07f), Shade(ink, 1.1f), true, -1, true);
        Fill(c, Shade(ink, .45f), A(r, .6f, .41f, -MathF.PI / 4, -.04f, -.07f), A(r, .6f, .41f, -MathF.PI / 4, .08f, -.07f),
            A(r, .6f, .41f, -MathF.PI / 4, .08f, .07f), A(r, .6f, .41f, -MathF.PI / 4, -.04f, .07f));
        Line(c, Shade(ink, .75f), S(r, .035f), P(r, .65f, .36f), P(r, .7f, .3f), P(r, .71f, .24f), P(r, .7f, .2f));
        var spark = new Vector2[16];
        for (int i = 0; i < 16; i++)
        {
            float a = i * MathF.PI / 8, rad = i % 2 == 0 ? (i % 4 == 0 ? .11f : .08f) : .03f;
            spark[i] = P(r, .7f + MathF.Cos(a) * rad, .19f + MathF.Sin(a) * rad);
        }
        Fill(c, Shade(ink, 1.2f), spark);
    }

    // ---- the horse and the hound ----

    /// <summary>A war hound's head, snarling, its ears cropped and a collar of spikes round its neck.</summary>
    public static void Hound(CanvasItem c, Rect2 r, Color ink)
    {
        Fill(c, Shade(ink, .7f), P(r, .53f, .22f), P(r, .6f, .05f), P(r, .69f, .26f));
        Fill(c, Shade(ink, .9f), P(r, .08f, .43f), P(r, .16f, .37f), P(r, .32f, .32f), P(r, .42f, .22f), P(r, .56f, .18f), P(r, .68f, .24f), P(r, .76f, .38f),
            P(r, .82f, .6f), P(r, .8f, .84f), P(r, .52f, .86f), P(r, .5f, .66f), P(r, .4f, .6f), P(r, .3f, .61f), P(r, .18f, .6f), P(r, .13f, .56f), P(r, .2f, .54f),
            P(r, .34f, .53f), P(r, .1f, .5f));
        c.DrawColoredPolygon(new[] { P(r, .08f, .43f), P(r, .16f, .37f), P(r, .28f, .35f), P(r, .31f, .5f), P(r, .1f, .5f) }, Shade(ink, .6f));
        c.DrawColoredPolygon(new[] { P(r, .1f, .5f), P(r, .34f, .53f), P(r, .2f, .54f), P(r, .13f, .56f) }, Hollow(ink));
        Fill(c, Shade(ink, 1.2f), P(r, .14f, .5f), P(r, .19f, .505f), P(r, .16f, .57f));
        c.DrawCircle(P(r, .09f, .44f), S(r, .035f), Hollow(ink), true, -1, true);
        c.DrawCircle(P(r, .42f, .31f), S(r, .038f), Hollow(ink), true, -1, true);
        Line(c, Shade(ink, .4f), S(r, .1f), P(r, .5f, .72f), P(r, .82f, .66f));
        Fill(c, Shade(ink, 1.1f), P(r, .5f, .67f), P(r, .39f, .72f), P(r, .5f, .77f));
        Fill(c, Shade(ink, 1.1f), P(r, .82f, .61f), P(r, .93f, .63f), P(r, .82f, .71f));
        foreach (float x in new[] { .59f, .67f, .75f }) Dot(c, P(r, x, .7f - (x - .5f) * .19f), S(r, .028f), Shade(ink, 1.1f));
    }

    /// <summary>
    /// A horse's head facing left, turned up by tilt about the root of its neck and drawn k its size there, set at (ox, oy):
    /// bridled, its mane lying along the neck, or flung up with the mane flying.
    /// </summary>
    static void Horse(CanvasItem c, Rect2 r, Color ink, float tilt, float k, float ox, float oy, bool flung)
    {
        float cs = MathF.Cos(tilt), sn = MathF.Sin(tilt);
        Vector2 Q(float x, float y)
        {
            float dx = (x - .6f) * k, dy = (y - .8f) * k;
            return P(r, ox + dx * cs - dy * sn, oy + dx * sn + dy * cs);
        }
        Fill(c, Shade(ink, .6f), Q(.57f, .22f), Q(.65f, .07f), Q(.68f, .26f));
        if (flung)
            // the mane streaming back off the crest in waves
            Fill(c, Shade(ink, .55f), Q(.6f, .2f), Q(.72f, .16f), Q(.84f, .14f), Q(.84f, .26f), Q(.94f, .3f), Q(.92f, .44f), Q(.98f, .52f), Q(.92f, .62f),
                Q(.94f, .72f), Q(.84f, .82f), Q(.86f, .62f), Q(.82f, .44f), Q(.72f, .26f));
        else
            Fill(c, Shade(ink, .55f), Q(.6f, .2f), Q(.72f, .26f), Q(.82f, .44f), Q(.86f, .62f), Q(.84f, .82f), Q(.9f, .66f), Q(.9f, .5f), Q(.84f, .34f), Q(.72f, .2f));
        Fill(c, Shade(ink, .9f), Q(.5f, .2f), Q(.54f, .06f), Q(.62f, .2f), Q(.72f, .3f), Q(.8f, .48f), Q(.82f, .66f), Q(.8f, .84f), Q(.48f, .9f),
            Q(.5f, .7f), Q(.44f, .62f), Q(.36f, .66f), Q(.26f, .74f), Q(.16f, .74f), Q(.12f, .66f), Q(.14f, .58f), Q(.24f, .44f), Q(.38f, .28f));
        c.DrawCircle(Q(.42f, .35f), S(r, .038f * k), Hollow(ink), true, -1, true);
        c.DrawCircle(Q(.17f, .64f), S(r, .028f * k), Hollow(ink), true, -1, true);
        if (flung)
        {
            c.DrawPolyline(new[] { Q(.13f, .7f), Q(.22f, .69f), Q(.26f, .72f) }, Hollow(ink, .7f), Math.Max(1.2f, S(r, .03f * k)), true);
            return;
        }
        var strap = Hollow(ink, .75f);
        float w = Math.Max(1.2f, S(r, .035f * k));
        c.DrawPolyline(new[] { Q(.25f, .47f), Q(.3f, .6f), Q(.3f, .72f) }, strap, w, true);
        c.DrawPolyline(new[] { Q(.29f, .62f), Q(.42f, .5f), Q(.53f, .24f) }, strap, w, true);
        c.DrawPolyline(new[] { Q(.47f, .26f), Q(.58f, .23f) }, strap, w, true);
        Line(c, Shade(ink, .5f), S(r, .035f * k), Q(.27f, .66f), Q(.44f, .78f), Q(.62f, .76f));
        Dot(c, Q(.27f, .66f), S(r, .04f * k), Shade(ink, 1.15f));
    }

    /// <summary>A horse's head, bridled: the bit at its mouth, the cheekpiece up to the poll, the rein hanging.</summary>
    public static void Steed(CanvasItem c, Rect2 r, Color ink) => Horse(c, r, ink, 0, 1, .6f, .8f, false);

    /// <summary>A hound at full stretch, running down its quarry.</summary>
    public static void Coursing(CanvasItem c, Rect2 r, Color ink)
    {
        var leg = Shade(ink, .75f);
        Line(c, leg, S(r, .045f), P(r, .34f, .54f), P(r, .24f, .64f), P(r, .12f, .7f));
        Line(c, leg, S(r, .045f), P(r, .7f, .46f), P(r, .8f, .6f), P(r, .9f, .66f));
        Line(c, Shade(ink, .7f), S(r, .04f), P(r, .8f, .4f), P(r, .87f, .33f), P(r, .9f, .27f));
        Fill(c, ink, P(r, .3f, .32f), P(r, .44f, .36f), P(r, .6f, .36f), P(r, .72f, .34f), P(r, .8f, .4f), P(r, .8f, .48f), P(r, .62f, .48f), P(r, .48f, .5f),
            P(r, .34f, .56f), P(r, .26f, .5f), P(r, .24f, .42f));
        Fill(c, ink, P(r, .3f, .32f), P(r, .26f, .24f), P(r, .18f, .26f), P(r, .08f, .34f), P(r, .1f, .38f), P(r, .22f, .4f), P(r, .24f, .42f));
        Fill(c, Shade(ink, .7f), P(r, .25f, .25f), P(r, .35f, .2f), P(r, .3f, .3f));
        Line(c, ink, S(r, .045f), P(r, .3f, .52f), P(r, .2f, .6f), P(r, .08f, .62f));
        Line(c, ink, S(r, .045f), P(r, .74f, .46f), P(r, .84f, .56f), P(r, .94f, .58f));
        c.DrawCircle(P(r, .2f, .3f), S(r, .025f), Hollow(ink), true, -1, true);
    }

    /// <summary>A horse's head, a bow behind it: the riders who shoot from the saddle.</summary>
    public static void HorseArcher(CanvasItem c, Rect2 r, Color ink)
    {
        var bow = Ring(r, .62f, .62f, .52f, .52f, MathF.PI, MathF.PI * 1.5f, 14);
        Line(c, Shade(ink, .7f), S(r, .025f), bow[0], bow[^1]);
        Line(c, ink, S(r, .065f), bow);
        Dot(c, bow[0], S(r, .035f), ink);
        Dot(c, bow[^1], S(r, .035f), ink);
        Steed(c, Sub(r, .22f, .2f, .7f), ink);
    }

    /// <summary>A lance couched for the charge, its guard at the grip, the rush of the ride behind it.</summary>
    public static void Couched(CanvasItem c, Rect2 r, Color ink)
    {
        const float ang = -.22f, ox = .12f, oy = .54f;
        foreach (var (u0, u1, v) in new[] { (.04f, .22f, -.2f), (.14f, .42f, .2f), (.12f, .34f, .3f) })
            Line(c, Shade(ink, .55f), S(r, .045f), A(r, ox, oy, ang, u0, v), A(r, ox, oy, ang, u1, v));
        Line(c, Shade(ink, .75f), S(r, .065f), A(r, ox, oy, ang, 0, 0), A(r, ox, oy, ang, .2f, 0));
        Fill(c, ink, A(r, ox, oy, ang, .2f, -.08f), A(r, ox, oy, ang, .8f, 0), A(r, ox, oy, ang, .2f, .08f));
        c.DrawLine(A(r, ox, oy, ang, .26f, -.035f), A(r, ox, oy, ang, .66f, -.01f), Shade(ink, 1.2f), Math.Max(1f, S(r, .02f)), true);
        Fill(c, Shade(ink, .72f), A(r, ox, oy, ang, .1f, -.18f), A(r, ox, oy, ang, .26f, -.08f), A(r, ox, oy, ang, .26f, .08f), A(r, ox, oy, ang, .1f, .18f));
        Line(c, ink, S(r, .045f), A(r, ox, oy, ang, .1f, -.18f), A(r, ox, oy, ang, .1f, .18f));
    }

    /// <summary>A lance upright, a swallow-tailed pennon streaming from under its head.</summary>
    public static void Pennon(CanvasItem c, Rect2 r, Color ink)
    {
        const float ang = -MathF.PI / 2 + .3f, ox = .3f, oy = .92f;
        Line(c, Shade(ink, .78f), S(r, .06f), A(r, ox, oy, ang, 0, 0), A(r, ox, oy, ang, .74f, 0));
        Leaf(c, r, ink, ox, oy, ang, .72f, .88f, .055f);
        Fill(c, Shade(ink, .9f), A(r, ox, oy, ang, .7f, .02f), A(r, ox, oy, ang, .7f, .18f), A(r, ox, oy, ang, .66f, .3f), A(r, ox, oy, ang, .68f, .44f),
            A(r, ox, oy, ang, .58f, .32f), A(r, ox, oy, ang, .5f, .42f), A(r, ox, oy, ang, .52f, .28f), A(r, ox, oy, ang, .52f, .14f), A(r, ox, oy, ang, .54f, .02f));
        c.DrawLine(A(r, ox, oy, ang, .62f, .06f), A(r, ox, oy, ang, .6f, .26f), Hollow(ink, .5f), 1.2f, true);
    }

    // ---- the household ----

    /// <summary>A long timber house: the gable with its door and the crossed boards at its peak, the long side behind.</summary>
    public static void Longhouse(CanvasItem c, Rect2 r, Color ink)
    {
        Fill(c, Shade(ink, .58f), P(r, .46f, .52f), P(r, .86f, .45f), P(r, .86f, .7f), P(r, .46f, .78f));
        foreach (float f in new[] { .3f, .6f })
            c.DrawLine(P(r, .46f, .52f + .26f * f), P(r, .86f, .45f + .25f * f), Hollow(ink, .5f), 1.2f, true);
        Fill(c, ink, P(r, .3f, .3f), P(r, .7f, .22f), P(r, .9f, .44f), P(r, .48f, .53f));
        foreach (float f in new[] { .35f, .7f })
            c.DrawLine(P(r, .3f + .18f * f, .3f + .23f * f), P(r, .7f + .2f * f, .22f + .22f * f), Hollow(ink, .4f), 1.1f, true);
        Fill(c, Shade(ink, .8f), P(r, .14f, .76f), P(r, .14f, .52f), P(r, .3f, .32f), P(r, .46f, .52f), P(r, .46f, .78f));
        var door = new List<Vector2> { P(r, .25f, .77f) };
        door.AddRange(Ring(r, .3f, .63f, .05f, .05f, MathF.PI, MathF.Tau, 6));
        door.Add(P(r, .35f, .77f));
        c.DrawColoredPolygon(door.ToArray(), Hollow(ink));
        Line(c, Shade(ink, 1.05f), S(r, .04f), P(r, .12f, .55f), P(r, .3f, .31f), P(r, .48f, .55f));
        Line(c, Shade(ink, 1.05f), S(r, .035f), P(r, .24f, .37f), P(r, .37f, .2f));
        Line(c, Shade(ink, 1.05f), S(r, .035f), P(r, .36f, .37f), P(r, .23f, .2f));
    }

    /// <summary>Two wooden practice swords crossed: the drill yard's.</summary>
    public static void Wasters(CanvasItem c, Rect2 r, Color ink)
    {
        Blade(c, r, Shade(ink, .85f), .84f, .84f, -3 * MathF.PI / 4, .9f, true);
        Blade(c, r, ink, .16f, .84f, -MathF.PI / 4, .9f, true);
    }

    /// <summary>A bunk of two tiers: a thick straw mattress on each, a pillow at its head and a blanket over its foot.</summary>
    public static void Bunk(CanvasItem c, Rect2 r, Color ink)
    {
        Line(c, Shade(ink, .7f), S(r, .055f), P(r, .19f, .2f), P(r, .19f, .84f));
        Line(c, Shade(ink, .7f), S(r, .055f), P(r, .81f, .3f), P(r, .81f, .84f));
        Dot(c, P(r, .19f, .18f), S(r, .045f), Shade(ink, .8f));
        foreach (float y in new[] { .24f, .58f })
        {
            Line(c, Shade(ink, .55f), S(r, .05f), P(r, .19f, y + .2f), P(r, .81f, y + .2f));
            Fill(c, Shade(ink, .95f), P(r, .22f, y + .06f), P(r, .8f, y + .06f), P(r, .8f, y + .18f), P(r, .22f, y + .18f));
            Fill(c, Shade(ink, .62f), P(r, .47f, y + .03f), P(r, .8f, y + .04f), P(r, .8f, y + .18f), P(r, .45f, y + .18f));
            Dot(c, P(r, .33f, y + .045f), S(r, .07f), Shade(ink, 1.15f));
        }
    }

    // ---- the great ones ----

    /// <summary>A troll's head: the low brow, the little eyes, the tusks up out of its jaw and an iron collar of chain.</summary>
    public static void Troll(CanvasItem c, Rect2 r, Color ink)
    {
        Fill(c, Shade(ink, .7f), P(r, .2f, .36f), P(r, .07f, .28f), P(r, .18f, .48f));
        Fill(c, Shade(ink, .7f), P(r, .8f, .36f), P(r, .93f, .28f), P(r, .82f, .48f));
        Fill(c, Shade(ink, .88f), P(r, .3f, .18f), P(r, .5f, .12f), P(r, .7f, .18f), P(r, .8f, .34f), P(r, .82f, .54f), P(r, .74f, .72f), P(r, .5f, .8f),
            P(r, .26f, .72f), P(r, .18f, .54f), P(r, .2f, .34f));
        Line(c, Shade(ink, .45f), S(r, .07f), P(r, .26f, .36f), P(r, .5f, .4f), P(r, .74f, .36f));
        foreach (float x in new[] { .38f, .62f })
        {
            c.DrawCircle(P(r, x, .46f), S(r, .05f), Hollow(ink), true, -1, true);
            c.DrawCircle(P(r, x, .46f), S(r, .022f), Shade(ink, 1.2f), true, -1, true);
        }
        Fill(c, Shade(ink, .62f), P(r, .46f, .44f), P(r, .54f, .44f), P(r, .58f, .56f), P(r, .42f, .56f));
        c.DrawPolyline(new[] { P(r, .3f, .64f), P(r, .5f, .68f), P(r, .7f, .64f) }, Hollow(ink), Math.Max(1.2f, S(r, .04f)), true);
        foreach (int s in new[] { -1, 1 })
            Fill(c, Shade(ink, 1.15f), P(r, .5f + s * .1f, .67f), P(r, .5f + s * .17f, .52f), P(r, .5f + s * .2f, .65f));
        for (int i = 0; i < 5; i++)
        {
            float x = .3f + i * .1f, y = .86f - MathF.Abs(x - .5f) * .1f;
            if (i % 2 == 0) c.DrawArc(P(r, x, y), S(r, .05f), 0, MathF.Tau, 12, Shade(ink, .8f), S(r, .035f), true);
            else Line(c, Shade(ink, .8f), S(r, .04f), P(r, x - .04f, y), P(r, x + .04f, y));
        }
    }

    /// <summary>A giant's maul: a great iron-bound block of a head on a haft thick as a man's leg.</summary>
    public static void Maul(CanvasItem c, Rect2 r, Color ink)
    {
        const float ang = -MathF.PI / 4, ox = .2f, oy = .8f;
        Line(c, Shade(ink, .72f), S(r, .1f), A(r, ox, oy, ang, 0, 0), A(r, ox, oy, ang, .54f, 0));
        foreach (float u in new[] { .04f, .1f, .16f })
            c.DrawLine(A(r, ox, oy, ang, u, -.05f), A(r, ox, oy, ang, u + .03f, .05f), Hollow(ink, .55f), 1.2f, true);
        Fill(c, ink, A(r, ox, oy, ang, .48f, -.2f), A(r, ox, oy, ang, .52f, -.24f), A(r, ox, oy, ang, .72f, -.24f), A(r, ox, oy, ang, .76f, -.2f),
            A(r, ox, oy, ang, .76f, .2f), A(r, ox, oy, ang, .72f, .24f), A(r, ox, oy, ang, .52f, .24f), A(r, ox, oy, ang, .48f, .2f));
        Fill(c, Shade(ink, .7f), A(r, ox, oy, ang, .48f, .04f), A(r, ox, oy, ang, .76f, .04f), A(r, ox, oy, ang, .76f, .2f), A(r, ox, oy, ang, .72f, .24f),
            A(r, ox, oy, ang, .52f, .24f), A(r, ox, oy, ang, .48f, .2f));
        foreach (float u in new[] { .54f, .7f })
            Line(c, Shade(ink, .45f), S(r, .04f), A(r, ox, oy, ang, u, -.24f), A(r, ox, oy, ang, u, .24f));
    }

    /// <summary>A boulder hurled along its arc, the dust it was thrown from still hanging behind.</summary>
    public static void Hurled(CanvasItem c, Rect2 r, Color ink)
    {
        foreach (var (x, y, rad) in new[] { (.2f, .76f, .065f), (.29f, .8f, .055f), (.24f, .68f, .05f) })
            Dot(c, P(r, x, y), S(r, rad), Shade(ink, .45f));
        foreach (var (d, a1, k) in new[] { (0f, 1.42f, .65f), (.08f, 1.38f, .5f), (-.08f, 1.4f, .5f) })
            Line(c, Shade(ink, k), S(r, d == 0 ? .05f : .035f), Ring(r, .64f, .86f, .44f + d, .5f + d, MathF.PI + .3f, MathF.PI * a1, 8));
        Rock(c, r, ink, .63f, .4f, .22f, .5f);
    }

    // ---- the branches and the page ----

    /// <summary>A heater shield parted down the middle, a spear standing behind it: the foot.</summary>
    public static void Infantry(CanvasItem c, Rect2 r, Color ink)
    {
        const float ang = -MathF.PI / 4 - .05f, ox = .16f, oy = .9f;
        Line(c, Shade(ink, .75f), S(r, .06f), A(r, ox, oy, ang, 0, 0), A(r, ox, oy, ang, .66f, 0));
        Leaf(c, r, ink, ox, oy, ang, .7f, .98f, .09f);
        var shield = new[] { P(r, .16f, .36f), P(r, .6f, .36f), P(r, .6f, .6f), P(r, .54f, .75f), P(r, .38f, .88f), P(r, .22f, .75f), P(r, .16f, .6f) };
        Fill(c, Shade(ink, .5f), shield);
        c.DrawColoredPolygon(new[] { P(r, .38f, .36f), P(r, .6f, .36f), P(r, .6f, .6f), P(r, .54f, .75f), P(r, .38f, .88f) }, Shade(ink, .78f));
        Line(c, ink, S(r, .05f), shield[0], shield[1], shield[2], shield[3], shield[4], shield[5], shield[6], shield[0]);
    }

    /// <summary>A crossbow over two bolts crossed: the shot.</summary>
    public static void Marksmen(CanvasItem c, Rect2 r, Color ink)
    {
        foreach (var (ox, ang) in new[] { (.16f, -MathF.PI / 4), (.84f, -3 * MathF.PI / 4) })
        {
            Line(c, Shade(ink, .6f), S(r, .035f), A(r, ox, .84f, ang, 0, 0), A(r, ox, .84f, ang, .8f, 0));
            Fill(c, Shade(ink, .75f), A(r, ox, .84f, ang, .92f, 0), A(r, ox, .84f, ang, .78f, .05f), A(r, ox, .84f, ang, .78f, -.05f));
        }
        Crossbow(c, Sub(r, .12f, .14f, .76f), ink);
    }

    /// <summary>A horse's head flung up, the mouth open and the mane flying: the riders.</summary>
    public static void Rearing(CanvasItem c, Rect2 r, Color ink) => Horse(c, r, ink, .35f, .82f, .54f, .8f, true);

    /// <summary>A giant's bare footprint, broad as a cart: the sole and its five toes.</summary>
    public static void Footprint(CanvasItem c, Rect2 r, Color ink)
    {
        Fill(c, ink, P(r, .42f, .92f), P(r, .58f, .92f), P(r, .67f, .8f), P(r, .7f, .6f), P(r, .68f, .42f), P(r, .6f, .33f), P(r, .42f, .32f), P(r, .33f, .4f),
            P(r, .36f, .56f), P(r, .41f, .68f), P(r, .36f, .82f));
        c.DrawColoredPolygon(new[] { P(r, .44f, .86f), P(r, .56f, .86f), P(r, .62f, .74f), P(r, .5f, .7f), P(r, .43f, .76f) }, Shade(ink, .72f));
        c.DrawColoredPolygon(new[] { P(r, .41f, .38f), P(r, .62f, .4f), P(r, .64f, .5f), P(r, .44f, .48f) }, Shade(ink, .8f));
        foreach (var (x, y, rad) in new[] { (.34f, .22f, .08f), (.49f, .17f, .062f), (.61f, .2f, .055f), (.7f, .27f, .048f), (.76f, .36f, .042f) })
            Dot(c, P(r, x, y), S(r, rad), ink);
    }

    /// <summary>Two swords crossed, points up: the troop's page.</summary>
    public static void CrossedSwords(CanvasItem c, Rect2 r, Color ink)
    {
        Blade(c, r, Shade(ink, .85f), .84f, .84f, -3 * MathF.PI / 4, .92f, false);
        Blade(c, r, ink, .16f, .84f, -MathF.PI / 4, .92f, false);
    }
}
