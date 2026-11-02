using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The household along the top of the screen, as colonists are in RimWorld: a card for each of our pawns with its face,
/// how much of it is left, and marks for what is the matter — bleeding, in a fight, idle with a reason, standing where
/// it was sent; a commander's level, and a star when he has a skill to choose. A click picks one (a second click takes
/// the camera to it), Shift adds it to the chosen or drops it; the right button on a commander opens his card as a hero.
/// A large household goes in two rows; one larger still has its last card «+N», which lets all of them out in as many
/// rows as they need, and folds them up again. At its left end, the marks of the parts of the household (Roster.cs) there
/// are men of now: a click on one shows only those, a second click all again; with Shift it chooses them all.
/// </summary>
public partial class ColonistBar : Control
{
    Main _m;
    readonly List<Unit> _pawns = new();
    /// <summary>Each card's top left in the bar's own frame, for those that have one (the first of the household in order).</summary>
    readonly List<Vector2> _at = new();
    float _card = 44;
    int _hover = -1;
    /// <summary>The card of those left out («+N»), or of folding them up again: where it is, if there is one.</summary>
    Vector2? _more;
    /// <summary>Those left out let out, in as many rows as they need.</summary>
    bool _open;
    /// <summary>Where it may lie, between these x of its parent, centred between them; and how far down.</summary>
    public float From, To = 1600, Top = 46;
    const float Gap = 4, KindGap = 10, RowGap = 5, MaxCard = 44, MinCard = 26, MinOneRow = 34;
    /// <summary>The «+N» card's index for <see cref="At"/>.</summary>
    const int More = -2;
    /// <summary>How many rows it takes now.</summary>
    public int Rows { get; private set; } = 1;
    /// <summary>How many of the household have no card now (folded into «+N»).</summary>
    public int Folded => _pawns.Count - _at.Count;
    /// <summary>The part of the household the list is narrowed to (all, or one kind of work or state).</summary>
    public Roster Filter { get; private set; } = Roster.All;
    /// <summary>The parts there are men of now, their marks' top left in the bar's frame, and how many each has.</summary>
    readonly List<(Roster part, Vector2 at, int count)> _parts = new();
    const float Mark = 20, MarkGap = 3, MarksAfter = 10;
    /// <summary>A part's mark's index for <see cref="At"/>: this, less its place among the marks.</summary>
    const int PartAt = -100;
    Sim _of;

    public void Init(Main m)
    {
        _m = m;
        MouseFilter = MouseFilterEnum.Stop;
        MouseExited += () => { _hover = -1; QueueRedraw(); };
    }

