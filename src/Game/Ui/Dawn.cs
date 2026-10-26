using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The end of a raid, under the beam in the middle: its report (Ui/Battle.cs) — what came of the horde and what became
/// of it, our fallen, the warlord's fate, the bands that broke, what was broken — the night's stars, and at a dawn the
/// gifts to choose one of, each a plate with its sign, what it is and why it came. A click takes it. It can be folded
/// into a strip that waits; what is not taken by the next horn is gone. With no gifts the report is closed with its
/// button, or goes by itself a minute after.
/// </summary>
public partial class DawnCard : Control
{
    Main _m;
    VBoxContainer _col;
    Label _head, _night;
    StarRow _stars;
    HBoxContainer _cards;
    Button _fold;
    bool _folded;
    string _dealt = "";
    double _takenAt = -99;
    string _taken = "";
    /// <summary>The report the player closed.</summary>
    BattleReport _closed;
    /// <summary>How long a report with no gifts stays, seconds of the game.</summary>
    const double ReportStays = 60;
    public const float W = 660;
    /// <summary>The report closed, as its button does when no gifts wait.</summary>
    public void Close() { if (_m.Sim.Gifts.Count == 0) { _closed = _m.Sim.LastBattle; Refresh(); } }

    /// <summary>The card's lines as they read now.</summary>
    public string Said => _head.Text + "\n" + _night.Text;

