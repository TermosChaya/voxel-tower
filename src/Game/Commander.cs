using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The player's word to the pawns: picking them out with a click or a frame, sending them with the right button (onto a
/// beast to hunt it, onto a raider to strike it down), hiring at the gate, the horn, calling the horde early, and how fast
/// the game runs. Space calls the horde, P pauses, Tab changes the speed, T sounds the horn.
/// </summary>
public partial class Commander : Node
{
    Main _m;
    bool _down, _framing;
    Vector2 _from, _to;

    /// <summary>The frame being dragged across the screen, or null.</summary>
    public Rect2? Frame => _framing ? Box(_from, _to) : null;

    static Rect2 Box(Vector2 a, Vector2 b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));

    public void Init(Main m) => _m = m;

    // ---------------------------------------------------------------- choosing

    /// <summary>Left button with no brush in hand: a click picks one, a frame picks many, Shift adds.</summary>
    public void Button(InputEventMouseButton mb)
    {
        if (mb.Pressed) { _down = true; _framing = false; _from = _to = mb.Position; return; }
        if (!_down) return;
        _down = false;
        bool add = mb.ShiftPressed;
        if (_framing)
        {
            _framing = false;
            var r = Box(_from, mb.Position);
            if (!add) _m.Selected.Clear();
            foreach (var u in _m.Sim.Units)
                if (!u.Dead && u.Team == Team.Player && r.HasPoint(Screen(u, .5f))) _m.Selected.Add(u);
            _m.Hud.OnSelectionChanged();
            return;
        }
        var hit = UnitAt(mb.Position);
        if (!add) _m.Selected.Clear();
        if (hit != null && hit.Team == Team.Player)
        {
            if (add && _m.Selected.Contains(hit)) _m.Selected.Remove(hit); else _m.Selected.Add(hit);
        }
        _m.Hud.OnSelectionChanged();
    }

    public void Motion(InputEventMouseMotion mm)
    {
        if (!_down) return;
        _to = mm.Position;
        if (!_framing && (_to - _from).Length() > 6) _framing = true;
    }

    /// <summary>Where on the screen a unit is, at a fraction of its height.</summary>
    public Vector2 Screen(Unit u, float up)
    {
        var w = _m.World;
        return _m.Rig.Cam.UnprojectPosition(new Vector3(w.WX(u.X), w.WY(u.Y + u.T.BodyH * up), w.WZ(u.Z)));
    }

    /// <summary>The living unit drawn nearest the cursor; pawns first, or anyone but pawns first.</summary>
    public Unit UnitAt(Vector2 p, bool pawnsFirst = true)
    {
        Unit best = null; float bd = float.MaxValue;
        float pxPerVox = PixelsPerVoxel();
        foreach (var u in _m.Sim.Units)
        {
            if (u.Dead) continue;
            var a = Screen(u, 0); var b = Screen(u, 1);
            float rad = Math.Max(10, (u.T.BodyR + 3) * pxPerVox);
            float d = DistToSegment(p, a, b) - rad;
            if (d > 0) continue;
            float score = d + ((u.Team == Team.Player) == pawnsFirst ? -1000 : 0);
            if (score < bd) { bd = score; best = u; }
        }
        return best;
    }

    float PixelsPerVoxel()
    {
        var cam = _m.Rig.Cam;
        return GetViewport().GetVisibleRect().Size.Y / Math.Max(.01f, cam.Size) * VoxelWorld.Vox;
    }

    static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float t = ab.LengthSquared() < 1e-6f ? 0 : Math.Clamp((p - a).Dot(ab) / ab.LengthSquared(), 0, 1);
        return p.DistanceTo(a + ab * t);
    }

    public void ClearSelection()
    {
        if (_m.Selected.Count == 0) return;
        _m.Selected.Clear();
        _m.Hud.OnSelectionChanged();
    }

    /// <summary>All the living pawns of one kind; a second call while they are chosen shows them.</summary>
    public void SelectKind(UnitType t)
    {
        var all = new List<Unit>();
        foreach (var u in _m.Sim.Units) if (!u.Dead && u.T == t) all.Add(u);
        if (all.Count == 0) return;
        bool same = _m.Selected.Count == all.Count && all.TrueForAll(_m.Selected.Contains);
        _m.Selected.Clear();
        foreach (var u in all) _m.Selected.Add(u);
        if (same)
        {
            float x = 0, z = 0;
            foreach (var u in all) { x += u.X; z += u.Z; }
            _m.Rig.Focus(_m.World.WX(x / all.Count), _m.World.WZ(z / all.Count));
        }
        _m.Hud.OnSelectionChanged();
    }

    /// <summary>Drops the fallen from the chosen.</summary>
    public void Prune()
    {
        if (_m.Selected.RemoveWhere(u => u.Dead) > 0) _m.Hud.OnSelectionChanged();
    }

    // ---------------------------------------------------------------- orders

    /// <summary>
    /// Right click with pawns chosen: on a beast they hunt it, on a raider they go for it,
    /// on the ground they go there and stay (soldiers make it their post).
    /// </summary>
    public void OrderTo(Vector2 screen)
    {
        Prune();
        if (_m.Selected.Count == 0) return;
        var prey = UnitAt(screen, pawnsFirst: false);
        if (prey != null && prey.Team != Team.Player)
        {
            int n = _m.Sim.Hunt(_m.Selected, prey);
            if (n > 0) _m.Hud.Float((prey.Team == Team.Wild ? "охота: " : "в атаку: ") + prey.T.Name.ToLowerInvariant(), Ornament.EmberGlow);
            _m.Hud.OnSelectionChanged();
            return;
        }
        var (o, d) = _m.Rig.Ray(screen);
        var w = _m.World;
        if (!Raycast.Cast(w, (o.X - w.OriginX) / VoxelWorld.Vox, o.Y / VoxelWorld.Vox, (o.Z - w.OriginZ) / VoxelWorld.Vox, d.X, d.Y, d.Z, out var hit, skipVeg: true)) return;
        var g = hit.Ground(w);
        int cell = StandNear(g.x, g.y, g.z);
        if (cell < 0) { _m.Hud.Float("туда не встать", Ornament.Blood); return; }
        int ok = _m.Sim.Command(_m.Selected, cell);
        if (ok == 0) _m.Hud.Float("туда не дойти", Ornament.Blood);
        else _m.Hud.Float(ok == 1 ? "иду" : $"идут: {ok}", Ornament.EmberGlow);
        _m.Hud.OnSelectionChanged();
    }

    /// <summary>A cell to stand in at or just around a voxel: the one it is in, a step up or down, then nearby.</summary>
    int StandNear(int x, int y, int z)
    {
        var nav = _m.Sim.Nav;
        int i = x >> NavGrid.Shift, k = z >> NavGrid.Shift, cy = y >> NavGrid.Shift;
        for (int dy = 0; dy <= 2; dy++)
            foreach (int yy in new[] { cy + dy, cy - dy })
                if (nav.Stand(i, yy, k, 2)) return nav.Id(i, yy, k);
        return _m.Sim.NearestStand(x, z, 2);
    }

    public void ReleaseSelected()
    {
        Prune();
        if (_m.Selected.Count == 0) return;
        _m.Sim.ReleaseHold(_m.Selected);
        _m.Hud.Float("за работу", Ornament.EmberGlow);
        _m.Hud.OnSelectionChanged();
    }

    /// <summary>Stay where you are: soldiers take the spot as their post.</summary>
    public void HoldSelected()
    {
        Prune();
        foreach (var u in _m.Selected) _m.Sim.Command(new[] { u }, u.Cell);
        if (_m.Selected.Count > 0) _m.Hud.Float("стоять здесь", Ornament.EmberGlow);
        _m.Hud.OnSelectionChanged();
    }

    /// <summary>The horn: every soldier hurries to his post.</summary>
    public void Alarm()
    {
        _m.Sim.Recall();
        _m.Hud.Log("Тревога! Солдаты спешат на посты.", Hud.Kind.Warn);
    }

    public Unit Hire(UnitType t)
    {
        var u = _m.Sim.Hire(t);
        if (u == null) _m.Hud.Float($"нужно {t.Cost} золота", Ornament.Blood);
        _m.Hud.Refresh();
        return u;
    }

    /// <summary>Calls the horde before the time is up: no reward, but it comes knowing less of the defences.</summary>
    public void CallWave()
    {
        var w = _m.Sim.Waves;
        if (w.Active || _m.Sim.Over) return;
        w.StartNow(_m.Sim);
        _m.Hud.Refresh();
    }

    public void TogglePause() { _m.Paused = !_m.Paused; _m.Hud.Refresh(); }

    public void SetSpeed(int s) { _m.Speed = Math.Clamp(s, 1, 3); _m.Paused = false; _m.Hud.Refresh(); }

    public void CycleSpeed() => SetSpeed(_m.Speed % 3 + 1);

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey k || !k.Pressed || k.Echo || k.CtrlPressed || k.MetaPressed) return;
        switch (k.PhysicalKeycode)
        {
            case Key.Space: CallWave(); break;
            case Key.P: TogglePause(); break;
            case Key.Tab: CycleSpeed(); break;
            case Key.T: Alarm(); break;
            default: return;
        }
        GetViewport().SetInputAsHandled();
    }
}
