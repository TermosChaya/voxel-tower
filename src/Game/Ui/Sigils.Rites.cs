using System;
using System.Collections.Generic;
using Godot;

namespace VoxelTower.Game;

/// <summary>
/// The sigils of the page of intervention: the four circles of the spells — the earth, the fire and the sky, death, the
/// will — and the grace the priests keep, in the same chased bronze; and which sigil, branch mark and page mark goes with what.
/// </summary>
public static partial class Icons
{
    static Color Gore(Color ink) => new(.8f, .16f, .12f, ink.A);
    static Color Bane(Color ink) => new(.5f, .64f, .3f, ink.A);

    /// <summary>An open hand held up, its palm to us: four fingers, the thumb out to the side, the wrist going down.</summary>
    static void Palm(CanvasItem c, Rect2 r, Color ink)
    {
        foreach (var (x0, y0, x1, y1) in new[] { (.36f, .46f, .3f, .2f), (.47f, .42f, .46f, .1f), (.58f, .44f, .61f, .13f), (.68f, .5f, .75f, .27f) })
        {
            Line(c, ink, S(r, .11f), P(r, x0, y0), P(r, x1, y1));
            Dot(c, P(r, x1, y1), S(r, .055f), ink);
        }
        Line(c, ink, S(r, .12f), P(r, .34f, .7f), P(r, .22f, .56f), P(r, .16f, .44f));
        Dot(c, P(r, .16f, .44f), S(r, .06f), ink);
        Fill(c, ink, P(r, .3f, .44f), P(r, .74f, .48f), P(r, .72f, .74f), P(r, .64f, .86f), P(r, .62f, 1f), P(r, .4f, 1f), P(r, .38f, .86f), P(r, .3f, .74f));
    }

    /// <summary>A cloud: three puffs over a flat underside, centred on (cx, cy) and w across, drawn as the one outline they make.</summary>
    static void Cloud(CanvasItem c, Rect2 r, Color col, float cx, float cy, float w)
    {
        var puffs = new[] { (cx - w * .25f, cy, w * .22f), (cx + w * .04f, cy - w * .1f, w * .28f), (cx + w * .3f, cy + w * .03f, w * .19f) };
        float x0 = cx - w * .47f, x1 = cx + w * .49f, yb = cy + w * .15f;
        const int n = 32;
        var top = new List<Vector2>();
        var bottom = new List<Vector2>();
        for (int i = 1; i < n; i++)
        {
            float x = x0 + (x1 - x0) * i / n, t = float.MaxValue, b = float.MinValue;
            foreach (var (px, py, pr) in puffs)
            {
                float d = x - px;
                if (MathF.Abs(d) >= pr) continue;
                float h = MathF.Sqrt(pr * pr - d * d);
                t = MathF.Min(t, py - h);
                b = MathF.Max(b, py + h);
            }
            if (t == float.MaxValue) continue;
            top.Add(P(r, x, t));
            bottom.Add(P(r, x, MathF.Min(b, yb)));
        }
        bottom.Reverse();
        top.AddRange(bottom);
        Fill(c, col, top.ToArray());
    }

    /// <summary>A wavy band from x0 to x1 about y, so many waves along it.</summary>
    static Vector2[] Wavy(Rect2 r, float x0, float x1, float y, float amp, float waves)
    {
        const int n = 16;
        var pts = new Vector2[n + 1];
        for (int i = 0; i <= n; i++)
        {
            float t = i / (float)n;
            pts[i] = P(r, x0 + (x1 - x0) * t, y + amp * MathF.Sin(t * waves * MathF.Tau));
        }
        return pts;
    }

    /// <summary>A gust curling into a spiral about (cx, cy), rad across its first turn; its tail runs back from the top to the right (dir 1) or from the bottom to the left (dir -1).</summary>
    static Vector2[] Gust(Rect2 r, float cx, float cy, float rad, float tail, int dir)
    {
        float a0 = dir > 0 ? -MathF.PI / 2 : MathF.PI / 2;
        var pts = new List<Vector2> { P(r, cx + dir * tail, cy + MathF.Sin(a0) * rad) };
        const int n = 26;
        for (int i = 0; i <= n; i++)
        {
            float t = i / (float)n, a = a0 - t * 1.6f * MathF.PI, rr = rad * (1 - .62f * t);
            pts.Add(P(r, cx + MathF.Cos(a) * rr, cy + MathF.Sin(a) * rr));
        }
        return pts.ToArray();
    }

    /// <summary>A rough stone about (cx, cy), s from its middle to its edge, turned by rot: in shadow, its upper faces catching the light.</summary>
    static void Rock(CanvasItem c, Rect2 r, Color ink, float cx, float cy, float s, float rot)
    {
        var k = new[] { 1f, .8f, .95f, .78f, 1f, .85f, .92f };
        var pts = new Vector2[k.Length];
        var lit = new Vector2[k.Length];
        for (int i = 0; i < k.Length; i++)
        {
            float a = rot + i * MathF.Tau / k.Length;
            pts[i] = P(r, cx + MathF.Cos(a) * s * k[i], cy + MathF.Sin(a) * s * k[i]);
            lit[i] = P(r, cx - s * .14f + MathF.Cos(a) * s * k[i] * .72f, cy - s * .16f + MathF.Sin(a) * s * k[i] * .72f);
        }
        Fill(c, Shade(ink, .6f), pts);
        c.DrawColoredPolygon(lit, ink);
    }

    // ---- the circle of the earth ----

    /// <summary>A hand of earth rising open out of its mound, clods dropping from it.</summary>
    public static void EarthHand(CanvasItem c, Rect2 r, Color ink)
    {
        Palm(c, Sub(r, .19f, .06f, .64f), ink);
        foreach (var (x, y, rad) in new[] { (.22f, .52f, .03f), (.8f, .42f, .026f), (.78f, .55f, .032f) })
            Dot(c, P(r, x, y), S(r, rad), Shade(ink, .6f));
        Fill(c, Shade(ink, .5f), Ring(r, .5f, .84f, .3f, .2f, MathF.PI, MathF.Tau, 16));
        foreach (var (x, y) in new[] { (.36f, .76f), (.5f, .71f), (.62f, .78f), (.44f, .81f) })
            c.DrawCircle(P(r, x, y), S(r, .025f), Hollow(ink, .5f), true, -1, true);
    }

