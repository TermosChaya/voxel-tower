using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// Engraved icons for the tool belt, drawn as bronze strokes with a dark under-cut so they read as chased metal.
/// Coordinates are fractions of the icon's square.
/// </summary>
public static class Icons
{
    public delegate void Painter(CanvasItem c, Rect2 r, Color ink);

    static Vector2 P(Rect2 r, float x, float y) => r.Position + new Vector2(x * r.Size.X, y * r.Size.Y);
    static float S(Rect2 r, float f) => f * r.Size.X;

    static Vector2[] Shift(Vector2[] pts, float dx, float dy)
    {
        var o = new Vector2[pts.Length];
        for (int i = 0; i < pts.Length; i++) o[i] = pts[i] + new Vector2(dx, dy);
        return o;
    }

    static void Line(CanvasItem c, Color ink, float w, params Vector2[] pts)
    {
        c.DrawPolyline(Shift(pts, .9f, 1.2f), new Color(0, 0, 0, .7f * ink.A), w + 1.2f, true);
        c.DrawPolyline(pts, ink, w, true);
    }

    static void Fill(CanvasItem c, Color ink, params Vector2[] pts)
    {
        c.DrawColoredPolygon(Shift(pts, .9f, 1.2f), new Color(0, 0, 0, .6f * ink.A));
        c.DrawColoredPolygon(pts, ink);
    }

    static void Dot(CanvasItem c, Vector2 p, float rad, Color ink)
    {
        c.DrawCircle(p + new Vector2(.8f, 1f), rad, new Color(0, 0, 0, .6f * ink.A), true, -1, true);
        c.DrawCircle(p, rad, ink, true, -1, true);
    }

    static Color Shade(Color ink, float k) => new(ink.R * k, ink.G * k, ink.B * k, ink.A);

    public static void Brush(CanvasItem c, Rect2 r, Color ink)
    {
        Line(c, Shade(ink, .8f), S(r, .07f), P(r, .84f, .12f), P(r, .53f, .45f));
        Fill(c, Shade(ink, .6f), P(r, .56f, .40f), P(r, .62f, .46f), P(r, .5f, .58f), P(r, .44f, .52f));
        Fill(c, ink, P(r, .44f, .52f), P(r, .5f, .58f), P(r, .39f, .74f), P(r, .17f, .86f), P(r, .27f, .64f));
        Line(c, new Color(ink, .65f * ink.A), S(r, .045f), P(r, .1f, .93f), P(r, .32f, .88f), P(r, .56f, .91f), P(r, .82f, .83f));
    }

    public static void StraightLine(CanvasItem c, Rect2 r, Color ink)
    {
        Line(c, ink, S(r, .13f), P(r, .2f, .78f), P(r, .8f, .24f));
        Dot(c, P(r, .2f, .78f), S(r, .09f), ink);
        Dot(c, P(r, .8f, .24f), S(r, .09f), ink);
        Line(c, Shade(ink, .6f), S(r, .03f), P(r, .1f, .66f), P(r, .3f, .9f));
        Line(c, Shade(ink, .6f), S(r, .03f), P(r, .7f, .12f), P(r, .9f, .36f));
    }

    static void IsoBox(CanvasItem c, Rect2 r, Color ink, float top, float bottom)
    {
        var t0 = P(r, .5f, top); var t1 = P(r, .86f, top + .17f); var t2 = P(r, .5f, top + .34f); var t3 = P(r, .14f, top + .17f);
        var b1 = P(r, .86f, bottom - .17f); var b2 = P(r, .5f, bottom); var b3 = P(r, .14f, bottom - .17f);
        Fill(c, Shade(ink, .55f), t3, t2, b2, b3);
        Fill(c, Shade(ink, .75f), t2, t1, b1, b2);
        Fill(c, ink, t0, t1, t2, t3);
        var edge = new Color(0, 0, 0, .55f * ink.A);
        c.DrawPolyline(new[] { t3, t2, t1 }, edge, 1.2f, true);
        c.DrawLine(t2, b2, edge, 1.2f, true);
    }

