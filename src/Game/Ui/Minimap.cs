using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The field from above, one pixel per voxel column: the colour of whatever tops it, lit from the north-west.
/// Shows the citadel and what the camera sees; a click or a drag moves the view there.
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
        TooltipText = "Карта\nЩелчок или протяжка переносит взгляд. Светлая рамка — то, что видит камера, ромб — цитадель. " +
            "Голубые точки — ваши люди (выбранные в кольце), красные — орда, бурые — звери.";
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

    Color Shade(int px, int pz)
    {
        int top = _top[pz * _w + px];
        var c = Ornament.MatColor(_mat[pz * _w + px]);
        int nw = _top[Math.Max(0, pz - 1) * _w + Math.Max(0, px - 1)];
        float shade = 1 + Math.Clamp((top - nw) * .07f / _step, -.35f, .35f);
        float lift = .72f + .55f * top / _m.World.SY;
        return new Color(Math.Min(1, c.R * shade * lift), Math.Min(1, c.G * shade * lift), Math.Min(1, c.B * shade * lift));
    }

    public override void _Process(double delta)
    {
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
        DrawTextureRect(_tex, r, false);
        var w = _m.World;
        var pts = _m.Rig.GroundFootprint(3f);
        var poly = new Vector2[pts.Length + 1];
        for (int i = 0; i < pts.Length; i++)
        {
            var p = ToMap((pts[i].X - w.OriginX) / VoxelWorld.Vox, (pts[i].Z - w.OriginZ) / VoxelWorld.Vox);
            poly[i] = new Vector2(Math.Clamp(p.X, 1, Size.X - 1), Math.Clamp(p.Y, 1, Size.Y - 1));
        }
        poly[^1] = poly[0];
        DrawCreatures();
        DrawPolyline(poly, new Color(0, 0, 0, .6f), 3f, true);
        DrawPolyline(poly, Ornament.BronzeLight, 1.4f, true);
        var keep = ToMap(_m.Site.KeepX, _m.Site.KeepZ);
        Ornament.Diamond(this, keep, 6.5f, new Color(0, 0, 0, .7f));
        Ornament.Diamond(this, keep, 5f, Ornament.EmberGlow);
        DrawRect(r, new Color(0, 0, 0, .7f), false, 2);
        DrawRect(r.Grow(1), Ornament.BronzeDark, false, 1);
    }

    static readonly Color Ours = new(.62f, .86f, 1f), Horde = new(1f, .3f, .22f), Beast = new(.66f, .47f, .28f), Rim = new(0, 0, 0, .75f);

    /// <summary>Everyone alive: beasts underneath, our people over them, the horde on top; the chosen ringed.</summary>
    void DrawCreatures()
    {
        var units = _m.Sim.Units;
        foreach (var team in new[] { Team.Wild, Team.Player, Team.Horde })
            foreach (var u in units)
            {
                if (u.Dead || u.Team != team) continue;
                float r = team switch { Team.Horde => 2.4f, Team.Player => 2.1f, _ => 1.5f };
                var p = ToMap(u.X, u.Z);
                DrawCircle(p, r + 1, Rim);
                DrawCircle(p, r, team switch { Team.Horde => Horde, Team.Player => Ours, _ => Beast });
            }
        foreach (var u in _m.Selected)
            if (!u.Dead) DrawArc(ToMap(u.X, u.Z), 4.4f, 0, Mathf.Tau, 14, Ornament.BronzeLight, 1.3f, true);
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            _drag = mb.Pressed;
            if (mb.Pressed) Jump(mb.Position);
            AcceptEvent();
        }
        else if (e is InputEventMouseMotion mm && _drag)
        {
            Jump(mm.Position);
            AcceptEvent();
        }
    }

    /// <summary>Moves the view to a point of the map given in its own pixels.</summary>
    public void Jump(Vector2 p)
    {
        var w = _m.World;
        float vx = Math.Clamp(p.X / Size.X, 0, 1) * w.SX, vz = Math.Clamp(p.Y / Size.Y, 0, 1) * w.SZ;
        _m.Rig.Focus(w.WX(vx), w.WZ(vz));
    }

    public override GodotObject _MakeCustomTooltip(string forText) => Tooltip.Make(forText);
}
