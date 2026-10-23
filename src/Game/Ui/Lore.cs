using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The lore of the household as a tree drawn like an astrolabe: three pages — the skills, the troops, the intervention —
/// each a round plate with a medallion at its heart saying what there is to spend, and its branches going out from it
/// as rays, each to a great emblem at its outer end. Along a ray stand the lores as round bronze medallions with their
/// sigils, four rings deep: the first on the ray, two beside it, a further one beyond each, and the last back on the ray,
/// resting on both. Lines run from what a lore rests on to it, in the branch's colour once both are learnt. A medallion
/// learnt is rimmed in that colour; one to be learnt now glows at its rim, and a click learns it; one that wants more is
/// plain; one whose roots are not learnt is faint. Words only on hovering: what it is, what it does, what it wants.
/// </summary>
public partial class LorePanel : VBoxContainer
{
    Main _m;
    int _page;
    readonly TabPlaque[] _tabs = new TabPlaque[3];
    RingCanvas _tree;
    readonly List<(Lore lore, Knot knot)> _knots = new();
    public const float Width = 960;
    /// <summary>The plate: its size, its heart, the rings the four tiers stand on (their half-heights; the plate is wider than tall by Wide).</summary>
    const float CW = 916, CH = 474, Wide = 1.78f, MarkRY = 216, R = 19, Heart = 30;
    static readonly Vector2 C = new(CW / 2, CH / 2);
    static readonly float[] RY = { 54, 94, 134, 170 };

    /// <summary>The colour of each branch, page by page, in the order the branches go round.</summary>
    static readonly Color[][] Hues =
    {
        new[] { Ornament.Moss, new Color("9aa7b8"), new Color("c9a24a"), Ornament.Blood, Ornament.Ember },
        new[] { new Color("9aa7b8"), Ornament.Moss, new Color("c9a24a"), Ornament.Blood },
        new[] { new Color("b0875a"), Ornament.Ember, new Color("9b7fc4"), new Color("7f9fc4"), new Color("e8d48a") },
    };

    public void Init(Main m)
    {
        _m = m;
        AddThemeConstantOverride("separation", 4);
        var tabs = new HBoxContainer();
        tabs.AddThemeConstantOverride("separation", 6);
        string[] tips =
        {
            "Умения\nЛуки, кладка, труд, дружина и ловушки. Изучаются за знания.",
            "Войско\nКого можно набирать в цитадели и что их усиливает. Изучается за знания.",
            "Вмешательство\nЗаклинания и благодать. Первое заклинание круга и благодать изучаются за вмешательство, остальные заклинания за откровения. " +
                "Откровения дают только жертвы.",
        };
        for (int i = 0; i < 3; i++)
        {
            int page = i;
            var t = new TabPlaque(Icons.PageMark(i), Sim.Pages[i], tips[i]);
            t.Clicked += () => ShowPage(page);
            tabs.AddChild(t);
            _tabs[i] = t;
        }
        AddChild(tabs);
        _tree = new RingCanvas(this) { CustomMinimumSize = new Vector2(CW, CH), MouseFilter = MouseFilterEnum.Pass };
        AddChild(_tree);
        foreach (var l in Sim.Lores)
        {
            var lore = l;
            var k = new Knot(this, l) { Size = new Vector2(2 * R + 6, 2 * R + 6), Position = Spot(l) - new Vector2(R + 3, R + 3) };
            k.Pressed += () => Press(lore);
            _tree.AddChild(k);
            _knots.Add((l, k));
        }
        ShowPage(0);
    }

    /// <summary>The page shown: 0 the skills, 1 the troops, 2 the intervention.</summary>
    public int Page => _page;

    public void ShowPage(int page)
    {
        _page = Math.Clamp(page, 0, 2);
        foreach (var (l, k) in _knots) k.Visible = l.Page == _page;
        for (int i = 0; i < 3; i++) { _tabs[i].Selected = i == _page; _tabs[i].QueueRedraw(); }
        Refresh();
    }

