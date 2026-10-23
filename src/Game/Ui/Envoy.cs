using System;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The horde's envoy waiting at the walls: what it offers, how long it will wait, and the answer — yes, if the stores
/// can pay, or no. Hangs under the wave clock while there is an offer; the siege goes on meanwhile.
/// </summary>
public partial class EnvoyCard : Control
{
    Main _m;
    Label _words, _wait;
    Button _yes, _no;
    public const float W = 420, H = 138;

    public void Init(Main m)
    {
        _m = m;
        Size = CustomMinimumSize = new Vector2(W, H);
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;
        var plate = new BottomConsole.Plate(c => Craft.Plate(c, new Rect2(Vector2.Zero, c.Size))) { MouseFilter = MouseFilterEnum.Ignore };
        plate.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(plate);
        var col = new VBoxContainer { Position = new Vector2(18, 12), Size = new Vector2(W - 36, H - 22) };
        col.AddThemeConstantOverride("separation", 4);
        AddChild(col);
        col.AddChild(Hud.CapsLabel("Посол орды", 9, Ornament.EmberGlow));
        _words = Hud.Text("", Ornament.SerifItalic, 14, Ornament.Parchment, wrap: true);
        _words.CustomMinimumSize = new Vector2(W - 36, 0);
        col.AddChild(_words);
        _wait = Hud.Text("", Ornament.Serif, 12, Ornament.Aged);
        col.AddChild(_wait);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        _yes = Button("Принять", () => _m.Sim.Answer(true));
        _no = Button("Отказать", () => _m.Sim.Answer(false));
        row.AddChild(_yes);
        row.AddChild(_no);
        col.AddChild(row);
    }

    Button Button(string text, Action act)
    {
        var b = new Button { Text = text, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(120, 28), MouseDefaultCursorShape = CursorShape.PointingHand };
        b.AddThemeFontSizeOverride("font_size", 13);
        b.Pressed += () => { act(); _m.Hud.Refresh(); };
        return b;
    }

    public void Refresh()
    {
        var sim = _m.Sim; var d = sim.Offer;
        bool show = d != null && d.Kind != DealKind.None && d.Envoy is { Dead: false };
        if (show && !Visible) Juice.Appear(this);
        Visible = show;
        if (!Visible) return;
        _words.Text = Sim.OfferText(d);
        _wait.Text = $"Ждёт ответа ещё {Math.Max(0, d.Until - sim.Time):0} с. Если убить посла, следующая волна будет сильнее.";
        bool can = sim.CanPay(d), ransom = d.Kind == DealKind.Ransom;
        _yes.Disabled = !can;
        _yes.TooltipText = ransom ? can ? $"Отпустить пленных ({sim.Prisoners()}) к орде и взять золото." : "Пленных уже нет." : can ? "Отдать и заключить сделку." : "Нечем заплатить.";
    }
}
