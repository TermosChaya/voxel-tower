using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// A commander's card as a hero's (Core/Heroes.cs), in the middle of the screen: his face, his name, his level and the
/// experience toward the next, those he struck down; his eight skills as signs with their grades, each telling what it
/// does when the mouse rests on it; at a level risen the skills offered, one to take with a click; and those he leads.
/// It is opened by the commander's face (the right button in the row of faces, the sign in his card at the bottom), by
/// his party's face on the map of valleys, and by the medallion that comes up when a hero has a skill to choose.
/// </summary>
public partial class HeroCard : Control
{
    Main _m;
    VBoxContainer _col;
    TextureRect _face;
    Label _name, _level, _deeds, _party, _offerHead;
    XpBar _bar;
    GridContainer _skills;
    HBoxContainer _offer;
    // whose card it is: a commander in the valley, or a party's on the map
    Unit _unit;
    MapParty _away;
    string _shown = "";
    public const float W = 600;

    /// <summary>The hero whose card is open, or null.</summary>
    public Hero Hero => _unit?.Hero ?? _away?.Party.Commander?.Hero;
    public bool IsOpen => Visible && Hero != null;
    /// <summary>The buttons of the skills offered at a level, as the card shows them now; and its name line (for the self-test).</summary>
    public List<Button> Choices => _offer.GetChildren().OfType<Choice>().Where(c => !c.IsQueuedForDeletion()).Cast<Button>().ToList();
    public string NameShown => _name.Text;

    static readonly Icons.Painter[] Signs =
    {
        Icons.Of("attached-shield"), Icons.Of("bowman"), Icons.Of("footprint"), Icons.Of("holy-symbol"),
        Icons.Of("sword-clash"), Icons.Of("rally-the-troops"), Icons.Of("raven"), Icons.Mend,
    };
    public static Icons.Painter SignOf(Skill s) => Signs[(int)s];

    public void Init(Main m)
    {
        _m = m;
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        CustomMinimumSize = new Vector2(W, 0);
        Size = new Vector2(W, 10);
        var plate = new BottomConsole.Plate(c => Craft.Plate(c, new Rect2(Vector2.Zero, c.Size))) { MouseFilter = MouseFilterEnum.Ignore };
        plate.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(plate);
        _col = new VBoxContainer { Position = new Vector2(20, 16) };
        _col.AddThemeConstantOverride("separation", 8);
        _col.CustomMinimumSize = new Vector2(W - 40, 0);
        AddChild(_col);
        _col.Resized += () => Size = new Vector2(W, _col.Size.Y + 32);

        // the face, the name, the level and its bar
        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 16);
        _face = new TextureRect { CustomMinimumSize = new Vector2(96, 96), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = MouseFilterEnum.Ignore };
        top.AddChild(_face);
        var who = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        who.AddThemeConstantOverride("separation", 4);
        var head = new HBoxContainer();
        _name = Hud.Text("", Ornament.SerifBold, 21, Ornament.Parchment);
        _name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(_name);
        var close = new Button { Text = "×", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(28, 26), MouseDefaultCursorShape = CursorShape.PointingHand, TooltipText = "Закрыть (Esc)" };
        close.AddThemeFontSizeOverride("font_size", 16);
        close.Pressed += Close;
        head.AddChild(close);
        who.AddChild(head);
        _level = Hud.Text("", Ornament.Serif, 14, Ornament.BronzeLight);
        who.AddChild(_level);
        _bar = new XpBar { CustomMinimumSize = new Vector2(W - 172, 10) };
        who.AddChild(_bar);
        _deeds = Hud.Text("", Ornament.Serif, 13, Ornament.Aged);
        who.AddChild(_deeds);
        top.AddChild(who);
        _col.AddChild(top);

        // the level's choice
        _offerHead = Hud.CapsLabel("Новый уровень · выберите умение", 10, Ornament.EmberGlow);
        _col.AddChild(_offerHead);
        _offer = new HBoxContainer();
        _offer.AddThemeConstantOverride("separation", 10);
        _col.AddChild(_offer);

        // the skills
        _col.AddChild(Hud.CapsLabel("Умения", 9));
        _skills = new GridContainer { Columns = 4 };
        _skills.AddThemeConstantOverride("h_separation", 8);
        _skills.AddThemeConstantOverride("v_separation", 8);
        _col.AddChild(_skills);
        for (int i = 0; i < Heroes.Count; i++) _skills.AddChild(new SkillTile(this, (Skill)i) { CustomMinimumSize = new Vector2((W - 40 - 24) / 4, 46) });