    // ---------------------------------------------------------------- where things stand

    static float Sector(int page) => MathF.Tau / Sim.PageBranches[page].Length;
    static float AxisOf(int page, int branch) => -MathF.PI / 2 + branch * Sector(page);
    static int BranchIndex(Lore l) => Array.IndexOf(Sim.PageBranches[l.Page], l.Branch);

    /// <summary>The angle a lore stands at: on its branch's ray, or to one side of it in the middle rings.</summary>
    static float AngleOf(Lore l)
    {
        float spread = l.Tier is 1 or 2 ? Sector(l.Page) * .2f : 0;
        return AxisOf(l.Page, Math.Max(0, BranchIndex(l))) + (l.Lane - .5f) * 2 * spread;
    }

    static Vector2 At(float ry, float a) => C + new Vector2(MathF.Cos(a) * ry * Wide, MathF.Sin(a) * ry);
    static Vector2 Spot(Lore l) => At(RY[Math.Clamp(l.Tier, 0, 3)], AngleOf(l));
    static Color HueOf(Lore l) => Hues[l.Page][Math.Clamp(BranchIndex(l), 0, Hues[l.Page].Length - 1)];

    /// <summary>How a lore stands now: 0 learnt, 1 to be learnt now, 2 open but wanting what it costs, 3 shut (its roots not learnt).</summary>
    int StateOf(Lore l)
    {
        var sim = _m.Sim;
        return sim.Knows(l.Key) ? 0 : sim.CanLearn(l) ? 1 : sim.Opened(l) && sim.TierAllows(l) && !sim.RivalShut(l) ? 2 : 3;
    }

    static string CoinWord(Coin c, int n) => c switch
    {
        Coin.Favour => "вмешательства",
        Coin.Revelations => Plural(n, "откровение", "откровения", "откровений"),
        _ => Plural(n, "знание", "знания", "знаний"),
    };

    static string Plural(int n, string one, string few, string many) =>
        n % 10 == 1 && n % 100 != 11 ? one : n % 10 is >= 2 and <= 4 && n % 100 is < 12 or > 14 ? few : many;

    /// <summary>A medallion pressed: learnt, if it can be; a spell already known goes into the hand; else why not.</summary>
    void Press(Lore l)
    {
        var sim = _m.Sim;
        if (l.Spell is { } spell && sim.Known.Contains(spell))
        {
            _m.Tools.TakeSpell(spell);
            _m.Hud.ClosePanel();
            return;
        }
        if (sim.Learn(l.Key)) { Refresh(); _m.Hud.Refresh(); return; }
        _m.Hud.Float(sim.RivalShut(l) ? $"выбрано другое: «{Sim.LoreOf(l.Rival)?.Name}»" : !sim.TierAllows(l) ? $"ярус {Sim.Roman(l.Tier + 1)} ещё закрыт" : StateOf(l) == 3 ? "сначала изучите предыдущее" : $"нужно {l.Cost} {CoinWord(l.Coin, l.Cost)}", Ornament.Blood);
    }

