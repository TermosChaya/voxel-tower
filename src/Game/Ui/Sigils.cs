using System;
using System.Collections.Generic;
using Godot;

namespace VoxelTower.Game;

/// <summary>
/// The sigils of the lore tree: an engraved mark for every lore, in the same chased bronze as the tool belt's, so that
/// the tree reads by its marks and says its words only when asked.
/// </summary>
public static partial class Icons
{
    /// <summary>A sub-square of the icon: its corner and side as fractions of the whole.</summary>
    static Rect2 Sub(Rect2 r, float x, float y, float s) => new(P(r, x, y), new Vector2(S(r, s), S(r, s)));

    /// <summary>A point along a slant from (ox, oy) at angle ang: u along it, v across it, all in fractions of the square.</summary>
    static Vector2 A(Rect2 r, float ox, float oy, float ang, float u, float v)
    {
        float cs = MathF.Cos(ang), sn = MathF.Sin(ang);
        return P(r, ox + u * cs - v * sn, oy + u * sn + v * cs);
    }

    /// <summary>An arrow from its nock at (ox, oy) along ang, len long: the shaft, a barbed head and the fletching.</summary>
    static void Arrow(CanvasItem c, Rect2 r, Color ink, float ox, float oy, float ang, float len, float w = .035f)
    {
        Line(c, Shade(ink, .85f), S(r, w), A(r, ox, oy, ang, .02f, 0), A(r, ox, oy, ang, len - .1f, 0));
        Fill(c, ink, A(r, ox, oy, ang, len, 0), A(r, ox, oy, ang, len - .14f, .065f), A(r, ox, oy, ang, len - .1f, 0), A(r, ox, oy, ang, len - .14f, -.065f));
        for (int s = -1; s <= 1; s += 2)
            Fill(c, Shade(ink, .62f), A(r, ox, oy, ang, 0, 0), A(r, ox, oy, ang, .16f, 0), A(r, ox, oy, ang, .1f, .065f * s), A(r, ox, oy, ang, -.02f, .065f * s));
    }

    /// <summary>Points round an ellipse from angle a0 to a1 (radians, 0 to the right, growing downward).</summary>
    static Vector2[] Ring(Rect2 r, float cx, float cy, float rx, float ry, float a0, float a1, int n)
    {
        var pts = new Vector2[n + 1];
        for (int i = 0; i <= n; i++)
        {
            float a = a0 + (a1 - a0) * i / n;
            pts[i] = P(r, cx + MathF.Cos(a) * rx, cy + MathF.Sin(a) * ry);
        }
        return pts;
    }

    static Color Water(Color ink) => new(.46f, .66f, .86f, ink.A);
    static Color Hollow(Color ink, float a = .8f) => new(0, 0, 0, a * ink.A);

    static void Drop(CanvasItem c, Rect2 r, float x, float y, float s, Color col) =>
        Fill(c, col, P(r, x, y - s), P(r, x + s * .55f, y + s * .25f), P(r, x + s * .35f, y + s * .62f), P(r, x, y + s * .75f),
            P(r, x - s * .35f, y + s * .62f), P(r, x - s * .55f, y + s * .25f));

    // ---- the bows ----

    /// <summary>A bow under a sprig of yew.</summary>
    public static void Yew(CanvasItem c, Rect2 r, Color ink)
    {
        Bow(c, Sub(r, 0, .12f, .86f), ink);
        var leaf = Shade(ink, .75f);
        Line(c, leaf, S(r, .03f), P(r, .54f, .32f), P(r, .94f, .06f));
        for (int i = 0; i < 4; i++)
        {
            float t = .18f + i * .2f;
            var b = P(r, .54f + .4f * t, .32f - .26f * t);
            Line(c, leaf, S(r, .026f), b, b + new Vector2(S(r, -.03f), S(r, -.1f)));
            Line(c, leaf, S(r, .026f), b, b + new Vector2(S(r, .09f), S(r, .04f)));
        }
    }