    public void Init(Main m)
    {
        _m = m;
        CustomMinimumSize = new Vector2(W, 0);
        Size = new Vector2(W, 10);
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;
        var plate = new BottomConsole.Plate(c => Craft.Plate(c, new Rect2(Vector2.Zero, c.Size))) { MouseFilter = MouseFilterEnum.Ignore };
        plate.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(plate);
        _col = new VBoxContainer { Position = new Vector2(18, 12) };
        _col.AddThemeConstantOverride("separation", 6);
        _col.CustomMinimumSize = new Vector2(W - 36, 0);
        AddChild(_col);
        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 10);
        _head = Hud.CapsLabel("", 10, Ornament.EmberGlow);
        top.AddChild(_head);
        _stars = new StarRow { CustomMinimumSize = new Vector2(66, 18), TooltipText = "Звёзды ночи\nЗа целую цитадель, за ночь без потерь и за всех убитых вожаков. Каждая звезда даёт 15 золота и 1 знание. С двумя звёздами даров на выбор 4." };
        top.AddChild(_stars);
        var gap = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        top.AddChild(gap);
        _fold = new Button { Text = "–", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(24, 22), MouseDefaultCursorShape = CursorShape.PointingHand };
        _fold.AddThemeFontSizeOverride("font_size", 14);
        _fold.Pressed += () =>
        {
            // with no gifts waiting the button closes the report
            if (_m.Sim.Gifts.Count == 0) { _closed = _m.Sim.LastBattle; Refresh(); return; }
            _folded = !_folded; Refresh();
        };
        top.AddChild(_fold);
        _col.AddChild(top);
        _night = Hud.Text("", Ornament.Serif, 13, Ornament.Parchment, wrap: true);
        _night.CustomMinimumSize = new Vector2(W - 36, 0);
        _col.AddChild(_night);
        _cards = new HBoxContainer();
        _cards.AddThemeConstantOverride("separation", 10);
        _col.AddChild(_cards);
        _col.Resized += () => Size = new Vector2(W, _col.Size.Y + 24);
    }

    static Icons.Painter Sign(Gift g) => g.Kind switch
    {
        GiftKind.Newcomers => g.Key switch
        {
            "workers" => Icons.Of("wheelbarrow"), "archers" => Icons.Bow, "priest" => Icons.Of("prayer"), "knight" => Icons.Of("black-knight-helm"),
            _ => Icons.Sword,
        },
        GiftKind.Caravan => g.Key switch { "stone" => Icons.Material(Mat.Stone), "wood" => Icons.Material(Mat.Wood), "earth" => Icons.Earth, _ => Icons.Coin },
        GiftKind.Omen => g.Key == "favour" ? Icons.Of("raven") : Icons.Eye,
        GiftKind.Trophy => Icons.SigilFor(g.Key),
        GiftKind.Relic => g.Key switch
        {
            "jaw" => Icons.Of("jawbone"), "hide" => Icons.Of("animal-hide"), "drum" => Icons.Of("drum"), "horn" => Icons.Horn,
            "eye" => Icons.Eye, "staff" => Icons.Of("wizard-staff"), _ => Icons.Of("cross-shield"),
        },
        _ => Icons.Skull,
    };

    static string KindName(GiftKind k) => k switch { GiftKind.Newcomers => "Пришлые", GiftKind.Caravan => "Обоз", GiftKind.Omen => "Знамение", GiftKind.Relic => "Легендарный трофей", _ => "Трофей" };

    public void Refresh()
    {
        var sim = _m.Sim;
        bool taking = sim.Gifts.Count > 0 && !sim.NoErrands && sim.LastNight != null, justTaken = sim.Time - _takenAt < 2.5;
        var b = sim.LastBattle;
        bool report = b != null && b != _closed && !sim.Raids.Active && sim.Time - b.Ended < ReportStays;
        bool due = (taking || justTaken || report) && _m.Tutor == null && !_m.FP.On;
        if (due != Visible) { Visible = due; if (due) Juice.Appear(this); }
        if (!due) return;
        var r = sim.LastNight;
        // the stars are the night's, when this raid was that night
        bool starred = r != null && b != null && r.Number == b.Number;
        bool legend = sim.Gifts.Exists(g => g.Kind == GiftKind.Relic);
        string head = taking && r != null ? $"Рассвет после ночи {r.Number}" : b == null ? "" : !b.Held ? "Цитадель пала" : sim.Source.Key == "map" ? $"Набег {b.Number} отбит" : $"Волна {b.Number} отбита";
        _head.Text = (legend ? head + " · трофеи вожака главы" : head).ToUpperInvariant();
        _stars.Visible = starred;
        _stars.Lit = starred ? r.Stars : 0;
        _stars.QueueRedraw();
        _fold.Text = !taking ? "×" : _folded ? "+" : "–";
        _fold.TooltipText = !taking ? "Закрыть" : _folded ? "Развернуть" : "Свернуть. Дары ждут до следующей волны.";
        if (!taking)
        {
            var said = new List<string>();
            if (justTaken) said.Add($"Принято: {_taken}.");
            if (report) said.AddRange(BattleLines.Of(b));
            if (report && starred && r.Best.Count > 0) said.Add("Отличились: " + string.Join(", ", r.Best.ConvertAll(x => $"{x.who.ToLowerInvariant()} ({x.felled})")));
            _night.Text = string.Join("\n", said);
            _cards.Visible = false;
            return;
        }
        if (_folded)
        {
            _night.Text = $"Дар рассвета ждёт выбора до следующей волны ({sim.Gifts.Count} на выбор).";
            _cards.Visible = false;
            return;
        }
        var lines = b != null && b.Number == r.Number ? BattleLines.Of(b) : new List<string>
        {
            $"Пало налётчиков: {r.Killed} · наших: {r.Lost} · цитадель потеряла {r.KeepLost * 100:0} %",
        };
        lines.Add($"Вожаков убито: {r.LeadersKilled} из {r.Leaders}.");
        if (r.Best.Count > 0) lines.Add("Отличились: " + string.Join(", ", r.Best.ConvertAll(x => $"{x.who.ToLowerInvariant()} ({x.felled})")));
        lines.Add(legend ? "Выберите один трофей, он действует до конца осады:" : "Выберите один дар:");
        _night.Text = string.Join("\n", lines);
        _cards.Visible = true;
        // the plates laid out anew only when the gifts change
        string dealt = string.Join("|", sim.Gifts.ConvertAll(g => g.Kind + g.Key));
        if (dealt == _dealt) return;
        _dealt = dealt;
        foreach (var c in _cards.GetChildren()) c.QueueFree();
        float w = (W - 36 - 10 * (sim.Gifts.Count - 1)) / sim.Gifts.Count;
        for (int i = 0; i < sim.Gifts.Count; i++)
        {
            var g = sim.Gifts[i];
            int at = i;
            var card = new GiftPlate(Sign(g), KindName(g.Kind), g.Title, g.About, w);
            card.Clicked += () =>
            {
                _taken = g.Title.ToLowerInvariant();
                if (sim.TakeGift(at)) { _takenAt = sim.Time; Sound.Ui("done"); _dealt = ""; _m.Hud.Refresh(); }
            };
            _cards.AddChild(card);
        }
    }

    /// <summary>Three stars, lit as many as the night earned.</summary>
    partial class StarRow : Control
    {
        public int Lit;
        public override void _Draw()
        {
            for (int i = 0; i < 3; i++)
            {
                var c = new Vector2(10 + i * 22, Size.Y / 2);
                var pts = new Vector2[10];
                for (int k = 0; k < 10; k++)
                {
                    float a = -MathF.PI / 2 + k * MathF.PI / 5, rr = k % 2 == 0 ? 8.5f : 3.6f;
                    pts[k] = c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * rr;
                }
                if (i < Lit) DrawColoredPolygon(pts, Ornament.EmberGlow);
                var ring = new Vector2[11];
                Array.Copy(pts, ring, 10); ring[10] = pts[0];
                DrawPolyline(ring, i < Lit ? Ornament.BronzeLight : Ornament.BronzeDark, 1.2f, true);
            }
        }
    }
}