    public void Refresh()
    {
        if (_m == null) return;
        var sim = _m.Sim;
        var open = new int[3];
        foreach (var (l, k) in _knots)
        {
            int state = StateOf(l);
            if (state == 1) open[l.Page]++;
            k.State = state;
            k.Disabled = state != 1 && !(state == 0 && l.Spell != null);
            if (l.Page != _page) continue;
            string why = state switch
            {
                0 => l.Spell != null ? "Изучено. Щелчок — выбрать заклинание." : "Изучено.",
                1 => $"Щелчок — изучить за {l.Cost} {CoinWord(l.Coin, l.Cost)}.",
                2 => $"Нужно {l.Cost} {CoinWord(l.Coin, l.Cost)}, есть {Math.Floor(sim.Wallet(l.Coin)):0}.",
                _ => sim.RivalShut(l)
                    ? $"Уже выбрано другое: «{Sim.LoreOf(l.Rival)?.Name}». Можно взять только одно из двух."
                    : !sim.TierAllows(l)
                    ? $"Ярус {Sim.Roman(l.Tier + 1)} закрыт. Откроется после гибели вожака главы {Sim.Roman(l.Tier)} или через ночь после его ночи."
                    : "Сначала: " + string.Join(", ", Array.ConvertAll(Array.FindAll(l.Needs, n => !sim.Knows(n)), n => $"«{Sim.LoreOf(n)?.Name ?? n}»")) + ".",
            };
            string rests = l.Needs.Length == 0 ? "" : "\nТребует: " + string.Join(l.Needs.Length == 2 && Sim.LoreOf(l.Needs[0])?.Rival == l.Needs[1] ? " или " : ", ", Array.ConvertAll(l.Needs, n => $"«{Sim.LoreOf(n)?.Name ?? n}»")) + ".";
            if (l.Rival != null && sim.OwnSiege) rests += $"\nРазвилка: это или «{Sim.LoreOf(l.Rival)?.Name}».";
            string cost = state == 0 ? "" : $" · {l.Cost} {CoinWord(l.Coin, l.Cost)}";
            k.TooltipText = $"{l.Name} · {l.Branch.ToLowerInvariant()}{cost}\n{l.About}{rests}\n{why}";
        }
        for (int i = 0; i < 3; i++) { _tabs[i].Badge = open[i] > 0 ? open[i].ToString() : ""; _tabs[i].QueueRedraw(); }
        if (IsVisibleInTree()) _tree.QueueRedraw();
    }

    /// <summary>The medallion of a lore (the self-test presses it).</summary>
    public BaseButton PlaqueOf(string key) => _knots.Find(p => p.lore.Key == key).knot;

    /// <summary>"7": the knowledge at hand, for the tab's badge, when something can be learnt with it; else nothing.</summary>
    public string Badge()
    {
        var sim = _m.Sim;
        foreach (var l in Sim.Lores) if (l.Coin == Coin.Knowledge && sim.CanLearn(l)) return $"{sim.Knowledge:0}";
        foreach (var l in Sim.Lores) if (sim.CanLearn(l)) return "✦";
        return "";
    }

    public override GodotObject _MakeCustomTooltip(string forText) => Tooltip.Make(forText);

    /// <summary>
    /// A lore's medallion: a disc of dark iron in a bronze rim, its sigil engraved on it, and its price on a small boss at
    /// its foot until it is learnt. Round to the mouse as it is to the eye.
    /// </summary>
    partial class Knot : BaseButton
    {
        readonly LorePanel _p;
        readonly Lore _l;
        public int State = 3;

        public Knot() { }

        public Knot(LorePanel p, Lore l)
        {
            _p = p; _l = l;
            FocusMode = FocusModeEnum.None;
            MouseDefaultCursorShape = CursorShape.PointingHand;
            MouseEntered += () => { _p._hot = _l; QueueRedraw(); _p._tree.QueueRedraw(); };
            MouseExited += () => { if (_p._hot == _l) _p._hot = null; QueueRedraw(); _p._tree.QueueRedraw(); };
        }

        public override bool _HasPoint(Vector2 point) => point.DistanceTo(Size / 2) <= R + 2;

        public override void _Process(double delta)
        {
            using var prof = Prof.Time("Lore");
            // only the one to be learnt now breathes
            if (State == 1 && IsVisibleInTree()) QueueRedraw();
        }

