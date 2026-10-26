using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>The big world map's view, when one is open (the made-up world in pictures and the self-test).</summary>
    public HexMapView WorldMap { get; private set; }
    public HexMapFigures WorldFigures { get; private set; }

    /// <summary>The big world map's view made and opened over a source, its figures under it.</summary>
    HexMapView OpenWorld(IHexSource src, float x = -1, float z = -1)
    {
        if (WorldMap == null)
        {
            WorldMap = new HexMapView();
            AddChild(WorldMap);
            WorldFigures = new HexMapFigures();
            WorldMap.Figures.AddChild(WorldFigures);
            WorldFigures.Init(WorldMap);
        }
        WorldMap.Open(src, x, z);
        return WorldMap;
    }

    /// <summary>
    /// The big world map made up (--demo --pose=world --size=S|M|L|XL, the seed --seed=N): --look=0 the walking view about
    /// our citadel, the fog about what is seen; 1 a ridge of mountains with its pass and the gate in it; 2 a mine with the
    /// monster that keeps it, a chest by; 3 the horde's town in its burnt land; 4 the strategic view of a region; 5 the
    /// whole map from as far as it goes; 6 a hero of ours with his way laid out, green today and red beyond, an army of the
    /// horde on its way; 7 the walking view close; 8 the walking view drawn across the map, measured.
    /// </summary>
    void WorldPose()
    {
        string size = "M";
        foreach (var a in OS.GetCmdlineUserArgs()) if (a.StartsWith("--size=") && !int.TryParse(a["--size=".Length..], out _)) size = a["--size=".Length..];
        int side = HexMapLayout.SideOf(size);
        var src = new DemoHexes(side, side, (uint)_seed);
        var lay = new HexMapLayout(side, side);
        var (hx, hz) = lay.Center(src.Home.Col, src.Home.Row);
        if (_look is not (0 or 6 or 7)) src.RevealAll = true;
        var v = OpenWorld(src, hx, hz);
        v.HoverAt(new Vector2(-9999, -9999));
        // the valley's own frame is not the map's
        Hud.Visible = false;
        v.Without(_perfWithout);
        _shotFrames = Math.Max(_shotFrames, 110);
        (int c, int r) Find(Func<int, int, HexLook, bool> what, int fromC, int fromR)
        {
            (int, int) best = (-1, -1); int bd = int.MaxValue;
            for (int r = 2; r < side - 2; r++)
                for (int c = 2; c < side - 2; c++)
                    if (what(c, r, src.Look(c, r)))
                    {
                        int d = DemoHexes.Distance(c, r, fromC, fromR);
                        if (d < bd) { bd = d; best = (c, r); }
                    }
            return best;
        }
        void At((int c, int r) h, float dist, float dz = 0)
        {
            if (h.c < 0) h = src.Home;
            var (x, z) = lay.Center(h.c, h.r);
            v.Snap(x, z + dz, dist);
        }
        // our hero by the citadel
        var hero = new MapWalker { Col = src.Home.Col, Row = src.Home.Row, Owner = 1, Body = Bodies.HeroRider(0) };
        var near = Find((c, r, l) => l.Icon == HexIcon.None && l.Relief != HexRelief.Mountain && l.Ground is not (HexGround.Water or HexGround.DeepWater) && DemoHexes.Distance(c, r, src.Home.Col, src.Home.Row) == 1, src.Home.Col, src.Home.Row);
        if (near.c >= 0) (hero.Col, hero.Row) = near;
        WorldFigures.Walkers.Add(hero);
        WorldFigures.Chosen = hero;
        switch (_look)
        {
            case 1:
            {
                var pass = Find((c, r, l) => l.Icon == HexIcon.Pass, src.Home.Col, src.Home.Row);
                At(pass, 30, 30);
                break;
            }
            case 2:
            {
                var mine = Find((c, r, l) => l.Icon is >= HexIcon.MineMetal and <= HexIcon.MineGems && Beside(c, r, HexIcon.Monster), src.Home.Col, src.Home.Row);
                At(mine, 24, 20);
                // the pointer over the mine: the plate says what it is
                _poseLater = () => v.HoverAt(v.Cam.UnprojectPosition(v.HexToScene(mine.c, mine.r)));
                break;
            }
            case 3:
            {
                var town = src.Towns.Count > 0 ? src.Towns[0] : src.Home;
                At(town, 32, 26);
                var foe = new MapWalker { Col = town.Col, Row = town.Row + 2, Owner = 2, Body = Bodies.Plan(Kinds.Troll, 0), Count = 23 };
                WorldFigures.Walkers.Add(foe);
                break;
            }
            case 4: At(src.Home, 110, 60); break;
            case 5: { var (cx, cz) = (lay.SX / 2f, lay.SZ / 2f); v.Snap(cx, cz + lay.SZ * .12f, 1000); break; }
            case 6:
            {
                // a way to a mine three zones off: green as far as today, red beyond
                var to = Find((c, r, l) => l.Icon is >= HexIcon.MineLogs and <= HexIcon.MineGems && DemoHexes.Distance(c, r, src.Home.Col, src.Home.Row) >= 9, src.Home.Col, src.Home.Row);
                var way = Way(src, (hero.Col, hero.Row), to);
                var steps = new List<(int, int, int)>();
                for (int i = 0; i < way.Count; i++) steps.Add((way[i].Item1, way[i].Item2, i / 7));
                v.ShowPath(hero.Col, hero.Row, steps);
                if (steps.Count > 0) { var (mc, mr, _) = steps[Math.Min(steps.Count - 1, 6)]; var (mx, mz) = lay.Center(mc, mr); v.Snap(mx, mz + 30, 40); }
                // an army of the horde on its way, seen
                var foe = new MapWalker { Col = src.Home.Col + 3, Row = src.Home.Row - 3, Owner = 2, Body = Bodies.Plan(Kinds.Orc, 0), Count = 17 };
                foe.Steps.Add((foe.Col - 1, foe.Row + 1, 0));
                foe.Progress = .4;
                src.Reveal(foe.Col, foe.Row, 2);
                WorldFigures.Walkers.Add(foe);
                break;
            }
            case 7: At(src.Home, 14, 12); break;
            case 9:
            {
                // the walking view standing, measured
                At(src.Home, 34, 30);
                _shotFrames = Math.Max(_shotFrames, 400);
                _poseLater = () => { var (t, d, sh) = v.Drawn(); GD.Print($"[world map] {Engine.GetFramesPerSecond()} fps; {t} k triangles in {d} draw calls, {sh} k for shadows; {v.PiecesShown} pieces"); };
                break;
            }
            case 8:
            {
                // the walking view drawn east across the map at a hero's gallop and more, to measure it
                At(src.Home, 34, 30);
                v.AutoPan = new Vector2(220, -40);
                _shotFrames = Math.Max(_shotFrames, 600);
                _poseLater = () => GD.Print($"[world map] {Engine.GetFramesPerSecond()} fps; slowest frame {v.WorstFrameMs:0} ms, the map's own work {v.WorstWorkMs:0} ms, meshes put in {v.WorstPutMs:0} ms; {v.PiecesShown} pieces, a piece {v.PieceMs:0} ms on a worker");
                break;
            }
            default: At(src.Home, 34, 30); break;
        }
        bool Beside(int c, int r, HexIcon icon)
        {
            for (int s = 0; s < 6; s++)
            {
                var (nc, nr) = HexMapLayout.Neighbour(c, r, s);
                if (lay.OnField(nc, nr) && src.Look(nc, nr).Icon == icon) return true;
            }
            return false;
        }
    }

    /// <summary>A way over the made-up world between two hexes, over open land (not mountains or water), the fewest steps.</summary>
    static List<(int, int)> Way(DemoHexes src, (int c, int r) from, (int c, int r) to)
    {
        var came = new Dictionary<(int, int), (int, int)>();
        var q = new Queue<(int, int)>();
        q.Enqueue(from);
        came[from] = from;
        while (q.Count > 0)
        {
            var at = q.Dequeue();
            if (at == to) break;
            for (int s = 0; s < 6; s++)
            {
                var n = HexMapLayout.Neighbour(at.Item1, at.Item2, s);
                if ((uint)n.Col >= (uint)src.Width || (uint)n.Row >= (uint)src.Height || came.ContainsKey(n)) continue;
                var l = src.Look(n.Col, n.Row);
                if (l.Relief == HexRelief.Mountain || l.Ground is HexGround.Water or HexGround.DeepWater) continue;
                came[n] = at;
                q.Enqueue(n);
            }
        }
        var way = new List<(int, int)>();
        if (!came.ContainsKey(to)) return way;
        for (var at = to; at != from; at = came[at]) way.Add(at);
        way.Reverse();
        return way;
    }
}
