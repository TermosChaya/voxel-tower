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
    /// The creatures of the wild on the big map (Bestiary.cs, Campaign.Bestiary.cs), after the map's own test: a new S
    /// campaign, a pack of wolves set on a hex near home; the map says what keeps it, how many and against the hero; he
    /// walks onto it, the pack neither runs nor joins, and he stops before a fight; gone in, the pack is in the valley as
    /// wolves, and they and his army fight. Returns false: the scene begins again for it.
    /// </summary>
    bool SelfTestBeasts(Func<int> fails)
    {
        string dir = Saves.CampaignDir(BoardSlot);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Atlas = Campaign.NewWorld(Seed, 36);
        Atlas.AddHero();
        CampSlot = BoardSlot;
        Atlas.Write(dir);
        _atlasAtStart = true;
        Again("beasts", fails);
        GetTree().ReloadCurrentScene();
        return false;
    }

    async Task<bool> SelfTestBeastsGo(string stage, Action<string, bool, string> check, Func<int> fails)
    {
        async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        void Run(double seconds) { for (double t = 0; t < seconds; t += .5) { Advance(.5); CampaignTick(.5); } }
        await Frames(4);
        var c = Atlas;
        var wolf = Kinds.Wolf;
        if (stage == "beasts")
        {
            var hero = c.Parties.Find(p => p.Owner == Holder.Ours && p.Party.Commander?.Hero != null);
            var home = c.Home;
            // an open hex two or three steps from home, nothing on it: six wolves set on it
            var den = c.Cells.Where(x => x.Passable && x.Thing == null && x.Sum.Keep.Count == 0 && !x.Pass && x.Owner == Holder.None)
                .Where(x => Campaign.Distance(x.Q, x.R, home.Q, home.R) is 2 or 3)
                .OrderBy(x => Campaign.Distance(x.Q, x.R, home.Q, home.R)).FirstOrDefault();
            if (hero == null || den == null)
            {
                check("a pack of the wild set near home on a new big map", false, $"hero {hero != null}, a hex for it {den != null}");
                return true;
            }
            den.Thing = new MapThing { Kind = ThingKind.Monster };
            for (int i = 0; i < 6; i++) den.Sum.Keep.Add(new Traveller { Kind = wolf, Hp = wolf.Hp, MaxHp = wolf.Hp, Commands = false });
            c.Reveal(den, 1);
            string said = DescribeHex(den.Col, den.Row);
            hero.MovesLeft = hero.MovesMax = 9999;
            Hud.MapPane.Choose(hero);
            Hud.MapPane.GoTo(den.Col, den.Row);
            for (int i = 0; i < 200 && hero.Moving; i++) Run(1);
            check("a pack of wolves near home: the map names it and its number and weighs it against the hero; walked onto, it neither runs nor joins, the hero stops before a fight",
                said.StartsWith("Стая волков: 6.") && said.Contains("Против выбранного отряда") && c.CellOf(hero) == den && hero.Waits
                && c.Foes(den) == 6 && den.Sum.Keep.Count == 6 && c.Joiners.Count == 0,
                $"said «{said.Replace('\n', ' ')}»; at the den {c.CellOf(hero) == den}, waits {hero.Waits}, foes {c.Foes(den)}, keep {den.Sum.Keep.Count}");
            Again("beasts-valley", fails);
            EnterCell(hero);
            return false;
        }
        // in the den's valley: the pack at its spot, the hero's army brought up to it
        var pack = Sim.Units.FindAll(u => !u.Dead && u.T == wolf);
        var ours = Sim.Units.FindAll(u => !u.Dead && u.Team == Team.Player);
        if (pack.Count > 0)
            foreach (var u in ours)
            {
                int at = Sim.NearestStand((int)pack[0].X + 30, (int)pack[0].Z + (u.Id % 5 - 2) * 4, u.T.Move.Height);
                if (at < 0) continue;
                u.Cell = at; (u.X, u.Y, u.Z) = Sim.Nav.Feet(at); u.Path = null;
            }
        Run(20);
        bool bitten = ours.Any(u => u.Dead || u.Hp < u.T.Hp), struck = pack.Any(u => u.Dead || u.Hp < u.T.Hp);
        check("gone in, the pack keeps the valley as wolves of the wild, and they and the hero's army fight: blows on both sides",
            pack.Count == 6 && pack.All(u => u.Team == Team.Wild) && ours.Count > 0 && bitten && struck,
            $"{pack.Count} wolves, {ours.Count} of ours; ours hurt {ours.Count(u => u.Dead || u.Hp < u.T.Hp)}, wolves hurt {pack.Count(u => u.Dead || u.Hp < u.T.Hp)}");
        string folder = Saves.CampaignDir(CampSlot);
        LeaveCampaign();
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
        return true;
    }
}