    /// <summary>The ground split open into a pit, its lips broken and clods tumbling in.</summary>
    public static void Rift(CanvasItem c, Rect2 r, Color ink)
    {
        var ground = new List<Vector2> { P(r, .07f, .42f), P(r, .93f, .42f) };
        ground.AddRange(Ring(r, .5f, .5f, .44f, .44f, .19f, MathF.PI - .19f, 14));
        Fill(c, Shade(ink, .5f), ground.ToArray());
        c.DrawColoredPolygon(new[] { P(r, .29f, .42f), P(r, .72f, .42f), P(r, .66f, .52f), P(r, .62f, .62f), P(r, .56f, .74f), P(r, .5f, .86f), P(r, .44f, .72f),
            P(r, .34f, .62f), P(r, .37f, .52f) }, Hollow(ink, .9f));
        Line(c, Shade(ink, .8f), S(r, .035f), P(r, .29f, .42f), P(r, .37f, .52f), P(r, .34f, .62f), P(r, .44f, .72f), P(r, .5f, .86f));
        Line(c, Shade(ink, .7f), S(r, .035f), P(r, .72f, .42f), P(r, .66f, .52f), P(r, .62f, .62f), P(r, .56f, .74f), P(r, .5f, .86f));
        Line(c, ink, S(r, .06f), P(r, .07f, .42f), P(r, .29f, .42f));
        Line(c, ink, S(r, .06f), P(r, .72f, .42f), P(r, .93f, .42f));
        Fill(c, ink, P(r, .42f, .46f), P(r, .5f, .44f), P(r, .52f, .52f), P(r, .44f, .54f));
        Fill(c, Shade(ink, .85f), P(r, .53f, .6f), P(r, .59f, .57f), P(r, .6f, .64f), P(r, .54f, .66f));
        foreach (var (x0, y0, x1, y1) in new[] { (.2f, .42f, .16f, .56f), (.82f, .42f, .86f, .54f) })
            c.DrawLine(P(r, x0, y0), P(r, x1, y1), Hollow(ink, .6f), 1.2f, true);
    }

    /// <summary>A bog: its slow ripples, bubbles rising out of it and reeds standing in it.</summary>
    public static void Bog(CanvasItem c, Rect2 r, Color ink)
    {
        foreach (var (x0, y0, x1, y1) in new[] { (.74f, .62f, .72f, .2f), (.84f, .6f, .85f, .34f) })
        {
            Line(c, Shade(ink, .7f), S(r, .035f), P(r, x0, y0), P(r, x1, y1));
            Line(c, ink, S(r, .075f), P(r, x1, y1 + .03f), P(r, x1 + .005f, y1 + .14f));
        }
        foreach (var (y, x0, x1, k) in new[] { (.6f, .12f, .88f, .9f), (.72f, .14f, .86f, .72f), (.84f, .26f, .74f, .55f) })
            Line(c, Shade(ink, k), S(r, .05f), Wavy(r, x0, x1, y, .025f, 2.5f));
        foreach (var (x, y, rad) in new[] { (.32f, .42f, .075f), (.48f, .28f, .05f), (.34f, .18f, .035f), (.56f, .46f, .04f) })
        {
            Line(c, ink, S(r, .035f), Ring(r, x, y, rad, rad, 0, MathF.Tau, 14));
            c.DrawCircle(P(r, x - rad * .35f, y - rad * .35f), S(r, rad * .3f), Shade(ink, 1.2f), true, -1, true);
        }
    }

    /// <summary>A shield of coursed stone with a glint on it: the walls made hard as the rock.</summary>
    public static void StoneSkin(CanvasItem c, Rect2 r, Color ink)
    {
        var shield = new[] { P(r, .22f, .16f), P(r, .78f, .16f), P(r, .78f, .5f), P(r, .72f, .7f), P(r, .5f, .9f), P(r, .28f, .7f), P(r, .22f, .5f) };
        Fill(c, Shade(ink, .6f), shield);
        static float Half(float y) => y <= .5f ? .28f : y <= .7f ? .28f - (y - .5f) * .3f : .22f - (y - .7f) * 1.1f;
        var mortar = Hollow(ink, .6f);
        float w = Math.Max(1f, S(r, .025f));
        foreach (float y in new[] { .32f, .48f, .64f, .78f })
            c.DrawLine(P(r, .5f - Half(y), y), P(r, .5f + Half(y), y), mortar, w, true);
        foreach (var (y0, y1, xs) in new[] { (.16f, .32f, new[] { .4f, .6f }), (.32f, .48f, new[] { .3f, .5f, .7f }), (.48f, .64f, new[] { .4f, .6f }), (.64f, .78f, new[] { .5f }) })
            foreach (float x in xs)
                c.DrawLine(P(r, x, y0), P(r, x, y1), mortar, w, true);
        Line(c, ink, S(r, .06f), shield[0], shield[1], shield[2], shield[3], shield[4], shield[5], shield[6], shield[0]);
        var glint = new Vector2[8];
        for (int i = 0; i < 8; i++)
        {
            float a = i * MathF.PI / 4, rad = i % 2 == 0 ? .13f : .03f;
            glint[i] = P(r, .64f + MathF.Cos(a) * rad, .34f + MathF.Sin(a) * rad);
        }
        Fill(c, Shade(ink, 1.25f), glint);
    }

    /// <summary>Rocks breaking off a ledge and falling.</summary>
    public static void Rockfall(CanvasItem c, Rect2 r, Color ink)
    {
        // the crag: a ledge jutting out over open air, broken off at its end
        Fill(c, Shade(ink, .55f), P(r, .07f, .34f), P(r, .28f, .2f), P(r, .58f, .16f), P(r, .6f, .26f), P(r, .52f, .3f), P(r, .48f, .38f), P(r, .36f, .4f), P(r, .32f, .54f),
            P(r, .36f, .68f), P(r, .28f, .86f), P(r, .14f, .8f), P(r, .07f, .62f));
        Line(c, ink, S(r, .06f), P(r, .07f, .34f), P(r, .28f, .2f), P(r, .58f, .16f));
        foreach (var (x0, y0, x1, y1) in new[] { (.12f, .48f, .3f, .44f), (.12f, .66f, .3f, .62f) })
            c.DrawLine(P(r, x0, y0), P(r, x1, y1), Hollow(ink, .45f), Math.Max(1f, S(r, .025f)), true);
        // what broke off, falling
        foreach (var (x, y, s, rot) in new[] { (.68f, .4f, .12f, .3f), (.74f, .68f, .09f, 1.2f), (.5f, .76f, .08f, 2f) })
        {
            foreach (float dx in new[] { -.035f, .035f })
                Line(c, Shade(ink, .5f), S(r, .035f), P(r, x + dx, y - s - .03f), P(r, x + dx - .01f, y - s - .12f));
            Rock(c, r, ink, x, y, s, rot);
        }
    }