    public override void _Process(double delta)
    {
        using var prof = Prof.Time("Colonists");
        if (_m?.Sim == null) return;
        // a new game, or one read back: the whole list again
        if (_of != _m.Sim) { if (_of != null) Filter = Roster.All; _of = _m.Sim; }
        _pawns.Clear();
        // the risen are no household of ours
        foreach (var u in _m.Sim.Units) if (!u.Dead && u.Team == Team.Player && !u.T.Called) _pawns.Add(u);
        // the parts there are men of, in two rows of marks at the left end; the list narrowed to the one chosen, all
        // again when it has nobody left (the last of the hurt mended)
        var counts = new int[Rosters.Order.Length];
        foreach (var u in _pawns)
            for (int g = 1; g < Rosters.Order.Length; g++) if (Rosters.Holds(Rosters.Order[g], u, _m.Sim)) counts[g]++;
        counts[0] = _pawns.Count;
        if (Filter != Roster.All && counts[Array.IndexOf(Rosters.Order, Filter)] == 0) Filter = Roster.All;
        _parts.Clear();
        var present = new List<int>();
        for (int g = 0; g < Rosters.Order.Length; g++) if (counts[g] > 0) present.Add(g);
        int perRow = present.Count <= 2 ? 0 : (present.Count + 1) / 2;
        for (int i = 0; i < present.Count && perRow > 0; i++)
            _parts.Add((Rosters.Order[present[i]], new Vector2(i % perRow * (Mark + MarkGap), i / perRow * (Mark + MarkGap)), counts[present[i]]));
        float marks = perRow > 0 ? perRow * (Mark + MarkGap) - MarkGap + MarksAfter : 0;
        if (Filter != Roster.All) _pawns.RemoveAll(u => !Rosters.Holds(Filter, u, _m.Sim));
        _pawns.Sort((a, b) => a.T == b.T ? a.Id.CompareTo(b.Id) : Array.IndexOf(Kinds.Ours, a.T).CompareTo(Array.IndexOf(Kinds.Ours, b.T)));
        float room = Math.Max(100, To - From - marks), width = 0;
        int n = _pawns.Count;
        // a row of full cards; else one of cards no smaller than a face is read at; else two rows of the largest that fit;
        // else two rows of the smallest, the last card «+N» — or, let out, all of them in as many rows as they take
        bool laid = false;
        for (float c = MaxCard; c >= MinOneRow && !laid; c--) laid = Lay(c, 1, n, false, room, out width);
        for (float c = MaxCard; c >= MinCard && !laid; c--) laid = Lay(c, 2, n, false, room, out width);
        // (all of them in two rows: nothing is left out, nothing to let out)
        if (laid) _open = false;
        else if (_open) laid = Lay(MinCard, int.MaxValue, n, true, room, out width);
        if (!laid)
        {
            int k = n;
            while (k > 0 && !Lay(MinCard, 2, k, true, room, out width)) k--;
        }
        // the cards laid after the marks
        for (int i = 0; i < _at.Count; i++) _at[i] += new Vector2(marks, 0);
        if (_more is { } mo) _more = mo + new Vector2(marks, 0);
        float tall = Math.Max(Rows * (_card * 1.22f + RowGap) - RowGap + 4, _parts.Count > 0 ? 2 * Mark + MarkGap + 2 : 0);
        Size = new Vector2(width + marks, tall);
        Position = new Vector2(From + (To - From - Size.X) / 2, Top);
        Visible = counts[0] > 0 && !_m.Hud.InFirstPerson && !_m.Hud.TitleOpen && !_m.Hud.Hidden(Hud.Part.Faces) && !_m.AtlasOpen;
        QueueRedraw();
    }

    /// <summary>
    /// Lays the first <paramref name="count"/> cards of the household (and the «+N» one after them, with
    /// <paramref name="more"/>) in rows no wider than the room, a little gap between the kinds; true if no more rows than
    /// so many were needed. What it laid is kept either way.
    /// </summary>
    bool Lay(float card, int rows, int count, bool more, float room, out float width)
    {
        _card = card;
        _at.Clear();
        _more = null;
        float x = 0, rowH = card * 1.22f + RowGap;
        int row = 0;
        width = 0;
        for (int i = 0; i <= count; i++)
        {
            bool last = i == count;
            if (last && !more) break;
            float gap = i > 0 && !last && x > 0 && _pawns[i].T != _pawns[i - 1].T ? KindGap : 0;
            if (x > 0 && x + gap + card > room) { row++; x = 0; gap = 0; }
            if (row >= rows) { Rows = rows; return false; }
            var at = new Vector2(x + gap, row * rowH);
            if (last) _more = at; else _at.Add(at);
            x = at.X + card + Gap;
            width = Math.Max(width, x - Gap);
        }
        Rows = row + 1;
        return true;
    }

    /// <summary>The middle of one's card in the row, in the row's own frame (for the self-test); null if it has none.</summary>
    public Vector2? FaceOf(Unit u)
    {
        int i = _pawns.IndexOf(u);
        return i < 0 || i >= _at.Count ? null : _at[i] + new Vector2(_card / 2, _card / 2);
    }

    /// <summary>The middle of the «+N» card in the bar's own frame, if it has one (for the self-test).</summary>
    public Vector2? MoreAt => _more is { } m ? m + new Vector2(_card / 2, _card / 2) : null;

    /// <summary>The middle of a part's mark in the bar's own frame (for the self-test); null if that part has none now.</summary>
    public Vector2? MarkOf(Roster part)
    {
        foreach (var (g, at, _) in _parts) if (g == part) return at + new Vector2(Mark / 2, Mark / 2);
        return null;
    }

    /// <summary>
    /// Narrows the list to a part of the household, or shows all again if it is narrowed to that part already; with
    /// <paramref name="choose"/> the part is narrowed to and all of it chosen.
    /// </summary>
    public void Narrow(Roster part, bool choose = false)
    {
        Filter = choose || Filter != part ? part : Roster.All;
        if (choose)
        {
            _m.Selected.Clear();
            foreach (var u in _m.Sim.Units)
                if (!u.Dead && u.Team == Team.Player && !u.T.Called && Rosters.Holds(part, u, _m.Sim)) _m.Selected.Add(u);
            _m.Hud.OnSelectionChanged();
        }
        _m.Hud.Refresh();
        QueueRedraw();
    }

