using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using VoxelTower.Core;
using Holder = VoxelTower.Core.Owner;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>Where the self-test of the campaign is, across the scene begun again for each valley; and the failures so far.</summary>
    static string _boardStage;
    static int _boardFails;
    const string BoardSlot = "campaign2";

    /// <summary>
    /// The campaign played through its input, the scene begun again for each valley as in play: a new campaign opens on
    /// its first valley with two commanders; the board opens with its key, nineteen cells, twelve in the fog; a commander
    /// and three men chosen and sent to the ore valley go out over the edge onto the map and walk there, and the card asks
    /// to go in; gone in, they come into the ore valley by the side facing the first one; its outpost marked and built,
    /// the valley is ours. A party put at the horde's lair goes in, its garrison falls, the lair is taken and the campaign
    /// won; ours sent out of it are on the map, and the game goes back to the first valley with the board open. The
    /// campaign written and read back is the same. Returns true when the scene is begun again to go on.
    /// </summary>
    async Task<bool> SelfTestBoard(Action<string, bool, string> check, Func<int> fails)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        string dir = Saves.CampaignDir(BoardSlot);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Atlas = Campaign.New(Seed);
        CampSlot = BoardSlot;
        Atlas.Write(dir);
        Again("home", fails);
        GetTree().ReloadCurrentScene();
        return true;
    }

    /// <summary>The scene begun again for the campaign's next valley, the failures carried over.</summary>
    bool Again(string stage, Func<int> fails)
    {
        _boardStage = stage;
        _boardFails = fails();
        GD.Print($"[selftest] the campaign goes on: {stage}");
        return true;
    }

    /// <summary>The campaign's self-test on the valley the scene was begun on; false when it begins again elsewhere, true when done.</summary>
    async Task<bool> SelfTestBoardGo(Action<string, bool, string> check, Func<int> fails)
    {
        async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        // the valley and the map run together, as a frame of play runs them
        void Run(double seconds) { for (double t = 0; t < seconds; t += .5) { Advance(.5); CampaignTick(.5); } }
        string stage = _boardStage;
        _boardStage = null;
        var c = Atlas;
        await Frames(4);
        var ore = c.Cells.Find(x => x.Kind == ValleyKind.Ore && Campaign.Distance(0, 0, x.Q, x.R) == 1);
        var lair = c.Cells.Find(x => x.Holding == Holding.HordeTown);
        Sim.Waves.Countdown = 1e9;
        switch (stage)
        {
            case "home":
            {
                check("a new campaign opens on its first valley: a citadel, the map's raids, the household and two commanders, the board's medallion",
                    Sim.Campaign && c.Here.Home && Site.HasKeep && Sim.Source is MapRaids && Sim.Count(Kinds.Captain) == Campaign.StartCommanders && Sim.Count(Kinds.Worker) == 4 && Hud.Board != null,
                    $"campaign {Sim.Campaign}, home {c.Here.Home}, source {Sim.Source.Key}, commanders {Sim.Count(Kinds.Captain)}, board {Hud.Board != null}");
                var key = new InputEventKey { PhysicalKeycode = Bindings.KeyOf(Act.Board), Keycode = Bindings.KeyOf(Act.Board), Pressed = true };
                Command._UnhandledInput(key);
                await Frames(3);
                check("the board opens with its key: nineteen cells drawn, twelve of them in the fog (only round the first valley seen)",
                    Hud.Board.IsOpen && Hud.Board.Drawn == 19 && Hud.Board.Fogged == 12, $"open {Hud.Board.IsOpen}, drawn {Hud.Board.Drawn}, in the fog {Hud.Board.Fogged}");
                // a commander and three men chosen, sent to the ore valley
                Selected.Clear();
                var captain = Sim.Units.Find(u => !u.Dead && u.T == Kinds.Captain);
                Selected.Add(captain);
                foreach (var u in Sim.Units) if (!u.Dead && u.Team == Team.Player && u.T is { } t && (t == Kinds.Swordsman || t == Kinds.Archer) && Selected.Count < 3) Selected.Add(u);
                Selected.Add(Sim.Units.Find(u => !u.Dead && u.T == Kinds.Worker));
                Hud.Board.Choose(ore);
                string why = Hud.Board.SendHere();
                int side = Campaign.SideToward(c.Here, ore.Q, ore.R);
                int going = Sim.Units.Count(u => !u.Dead && u.LeaveBy == side);
                Hud.Board.Close();
                MapParty party = null;
                for (int i = 0; i < 300 && party == null; i++) { Run(1); party = c.Parties.Find(p => p.Owner == Holder.Ours); }
                check("a commander and three men sent to the ore valley go to the edge, over it and onto the map, walking there",
                    why == null && going == 4 && party != null && party.Moving && party.Path[^1] == ore && party.Party.Members.Count == 4 && party.Party.Commander != null,
                    $"refused «{why}», {going} going, on the map {party != null}{(party != null ? $", {party.Party.Members.Count} of them walking {party.Moving} to {party.Path.LastOrDefault()?.Kind}" : "")}");
                if (party == null) return true;
                for (int i = 0; i < 400 && party.Moving; i++) CampaignTick(5);
                await Frames(4);
                check("walked there, it stands in the ore valley and the card asks to go in",
                    !party.Moving && c.CellOf(party) == ore && Hud.Board.CardShown && Hud.Board.CardParty == party,
                    $"walking {party.Moving}, at {c.CellOf(party).Kind}, card {Hud.Board.CardShown}");
                Again("ore", fails);
                Hud.Board.EnterWith(party);
                return false;
            }
            case "ore":
            {
                int side = Campaign.SideToward(ore, 0, 0);
                var ours = Sim.Units.FindAll(u => !u.Dead && u.Team == Team.Player);
                var hex = Site.Hex;
                float far = ours.Count == 0 ? 0 : ours.Max(u => hex.FromSide(side, u.X, u.Z).d) / VoxelWorld.PerMetre;
                bool said = Hud.Lines.Any(l => l.text.StartsWith("Отряд пришёл " + Sim.EdgeName(side)));
                check("gone in, the scene begins on the ore valley: its outpost's place, the four come in by the side facing the first valley, and said; the first valley written to the folder",
                    c.Here == ore && Site.Holding == Holding.Outpost && Sim.Campaign && ours.Count == 4 && far < 45 && said && c.Home.Visited && File.Exists(Path.Combine(Saves.CampaignDir(CampSlot), c.Home.File)),
                    $"here {c.Here.Kind}, holding {Site.Holding}, {ours.Count} of ours, the farthest {far:0} m from the side {side}, said {said}, the first valley written {c.Home.Visited}");
                Run(12);
                bool stillNobodys = ore.Owner == Holder.None;
                int built0 = Sim.Orders.BuildTotal;
                string why = RaiseOutpost();
                int marked = Sim.Orders.BuildTotal - built0;
                // the workers' labour cut short: the tower laid at once
                Sim.InstantLabour = true;
                for (int i = 0; i < 4; i++) { Sim.CompleteAll(); Run(1); }
                Sim.InstantLabour = false;
                Run(6);
                check("standing in it takes nothing; its outpost marked for the workers beside the stone and built, the valley is ours, and the chronicle says so",
                    stillNobodys && why == null && ore.Outpost && marked > 500 && ore.Owner == Holder.Ours && Hud.Lines.Any(l => l.text.StartsWith("Застава в рудной долине готова")),
                    $"nobody's after standing {stillNobodys}; refused «{why}», marked {marked}; owner {ore.Owner}");
                // a strong party put at the horde's town, as if it had walked there
                var p = new MapParty { Q = lair.Q, R = lair.R, Owner = Holder.Ours, Party = new Party() };
                var before = c.Neighbour(lair, Campaign.SideToward(lair, 0, 0));
                p.CameQ = before.Q; p.CameR = before.R;
                foreach (var k in new[] { Kinds.Captain, Kinds.Swordsman, Kinds.Swordsman, Kinds.Spearman, Kinds.Spearman, Kinds.Archer, Kinds.Archer })
                    p.Party.Members.Add(new Traveller { Kind = k, Look = 3, Hp = k.Hp, MaxHp = k.Hp });
                c.Parties.Add(p);
                Again("lair", fails);
                EnterCell(p);
                return false;
            }
            case "lair":
            {
                int garrison = Sim.Units.Count(u => !u.Dead && u.Team == Team.Horde);
                // (those who only come raiding — miners, sappers, thieves — and engines keep no town: the map counts them, the town has none of them)
                int keeps = lair.Sum.Keep.Count(m => !Kinds.IsEngine(m.Kind) && m.Kind != Kinds.Miner && m.Kind != Kinds.Sapper && m.Kind != Kinds.Thief);
                int ours = Sim.Units.Count(u => !u.Dead && u.Team == Team.Player);
                check("a party goes into the horde's lair: its palisade and huts, its garrison of the map at its posts, ours come in",
                    c.Here == lair && Site.Holding == Holding.HordeTown && garrison == keeps && garrison > 5 && ours == 7,
                    $"here {c.Here.Kind}, holding {Site.Holding}, garrison {garrison} of {keeps} (the map's {lair.Sum.Keep.Count}), ours {ours}");
                // the fight cut short: the garrison falls
                foreach (var u in Sim.Units.ToArray()) if (!u.Dead && u.Team == Team.Horde) Sim.Hurt(u, null, 1e6f);
                Run(6);
                check("its garrison fallen, the lair is taken: nobody's now, the campaign won, and the chronicle and the card say so",
                    lair.Owner == Holder.None && c.Won && _campaignOver && Hud.Lines.Any(l => l.text.Contains("Логово орды взято")),
                    $"owner {lair.Owner}, won {c.Won}, over {_campaignOver}");
                // (the victory put by, to go on with the test)
                c.Won = false; _campaignOver = false;
                // all of ours sent out toward the first valley
                Selected.Clear();
                foreach (var u in Sim.Units) if (!u.Dead && u.Team == Team.Player) Selected.Add(u);
                string why = SendOut(c.Home);
                for (int i = 0; i < 300 && Sim.Units.Exists(u => !u.Dead && u.Team == Team.Player); i++) Run(1);
                var back = c.Parties.Find(x => x.Owner == Holder.Ours && x.Party.Members.Count == 7);
                check("all of ours sent out of it are on the map, walking to the first valley, the town left empty of ours",
                    why == null && back != null && back.Moving && back.Path[^1] == c.Home && !Sim.Units.Exists(u => !u.Dead && u.Team == Team.Player),
                    $"refused «{why}», on the map {back != null}, walking {back?.Moving}");
                Again("back", fails);
                GoHome();
                return false;
            }
            case "back":
            {
                check("the last of ours gone out, the game goes back to the first valley read from its file, the board open on it",
                    c.Here.Home && _loaded && Hud.Board.IsOpen && lair.Owner == Holder.None && ore.Owner == Holder.Ours && ore.Outpost,
                    $"home {c.Here.Home}, read back {_loaded}, board {Hud.Board.IsOpen}, the town {lair.Owner}, the ore valley {ore.Owner}");
                c.Gold = Sim.Stock.Gold = 777;
                bool wrote = SaveCampaign();
                var read = Campaign.Read(Saves.CampaignDir(CampSlot));
                bool same = read.Cells.Count == c.Cells.Count && read.Cells.All(x => c.At(x.Q, x.R) is { } y && y.Owner == x.Owner && y.Outpost == x.Outpost && y.Visited == x.Visited && y.Sum.Keep.Count == x.Sum.Keep.Count)
                    && read.Parties.Count == c.Parties.Count && read.Parties.All(x => c.Parties.Exists(y => y.Q == x.Q && y.R == x.R && y.Owner == x.Owner && y.Path.Count == x.Path.Count && y.Party.Members.Count == x.Party.Members.Count))
                    && Math.Abs(read.Gold - 777) < .01 && read.Clock.Day == c.Clock.Day && read.HereQ == c.HereQ && read.HereR == c.HereR;
                check("the campaign written and read back is the same: the cells, whose they are, the outposts, the parties walking, the gold, the day", wrote && same,
                    $"written {wrote}, the same {same}; {read.Parties.Count} parties, gold {read.Gold:0}");
                Hud.Board.Close();
                string folder = Saves.CampaignDir(CampSlot);
                LeaveCampaign();
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
                return true;
            }
        }
        return true;
    }
}