    /// <summary>The ground heaved apart along a jagged fissure, one side up and one side down, clods thrown out of the crack.</summary>
    public static void Quake(CanvasItem c, Rect2 r, Color ink)
    {
        // each side runs round the rim of the disc and back up the crack
        var left = new List<Vector2>(Ring(r, .5f, .5f, .44f, .44f, 1.73f, 3.0f, 10)) { P(r, .46f, .46f), P(r, .35f, .6f), P(r, .47f, .7f), P(r, .37f, .82f) };
        Fill(c, Shade(ink, .7f), left.ToArray());
        var right = new List<Vector2> { P(r, .55f, .57f) };
        right.AddRange(Ring(r, .5f, .5f, .44f, .44f, .27f, 1.46f, 10));
        right.AddRange(new[] { P(r, .47f, .84f), P(r, .57f, .72f), P(r, .45f, .62f) });
        Fill(c, Shade(ink, .55f), right.ToArray());
        Line(c, ink, S(r, .06f), left[10], P(r, .46f, .46f));
        Line(c, Shade(ink, .9f), S(r, .06f), P(r, .55f, .57f), right[1]);
        foreach (var (x, y, s, rot) in new[] { (.52f, .32f, .07f, .4f), (.36f, .3f, .05f, 1.4f), (.66f, .4f, .05f, 2.4f) })
            Rock(c, r, ink, x, y, s, rot);
    }

    // ---- the circle of fire and the sky ----

    /// <summary>A dark cloud raining fire.</summary>
    public static void FireRain(CanvasItem c, Rect2 r, Color ink)
    {
        foreach (var (x, y) in new[] { (.17f, .46f), (.39f, .54f), (.61f, .46f), (.29f, .7f), (.51f, .72f) })
            Flame(c, Sub(r, x, y, .23f), ink);
        Cloud(c, r, Shade(ink, .55f), .5f, .28f, .78f);
    }

    /// <summary>A forked bolt of lightning.</summary>
    public static void Thunderbolt(CanvasItem c, Rect2 r, Color ink)
    {
        Line(c, Shade(ink, .8f), S(r, .05f), P(r, .42f, .6f), P(r, .3f, .68f), P(r, .3f, .76f), P(r, .2f, .82f));
        Fill(c, ink, P(r, .56f, .06f), P(r, .69f, .07f), P(r, .55f, .38f), P(r, .74f, .38f), P(r, .37f, .94f), P(r, .47f, .52f), P(r, .28f, .52f));
        c.DrawLine(P(r, .6f, .1f), P(r, .38f, .48f), Shade(ink, 1.25f), Math.Max(1f, S(r, .025f)), true);
    }

    /// <summary>A cloud, and the rain slanting out of it.</summary>
    public static void Storm(CanvasItem c, Rect2 r, Color ink)
    {
        foreach (var (x0, y0, x1, y1) in new[] { (.3f, .56f, .22f, .78f), (.44f, .58f, .34f, .88f), (.58f, .56f, .48f, .86f), (.72f, .56f, .64f, .8f) })
            Line(c, Water(ink), S(r, .04f), P(r, x0, y0), P(r, x1, y1));
        Cloud(c, r, Shade(ink, .85f), .5f, .36f, .8f);
    }

    /// <summary>Black wind: two gusts curling into dark spirals.</summary>
    public static void BlackWind(CanvasItem c, Rect2 r, Color ink)
    {
        foreach (var (cx, cy, rad, tail, dir) in new[] { (.36f, .36f, .17f, .44f, 1), (.62f, .66f, .15f, .44f, -1) })
        {
            var g = Gust(r, cx, cy, rad, tail, dir);
            Line(c, Shade(ink, .9f), S(r, .09f), g);
            c.DrawPolyline(g, Shade(ink, .25f), S(r, .042f), true);
        }
    }

    /// <summary>A crescent moon, blood running down from it.</summary>
    public static void BloodMoon(CanvasItem c, Rect2 r, Color ink)
    {
        // the moon's disc less a second disc laid over it: out along the one rim between where they cross, back along the other
        Vector2 c1 = new(.46f, .42f), c2 = new(.6f, .32f);
        const float r1 = .34f, r2 = .3f;
        float d = (c2 - c1).Length(), a = (r1 * r1 - r2 * r2 + d * d) / (2 * d), h = MathF.Sqrt(r1 * r1 - a * a);
        var along = (c2 - c1) / d;
        var across = new Vector2(-along.Y, along.X);
        Vector2 x1 = c1 + along * a + across * h, x2 = c1 + along * a - across * h;
        static float Angle(Vector2 v) => MathF.Atan2(v.Y, v.X);
        float o1 = Angle(x1 - c1), o2 = Angle(x2 - c1), n1 = Angle(x1 - c2), n2 = Angle(x2 - c2);
        if (o2 < o1) o2 += MathF.Tau;
        if (n2 < n1) n2 += MathF.Tau;
        var moon = new List<Vector2>(Ring(r, c1.X, c1.Y, r1, r1, o1, o2, 24));
        var back = Ring(r, c2.X, c2.Y, r2, r2, n2, n1, 18);
        for (int i = 1; i < back.Length - 1; i++) moon.Add(back[i]);
        Fill(c, ink, moon.ToArray());
        foreach (var (x, y0, y1, s) in new[] { (.4f, .72f, .82f, .065f), (.55f, .7f, .78f, .05f) })
        {
            Line(c, Gore(ink), S(r, .04f), P(r, x, y0), P(r, x, y1 - s));
            Drop(c, r, x, y1, s, Gore(ink));
        }
    }