    int At(Vector2 p)
    {
        for (int i = 0; i < _parts.Count; i++) if (new Rect2(_parts[i].at, Mark, Mark).HasPoint(p)) return PartAt - i;
        for (int i = 0; i < _at.Count; i++) if (new Rect2(_at[i], _card, _card * 1.22f).HasPoint(p)) return i;
        if (_more is { } m && new Rect2(m, _card, _card * 1.22f).HasPoint(p)) return More;
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
        if (e is not InputEventMouseButton { Pressed: true } mb || mb.ButtonIndex is not (MouseButton.Left or MouseButton.Right)) return;
        int i = At(mb.Position);
        AcceptEvent();
        // those left out let out, or folded up again
        if (i == More) { if (mb.ButtonIndex == MouseButton.Left) _open = !_open; return; }
        // a part's mark: the list narrowed to it, or (with Shift) all of it chosen
        if (i <= PartAt) { if (mb.ButtonIndex == MouseButton.Left) Narrow(_parts[PartAt - i].part, mb.ShiftPressed); return; }
        if (i < 0) return;
        var u = _pawns[i];
        // a commander's card as a hero: the right button, or the left on one with a skill to choose
        if (u.Hero != null && (mb.ButtonIndex == MouseButton.Right || u.Hero.Pending)) { _m.Hud.HeroPane.Show(u); if (mb.ButtonIndex == MouseButton.Right) return; }
        if (mb.ButtonIndex != MouseButton.Left) return;
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
        if (i == More) return _open ? "Свернуть\nСнова два ряда лиц." : $"Ещё {Folded}\nЩелчок — показать всех жителей, ещё раз — свернуть.";
        if (i <= PartAt)
        {
            var (g, _, n) = _parts[PartAt - i];
            if (g == Roster.All) return $"Все: {n}\nПоказать весь список.";
            return $"{Rosters.Name(g)}: {n}\nВ списке только {Rosters.About(g)}. Ещё щелчок — снова все. Shift+щелчок — выбрать их всех.";
        }
        if (i < 0) return "";
        var u = _pawns[i];
        return $"{u.Name} №{u.Id} · {Math.Ceiling(Math.Max(0, u.Hp))} из {Math.Ceiling(u.MaxHp)}\n{Retinue.Doing(u, _m.Sim)}.\nЩелчок — выбрать, ещё раз — показать; Shift — добавить к выбранным."
            + (u.Hero != null ? $"\nУровень {u.Hero.Level}. {{Rmb}} — карточка героя{(u.Hero.Pending ? ": новый уровень, выберите умение" : "")}." : "");
    }

    public override GodotObject _MakeCustomTooltip(string forText) => Tooltip.Make(forText);

    public override void _Draw()
    {
        for (int i = 0; i < _parts.Count; i++) PartMark(_parts[i].part, new Rect2(_parts[i].at, Mark, Mark), _hover == PartAt - i);
        for (int i = 0; i < _at.Count; i++) Card(_pawns[i], new Rect2(_at[i], _card, _card * 1.22f), i == _hover);
        if (_more is { } m) MoreCard(new Rect2(m, _card, _card * 1.22f), _hover == More);
    }

    /// <summary>A part's mark: a small round plate, its sign on it; the part the list is narrowed to ringed in ember.</summary>
    void PartMark(Roster g, Rect2 r, bool hover)
    {
        var c = r.GetCenter();
        float rad = r.Size.X / 2;
        bool on = Filter == g;
        DrawCircle(c + new Vector2(.6f, 1.2f), rad, new Color(0, 0, 0, .5f), true, -1, true);
        DrawCircle(c, rad, on ? new Color("3a2a22") : new Color("1b1518"), true, -1, true);
        Rosters.Paint(this, r.Grow(-rad * .38f), g, on ? Ornament.EmberGlow : hover ? Craft.BrassLight : Ornament.BronzeLight);
        DrawArc(c, rad - .5f, 0, MathF.Tau, 24, on ? Ornament.EmberGlow : hover ? Craft.BrassLight : Craft.Brass, on ? 1.8f : 1.1f, true);
    }