    /// <summary>A broad barbed arrowhead on the end of its shaft.</summary>
    public static void Barbs(CanvasItem c, Rect2 r, Color ink)
    {
        const float ang = -MathF.PI / 4;
        Line(c, Shade(ink, .8f), S(r, .06f), A(r, .14f, .86f, ang, 0, 0), A(r, .14f, .86f, ang, .56f, 0));
        Fill(c, ink, A(r, .14f, .86f, ang, .98f, 0), A(r, .14f, .86f, ang, .62f, .16f), A(r, .14f, .86f, ang, .46f, .22f),
            A(r, .14f, .86f, ang, .57f, .06f), A(r, .14f, .86f, ang, .57f, -.06f), A(r, .14f, .86f, ang, .46f, -.22f), A(r, .14f, .86f, ang, .62f, -.16f));
        Line(c, Shade(ink, .5f), S(r, .025f), A(r, .14f, .86f, ang, .94f, 0), A(r, .14f, .86f, ang, .6f, 0));
        for (int i = 0; i < 3; i++)
            Line(c, Shade(ink, .55f), S(r, .03f), A(r, .14f, .86f, ang, .44f + i * .04f, -.05f), A(r, .14f, .86f, ang, .44f + i * .04f, .05f));
    }

    /// <summary>A brazier on three legs with the fire in its bowl.</summary>
    public static void Brazier(CanvasItem c, Rect2 r, Color ink)
    {
        Line(c, Shade(ink, .7f), S(r, .05f), P(r, .3f, .64f), P(r, .18f, .95f));
        Line(c, Shade(ink, .7f), S(r, .05f), P(r, .7f, .64f), P(r, .82f, .95f));
        Line(c, Shade(ink, .55f), S(r, .05f), P(r, .5f, .7f), P(r, .5f, .93f));
        Flame(c, Sub(r, .25f, .02f, .5f), ink);
        var bowl = new List<Vector2> { P(r, .12f, .5f), P(r, .88f, .5f) };
        bowl.AddRange(Ring(r, .5f, .5f, .36f, .22f, 0, MathF.PI, 12));
        Fill(c, Shade(ink, .8f), bowl.ToArray());
        Line(c, ink, S(r, .05f), P(r, .1f, .5f), P(r, .9f, .5f));
    }

    /// <summary>Three arrows in flight side by side.</summary>
    public static void Volley(CanvasItem c, Rect2 r, Color ink)
    {
        const float ang = -MathF.PI / 4;
        Arrow(c, r, ink, .07f, .62f, ang, .78f);
        Arrow(c, r, ink, .19f, .77f, ang, .78f);
        Arrow(c, r, ink, .31f, .92f, ang, .78f);
    }

    /// <summary>An owl's face: the ear tufts, two great eyes and the hooked beak.</summary>
    public static void Owl(CanvasItem c, Rect2 r, Color ink)
    {
        Fill(c, Shade(ink, .58f), P(r, .14f, .12f), P(r, .34f, .28f), P(r, .66f, .28f), P(r, .86f, .12f), P(r, .88f, .58f), P(r, .72f, .84f),
            P(r, .5f, .94f), P(r, .28f, .84f), P(r, .12f, .58f));
        foreach (float x in new[] { .34f, .66f })
        {
            Dot(c, P(r, x, .5f), S(r, .16f), ink);
            c.DrawCircle(P(r, x, .5f), S(r, .075f), Hollow(ink, .9f), true, -1, true);
            c.DrawCircle(P(r, x - .025f, .47f), S(r, .022f), Shade(ink, 1.15f), true, -1, true);
        }
        Fill(c, Shade(ink, .92f), P(r, .45f, .62f), P(r, .55f, .62f), P(r, .5f, .78f));
    }

    /// <summary>A bow as tall as the square, an arrow on its string.</summary>
    public static void Longbow(CanvasItem c, Rect2 r, Color ink)
    {
        var arc = Ring(r, .26f, .5f, .3f, .47f, -MathF.PI / 2, MathF.PI / 2, 16);
        Line(c, ink, S(r, .065f), arc);
        c.DrawLine(arc[0], arc[^1], Shade(ink, .7f), 1.2f, true);
        Arrow(c, r, ink, .2f, .5f, 0, .78f);
        Dot(c, arc[0], S(r, .035f), ink);
        Dot(c, arc[^1], S(r, .035f), ink);
    }