    public static void Box(CanvasItem c, Rect2 r, Color ink) => IsoBox(c, r, ink, .16f, .86f);

    public static void Circle(CanvasItem c, Rect2 r, Color ink)
    {
        var top = P(r, .5f, .3f); float rx = S(r, .33f), ry = S(r, .12f), h = S(r, .44f);
        const int n = 16;
        var body = new Vector2[n + 3];
        body[0] = top + new Vector2(rx, 0);
        body[1] = top + new Vector2(-rx, 0);
        for (int i = 0; i <= n; i++)
        {
            float a = i / (float)n * MathF.PI;
            body[i + 2] = top + new Vector2(-MathF.Cos(a) * rx, h + MathF.Sin(a) * ry);
        }
        Fill(c, Shade(ink, .6f), body);
        c.DrawEllipse(top, rx, ry, ink, true, -1, true);
        c.DrawEllipse(top, rx * .62f, ry * .62f, Shade(ink, .45f), true, -1, true);
        c.DrawEllipseArc(top, rx, ry, 0, MathF.Tau, 32, new Color(0, 0, 0, .5f * ink.A), 1.2f, true);
    }

    public static void Extrude(CanvasItem c, Rect2 r, Color ink)
    {
        IsoBox(c, new Rect2(r.Position + new Vector2(0, S(r, .32f)), r.Size), Shade(ink, .9f), .3f, .62f);
        Line(c, ink, S(r, .075f), P(r, .5f, .52f), P(r, .5f, .2f));
        Fill(c, ink, P(r, .34f, .27f), P(r, .5f, .07f), P(r, .66f, .27f));
    }

    public static void Erase(CanvasItem c, Rect2 r, Color ink)
    {
        Fill(c, ink, P(r, .16f, .6f), P(r, .54f, .22f), P(r, .84f, .52f), P(r, .46f, .9f));
        Fill(c, Shade(ink, .55f), P(r, .33f, .43f), P(r, .42f, .34f), P(r, .72f, .64f), P(r, .63f, .73f));
        Dot(c, P(r, .14f, .86f), S(r, .035f), Shade(ink, .8f));
        Dot(c, P(r, .24f, .93f), S(r, .028f), Shade(ink, .8f));
        Dot(c, P(r, .08f, .76f), S(r, .025f), Shade(ink, .8f));
    }

    /// <summary>A tongue of flame with a hotter one inside it: fire, and putting it out.</summary>
    public static void Flame(CanvasItem c, Rect2 r, Color ink)
    {
        Fill(c, ink, P(r, .5f, .07f), P(r, .64f, .28f), P(r, .79f, .5f), P(r, .8f, .72f), P(r, .66f, .9f), P(r, .34f, .9f), P(r, .2f, .72f),
            P(r, .22f, .5f), P(r, .33f, .35f), P(r, .41f, .5f), P(r, .45f, .29f));
        Fill(c, Shade(ink, .5f), P(r, .53f, .43f), P(r, .63f, .6f), P(r, .62f, .76f), P(r, .5f, .86f), P(r, .38f, .76f), P(r, .4f, .62f), P(r, .47f, .66f));
    }

    /// <summary>Three bars, one over another: the game's menu.</summary>
    public static void Menu(CanvasItem c, Rect2 r, Color ink)
    {
        foreach (float y in new[] { .28f, .5f, .72f })
        {
            Line(c, ink, S(r, .11f), P(r, .2f, y), P(r, .8f, y));
            Dot(c, P(r, .2f, y), S(r, .07f), Shade(ink, 1.1f));
        }
    }

    public static void Shovel(CanvasItem c, Rect2 r, Color ink)
    {
        Line(c, Shade(ink, .8f), S(r, .065f), P(r, .8f, .14f), P(r, .45f, .56f));
        Line(c, Shade(ink, .8f), S(r, .065f), P(r, .7f, .07f), P(r, .9f, .22f));
        Fill(c, ink, P(r, .43f, .49f), P(r, .53f, .59f), P(r, .42f, .83f), P(r, .19f, .93f), P(r, .17f, .74f), P(r, .29f, .56f));
        Line(c, Shade(ink, .5f), S(r, .02f), P(r, .45f, .58f), P(r, .27f, .8f));
    }

