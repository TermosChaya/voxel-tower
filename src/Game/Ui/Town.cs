using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The «Город» tab (Sim.Town.cs): what the town pays a minute, then every kind of building in its order — locked till
/// the lore opens it, then «Поставить» (the plan follows the cursor), going up, and standing: its level, a button to
/// hire each kind it hires with the stock waiting, «Второй этаж» to raise it, and at the market the trade. Under the
/// town's income, the houses' dues in it (Sim.Dues.cs) and a line on what to build next (Sim.TownNext). The tooltips
/// say what each building and each level gives. Laid out again only when something in it changes.
/// </summary>
public partial class TownPanel : VBoxContainer
{
    public const float Width = 470;
    /// <summary>The line of what to build next, as it shows (for the self-test).</summary>
    public string Advice => _advice is { Visible: true } ? _advice.Text : null;
    Main _m;
    Label _income, _standing, _advice;
    VBoxContainer _rows;
    string _key = "";
    double _next;

    public void Init(Main m)
    {
        _m = m;
        AddThemeConstantOverride("separation", 6);
        AddChild(Hud.CapsLabel("Город"));
        _standing = Hud.Text("", Ornament.SerifBold, 14, Ornament.BronzeLight, wrap: true);
        _standing.CustomMinimumSize = new Vector2(Width - 24, 0);
        _standing.TooltipText = Inspector.StandingTip;
        _standing.MouseFilter = MouseFilterEnum.Pass;
        AddChild(_standing);
        _income = Hud.Text("", Ornament.Serif, 13, Ornament.Aged, wrap: true);
        _income.CustomMinimumSize = new Vector2(Width - 24, 0);
        AddChild(_income);
        _advice = Hud.Text("", Ornament.Serif, 13, Ornament.BronzeLight, wrap: true);
        _advice.CustomMinimumSize = new Vector2(Width - 24, 0);
        AddChild(_advice);
        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 7);
        AddChild(_rows);
        Refresh();
    }

    public override void _Process(double delta)
    {
        if (_m == null || !IsVisibleInTree()) return;
        _next -= delta;
        if (_next > 0) return;
        _next = .3;
        Refresh();
    }

    /// <summary>What the panel shows now, to tell whether it needs laying out again.</summary>
    string Key()
    {
        var sim = _m.Sim;
        var key = new System.Text.StringBuilder();
        key.Append(sim.StandingLine()).Append('|').Append(sim.TownNext()).Append('|').Append((int)(sim.Dues() * 10)).Append('|').Append(sim.TownVersion).Append('|').Append((int)sim.Stock.Gold).Append('|').Append(sim.Learnt.Count).Append('|').Append(sim.Household()).Append('|').Append(sim.Places);
        foreach (var b in sim.Buildings)
        {
            key.Append('|').Append(b.Kind).Append(b.Level).Append(b.Target).Append(b.Ready);
            foreach (var (k, n) in b.Stock) key.Append(k).Append((int)n);
        }
        for (int k = 1; k < Stockpile.Kinds; k++) key.Append('|').Append((int)(sim.Stock[k] / VoxelWorld.PerM3));
        return key.ToString();
    }

    public void Refresh()
    {
        string key = Key();
        if (key == _key) return;
        _key = key;
        var sim = _m.Sim;
        float income = sim.TownIncome(), metal = sim.TownMetal(), dues = sim.Dues();
        _standing.Text = "Поселение: " + sim.StandingLine();
        _income.Text = sim.Buildings.Count == 0
            ? "Здания открывают во вкладке «Знания» (страницы «Город» и «Войско»), а ставят здесь: рабочие строят их, потом здание нанимает войска и улучшает их."
            : $"Доход города: {income:0.#} золота{(metal > 0 ? $" и {metal:0.#} м³ металла" : "")} в минуту{(dues > 0 ? $", из них подать с домов {dues:0.#}" : "")}. Зданий: {sim.Buildings.FindAll(b => b.Ready).Count} из {sim.Buildings.Count}.";
        _income.TooltipText = Sim.DuesTip;
        _income.MouseFilter = MouseFilterEnum.Pass;
        string next = sim.TownNext();
        _advice.Text = next ?? "";
        _advice.Visible = next != null;
        for (int i = _rows.GetChildCount() - 1; i >= 0; i--) _rows.GetChild(i).QueueFree();
        foreach (var k in Sim.TownKinds) _rows.AddChild(Row(k));
    }

    Control Row(TownKind k)
    {
        var sim = _m.Sim;
        var b = sim.BuildingOf(k.Key);
        bool open = sim.Sandbox || sim.Knows(k.Lore);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        var icon = new SigilBox(Icons.SigilFor(k.Lore), open ? Ornament.BronzeLight : Ornament.Faded) { CustomMinimumSize = new Vector2(30, 30), TooltipText = Tip(k, b) };
        row.AddChild(icon);
        var col = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", 3);
        row.AddChild(col);
        string state = b == null ? (open ? "" : " · изучите в «Знаниях»")
            : !b.Ready ? (b.Level == 0 ? " · строится" : " · закрыта, не работает")
            : b.Target > b.Level ? $" · ур. {b.Level}, поднимают {TownKind.Storey(b.Target)} этаж" : $" · ур. {b.Level}";
        var name = Hud.Text(k.Name + state, Ornament.SerifBold, 14, open ? Ornament.BronzeLight : Ornament.Faded);
        name.TooltipText = Tip(k, b);
        name.MouseFilter = MouseFilterEnum.Pass;
        col.AddChild(name);
        var buttons = new HFlowContainer();
        buttons.AddThemeConstantOverride("h_separation", 5);
        buttons.AddThemeConstantOverride("v_separation", 4);
        col.AddChild(buttons);
        if (b == null)
        {
            if (!open) return row;
            string no = sim.CantBuild(k);
            string iron = k.MetalCost > 0 ? $" · {k.MetalCost:0.##} м³ мет." : "";
            string where = k.OnOre ? " Ставится только на рудный выход: ржавую скалу, где жила выходит наружу. Пока план в руке, выходы отмечены." : "";
            var put = Button($"Поставить · {k.Gold} зол.{iron}", no == null ? $"Взять план здания: щелчок по земле ставит его, {Bindings.Label(Act.Rotate)} поворачивает.{where}" : $"Нельзя: {no}.");
            put.Disabled = no != null && !no.StartsWith("нужно");
            put.Pressed += () =>
            {
                string why = sim.CantBuild(k);
                if (why != null) { _m.Hud.Float(why, Ornament.Blood); return; }
                _m.Tools.StartBuilding(k);
            };
            buttons.AddChild(put);
            return row;
        }
        if (!b.Ready) return row;
        foreach (var (kind, level, growth) in k.Hires)
        {
            if (b.Level < level) continue;
            b.Stock.TryGetValue(kind.Key, out float n);
            string no = sim.CantHireAt(b, kind);
            var hire = Button($"{kind.Name} · {(int)n} · {sim.HireCost(kind)} зол.{(kind.Metal > 0 ? $" · {kind.Metal:0.##} м³ мет." : "")}", $"{kind.Name}: {Retinue.About(kind)}\nЖдут найма: {(int)n}, прибавляется {growth} за волну.\n" + (no == null ? "Щелчок — нанять, выйдет из двери здания." : $"Нельзя: {no}."));
            hire.Disabled = no != null;
            hire.Pressed += () =>
            {
                if (sim.HireAt(b, kind) == null) _m.Hud.Float(sim.CantHireAt(b, kind) ?? "нельзя", Ornament.Blood);
                else Sound.Ui("tick");
                _key = "";
            };
            buttons.AddChild(hire);
        }
        if (b.Level < k.MaxLevel && b.Target <= b.Level)
        {
            string no = sim.CantRaise(b);
            int level = b.Level + 1;
            var (gold, metal) = k.UpCost(level);
            string storey = TownKind.Storey(level);
            var up = Button($"{char.ToUpper(storey[0])}{storey[1..]} этаж · {gold} зол.{(metal > 0 ? $" · {metal:0.##} м³ мет." : "")}", $"Рабочие снимут крышу и поднимут {storey} этаж. Тогда: {k.Gives(level)}" + (no != null ? $"\nНельзя: {no}." : ""));
            up.Disabled = no != null;
            up.Pressed += () => { string why = sim.Raise(b); if (why != null) _m.Hud.Float(why, Ornament.Blood); else Sound.Ui("mark"); _key = ""; };
            buttons.AddChild(up);
        }
        if (k.Key == "market")
            foreach (int kind in new[] { Stockpile.Stone, Stockpile.Wood, Stockpile.Earth, Stockpile.Metal })
            {
                var (sell, buy) = sim.MarketRates(kind);
                var s = Button($"Продать 1 м³ ({Stockpile.Name(kind)}) · +{sell}", $"Продать кубометр: {Stockpile.Name(kind)} за {sell} золота.");
                s.Pressed += () => { string why = sim.Trade(kind, true); if (why != null) _m.Hud.Float(why, Ornament.Blood); _key = ""; };
                buttons.AddChild(s);
                var bb = Button($"Купить · {buy}", $"Купить кубометр: {Stockpile.Name(kind)} за {buy} золота.");
                bb.Pressed += () => { string why = sim.Trade(kind, false); if (why != null) _m.Hud.Float(why, Ornament.Blood); _key = ""; };
                buttons.AddChild(bb);
            }
        return row;
    }

    static string Tip(TownKind k, Building b)
    {
        string needs = k.Needs.Length == 0 ? "" : "\nНужно: " + string.Join(", ", Array.ConvertAll(k.Needs, n => (n.level > 1 ? "улучшенная " : "") + "«" + Sim.TownKindOf(n.key)?.Name + "»"));
        string up = (k.MaxLevel > 1 ? $"\nВторой этаж: {k.Level2}" : "") + (k.MaxLevel > 2 ? $"\nТретий этаж, когда поселение станет «{Sim.StandingNames[k.Rank3]}»: {k.Level3}" : "");
        string where = k.OnOre ? "\nСтавится только на рудный выход." : "";
        string iron = k.MetalCost > 0 ? $"\nМеталл: {k.MetalCost:0.##} м³" + (k.UpMetal > 0 ? $", этаж {k.UpMetal:0.##} м³." : ".") : "";
        return $"{k.Name}\n{k.About}{up}{where}{iron}{needs}";
    }

    static Button Button(string text, string tip) => new()
    {
        Text = text, TooltipText = tip, FocusMode = FocusModeEnum.None, MouseDefaultCursorShape = CursorShape.PointingHand,
    };
}

/// <summary>A sigil drawn in a little box, in an ink of its own.</summary>
public partial class SigilBox : Control
{
    readonly Icons.Painter _icon;
    readonly Color _ink;
    public SigilBox() { }
    public SigilBox(Icons.Painter icon, Color ink) { _icon = icon; _ink = ink; MouseFilter = MouseFilterEnum.Pass; }
    public override void _Draw() => _icon?.Invoke(this, new Rect2(Vector2.Zero, Size), _ink);
    public override GodotObject _MakeCustomTooltip(string forText) => Tooltip.Make(forText);
}

/// <summary>
/// Over each town building a plate of its name and level, and while it goes up how far; none from someone's eyes, nor
/// over the menu, nor from far off where they would crowd the valley.
/// </summary>
public partial class TownPlates : CanvasLayer
{
    Main _m;
    Control _root;
    readonly Dictionary<Building, Label> _tags = new();
    readonly List<Building> _gone = new();

    /// <summary>The plate over a building as it shows now: null when none shows (for the self-test).</summary>
    public string PlateOf(Building b) => _tags.TryGetValue(b, out var tag) && tag.Visible ? tag.Text : null;

    public void Init(Main m)
    {
        _m = m;
        // under the frame (layer 1): a panel let down covers the plates
        Layer = 0;
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);
    }

    public override void _Process(double delta)
    {
        var sim = _m.Sim;
        var cam = GetViewport().GetCamera3D();
        bool hidden = cam == null || _m.FP.On || _m.Hud.MenuOpen || _m.Hud.TitleOpen || _m.Rig.Cam.Size > 260;
        foreach (var b in sim.Buildings)
        {
            if (_tags.ContainsKey(b)) continue;
            var tag = Hud.Text("", Ornament.SerifBold, 14, Ornament.BronzeLight, outline: 6);
            tag.MouseFilter = Control.MouseFilterEnum.Ignore;
            _root.AddChild(tag);
            _tags[b] = tag;
        }
        _gone.Clear();
        foreach (var (b, tag) in _tags)
        {
            if (!sim.Buildings.Contains(b)) { tag.QueueFree(); _gone.Add(b); continue; }
            var zone = sim.ZoneOf(b);
            int top = zone != null ? zone.Depth + 18 : _m.World.SurfAt((int)b.CX, (int)b.CZ) + 50;
            var at = new Vector3(_m.World.WX(b.CX), _m.World.WY(top), _m.World.WZ(b.CZ));
            tag.Visible = !hidden && !cam.IsPositionBehind(at);
            if (!tag.Visible) continue;
            tag.Text = b.Ready ? (b.Target > b.Level ? $"{b.K.Name} · ур. {b.Level} · {TownKind.Storey(b.Target)} этаж строится" : $"{b.K.Name} · ур. {b.Level}") : b.Level == 0 ? $"{b.K.Name} · строится" : $"{b.K.Name} · не работает";
            tag.Modulate = b.Ready ? Colors.White : new Color(1, 1, 1, .75f);
            tag.ResetSize();
            tag.Position = cam.UnprojectPosition(at) - new Vector2(tag.Size.X / 2, tag.Size.Y);
        }
        foreach (var b in _gone) _tags.Remove(b);
    }
}