    // ---- the masonry ----

    /// <summary>A tub of lime with a trowel standing in it.</summary>
    public static void MortarTub(CanvasItem c, Rect2 r, Color ink)
    {
        Fill(c, Shade(ink, .6f), P(r, .1f, .52f), P(r, .9f, .52f), P(r, .8f, .92f), P(r, .2f, .92f));
        c.DrawEllipse(P(r, .5f, .52f), S(r, .4f), S(r, .085f), Shade(ink, 1.1f), true, -1, true);
        foreach (float y in new[] { .66f, .82f }) Line(c, Shade(ink, .4f), S(r, .035f), P(r, .14f + (y - .52f) * .25f, y), P(r, .86f - (y - .52f) * .25f, y));
        Fill(c, ink, P(r, .48f, .54f), P(r, .6f, .3f), P(r, .8f, .2f), P(r, .7f, .42f));
        Line(c, Shade(ink, .8f), S(r, .045f), P(r, .6f, .3f), P(r, .55f, .17f), P(r, .44f, .11f));
        Line(c, ink, S(r, .085f), P(r, .44f, .11f), P(r, .24f, .07f));
    }

    /// <summary>A mason's mallet across his chisel, chips flying from its edge.</summary>
    public static void Masons(CanvasItem c, Rect2 r, Color ink)
    {
        Line(c, Shade(ink, .85f), S(r, .07f), P(r, .82f, .12f), P(r, .32f, .72f));
        Fill(c, ink, P(r, .35f, .68f), P(r, .25f, .84f), P(r, .18f, .86f), P(r, .27f, .72f));
        Line(c, Shade(ink, .72f), S(r, .06f), P(r, .24f, .24f), P(r, .78f, .86f));
        Fill(c, ink, P(r, .06f, .26f), P(r, .24f, .06f), P(r, .42f, .22f), P(r, .24f, .42f));
        Dot(c, P(r, .1f, .93f), S(r, .03f), Shade(ink, .8f));
        Dot(c, P(r, .05f, .8f), S(r, .025f), Shade(ink, .8f));
        Dot(c, P(r, .2f, .97f), S(r, .022f), Shade(ink, .8f));
    }

    /// <summary>A round-headed leaf of planks bound with three iron straps and their rivets.</summary>
    public static void Ironbound(CanvasItem c, Rect2 r, Color ink)
    {
        var leaf = new List<Vector2> { P(r, .2f, .94f) };
        leaf.AddRange(Ring(r, .5f, .38f, .3f, .3f, MathF.PI, 2 * MathF.PI, 14));
        leaf.Add(P(r, .8f, .94f));
        Fill(c, Shade(ink, .5f), leaf.ToArray());
        foreach (float x in new[] { .35f, .5f, .65f }) c.DrawLine(P(r, x, .12f + MathF.Abs(x - .5f) * .7f), P(r, x, .93f), Hollow(ink, .5f), 1.2f, true);
        foreach (float y in new[] { .42f, .62f, .82f })
        {
            Line(c, ink, S(r, .07f), P(r, .14f, y), P(r, .86f, y));
            foreach (float x in new[] { .28f, .5f, .72f }) c.DrawCircle(P(r, x, y), S(r, .022f), Shade(ink, .45f), true, -1, true);
        }
    }

    /// <summary>A dressed block, the marks of the chisel across its faces.</summary>
    public static void Ashlar(CanvasItem c, Rect2 r, Color ink)
    {
        IsoBox(c, r, ink, .2f, .84f);
        var mark = Hollow(ink, .4f);
        for (int i = 0; i < 4; i++)
        {
            float f = .2f + i * .18f;
            c.DrawLine(P(r, .14f + .1f * f, .4f + .3f * f), P(r, .14f + .26f * f, .56f + .3f * f), mark, 1.1f, true);
            c.DrawLine(P(r, .56f + .3f * f, .62f - .1f * f), P(r, .56f + .3f * f, .76f - .1f * f), mark, 1.1f, true);
        }
    }

