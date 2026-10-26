using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using VoxelTower.Core;
using Holder = VoxelTower.Core.Owner;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// A new campaign on a big map of the smallest size (S, 36 hexes a side), as the player starts it: it opens on the map,
    /// the hero by the citadel with a day's moves, the fog lifted round home only; the hero planned to a hex and sent with
    /// a second plan walks there in the map's time, his moves spent by the ground; with too few for the next step he
    /// waits, and «До рассвета» brings the dawn at once, his moves whole; walked onto a mine he flags it; walked onto a
    /// chest its card asks gold or experience. Returns false: the scene begins again for it.
    /// </summary>
    bool SelfTestWorld(Func<int> fails)
    {
        string dir = Saves.CampaignDir(BoardSlot);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Atlas = Campaign.NewWorld(Seed, 36);
        Atlas.AddHero();
        CampSlot = BoardSlot;
        Atlas.Write(dir);
        _atlasAtStart = true;
        Again("world", fails);
        GetTree().ReloadCurrentScene();
        return false;
    }

    async Task<bool> SelfTestWorldGo(Action<string, bool, string> check)
    {
        async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        void Run(double seconds) { for (double t = 0; t < seconds; t += .5) { Advance(.5); CampaignTick(.5); } }
        await Frames(4);
        var c = Atlas;
        var hero = c.Parties.Find(p => p.Owner == Holder.Ours && p.Party.Commander?.Hero != null);
        var home = c.Home;
        var far = c.Cells.OrderByDescending(x => Campaign.Distance(x.Q, x.R, home.Q, home.R)).First();
        check("a new S campaign opens on the big map: its hero by the citadel with a day's moves, the fog lifted round home only, the realm's resources up",
            AtlasOpen && c.Big && c.Width == 36 && hero != null && c.CellOf(hero) == home && hero.MovesLeft == hero.MovesMax && hero.MovesMax >= MapWalk.SlowMoves
            && c.Seen(home) && !c.Seen(far) && Hud.MapPane.IsOpen,
            $"the map {AtlasOpen}, {c.Width}×{c.Height}, hero {hero != null} moves {hero?.MovesLeft}/{hero?.MovesMax}, home seen {c.Seen(home)}, the far corner {c.Seen(far)}");
        if (hero == null) return true;

        // a hex of open ground a few steps off in our zone, nothing on it and nothing kept: planned, planned again, walked
        MapCell Open(MapCell from, int least, int most) => c.Cells
            .Where(x => x.Zone == home.Zone && x.Passable && x.Thing == null && x.Sum.Keep.Count == 0 && !x.Pass)
            .Where(x => Campaign.Distance(x.Q, x.R, from.Q, from.R) is var d && d >= least && d <= most)
            .OrderBy(x => Campaign.Distance(x.Q, x.R, from.Q, from.R)).FirstOrDefault();
        var to = Open(home, 3, 6);
        Hud.MapPane.Choose(hero);
        bool planned = Hud.MapPane.PlanTo(to.Col, to.Row);
        var steps = Hud.MapPane.Planned?.ToList() ?? new List<HexStep>();
        bool sent = Hud.MapPane.PlanTo(to.Col, to.Row);
        int m0 = hero.MovesLeft, cost = 0;
        var a = home;
        foreach (var s in steps) { var b = c.Cell(s.Col, s.Row); cost += MapWalk.Cost(a, b, MapPath.Side(a, b)); a = b; }
        double took = 0;
        for (int i = 0; i < 400 && hero.Moving; i++) { Run(1); took += 1; }
        check("a hex planned shows its way and its days, planned again the hero goes: he walks it in the map's time, his moves spent by the ground",
            planned && sent && steps.Count >= 3 && steps.All(s => s.Day == 0) && c.CellOf(hero) == to && m0 - hero.MovesLeft == cost && Math.Abs(took - MapWalk.Seconds(cost)) < 8,
            $"planned {planned} ({steps.Count} hexes), sent {sent}; came {c.CellOf(hero) == to}, moves {m0} → {hero.MovesLeft} (should spend {cost}), {took:0} s of {MapWalk.Seconds(cost):0}");

        // too few moves for the next step: he waits; «До рассвета» brings the dawn at once, his moves whole, and he goes on
        var next = Enumerable.Range(0, 6).Select(s => c.Neighbour(to, s)).First(n => n != null && n.Passable && n.Thing == null && n.Sum.Keep.Count == 0 && !n.Pass);
        int stepCost = MapWalk.Cost(to, next, MapPath.Side(to, next));
        hero.MovesLeft = stepCost - 1;
        Hud.MapPane.GoTo(next.Col, next.Row);
        Run(3);
        bool waits = hero.Tired && c.CellOf(hero) == to;
        int day = c.Clock.Day;
        ToDawn();
        await Frames(2);
        bool dawn = c.Clock.Day == day + 1 && Sim.Clock.Now.Day == day + 1 && hero.MovesLeft + hero.StepCost == hero.MovesMax;
        for (int i = 0; i < 100 && hero.Moving; i++) Run(1);
        check("with too few moves for the next step the hero waits; «До рассвета» brings the dawn on the map and in the valley at once, his moves whole, and he goes on",
            waits && dawn && c.CellOf(hero) == next,
            $"waited {waits}, day {day} → the map's {c.Clock.Day}, the valley's {Sim.Clock.Now.Day}, moves whole {dawn}, came on {c.CellOf(hero) == next}");

        // a mine of our zone, walked onto: ours
        var mine = c.Cells.Where(x => x.Zone == home.Zone && x.Thing is { Kind: ThingKind.Mine, Owner: Holder.None } && x.Sum.Keep.Count == 0)
            .OrderBy(x => Campaign.Distance(x.Q, x.R, hero.Q, hero.R)).FirstOrDefault();
        bool flagged = false;
        if (mine != null)
        {
            hero.MovesLeft = hero.MovesMax = 9999;
            Hud.MapPane.GoTo(mine.Col, mine.Row);
            for (int i = 0; i < 600 && hero.Moving; i++) Run(1);
            flagged = mine.Thing.Owner == Holder.Ours;
        }
        var (mines, daily) = c.Mines();
        check("a mine walked onto is ours, and the realm counts what it gives a day", flagged && mines >= 1 && daily[(int)mine.Thing.Res] > 0,
            $"{mine?.Thing.Name ?? "no mine"}: ours {flagged}, the realm's mines {mines}");

        // a chest set by the hero: walked onto, its card asks; the gold taken
        var spot = Open(c.CellOf(hero), 1, 1);
        spot.Thing = new MapThing { Kind = ThingKind.Chest, Amount = 1 };
        Hud.MapPane.GoTo(spot.Col, spot.Row);
        for (int i = 0; i < 100 && hero.Moving; i++) Run(1);
        await Frames(3);
        bool asked = c.Chests.Exists(x => x.cell == spot) && Hud.MapPane.CardShown;
        double gold0 = c.StockOf(Res.Gold);
        string said = c.OpenChest(spot, gold: true);
        double got = c.StockOf(Res.Gold) - gold0;
        check("a chest walked onto: its card asks gold or experience; the gold goes into the treasury, once",
            asked && Math.Abs(got - Campaign.ChestOf(spot.Thing).gold) < 1 && spot.Thing.Taken && c.OpenChest(spot, true) == null,
            $"asked {asked}, «{said}», +{got:0} gold");

        // written and read back: the hero where he stands, his moves, the mine ours, the chest taken
        bool wrote = SaveCampaign();
        var read = Campaign.Read(Saves.CampaignDir(CampSlot));
        var rh = read.Parties.Find(p => p.Owner == Holder.Ours && p.Party.Commander?.Hero != null);
        check("the big map written and read back keeps the hero, his moves, the mine's flag and the chest taken",
            wrote && read.Big && rh != null && rh.Q == hero.Q && rh.R == hero.R && rh.MovesLeft == hero.MovesLeft
            && read.Cell(mine.Col, mine.Row).Thing.Owner == Holder.Ours && read.Cell(spot.Col, spot.Row).Thing == null,
            $"written {wrote}; hero at {rh?.Q},{rh?.R} of {hero.Q},{hero.R}, moves {rh?.MovesLeft} of {hero.MovesLeft}");
        CloseAtlas();
        string folder = Saves.CampaignDir(CampSlot);
        LeaveCampaign();
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
        return true;
    }
}
