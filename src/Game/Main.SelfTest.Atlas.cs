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
    /// <summary>
    /// A new campaign as the player starts it, played on the map's land through its input: it opens on the map, its
    /// first hero by the first valley's citadel and his army with him; a click chooses him, a second his card; a click
    /// on the ore valley lays out his way and its hours, a click on its end sends him, and he walks there in the map's
    /// time; an army of the horde is drawn only where ours see it; standing in the ore valley, {EnterCell} takes him and
    /// his army into it by the side toward the first valley; «Вывести на карту» takes them out again at once, by the
    /// side nearest them; the campaign written and read back keeps him where he stands. Returns false: the scene begins
    /// again for it.
    /// </summary>
    bool SelfTestAtlas(Func<int> fails)
    {
        string dir = Saves.CampaignDir(BoardSlot);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Atlas = Campaign.New(Seed);
        Atlas.AddHero();
        CampSlot = BoardSlot;
        Atlas.Write(dir);
        _atlasAtStart = true;
        Again("atlas-home", fails);
        GetTree().ReloadCurrentScene();
        return false;
    }

    async Task<bool> SelfTestAtlasGo(string stage, Action<string, bool, string> check, Func<int> fails)
    {
        async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        void Run(double seconds) { for (double t = 0; t < seconds; t += .5) { Advance(.5); CampaignTick(.5); } }
        Vector2 Screen(float x, float z) => AtlasLand.Cam.UnprojectPosition(AtlasLand.ToScene(x, z));
        void Click(Vector2 at)
        {
            AtlasLand._UnhandledInput(new InputEventMouseButton { ButtonIndex = Bindings.First, Pressed = true, Position = at });
            AtlasLand._UnhandledInput(new InputEventMouseButton { ButtonIndex = Bindings.First, Pressed = false, Position = at });
        }
        static float Apart((float, float) a, (float, float) b) => MathF.Sqrt((a.Item1 - b.Item1) * (a.Item1 - b.Item1) + (a.Item2 - b.Item2) * (a.Item2 - b.Item2));
        var c = Atlas;
        var ore = c.Cells.Find(x => x.Kind == ValleyKind.Ore && Campaign.Distance(0, 0, x.Q, x.R) == 1);
        MapParty Hero() => c.Parties.Find(p => p.Owner == Holder.Ours && p.Party.Commander?.Hero != null);
        switch (stage)
        {
            case "atlas-home":
            {
                await Frames(4);
                var hero = Hero();
                check("a new campaign opens on the map: its first hero by the first valley's citadel, his figure on the land, his army with him; the valley keeps its workers and a commander",
                    AtlasOpen && hero != null && c.CellOf(hero) == c.Home && hero.Party.Members.Count == 1 + Campaign.HeroArmy.Length && Hud.MapPane.Figures.OursDrawn >= 1
                    && Sim.Count(Kinds.Worker) == 4 && Sim.Count(Kinds.Captain) == Campaign.StartCommanders - 1 && Sim.Count(Kinds.Archer) == 0 && Sim.Count(Kinds.Swordsman) == 0,
                    $"the map {AtlasOpen}, hero {hero != null} in {(hero == null ? "-" : c.CellOf(hero).Kind.ToString())} with {hero?.Party.Members.Count}, drawn {Hud.MapPane.Figures.OursDrawn}; the valley: workers {Sim.Count(Kinds.Worker)}, commanders {Sim.Count(Kinds.Captain)}, archers {Sim.Count(Kinds.Archer)}");
                if (hero == null) return true;
                // a click on him chooses him, a second his card
                Hud.MapPane.Choose(null);
                AtlasLand.Focus(hero.AtX, hero.AtZ);
                await Frames(3);
                Click(Screen(hero.AtX, hero.AtZ));
                await Frames(2);
                bool chosen = Hud.MapPane.Hero == hero;
                Click(Screen(hero.AtX, hero.AtZ));
                await Frames(2);
                bool card = Hud.HeroPane.IsOpen && Hud.HeroPane.Hero == hero.Party.Commander.Hero;
                Hud.HeroPane.Close();
                // a click on the ore valley lays out his way, a click on its end sends him
                var (ox, oz) = _land.Center(ore);
                AtlasLand.Focus((hero.AtX + ox) / 2, (hero.AtZ + oz) / 2);
                await Frames(3);
                Click(Screen(ox, oz));
                await Frames(2);
                bool shown = Hud.MapPane.Figures.Shown != null && !hero.Walking;
                Click(Screen(ox, oz));
                await Frames(2);
                bool going = hero.Walking;
                double said = c.TrailLeft(hero);
                check("a click on the hero chooses him, a second opens his card; a click on the land lays out his way and its hours, a click on its end sends him",
                    chosen && card && shown && going && said > 0, $"chosen {chosen}, card {card}, the way shown {shown}, going {going}, {said:0} s to walk");
                // he walks the land in the map's time
                var at0 = (hero.AtX, hero.AtZ);
                Run(40);
                bool moved = Apart(at0, (hero.AtX, hero.AtZ)) > 2 && hero.Walking;
                double walked = 40;
                while (hero.Walking && walked < 3000) { Run(5); walked += 5; }
                await Frames(3);
                check("he walks the land in the map's time, as long as was said, into the ore valley; standing there, «Войти в клетку» is offered",
                    moved && c.CellOf(hero) == ore && Math.Abs(walked - said) <= 8 && Hud.MapPane.EnterShown && Apart((hero.AtX, hero.AtZ), (ox, oz)) < 3,
                    $"moved {moved}, in {c.CellOf(hero).Kind}, walked {walked:0} of {said:0} s, the button {Hud.MapPane.EnterShown}");
                // an army of the horde: drawn only where ours see it
                var far = c.Cells.First(x => !c.Seen(x));
                var foe = new MapParty { Q = far.Q, R = far.R, CameQ = far.Q, CameR = far.R, Owner = Holder.Horde, Aim = PartyAim.Hold, Party = new Party() };
                foe.Party.Members.Add(new Traveller { Kind = Kinds.Orc, Hp = Kinds.Orc.Hp, MaxHp = Kinds.Orc.Hp });
                c.Parties.Add(foe);
                c.PlaceIn(foe, far);
                await Frames(3);
                int hidden = Hud.MapPane.Figures.TheirsDrawn;
                var open = c.Cells.First(x => c.Seen(x) && x.Owner == Holder.None && x != ore);
                foe.Q = open.Q; foe.R = open.R; foe.CameQ = open.Q; foe.CameR = open.R;
                c.PlaceIn(foe, open);
                await Frames(3);
                int seen = Hud.MapPane.Figures.TheirsDrawn;
                c.Parties.Remove(foe);
                check("an army of the horde is drawn on the map only where ours see it", hidden == 0 && seen == 1, $"in the fog {hidden}, seen {seen}");
                // {EnterCell}: into the ore valley
                Again("atlas-ore", fails);
                Tools._UnhandledInput(new InputEventKey { PhysicalKeycode = Bindings.KeyOf(Act.EnterCell), Keycode = Bindings.KeyOf(Act.EnterCell), Pressed = true });
                return false;
            }
            case "atlas-ore":
            {
                await Frames(4);
                int side = Campaign.SideToward(ore, 0, 0);
                var ours = Sim.Units.FindAll(u => !u.Dead && u.Team == Team.Player);
                var hex = Site.Hex;
                float far = ours.Count == 0 ? 999 : ours.Max(u => hex.FromSide(side, u.X, u.Z).d) / VoxelWorld.PerMetre;
                check("{EnterCell} takes the hero and his army into the ore valley, by the side toward the first valley: his army is seen there, the map put by",
                    c.Here == ore && !AtlasOpen && ours.Count == 1 + Campaign.HeroArmy.Length && ours.Count(u => u.T == Kinds.Captain && u.Hero != null) == 1 && far < 45 && Hero() == null,
                    $"here {c.Here.Kind}, the map {AtlasOpen}, {ours.Count} of ours, the farthest {far:0} m from side {side}");
                // «Вывести на карту»: out again at once
                Run(4);
                string why = LeaveToMap(ours);
                await Frames(3);
                var back = Hero();
                check("«Вывести на карту» takes them out at once: on the map by the side nearest them, the map open on the hero",
                    why == null && back != null && back.Party.Members.Count == 1 + Campaign.HeroArmy.Length && AtlasOpen && Hud.MapPane.Hero == back && c.CellOf(back) == ore
                    && !Sim.Units.Exists(u => !u.Dead && u.Team == Team.Player) && SideNear(back) == side,
                    $"refused «{why}»; on the map {back != null} with {back?.Party.Members.Count}, the map {AtlasOpen}, chosen {Hud.MapPane.Hero == back}, nearest side {(back == null ? -1 : SideNear(back))} of {side}");
                bool wrote = SaveCampaign();
                var read = Campaign.Read(Saves.CampaignDir(CampSlot));
                var rb = read.Parties.Find(p => p.Owner == Holder.Ours && p.Party.Commander?.Hero != null);
                check("the campaign written and read back keeps the hero where he stands on the map, with his army",
                    wrote && rb != null && back != null && Math.Abs(rb.AtX - back.AtX) < .01f && Math.Abs(rb.AtZ - back.AtZ) < .01f && rb.Party.Members.Count == back.Party.Members.Count
                    && rb.Party.Commander.Hero.Name == back.Party.Commander.Hero.Name,
                    $"written {wrote}; read at {rb?.AtX:0.0},{rb?.AtZ:0.0} of {back?.AtX:0.0},{back?.AtZ:0.0}");
                CloseAtlas();
                string folder = Saves.CampaignDir(CampSlot);
                LeaveCampaign();
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
                // the big map as a new campaign is played (Main.SelfTest.World.cs)
                return SelfTestWorld(fails);
            }
        }
        return true;
    }
}
