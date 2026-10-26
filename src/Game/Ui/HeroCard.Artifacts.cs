using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The hero's things on his card (Artifacts.cs): a medallion for each place he wears one, its thing's sign and name in
/// the hover, a click taking it off into his bag; what he carries under them, a click putting it on.
/// </summary>
public partial class HeroCard
{
    HBoxContainer _worn, _bag;
    Label _bagHead;

    void ArtifactsInit()
    {
        _col.AddChild(Hud.CapsLabel("Артефакты", 9));
        _worn = new HBoxContainer();
        _worn.AddThemeConstantOverride("separation", 6);
        _col.AddChild(_worn);
        _bagHead = Hud.CapsLabel("В сумке · щелчок надевает", 9);
        _col.AddChild(_bagHead);
        _bag = new HBoxContainer();
        _bag.AddThemeConstantOverride("separation", 6);
        _col.AddChild(_bag);
    }

    /// <summary>What the card shows of his things, as a key: shown anew when it changes.</summary>
    static string ArtifactsKey(Hero h) => string.Join(",", h.Worn) + "|" + string.Join(",", h.Bag);

    void ArtifactsRefresh(Hero h)
    {
        foreach (var c in _worn.GetChildren()) c.QueueFree();
        foreach (var c in _bag.GetChildren()) c.QueueFree();
        float size = (W - 40 - 7 * 6) / 8;
        for (int i = 0; i < Artifacts.Slots; i++)
        {
            var slot = (ArtSlot)i;
            var a = Artifacts.Of(h.Worn[i]);
            var b = new ThingButton
            {
                Sign = AtlasHud.SlotSign(slot), Ink = a != null ? AtlasHud.TierInk(a.Tier) : Ornament.Fade(Ornament.Faded, .45f), Worn = a != null,
                CustomMinimumSize = new Vector2(size, size),
                TooltipText = a != null ? AtlasHud.ArtTip(a) + "\nЩелчок: снять в сумку." : $"{char.ToUpper(Artifacts.SlotNames[i][0])}{Artifacts.SlotNames[i][1..]}: пусто.",
            };
            if (a != null) b.Pressed += () => { if (Artifacts.TakeOff(h, slot)) { Sound.Ui("tick"); _shown = ""; Refresh(); } };
            _worn.AddChild(b);
        }
        _bagHead.Visible = _bag.Visible = h.Bag.Count > 0;
        foreach (var key in h.Bag.ToArray())
        {
            var a = Artifacts.Of(key);
            if (a == null) continue;
            var k = key;
            var b = new ThingButton { Sign = AtlasHud.SlotSign(a.Slot), Ink = AtlasHud.TierInk(a.Tier), Worn = true, CustomMinimumSize = new Vector2(size * .8f, size * .8f), TooltipText = AtlasHud.ArtTip(a) + "\nЩелчок: надеть." };
            b.Pressed += () => { if (Artifacts.PutOn(h, k)) { Sound.Ui("tick"); _shown = ""; Refresh(); } };
            _bag.AddChild(b);
        }
    }

    /// <summary>A place a hero wears a thing in, or a thing in his bag: its sign on a dark square, framed in brass when filled.</summary>
    sealed partial class ThingButton : Button
    {
        public Icons.Painter Sign;
        public Color Ink;
        public bool Worn;
        public ThingButton() { FocusMode = FocusModeEnum.None; Flat = true; MouseDefaultCursorShape = CursorShape.PointingHand; }
        public override void _Draw()
        {
            var r = new Rect2(Vector2.Zero, Size);
            DrawRect(r, new Color(0, 0, 0, Worn ? .4f : .2f));
            DrawRect(r, Worn ? Craft.Brass : Ornament.Fade(Ornament.Faded, .35f), false, 1);
            Sign?.Invoke(this, r.Grow(-7), Ink);
        }
        public override GodotObject _MakeCustomTooltip(string forText) => Tooltip.Make(forText);
    }
}
