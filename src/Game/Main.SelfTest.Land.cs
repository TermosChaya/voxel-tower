using System;
using System.Threading.Tasks;
using Godot;
using VoxelTower.Core;
using Holder = VoxelTower.Core.Owner;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// The map of valleys as a land (AtlasView.cs), on a big map of its own: it opens over the valley in a world of its own
    /// and the valley is no longer drawn; the fog lies over what is not seen; a drag with the mouse moves the eye with the
    /// ground under the pointer, the wheel brings it nearer; a click on the ground tells where, in the cell clicked; the
    /// pointer over a lair says what it is; a cell taken is put on again with an outpost on it; closed, the valley is drawn
    /// again.
    /// </summary>
    async Task SelfTestLand(Action<string, bool, string> check)
    {
        async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        var c = Campaign.New(11, 3);
        var v = new AtlasView();
        AddChild(v);
        v.Open(c);
        v.HoverAt(new Vector2(-9999, -9999));
        await Frames(3);
        var a = v.Atlas;
        var near = c.Neighbour(c.Home, 0);
        var lair = c.Cells.Find(x => x.Holding == Holding.HordeTown);
        check("the map of valleys opens as a land of its own over the valley, the valley no longer drawn; what is not seen in the fog",
            v.IsOpen && GetTree().Root.Disable3D && v.Cam.IsCurrent() && a.Things.Count > 30 && v.Seen(near) && !v.Seen(lair),
            $"{a.SX}×{a.SZ} voxels, {a.Things.Count} things, made in {v.BuildMs:0} ms and meshed in {v.MeshMs:0} ms; the valley drawn {!GetTree().Root.Disable3D}; seen: next to the first valley {v.Seen(near)}, the lair {v.Seen(lair)}");

        // a drag moves the eye, the wheel brings it nearer
        var (hx, hz) = a.Center(c.Home);
        v.Snap(hx, hz, 14);
        await Frames(2);
        var mid = GetViewport().GetVisibleRect().Size / 2;
        var before = v.Cam.GlobalPosition;
        v._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = mid });
        for (int i = 1; i <= 10; i++) v._UnhandledInput(new InputEventMouseMotion { Position = mid + new Vector2(-20 * i, 0), ButtonMask = MouseButtonMask.Right });
        v._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = mid + new Vector2(-200, 0) });
        await Frames(2);
        var dragged = v.Cam.GlobalPosition;
        float z0 = v.Zoom;
        for (int i = 0; i < 4; i++) v._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true, Position = mid });
        await Frames(40);
        float z1 = v.Zoom;
        check("dragging the map moves the eye the way the ground is dragged, the wheel brings it nearer",
            dragged.X > before.X + .5f && MathF.Abs(dragged.Z - before.Z) < .3f && z1 < z0 - .1f,
            $"the eye {before.X:0.0} → {dragged.X:0.0} across, {before.Z:0.0} → {dragged.Z:0.0} along; nearness {z0:0.00} → {z1:0.00}");

        // a click on the ground: where, in the cell clicked
        c.RevealAll = true;
        var (nx, nz) = a.Center(near);
        v.Snap(nx, nz, 12);
        await Frames(3);
        Vector2? got = null; MouseButton by = MouseButton.None;
        void Heard(Vector2 at, MouseButton b) { got = at; by = b; }
        v.Clicked += Heard;
        var aim = v.Cam.UnprojectPosition(v.ToScene(nx + 3, nz + 2));
        v._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = aim });
        v._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = aim });
        v.Clicked -= Heard;
        var cell = got is { } g ? a.CellAt(g.X, g.Y) : null;
        check("a click on the map's ground tells where it was, in the cell clicked", cell == near && by == MouseButton.Left && got is { } p && MathF.Abs(p.X - (nx + 3)) < 3 && MathF.Abs(p.Y - (nz + 2)) < 3,
            got is { } q ? $"clicked at {q.X:0},{q.Y:0} (aimed {nx + 3:0},{nz + 2:0}), cell {cell?.Q},{cell?.R}, wanted {near.Q},{near.R}" : "no click heard");

        // the pointer over the lair says what it is
        var den = a.ThingIn(lair, AtlasThingKind.Lair);
        v.Snap(den.X, den.Z - 6, 9);
        await Frames(3);
        v.HoverAt(v.Cam.UnprojectPosition(v.ToScene(den.X, den.Z)));
        await Frames(3);
        string said = v.Describe(lair, den);
        check("the pointer over the horde's lair: the cell is pointed at and says it is the lair, and who holds it", v.Hovered == lair && said.Contains("Логово орды") && said.Contains("Во власти орды"),
            $"pointed at {v.Hovered?.Q},{v.Hovered?.R}, the lair {lair.Q},{lair.R}: «{said.Replace('\n', ' ')}»");

        // a cell taken: put on again with an outpost on it; closed, the valley drawn again
        near.Owner = Holder.Ours;
        v.Refresh(near);
        await Frames(2);
        bool outpost = a.ThingIn(near, AtlasThingKind.Outpost) != null;
        v.Close();
        await Frames(2);
        check("a cell taken shows an outpost of ours at once; the map closed, the valley is drawn again", outpost && !v.IsOpen && !GetTree().Root.Disable3D,
            $"outpost {outpost}, the valley drawn {!GetTree().Root.Disable3D}");
        v.QueueFree();
        await Frames(2);
    }
}