        public override void _Draw()
        {
            var c = Size / 2;
            var hue = HueOf(_l);
            bool hover = IsHovered();
            DrawCircle(c + new Vector2(1, 2), R + 1, new Color(0, 0, 0, .55f), true, -1, true);
            if (State == 0) DrawCircle(c, R + 3.5f, Ornament.Fade(hue, .22f), true, -1, true);
            if (State == 1)
            {
                float pulse = .55f + .45f * MathF.Sin(Time.GetTicksMsec() / 1000f * 3.6f);
                for (int i = 1; i <= 3; i++) DrawArc(c, R + i * 1.7f, 0, MathF.Tau, 44, Ornament.Fade(Ornament.EmberGlow, (.34f - .09f * i) * pulse), 1.7f, true);
            }
            DrawCircle(c, R, Ornament.IronDark, true, -1, true);
            DrawCircle(c, R - 2.5f, State == 0 ? Ornament.IronMid.Lerp(hue, .14f) : Ornament.IronMid, true, -1, true);
            var rim = hover ? Ornament.BronzeLight : State switch { 0 => hue, 1 => Ornament.EmberGlow, 2 => Ornament.BronzeDark, _ => Ornament.IronLight };
            DrawArc(c, R - 1, 0, MathF.Tau, 48, rim, State <= 1 || hover ? 2.4f : 1.5f, true);
            var ink = State switch
            {
                0 => hue.Lerp(Ornament.Parchment, .35f),
                1 => Ornament.EmberGlow,
                2 => Ornament.Aged,
                _ => Ornament.Faded,
            };
            if (hover && State != 0) ink = ink.Lightened(.15f);
            float s = R * 1.22f;
            Icons.SigilFor(_l.Key)(this, new Rect2(c - new Vector2(s / 2, s / 2), new Vector2(s, s)), ink);
            if (State == 0) return;
            // the price on a small boss at the foot, in the colour of what it is paid in
            var boss = c + new Vector2(R * .72f, R * .72f);
            var coin = _l.Coin switch { Coin.Favour => new Color("3a2552"), Coin.Revelations => new Color("4a1418"), _ => Ornament.BronzeDeep };
            DrawCircle(boss, 7.5f, Ornament.IronDark, true, -1, true);
            DrawCircle(boss, 6.5f, coin, true, -1, true);
            DrawArc(boss, 7, 0, MathF.Tau, 20, State == 1 ? Ornament.EmberGlow : Ornament.BronzeDark, 1.1f, true);
            DrawString(Ornament.Mono, boss + new Vector2(-8, 3.4f), _l.Cost.ToString(), HorizontalAlignment.Center, 16, 9,
                State == 1 ? Ornament.EmberGlow : State == 2 ? Ornament.Aged : Ornament.Faded);
        }

        public override GodotObject _MakeCustomTooltip(string forText) => Tooltip.Make(forText);
    }

    /// <summary>The lore hovered now, whose roots are traced in ember.</summary>
    Lore _hot;

    /// <summary>
    /// The plate the tree is drawn on: rings engraved round its heart like an astrolabe's, rays between the branches, a
    /// great circle about each branch's emblem, the lines between the medallions, the emblems themselves, and the heart.
    /// </summary>
    partial class RingCanvas : Control
    {
        readonly LorePanel _p;
        public RingCanvas(LorePanel p) { _p = p; }
        public RingCanvas() { }

        public override string _GetTooltip(Vector2 at)
        {
            var sim = _p._m.Sim;
            if (at.DistanceTo(C) < Heart + 4)
                return _p._page == 2
                    ? $"Вмешательство: {sim.Favour:0} из {sim.FavourCap:0} · откровений {sim.Revelations}\nВмешательство копят жрецы молитвой и жертвами, откровения дают только жертвы."
                    : $"Знаний: {sim.Knowledge:0}\nЗнания дают за отбитые волны, к концу осады больше. На всё их не хватит.";
            var branches = Sim.PageBranches[_p._page];
            for (int b = 0; b < branches.Length; b++)
            {
                if (at.DistanceTo(At(MarkRY, AxisOf(_p._page, b))) > 22) continue;
                int all = 0, known = 0;
                foreach (var l in Sim.LoresOn(_p._page)) if (l.Branch == branches[b]) { all++; if (sim.Knows(l.Key)) known++; }
                return $"{branches[b]}\nИзучено {known} из {all}.";
            }
            return null;
        }

