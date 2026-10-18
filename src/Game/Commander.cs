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
                if (!u.Dead && !u.Inside && u.Team == Team.Player && r.HasPoint(Screen(u, .5f))) _m.Selected.Add(u);
            _m.Hud.OnSelectionChanged();
            return;
        }
        var hit = UnitAt(mb.Position);
        if (hit == null && _m.Tools.HoverGate is { } gate) { Toggle(gate); return; }
        if (hit == null && _m.Tools.HoverBridge is { } draw) { Toggle(draw); return; }
        if (!add) _m.Selected.Clear();
        // one of ours is picked; a raider or a beast is only looked at
        _m.Inspected = hit != null && hit.Team != Team.Player && _m.Selected.Count == 0 ? hit : null;
        if (hit != null && hit.Team == Team.Player)
        {
            if (add && _m.Selected.Contains(hit)) _m.Selected.Remove(hit); else _m.Selected.Add(hit);
        }
        _m.Hud.OnSelectionChanged();
    }

    /// <summary>The chosen bring in a resource by themselves when nothing marked needs them.</summary>
    public void DutySelected(int kind)
    {
        if (_m.Selected.Count == 0) { _m.Hud.Float("сначала выберите пешек", Ornament.Aged); return; }
        _m.Sim.SetDuty(_m.Selected, kind);
        _m.Hud.Float(kind == Stockpile.None ? "без задания" : $"добывать: {Stockpile.Name(kind)}", Ornament.BronzeLight);
        _m.Hud.Refresh();
    }

    /// <summary>Calls the chosen workers up, or stands them down again.</summary>
    public void MilitiaSelected()
    {
        bool any = false, all = true;
        foreach (var u in _m.Selected) if (u.T.Work >= 1) { any = true; all &= u.Militia; }
        if (!any) { _m.Hud.Float("в ополчение идут рабочие", Ornament.Aged); return; }
        _m.Sim.SetMilitia(_m.Selected, !all);
        _m.Hud.Float(all ? "ополчение распущено: рабочие снова бегут от орды" : "ополчение: рабочие встретят орду", Ornament.BronzeLight);
        _m.Hud.Refresh();
    }

    /// <summary>Fire arrows for the chosen archers, or plain ones again if all of them had them.</summary>
    public void FireArrowsSelected()
    {
        bool any = false, all = true;
        foreach (var u in _m.Selected) if (u.T.Ranged) { any = true; all &= u.FireArrows; }
        if (!any) { _m.Hud.Float("огнём стреляют лучники", Ornament.Aged); return; }
        _m.Sim.SetFireArrows(_m.Selected, !all);
        _m.Hud.Float(all ? "простые стрелы" : "огненные стрелы: ПКМ по деревянному — поджечь", Ornament.BronzeLight);
        _m.Hud.Refresh();
    }

    /// <summary>A click on a gate: opens for our people → stands open → barred → round again; a holed one is mended.</summary>
    void Toggle(Gateway g)
    {
        var sim = _m.Sim;
        if (g.Broken)
        {
            int n = sim.MendGate(g);
            _m.Hud.Float(n > 0 ? "ворота чинят: створки сколотят заново" : "чинить нечего", Ornament.BronzeLight);
            return;
        }
        if (!g.Ready) { _m.Hud.Float("ворота ещё навешивают", Ornament.Aged); return; }
        var next = g.Mode switch { GateMode.Auto => GateMode.Open, GateMode.Open => GateMode.Shut, _ => GateMode.Auto };
        sim.SetGateMode(g, next);
        _m.Hud.Float(GateWord(g), Ornament.BronzeLight);
    }

    /// <summary>A click on a drawbridge: up before the horde by itself → held down → held up → round again.</summary>
    void Toggle(Drawbridge b)
    {
        if (b.Broken) { _m.Hud.Float("мост разбит: постройте новый", Ornament.Blood); return; }
        if (!b.Ready) { _m.Hud.Float("мост ещё сколачивают", Ornament.Aged); return; }
        var next = b.Mode switch { DrawMode.Auto => DrawMode.Down, DrawMode.Down => DrawMode.Up, _ => DrawMode.Auto };
        _m.Sim.SetDrawMode(b, next);
        _m.Hud.Float(BridgeWord(b), Ornament.BronzeLight);
    }

    /// <summary>What a drawbridge does now, for the tag and the float.</summary>
    public static string BridgeWord(Drawbridge b) =>
        b.Broken ? "разбит" : !b.Ready ? "сколачивают"
        : b.Mode switch
        {
            DrawMode.Down => "опущен и не поднимается",
            DrawMode.Up => "поднят: пути нет",
            _ => b.Raise > .5f ? "поднят перед ордой" : "опущен, перед ордой поднимется сам",
        };

    /// <summary>What a gate does now, for the tag and the float.</summary>
    public static string GateWord(Gateway g) =>
        g.Broken ? "прорублены — щелчок: починить"
        : !g.Ready ? "навешивают"
        : g.Mode switch
        {
            GateMode.Open => "стоят открытыми",
            GateMode.Shut => "на засове: никого не пускают",
            _ => "открываются своим, перед ордой закрыты",
        };

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
            if (u.Dead || u.Inside) continue;
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

    /// <summary>Five squads to call up again by a key: Ctrl and a digit remembers the chosen, Shift and the digit picks them.</summary>
    public readonly List<Unit>[] Squads = { new(), new(), new(), new(), new() };
    double _squadAt = -9;
    int _squadLast = -1;

    /// <summary>The chosen become squad n (0–4).</summary>
    public void RememberSquad(int n)
    {
        Squads[n].Clear();
        foreach (var u in _m.Selected) if (!u.Dead) Squads[n].Add(u);
        _m.Hud.Float(Squads[n].Count > 0 ? $"отряд {Roman(n)}: {Squads[n].Count}" : $"отряд {Roman(n)} распущен", Ornament.BronzeLight);
        _m.Hud.OnSelectionChanged();
    }

    /// <summary>Picks squad n; a second time quickly, the camera goes to it.</summary>
    public void CallSquad(int n)
    {
        Squads[n].RemoveAll(u => u.Dead);
        if (Squads[n].Count == 0) { _m.Hud.Float($"отряд {Roman(n)} пуст: Ctrl+{n + 1} — запомнить выбранных", Ornament.Aged); return; }
        _m.Selected.Clear();
        foreach (var u in Squads[n]) _m.Selected.Add(u);
        double now = Time.GetTicksMsec() / 1000.0;
        if (_squadLast == n && now - _squadAt < .6) Show(Squads[n]);
        _squadLast = n; _squadAt = now;
        _m.Hud.OnSelectionChanged();
    }

    public static string Roman(int n) => n switch { 0 => "I", 1 => "II", 2 => "III", 3 => "IV", _ => "V" };

    /// <summary>Picks one pawn alone; a second time, the camera goes to it.</summary>
    public void SelectOne(Unit u)
    {
        if (u.Dead) return;
        bool again = _m.Selected.Count == 1 && _m.Selected.Contains(u);
        _m.Selected.Clear();
        _m.Selected.Add(u);
        if (again) Show(new List<Unit> { u });
        _m.Hud.OnSelectionChanged();
    }

    void Show(List<Unit> units)
    {
        float x = 0, z = 0;
        foreach (var u in units) { x += u.X; z += u.Z; }
        _m.Rig.Focus(_m.World.WX(x / units.Count), _m.World.WZ(z / units.Count));
    }

    /// <summary>Drops the fallen from the chosen, and stops looking at one gone from the valley.</summary>
    public void Prune()
    {
        if (_m.Selected.RemoveWhere(u => u.Dead) > 0) _m.Hud.OnSelectionChanged();
        if (_m.Inspected != null && (_m.Selected.Count > 0 || !_m.Sim.Units.Contains(_m.Inspected))) { _m.Inspected = null; _m.Hud.OnSelectionChanged(); }
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
        float ox = (o.X - w.OriginX) / VoxelWorld.Vox, oy = o.Y / VoxelWorld.Vox, oz = (o.Z - w.OriginZ) / VoxelWorld.Vox;
        // archers with fire arrows, a click on something that burns (not the grass underfoot): set it alight
        bool fire = false;
        foreach (var u in _m.Selected) fire |= u.FireArrows && !u.Dead;
        if (fire && Raycast.Cast(w, ox, oy, oz, d.X, d.Y, d.Z, out var lit, skipVeg: false) && Mat.Burns(lit.M) && lit.M is not (Mat.Tuft or Mat.Flower or Mat.Bloom))
        {
            int n = _m.Sim.KindleOrder(_m.Selected, lit.X, lit.Y, lit.Z);
            if (n > 0) { _m.Hud.Float("поджечь: " + Mat.NameOf(lit.M).ToLowerInvariant(), Ornament.EmberGlow); _m.Hud.OnSelectionChanged(); return; }
        }
        if (!Raycast.Cast(w, ox, oy, oz, d.X, d.Y, d.Z, out var hit, skipVeg: true)) return;
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

    /// <summary>The alarm, or the all-clear: soldiers to their posts and everyone else to shelter, or all back to work.</summary>
    public void Alarm()
    {
        if (_m.Sim.Alarm) _m.Sim.StandDown(); else _m.Sim.RaiseAlarm();
        _m.Hud.Refresh();
    }

    public const string AlarmTip = "Тревога (T)\nМирные бросают работу и уходят в ближайшее убежище: в цитадель — там их не достать — или на участок, " +
        "отмеченный убежищем (8, «убежище»). Солдаты бегут на посты и держат их. Посланные по приказу идут, куда посланы. Ещё раз — отбой; " +
        "после отбитой волны отбой трубят сами. Рабочие и без тревоги бегут в убежище, когда враг подходит ближе 9 м.";
    public const string StandDownTip = "Отбой (T)\nМирные выходят из убежищ и возвращаются к работе, солдаты — к своим делам.";

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

    /// <summary>Shows or hides where the horde's warbands will go.</summary>
    public void ToggleTrails()
    {
        _m.Sim.WantTrails = !_m.Sim.WantTrails;
        _m.Hud.Float(_m.Sim.WantTrails ? "пути орды видны" : "пути орды скрыты", Ornament.BronzeLight);
        _m.Hud.Refresh();
    }

    public void SetSpeed(int s) { _m.Speed = Math.Clamp(s, 1, 3); _m.Paused = false; _m.Hud.Refresh(); }

    public void CycleSpeed() => SetSpeed(_m.Speed % 3 + 1);

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey k || !k.Pressed || k.Echo || _m.Hud.MenuOpen) return;
        // the quick save, and reading it back
        if (k.PhysicalKeycode == Key.F5) { if (Saves.Write(_m, Saves.Quick)) _m.Hud.Float("быстрое сохранение", Ornament.Moss); GetViewport().SetInputAsHandled(); return; }
        if (k.PhysicalKeycode == Key.F9) { Saves.Read(_m, Saves.Quick); GetViewport().SetInputAsHandled(); return; }
        // squads: Ctrl and a digit remembers the chosen, Shift and the digit calls them up
        if (k.PhysicalKeycode is >= Key.Key1 and <= Key.Key5 && (k.CtrlPressed || k.MetaPressed || k.ShiftPressed))
        {
            int n = (int)(k.PhysicalKeycode - Key.Key1);
            if (k.ShiftPressed) CallSquad(n); else RememberSquad(n);
            GetViewport().SetInputAsHandled();
            return;
        }
        // the interface larger or smaller, as a page in a browser: Ctrl and + or −, Ctrl+0 as it was
        if ((k.CtrlPressed || k.MetaPressed) && k.PhysicalKeycode is Key.Equal or Key.Minus or Key.Key0)
        {
            int dir = k.PhysicalKeycode == Key.Equal ? 1 : k.PhysicalKeycode == Key.Minus ? -1 : 0;
            float s = Settings.Step(GetTree().Root, dir);
            _m.Hud.Float($"интерфейс: {Math.Round(s * 100)} %", Ornament.BronzeLight);
            GetViewport().SetInputAsHandled();
            return;
        }
        if (k.CtrlPressed || k.MetaPressed) return;
        switch (k.PhysicalKeycode)
        {
            case Key.Space: CallWave(); break;
            case Key.P: TogglePause(); break;
            case Key.Tab: CycleSpeed(); break;
            case Key.T: Alarm(); break;
            case Key.J: ToggleTrails(); break;
            default: return;
        }
        GetViewport().SetInputAsHandled();
    }
}
