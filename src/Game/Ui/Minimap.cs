using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The field from above, one pixel per voxel column: the colour of whatever tops it, lit from the north-west; round the
/// valley the land goes on, its edges mirrored out to the ring, so that the map is round. Shows the citadel and what the
/// camera sees; a click or a drag moves the view there.
/// </summary>
public partial class Minimap : Control
{
    Main _m;
    Image _img;
    ImageTexture _tex;
    int[] _top;
    byte[] _mat;
    int _step, _w, _h;
    readonly List<int> _cols = new();
    readonly HashSet<int> _pix = new();
    bool _drag;

    public void Init(Main m)
    {
        _m = m;
        var w = m.World;
        // one pixel per block of columns, so a big valley stays a small picture
        _step = Math.Max(1, (int)Math.Ceiling(Math.Max(w.SX, w.SZ) / 512.0));
        _w = (w.SX + _step - 1) / _step; _h = (w.SZ + _step - 1) / _step;
        _top = new int[_w * _h];
        _mat = new byte[_w * _h];
        System.Threading.Tasks.Parallel.For(0, _h, pz => { for (int px = 0; px < _w; px++) Sample(px, pz); });
        var bytes = new byte[_w * _h * 4];
        for (int pz = 0; pz < _h; pz++)
            for (int px = 0; px < _w; px++)
            {
                var c = Shade(px, pz); int o = (pz * _w + px) * 4;
                bytes[o] = (byte)(c.R * 255); bytes[o + 1] = (byte)(c.G * 255); bytes[o + 2] = (byte)(c.B * 255); bytes[o + 3] = 255;
            }
        _img = Image.CreateFromData(_w, _h, false, Image.Format.Rgba8, bytes);
        _tex = ImageTexture.CreateFromImage(_img);
        CustomMinimumSize = new Vector2(256, 256f * w.SZ / w.SX);
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        TooltipText = "Карта\nЩелчок переносит вид. Рамка — видимая часть, ромб — цитадель. " +
            "Голубые точки — ваши люди, красные — орда, бурые — звери. " +
            "Квадраты — рудники, серые ромбики — руины.";
    }

    /// <summary>The highest column of a pixel's block, and what tops it.</summary>
    void Sample(int px, int pz)
    {
        var w = _m.World;
        int best = 0; byte mat = Mat.Bed;
        for (int dz = 0; dz < _step; dz++)
            for (int dx = 0; dx < _step; dx++)
            {
                int x = px * _step + dx, z = pz * _step + dz;
                if (x >= w.SX || z >= w.SZ) continue;
                int t = w.ColTop(x, z);
                if (t > best) { best = t; mat = w.Get(x, t - 1, z); }
            }
        _top[pz * _w + px] = best;
        _mat[pz * _w + px] = mat;
    }

    /// <summary>Off the land: the mist the valley stands in, a little darker than the far land.</summary>
    static readonly Color Off = new(.13f, .14f, .14f);

    Color Shade(int px, int pz)
    {
        var land = _m.World.Land;
        if (land != null && land.Edge((px + .5f) * _step, (pz + .5f) * _step) < -.5f * _step) return Off;
        int top = _top[pz * _w + px];
        var c = Ornament.MatColor(_mat[pz * _w + px]);
        int nw = _top[Math.Max(0, pz - 1) * _w + Math.Max(0, px - 1)];
        float shade = 1 + Math.Clamp((top - nw) * .07f / _step, -.35f, .35f);
        float lift = .72f + .55f * top / _m.World.SY;
        return new Color(Math.Min(1, c.R * shade * lift), Math.Min(1, c.G * shade * lift), Math.Min(1, c.B * shade * lift));
    }

    public override void _Process(double delta)
    {
        using var prof = Prof.Time("Minimap");
        if (_m == null) return;
        var w = _m.World;
        if (w.TakeDirtyColumns(_cols) > 0)
        {
            _pix.Clear();
            foreach (int c in _cols) _pix.Add((c / w.SX / _step) * _w + (c % w.SX) / _step);
            foreach (int p in _pix) Sample(p % _w, p / _w);
            foreach (int p in _pix)
            {
                int px = p % _w, pz = p / _w;
                _img.SetPixel(px, pz, Shade(px, pz));
                if (px + 1 < _w && pz + 1 < _h) _img.SetPixel(px + 1, pz + 1, Shade(px + 1, pz + 1));
            }
            _cols.Clear();
            _tex.Update(_img);
        }
        QueueRedraw();
    }

