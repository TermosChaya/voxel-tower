using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// The big world map's view (HexMapView.cs) on a made-up world of the largest size, 144 × 144 hexes: it opens over the
    /// valley in a world of its own, the land about the eye made in voxels piece by piece and put in, the far view under
    /// it; a drag moves the eye with the ground under the pointer, and as it goes across the map the pieces left behind are
    /// let go and those ahead made, no frame held long; a click on a hex's middle tells that hex; a click on the small map
    /// takes the eye there; the fog lifts from a hex newly seen; the way of a hero laid out in arrows, green today.
    /// </summary>
    async Task SelfTestHexView(Action<string, bool, string> check)
    {
        async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        async Task<bool> Settle(HexMapView view, int most = 600)
        {
            for (int i = 0; i < most; i++) { if (view.Settled) return true; await Frames(1); }
            return false;
        }
        var src = new DemoHexes(144, 144, 11);
        var lay = new HexMapLayout(144, 144);
        var (hx, hz) = lay.Center(src.Home.Col, src.Home.Row);
        var v = OpenWorld(src, hx, hz);
        v.HoverAt(new Vector2(-9999, -9999));
        v.Snap(hx, hz + 30, 34);
        await Frames(3);
        var sw = Stopwatch.StartNew();
        bool settled = await Settle(v);
        double settleMs = sw.Elapsed.TotalMilliseconds;
        var far = lay.Center(src.Towns[0].Col, src.Towns[0].Row);
        check("the big world map (144 × 144 hexes) opens in a world of its own over the valley, the valley no longer drawn; the land about the eye made in voxels and put in",
            v.IsOpen && GetTree().Root.Disable3D && v.Cam.IsCurrent() && settled && v.PiecesShown >= 4 && v.Seen(src.Home.Col, src.Home.Row) && !v.Seen(src.Towns[0].Col, src.Towns[0].Row),
            $"{lay.SX}×{lay.SZ} voxels; {v.PiecesShown} pieces drawn after {settleMs:0} ms (a piece {v.PieceMs:0} ms on a worker); our citadel seen {v.Seen(src.Home.Col, src.Home.Row)}, the horde's town {v.Seen(src.Towns[0].Col, src.Towns[0].Row)}");

        // a drag moves the eye the way the ground is dragged; across the map the pieces behind let go, those ahead made, no long frame
        var mid = GetViewport().GetVisibleRect().Size / 2;
        var before = v.Look;
        v._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = mid });
        for (int i = 1; i <= 10; i++) v._UnhandledInput(new InputEventMouseMotion { Position = mid + new Vector2(-30 * i, 0), ButtonMask = MouseButtonMask.Right });
        v._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = mid + new Vector2(-300, 0) });
        await Frames(2);
        var dragged = v.Look;
        int shownAtHome = v.PiecesShown;
        v.WorstFrameMs = v.WorstPutMs = v.WorstWorkMs = 0;
        // across the map to the horde's town, a few hexes a frame
        var from = v.Look;
        for (int i = 1; i <= 90; i++)
        {
            var p = from.Lerp(new Vector2(far.X, far.Z + 30), i / 90f);
            v.Snap(p.X, p.Y, 34);
            await Frames(1);
        }
        bool settledFar = await Settle(v);
        double worst = v.WorstFrameMs, worstPut = v.WorstPutMs, worstWork = v.WorstWorkMs;
        check("dragging the map moves the eye with the ground; going across the map the pieces ahead are made and those behind let go, no frame held long",
            dragged.X > before.X + 40 && MathF.Abs(dragged.Y - before.Y) < 10 && settledFar && v.PiecesShown < 80 && worst < 120,
            $"the eye {before.X:0} → {dragged.X:0} across, {before.Y:0} → {dragged.Y:0} along; pieces {shownAtHome} at home, {v.PiecesShown} by the horde's town; the slowest frame {worst:0} ms, of it the map's own work {worstWork:0} ms and putting meshes in {worstPut:0} ms");

        // a click on a hex's middle tells that hex
        var aim = (src.Towns[0].Col + 2, src.Towns[0].Row + 1);
        var (ax, az) = lay.Center(aim.Item1, aim.Item2);
        v.Snap(ax, az + 20, 30);
        await Frames(3);
        var screen = v.Cam.UnprojectPosition(v.HexToScene(aim.Item1, aim.Item2));
        (int, int) heard = (-1, -1);
        void Heard(int c, int r, MouseButton b) => heard = (c, r);
        v.ClickedHex += Heard;
        v._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = screen });
        v._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = screen });
        v.ClickedHex -= Heard;
        check("a click on a hex's middle tells that hex", heard == aim, $"clicked {aim}, heard {heard}");

        // a click on the small map takes the eye there
        var (tx, tz) = lay.Center(20, 20);
        var on = v.MiniPoint(tx, tz);
        var ev = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = on, GlobalPosition = on };
        GetViewport().PushInput(ev);
        await Frames(40);
        var went = v.Look;
        check("a click on the small map in the corner takes the eye there", new Vector2(went.X - tx, went.Y - tz).Length() < 60,
            $"the eye at {went.X:0},{went.Y:0}, wanted {tx:0},{tz:0}");

        // the fog lifts from a hex newly seen, smoothly
        var dark = (src.Towns[0].Col, src.Towns[0].Row);
        bool wasSeen = v.Seen(dark.Col, dark.Row);
        src.Reveal(dark.Col, dark.Row, 2);
        await Frames(3);
        bool seenNow = v.Seen(dark.Col, dark.Row);
        await Frames(40);
        check("the fog lifts from a hex newly seen", !wasSeen && seenNow, $"seen before {wasSeen}, after {seenNow}");

        // a hero's way laid out: arrows, green as far as today
        var steps = new List<(int, int, int)>();
        int c0 = src.Home.Col, r0 = src.Home.Row;
        var way = Way(src, (c0, r0), (c0 + 6, r0 - 4));
        for (int i = 0; i < way.Count; i++) steps.Add((way[i].Item1, way[i].Item2, i / 4));
        v.ShowPath(c0, r0, steps);
        check("a hero's way laid out in arrows, green as far as he gets today and red beyond", v.ArrowsShown == steps.Count && v.ArrowsToday == Math.Min(4, steps.Count) && steps.Count > 4,
            $"{steps.Count} steps, {v.ArrowsShown} arrows, {v.ArrowsToday} green");
        v.HidePath();
        v.Close();
        await Frames(2);
        check("closed, the valley is drawn again", !GetTree().Root.Disable3D && !v.IsOpen, "");
    }
}