    /// <summary>The card of those left out: «+N» on it; let out, a mark to fold them up again.</summary>
    void MoreCard(Rect2 r, bool hover)
    {
        DrawRect(new Rect2(r.Position + new Vector2(1, 2), r.Size), new Color(0, 0, 0, .5f));
        Ornament.Gradient(this, r, new Color("2a2024"), new Color("0e0b0d"));
        var ink = hover ? Craft.BrassLight : Ornament.BronzeLight;
        if (_open)
        {
            // a chevron up: fold them up
            var c = r.GetCenter();
            float s = r.Size.X * .22f;
            DrawPolyline(new[] { c + new Vector2(-s, s * .5f), c + new Vector2(0, -s * .5f), c + new Vector2(s, s * .5f) }, ink, 2, true);
        }
        else
        {
            string text = "+" + Folded;
            int size = (int)Math.Clamp(r.Size.X * .42f, 10, 18);
            float w = Ornament.SerifBold.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
            DrawString(Ornament.SerifBold, new Vector2(r.GetCenter().X - w / 2, r.GetCenter().Y + size * .35f), text, HorizontalAlignment.Left, -1, size, ink);
        }
        DrawRect(r, hover ? Craft.BrassLight : Craft.Brass, false, 1.1f);
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
        if (u.Hero != null) Rank(u.Hero, face);
    }

    /// <summary>A commander's level, small at the lower left of his face, and a star over it when he has a skill to choose.</summary>
    void Rank(Hero h, Rect2 face)
    {
        float s = Math.Max(7, face.Size.X * .22f);
        var c = new Vector2(face.Position.X + s * .75f, face.End.Y - s * .7f);
        DrawCircle(c, s * .72f, new Color(0, 0, 0, .75f), true, -1, true);
        DrawArc(c, s * .72f, 0, MathF.Tau, 16, Craft.BrassLight, 1, true);
        DrawString(Ornament.SerifBold, c + new Vector2(-s, s * .35f), h.Level.ToString(), HorizontalAlignment.Center, s * 2, (int)Math.Max(8, s * .95f), Ornament.BronzeLight);
        if (!h.Pending) return;
        var t = new Vector2(face.Position.X + face.Size.X / 2, face.Position.Y + s * .55f);
        var pts = new Vector2[10];
        for (int k = 0; k < 10; k++)
        {
            float a = -MathF.PI / 2 + k * MathF.PI / 5, rr = k % 2 == 0 ? s * .62f : s * .26f;
            pts[k] = t + new Vector2(MathF.Cos(a), MathF.Sin(a)) * rr;
        }
        DrawColoredPolygon(pts, Ornament.EmberGlow);
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
        // a broken bone: a splint, two pale slats bound across; pinned: a heap of stones over it
        if (u.PinKg > 0) Heap(new Vector2(face.Position.X + face.Size.X / 2, face.End.Y - s * .55f), s * .5f);
        else if (w != null && w.Broken != Break.None) Splint(new Vector2(face.End.X - s * .65f, face.End.Y - s * .6f), s * .5f, (w.Splinted & w.Broken) == w.Broken);
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

    void Splint(Vector2 c, float r, bool bound)
    {
        DrawCircle(c, r * 1.25f, new Color(0, 0, 0, .65f), true, -1, true);
        var bone = new Color("e6dcc4");
        DrawLine(c + new Vector2(-r * .8f, r * .8f), c + new Vector2(r * .8f, -r * .8f), bone, 2.2f, true);
        // the break across it, and the binding over it once splinted
        DrawLine(c + new Vector2(-r * .35f, -r * .05f), c + new Vector2(r * .05f, r * .35f), Ornament.Blood, 1.6f, true);
        if (bound) DrawLine(c + new Vector2(-r * .55f, -r * .25f), c + new Vector2(r * .25f, r * .55f), Ornament.BronzeLight, 1.4f, true);
    }

    void Heap(Vector2 c, float r)
    {
        var stone = new Color("8c8578");
        DrawCircle(c, r * 1.35f, new Color(0, 0, 0, .65f), true, -1, true);
        DrawCircle(c + new Vector2(-r * .5f, r * .25f), r * .48f, stone, true, -1, true);
        DrawCircle(c + new Vector2(r * .5f, r * .25f), r * .48f, stone, true, -1, true);
        DrawCircle(c + new Vector2(0, -r * .35f), r * .5f, stone.Lightened(.15f), true, -1, true);
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