    /// <summary>A square keep with its crenels, standing on a crag.</summary>
    public static void Bastion(CanvasItem c, Rect2 r, Color ink)
    {
        Fill(c, Shade(ink, .48f), P(r, .02f, .97f), P(r, .14f, .74f), P(r, .3f, .66f), P(r, .44f, .72f), P(r, .62f, .63f), P(r, .8f, .7f), P(r, .98f, .97f));
        Fill(c, ink, P(r, .3f, .7f), P(r, .3f, .24f), P(r, .7f, .24f), P(r, .7f, .7f));
        foreach (float x in new[] { .3f, .45f, .6f }) Fill(c, ink, P(r, x, .24f), P(r, x, .12f), P(r, x + .1f, .12f), P(r, x + .1f, .24f));
        c.DrawRect(new Rect2(P(r, .47f, .34f), new Vector2(S(r, .06f), S(r, .14f))), Hollow(ink), true);
        c.DrawRect(new Rect2(P(r, .44f, .56f), new Vector2(S(r, .12f), S(r, .14f))), Hollow(ink), true);
    }

    /// <summary>A curtain wall between two towers, the gate in its middle.</summary>
    public static void Fortress(CanvasItem c, Rect2 r, Color ink)
    {
        Fill(c, Shade(ink, .7f), P(r, .22f, .92f), P(r, .22f, .5f), P(r, .78f, .5f), P(r, .78f, .92f));
        for (int i = 0; i < 4; i++) { float x = .26f + i * .13f; Fill(c, Shade(ink, .7f), P(r, x, .5f), P(r, x, .42f), P(r, x + .07f, .42f), P(r, x + .07f, .5f)); }
        foreach (float x0 in new[] { .04f, .72f })
        {
            Fill(c, ink, P(r, x0, .94f), P(r, x0, .3f), P(r, x0 + .24f, .3f), P(r, x0 + .24f, .94f));
            foreach (float dx in new[] { 0f, .09f, .18f }) Fill(c, ink, P(r, x0 + dx, .3f), P(r, x0 + dx, .2f), P(r, x0 + dx + .06f, .2f), P(r, x0 + dx + .06f, .3f));
            c.DrawRect(new Rect2(P(r, x0 + .1f, .44f), new Vector2(S(r, .04f), S(r, .12f))), Hollow(ink), true);
        }
        var gate = new List<Vector2> { P(r, .4f, .92f) };
        gate.AddRange(Ring(r, .5f, .72f, .1f, .1f, MathF.PI, 2 * MathF.PI, 8));
        gate.Add(P(r, .6f, .92f));
        c.DrawColoredPolygon(gate.ToArray(), Hollow(ink));
    }

    // ---- the labour ----

    /// <summary>A pick striking sparks.</summary>
    public static void GoodPick(CanvasItem c, Rect2 r, Color ink)
    {
        Pick(c, Sub(r, .08f, .08f, .9f), ink);
        foreach (var (dx, dy) in new[] { (-.08f, .02f), (-.06f, .12f), (.0f, .2f) })
            Line(c, Shade(ink, 1.1f), S(r, .025f), P(r, .14f, .48f), P(r, .14f + dx, .48f + dy));
    }

    /// <summary>A wheelbarrow, heaped.</summary>
    public static void Barrow(CanvasItem c, Rect2 r, Color ink)
    {
        Line(c, Shade(ink, .8f), S(r, .05f), P(r, .62f, .6f), P(r, .97f, .44f));
        Line(c, Shade(ink, .7f), S(r, .045f), P(r, .56f, .62f), P(r, .62f, .86f));
        Fill(c, Shade(ink, .5f), P(r, .12f, .38f), P(r, .26f, .24f), P(r, .42f, .2f), P(r, .58f, .28f), P(r, .7f, .38f));
        Fill(c, ink, P(r, .06f, .38f), P(r, .76f, .38f), P(r, .64f, .64f), P(r, .2f, .64f));
        Dot(c, P(r, .26f, .76f), S(r, .15f), Shade(ink, .85f));
        c.DrawCircle(P(r, .26f, .76f), S(r, .05f), Hollow(ink, .6f), true, -1, true);
        for (int i = 0; i < 4; i++)
        {
            float a = i * MathF.PI / 4;
            c.DrawLine(P(r, .26f, .76f) + new Vector2(MathF.Cos(a), MathF.Sin(a)) * S(r, .13f), P(r, .26f, .76f) - new Vector2(MathF.Cos(a), MathF.Sin(a)) * S(r, .13f), Hollow(ink, .5f), 1.1f, true);
        }
    }

