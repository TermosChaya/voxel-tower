using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;
using Holder = VoxelTower.Core.Owner;

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

    /// <summary>
    /// A new campaign on the big map of «Heroes» (--campaign --pose=bigmap --size=S|M|L|XL): --look=0 the start, our hero
    /// by the citadel, chosen; 1 his way planned to a mine (most of today's moves spent), green as far as today, red
    /// beyond, the days at the cross; 2 him on his way there, walking; 3 a mine flagged ours, him on it; 4 a chest come to, its card asking gold or
    /// experience; 5 a pass kept by a stack; 6 the horde's realm, its town in the burnt land; 7 the strategic view; 8 a road over a river on its bridge;
    /// 9 a mine held by a garrison of ours behind works, its card up (Campaign.Hold.cs); 10 a hero of the horde come up by
    /// ours under its banner; 11 the creatures of the wild about the citadel, the word over the nearest stack (what, how
    /// many, against our hero).
    /// </summary>
    void BigMapPose()
    {
        var c = Atlas;
        var hero = c.Parties.Find(p => p.Owner == Holder.Ours && p.Party.Commander != null);
        OpenAtlas();
        Hud.MapPane.Choose(hero, focus: true);
        var v = WorldMap;
        v.HoverAt(new Vector2(-9999, -9999));
        _shotFrames = Math.Max(_shotFrames, 130);
        MapCell Nearest(Func<MapCell, bool> what, MapCell from)
        {
            MapCell best = null; int bd = int.MaxValue;
            foreach (var h in c.Cells)
                if (what(h)) { int d = Campaign.Distance(h.Q, h.R, from.Q, from.R); if (d < bd) { bd = d; best = h; } }
            return best;
        }
        void Look(MapCell h, float dist, float dz = 26) { var (x, z) = v.Layout.Center(h.Col, h.Row); v.Snap(x, z + dz, dist); }
        var home = c.Home;
        Action hover = null;
        bool Mine(MapCell h) => h.Thing is { Kind: ThingKind.Mine, Owner: Holder.None } && h.Sum.Keep.Count == 0 && h.Passable;
        switch (_look)
        {
            case 1:
            case 2:
            {
                // a free mine two days off or so that a way leads to
                // (the hero has walked the better part of today already: what is left of the way is tomorrow's and after)
                if (_look == 1) hero.MovesLeft = 350;
                // (a thing of the map, a mine first, that a way of a day and more leads to; any open hex else)
                MapCell mine = null; int best = int.MaxValue;
                foreach (var h in c.Cells)
                {
                    if (!h.Passable || h.Sum.Keep.Count > 0) continue;
                    var way = MapWalk.Plan(c, hero, h.Col, h.Row);
                    if (way == null || way.Count < 6 || way[^1].Day < (_look == 1 ? 1 : 0) || way[^1].Day > 2) continue;
                    int score = (Mine(h) ? 0 : h.Thing != null ? 1000 : 2000) + way.Count;
                    if (score < best) { best = score; mine = h; }
                }
                mine ??= Nearest(Mine, home);
                // (the way laid over land seen: the land about it as if walked before)
                for (int s = 0; s < 3; s++) foreach (var h in c.Cells) if (Campaign.Distance(h.Q, h.R, (home.Q * (2 - s) + mine.Q * s) / 2, (home.R * (2 - s) + mine.R * s) / 2) <= 5) c.Reveal(h, 0);
                Hud.MapPane.PlanTo(mine.Col, mine.Row);
                if (_look == 2)
                {
                    Hud.MapPane.GoTo(mine.Col, mine.Row);
                    c.Advance(70);
                }
                var way2 = Hud.MapPane.Planned ?? hero.Steps;
                var mid = way2.Count > 0 ? way2[way2.Count / 3] : new HexStep(hero.Col, hero.Row, 0);
                var (mx, mz) = v.Layout.Center((hero.Col + mid.Col) / 2, (hero.Row + mid.Row) / 2);
                v.Snap(mx, mz + 26, 38);
                break;
            }
            case 3:
            {
                var mine = Nearest(Mine, home);
                hero.Q = mine.Q; hero.R = mine.R;
                c.Reveal(mine, 4);
                c.Visit(hero, mine);
                Look(mine, 24, 18);
                break;
            }
            case 4:
            {
                var chest = Nearest(h => h.Thing is { Kind: ThingKind.Chest, Taken: false } && h.Sum.Keep.Count == 0, home);
                hero.Q = chest.Q; hero.R = chest.R;
                c.Reveal(chest, 4);
                c.Visit(hero, chest);
                Hud.MapPane.Refresh();
                Look(chest, 24, 18);
                break;
            }
            case 5:
            {
                var pass = Nearest(h => h.Pass && h.Sum.Keep.Count > 0, home) ?? Nearest(h => h.Pass, home);
                c.Reveal(pass, 4);
                Look(pass, 30, 24);
                break;
            }
            case 6:
            {
                c.RevealAll = true;
                var town = Nearest(h => h.Thing is { Kind: ThingKind.HordeTown } && h.Owner == Holder.Horde, home);
                Look(town, 40, 30);
                break;
            }
            case 7:
                c.RevealAll = true;
                Look(home, 150, 90);
                break;
            case 9:
            {
                // a mine held: its garrison by it under our banner, the fort's mark over them
                var mine = Nearest(Mine, home);
                int key = Campaign.KeyOf(mine);
                var stay = new List<Traveller>();
                foreach (var t in new[] { Kinds.Archer, Kinds.Archer, Kinds.Spearman, Kinds.Spearman, Kinds.Swordsman, Kinds.Worker, Kinds.Worker })
                    stay.Add(new Traveller { Kind = t, Hp = t.Hp, MaxHp = t.Hp, Billet = key });
                mine.Sum.Works = new Fort(1, 3.3f, 2, 1, .6f, false, 0, 0, false);
                mine.Sum.Walls = mine.Sum.Works.Score;
                // (left there by the hero, as he would on the map)
                hero.Party.Members.AddRange(stay);
                c.Garrison(hero, mine, stay);
                c.Reveal(mine, 4);
                Hud.MapPane.Refresh();
                Look(mine, 20, 14);
                break;
            }
            case 8:
            {
                // a road crossing a river on its bridge, the land about it seen
                var ford = Nearest(h => h.Roads != 0 && h.Rivers != 0 && (h.Roads & h.Rivers) == 0 && h.Passable, home) ?? Nearest(h => h.Rivers != 0, home);
                c.Reveal(ford, 4);
                Look(ford, 22, 16);
                break;
            }
            case 10:
            {
                // a hero of the horde come up by ours, under its banner, his figure the warlord he is
                MapParty foe = null;
                foreach (var p in c.HeroesOf(Holder.Horde)) { foe = p; break; }
                if (foe == null) break;
                var by = c.Neighbour(c.Neighbour(home, 0) ?? home, 0) ?? home;
                foe.Q = by.Q; foe.R = by.R; foe.Path = null; foe.ThinkAt = double.MaxValue;
                c.Reveal(by, 3);
                var (fx, fz) = v.Layout.Center((home.Col + by.Col) / 2, (home.Row + by.Row) / 2);
                v.Snap(fx, fz + 14, 18);
                break;
            }
            case 11:
            {
                // (on open land, the figure not lost among the trees)
                var stack = Nearest(h => h.Thing is { Kind: ThingKind.Monster } && h.Sum.Keep.Count > 0 && h.Relief is HexRelief.Flat or HexRelief.Hills, home)
                    ?? Nearest(h => h.Thing is { Kind: ThingKind.Monster } && h.Sum.Keep.Count > 0, home);
                c.Reveal(stack, 6);
                Look(stack, 22, 16);
                hover = () => v.HoverAt(v.Cam.UnprojectPosition(v.HexToScene(stack.Col, stack.Row)));
                break;
            }
        }
        _poseLater = () => { hover?.Invoke(); var (t, d, sh) = v.Drawn(); GD.Print($"[world map] {Engine.GetFramesPerSecond()} fps; {t} k triangles in {d} draw calls, {sh} k for shadows; {v.PiecesShown} pieces; slowest frame {v.WorstFrameMs:0} ms"); };
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
