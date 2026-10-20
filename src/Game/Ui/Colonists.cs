using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The household along the top of the screen, as colonists are in RimWorld: a card for each of our pawns with its face,
/// how much of it is left, and marks for what is the matter — bleeding, in a fight, idle with a reason, standing where
/// it was sent. A click picks one (a second click takes the camera to it), Shift adds it to the chosen or drops it.
/// </summary>
public partial class ColonistBar : Control
{
    Main _m;
    readonly List<Unit> _pawns = new();
    readonly List<float> _x = new();
    float _card = 44;
    int _hover = -1;
    /// <summary>Where it may lie, between these x of its parent, centred between them; and how far down.</summary>
    public float From, To = 1600, Top = 46;
    const float Gap = 4, KindGap = 10, MaxCard = 44, MinCard = 26;

    public void Init(Main m)
    {
        _m = m;
        MouseFilter = MouseFilterEnum.Stop;
        MouseExited += () => { _hover = -1; QueueRedraw(); };
    }

    public override void _Process(double delta)
    {
        if (_m?.Sim == null) return;
        _pawns.Clear();
        // the risen are no household of ours
        foreach (var u in _m.Sim.Units) if (!u.Dead && u.Team == Team.Player && u.T != Kinds.Risen) _pawns.Add(u);
        _pawns.Sort((a, b) => a.T == b.T ? a.Id.CompareTo(b.Id) : Array.IndexOf(Kinds.PlayerKinds, a.T).CompareTo(Array.IndexOf(Kinds.PlayerKinds, b.T)));
        int breaks = 0;
        for (int i = 1; i < _pawns.Count; i++) if (_pawns[i].T != _pawns[i - 1].T) breaks++;
        float room = Math.Max(100, To - From), need = _pawns.Count * (MaxCard + Gap) + breaks * KindGap;
        _card = need <= room ? MaxCard : Math.Max(MinCard, (room - breaks * KindGap) / Math.Max(1, _pawns.Count) - Gap);
        _x.Clear();
        float x = 0;
        for (int i = 0; i < _pawns.Count; i++)
        {
            if (i > 0 && _pawns[i].T != _pawns[i - 1].T) x += KindGap;
            _x.Add(x);
            x += _card + Gap;
        }
        float width = Math.Max(0, x - Gap);
        Size = new Vector2(width, _card * 1.22f + 4);
        Position = new Vector2(From + (room - width) / 2, Top);
        Visible = _pawns.Count > 0 && !_m.Hud.InFirstPerson && !_m.Hud.TitleOpen && !_m.Hud.Hidden(Hud.Part.Faces);
        QueueRedraw();
    }