    /// <summary>A bucket of water, a drop leaping from it.</summary>
    public static void Bucket(CanvasItem c, Rect2 r, Color ink)
    {
        Line(c, Shade(ink, .7f), S(r, .035f), Ring(r, .5f, .38f, .3f, .3f, MathF.PI, 2 * MathF.PI, 12));
        Fill(c, ink, P(r, .2f, .38f), P(r, .8f, .38f), P(r, .7f, .92f), P(r, .3f, .92f));
        c.DrawEllipse(P(r, .5f, .38f), S(r, .3f), S(r, .06f), Water(ink), true, -1, true);
        foreach (float y in new[] { .52f, .8f }) Line(c, Shade(ink, .4f), S(r, .035f), P(r, .2f + (y - .38f) * .18f, y), P(r, .8f - (y - .38f) * .18f, y));
        Drop(c, r, .88f, .2f, .1f, Water(ink));
    }

    /// <summary>A cart wheel rolling, the dust of its haste behind it.</summary>
    public static void Wheel(CanvasItem c, Rect2 r, Color ink)
    {
        var at = P(r, .6f, .54f);
        float rad = S(r, .33f);
        c.DrawArc(at + new Vector2(.9f, 1.2f), rad, 0, MathF.Tau, 32, Hollow(ink, .7f), S(r, .09f), true);
        c.DrawArc(at, rad, 0, MathF.Tau, 32, ink, S(r, .08f), true);
        for (int i = 0; i < 6; i++)
        {
            float a = i * MathF.PI / 3 + .3f;
            Line(c, Shade(ink, .8f), S(r, .04f), at, at + new Vector2(MathF.Cos(a), MathF.Sin(a)) * rad);
        }
        Dot(c, at, S(r, .08f), ink);
        foreach (float y in new[] { .36f, .54f, .72f }) Line(c, Shade(ink, .55f), S(r, .035f), P(r, .04f, y), P(r, .2f, y));
    }

    /// <summary>A hide stretched on its cords, wet through.</summary>
    public static void Hide(CanvasItem c, Rect2 r, Color ink)
    {
        Fill(c, Shade(ink, .72f), P(r, .2f, .14f), P(r, .36f, .22f), P(r, .5f, .12f), P(r, .64f, .22f), P(r, .8f, .14f), P(r, .76f, .4f),
            P(r, .88f, .56f), P(r, .74f, .66f), P(r, .8f, .9f), P(r, .6f, .82f), P(r, .5f, .92f), P(r, .4f, .82f), P(r, .2f, .9f), P(r, .26f, .66f),
            P(r, .12f, .56f), P(r, .24f, .4f));
        foreach (var (x0, y0, x1, y1) in new[] { (.2f, .14f, .06f, .04f), (.8f, .14f, .94f, .04f), (.2f, .9f, .06f, .98f), (.8f, .9f, .94f, .98f) })
            Line(c, Shade(ink, .5f), S(r, .02f), P(r, x0, y0), P(r, x1, y1));
        Drop(c, r, .42f, .42f, .09f, Water(ink));
        Drop(c, r, .6f, .56f, .08f, Water(ink));
        Drop(c, r, .45f, .7f, .07f, Water(ink));
    }

    /// <summary>An anvil, a hammer raised over it.</summary>
    public static void Anvil(CanvasItem c, Rect2 r, Color ink)
    {
        Fill(c, ink, P(r, .06f, .46f), P(r, .78f, .46f), P(r, .94f, .4f), P(r, .88f, .54f), P(r, .66f, .6f), P(r, .6f, .74f), P(r, .74f, .9f),
            P(r, .22f, .9f), P(r, .36f, .74f), P(r, .3f, .6f), P(r, .12f, .56f));
        Line(c, Shade(ink, .5f), S(r, .025f), P(r, .1f, .5f), P(r, .8f, .5f));
        Line(c, Shade(ink, .78f), S(r, .055f), P(r, .52f, .34f), P(r, .86f, .06f));
        Fill(c, ink, P(r, .34f, .3f), P(r, .5f, .16f), P(r, .62f, .3f), P(r, .46f, .44f));
    }