    /// <summary>A miner's pick: the worker's sign, and the slot that turns brushes to digging.</summary>
    public static void Pick(CanvasItem c, Rect2 r, Color ink)
    {
        Line(c, Shade(ink, .78f), S(r, .075f), P(r, .2f, .9f), P(r, .6f, .32f));
        var head = new[] { P(r, .14f, .36f), P(r, .3f, .24f), P(r, .5f, .17f), P(r, .7f, .19f), P(r, .86f, .28f), P(r, .94f, .42f) };
        Line(c, ink, S(r, .085f), head);
        Fill(c, ink, P(r, .1f, .4f), P(r, .16f, .31f), P(r, .2f, .36f));
        Fill(c, ink, P(r, .9f, .38f), P(r, .98f, .5f), P(r, .91f, .45f));
        Dot(c, P(r, .6f, .21f), S(r, .05f), Shade(ink, .6f));
    }

    public static void Bow(CanvasItem c, Rect2 r, Color ink)
    {
        const int n = 14;
        var arc = new Vector2[n + 1];
        for (int i = 0; i <= n; i++) { float a = -MathF.PI / 2 + i / (float)n * MathF.PI; arc[i] = P(r, .3f + MathF.Cos(a) * .42f, .5f + MathF.Sin(a) * .42f); }
        Line(c, ink, S(r, .07f), arc);
        c.DrawLine(arc[0], arc[^1], Shade(ink, .7f), 1.2f, true);
        Line(c, Shade(ink, .85f), S(r, .04f), P(r, .14f, .5f), P(r, .9f, .5f));
        Fill(c, ink, P(r, .82f, .44f), P(r, .95f, .5f), P(r, .82f, .56f));
        Line(c, Shade(ink, .6f), S(r, .03f), P(r, .14f, .5f), P(r, .07f, .43f));
        Line(c, Shade(ink, .6f), S(r, .03f), P(r, .14f, .5f), P(r, .07f, .57f));
    }

    public static void Sword(CanvasItem c, Rect2 r, Color ink)
    {
        Fill(c, ink, P(r, .8f, .1f), P(r, .9f, .2f), P(r, .42f, .68f), P(r, .32f, .58f));
        Line(c, Shade(ink, .55f), S(r, .02f), P(r, .84f, .16f), P(r, .38f, .62f));
        Line(c, Shade(ink, .85f), S(r, .07f), P(r, .22f, .52f), P(r, .48f, .78f));
        Line(c, Shade(ink, .7f), S(r, .07f), P(r, .35f, .65f), P(r, .17f, .83f));
        Dot(c, P(r, .14f, .86f), S(r, .06f), ink);
    }

    public static void Ladder(CanvasItem c, Rect2 r, Color ink)
    {
        Line(c, ink, S(r, .07f), P(r, .3f, .92f), P(r, .42f, .08f));
        Line(c, ink, S(r, .07f), P(r, .62f, .92f), P(r, .74f, .08f));
        for (int i = 1; i <= 5; i++)
        {
            float t = i / 6f;
            Line(c, Shade(ink, .8f), S(r, .05f), P(r, .3f + .12f * t, .92f - .84f * t), P(r, .62f + .12f * t, .92f - .84f * t));
        }
        Line(c, Shade(ink, .55f), S(r, .03f), P(r, .12f, .95f), P(r, .9f, .95f));
    }

