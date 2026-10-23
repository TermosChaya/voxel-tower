using System;
using System.Collections.Generic;
using Godot;

namespace VoxelTower.Game;

/// <summary>
/// The keys on the buttons while Alt is held: a button with a key of its own is tagged with it when it is made, and as
/// long as Alt is down (and not to turn the view, with a mouse button) a small plate with the key as it is bound now
/// stands at its upper right corner. The tool wells show theirs always and are not tagged.
/// </summary>
public partial class KeyTags : Control
{
    static readonly List<(Control c, Func<string> key)> _tagged = new();
    bool _shown;

    /// <summary>A button with a command's key.</summary>
    public static void Tag(Control c, Act act) => _tagged.Add((c, () => Bindings.Label(act)));

    /// <summary>A button with a key that is no command of its own (Esc, Ctrl+Z, a pair of keys).</summary>
    public static void Tag(Control c, Func<string> key) => _tagged.Add((c, key));

    public KeyTags()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }

    public override void _Process(double delta)
    {
        bool on = Input.IsKeyPressed(Key.Alt) && !Input.IsMouseButtonPressed(MouseButton.Left) && !Input.IsMouseButtonPressed(MouseButton.Right);
        if (on || _shown) QueueRedraw();
        _shown = on;
    }

    public override void _Draw()
    {
        if (!_shown) return;
        _tagged.RemoveAll(t => !IsInstanceValid(t.c));
        var back = GetGlobalTransform().AffineInverse();
        foreach (var (c, key) in _tagged)
        {
            if (!c.IsVisibleInTree()) continue;
            string text = key();
            if (string.IsNullOrEmpty(text)) continue;
            var r = c.GetGlobalRect();
            var corner = back * new Vector2(r.End.X, r.Position.Y);
            float w = Ornament.SerifBold.GetStringSize(text, HorizontalAlignment.Left, -1, 11).X + 10;
            var plate = new Rect2(corner + new Vector2(-w + 6, -8), new Vector2(w, 17));
            DrawRect(plate.Grow(1), new Color(0, 0, 0, .6f));
            DrawRect(plate, Ornament.EmberDim);
            DrawRect(plate, Ornament.Ember, false, 1);
            DrawString(Ornament.SerifBold, plate.Position + new Vector2(5, 12.5f), text, HorizontalAlignment.Left, -1, 11, Ornament.EmberGlow);
        }
    }
}
