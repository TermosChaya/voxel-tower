using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The dawn after a night held, under the beam in the middle: what the night was, its stars, and the gifts to choose
/// one of — each a plate with its sign, what it is and why it came. A click takes it. It can be folded into a strip
/// that waits; what is not taken by the next horn is gone.
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
    public const float W = 660;

    public void Init(Main m)
    {
        _m = m;
        CustomMinimumSize = new Vector2(W, 0);
        Size = new Vector2(W, 10);
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;
        var plate = new BottomConsole.Plate(c => Craft.Framed(c, new Rect2(Vector2.Zero, c.Size), 10)) { MouseFilter = MouseFilterEnum.Ignore };
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
        _fold.Pressed += () => { _folded = !_folded; Refresh(); };
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
        bool taking = sim.Gifts.Count > 0, justTaken = sim.Time - _takenAt < 2.5;
        bool due = (taking || justTaken) && sim.LastNight != null && _m.Tutor == null && !_m.FP.On && !sim.NoErrands;
        if (due != Visible) Visible = due;
        if (!due) return;
        var r = sim.LastNight;
        bool legend = sim.Gifts.Exists(g => g.Kind == GiftKind.Relic);
        _head.Text = (legend ? $"Рассвет после ночи {r.Number} · трофеи вожака главы" : $"Рассвет после ночи {r.Number}").ToUpperInvariant();
        _stars.Lit = r.Stars;
        _stars.QueueRedraw();
        _fold.Text = _folded ? "+" : "–";
        _fold.TooltipText = _folded ? "Развернуть" : "Свернуть. Дары ждут до следующей волны.";
        if (!taking)
        {
            _night.Text = $"Принято: {_taken}.";
            _cards.Visible = false;
            return;
        }
        if (_folded)
        {
            _night.Text = $"Дар рассвета ждёт выбора до следующей волны ({sim.Gifts.Count} на выбор).";
            _cards.Visible = false;
            return;
        }
        var lines = new List<string>
        {
            $"Пало налётчиков: {r.Killed} · наших: {r.Lost} · цитадель потеряла {r.KeepLost * 100:0} % · вожаков убито: {r.LeadersKilled} из {r.Leaders}",
        };
        if (r.Best.Count > 0) lines.Add("Отличились: " + string.Join(", ", r.Best.ConvertAll(b => $"{b.who.ToLowerInvariant()} ({b.felled})")));
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
        Clicked();
        AcceptEvent();
    }

    public override GodotObject _MakeCustomTooltip(string forText) => Tooltip.Make(forText);
}