    /// <summary>A burning rock falling slantwise out of the sky, its tail of fire streaming behind.</summary>
    public static void Meteor(CanvasItem c, Rect2 r, Color ink)
    {
        const float ang = -MathF.PI / 4, ox = .34f, oy = .66f;
        Fill(c, Shade(ink, .85f), A(r, ox, oy, ang, 0, -.19f), A(r, ox, oy, ang, .2f, -.15f), A(r, ox, oy, ang, .3f, -.2f), A(r, ox, oy, ang, .42f, -.1f),
            A(r, ox, oy, ang, .54f, -.13f), A(r, ox, oy, ang, .66f, 0), A(r, ox, oy, ang, .5f, .1f), A(r, ox, oy, ang, .38f, .08f), A(r, ox, oy, ang, .26f, .17f),
            A(r, ox, oy, ang, .14f, .13f), A(r, ox, oy, ang, 0, .19f));
        c.DrawColoredPolygon(new[] { A(r, ox, oy, ang, 0, -.11f), A(r, ox, oy, ang, .22f, -.08f), A(r, ox, oy, ang, .4f, -.04f), A(r, ox, oy, ang, .5f, 0),
            A(r, ox, oy, ang, .36f, .04f), A(r, ox, oy, ang, .2f, .08f), A(r, ox, oy, ang, 0, .11f) }, Shade(ink, 1.2f));
        Rock(c, r, Shade(ink, .55f), ox, oy, .17f, .4f);
    }

    // ---- the circle of death ----

    /// <summary>A face struck with dread: the eyes wide, the mouth open in a howl.</summary>
    public static void Dread(CanvasItem c, Rect2 r, Color ink)
    {
        Fill(c, Shade(ink, .85f), P(r, .5f, .06f), P(r, .7f, .12f), P(r, .78f, .32f), P(r, .74f, .54f), P(r, .66f, .78f), P(r, .5f, .94f), P(r, .34f, .78f),
            P(r, .26f, .54f), P(r, .22f, .32f), P(r, .3f, .12f));
        var dark = Hollow(ink, .85f);
        foreach (int s in new[] { -1, 1 })
        {
            Line(c, Shade(ink, .4f), S(r, .04f), P(r, .5f + s * .06f, .22f), P(r, .5f + s * .22f, .29f));
            c.DrawEllipse(P(r, .5f + s * .12f, .38f), S(r, .07f), S(r, .095f), dark, true, -1, true);
            c.DrawCircle(P(r, .5f + s * .12f, .39f), S(r, .025f), Shade(ink, 1.2f), true, -1, true);
            c.DrawLine(P(r, .5f + s * .18f, .52f), P(r, .5f + s * .14f, .7f), Hollow(ink, .45f), Math.Max(1f, S(r, .025f)), true);
        }
        c.DrawEllipse(P(r, .5f, .7f), S(r, .085f), S(r, .15f), dark, true, -1, true);
    }

    /// <summary>A skull in banks of fog.</summary>
    public static void DeadFog(CanvasItem c, Rect2 r, Color ink)
    {
        Line(c, Shade(ink, .5f), S(r, .07f), Wavy(r, .1f, .9f, .3f, .025f, 2));
        Skull(c, Sub(r, .2f, .06f, .6f), ink);
        foreach (var (y, x0, x1, k) in new[] { (.54f, .1f, .9f, .78f), (.68f, .12f, .88f, .64f), (.82f, .24f, .76f, .5f) })
            Line(c, Shade(ink, k), S(r, .08f), Wavy(r, x0, x1, y, .025f, 2));
    }

    /// <summary>Two shackles, the chain between them.</summary>
    public static void Shackles(CanvasItem c, Rect2 r, Color ink)
    {
        Line(c, Shade(ink, .8f), S(r, .035f), Ring(r, .37f, .43f, .075f, .05f, 0, MathF.Tau, 12));
        Line(c, Shade(ink, .8f), S(r, .035f), Ring(r, .63f, .43f, .075f, .05f, 0, MathF.Tau, 12));
        Line(c, Shade(ink, .9f), S(r, .05f), P(r, .45f, .39f), P(r, .55f, .39f));
        foreach (float x in new[] { .26f, .74f })
        {
            Line(c, ink, S(r, .07f), Ring(r, x, .66f, .13f, .13f, 0, MathF.Tau, 20));
            Dot(c, P(r, x, .52f), S(r, .045f), Shade(ink, .9f));
            Dot(c, P(r, x, .8f), S(r, .04f), Shade(ink, .6f));
        }
    }

    /// <summary>A stoppered flask of something sickly, bubbling, a skull on it.</summary>
    public static void Plague(CanvasItem c, Rect2 r, Color ink)
    {
        Fill(c, Shade(ink, .7f), P(r, .43f, .24f), P(r, .57f, .24f), P(r, .57f, .42f), P(r, .43f, .42f));
        Dot(c, P(r, .5f, .64f), S(r, .27f), Shade(ink, .72f));
        c.DrawColoredPolygon(Ring(r, .5f, .64f, .22f, .22f, -.37f, MathF.PI + .37f, 16), Bane(ink));
        foreach (var (x, y, rad) in new[] { (.34f, .68f, .025f), (.66f, .62f, .03f), (.62f, .76f, .02f) })
            c.DrawCircle(P(r, x, y), S(r, rad), Shade(Bane(ink), 1.35f), true, -1, true);
        Skull(c, Sub(r, .33f, .46f, .34f), ink);
        Line(c, ink, S(r, .05f), P(r, .4f, .24f), P(r, .6f, .24f));
        Fill(c, Shade(ink, .5f), P(r, .44f, .11f), P(r, .56f, .11f), P(r, .55f, .23f), P(r, .45f, .23f));
        Line(c, Shade(ink, 1.15f), S(r, .03f), Ring(r, .5f, .64f, .2f, .2f, 3.6f, 4.3f, 5));
    }