    /// <summary>A round-headed gateway in a wall: the left leaf shut, banded with iron, the right one swung open.</summary>
    public static void GateIcon(CanvasItem c, Rect2 r, Color ink)
    {
        var arch = new List<Vector2> { P(r, .16f, .94f), P(r, .16f, .44f) };
        for (int i = 1; i < 12; i++)
        {
            float a = MathF.PI * (1 - i / 12f);
            arch.Add(P(r, .5f + .34f * MathF.Cos(a), .44f - .3f * MathF.Sin(a)));
        }
        arch.Add(P(r, .84f, .44f));
        arch.Add(P(r, .84f, .94f));
        Line(c, ink, S(r, .075f), arch.ToArray());
        Fill(c, Shade(ink, .72f), P(r, .24f, .9f), P(r, .24f, .46f), P(r, .34f, .27f), P(r, .5f, .2f), P(r, .5f, .9f));
        for (int i = 0; i < 3; i++)
        {
            float y = .42f + i * .19f;
            Line(c, Shade(ink, .4f), S(r, .035f), P(r, .25f, y), P(r, .49f, y));
        }
        Fill(c, Shade(ink, .55f), P(r, .56f, .9f), P(r, .56f, .3f), P(r, .7f, .38f), P(r, .7f, .84f));
        Line(c, Shade(ink, .5f), S(r, .03f), P(r, .06f, .96f), P(r, .94f, .96f));
    }

    /// <summary>A stone bridge arching over water, its parapet along the top.</summary>
    public static void BridgeIcon(CanvasItem c, Rect2 r, Color ink)
    {
        var deck = new List<Vector2>();
        var under = new List<Vector2>();
        for (int i = 0; i <= 16; i++)
        {
            float t = i / 16f, a = MathF.Sin(MathF.PI * t);
            deck.Add(P(r, .06f + .88f * t, .5f - .16f * a));
            under.Add(P(r, .06f + .88f * t, .5f - .16f * a + .06f + .22f * (1 - a) * (1 - a)));
        }
        var body = new List<Vector2>(deck);
        for (int i = under.Count - 1; i >= 0; i--) body.Add(under[i]);
        Fill(c, Shade(ink, .62f), body.ToArray());
        var arch = new List<Vector2>();
        for (int i = 0; i <= 12; i++)
        {
            float a = MathF.PI * i / 12f;
            arch.Add(P(r, .5f - .3f * MathF.Cos(a), .72f - .2f * MathF.Sin(a)));
        }
        Line(c, Shade(ink, .4f), S(r, .05f), arch.ToArray());
        Line(c, ink, S(r, .06f), deck.ToArray());
        var rail = new List<Vector2>();
        foreach (var p in deck) rail.Add(p - new Vector2(0, S(r, .1f)));
        Line(c, Shade(ink, .85f), S(r, .035f), rail.ToArray());
        Line(c, Shade(ink, .45f), S(r, .03f), P(r, .06f, .86f), P(r, .3f, .82f), P(r, .5f, .87f), P(r, .72f, .82f), P(r, .94f, .86f));
    }

    /// <summary>A trowel over a wall with a gap broken out of its top: mending.</summary>
    public static void Mend(CanvasItem c, Rect2 r, Color ink)
    {
        // the wall: three courses, the top one broken off on the right
        var stone = Shade(ink, .62f);
        void Course(float y, float x0, float x1, float h)
        {
            float x = x0;
            int n = 0;
            while (x < x1 - .02f)
            {
                float w = Math.Min(x1 - x, n % 2 == 0 ? .2f : .16f);
                Fill(c, stone, P(r, x + .012f, y), P(r, x + w - .012f, y), P(r, x + w - .012f, y + h), P(r, x + .012f, y + h));
                x += w; n++;
            }
        }
        Course(.8f, .08f, .92f, .12f);
        Course(.66f, .14f, .92f, .12f);
        Course(.52f, .08f, .5f, .12f);
        // the trowel: a diamond blade, its neck and handle
        Fill(c, ink, P(r, .5f, .48f), P(r, .68f, .24f), P(r, .9f, .16f), P(r, .8f, .36f));
        Line(c, Shade(ink, .8f), S(r, .05f), P(r, .68f, .24f), P(r, .58f, .14f), P(r, .46f, .1f));
        Line(c, ink, S(r, .09f), P(r, .46f, .1f), P(r, .26f, .06f));
    }