        public override GodotObject _MakeCustomTooltip(string forText) => Tooltip.Make(forText);

        public override void _Draw()
        {
            if (_p._m == null) return;
            var sim = _p._m.Sim;
            int page = _p._page;
            var branches = Sim.PageBranches[page];
            int n = branches.Length;
            // the plate: dark, rings engraved about its heart, the outer one ticked like an astrolabe's rim
            DrawRect(new Rect2(Vector2.Zero, Size), new Color(0, 0, 0, .28f));
            var etch = Ornament.Fade(Ornament.BronzeDark, .42f);
            foreach (float ry in RY) DrawEllipseArc(C, ry * Wide, ry, 0, MathF.Tau, 120, Ornament.Fade(Ornament.BronzeDark, .22f), 1, true);
            float rim = MarkRY + 24;
            DrawEllipseArc(C, rim * Wide, rim, 0, MathF.Tau, 160, etch, 1.2f, true);
            DrawEllipseArc(C, (rim - 6) * Wide, rim - 6, 0, MathF.Tau, 160, Ornament.Fade(Ornament.BronzeDark, .25f), 1, true);
            for (int i = 0; i < 120; i++)
            {
                float a = i * MathF.Tau / 120, len = i % 10 == 0 ? 6 : 3;
                DrawLine(At(rim, a), At(rim - len, a), etch, 1, true);
            }
            // a great faint circle about each branch's emblem, overlapping its neighbours'
            for (int b = 0; b < n; b++)
                DrawArc(At(MarkRY * .78f, AxisOf(page, b)), 120, 0, MathF.Tau, 72, Ornament.Fade(Ornament.BronzeDark, .2f), 1, true);
            // rays between the branches
            for (int b = 0; b < n; b++)
            {
                float a = AxisOf(page, b) + Sector(page) / 2;
                Ornament.Divider(this, At(RY[0] * .9f, a), At(MarkRY + 10, a), .28f);
            }
            // each branch's own ray: from the heart to its first medallion, and from its last to its emblem
            for (int b = 0; b < n; b++)
            {
                float a = AxisOf(page, b);
                var hue = Hues[page][b];
                Lore first = null, last = null;
                foreach (var l in Sim.LoresOn(page))
                {
                    if (l.Branch != branches[b]) continue;
                    if (l.Tier == 0) first = l;
                    if (l.Tier == 3) last = l;
                }
                bool began = first != null && sim.Knows(first.Key), done = last != null && sim.Knows(last.Key);
                Ray(At(Heart / Wide + 4, a), At(RY[0], a) - Dir(a) * R, began ? hue : Ornament.Fade(Ornament.Faded, .45f), began);
                Ray(At(RY[3], a) + Dir(a) * R, At(MarkRY, a) - Dir(a) * 20, done ? hue : Ornament.Fade(Ornament.Faded, .45f), done);
            }
            // the lines: from what a lore rests on to it
            foreach (var l in Sim.LoresOn(page))
            {
                var to = Spot(l);
                foreach (string need in l.Needs)
                {
                    var from = Sim.LoreOf(need);
                    if (from == null || from.Page != page) continue;
                    var a = Spot(from);
                    var d = (to - a).Normalized();
                    bool both = sim.Knows(need) && sim.Knows(l.Key), open = sim.Knows(need);
                    Ray(a + d * R, to - d * R, both ? HueOf(l) : open ? Ornament.BronzeLight : Ornament.Fade(Ornament.Faded, .55f), both);
                }
            }
            // the roots of the hovered one, traced in ember
            if (_p._hot is { } hot && hot.Page == page) Roots(hot, sim);
            // the emblems at the outer ends
            for (int b = 0; b < n; b++)
            {
                var at = At(MarkRY, AxisOf(page, b));
                var hue = Hues[page][b];
                int all = 0, known = 0;
                foreach (var l in Sim.LoresOn(page)) if (l.Branch == branches[b]) { all++; if (sim.Knows(l.Key)) known++; }
                float k = all == 0 ? 0 : known / (float)all;
                if (k > 0) DrawCircle(at, 24 + 6 * k, Ornament.Fade(hue, .1f + .16f * k), true, -1, true);
                DrawCircle(at + new Vector2(1, 2), 21, new Color(0, 0, 0, .55f), true, -1, true);
                DrawCircle(at, 20, Ornament.IronDark, true, -1, true);
                DrawCircle(at, 17, Ornament.IronMid.Lerp(hue, .12f * k), true, -1, true);
                DrawArc(at, 19, 0, MathF.Tau, 48, k > 0 ? hue : Ornament.BronzeDark, 2.2f, true);
                // the share learnt, as an arc round it
                if (k > 0) DrawArc(at, 22.5f, -MathF.PI / 2, -MathF.PI / 2 + MathF.Tau * k, 48, hue.Lightened(.2f), 2, true);
                Icons.BranchMark(branches[b])(this, new Rect2(at - new Vector2(12, 12), new Vector2(24, 24)), k > 0 ? hue.Lerp(Ornament.Parchment, .3f) : Ornament.Aged);
            }
            Heartstone(sim, page);
        }

