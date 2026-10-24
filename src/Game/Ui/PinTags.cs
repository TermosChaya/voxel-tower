using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// Over each of ours pinned under what came down (Sim.Trauma.cs) a tag of how much lies on it and whether anyone is
/// digging it out yet, so that the one buried under a heap is not lost from sight: «Придавлен · 230 кг». None from
/// someone's eyes, nor over the menu.
/// </summary>
public partial class PinTags : CanvasLayer
{
    Main _m;
    Control _root;
    readonly Dictionary<Unit, Label> _tags = new();
    readonly List<Unit> _gone = new();

    public void Init(Main m)
    {
        _m = m;
        Layer = 2;
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);
    }

    public override void _Process(double delta)
    {
        var sim = _m.Sim;
        var cam = GetViewport().GetCamera3D();
        bool hidden = cam == null || _m.FP.On || _m.Hud.MenuOpen || _m.Hud.TitleOpen;
        foreach (var u in sim.Units)
        {
            if (u.Dead || u.PinKg <= 0 || u.Team != Team.Player || _tags.ContainsKey(u)) continue;
            var tag = Hud.Text("", Ornament.SerifBold, 14, new Color("f0c8b8"), outline: 6);
            tag.MouseFilter = Control.MouseFilterEnum.Ignore;
            _root.AddChild(tag);
            _tags[u] = tag;
        }
        _gone.Clear();
        foreach (var (u, tag) in _tags)
        {
            if (u.Dead || u.PinKg <= 0 || !sim.Units.Contains(u)) { tag.QueueFree(); _gone.Add(u); continue; }
            var at = new Vector3(_m.World.WX(u.X), _m.World.WY(u.Y + u.T.BodyH + 6), _m.World.WZ(u.Z));
            tag.Visible = !hidden && !cam.IsPositionBehind(at);
            if (!tag.Visible) continue;
            tag.Text = u.Digger is { Dead: false } ? $"Придавлен · {(int)u.PinKg} кг · откапывают" : $"Придавлен · {(int)u.PinKg} кг";
            tag.ResetSize();
            tag.Position = cam.UnprojectPosition(at) - new Vector2(tag.Size.X / 2, tag.Size.Y);
        }
        foreach (var u in _gone) _tags.Remove(u);
    }
}