    /// <summary>A gravestone, and before it a bony hand clawing up out of the grave.</summary>
    public static void Raise(CanvasItem c, Rect2 r, Color ink)
    {
        var stone = new List<Vector2>(Ring(r, .58f, .34f, .22f, .2f, MathF.PI, MathF.Tau, 12)) { P(r, .8f, .78f), P(r, .36f, .78f) };
        Fill(c, Shade(ink, .5f), stone.ToArray());
        c.DrawLine(P(r, .58f, .2f), P(r, .58f, .48f), Hollow(ink, .7f), Math.Max(1.2f, S(r, .04f)), true);
        c.DrawLine(P(r, .49f, .3f), P(r, .67f, .3f), Hollow(ink, .7f), Math.Max(1.2f, S(r, .04f)), true);
        Fill(c, Shade(ink, .65f), Ring(r, .5f, .86f, .36f, .1f, MathF.PI, MathF.Tau, 12));
        var bone = Shade(ink, 1.1f);
        Line(c, bone, S(r, .05f), P(r, .33f, .86f), P(r, .33f, .62f));
        Line(c, bone, S(r, .05f), P(r, .39f, .86f), P(r, .37f, .62f));
        foreach (var (x, y) in new[] { (.2f, .42f), (.27f, .36f), (.35f, .35f), (.43f, .4f) })
        {
            Line(c, bone, S(r, .045f), P(r, .35f, .58f), P(r, (x + .35f) / 2, (y + .58f) / 2 - .02f), P(r, x, y));
            Dot(c, P(r, x, y), S(r, .028f), bone);
        }
        Line(c, bone, S(r, .045f), P(r, .38f, .6f), P(r, .46f, .55f), P(r, .5f, .5f));
        Dot(c, P(r, .35f, .6f), S(r, .06f), bone);
    }

    /// <summary>A scythe: the long curved snath with its grips, the blade sweeping out from the top.</summary>
    public static void Scythe(CanvasItem c, Rect2 r, Color ink)
    {
        Line(c, Shade(ink, .75f), S(r, .06f), P(r, .66f, .9f), P(r, .6f, .66f), P(r, .58f, .4f), P(r, .62f, .14f));
        Line(c, Shade(ink, .7f), S(r, .05f), P(r, .6f, .62f), P(r, .46f, .6f));
        Line(c, Shade(ink, .7f), S(r, .05f), P(r, .59f, .36f), P(r, .71f, .33f));
        Fill(c, ink, P(r, .64f, .12f), P(r, .5f, .08f), P(r, .36f, .1f), P(r, .22f, .17f), P(r, .1f, .36f), P(r, .19f, .27f), P(r, .31f, .2f), P(r, .45f, .17f),
            P(r, .56f, .19f), P(r, .63f, .21f));
        Line(c, Shade(ink, 1.2f), S(r, .02f), P(r, .12f, .33f), P(r, .19f, .27f), P(r, .31f, .2f), P(r, .45f, .17f), P(r, .56f, .19f));
        Dot(c, P(r, .62f, .16f), S(r, .045f), Shade(ink, .6f));
    }

    // ---- the circle of the will ----

    /// <summary>An open hand held up, an orb hanging in the air over it.</summary>
    public static void Telekinesis(CanvasItem c, Rect2 r, Color ink)
    {
        Palm(c, Sub(r, .2f, .36f, .6f), Shade(ink, .9f));
        foreach (var (rad, k) in new[] { (.17f, .8f), (.23f, .55f) })
            Line(c, Shade(ink, k), S(r, .035f), Ring(r, .5f, .2f, rad, rad, .55f, MathF.PI - .55f, 8));
        Dot(c, P(r, .5f, .2f), S(r, .12f), ink);
        c.DrawCircle(P(r, .46f, .16f), S(r, .04f), Shade(ink, 1.25f), true, -1, true);
    }

    /// <summary>A tower, and an arrow curving round into its door: one of ours called home.</summary>
    public static void Recall(CanvasItem c, Rect2 r, Color ink)
    {
        Fill(c, Shade(ink, .62f), P(r, .54f, .26f), P(r, .8f, .26f), P(r, .8f, .84f), P(r, .54f, .84f));
        foreach (float x in new[] { .54f, .635f, .73f }) Fill(c, Shade(ink, .62f), P(r, x, .27f), P(r, x, .16f), P(r, x + .07f, .16f), P(r, x + .07f, .27f));
        c.DrawRect(new Rect2(P(r, .65f, .36f), new Vector2(S(r, .04f), S(r, .1f))), Hollow(ink), true);
        var door = new List<Vector2> { P(r, .6f, .84f) };
        door.AddRange(Ring(r, .67f, .66f, .07f, .07f, MathF.PI, MathF.Tau, 6));
        door.Add(P(r, .74f, .84f));
        c.DrawColoredPolygon(door.ToArray(), Hollow(ink));
        Line(c, ink, S(r, .065f), Ring(r, .52f, .34f, .36f, .42f, MathF.PI, MathF.PI / 2 + .1f, 12));
        Fill(c, ink, P(r, .47f, .67f), P(r, .63f, .76f), P(r, .47f, .85f));
    }

    /// <summary>A blast: a star of fire, and the pieces it tore loose flying out of it.</summary>
    public static void Burst(CanvasItem c, Rect2 r, Color ink)
    {
        const int n = 10;
        var len = new[] { .32f, .26f, .33f, .27f, .3f, .33f, .25f, .31f, .28f, .33f };
        var star = new Vector2[n * 2];
        var core = new Vector2[n * 2];
        for (int i = 0; i < n * 2; i++)
        {
            float a = i * MathF.PI / n - MathF.PI / 2, rad = i % 2 == 0 ? len[i / 2] : .14f;
            star[i] = P(r, .5f + MathF.Cos(a) * rad, .5f + MathF.Sin(a) * rad);
            core[i] = P(r, .5f + MathF.Cos(a) * rad * .55f, .5f + MathF.Sin(a) * rad * .55f);
        }
        Fill(c, Shade(ink, .72f), star);
        c.DrawColoredPolygon(core, Shade(ink, 1.15f));
        for (int k = 0; k < 6; k++)
        {
            float a = k * MathF.Tau / 6 + .3f, s = .045f, t = a * .7f;
            var at = new Vector2(.5f + MathF.Cos(a) * .4f, .5f + MathF.Sin(a) * .4f);
            Line(c, Shade(ink, .6f), S(r, .03f), P(r, .5f + MathF.Cos(a) * .27f, .5f + MathF.Sin(a) * .27f), P(r, .5f + MathF.Cos(a) * .33f, .5f + MathF.Sin(a) * .33f));
            Fill(c, ink, P(r, at.X + MathF.Cos(t) * s, at.Y + MathF.Sin(t) * s), P(r, at.X - MathF.Sin(t) * s, at.Y + MathF.Cos(t) * s),
                P(r, at.X - MathF.Cos(t) * s, at.Y - MathF.Sin(t) * s), P(r, at.X + MathF.Sin(t) * s, at.Y - MathF.Cos(t) * s));
        }
    }

