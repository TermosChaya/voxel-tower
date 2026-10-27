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
    /// <summary>The mine of the big map the self-test holds, across the scenes; the map's clock as the battle for it began.</summary>
    static MapCell _holdMine;
    static ClockTime _holdClock;

    /// <summary>
    /// A point held (Campaign.Hold.cs, Main.Defence.cs), after the big map's own test: the hero gone into the mine he
    /// flagged, two workers with him; «Оставить гарнизон» for the archers, a swordsman and the workers, a wall raised
    /// round them, the hero out to the map with the rest; the valley left, it is ours with them as its garrison and its
    /// works measured, and the map shows it held. A party of the horde sent at it stops the map, and the game goes
    /// there: the party comes in as a raid by the side it came, the map's clock standing while the battle is fought;
    /// its raiders beaten, the point is still ours and the map runs on. Returns false: the scene begins again for it.
    /// </summary>
    bool SelfTestHold(Func<int> fails, MapParty hero, MapCell mine)
    {
        _holdMine = mine;
        hero.Path?.Clear(); hero.Trail = null;
        hero.Q = mine.Q; hero.R = mine.R;
        Again("hold", fails);
        EnterCell(hero);
        return false;
    }

    async Task<bool> SelfTestHoldGo(string stage, Action<string, bool, string> check, Func<int> fails)
    {
        async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        void Run(double seconds) { for (double t = 0; t < seconds; t += .5) { Advance(.5); CampaignTick(.5); } }
        await Frames(4);
        var c = Atlas;
        var mine = c.Cell(_holdMine.Col, _holdMine.Row);
        switch (stage)
        {
            case "hold":
            {
                check("a valley gone into from the open map is drawn: the map's switch for the valley's drawing is not left off as the scene begins again",
                    !GetTree().Root.Disable3D && Rig.Cam.IsCurrent() && !AtlasOpen, $"3D off {GetTree().Root.Disable3D}, the valley's camera current {Rig.Cam.IsCurrent()}, the map open {AtlasOpen}");
                var ours = Sim.Units.FindAll(u => !u.Dead && u.Team == Team.Player);
                var captain = ours.Find(u => u.T == Kinds.Captain);
                var archers = ours.FindAll(u => u.T == Kinds.Archer);
                var swords = ours.FindAll(u => u.T == Kinds.Swordsman);
                // two workers come with him; the garrison held on level ground (the wall round it not lost in a slope)
                var (lx, lz) = LevelNearKeep();
                float cx = lx, cz = lz;
                if (lx < 0) { cx = cz = 0; foreach (var u in ours) { cx += u.X / ours.Count; cz += u.Z / ours.Count; } }
                int mid = Sim.NearestStand((int)cx, (int)cz, 2);
                var hands = new List<Unit> { Sim.Spawn(Kinds.Worker, mid), Sim.Spawn(Kinds.Worker, mid) };
                // the garrison chosen: the archers, a swordsman, the workers; drawn together at the middle
                var stay = new List<Unit>(archers);
                if (swords.Count > 0) stay.Add(swords[0]);
                stay.AddRange(hands);
                foreach (var u in stay) { u.Cell = Sim.NearestStand((int)cx + Sim.Rng.Next(-6, 7), (int)cz + Sim.Rng.Next(-6, 7), u.T.Move.Height); (u.X, u.Y, u.Z) = Sim.Nav.Feet(u.Cell); u.Path = null; }
                Selected.Clear(); foreach (var u in stay) Selected.Add(u);
                Hud.OnSelectionChanged();
                Command.GarrisonSelected();
                bool marked = stay.All(u => u.Garrison) && !ours.Exists(u => !stay.Contains(u) && u.Garrison);
                // a wall raised round them, 5 m off, 3 m high
                int top = Shapes.LevelTop(World, (int)cx, (int)cz, 24);
                Sim.InstantLabour = true;
                Sim.DesignateBuild(Shapes.Box(World, Site, (int)cx - 40, (int)cz - 40, (int)cx + 40, (int)cz + 40, new Stroke { Mat = Mat.Stone, Width = 4, Height = 24, Level = true, Hollow = true }, top));
                Sim.CompleteAll();
                Sim.InstantLabour = false;
                Run(1);
                // the map's key opens the big map over the valley, its pane offering the valley's commander out, and shuts it
                // again; his card offers it too; {EnterCell} with all of them chosen takes the hero out with the rest, and
                // those left to hold it stay
                static InputEventKey Key(Act a) => new() { PhysicalKeycode = Bindings.KeyOf(a), Keycode = Bindings.KeyOf(a), Pressed = true };
                Selected.Clear(); Hud.OnSelectionChanged();
                Command._UnhandledInput(Key(Act.Board));
                await Frames(3);
                bool opened = AtlasOpen && Hud.MapPane.IsOpen, offered = Hud.MapPane.LeaveShown;
                Command._UnhandledInput(Key(Act.Board));
                await Frames(3);
                bool shut = !AtlasOpen;
                if (captain != null) Hud.HeroPane.Show(captain);
                await Frames(2);
                bool card = Hud.HeroPane.IsOpen && Hud.HeroPane.OutShown;
                Hud.HeroPane.Close();
                check("in a valley of the big map its key opens the map, which offers the commander out, and shuts it; his card offers it as well",
                    opened && offered && shut && card, $"opened {opened} offering {offered}, shut {shut}, the card's button {card}");
                Selected.Clear(); foreach (var u in ours) Selected.Add(u);
                Hud.OnSelectionChanged();
                Command._UnhandledInput(Key(Act.EnterCell));
                await Frames(3);
                bool went = AtlasOpen && captain != null && captain.Dead;
                bool stayed = stay.All(u => !u.Dead) && went;
                check("in a valley of the map «Оставить гарнизон» marks the chosen; {EnterCell} with all of them chosen takes the hero out to the map, those marked stay",
                    marked && went && stayed, $"marked {marked}, out {went}, the garrison stayed {stayed} ({stay.Count(u => !u.Dead)} of {stay.Count}), the hero gone {captain?.Dead}");
                // the valley left: home, the scene begun again
                Again("hold-check", fails);
                GoToValley(c.Home);
                return false;
            }
            case "hold-check":
            {
                var look = new CampaignHexes(c).Look(mine.Col, mine.Row);
                check("left with them in it, the mine's valley is ours: its garrison those left, its works measured, and the map shows it held behind works",
                    mine.Owner == Holder.Ours && mine.Sum.Keep.Count == 5 && c.Garrisoned(mine) && mine.Sum.Walls >= 2 && (look.Variant & 4) != 0 && (look.Variant & 8) != 0,
                    $"owner {mine.Owner}, garrison {mine.Sum.Keep.Count}, works {mine.Sum.Walls:0.00} «{mine.Sum.Works.Words()}», the map's marks {look.Variant}");
                // a party of the horde sent at it, the game letting it stop the map
                var near = Enumerable.Range(0, 6).Select(s => c.Neighbour(mine, s)).First(n => n != null && n.Passable && n.Owner == Holder.None && n.Sum.Keep.Count == 0 && !n.Home);
                var p = new MapParty { Id = 9900, Owner = Holder.Horde, Party = new Party(), Q = near.Q, R = near.R, CameQ = near.Q, CameR = near.R, Aim = PartyAim.Strike };
                foreach (var t in new[] { Kinds.Goblin, Kinds.Goblin, Kinds.Goblin, Kinds.Orc }) p.Party.Members.Add(new Traveller { Kind = t, Hp = t.Hp, MaxHp = t.Hp });
                c.Parties.Add(p);
                c.Send(p, mine);
                _selfDefence = true;
                // (the scene begins again on the mine as the party reaches it: the next stage is set before)
                Again("defend", fails);
                for (int i = 0; i < 1200 && _defending == null; i++)
                {
                    Run(1);
                    if (c.Frozen != null) _holdClock = c.Clock;
                }
                check("a party of the horde reaching a point of ours with a garrison stops the map and the game goes to its valley",
                    _defending != null && c.Frozen == mine, $"going {_defending != null}, frozen at the mine {c.Frozen == mine}, day {c.Clock.Day}");
                if (_defending == null) { _boardStage = null; _selfDefence = false; return true; }
                return false;
            }
            case "defend":
            {
                bool raid = Sim.Raids.Active && Atlas.Here == mine && c.Frozen == mine;
                int garrison = Sim.Units.Count(u => !u.Dead && u.Team == Team.Player && Fortification.Fighter(u));
                var walkers = c.Parties.Where(x => x.Moving).Select(x => (x, x.Q, x.R, x.Progress)).ToList();
                Run(4);
                bool stood = c.Clock.Equals(_holdClock) && walkers.All(w => w.x.Q == w.Q && w.x.R == w.R && w.x.Progress == w.Progress);
                check("the game opens the point's valley: the party comes in as a raid, its garrison there, and the map stands while the battle is fought",
                    raid && garrison >= 3 && stood, $"raid {Sim.Raids.Active} here {Atlas.Here == mine}, frozen {c.Frozen == mine}, our fighters {garrison}, the map stood {stood} ({c.Clock.Day}/{c.Clock.At:0} as {_holdClock.Day}/{_holdClock.At:0})");
                // the raiders struck down as they come: the battle over, held
                for (int i = 0; i < 240 && (Sim.Raids.Active || c.Frozen != null); i++)
                {
                    foreach (var u in Sim.Units.ToArray()) if (!u.Dead && u.Team == Team.Horde && !u.Envoy) Sim.Hurt(u, null, 1e6f);
                    Run(1);
                }
                await Frames(2);
                check("the battle over, the point is still ours, the map runs on from where it stood, and the valley's day is the map's",
                    c.Frozen == null && mine.Owner == Holder.Ours && Sim.Clock.Now.Day == c.Clock.Day && Math.Abs(Sim.Clock.Now.At - c.Clock.At) < 5,
                    $"frozen {c.Frozen != null}, owner {mine.Owner}, the valley's {Sim.Clock.Now.Day}/{Sim.Clock.Now.At:0}, the map's {c.Clock.Day}/{c.Clock.At:0}");
                _selfDefence = false;
                CloseAtlas();
                string folder = Saves.CampaignDir(CampSlot);
                LeaveCampaign();
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
                return true;
            }
        }
        return true;
    }
}
