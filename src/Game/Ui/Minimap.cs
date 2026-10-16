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
    readonly List<int> _cols = new();
    bool _drag;

    public void Init(Main m)
    {
        _m = m;
        var w = m.World;
        _top = new int[w.SX * w.SZ];
        _img = Image.CreateEmpty(w.SX, w.SZ, false, Image.Format.Rgba8);
        for (int z = 0; z < w.SZ; z++)
            for (int x = 0; x < w.SX; x++) _top[z * w.SX + x] = w.ColTop(x, z);
        for (int z = 0; z < w.SZ; z++)
            for (int x = 0; x < w.SX; x++) Paint(x, z);
        _tex = ImageTexture.CreateFromImage(_img);
        CustomMinimumSize = new Vector2(256, 256f * w.SZ / w.SX);
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        TooltipText = "Карта\nЩелчок или протяжка переносит взгляд. Светлая рамка — то, что видит камера, ромб — цитадель.";
    }

    void Paint(int x, int z)
    {
        var w = _m.World;
        int top = _top[z * w.SX + x];
        byte m = top > 0 ? w.Get(x, top - 1, z) : Mat.Bed;
        var c = Ornament.MatColor(m);
        int nw = _top[Math.Max(0, z - 1) * w.SX + Math.Max(0, x - 1)];
        float shade = 1 + Math.Clamp((top - nw) * .07f, -.35f, .35f);
        float lift = .72f + .55f * top / w.SY;
        _img.SetPixel(x, z, new Color(c.R * shade * lift, c.G * shade * lift, c.B * shade * lift));
    }

    public override void _Process(double delta)
    {
        if (_m == null) return;
        var w = _m.World;
        if (w.TakeDirtyColumns(_cols) > 0)
        {
            foreach (int c in _cols) _top[c] = w.ColTop(c % w.SX, c / w.SX);
            foreach (int c in _cols)
            {
                int x = c % w.SX, z = c / w.SX;
                Paint(x, z);
                if (x + 1 < w.SX && z + 1 < w.SZ) Paint(x + 1, z + 1);
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
        DrawPolyline(poly, new Color(0, 0, 0, .6f), 3f, true);
        DrawPolyline(poly, Ornament.BronzeLight, 1.4f, true);
        var keep = ToMap(_m.Site.KeepX, _m.Site.KeepZ);
        Ornament.Diamond(this, keep, 6.5f, new Color(0, 0, 0, .7f));
        Ornament.Diamond(this, keep, 5f, Ornament.EmberGlow);
        DrawRect(r, new Color(0, 0, 0, .7f), false, 2);
        DrawRect(r.Grow(1), Ornament.BronzeDark, false, 1);
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