        static Vector2 Dir(float a) => new Vector2(MathF.Cos(a) * Wide, MathF.Sin(a)).Normalized();

        /// <summary>A line of the tree: a dark under-cut, then the line itself, a soft glow along it when it is learnt.</summary>
        void Ray(Vector2 a, Vector2 b, Color c, bool lit)
        {
            if (lit) DrawLine(a, b, Ornament.Fade(c, .22f), 6, true);
            DrawLine(a + new Vector2(.8f, 1.2f), b + new Vector2(.8f, 1.2f), new Color(0, 0, 0, .55f), lit ? 3.4f : 2.6f, true);
            DrawLine(a, b, c, lit ? 2.2f : 1.3f, true);
        }

        /// <summary>The roots of a lore: its lines back to the first of its branch, in ember.</summary>
        void Roots(Lore l, Sim sim)
        {
            foreach (string need in l.Needs)
            {
                var from = Sim.LoreOf(need);
                if (from == null || from.Page != l.Page) continue;
                var a = Spot(from); var to = Spot(l); var d = (to - a).Normalized();
                DrawLine(a + d * R, to - d * R, Ornament.Fade(Ornament.EmberGlow, .85f), 2.2f, true);
                Roots(from, sim);
            }
        }

        /// <summary>The heart of the plate: the page's emblem, and what there is to spend on it.</summary>
        void Heartstone(Sim sim, int page)
        {
            DrawCircle(C + new Vector2(1, 2), Heart + 1, new Color(0, 0, 0, .6f), true, -1, true);
            DrawCircle(C, Heart, Ornament.IronDark, true, -1, true);
            DrawCircle(C, Heart - 3, Ornament.IronMid, true, -1, true);
            DrawArc(C, Heart - 1, 0, MathF.Tau, 56, Ornament.Bronze, 2.2f, true);
            Icons.PageMark(page)(this, new Rect2(C - new Vector2(10, 23), new Vector2(20, 20)), Ornament.Fade(Ornament.Bronze, .9f));
            string big = page == 2 ? $"{sim.Favour:0}" : $"{sim.Knowledge:0}";
            Ornament.Lettering(this, Ornament.SerifBold, C + new Vector2(-Heart, 10), big, 20, Ornament.Parchment, HorizontalAlignment.Center, 2 * Heart);
            string small = page == 2 ? $"откр. {sim.Revelations}" : "знаний";
            Ornament.Lettering(this, Ornament.Caps, C + new Vector2(-Heart, 21), small.ToUpperInvariant(), 7, Ornament.Aged, HorizontalAlignment.Center, 2 * Heart);
        }
    }
}
