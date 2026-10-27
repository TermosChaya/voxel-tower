using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// Advice on what is amiss now, over the map at the lower left: masonry of ours cracking under its load, work the
/// workers cannot get to, a material run out,
/// marks hanging in the air, workers with nothing to do or with all their work turned off, no place left in the
/// household, knowledge or gold that could be spent. Two lines at most, the most pressing first. A click on one does
/// what it says: chooses those people and shows them, takes up the tool, opens the panel; its cross hides it for 5
/// minutes. Nothing is shown when nothing is amiss, in the tutorial, or with the bottom panel put away. The workers are
/// read as the simulation leaves them (their Why): idle with no reason and not told to stand, they have nothing to do,
/// and are told of once one of them has stood so a few seconds.
/// </summary>
public partial class Advisor : VBoxContainer
{
    sealed record Advice(string Key, Icons.Painter Icon, string Text, string Tip, Action Act);

    Main _m;
    double _next;
    string _shown = "";
    // since when each worker has stood with nothing to do; the advice hidden by its cross, and till when (real seconds)
    readonly Dictionary<int, double> _idleSince = new();
    readonly Dictionary<string, double> _hidden = new();
    public const float W = 380;

    public void Init(Main m)
    {
        _m = m;
        AddThemeConstantOverride("separation", 6);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    static double Now => Time.GetTicksMsec() / 1000.0;

    public override void _Process(double delta)
    {
        if (_m == null || Now < _next) return;
        _next = Now + 1;
        var all = Collect();
        var shown = new List<Advice>();
        foreach (var a in all)
            if (!(_hidden.TryGetValue(a.Key, out double until) && Now < until) && shown.Count < 2) shown.Add(a);
        string said = string.Join("|", shown.ConvertAll(a => a.Text));
        if (said == _shown) return;
        _shown = said;
        foreach (var c in GetChildren()) c.QueueFree();
        foreach (var a in shown)
        {
            var key = a.Key;
            AddChild(new Line(a.Icon, a.Text, a.Tip, a.Act, () => { _hidden[key] = Now + 300; _next = 0; }));
        }
    }

    List<Advice> Collect()
    {
        var sim = _m.Sim;
        var list = new List<Advice>();
        if (sim.Over) return list;
        // a commander risen a level with no skill chosen him
        var (risen, risenAway) = _m.Hud.RisenHero();
        if ((risen?.Hero ?? risenAway?.Party.Commander?.Hero) is { } rh)
            list.Add(new Advice("hero", Icons.Of("black-knight-helm"), $"Командир {rh.Name}: новый уровень, выберите умение",
                Bindings.Fill("Новый уровень командира\nКаждый уровень даёт одно умение на выбор. Щелчок откроет его карточку. Её открывает и {Rmb} по его лицу в ряду наверху."),
                () => _m.Hud.OpenRisenHero()));
        // in a campaign, the map of valleys till it has been opened once
        if (Main.Atlas != null && !Settings.Met("atlas"))
            list.Add(new Advice("board", Icons.Of("tied-scroll"), Bindings.Fill("Карта долин: {Board}. По ней ходят герои"),
                Bindings.Fill("Карта долин ({Board})\nВсе долины кампании. Щёлкните героя, потом место на карте, и он пойдёт туда. У долины нажмите «Войти в клетку». Щелчок откроет карту."),
                () => _m.OpenAtlas()));
        // the first architect hired: what he does by himself, till he has been looked at once
        if (!Settings.Met("architect") && sim.Units.Find(u => !u.Dead && u.T == Kinds.Architect && u.Team == Team.Player) is { } arch)
            list.Add(new Advice("architect", Icons.Of("hill-fort"), "Архитектор сам строит оборону",
                "Архитектор\nОн сам размечает стену или частокол вокруг места, ворота к дороге, вышки, ров и колья, а рабочие строят. Щелчок выберет его: план виден призраком, на карточке — выключить и размах.",
                () => { Settings.Meet("architect"); _m.Selected.Clear(); _m.Selected.Add(arch); _m.Hud.OnSelectionChanged(); }));
        var stuck = new List<Unit>(); var idle = new List<Unit>(); var unfooted = new List<Unit>();
        int wanting = Stockpile.None;
        bool barred = false, idleLong = false;
        var seen = new HashSet<int>();
        foreach (var u in sim.Units)
        {
            if (u.Dead || u.Team != Team.Player || u.T != Kinds.Worker || u.Inside || u.Captor != null || u.Airborne != null) continue;
            if (u.State != UnitState.Idle || u.Hold) continue;
            string why = u.Why ?? "";
            if (why == "до работы не дойти") stuck.Add(u);
            else if (why.StartsWith("ждёт опоры")) unfooted.Add(u);
            else if (why.StartsWith("ждёт: ") || why.StartsWith("нет: ")) wanting = KindOf(why[(why.IndexOf(':') + 2)..]);
            else if (why == "все работы отключены") barred = true;
            else if (why == "")
            {
                seen.Add(u.Id);
                idle.Add(u);
                if (!_idleSince.TryGetValue(u.Id, out double since)) _idleSince[u.Id] = since = Now;
                if (Now - since > 6) idleLong = true;
            }
        }
        foreach (int id in new List<int>(_idleSince.Keys)) if (!seen.Contains(id)) _idleSince.Remove(id);

        // what of ours cracks under its load now: where the most of it is
        int cracking = -1;
        foreach (int p in sim.Cracking)
        {
            var (x, y, z) = WaterPlanner.Unpack(p);
            if (Mat.IsPlayer[_m.World.Get(x, y, z)]) { cracking = p; break; }
        }
        if (cracking >= 0)
            list.Add(new Advice("cracks", Icons.Mend, "Кладка трескается: засыпьте подкоп или почините",
                "Кладка трескается\nПостройка несёт больше, чем держит: под ней подкоп, пролом или её разбили камни. Засыпьте яму кистью ({ToolBrush}) или подоприте, починка ({ToolRepair}) переложит треснувшие камни. Щелчок покажет место и возьмёт починку.",
                () =>
                {
                    var (x, _, z) = WaterPlanner.Unpack(cracking);
                    _m.Rig.Focus(_m.World.WX(x), _m.World.WZ(z));
                    _m.Tools.SetTool(Tool.Repair);
                }));
        if (stuck.Count > 0)
            list.Add(new Advice("stuck", Icons.Ladder, "Рабочим не дойти до разметки: нужна лестница или проход",
                "Рабочим не дойти до разметки\nПоставьте лестницу ({ToolLadder}) или оставьте проход к месту стройки. Щелчок покажет, кому.", () => Show(stuck)));
        if (wanting != Stockpile.None)
            list.Add(new Advice("want" + wanting, Icons.Gather, $"Не хватает {Genitive(wanting)}: отведите делянку {Where(wanting)}",
                $"Не хватает {Genitive(wanting)}\nРабочие ждут материал для разметки. Щелчок возьмёт делянку ({{ToolGather}}) с этим материалом: обведите участок.",
                () => { _m.Tools.SetTool(Tool.Gather); _m.Tools.SetMaterial(MatOf(wanting)); }));
        if (unfooted.Count > 0)
            list.Add(new Advice("unfooted", Icons.Brush, "Разметка висит в воздухе: рабочим не на что класть",
                "Разметка висит в воздухе\nКладут только на опору: на землю или на уже сложенное. Снимите висящую разметку или начните её от земли. Щелчок покажет, кто ждёт.",
                () => Show(unfooted)));
        if (idleLong)
            list.Add(new Advice("idle", Icons.Pick, $"{idle.Count} {Plural(idle.Count, "рабочий", "рабочих", "рабочих")} без дела: разметьте стройку или делянку",
                "Рабочие без дела\nНарисуйте стену кистью ({ToolBrush}) или отведите делянку ({ToolGather}), и они возьмутся за неё. Щелчок выберет их.", () => Show(idle)));
        if (barred)
            list.Add(new Advice("barred", Icons.Pick, "У рабочих отключены все работы",
                "Все работы отключены\nВо вкладке «Работы» включите им хотя бы одну работу.", () => _m.Hud.OpenPanel("works")));
        bool canPay = false, roomless = false;
        foreach (var t in Kinds.PlayerKinds)
        {
            if (t == Kinds.Worker || !sim.Unlocked(t)) continue;
            string why = sim.CantEnlist(t);
            if (why == null) canPay = true;
            else if (why.Contains("жилой дом") && sim.Stock.Gold >= sim.HireCost(t)) roomless = true;
        }
        if (roomless && !canPay)
            list.Add(new Advice("rooms", Icons.Of("bunk-beds"), "Мест в войске нет: постройте жилой дом",
                "Мест в войске нет\nПрямоугольник ({ToolBox}) с заливкой «дом»: рабочие поставят стены, крышу и лежанки, каждая даёт место. Щелчок возьмёт его.",
                () =>
                {
                    _m.Tools.SetTool(Tool.Box); _m.Tools.SetDig(false);
                    _m.Tools.Opt.Hollow = true; _m.Tools.Opt.House = true;
                    _m.Tools.Changed();
                }));
        foreach (var l in Sim.Lores)
            if (l.Coin == Coin.Knowledge && sim.CanLearn(l))
            {
                list.Add(new Advice("lore", Icons.Tome, "Есть знания на улучшение", "Есть знания на улучшение\nОткройте вкладку «Знания» и изучите, что нужно.", () => _m.Hud.OpenPanel("lore")));
                break;
            }
        if (canPay)
            list.Add(new Advice("hire", Icons.Coin, "Хватит золота нанять бойца", "Хватит золота нанять бойца\nЦитадель ({Keep}) нанимает войско за золото.", () => _m.Command.OpenKeep()));
        return list;
    }

    /// <summary>These people chosen, and the view brought to the first of them.</summary>
    void Show(List<Unit> who)
    {
        _m.Selected.Clear();
        foreach (var u in who) if (!u.Dead) _m.Selected.Add(u);
        if (who.Count > 0) _m.Rig.Focus(_m.World.WX(who[0].X), _m.World.WZ(who[0].Z));
    }

    static int KindOf(string name) => name switch { "камень" => Stockpile.Stone, "брёвна" => Stockpile.Wood, "земля" => Stockpile.Earth, _ => Stockpile.None };
    static string Genitive(int kind) => kind switch { Stockpile.Stone => "камня", Stockpile.Wood => "брёвен", _ => "земли" };
    static string Where(int kind) => kind switch { Stockpile.Stone => "в скалах", Stockpile.Wood => "в лесу", _ => "на лугу" };
    static byte MatOf(int kind) => kind switch { Stockpile.Wood => Mat.Wood, Stockpile.Earth => Mat.Rammed, _ => Mat.Stone };

    static string Plural(int n, string one, string few, string many)
    {
        int d = n % 10, h = n % 100;
        return d == 1 && h != 11 ? one : d is >= 2 and <= 4 && h is < 12 or > 14 ? few : many;
    }

    /// <summary>One piece of advice: its sign, what is amiss and what to do, a cross to hide it; a click does it.</summary>
    partial class Line : Control
    {
        readonly Icons.Painter _icon;
        readonly string _text;
        readonly Action _act, _hide;
        bool _hover, _overCross;
        const float H = 34;

        public Line() { }

        public Line(Icons.Painter icon, string text, string tip, Action act, Action hide)
        {
            _icon = icon; _text = text; _act = act; _hide = hide;
            TooltipText = tip;
            float w = Math.Min(W, Ornament.Serif.GetStringSize(text, HorizontalAlignment.Left, -1, 13).X + 72);
            CustomMinimumSize = new Vector2(w, H);
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
            MouseFilter = MouseFilterEnum.Stop;
            MouseDefaultCursorShape = CursorShape.PointingHand;
            MouseEntered += () => { _hover = true; QueueRedraw(); };
            MouseExited += () => { _hover = false; _overCross = false; QueueRedraw(); };
        }

        Rect2 Cross => new(Size.X - 26, 0, 26, Size.Y);

        public override void _GuiInput(InputEvent e)
        {
            if (e is InputEventMouseMotion mm)
            {
                bool over = Cross.HasPoint(mm.Position);
                if (over != _overCross) { _overCross = over; QueueRedraw(); }
            }
            if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mb) return;
            Sound.Ui("tick");
            if (Cross.HasPoint(mb.Position)) _hide?.Invoke();
            else _act?.Invoke();
            AcceptEvent();
        }

        public override void _Draw()
        {
            var r = new Rect2(Vector2.Zero, Size);
            Craft.Plate(this, r);
            if (_hover && !_overCross) DrawRect(r.Grow(-1), new Color(Craft.Raised, .5f));
            _icon?.Invoke(this, new Rect2(10, (H - 18) / 2, 18, 18), Ornament.BronzeLight);
            DrawString(Ornament.Serif, new Vector2(36, H / 2 + 13 * .36f), _text, HorizontalAlignment.Left, Size.X - 36 - 28, 13, Ornament.Parchment);
            DrawString(Ornament.Serif, new Vector2(Size.X - 19, H / 2 + 14 * .36f), "×", HorizontalAlignment.Left, -1, 14, _overCross ? Ornament.Parchment : Ornament.Faded);
        }

        public override GodotObject _MakeCustomTooltip(string forText) => Tooltip.Make(forText);
    }
}