/// <summary>One gift on the dawn's card: its sign, its kind, its name and what it is; a click takes it.</summary>
public partial class GiftPlate : Control
{
    readonly Icons.Painter _sign;
    bool _hover;
    public event Action Clicked;

    public GiftPlate() { }

    public GiftPlate(Icons.Painter sign, string kind, string title, string about, float width)
    {
        _sign = sign;
        CustomMinimumSize = new Vector2(width, 184);
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        TooltipText = title + "\n" + about;
        var col = new VBoxContainer { Position = new Vector2(10, 52), CustomMinimumSize = new Vector2(width - 20, 0), MouseFilter = MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", 2);
        AddChild(col);
        var k = Hud.CapsLabel(kind, 8, Ornament.Aged);
        k.HorizontalAlignment = HorizontalAlignment.Center;
        col.AddChild(k);
        var t = Hud.Text(title, Ornament.SerifBold, 14, Ornament.Parchment, wrap: true);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.CustomMinimumSize = new Vector2(width - 20, 0);
        col.AddChild(t);
        var a = Hud.Text(about, Ornament.Serif, 11, Ornament.Aged, wrap: true);
        a.CustomMinimumSize = new Vector2(width - 20, 0);
        a.HorizontalAlignment = HorizontalAlignment.Center;
        col.AddChild(a);
        MouseEntered += () => { _hover = true; QueueRedraw(); };
        MouseExited += () => { _hover = false; QueueRedraw(); };
    }

    public override void _Draw()
    {
        var r = new Rect2(Vector2.Zero, Size);
        DrawRect(r, new Color(0, 0, 0, _hover ? .28f : .18f));
        DrawRect(r, _hover ? Ornament.BronzeLight : Ornament.BronzeDark, false, 1.5f);
        var box = new Rect2(new Vector2(Size.X / 2 - 20, 8), new Vector2(40, 40));
        _sign?.Invoke(this, box, _hover ? Ornament.EmberGlow : Ornament.Bronze);
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } || Clicked == null) return;
        Sound.Ui("tick");
        Juice.Press(this);
        Clicked();
        AcceptEvent();
    }

    public override GodotObject _MakeCustomTooltip(string forText) => Tooltip.Make(forText);
}
