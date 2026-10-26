using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;
using Holder = VoxelTower.Core.Owner;

namespace VoxelTower.Game;

/// <summary>
/// The map's places to spend the realm's gold (Campaign.Economy.cs): the tavern by the citadel with its two heroes, the
/// merchant there with his artifacts and his trade in the rare resources, the citadel's treasury, and a convoy sent from a
/// valley of ours to another. A plate over the map, one of them at a time; the buttons that open them stand with the hero
/// or the valley chosen.
/// </summary>
public partial class AtlasHud
{
    public enum Shop { None, Tavern, Merchant, Convoy }

    PlatePanel _shop;
    Label _shopTitle;
    VBoxContainer _shopBody;
    /// <summary>Which plate is up, and for whom: the hero at the merchant, the valley a convoy goes from and to and its load (logs, stone, metal).</summary>
    public Shop ShopShown { get; private set; }
    MapParty _buyer;
    MapCell _from, _to;
    readonly double[] _load = new double[3];
    static readonly Res[] Hauled = { Res.Logs, Res.Stone, Res.Metal };
    /// <summary>The buttons of the plate up now, by what they say first (for the self-test).</summary>
    public readonly Dictionary<string, Button> ShopButtons = new();

    void EconomyInit()
    {
        _shop = new PlatePanel(16) { Visible = false, MouseFilter = MouseFilterEnum.Stop };
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 8);
        var head = new HBoxContainer();
        _shopTitle = Hud.Text("", Ornament.SerifBold, 18, Ornament.BronzeLight);
        _shopTitle.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(_shopTitle);
        var close = new Button { Text = "×", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(28, 26), MouseDefaultCursorShape = CursorShape.PointingHand, TooltipText = "Закрыть" };
        close.Pressed += CloseShop;
        head.AddChild(close);
        col.AddChild(head);
        _shopBody = new VBoxContainer();
        _shopBody.AddThemeConstantOverride("separation", 8);
        _shopBody.CustomMinimumSize = new Vector2(560, 0);
        col.AddChild(_shopBody);
        _shop.AddChild(col);
        _frame.AddChild(_shop);
    }

    public void CloseShop() { ShopShown = Shop.None; _shop.Visible = false; }

    public void ShowTavern() { ShopShown = Shop.Tavern; RefreshShop(); }
    public void ShowMerchant(MapParty p) { ShopShown = Shop.Merchant; _buyer = p; RefreshShop(); }
    public void ShowConvoy(MapCell from) { ShopShown = Shop.Convoy; _from = from; _to = null; Array.Clear(_load); RefreshShop(); }

    void EconomyProcess(Vector2 vp)
    {
        if (!_shop.Visible) return;
        _shop.Size = Vector2.Zero;
        _shop.Position = new Vector2(MathF.Round((vp.X - Side - _shop.Size.X) / 2), 110);
    }

    /// <summary>The buttons with the hero chosen: by the citadel the tavern's, the merchant's and the treasury's; his things.</summary>
    void EconomyHero(MapParty h)
    {
        var hero = h.Party.Commander?.Hero;
        if (hero == null || !C.Big) return;
        _about.AddChild(Line($"Артефакты: {Artifacts.Worn(hero)}{(hero.Bag.Count > 0 ? $"; в сумке {hero.Bag.Count}" : "")}.", Ornament.Aged));
        if (!C.AtCitadel(h)) return;
        _about.AddChild(Big($"Торговец{(C.Vault.Count > 0 ? $" · в сокровищнице {C.Vault.Count}" : "")}", "Артефакты для героя, обмен редких ресурсов на золото и обратно, сокровищница цитадели.", () => ShowMerchant(h)));
        _about.AddChild(Big("Таверна", $"Два героя ждут найма, по {Campaign.HeroPrice} золота. Каждую неделю новые.", ShowTavern));
    }

    /// <summary>The buttons with a valley of ours chosen: the tavern by the citadel, a convoy from a valley with a stronghold.</summary>
    void EconomyCell(MapCell c)
    {
        if (!C.Big || c.Owner != Holder.Ours) return;
        if (c == C.Home) _about.AddChild(Big("Таверна", $"Два героя ждут найма, по {Campaign.HeroPrice} золота. Каждую неделю новые.", ShowTavern));
        foreach (var s in C.Strongholds)
            if (s == c)
            {
                _about.AddChild(Line($"Запасы: {C.StoreIn(c, Res.Logs):0.#} м³ брёвен, {C.StoreIn(c, Res.Stone):0.#} м³ камня, {C.StoreIn(c, Res.Metal):0.##} м³ металла.", Ornament.Aged));
                _about.AddChild(Big("Обоз", "Отправить брёвна, камень и металл в другую нашу долину. Обоз идёт медленно, враг может его перехватить.", () => ShowConvoy(c)));
                break;
            }
    }

    /// <summary>A button of the plate, kept under its text (or under a key of its own, where texts repeat).</summary>
    Button ShopButton(string text, string tip, Action act, bool can = true, string key = null)
    {
        var b = new Button { Text = text, FocusMode = FocusModeEnum.None, TooltipText = tip, Disabled = !can, MouseDefaultCursorShape = CursorShape.PointingHand };
        b.AddThemeFontSizeOverride("font_size", 14);
        b.Pressed += () => { act(); RefreshShop(); Refresh(); _m.TellMap(); };
        ShopButtons[key ?? text] = b;
        return b;
    }

    static Control Medallion(Icons.Painter sign, Color ink, string tip, float size = 44)
    {
        var m = new Medal { Sign = sign, Ink = ink, CustomMinimumSize = new Vector2(size, size), TooltipText = tip, MouseFilter = MouseFilterEnum.Stop };
        return m;
    }

    sealed partial class Medal : Control
    {
        public Icons.Painter Sign;
        public Color Ink;
        public override void _Draw()
        {
            var r = new Rect2(Vector2.Zero, Size);
            DrawRect(r, new Color(0, 0, 0, .35f));
            DrawRect(r, Craft.Brass, false, 1);
            Sign?.Invoke(this, r.Grow(-6), Ink);
        }
        public override GodotObject _MakeCustomTooltip(string forText) => Tooltip.Make(forText);
    }

    /// <summary>The sign of a place a hero wears a thing in.</summary>
    public static Icons.Painter SlotSign(ArtSlot s) => s switch
    {
        ArtSlot.Head => Icons.Of("visored-helm"), ArtSlot.Neck => Icons.Of("prayer-beads"), ArtSlot.Body => Icons.Of("chain-mail"),
        ArtSlot.Weapon => Icons.Of("broadsword"), ArtSlot.Shield => Icons.Of("shield"), ArtSlot.Feet => Icons.Of("footprint"),
        ArtSlot.Ring => Icons.Of("magic-swirl"), _ => Icons.Of("leather-vest"),
    };

    public static Color TierInk(ArtTier t) => t switch { ArtTier.Relic => Ornament.EmberGlow, ArtTier.Major => Ornament.BronzeLight, _ => Ornament.Parchment };

    /// <summary>What a thing is, for its hover: its name, its tier and place, what it gives.</summary>
    public static string ArtTip(Artifact a) => $"{a.Name}\n{Artifacts.TierNames[(int)a.Tier]}, {Artifacts.SlotNames[(int)a.Slot]}\n{a.About()}";

    void RefreshShop()
    {
        ShopButtons.Clear();
        foreach (var c in _shopBody.GetChildren()) c.QueueFree();
        _shop.Visible = ShopShown != Shop.None;
        if (!_shop.Visible) return;
        switch (ShopShown)
        {
            case Shop.Tavern: Tavern(); break;
            case Shop.Merchant: Merchant(); break;
            case Shop.Convoy: Convoy(); break;
        }
    }

    void Tavern()
    {
        _shopTitle.Text = "Таверна у цитадели";
        _shopBody.AddChild(Line($"Героев у державы {C.HeroCount()} из {C.HeroCap()}. Ещё одного примет каждый этаж ратуши. Каждую неделю в таверне двое новых.", Ornament.Aged));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        _shopBody.AddChild(row);
        var list = C.TavernNow();
        if (list.Count == 0) _shopBody.AddChild(Line("В таверне никого до следующей недели.", Ornament.Aged));
        for (int i = 0; i < list.Count; i++)
        {
            var t = list[i];
            int idx = i;
            var plate = new PlatePanel(10) { CustomMinimumSize = new Vector2(270, 0) };
            var box = new VBoxContainer();
            box.AddThemeConstantOverride("separation", 6);
            var top = new HBoxContainer();
            top.AddThemeConstantOverride("separation", 10);
            top.AddChild(new TextureRect { Texture = Portraits.Of(Kinds.Captain, (int)(t.Hero.Seed & 3), 64), CustomMinimumSize = new Vector2(64, 64), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered });
            var who = new VBoxContainer();
            who.AddChild(Hud.Text(t.Hero.Name, Ornament.SerifBold, 16, Ornament.Parchment));
            who.AddChild(Hud.Text($"{Upper(t.Calling)} · ур. 1", Ornament.Serif, 13, Ornament.BronzeLight));
            Skill sk = Skill.Line;
            for (int s = 0; s < Heroes.Count; s++) if (t.Hero.Grade[s] > 0) sk = (Skill)s;
            var signs = new HBoxContainer();
            signs.AddChild(Medallion(HeroCard.SignOf(sk), Ornament.BronzeLight, $"{Heroes.NameOf(sk)} I\n{Heroes.About(sk, 1)}", 30));
            who.AddChild(signs);
            top.AddChild(who);
            box.AddChild(top);
            var men = new List<string>();
            foreach (var k in t.Army) men.Add(k.Name.ToLowerInvariant());
            var army = Hud.Text($"С ним: {string.Join(", ", men)}.", Ornament.Serif, 13, Ornament.Aged, wrap: true);
            army.CustomMinimumSize = new Vector2(250, 0);
            box.AddChild(army);
            string no = C.CantHireHero(idx);
            box.AddChild(ShopButton($"Нанять · {Campaign.HeroPrice} зол.", no == null ? $"{t.Hero.Name} с отрядом встанет у цитадели на карте." : $"Нельзя: {no}.", () =>
            {
                var p = C.HireHero(idx);
                if (p != null) { Choose(p, focus: true); _m.Hud.Float($"Нанят {p.Party.Commander.Hero.Name}", Ornament.Moss); Sound.Ui("mark"); }
            }, no == null));
            plate.AddChild(box);
            row.AddChild(plate);
        }
    }

    void Merchant()
    {
        var p = _buyer;
        _shopTitle.Text = "Торговец у цитадели";
        if (p == null || !C.Parties.Contains(p) || !C.AtCitadel(p)) { _shopBody.AddChild(Line("Торговец говорит только с героем у цитадели.", Ornament.Aged)); return; }
        var hero = p.Party.Commander.Hero;
        _shopBody.AddChild(Line($"Покупает {hero.Name}. Купленное он сразу надевает, а если место занято, кладёт в сумку (карточка героя).", Ornament.Aged));
        _shopBody.AddChild(Hud.CapsLabel("Артефакты этой недели", 10));
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 6);
        foreach (var key in C.MarketNow())
        {
            var a = Artifacts.Of(key);
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", 8);
            line.AddChild(Medallion(SlotSign(a.Slot), TierInk(a.Tier), ArtTip(a), 36));
            var text = new VBoxContainer { CustomMinimumSize = new Vector2(150, 0) };
            text.AddChild(Hud.Text(a.Name, Ornament.SerifBold, 13, TierInk(a.Tier)));
            text.AddChild(Hud.Text(a.About(), Ornament.Serif, 11, Ornament.Aged));
            line.AddChild(text);
            string no = C.CantBuy(p, key);
            var k = key;
            line.AddChild(ShopButton(a.Price(), no == null ? $"Купить «{a.Name}» для {hero.Name}." : $"Нельзя: {no}.", () => { if (C.Buy(p, k)) Sound.Ui("mark"); }, no == null));
            grid.AddChild(line);
        }
        _shopBody.AddChild(grid);
        // the rare resources' trade, as the marketplace of «Heroes»
        _shopBody.AddChild(Hud.CapsLabel("Редкие ресурсы", 10));
        var trade = new HBoxContainer();
        trade.AddThemeConstantOverride("separation", 10);
        for (int i = 0; i < C.Rare.Length; i++)
        {
            int ri = i;
            var r = (Res)((int)Res.Mercury + i);
            var col = new VBoxContainer();
            col.AddChild(Hud.Text($"{Upper(MapThing.NameOf(r))}: {C.Rare[i]:0}", Ornament.Serif, 13, Ornament.Parchment));
            var b = new HBoxContainer();
            b.AddChild(ShopButton($"Продать · {Campaign.RareSells}", $"Продать 1 ({MapThing.NameOf(r)}) за {Campaign.RareSells} золота.", () => C.SellRare(ri, 1), C.Rare[i] >= 1, $"Продать {MapThing.NameOf(r)}"));
            b.AddChild(ShopButton($"Купить · {Campaign.RareBuys}", $"Купить 1 ({MapThing.NameOf(r)}) за {Campaign.RareBuys} золота.", () => C.BuyRare(ri, 1), C.StockOf(Res.Gold) >= Campaign.RareBuys, $"Купить {MapThing.NameOf(r)}"));
            col.AddChild(b);
            trade.AddChild(col);
        }
        _shopBody.AddChild(trade);
        if (C.Vault.Count > 0)
        {
            _shopBody.AddChild(Hud.CapsLabel("Сокровищница цитадели", 10));
            var vault = new HBoxContainer();
            vault.AddThemeConstantOverride("separation", 8);
            foreach (var key in C.Vault.ToArray())
            {
                var a = Artifacts.Of(key);
                var k = key;
                vault.AddChild(ShopButton($"Взять «{a.Name}»", ArtTip(a), () => C.TakeFromVault(p, k)));
            }
            _shopBody.AddChild(vault);
        }
    }

    void Convoy()
    {
        _shopTitle.Text = "Обоз";
        if (_from == null) { CloseShop(); return; }
        var to = new List<MapCell>();
        foreach (var s in C.Strongholds) if (s != _from) to.Add(s);
        _shopBody.AddChild(Line($"Из {Campaign.Names(_from).gen}: {C.StoreIn(_from, Res.Logs):0.#} м³ брёвен, {C.StoreIn(_from, Res.Stone):0.#} м³ камня, {C.StoreIn(_from, Res.Metal):0.##} м³ металла.", Ornament.Parchment));
        if (to.Count == 0) { _shopBody.AddChild(Line("Обозу некуда идти: нужна вторая наша долина с цитаделью или заставой.", Ornament.Aged)); return; }
        _to ??= to.Contains(C.Home) ? C.Home : to[0];
        var where = new HBoxContainer();
        where.AddThemeConstantOverride("separation", 8);
        where.AddChild(Hud.Text("Куда:", Ornament.Serif, 14, Ornament.Aged));
        foreach (var c in to)
        {
            var cc = c;
            var b = ShopButton($"{Upper(Campaign.Names(c).nom)} ({c.Col},{c.Row})", "Сюда пойдёт обоз.", () => _to = cc);
            if (c == _to) b.AddThemeColorOverride("font_color", Ornament.EmberGlow);
            where.AddChild(b);
        }
        _shopBody.AddChild(where);
        for (int k = 0; k < Hauled.Length; k++)
        {
            int kk = k;
            var r = Hauled[k];
            double have = C.StoreIn(_from, r);
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", 8);
            var name = Hud.Text($"{Upper(MapThing.NameOf(r))}: {_load[k]:0.#} м³", Ornament.Serif, 14, Ornament.Parchment);
            name.CustomMinimumSize = new Vector2(180, 0);
            line.AddChild(name);
            line.AddChild(ShopButton($"−5 {MapThing.NameOf(r)}", "Меньше на 5 м³.", () => _load[kk] = Math.Max(0, _load[kk] - 5), _load[k] > 0));
            line.AddChild(ShopButton($"+5 {MapThing.NameOf(r)}", "Больше на 5 м³.", () => _load[kk] = Math.Min(have, _load[kk] + 5), _load[k] < have));
            line.AddChild(ShopButton($"Всё {MapThing.NameOf(r)}", "Весь запас.", () => _load[kk] = have, have > 0));
            _shopBody.AddChild(line);
        }
        double sum = _load[0] + _load[1] + _load[2];
        string no = C.CantConvoy(_from, _to, _load);
        _shopBody.AddChild(ShopButton($"Отправить · {Campaign.ConvoyCost(sum)} зол.", no == null ? "Обоз выйдет на карту и пойдёт сам." : $"Нельзя: {no}.", () =>
        {
            if (C.SendConvoy(_from, _to, _load, out string why) != null) { _m.Hud.Float("Обоз вышел", Ornament.Moss); Sound.Ui("mark"); CloseShop(); }
            else _m.Hud.Float(why, Ornament.Blood);
        }, no == null));
    }
}