    // ---- the troop ----

    /// <summary>A quilted coat, sleeves out.</summary>
    public static void Gambeson(CanvasItem c, Rect2 r, Color ink)
    {
        Fill(c, ink, P(r, .34f, .1f), P(r, .5f, .18f), P(r, .66f, .1f), P(r, .92f, .26f), P(r, .84f, .52f), P(r, .74f, .46f), P(r, .76f, .92f),
            P(r, .24f, .92f), P(r, .26f, .46f), P(r, .16f, .52f), P(r, .08f, .26f));
        var seam = Hollow(ink, .45f);
        foreach (float y in new[] { .34f, .48f, .62f, .76f }) c.DrawLine(P(r, .27f, y), P(r, .73f, y), seam, 1.2f, true);
        c.DrawLine(P(r, .5f, .2f), P(r, .5f, .9f), seam, 1.2f, true);
    }

    /// <summary>A quintain to drill at: a post, its arm with a shield on one end and a sack on the other.</summary>
    public static void Quintain(CanvasItem c, Rect2 r, Color ink)
    {
        Line(c, Shade(ink, .5f), S(r, .04f), P(r, .28f, .96f), P(r, .72f, .96f));
        Line(c, Shade(ink, .8f), S(r, .07f), P(r, .5f, .96f), P(r, .5f, .22f));
        Line(c, Shade(ink, .8f), S(r, .05f), P(r, .1f, .32f), P(r, .9f, .32f));
        Dot(c, P(r, .5f, .16f), S(r, .09f), ink);
        Fill(c, ink, P(r, .06f, .36f), P(r, .3f, .36f), P(r, .3f, .56f), P(r, .18f, .72f), P(r, .06f, .56f));
        Line(c, Shade(ink, .6f), S(r, .02f), P(r, .86f, .32f), P(r, .86f, .46f));
        Fill(c, Shade(ink, .7f), P(r, .8f, .46f), P(r, .92f, .46f), P(r, .96f, .62f), P(r, .86f, .7f), P(r, .76f, .62f));
    }

    /// <summary>A swallow-tailed banner on its staff.</summary>
    public static void Banner(CanvasItem c, Rect2 r, Color ink)
    {
        Line(c, Shade(ink, .8f), S(r, .055f), P(r, .22f, .96f), P(r, .22f, .08f));
        Dot(c, P(r, .22f, .07f), S(r, .05f), ink);
        Line(c, Shade(ink, .7f), S(r, .04f), P(r, .22f, .15f), P(r, .84f, .15f));
        Fill(c, ink, P(r, .24f, .17f), P(r, .82f, .17f), P(r, .82f, .76f), P(r, .66f, .62f), P(r, .53f, .78f), P(r, .4f, .62f), P(r, .24f, .76f));
        Line(c, Shade(ink, .42f), S(r, .05f), P(r, .53f, .26f), P(r, .53f, .56f));
        Line(c, Shade(ink, .42f), S(r, .05f), P(r, .4f, .36f), P(r, .66f, .36f));
    }

    /// <summary>A shirt of mail, ring by ring.</summary>
    public static void Mail(CanvasItem c, Rect2 r, Color ink)
    {
        Fill(c, Shade(ink, .7f), P(r, .32f, .1f), P(r, .68f, .1f), P(r, .9f, .26f), P(r, .82f, .5f), P(r, .74f, .44f), P(r, .76f, .94f),
            P(r, .24f, .94f), P(r, .26f, .44f), P(r, .18f, .5f), P(r, .1f, .26f));
        var ring = Hollow(ink, .55f);
        for (int row = 0; row < 6; row++)
            for (int i = 0; i < 4; i++)
            {
                float x = .32f + i * .12f + (row % 2) * .06f, y = .24f + row * .12f;
                if (x > .72f) continue;
                c.DrawArc(P(r, x, y), S(r, .05f), 0, MathF.Tau, 10, ring, 1.1f, true);
            }
    }