        // those he leads
        _party = Hud.Text("", Ornament.Serif, 13, Ornament.Aged, wrap: true);
        _party.CustomMinimumSize = new Vector2(W - 40, 0);
        _col.AddChild(_party);
    }

    /// <summary>The card of a commander of the valley.</summary>
    public void Show(Unit cap)
    {
        if (cap?.Hero == null) return;
        _unit = cap; _away = null;
        Open();
    }

    /// <summary>The card of the hero at the head of a party of ours on the map.</summary>
    public void Show(MapParty p)
    {
        if (p?.Party.Commander?.Hero == null) return;
        _away = p; _unit = null;
        Open();
    }

    void Open()
    {
        _shown = "";
        if (!Visible) { Visible = true; Juice.Appear(this); Sound.Ui("tick"); }
        Refresh();
    }

    public void Close() { Visible = false; _unit = null; _away = null; }

    /// <summary>One of the offered skills taken (the card's buttons and the self-test).</summary>
    public bool Take(Skill s)
    {
        var h = Hero;
        if (h == null) return false;
        bool done = _unit != null ? _m.Sim.ChooseSkill(_unit, s) : Heroes.Choose(h, s);
        if (done)
        {
            Sound.Ui("tick");
            _m.Hud.Float($"{h.Name}: {Heroes.NameOf(s)} {Sim.Roman(h[s])}", Ornament.Moss);
            _m.Hud.Board?.Refresh();
        }
        _shown = "";
        Refresh();
        return done;
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        var vp = GetViewportRect().Size;
        Position = new Vector2(MathF.Round((vp.X - Size.X) / 2), MathF.Round(Math.Max(60, (vp.Y - Size.Y) / 2 - 40)));
        if (Hero == null || _unit != null && _unit.Dead || _away != null && Main.Atlas?.Parties.Contains(_away) != true) { Close(); return; }
        Refresh();
    }

    void Refresh()
    {
        var h = Hero;
        if (h == null) return;
        string kind = _unit?.T.Key ?? "captain";
        int look = _unit?.Look ?? _away.Party.Commander.Look;
        string party = _unit != null ? $"Под его словом сейчас: {_m.Sim.Heeding(_unit)} {Sim.Plural(_m.Sim.Heeding(_unit), "боец", "бойца", "бойцов")} в {Math.Round(40 * Sim.WordFar(_unit))} м."
            : $"Его отряд на карте: {MapBoard.Who(_away).ToLowerInvariant()}.";
        string key = $"{h.Name}|{h.Level}|{h.Xp}|{h.Unspent}|{string.Join(",", h.Grade)}|{string.Join(",", h.Offer)}|{h.Kills}|{party}";
        if (key == _shown) return;
        _shown = key;
        _face.Texture = _unit != null ? Portraits.Of(_unit, 96) : Portraits.Of(Kinds.Captain, look, 96);
        _name.Text = $"Командир {h.Name}";
        _level.Text = h.Level >= Heroes.MaxLevel ? $"Уровень {h.Level} · высший · опыт {h.Xp}" : $"Уровень {h.Level} · опыт {h.Xp} из {h.Next}";
        _bar.Share = h.Level >= Heroes.MaxLevel ? 1 : Math.Clamp((h.Xp - h.From) / (float)Math.Max(1, h.Next - h.From), 0, 1);
        _bar.QueueRedraw();
        _deeds.Text = $"Сразил сам: {h.Kills}. Умения: {Heroes.Skills(h)}.";
        _party.Text = party;
        foreach (var c in _skills.GetChildren()) ((Control)c).QueueRedraw();
        foreach (var c in _offer.GetChildren()) c.QueueFree();
        _offerHead.Visible = _offer.Visible = h.Pending;
        if (h.Pending)
        {
            _offerHead.Text = (h.Unspent > 1 ? $"Новый уровень · выберите умение · ещё {h.Unspent}" : "Новый уровень · выберите умение").ToUpperInvariant();
            foreach (var s in h.Offer) _offer.AddChild(new Choice(this, s, h) { CustomMinimumSize = new Vector2((W - 40 - 20) / 3, 132) });
        }
    }

    public override GodotObject _MakeCustomTooltip(string forText) => Tooltip.Make(forText);

    /// <summary>How far through his level he is.</summary>
    sealed partial class XpBar : Control
    {
        public float Share;
        public XpBar() { MouseFilter = MouseFilterEnum.Ignore; }
        public override void _Draw()
        {
            var r = new Rect2(Vector2.Zero, Size);
            DrawRect(r, new Color(0, 0, 0, .6f));
            DrawRect(new Rect2(1, 1, (Size.X - 2) * Share, Size.Y - 2), Ornament.BronzeLight);
            DrawRect(r, Craft.Brass, false, 1);
        }
    }

    /// <summary>A skill's sign with its grade: bright if he has it, faint if not; what it does at the mouse.</summary>
    sealed partial class SkillTile : Control
    {
        readonly HeroCard _card; readonly Skill _s;
        public SkillTile() { }
        public SkillTile(HeroCard card, Skill s) { _card = card; _s = s; MouseFilter = MouseFilterEnum.Stop; TooltipText = " "; }

        public override string _GetTooltip(Vector2 at)
        {
            var h = _card.Hero;
            int g = h?[_s] ?? 0;
            string now = g > 0 ? $"Сейчас ({Sim.Roman(g)}): {Heroes.About(_s, g)}" : "Не изучено.";
            string next = g < Heroes.Grades ? $"\nДальше ({Sim.Roman(g + 1)}): {Heroes.About(_s, g + 1)}" : "";
            return $"{Heroes.NameOf(_s)}\n{now}{next}";
        }

        public override GodotObject _MakeCustomTooltip(string forText) => Tooltip.Make(forText);

        public override void _Draw()
        {
            int g = _card.Hero?[_s] ?? 0;
            var r = new Rect2(Vector2.Zero, Size);
            DrawRect(r, g > 0 ? new Color(0, 0, 0, .35f) : new Color(0, 0, 0, .18f));
            DrawRect(r, g > 0 ? Craft.Brass : Ornament.Fade(Ornament.Faded, .35f), false, 1);
            float s = Size.Y - 12;
            SignOf(_s)(this, new Rect2(6, 6, s, s), g > 0 ? Ornament.BronzeLight : Ornament.Fade(Ornament.Faded, .55f));
            DrawString(Ornament.SerifBold, new Vector2(s + 14, Size.Y / 2 - 2), Heroes.NameOf(_s), HorizontalAlignment.Left, Size.X - s - 18, 14, g > 0 ? Ornament.Parchment : Ornament.Faded);
            // the grade, as three pips
            for (int i = 0; i < Heroes.Grades; i++)
            {
                var c = new Vector2(s + 18 + i * 11, Size.Y / 2 + 10);
                DrawCircle(c, 3.6f, i < g ? Ornament.EmberGlow : new Color(0, 0, 0, .55f), true, -1, true);
                DrawArc(c, 3.6f, 0, MathF.Tau, 12, i < g ? Ornament.BronzeLight : Ornament.Fade(Ornament.Faded, .5f), 1, true);
            }
        }
    }

    /// <summary>One skill offered at a level: its sign, its name and the grade it brings, what it does then; a click takes it.</summary>
    sealed partial class Choice : Button
    {
        readonly HeroCard _card; readonly Skill _s; readonly Hero _h;
        public Choice() { }
        public Choice(HeroCard card, Skill s, Hero h)
        {
            _card = card; _s = s; _h = h;
            FocusMode = FocusModeEnum.None;
            MouseDefaultCursorShape = CursorShape.PointingHand;
            int g = h[s] + 1;
            TooltipText = $"{Heroes.NameOf(s)} {Sim.Roman(g)}\n{Heroes.About(s, g)} Щелчок — взять.";
            Pressed += () => _card.Take(_s);
        }

        public override GodotObject _MakeCustomTooltip(string forText) => Tooltip.Make(forText);

        public override void _Draw()
        {
            var r = new Rect2(Vector2.Zero, Size);
            bool hover = IsHovered();
            Ornament.Gradient(this, r, hover ? new Color("3a2c22") : new Color("2a2024"), new Color("0e0b0d"));
            DrawRect(r, hover ? Ornament.EmberGlow : Craft.Brass, false, hover ? 2 : 1);
            int g = _h[_s] + 1;
            SignOf(_s)(this, new Rect2(10, 10, 34, 34), hover ? Ornament.EmberGlow : Ornament.BronzeLight);
            DrawString(Ornament.SerifBold, new Vector2(52, 26), $"{Heroes.NameOf(_s)} {Sim.Roman(g)}", HorizontalAlignment.Left, Size.X - 58, 15, Ornament.Parchment);
            DrawString(Ornament.Serif, new Vector2(52, 42), g > 1 ? "сильнее" : "новое", HorizontalAlignment.Left, Size.X - 58, 12, Ornament.Aged);
            DrawMultilineString(Ornament.Serif, new Vector2(10, 64), Heroes.About(_s, g), HorizontalAlignment.Left, Size.X - 20, 12, 5, Ornament.Aged);
        }
    }
}