    Vector2 ToMap(float vx, float vz) => new(vx / _m.World.SX * Size.X, vz / _m.World.SZ * Size.Y);

    public override void _Draw()
    {
        if (_m == null) return;
        var r = new Rect2(Vector2.Zero, Size);
        var w = _m.World;
        // the land beyond, as far as the ring shows it: on a square valley the valley turned over at each of its edges
        // and corners, in shadow; a hexagonal one stands in its mist, its six sides drawn round it
        if (w.Land == null)
            for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                {
                    if (i == 0 && j == 0) continue;
                    DrawSetTransform(new Vector2(i > 0 ? 2 * Size.X : 0, j > 0 ? 2 * Size.Y : 0), 0, new Vector2(i == 0 ? 1 : -1, j == 0 ? 1 : -1));
                    DrawTextureRect(_tex, r, false, new Color(.62f, .62f, .68f));
                }
        else DrawRect(new Rect2(-Size, Size * 3), Off);
        DrawSetTransformMatrix(Transform2D.Identity);
        DrawTextureRect(_tex, r, false);
        if (w.Land != null)
        {
            var corners = w.Land.Corners();
            var hexLine = new Vector2[7];
            for (int i = 0; i < 7; i++) hexLine[i] = ToMap(corners[i % 6].x, corners[i % 6].z);
            DrawPolyline(hexLine, new Color(0, 0, 0, .55f), 3f, true);
            DrawPolyline(hexLine, new Color(Ornament.BronzeLight, .55f), 1.2f, true);
        }
        var pts = _m.Rig.GroundFootprint(3f);
        var poly = new Vector2[pts.Length + 1];
        for (int i = 0; i < pts.Length; i++)
        {
            var p = ToMap((pts[i].X - w.OriginX) / VoxelWorld.Vox, (pts[i].Z - w.OriginZ) / VoxelWorld.Vox);
            poly[i] = new Vector2(Math.Clamp(p.X, 1, Size.X - 1), Math.Clamp(p.Y, 1, Size.Y - 1));
        }
        poly[^1] = poly[0];
        DrawPlaces();
        if (_m.Sim.WantTrails) { DrawSides(); DrawTrails(); }
        DrawCreatures();
        DrawPolyline(poly, new Color(0, 0, 0, .6f), 3f, true);
        DrawPolyline(poly, Ornament.BronzeLight, 1.4f, true);
        var keep = ToMap(_m.Site.KeepX, _m.Site.KeepZ);
        Ornament.Diamond(this, keep, 6.5f, new Color(0, 0, 0, .7f));
        Ornament.Diamond(this, keep, 5f, Ornament.EmberGlow);
    }

    static readonly Color Ours = new(.62f, .86f, 1f), Horde = new(1f, .3f, .22f), Beast = new(.66f, .47f, .28f), Rim = new(0, 0, 0, .75f);

    static readonly Color RuinGrey = new(.66f, .64f, .6f), PowerViolet = new(.62f, .38f, .86f);

    /// <summary>The named places: ruins as grey diamonds, mines as squares in the colours of whoever holds them.</summary>
    void DrawPlaces()
    {
        foreach (var l in _m.Site.Landmarks)
        {
            if (l.Kind != LandmarkKind.Ruin) continue;
            var p = ToMap(l.X, l.Z);
            Ornament.Diamond(this, p, 4.6f, Rim);
            Ornament.Diamond(this, p, 3.3f, RuinGrey);
        }
        foreach (var m in _m.Sim.Mines)
        {
            var p = ToMap(m.Place.X, m.Place.Z);
            if (m.Power)
            {
                // a place of power: a round mark, violet, ringed in the colours of whoever holds it
                var ring = m.Holder switch { Holder.Ours => MineView.Ours, Holder.Horde => MineView.Horde, _ => MineView.Nobody };
                DrawCircle(p, 5.6f, Rim, true, -1, true);
                DrawCircle(p, 4.4f, ring, true, -1, true);
                DrawCircle(p, 2.7f, PowerViolet, true, -1, true);
                continue;
            }
            var c = m.Holder switch { Holder.Ours => MineView.Ours, Holder.Horde => MineView.Horde, _ => MineView.Nobody };
            DrawRect(new Rect2(p - new Vector2(5, 5), new Vector2(10, 10)), Rim);
            DrawRect(new Rect2(p - new Vector2(3.8f, 3.8f), new Vector2(7.6f, 7.6f)), c);
            DrawRect(new Rect2(p - new Vector2(1.4f, 1.4f), new Vector2(2.8f, 2.8f)), Ornament.BronzeLight);
        }
    }