    /// <summary>An axe across a pick over a pile: the gathering area.</summary>
    public static void Gather(CanvasItem c, Rect2 r, Color ink)
    {
        Fill(c, Shade(ink, .55f), P(r, .12f, .92f), P(r, .3f, .72f), P(r, .52f, .66f), P(r, .74f, .72f), P(r, .9f, .92f));
        Line(c, Shade(ink, .8f), S(r, .06f), P(r, .2f, .78f), P(r, .72f, .16f));
        Fill(c, ink, P(r, .62f, .1f), P(r, .82f, .14f), P(r, .86f, .3f), P(r, .7f, .3f));
        Line(c, Shade(ink, .8f), S(r, .06f), P(r, .82f, .78f), P(r, .3f, .16f));
        Line(c, ink, S(r, .07f), P(r, .14f, .26f), P(r, .3f, .14f), P(r, .46f, .1f));
    }

    /// <summary>A horn: the call to arms.</summary>
    public static void Horn(CanvasItem c, Rect2 r, Color ink)
    {
        Fill(c, ink, P(r, .1f, .7f), P(r, .3f, .52f), P(r, .55f, .42f), P(r, .8f, .2f), P(r, .9f, .3f), P(r, .62f, .56f), P(r, .36f, .7f), P(r, .16f, .86f));
        c.DrawEllipse(P(r, .85f, .25f), S(r, .09f), S(r, .06f), Shade(ink, .5f), true, -1, true);
        Line(c, Shade(ink, .6f), S(r, .025f), P(r, .3f, .62f), P(r, .36f, .5f));
        Line(c, Shade(ink, .6f), S(r, .025f), P(r, .5f, .55f), P(r, .55f, .43f));
    }

    public static void Scroll(CanvasItem c, Rect2 r, Color ink)
    {
        Fill(c, Shade(ink, .92f), P(r, .24f, .2f), P(r, .76f, .2f), P(r, .76f, .8f), P(r, .24f, .8f));
        var dark = new Color(0, 0, 0, .55f * ink.A);
        c.DrawLine(P(r, .32f, .38f), P(r, .68f, .38f), dark, 1.4f, true);
        c.DrawLine(P(r, .32f, .5f), P(r, .68f, .5f), dark, 1.4f, true);
        c.DrawLine(P(r, .32f, .62f), P(r, .56f, .62f), dark, 1.4f, true);
        foreach (float y in new[] { .2f, .8f })
        {
            Fill(c, Shade(ink, .62f), P(r, .17f, y - .06f), P(r, .83f, y - .06f), P(r, .83f, y + .06f), P(r, .17f, y + .06f));
            Dot(c, P(r, .17f, y), S(r, .06f), Shade(ink, .75f));
            Dot(c, P(r, .83f, y), S(r, .06f), Shade(ink, .75f));
        }
    }

    public static void Coin(CanvasItem c, Rect2 r, Color ink)
    {
        Dot(c, P(r, .5f, .5f), S(r, .4f), Shade(ink, .8f));
        c.DrawCircle(P(r, .5f, .5f), S(r, .31f), ink, true, -1, true);
        c.DrawArc(P(r, .5f, .5f), S(r, .25f), 0, MathF.Tau, 28, Shade(ink, .55f), 1.3f, true);
        var tw = new[] { P(r, .41f, .63f), P(r, .41f, .41f), P(r, .45f, .41f), P(r, .45f, .36f), P(r, .48f, .36f), P(r, .48f, .41f), P(r, .52f, .41f), P(r, .52f, .36f), P(r, .55f, .36f), P(r, .55f, .41f), P(r, .59f, .41f), P(r, .59f, .63f) };
        c.DrawColoredPolygon(tw, Shade(ink, .45f));
    }

    public static void Earth(CanvasItem c, Rect2 r, Color ink)
    {
        const int n = 14;
        var pts = new Vector2[n + 1];
        for (int i = 0; i <= n; i++) { float a = i / (float)n * MathF.PI; pts[i] = P(r, .5f - MathF.Cos(a) * .4f, .8f - MathF.Sin(a) * .46f); }
        Fill(c, new Color("8a6544"), pts);
        c.DrawPolyline(pts, Shade(ink, .9f), 1.4f, true);
        foreach (var (x, y) in new[] { (.38f, .62f), (.56f, .52f), (.64f, .7f), (.45f, .74f), (.3f, .72f) })
            c.DrawCircle(P(r, x, y), S(r, .03f), new Color("4b3727"), true, -1, true);
    }