    /// <summary>A heater shield with two chevrons on it.</summary>
    public static void Chevrons(CanvasItem c, Rect2 r, Color ink)
    {
        Fill(c, Shade(ink, .55f), P(r, .14f, .08f), P(r, .86f, .08f), P(r, .86f, .46f), P(r, .72f, .74f), P(r, .5f, .94f), P(r, .28f, .74f), P(r, .14f, .46f));
        Line(c, ink, S(r, .09f), P(r, .24f, .52f), P(r, .5f, .3f), P(r, .76f, .52f));
        Line(c, ink, S(r, .09f), P(r, .3f, .72f), P(r, .5f, .54f), P(r, .7f, .72f));
    }

    /// <summary>A nasal helm cut across by an old blow, a sprig of laurel under it.</summary>
    public static void Helm(CanvasItem c, Rect2 r, Color ink)
    {
        var dome = new List<Vector2>(Ring(r, .5f, .5f, .3f, .38f, MathF.PI, 2 * MathF.PI, 14)) { P(r, .8f, .74f), P(r, .66f, .74f), P(r, .62f, .52f), P(r, .38f, .52f), P(r, .34f, .74f), P(r, .2f, .74f) };
        Fill(c, ink, dome.ToArray());
        Line(c, Shade(ink, .7f), S(r, .07f), P(r, .5f, .44f), P(r, .5f, .74f));
        Line(c, Shade(ink, .6f), S(r, .04f), P(r, .2f, .5f), P(r, .8f, .5f));
        Line(c, Hollow(ink, .9f), S(r, .025f), P(r, .34f, .2f), P(r, .42f, .3f), P(r, .38f, .38f));
        var leaf = Shade(ink, .7f);
        foreach (int s in new[] { -1, 1 })
        {
            Line(c, leaf, S(r, .03f), P(r, .5f, .94f), P(r, .5f + s * .3f, .84f), P(r, .5f + s * .42f, .7f));
            for (int i = 1; i <= 3; i++) Dot(c, P(r, .5f + s * (.1f + i * .1f), .92f - i * .06f), S(r, .03f), leaf);
        }
    }

    // ---- the traps ----

    /// <summary>A row of sharpened stakes leaning out of the ground.</summary>
    public static void Stakes(CanvasItem c, Rect2 r, Color ink)
    {
        for (int i = 0; i < 4; i++)
        {
            float x = .14f + i * .22f, top = i % 2 == 0 ? .14f : .26f;
            Fill(c, i % 2 == 0 ? ink : Shade(ink, .78f), P(r, x - .06f, .88f), P(r, x + .06f, .88f), P(r, x + .13f, top + .12f), P(r, x + .15f, top));
        }
        Fill(c, Shade(ink, .45f), P(r, .02f, .97f), P(r, .02f, .84f), P(r, .98f, .8f), P(r, .98f, .97f));
    }

    /// <summary>A pot tipped over, its tar running out thick.</summary>
    public static void TarPot(CanvasItem c, Rect2 r, Color ink)
    {
        var pot = new List<Vector2>(Ring(r, .34f, .34f, .26f, .22f, -.6f, 2.6f, 12));
        Fill(c, ink, pot.ToArray());
        Line(c, Shade(ink, .5f), S(r, .05f), A(r, .34f, .34f, -.6f, .26f, 0), A(r, .34f, .34f, 2.6f, .26f, 0));
        var tar = Shade(ink, .28f);
        Line(c, tar, S(r, .1f), P(r, .56f, .26f), P(r, .68f, .4f), P(r, .7f, .62f), P(r, .7f, .84f));
        c.DrawEllipse(P(r, .7f, .9f), S(r, .22f), S(r, .06f), tar, true, -1, true);
        Drop(c, r, .86f, .66f, .06f, tar);
    }

    /// <summary>An arrow with its head wrapped in fire.</summary>
    public static void FireArrow(CanvasItem c, Rect2 r, Color ink)
    {
        Flame(c, Sub(r, .44f, .1f, .4f), ink);
        Arrow(c, r, Shade(ink, .9f), .1f, .9f, -MathF.PI / 4, .9f);
    }