    int At(Vector2 p)
    {
        for (int i = 0; i < _x.Count; i++) if (p.X >= _x[i] && p.X < _x[i] + _card) return i;
        return -1;
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseMotion mm)
        {
            int h = At(mm.Position);
            if (h != _hover) { _hover = h; QueueRedraw(); }
            return;
        }
        if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mb) return;
        int i = At(mb.Position);
        AcceptEvent();
        if (i < 0) return;
        var u = _pawns[i];
        if (mb.ShiftPressed)
        {
            if (!_m.Selected.Remove(u)) _m.Selected.Add(u);
            _m.Hud.OnSelectionChanged();
        }
        else _m.Command.SelectOne(u);
    }

    public override string _GetTooltip(Vector2 at)
    {
        int i = At(at);
        if (i < 0) return "";
        var u = _pawns[i];
        return $"{u.Name} №{u.Id} · {Math.Ceiling(Math.Max(0, u.Hp))} из {Math.Ceiling(u.MaxHp)}\n{Retinue.Doing(u, _m.Sim)}.\nЩелчок — выбрать, ещё раз — показать; Shift — добавить к выбранным.";
    }

    public override GodotObject _MakeCustomTooltip(string forText) => Tooltip.Make(forText);

    public override void _Draw()
    {
        for (int i = 0; i < _pawns.Count; i++) Card(_pawns[i], new Rect2(_x[i], 0, _card, _card * 1.22f), i == _hover);
    }

    void Card(Unit u, Rect2 r, bool hover)
    {
        bool chosen = _m.Selected.Contains(u);
        DrawRect(new Rect2(r.Position + new Vector2(1, 2), r.Size), new Color(0, 0, 0, .5f));
        Ornament.Gradient(this, r, new Color("2a2024"), new Color("0e0b0d"));
        var face = new Rect2(r.Position + new Vector2(2, 2), new Vector2(r.Size.X - 4, r.Size.X - 4));
        DrawTextureRect(Portraits.Of(u, 48), face, false);
        // in the citadel: in the shadow of its walls
        if (u.Inside) DrawRect(face, new Color(.02f, .02f, .05f, .5f));
        // how much of it is left
        float f = Math.Clamp(u.Hp / u.MaxHp, 0, 1), by = face.End.Y + 2, bh = Math.Max(3, r.End.Y - by - 2);
        DrawRect(new Rect2(r.Position.X + 2, by, r.Size.X - 4, bh), new Color(0, 0, 0, .7f));
        DrawRect(new Rect2(r.Position.X + 2, by, (r.Size.X - 4) * f, bh), f > .6f ? Ornament.Moss : f > .3f ? Ornament.Ember : Ornament.Blood);
        var edge = chosen ? Ornament.EmberGlow : hover ? Craft.BrassLight : Craft.Brass;
        if (chosen) for (int k = 3; k >= 1; k--) DrawRect(r.Grow(k * 1.4f), new Color(Ornament.Ember, .07f * (4 - k)), false, 1.4f);
        DrawRect(r, edge, false, chosen ? 1.8f : 1.1f);
        Marks(u, face);
    }

    /// <summary>What is the matter with it, in small marks on the corners of its face.</summary>
    void Marks(Unit u, Rect2 face)
    {
        float s = Math.Max(6, face.Size.X * .2f);
        var tr = new Vector2(face.End.X - s * .7f, face.Position.Y + s * .75f);
        var tl = new Vector2(face.Position.X + s * .65f, face.Position.Y + s * .75f);
        var w = u.Wounds;
        if (w != null && w.Bleed > .15f) Drop(tr, s * .55f, w.Bleed > .8f);
        else if (u.Inside) Door(tr, s * .6f);
        else if (u.State == UnitState.Fight || u.Prey is { Dead: false }) Swords(tr, s * .62f);
        else if (u.State == UnitState.Flee) Arrow(tr, s * .6f);
        if (u.Hold) Flag(tl, s * .62f);
        else if (u.State == UnitState.Idle && u.Why != "" && !u.Inside) Dots(new Vector2(face.Position.X + face.Size.X / 2, face.End.Y - s * .35f), s * .17f);
        if (w != null && w.Wounds.Exists(t => t.StartsWith("отрублен")))
        {
            var bl = new Vector2(face.Position.X + s * .6f, face.End.Y - s * .6f);
            DrawLine(bl - new Vector2(s * .35f, s * .35f), bl + new Vector2(s * .35f, s * .35f), Ornament.Blood, 2, true);
            DrawLine(bl + new Vector2(-s * .35f, s * .35f), bl + new Vector2(s * .35f, -s * .35f), Ornament.Blood, 2, true);
        }
    }

    /// <summary>A little tower with its door: in the citadel.</summary>
    void Door(Vector2 c, float r)
    {
        DrawCircle(c, r * 1.25f, new Color(0, 0, 0, .6f), true, -1, true);
        var col = Ornament.BronzeLight; var dark = new Color(0, 0, 0, .9f);
        // the tower, three merlons on it, and the dark of the door at its foot
        DrawRect(new Rect2(c.X - r * .6f, c.Y - r * .45f, r * 1.2f, r * 1.2f), col);
        for (int m = -1; m <= 1; m++) DrawRect(new Rect2(c.X + m * r * .45f - r * .15f, c.Y - r * .8f, r * .3f, r * .4f), col);
        DrawRect(new Rect2(c.X - r * .2f, c.Y + r * .2f, r * .4f, r * .55f), dark);
        DrawCircle(new Vector2(c.X, c.Y + r * .2f), r * .2f, dark, true, -1, true);
    }

    void Drop(Vector2 c, float r, bool hard)
    {
        var col = hard ? new Color("e0412d") : new Color("b0352a");
        DrawCircle(c + new Vector2(0, r * .35f), r + 1.2f, new Color(0, 0, 0, .7f), true, -1, true);
        DrawColoredPolygon(new[] { c + new Vector2(0, -r * 1.4f), c + new Vector2(r * .85f, 0), c + new Vector2(-r * .85f, 0) }, col);
        DrawCircle(c + new Vector2(0, r * .35f), r, col, true, -1, true);
        DrawCircle(c + new Vector2(-r * .3f, 0), r * .28f, new Color(1, 1, 1, .45f), true, -1, true);
    }

    void Swords(Vector2 c, float r)
    {
        DrawCircle(c, r * 1.2f, new Color(0, 0, 0, .6f), true, -1, true);
        DrawLine(c + new Vector2(-r, r), c + new Vector2(r, -r), Ornament.EmberGlow, 1.8f, true);
        DrawLine(c + new Vector2(r, r), c + new Vector2(-r, -r), Ornament.EmberGlow, 1.8f, true);
        DrawLine(c + new Vector2(-r * .9f, r * .3f), c + new Vector2(-r * .3f, r * .9f), Ornament.EmberGlow, 1.4f, true);
        DrawLine(c + new Vector2(r * .9f, r * .3f), c + new Vector2(r * .3f, r * .9f), Ornament.EmberGlow, 1.4f, true);
    }

    void Arrow(Vector2 c, float r)
    {
        DrawCircle(c, r * 1.2f, new Color(0, 0, 0, .6f), true, -1, true);
        DrawLine(c + new Vector2(r * .8f, 0), c - new Vector2(r * .8f, 0), Ornament.BronzeLight, 1.6f, true);
        DrawColoredPolygon(new[] { c - new Vector2(r, 0), c + new Vector2(-r * .2f, -r * .6f), c + new Vector2(-r * .2f, r * .6f) }, Ornament.BronzeLight);
    }

    void Flag(Vector2 c, float r)
    {
        DrawLine(c + new Vector2(-r * .5f, r), c + new Vector2(-r * .5f, -r), new Color(0, 0, 0, .8f), 2.6f, true);
        DrawLine(c + new Vector2(-r * .5f, r), c + new Vector2(-r * .5f, -r), Ornament.BronzeLight, 1.3f, true);
        DrawColoredPolygon(new[] { c + new Vector2(-r * .4f, -r), c + new Vector2(r, -r * .55f), c + new Vector2(-r * .4f, -r * .1f) }, new Color("3e66a8"));
    }

    void Dots(Vector2 c, float r)
    {
        for (int k = -1; k <= 1; k++)
        {
            DrawCircle(c + new Vector2(k * r * 3, 0), r + .8f, new Color(0, 0, 0, .7f), true, -1, true);
            DrawCircle(c + new Vector2(k * r * 3, 0), r, Ornament.BronzeLight, true, -1, true);
        }
    }
}
