using Godot;

namespace VoxelTower.Game;

/// <summary>
/// A card at the lower right the first time a tool is taken up or a tab let down outside the tutorial: what it is and
/// how it is used, in the words of its own tooltip, with «Понятно» to put it away. Each thing once, kept between games
/// (<see cref="Settings.Meet"/>); what the tutorial shows is met there. The card goes by itself after 20 seconds, and
/// gives way to the next new thing met.
/// </summary>
public partial class FirstLook : PlatePanel
{
    Label _title, _body;
    double _until;
    public const float W = 360;

    public FirstLook() : base(14)
    {
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 8);
        _title = Hud.Text("", Ornament.SerifBold, 15, Ornament.BronzeLight);
        col.AddChild(_title);
        _body = Hud.Text("", Ornament.Serif, 13, Ornament.Parchment, wrap: true);
        _body.CustomMinimumSize = new Vector2(W - 28, 0);
        col.AddChild(_body);
        var ok = new Button { Text = "Понятно", FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ShrinkEnd, MouseDefaultCursorShape = CursorShape.PointingHand };
        ok.Pressed += () => Visible = false;
        col.AddChild(ok);
        AddChild(col);
        Visible = false;
    }

    /// <summary>The card for a tooltip's text: its first line the title, the rest what it says.</summary>
    public void Show(string tip)
    {
        var parts = Bindings.Fill(tip).Split('\n', 2);
        _title.Text = parts[0];
        _body.Text = parts.Length > 1 ? parts[1] : "";
        _body.Visible = parts.Length > 1;
        Size = Vector2.Zero;
        _until = Time.GetTicksMsec() / 1000.0 + 20;
        Visible = true;
    }

    public override void _Process(double delta)
    {
        if (Visible && Time.GetTicksMsec() / 1000.0 > _until) Visible = false;
    }
}