    /// <summary>A dagger point down, a drop of blood falling from its tip: the oath sworn in blood.</summary>
    public static void Oath(CanvasItem c, Rect2 r, Color ink)
    {
        Line(c, Shade(ink, .7f), S(r, .1f), P(r, .5f, .14f), P(r, .5f, .29f));
        Dot(c, P(r, .5f, .1f), S(r, .065f), ink);
        Fill(c, ink, P(r, .42f, .34f), P(r, .58f, .34f), P(r, .56f, .58f), P(r, .5f, .74f), P(r, .44f, .58f));
        c.DrawLine(P(r, .5f, .36f), P(r, .5f, .6f), Hollow(ink, .5f), Math.Max(1f, S(r, .025f)), true);
        Line(c, ink, S(r, .085f), P(r, .27f, .32f), P(r, .73f, .32f));
        c.DrawColoredPolygon(new[] { P(r, .455f, .6f), P(r, .545f, .6f), P(r, .5f, .74f) }, Gore(ink));
        Drop(c, r, .5f, .855f, .075f, Gore(ink));
    }

    /// <summary>A whirlwind: a twisting funnel of wind, bits of the ground caught up in it.</summary>
    public static void Whirlwind(CanvasItem c, Rect2 r, Color ink)
    {
        for (int i = 0; i < 6; i++)
        {
            float t = i / 5f, y = .18f + t * .66f, w = .3f * (1 - t) + .05f, x = .5f + .08f * MathF.Sin(t * 4f);
            Line(c, Shade(ink, .5f), S(r, .04f), Ring(r, x, y, w, w * .22f, MathF.PI + .2f, MathF.Tau - .2f, 8));
            Line(c, Shade(ink, 1f - t * .25f), S(r, .06f), Ring(r, x, y, w, w * .22f, -.1f, MathF.PI + .1f, 10));
        }
        foreach (var (x, y, a) in new[] { (.16f, .44f, .4f), (.84f, .34f, 1.1f), (.8f, .62f, .2f), (.24f, .68f, .9f) })
        {
            float s = .035f;
            Fill(c, Shade(ink, .85f), P(r, x + MathF.Cos(a) * s, y + MathF.Sin(a) * s), P(r, x - MathF.Sin(a) * s, y + MathF.Cos(a) * s),
                P(r, x - MathF.Cos(a) * s, y - MathF.Sin(a) * s), P(r, x + MathF.Sin(a) * s, y - MathF.Cos(a) * s));
        }
    }

    // ---- grace ----

    /// <summary>A loop of prayer beads, and the cross hanging from where its ends meet.</summary>
    public static void Rosary(CanvasItem c, Rect2 r, Color ink)
    {
        Line(c, Shade(ink, .5f), S(r, .02f), Ring(r, .5f, .38f, .3f, .25f, 0, MathF.Tau, 24));
        for (int k = 1; k <= 12; k++)
        {
            float a = MathF.PI / 2 + k * MathF.Tau / 13;
            Dot(c, P(r, .5f + MathF.Cos(a) * .3f, .38f + MathF.Sin(a) * .25f), S(r, .048f), k % 2 == 0 ? ink : Shade(ink, .85f));
        }
        Line(c, Shade(ink, .6f), S(r, .025f), P(r, .5f, .63f), P(r, .5f, .76f));
        Dot(c, P(r, .5f, .63f), S(r, .055f), Shade(ink, 1.1f));
        Dot(c, P(r, .5f, .7f), S(r, .038f), ink);
        Line(c, ink, S(r, .055f), P(r, .5f, .75f), P(r, .5f, .93f));
        Line(c, ink, S(r, .055f), P(r, .43f, .81f), P(r, .57f, .81f));
    }

    /// <summary>A chalice: the deep bowl, the knop on its stem and the broad foot.</summary>
    public static void Chalice(CanvasItem c, Rect2 r, Color ink)
    {
        Fill(c, ink, Ring(r, .5f, .18f, .3f, .34f, MathF.PI, 0, 14));
        c.DrawColoredPolygon(new[] { P(r, .5f, .18f), P(r, .8f, .18f), P(r, .77f, .32f), P(r, .7f, .44f), P(r, .6f, .5f), P(r, .5f, .52f) }, Shade(ink, .75f));
        c.DrawEllipse(P(r, .5f, .18f), S(r, .3f), S(r, .055f), Shade(ink, 1.1f), true, -1, true);
        c.DrawEllipse(P(r, .5f, .185f), S(r, .24f), S(r, .032f), Hollow(ink, .6f), true, -1, true);
        Line(c, Shade(ink, .85f), S(r, .07f), P(r, .5f, .5f), P(r, .5f, .8f));
        Dot(c, P(r, .5f, .64f), S(r, .07f), ink);
        Fill(c, ink, P(r, .45f, .76f), P(r, .55f, .76f), P(r, .7f, .86f), P(r, .7f, .9f), P(r, .3f, .9f), P(r, .3f, .86f));
    }

    /// <summary>A stone altar, a flame burning on its top.</summary>
    public static void Altar(CanvasItem c, Rect2 r, Color ink)
    {
        Flame(c, Sub(r, .28f, .02f, .44f), ink);
        Fill(c, Shade(ink, .6f), P(r, .24f, .5f), P(r, .76f, .5f), P(r, .76f, .8f), P(r, .24f, .8f));
        c.DrawLine(P(r, .5f, .58f), P(r, .5f, .74f), Hollow(ink, .6f), Math.Max(1.2f, S(r, .035f)), true);
        c.DrawLine(P(r, .43f, .63f), P(r, .57f, .63f), Hollow(ink, .6f), Math.Max(1.2f, S(r, .035f)), true);
        Fill(c, ink, P(r, .14f, .43f), P(r, .86f, .43f), P(r, .86f, .51f), P(r, .14f, .51f));
        Fill(c, Shade(ink, .8f), P(r, .19f, .8f), P(r, .81f, .8f), P(r, .81f, .87f), P(r, .19f, .87f));
    }

