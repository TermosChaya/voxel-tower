using Godot;

namespace VoxelTower.Game;

/// <summary>
/// The interface's small motions, each over in a fifth of a second or so: a card or a panel rises out of a slightly
/// smaller, faint self when it is shown; a button gives under the finger and springs back; a line fades in. Every
/// button of the interface gives when pressed (<see cref="HookButtons"/>); the frame's own plaques and slots call
/// <see cref="Press"/> themselves. A new motion on a control takes over from the one before.
/// </summary>
public static class Juice
{
    /// <summary>A card or a panel shown: it grows out of <paramref name="from"/> of its size, out of nothing.</summary>
    public static void Appear(Control c, float from = .94f, double time = .2)
    {
        var tw = Fresh(c);
        c.Modulate = new Color(c.Modulate, 0);
        Scale(c, from);
        tw.SetParallel();
        tw.TweenMethod(Callable.From<float>(k => Scale(c, k)), from, 1f, time).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tw.TweenProperty(c, "modulate:a", 1f, time * .7);
    }

    /// <summary>A line or a page put in place of another: it fades in where it stands.</summary>
    public static void FadeIn(Control c, double time = .25)
    {
        var tw = Fresh(c);
        c.Modulate = new Color(c.Modulate, 0);
        tw.TweenProperty(c, "modulate:a", 1f, time).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
    }

    /// <summary>A button pressed: it gives a little and springs back.</summary>
    public static void Press(Control c)
    {
        var tw = Fresh(c);
        tw.TweenMethod(Callable.From<float>(k => Scale(c, k)), 1f, .92f, .05).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tw.TweenMethod(Callable.From<float>(k => Scale(c, k)), .92f, 1f, .16).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    /// <summary>Every button in the tree, now and to come, gives when it is pressed.</summary>
    public static void HookButtons(SceneTree tree)
    {
        // the tree outlives a new game or a game read back: it is hooked once
        if (_hooked == tree) return;
        _hooked = tree;
        tree.NodeAdded += Hook;
        Walk(tree.Root);
    }

    static SceneTree _hooked;

    static void Walk(Node n)
    {
        Hook(n);
        foreach (var child in n.GetChildren()) Walk(child);
    }

    static void Hook(Node n)
    {
        if (n is not BaseButton b || b.HasMeta("juice_hooked")) return;
        b.SetMeta("juice_hooked", true);
        b.ButtonDown += () => Press(b);
    }

    static void Scale(Control c, float k)
    {
        c.PivotOffset = c.Size / 2;
        c.Scale = new Vector2(k, k);
    }

    /// <summary>The control's motion before is stopped where it was and the control set whole again.</summary>
    static Tween Fresh(Control c)
    {
        if (c.HasMeta("juice") && c.GetMeta("juice").As<Tween>() is { } old && old.IsValid()) old.Kill();
        c.Scale = Vector2.One;
        c.Modulate = new Color(c.Modulate, 1);
        var tw = c.CreateTween();
        c.SetMeta("juice", tw);
        return tw;
    }
}