    public static void Question(CanvasItem c, Rect2 r, Color ink)
    {
        int size = (int)(r.Size.Y * .8f);
        c.DrawString(Ornament.SerifBold, P(r, 0, .78f), "?", HorizontalAlignment.Center, r.Size.X, size, ink);
    }

    /// <summary>A small isometric block of the material, with its own surface: coursed brick, logs, or earth.</summary>
    public static Painter Material(byte m) => (c, r, ink) =>
    {
        var col = m == Mat.Stone ? new Color("8c8689") : Ornament.MatColor(m);
        var t0 = P(r, .5f, .12f); var t1 = P(r, .88f, .3f); var t2 = P(r, .5f, .48f); var t3 = P(r, .12f, .3f);
        var b1 = P(r, .88f, .7f); var b2 = P(r, .5f, .88f); var b3 = P(r, .12f, .7f);
        c.DrawColoredPolygon(new[] { t3 + new Vector2(1, 1.5f), t2 + new Vector2(1, 1.5f), b2 + new Vector2(1, 1.5f), b3 + new Vector2(1, 1.5f) }, new Color(0, 0, 0, .5f));
        c.DrawColoredPolygon(new[] { t3, t2, b2, b3 }, Shade(col, .62f));
        c.DrawColoredPolygon(new[] { t2, t1, b1, b2 }, Shade(col, .8f));
        c.DrawColoredPolygon(new[] { t0, t1, t2, t3 }, Shade(col, 1.08f));
        var line = new Color(0, 0, 0, .45f);
        if (m == Mat.Stone)
        {
            for (int k = 1; k <= 3; k++)
            {
                float f = k / 4f;
                c.DrawLine(t3.Lerp(b3, f), t2.Lerp(b2, f), line, 1, true);
                c.DrawLine(t2.Lerp(b2, f), t1.Lerp(b1, f), line, 1, true);
            }
            for (int k = 0; k < 4; k++)
            {
                float f0 = k / 4f, f1 = (k + 1) / 4f, s = k % 2 == 0 ? .5f : .25f;
                var a = t3.Lerp(t2, s); var b = b3.Lerp(b2, s);
                c.DrawLine(a.Lerp(b, f0), a.Lerp(b, f1), line, 1, true);
                var a2 = t2.Lerp(t1, s); var b2b = b2.Lerp(b1, s);
                c.DrawLine(a2.Lerp(b2b, f0), a2.Lerp(b2b, f1), line, 1, true);
            }
        }
        else if (m == Mat.Wood)
        {
            for (int k = 1; k <= 2; k++)
            {
                float f = k / 3f;
                c.DrawLine(t3.Lerp(b3, f), t2.Lerp(b2, f), line, 1.2f, true);
                c.DrawLine(t2.Lerp(b2, f), t1.Lerp(b1, f), line, 1.2f, true);
            }
            var mid = (t0 + t2) / 2;
            c.DrawEllipseArc(mid, S(r, .16f), S(r, .07f), 0, MathF.Tau, 20, line, 1, true);
            c.DrawEllipseArc(mid, S(r, .08f), S(r, .035f), 0, MathF.Tau, 16, line, 1, true);
        }
        else
        {
            var rnd = new Mulberry32(m * 31u);
            for (int k = 0; k < 9; k++)
            {
                float u = (float)rnd.Next(), v = (float)rnd.Next();
                var p = (k % 2 == 0 ? t3.Lerp(t2, u) : t2.Lerp(t1, u)).Lerp(k % 2 == 0 ? b3.Lerp(b2, u) : b2.Lerp(b1, u), v * .9f + .05f);
                c.DrawCircle(p, 1.1f, new Color(0, 0, 0, .35f), true, -1, true);
            }
        }
        c.DrawPolyline(new[] { t3, t0, t1, b1, b2, b3, t3 }, new Color(0, 0, 0, .6f), 1.2f, true);
        c.DrawPolyline(new[] { t3, t2, t1 }, new Color(1, 1, 1, .12f), 1, true);
    };
}