    /// <summary>An open book, rays of light rising out of it: the litany read aloud.</summary>
    public static void Litany(CanvasItem c, Rect2 r, Color ink)
    {
        for (int i = 0; i < 7; i++)
        {
            float a = -MathF.PI + .45f + i * (MathF.PI - .9f) / 6, rad = i % 2 == 0 ? .42f : .33f;
            Line(c, Shade(ink, i % 2 == 0 ? 1f : .75f), S(r, .045f), P(r, .5f + MathF.Cos(a) * .18f, .58f + MathF.Sin(a) * .18f), P(r, .5f + MathF.Cos(a) * rad, .58f + MathF.Sin(a) * rad));
        }
        Fill(c, Shade(ink, .5f), P(r, .12f, .62f), P(r, .5f, .7f), P(r, .88f, .62f), P(r, .88f, .82f), P(r, .5f, .9f), P(r, .12f, .82f));
        Fill(c, Shade(ink, .95f), P(r, .16f, .57f), P(r, .48f, .64f), P(r, .48f, .85f), P(r, .16f, .78f));
        Fill(c, Shade(ink, .82f), P(r, .52f, .64f), P(r, .84f, .57f), P(r, .84f, .78f), P(r, .52f, .85f));
        var dark = Hollow(ink, .5f);
        foreach (float f in new[] { .3f, .6f })
        {
            c.DrawLine(P(r, .21f, .58f + .21f * f), P(r, .43f, .63f + .21f * f), dark, 1.1f, true);
            c.DrawLine(P(r, .57f, .63f + .21f * f), P(r, .79f, .58f + .21f * f), dark, 1.1f, true);
        }
    }

    /// <summary>A candle burning in its dish, a halo round the flame: the vigil kept.</summary>
    public static void Candle(CanvasItem c, Rect2 r, Color ink)
    {
        c.DrawArc(P(r, .5f, .2f), S(r, .17f), 0, MathF.Tau, 28, Shade(ink, .5f), Math.Max(1f, S(r, .03f)), true);
        Flame(c, Sub(r, .37f, .04f, .26f), ink);
        Fill(c, Shade(ink, .88f), P(r, .41f, .34f), P(r, .59f, .34f), P(r, .59f, .8f), P(r, .41f, .8f));
        Fill(c, Shade(ink, 1.05f), P(r, .41f, .34f), P(r, .47f, .34f), P(r, .45f, .48f), P(r, .41f, .44f));
        c.DrawLine(P(r, .5f, .27f), P(r, .5f, .34f), Hollow(ink), Math.Max(1f, S(r, .02f)), true);
        Fill(c, Shade(ink, .7f), Ring(r, .5f, .8f, .28f, .09f, 0, MathF.PI, 12));
        Line(c, ink, S(r, .045f), P(r, .2f, .8f), P(r, .8f, .8f));
        Line(c, Shade(ink, .7f), S(r, .04f), Ring(r, .82f, .76f, .06f, .06f, -1.6f, 1.6f, 6));
    }

    /// <summary>A fist raised in a burst of rays: the strength the old powers lend.</summary>
    public static void Might(CanvasItem c, Rect2 r, Color ink)
    {
        // the rays over it, a fan from one shoulder to the other
        for (int i = 0; i < 9; i++)
        {
            float a = -MathF.PI + .2f + i * (MathF.PI - .4f) / 8, rad = i % 2 == 0 ? .44f : .35f;
            Fill(c, Shade(ink, .6f), P(r, .5f + MathF.Cos(a - .12f) * .25f, .5f + MathF.Sin(a - .12f) * .25f), P(r, .5f + MathF.Cos(a) * rad, .5f + MathF.Sin(a) * rad),
                P(r, .5f + MathF.Cos(a + .12f) * .25f, .5f + MathF.Sin(a + .12f) * .25f));
        }
        // the forearm, and the fist clenched on top of it: four curled fingers, the thumb across them
        Fill(c, Shade(ink, .75f), P(r, .38f, .6f), P(r, .62f, .6f), P(r, .66f, .93f), P(r, .34f, .93f));
        Fill(c, ink, P(r, .3f, .34f), P(r, .7f, .32f), P(r, .72f, .5f), P(r, .66f, .64f), P(r, .36f, .64f), P(r, .29f, .5f));
        foreach (float x in new[] { .35f, .45f, .55f, .65f }) Dot(c, P(r, x, .33f), S(r, .055f), ink);
        foreach (float x in new[] { .4f, .5f, .6f }) c.DrawLine(P(r, x, .31f), P(r, x, .46f), Hollow(ink, .6f), Math.Max(1f, S(r, .03f)), true);
        Line(c, Shade(ink, 1.15f), S(r, .07f), P(r, .31f, .5f), P(r, .55f, .48f));
    }

    // ---- the branches and the page ----

    /// <summary>A mountain with snow on its peak and a lesser one beside it: the circle of the earth.</summary>
    public static void Mountain(CanvasItem c, Rect2 r, Color ink)
    {
        Fill(c, Shade(ink, .5f), P(r, .46f, .76f), P(r, .7f, .32f), P(r, .84f, .56f), P(r, .86f, .76f));
        Fill(c, Shade(ink, .68f), P(r, .14f, .76f), P(r, .44f, .14f), P(r, .72f, .76f));
        Fill(c, ink, P(r, .14f, .76f), P(r, .44f, .14f), P(r, .4f, .4f), P(r, .46f, .56f), P(r, .38f, .76f));
        Fill(c, Shade(ink, 1.2f), P(r, .44f, .14f), P(r, .53f, .32f), P(r, .48f, .29f), P(r, .44f, .34f), P(r, .4f, .29f), P(r, .35f, .32f));
        Line(c, Shade(ink, .55f), S(r, .04f), P(r, .12f, .78f), P(r, .88f, .78f));
    }

    /// <summary>A flame with a bolt of lightning through it: the circle of fire and the sky.</summary>
    public static void FireSky(CanvasItem c, Rect2 r, Color ink)
    {
        Flame(c, r, Shade(ink, .72f));
        Fill(c, Shade(ink, 1.15f), P(r, .53f, .26f), P(r, .62f, .26f), P(r, .55f, .48f), P(r, .65f, .48f), P(r, .42f, .86f), P(r, .48f, .56f), P(r, .38f, .56f));
    }