    /// <summary>The chief's ring round the citadel: how weak each side looks to him.</summary>
    void DrawSides()
    {
        var sim = _m.Sim;
        if (!sim.Bands.Exists(b => b.State is BandState.Coming or BandState.Forming or BandState.March && b.Target == null)) return;
        var c = ToMap(_m.Site.KeepX, _m.Site.KeepZ);
        float r = ToMap(_m.Site.KeepX + 30 * VoxelWorld.PerMetre, _m.Site.KeepZ).X - c.X;
        for (int s = 0; s < Sim.Sides; s++)
        {
            float a0 = (s - .5f) * MathF.PI / 4 + .05f, a1 = (s + .5f) * MathF.PI / 4 - .05f;
            var col = TrailView.SideColor(sim.SideSeen[s], sim.SideBands[s] > 0);
            DrawArc(c, r, a0, a1, 8, new Color(0, 0, 0, .6f), 4.2f, true);
            DrawArc(c, r, a0, a1, 8, new Color(col, Math.Min(1, col.A + .35f)), 2.6f, true);
        }
    }

    /// <summary>Where the warbands will go: a line each, in its colour, from its head to the citadel.</summary>
    void DrawTrails()
    {
        var nav = _m.Sim.Nav;
        foreach (var b in _m.Sim.Bands)
        {
            if (b.Trail.Count < 2) continue;
            var pts = new Vector2[(b.Trail.Count + 3) / 4 + 1];
            int n = 0;
            for (int i = 0; i < b.Trail.Count; i += 4) { var (x, _, z) = nav.Feet(b.Trail[i]); pts[n++] = ToMap(x, z); }
            var (lx, _, lz) = nav.Feet(b.Trail[^1]);
            pts[n++] = ToMap(lx, lz);
            if (n < pts.Length) Array.Resize(ref pts, n);
            var c = TrailView.ColorOf(b);
            DrawPolyline(pts, new Color(0, 0, 0, .55f), 3f, true);
            DrawPolyline(pts, new Color(c, .9f), 1.5f, true);
            DrawCircle(pts[0], 3.2f, Rim);
            DrawCircle(pts[0], 2.3f, c);
        }
    }

    /// <summary>Everyone alive: beasts underneath, our people over them, the horde on top; the chosen ringed.</summary>
    void DrawCreatures()
    {
        var units = _m.Sim.Units;
        foreach (var team in new[] { Team.Wild, Team.Player, Team.Horde })
            foreach (var u in units)
            {
                if (u.Dead || u.Inside || u.Team != team) continue;
                float r = team switch { Team.Horde => 2.4f, Team.Player => 2.1f, _ => 1.5f };
                var p = ToMap(u.X, u.Z);
                DrawCircle(p, r + 1, Rim);
                // a raider lying bound is drawn small and dim: it fights nobody
                DrawCircle(p, u.Bound ? r * .7f : r, u.Bound ? Horde.Darkened(.45f) : team switch { Team.Horde => Horde, Team.Player => Ours, _ => Beast });
            }
        foreach (var u in _m.Selected)
            if (!u.Dead) DrawArc(ToMap(u.X, u.Z), 4.4f, 0, Mathf.Tau, 14, Ornament.BronzeLight, 1.3f, true);
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            _drag = mb.Pressed;
            if (mb.Pressed) { Jump(mb.Position); Clicks++; }
            AcceptEvent();
        }
        else if (e is InputEventMouseMotion mm && _drag)
        {
            Jump(mm.Position);
            AcceptEvent();
        }
    }

    /// <summary>How many times the map was clicked to look somewhere (the tutorial counts them).</summary>
    public int Clicks;

    /// <summary>Moves the view to a point of the map given in its own pixels.</summary>
    public void Jump(Vector2 p)
    {
        var w = _m.World;
        float vx = Math.Clamp(p.X / Size.X, 0, 1) * w.SX, vz = Math.Clamp(p.Y / Size.Y, 0, 1) * w.SZ;
        _m.Rig.Focus(w.WX(vx), w.WZ(vz));
    }

    public override GodotObject _MakeCustomTooltip(string forText) => Tooltip.Make(forText);
}