    /// <summary>A keg with a burning fuse and the burst behind it.</summary>
    public static void Keg(CanvasItem c, Rect2 r, Color ink)
    {
        var star = new Vector2[16];
        for (int i = 0; i < 16; i++)
        {
            float a = i * MathF.PI / 8, rad = i % 2 == 0 ? .48f : .3f;
            star[i] = P(r, .5f + MathF.Cos(a) * rad, .54f + MathF.Sin(a) * rad);
        }
        Fill(c, Shade(ink, .5f), star);
        Fill(c, ink, P(r, .32f, .28f), P(r, .68f, .28f), P(r, .76f, .5f), P(r, .68f, .84f), P(r, .32f, .84f), P(r, .24f, .5f));
        foreach (float y in new[] { .38f, .74f }) Line(c, Shade(ink, .42f), S(r, .04f), P(r, .29f, y), P(r, .71f, y));
        Line(c, Shade(ink, .7f), S(r, .025f), P(r, .5f, .28f), P(r, .56f, .16f), P(r, .66f, .1f));
        Dot(c, P(r, .68f, .09f), S(r, .04f), Shade(ink, 1.2f));
    }

    /// <summary>Fire falling from the sky: three burning arrows coming down.</summary>
    public static void Firestorm(CanvasItem c, Rect2 r, Color ink)
    {
        Fill(c, Shade(ink, .4f), P(r, .02f, .97f), P(r, .02f, .9f), P(r, .98f, .88f), P(r, .98f, .97f));
        for (int i = 0; i < 3; i++)
        {
            float x = .2f + i * .3f, y = i == 1 ? .02f : .12f;
            Arrow(c, r, Shade(ink, .85f), x, y, MathF.PI * .62f, .62f, .03f);
            var tip = A(r, x, y, MathF.PI * .62f, .55f, 0);
            Flame(c, new Rect2(tip - new Vector2(S(r, .1f), S(r, .13f)), new Vector2(S(r, .2f), S(r, .2f))), ink);
        }
    }

    /// <summary>A pit dug in the ground, stakes standing in it.</summary>
    public static void Pit(CanvasItem c, Rect2 r, Color ink)
    {
        Fill(c, Shade(ink, .6f), P(r, .02f, .4f), P(r, .24f, .4f), P(r, .3f, .96f), P(r, .02f, .96f));
        Fill(c, Shade(ink, .6f), P(r, .76f, .4f), P(r, .98f, .4f), P(r, .98f, .96f), P(r, .7f, .96f));
        c.DrawColoredPolygon(new[] { P(r, .24f, .4f), P(r, .76f, .4f), P(r, .7f, .96f), P(r, .3f, .96f) }, Hollow(ink, .7f));
        foreach (float x in new[] { .38f, .5f, .62f })
            Fill(c, ink, P(r, x - .04f, .94f), P(r, x + .04f, .94f), P(r, x, x == .5f ? .5f : .58f));
        Line(c, Shade(ink, .7f), S(r, .03f), P(r, .14f, .36f), P(r, .5f, .3f), P(r, .86f, .36f));
    }

    /// <summary>The sigil of a lore by its key; a question mark for one without its own.</summary>
    public static Painter SigilOf(string key) => key switch
    {
        "yew" => Yew, "barbs" => Barbs, "braziers" => Brazier, "volley" => Volley, "owleye" => Owl, "longbows" => Longbow,
        "mortar" => MortarTub, "masons" => Masons, "ironbound" => Ironbound, "granite" => Ashlar, "bastion" => Bastion, "fortress" => Fortress,
        "picks" => GoodPick, "barrows" => Barrow, "buckets" => Bucket, "carts" => Wheel, "hides" => Hide, "guild" => Anvil,
        "gambesons" => Gambeson, "drill" => Quintain, "banners" => Banner, "mail" => Mail, "sergeants" => Chevrons, "veterans" => Helm,
        "spikes" => Stakes, "tar" => TarPot, "soaked" => FireArrow, "naphtha" => Keg, "firestorm" => Firestorm, "deathtraps" => Pit,
        _ => Question,
    };
}