    /// <summary>A skull over two crossed bones: the circle of death.</summary>
    public static void DeathMark(CanvasItem c, Rect2 r, Color ink)
    {
        foreach (var (x0, y0, x1, y1) in new[] { (.24f, .56f, .76f, .82f), (.76f, .56f, .24f, .82f) })
        {
            Line(c, Shade(ink, .8f), S(r, .07f), P(r, x0, y0), P(r, x1, y1));
            foreach (var (x, y) in new[] { (x0, y0), (x1, y1) })
            {
                var d = new Vector2(x1 - x0, y1 - y0).Normalized();
                var side = new Vector2(-d.Y, d.X) * .035f;
                Dot(c, P(r, x + side.X, y + side.Y), S(r, .045f), Shade(ink, .85f));
                Dot(c, P(r, x - side.X, y - side.Y), S(r, .045f), Shade(ink, .85f));
            }
        }
        Skull(c, Sub(r, .2f, .02f, .6f), ink);
    }

    /// <summary>An open hand with an eye in its palm: the circle of the will.</summary>
    public static void WillHand(CanvasItem c, Rect2 r, Color ink)
    {
        var h = Sub(r, .1f, .04f, .84f);
        Palm(c, h, ink);
        // the eye: its corners once each, its lids between them
        const int n = 10;
        var eye = new List<Vector2> { P(h, .36f, .66f) };
        for (int i = 1; i < n; i++) eye.Add(P(h, .36f + .32f * i / n, .66f - MathF.Sin(MathF.PI * i / n) * .09f));
        eye.Add(P(h, .68f, .66f));
        for (int i = n - 1; i > 0; i--) eye.Add(P(h, .36f + .32f * i / n, .66f + MathF.Sin(MathF.PI * i / n) * .09f));
        c.DrawColoredPolygon(eye.ToArray(), Hollow(ink, .85f));
        c.DrawCircle(P(h, .52f, .66f), S(h, .065f), Shade(ink, 1.15f), true, -1, true);
        c.DrawCircle(P(h, .52f, .66f), S(h, .03f), Hollow(ink, .9f), true, -1, true);
    }

    /// <summary>A sun in its glory, a ring of light about it: grace.</summary>
    public static void Grace(CanvasItem c, Rect2 r, Color ink)
    {
        for (int i = 0; i < 16; i++)
        {
            float a = i * MathF.Tau / 16 - MathF.PI / 2, rad = i % 2 == 0 ? .45f : .35f, w = i % 2 == 0 ? .1f : .08f;
            Fill(c, Shade(ink, i % 2 == 0 ? .85f : .65f), P(r, .5f + MathF.Cos(a - w) * .22f, .5f + MathF.Sin(a - w) * .22f), P(r, .5f + MathF.Cos(a) * rad, .5f + MathF.Sin(a) * rad),
                P(r, .5f + MathF.Cos(a + w) * .22f, .5f + MathF.Sin(a + w) * .22f));
        }
        c.DrawArc(P(r, .5f, .5f), S(r, .25f), 0, MathF.Tau, 32, Shade(ink, .5f), Math.Max(1f, S(r, .03f)), true);
        Dot(c, P(r, .5f, .5f), S(r, .19f), ink);
        c.DrawCircle(P(r, .5f, .5f), S(r, .11f), Shade(ink, 1.2f), true, -1, true);
    }

    /// <summary>The sigil of any node of the tree by its key: the troops' and the rites' here, the rest as SigilOf has them.</summary>
    public static Painter SigilFor(string key) => key switch
    {
        "spearmen" => Spear, "shieldmen" => Pavise, "halberds" => Halberd, "shieldwall" => ShieldWall, "longshafts" => Pikes, "knights" => GreatHelm,
        "slingers" => Sling, "crossbows" => Crossbow, "leadshot" => LeadShot, "bodkins" => Bodkin, "firepots" => FirePot, "bombards" => Bomb,
        "hounds" => Hound, "riders" => Steed, "hunt" => Coursing, "horsearchers" => HorseArcher, "charge" => Couched, "lancers" => Pennon,
        "barracks" => Longhouse, "drillyard" => Wasters, "bunks" => Bunk, "trolls" => Troll, "giants" => Maul, "boulders" => Hurled,
        "spell.Hand" => EarthHand, "spell.Rift" => Rift, "spell.Mire" => Bog, "spell.Skin" => StoneSkin, "spell.Fall" => Rockfall, "spell.Quake" => Quake,
        "spell.FireRain" => FireRain, "spell.Thunder" => Thunderbolt, "spell.Weather" => Storm, "spell.BlackWind" => BlackWind, "spell.BloodMoon" => BloodMoon,
        "spell.Meteor" => Meteor, "spell.Dread" => Dread, "spell.DeadFog" => DeadFog, "spell.Chains" => Shackles, "spell.Plague" => Plague,
        "spell.Raise" => Raise, "reap" => Scythe, "spell.Telekinesis" => Telekinesis, "spell.Recall" => Recall, "spell.Eye" => Eye,
        "spell.Explosion" => Burst, "spell.Oath" => Oath, "spell.Whirlwind" => Whirlwind,
        "rosary" => Rosary, "vessel" => Chalice, "altar" => Altar, "litany" => Litany, "vigil" => Candle, "might" => Might,
        _ => SigilOf(key),
    };

    /// <summary>The great emblem at the outer end of a branch, by the branch's name.</summary>
    public static Painter BranchMark(string branch) => branch switch
    {
        "Луки" => Bow, "Кладка" => Box, "Труд" => Pick, "Дружина" => Sword, "Ловушки" => Flame,
        "Пехота" => Infantry, "Стрелки" => Marksmen, "Конница" => Rearing, "Великаны" => Footprint,
        "Круг земли" => Mountain, "Круг огня и неба" => FireSky, "Круг смерти" => DeathMark, "Круг воли" => WillHand, "Благодать" => Grace,
        _ => Question,
    };

    /// <summary>The emblem of a page of the tree, for its tab and its middle.</summary>
    public static Painter PageMark(int page) => page switch { 0 => Tome, 1 => CrossedSwords, _ => Eye };
}
