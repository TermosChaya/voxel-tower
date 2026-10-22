using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// What the frame of the interface is made of, all drawn in code: oak boards with their grain, knots and seams, dressed
/// stone laid in courses, black iron straps with rivets, brass edging, and dark stained wells where things are read and
/// pressed. The textures are made once, from noise, when the game starts; controls drawing them tile them as they are.
/// </summary>
public static class Craft
{
    public static readonly Color OakDark = new("23170e"), Oak = new("4a3220"), OakLight = new("74512f");
    public static readonly Color StoneDark = new("39353a"), StoneLight = new("7b7573"), Mortar = new("19161a");
    public static readonly Color IronBlack = new("0e0c10"), IronEdge = new("2e2a33");
    public static readonly Color Brass = new("a8803f"), BrassLight = new("e6c47a"), BrassDark = new("4e3819");

    public static readonly ImageTexture Boards = MakeBoards(), Ashlar = MakeAshlar(), Stain = MakeStain();

    // ------------------------------------------------------------------ textures

    /// <summary>Oak boards lying along the width: each its own shade, grain streaked along it, a knot here and there, butt joints.</summary>
    static ImageTexture MakeBoards()
    {
        const int W = 512, H = 128;
        var img = Image.CreateEmpty(W, H, false, Image.Format.Rgba8);
        var grain = new ValueNoise(23); var fine = new ValueNoise(71); var r = new Mulberry32(5);
        int y0 = 0, board = 0;
        while (y0 < H)
        {
            int h = Math.Min(H - y0, 19 + (int)(r.Next() * 9));
            if (H - y0 - h < 15) h = H - y0;
            float tone = .8f + (float)r.Next() * .32f;
            int seam = (int)(r.Next() * W), seam2 = (seam + 170 + (int)(r.Next() * 160)) % W;
            float knotX = (float)(r.Next() * W), knotY = y0 + h * (.3f + (float)r.Next() * .4f);
            bool knot = r.Next() < .6;
            for (int y = y0; y < y0 + h; y++)
                for (int x = 0; x < W; x++)
                {
                    // the grain: noise drawn out along the board, a finer one over it
                    float v = (float)(grain.Sample(x * .016, (y - y0) * .45 + board * 13.7) * .72 + fine.Sample(x * .12, y * .9) * .28);
                    if (knot)
                    {
                        float dx = (x - knotX) / 9f, dy = (y - knotY) / 3.6f;
                        if (dx > W / 18f) dx -= W / 9f;
                        float d = MathF.Sqrt(dx * dx + dy * dy);
                        if (d < 2.6f) v = v * .55f + .45f * (.5f + .5f * MathF.Cos(d * 5.5f));
                        if (d < .7f) v *= .4f;
                    }
                    var c = OakDark.Lerp(OakLight, Math.Clamp(v * 1.2f - .12f, 0, 1));
                    c = new Color(c.R * tone, c.G * tone, c.B * tone);
                    // the board's edges: lit along the top, a dark gap under it
                    int ly = y - y0;
                    if (ly == 0) c = c.Lightened(.16f);
                    else if (ly == h - 1) c = c.Darkened(.72f);
                    else if (ly == h - 2) c = c.Darkened(.3f);
                    if (x == seam || x == seam2) c = c.Darkened(.7f);
                    else if (x == (seam + 1) % W || x == (seam2 + 1) % W) c = c.Lightened(.1f);
                    img.SetPixel(x, y, c);
                }
            y0 += h; board++;
        }
        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>Dressed stone in courses of a hand's height: stones of different lengths and shades, dark joints, each stone bevelled.</summary>
    static ImageTexture MakeAshlar()
    {
        const int W = 256, H = 128, Row = 32;
        var img = Image.CreateEmpty(W, H, false, Image.Format.Rgba8);
        var n1 = new ValueNoise(11); var n2 = new ValueNoise(29); var r = new Mulberry32(9);
        for (int row = 0; row < H / Row; row++)
        {
            int start = (int)(r.Next() * 60);
            var edges = new List<int> { start };
            while (edges[^1] < start + W) edges.Add(edges[^1] + 40 + (int)(r.Next() * 38));
            edges[^1] = start + W;
            var tone = new float[edges.Count]; var warm = new float[edges.Count];
            for (int i = 0; i < tone.Length; i++) { tone[i] = .8f + (float)r.Next() * .34f; warm[i] = (float)(r.Next() - .5) * .07f; }
            for (int y = row * Row; y < (row + 1) * Row; y++)
                for (int px = 0; px < W; px++)
                {
                    int xx = px < start ? px + W : px, b = 0;
                    while (b + 2 < edges.Count && edges[b + 1] <= xx) b++;
                    int lx = xx - edges[b], bw = edges[b + 1] - edges[b], ly = y - row * Row;
                    Color c;
                    if (lx < 2 || ly < 2) c = Mortar;
                    else
                    {
                        float v = (float)(n1.Sample(px * .08, y * .08) * .6 + n2.Sample(px * .4, y * .4) * .4);
                        c = StoneDark.Lerp(StoneLight, Math.Clamp(v * 1.1f - .05f, 0, 1));
                        c = new Color(c.R * tone[b] * (1 + warm[b]), c.G * tone[b], c.B * tone[b] * (1 - warm[b]));
                        if (ly == 2 || lx == 2) c = c.Lightened(.2f);
                        else if (ly == Row - 1 || lx == bw - 1) c = c.Darkened(.32f);
                    }
                    img.SetPixel(px, y, c);
                }
        }
        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>Dark stained wood for the wells, barely grained.</summary>
    static ImageTexture MakeStain()
    {
        const int N = 128;
        var img = Image.CreateEmpty(N, N, false, Image.Format.Rgba8);
        var a = new ValueNoise(41); var b = new ValueNoise(43); var r = new Mulberry32(3);
        Color lo = new("0f0a09"), hi = new("2a1c15");
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float v = (float)(a.Sample(x * .03, y * .22) * .55 + b.Sample(x * .17, y * .17) * .25 + r.Next() * .2);
                img.SetPixel(x, y, lo.Lerp(hi, v));
            }
        return ImageTexture.CreateFromImage(img);
    }

    // ------------------------------------------------------------------ drawing

    static Color Shade(float k) => new(k, k, k);

    /// <summary>Oak boards filling a rect, a little lighter toward the top.</summary>
    public static void Wood(CanvasItem c, Rect2 r, float light = 1)
    {
        c.DrawTextureRect(Boards, r, true, Shade(light));
        Ornament.Gradient(c, r, new Color(1, .93f, .8f, .07f), new Color(0, 0, 0, .2f));
    }

    /// <summary>Dressed stone filling a rect.</summary>
    public static void Stone(CanvasItem c, Rect2 r, float light = 1)
    {
        c.DrawTextureRect(Ashlar, r, true, Shade(light));
        Ornament.Gradient(c, r, new Color(1, 1, 1, .05f), new Color(0, 0, 0, .22f));
    }

    /// <summary>A rivet of black iron with a glint on it.</summary>
    public static void Rivet(CanvasItem c, Vector2 p, float rad)
    {
        c.DrawCircle(p + new Vector2(.6f, 1), rad + .7f, new Color(0, 0, 0, .6f), true, -1, true);
        c.DrawCircle(p, rad, new Color("1c1a1f"), true, -1, true);
        c.DrawCircle(p, rad * .78f, new Color("34303a"), true, -1, true);
        c.DrawCircle(p - new Vector2(rad * .32f, rad * .32f), rad * .34f, new Color(1, .95f, .85f, .45f), true, -1, true);
    }

    /// <summary>A strap of black iron across a rect (upright if it is taller than wide), riveted along its length.</summary>
    public static void Band(CanvasItem c, Rect2 r, float every = 48)
    {
        bool up = r.Size.Y > r.Size.X;
        c.DrawRect(r, IronBlack);
        var i = r.Grow(-1);
        if (up)
        {
            var pts = new[] { i.Position, new Vector2(i.End.X, i.Position.Y), i.End, new Vector2(i.Position.X, i.End.Y) };
            c.DrawPolygon(pts, new[] { IronEdge, IronBlack, IronBlack, IronEdge });
        }
        else Ornament.Gradient(c, i, IronEdge, IronBlack);
        c.DrawTextureRect(Ornament.Grain, i, true, new Color(1, 1, 1, .09f));
        if (up) c.DrawLine(new Vector2(i.Position.X + .5f, i.Position.Y), new Vector2(i.Position.X + .5f, i.End.Y), new Color(1, .92f, .8f, .12f), 1);
        else c.DrawLine(new Vector2(i.Position.X, i.Position.Y + .5f), new Vector2(i.End.X, i.Position.Y + .5f), new Color(1, .92f, .8f, .14f), 1);
        if (every <= 0) return;
        float len = up ? r.Size.Y : r.Size.X, thick = up ? r.Size.X : r.Size.Y, rad = Math.Clamp(thick * .2f, 1.6f, 3.4f);
        for (float t = every / 2; t < len; t += every)
            Rivet(c, up ? new Vector2(r.Position.X + thick / 2, r.Position.Y + t) : new Vector2(r.Position.X + t, r.Position.Y + thick / 2), rad);
    }

    /// <summary>A line of brass edging with a bright lip and a shadow under it.</summary>
    public static void BrassEdge(CanvasItem c, Vector2 a, Vector2 b, float w = 2)
    {
        var n = (b - a).Normalized().Orthogonal();
        if (n.Y < 0) n = -n;
        c.DrawLine(a + n * (w * .5f + .8f), b + n * (w * .5f + .8f), new Color(0, 0, 0, .55f), 1.4f, true);
        c.DrawLine(a, b, Brass, w, true);
        c.DrawLine(a - n * (w * .3f), b - n * (w * .3f), new Color(BrassLight, .75f), Math.Max(1, w * .4f), true);
    }

    /// <summary>A brass outline round a rect.</summary>
    public static void BrassRect(CanvasItem c, Rect2 r, float w = 2)
    {
        c.DrawRect(r.Grow(1), new Color(0, 0, 0, .5f), false, 1);
        c.DrawRect(r, Brass, false, w);
        c.DrawLine(r.Position + new Vector2(0, -w * .3f), new Vector2(r.End.X, r.Position.Y - w * .3f), new Color(BrassLight, .7f), 1);
    }

    /// <summary>A well of dark stained wood sunk into the frame, where text is read and buttons are pressed.</summary>
    public static void Inset(CanvasItem c, Rect2 r, float alpha = 1)
    {
        c.DrawRect(r.Grow(1.5f), new Color(0, 0, 0, .7f * alpha));
        c.DrawTextureRect(Stain, r, true, new Color(1, 1, 1, alpha));
        Ornament.Gradient(c, new Rect2(r.Position, new Vector2(r.Size.X, Math.Min(10, r.Size.Y))), new Color(0, 0, 0, .45f * alpha), new Color(0, 0, 0, 0));
        Ornament.Gradient(c, new Rect2(r.Position.X, r.End.Y - Math.Min(6, r.Size.Y), r.Size.X, Math.Min(6, r.Size.Y)), new Color(0, 0, 0, 0), new Color(1, .9f, .7f, .05f * alpha));
        c.DrawRect(r, new Color(BrassDark, alpha), false, 1);
    }

    /// <summary>An iron corner plate, bent round a corner of a frame, with two rivets.</summary>
    public static void Corner(CanvasItem c, Vector2 p, Vector2 d, float s = 18)
    {
        var a = p + new Vector2(0, s * d.Y); var e = p + new Vector2(s * d.X, 0);
        var q = p + new Vector2(5 * d.X, 5 * d.Y);
        var plate = new[] { p, e, e + new Vector2(0, 5 * d.Y), q, a + new Vector2(5 * d.X, 0), a };
        var shadow = new Vector2[plate.Length];
        for (int i = 0; i < plate.Length; i++) shadow[i] = plate[i] + new Vector2(1, 1.5f);
        c.DrawColoredPolygon(shadow, new Color(0, 0, 0, .5f));
        c.DrawColoredPolygon(plate, new Color("1f1c22"));
        var rim = new Vector2[plate.Length + 1];
        plate.CopyTo(rim, 0); rim[^1] = plate[0];
        c.DrawPolyline(rim, new Color("3d3843"), 1, true);
        Rivet(c, p + new Vector2(2.6f * d.X, (s - 4) * d.Y), 1.8f);
        Rivet(c, p + new Vector2((s - 4) * d.X, 2.6f * d.Y), 1.8f);
    }

    /// <summary>A frame of boards round a dark well, with iron at its corners: the panels that hang from the beam.</summary>
    public static void Framed(CanvasItem c, Rect2 r, float border = 9)
    {
        for (int i = 3; i >= 1; i--) c.DrawRect(new Rect2(r.Position + new Vector2(0, 4), r.Size).Grow(i * 2.5f), new Color(0, 0, 0, .18f));
        Wood(c, r, .9f);
        c.DrawRect(r, OakDark, false, 1.5f);
        var well = r.Grow(-border);
        Inset(c, well);
        Corner(c, r.Position, new Vector2(1, 1));
        Corner(c, new Vector2(r.End.X, r.Position.Y), new Vector2(-1, 1));
        Corner(c, new Vector2(r.Position.X, r.End.Y), new Vector2(1, -1));
        Corner(c, r.End, new Vector2(-1, -1));
    }

    /// <summary>A raised brass-rimmed plaque on the beam: a tab, a button of the frame.</summary>
    public static void Plaque(CanvasItem c, Rect2 r, bool hover, bool selected)
    {
        c.DrawRect(new Rect2(r.Position + new Vector2(1, 2), r.Size), new Color(0, 0, 0, .45f));
        var top = selected ? new Color("3b2414") : hover ? new Color("2e2422") : new Color("1d1716");
        var bottom = selected ? new Color("5a3418") : hover ? new Color("221b1b") : new Color("120e0e");
        Ornament.Gradient(c, r, top, bottom);
        c.DrawTextureRect(Stain, r, true, new Color(1, 1, 1, .25f));
        if (selected) for (int i = 3; i >= 1; i--) c.DrawRect(r.Grow(-i * 1.5f), new Color(Ornament.Ember, .05f * (4 - i)), false, 1.5f);
        c.DrawRect(r, selected ? Ornament.Ember : hover ? BrassLight : Brass, false, selected ? 1.6f : 1.2f);
        c.DrawLine(r.Position + new Vector2(1, 1), new Vector2(r.End.X - 1, r.Position.Y + 1), new Color(1, .9f, .7f, .12f), 1);
    }
}
