using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using VoxelTower.Core;

/// <summary>
/// The bench's ground for the horde as a faction on the big map (--hordeai: MapAI.Heroes.cs, MapAI.Towns.cs,
/// Factions.cs): it begins with its treasury, its towns and a hero in each; its towns build one building a day and breed
/// by the week; its heroes walk with a day's moves, flag mines and pick up piles, gain and rise; they go round a stack of
/// the map's keepers they would not beat and through one they would; nothing against ours while the peace lasts, then
/// armies against the first valley only big enough and days apart; a hero fallen is hired anew; the same seed the same,
/// a campaign written and read back goes on the same; a day of an XL map in a few milliseconds.
/// </summary>
static class HordeAIBench
{
    /// <summary>All the horde's game that plays on, as a line.</summary>
    static string Print(Campaign c)
    {
        var sb = new StringBuilder($"{c.Clock.Day}/{c.Clock.At:0.0} ");
        foreach (double s in c.Realm.Stock) sb.Append($"{s:0.00},");
        foreach (var t in c.Realm.Towns.Values.OrderBy(t => t.Key)) sb.Append($" T{t.Key}:{t.Works}:{string.Join(".", t.Pool)}");
        foreach (var p in c.Parties)
            sb.Append($" P{p.Id}{p.Owner}@{p.Q},{p.R}:{p.Party.Members.Count}:{p.Lord?.Hero?.Level}:{p.Lord?.Hero?.Xp}:{p.Aim}:{p.MovesLeft}:{p.Path?.Count ?? 0}");
        sb.Append($" horde cells {c.Cells.Count(x => x.Owner == Owner.Horde)}, mines {c.Cells.Count(x => x.Thing is { Kind: ThingKind.Mine, Owner: Owner.Horde })}");
        sb.Append($" built {c.Realm.Built} hired {c.Realm.Hired} picked {c.Realm.Picked} marches {c.Realm.Marches}");
        return sb.ToString();
    }

    static IEnumerable<MapParty> Heroes(Campaign c) => c.HeroesOf(Owner.Horde);

    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine();
        // ---- the horde begins as a faction
        {
            var c = Campaign.NewWorld(7, 36);
            c.AddHero();
            c.Prepare();
            var towns = c.Lairs;
            int main = towns.Count(t => t.Thing?.Kind == ThingKind.HordeTown);
            var heroes = Heroes(c).ToList();
            bool atTowns = heroes.All(p => towns.Any(t => t.Q == p.Q && t.R == p.R));
            check("the horde begins as a faction of its own: a treasury, its towns with goblins' huts and a fighting pit, a hero standing in each of its towns with an army",
                c.Factions[1].Kind == FactionKind.Horde && c.Factions[1].Machine && !c.Factions[0].Machine
                && c.Realm.Stock[(int)Res.Gold] >= 500 && c.Realm.Towns.Count == towns.Count && main >= 1 && heroes.Count == main && atTowns
                && heroes.All(p => p.Party.Members.Count >= 8 && p.Lord.Hero.Level == 1)
                && c.Realm.Towns.Values.Where(t => !t.Lair).All(t => t.Has(HordeWork.Huts) && t.Has(HordeWork.Pit)),
                $"gold {c.Realm.Stock[(int)Res.Gold]:0}, towns {c.Realm.Towns.Count} of {towns.Count} ({main} main), heroes {heroes.Count} at their towns {atTowns}");
        }

        // ---- a town breeds by the week; it builds one a day while it can pay, walls of stone with the stone wall
        {
            var c = Campaign.NewWorld(7, 36);
            c.AddHero();
            c.Advance(1);
            var cell = c.Lairs.First(t => t.Thing?.Kind == ThingKind.HordeTown);
            var t = c.TownOf(cell);
            int goblins = Array.IndexOf(Campaign.HordeKinds, Kinds.Goblin), orcs = Array.IndexOf(Campaign.HordeKinds, Kinds.Orc);
            int g0 = t.Pool[goblins], o0 = t.Pool[orcs];
            c.Breed(t);
            bool bred = t.Pool[goblins] == Math.Min(g0 + 12, 36) && t.Pool[orcs] == Math.Min(o0 + 6, 18);
            // rich enough for everything: a building a day, in its order
            for (int i = 0; i < c.Realm.Stock.Length; i++) c.Realm.Stock[i] = 50000;
            var builtDays = new List<int>();
            int was = c.Realm.Built;
            for (int d = 0; d < 12; d++)
            {
                c.Advance(DayClock.Cycle);
                c.News.Clear();
                if (c.Realm.Built > was) { builtDays.Add(c.Clock.Day); was = c.Realm.Built; }
            }
            bool oneADay = builtDays.Count >= 9 && builtDays.Distinct().Count() == builtDays.Count;
            check("a town of the horde breeds its kinds at a week's start; it builds one building a day in its order while the treasury pays, its walls of stone once the stone wall stands",
                bred && oneADay && t.Has(HordeWork.TrollDen) && t.Has(HordeWork.GiantCave) && cell.Sum.Walls >= Campaign.StoneWalls,
                $"goblins {g0} → {t.Pool[goblins]}, orcs {o0} → {t.Pool[orcs]}; built on days {string.Join(",", builtDays)}; walls {cell.Sum.Walls:0.00}");
        }

        // ---- thirty days with our autoplayer: the horde's heroes walk with their moves, flag mines, pick up piles, rise;
        // nothing of ours struck in the peace; its armies against home big and days apart; no little party sent at ours
        int flaggedAll = 0, pickedAll = 0;
        foreach (uint seed in new uint[] { 7, 21 })
        {
            var c = Campaign.NewWorld(seed, 36);
            c.AddHero();
            var ap = new AutoPlayer(c);
            ap.Begin();
            var hordeMines0 = c.Cells.Where(x => x.Thing is { Kind: ThingKind.Mine, Owner: Owner.Horde }).ToHashSet();
            bool walkedWithMoves = false, tiredSeen = false, peaceBroken = false, littleSent = false;
            var marches = new List<int>();
            string peaceWhat = "", little = "";
            for (double t = 0; t < 40 * DayClock.Cycle; t += 5)
            {
                c.Advance(5);
                ap.Tick();
                foreach (var p in c.Parties)
                {
                    if (p.Owner != Owner.Horde) continue;
                    if (p.Lord?.Hero != null && p.Moving && p.MovesLeft < p.MovesMax) walkedWithMoves = true;
                    if (p.Tired) tiredSeen = true;
                    // (a band without a hero walking at ours: none on the big map)
                    if (p.Lord?.Hero == null && p.Moving && p.Aim is PartyAim.Strike or PartyAim.Take) { littleSent = true; little = $"a band of {p.Party.Members.Count} without a hero, {p.Aim}"; }
                }
                foreach (var n in c.News)
                {
                    bool againstOurs = n.Kind == NewsKind.RaidComing || n.Text.Contains("теперь у орды") || n.Text.StartsWith("Орда напала") || n.Text.StartsWith("Орда взяла")
                        || n.Text.Contains("орда разбила наш отряд");
                    if (againstOurs && c.Clock.Day <= c.PeaceDays) { peaceBroken = true; peaceWhat = $"day {c.Clock.Day}: {n.Text}"; }
                    if (n.Kind == NewsKind.RaidComing && n.Text.StartsWith("Войско орды идёт на первую долину"))
                    {
                        marches.Add(c.Clock.Day);
                        if (n.Party != null && n.Party.Party.Members.Count < c.LeastArmy) { littleSent = true; little = $"an army of {n.Party.Party.Members.Count} against home"; }
                    }
                }
                c.News.Clear();
            }
            int flagged = c.Cells.Count(x => x.Thing is { Kind: ThingKind.Mine, Owner: Owner.Horde } && !hordeMines0.Contains(x));
            int best = Heroes(c).Select(p => p.Lord.Hero.Level).DefaultIfEmpty(0).Max();
            bool apart = marches.Zip(marches.Skip(1), (a, b) => b - a).All(d => d >= c.MarchGap);
            flaggedAll += flagged; pickedAll += c.Realm.Picked;
            check($"forty days of an S map (seed {seed}): the horde's heroes walk with a day's moves and rise; its towns build; another hero hired",
                walkedWithMoves && tiredSeen && (best >= 2 || c.Realm.Fell > 0) && c.Realm.Built >= 5 && c.Realm.Hired >= 2,
                $"walked with moves {walkedWithMoves}, waited for dawn {tiredSeen}; mines flagged {flagged}, picked {c.Realm.Picked}, best level {best}, fell {c.Realm.Fell}; built {c.Realm.Built}, heroes hired {c.Realm.Hired}");
            check($"…nothing of ours struck while the peace lasts ({c.PeaceDays} days); its armies against the first valley at least {c.LeastArmy} strong and {c.MarchGap} days apart; no little band sent at ours",
                !peaceBroken && apart && !littleSent,
                $"{(peaceBroken ? peaceWhat : "peace kept")}; marches on days {string.Join(",", marches)}; {(littleSent ? little : "no little band")}");
        }

        check("…and on both maps its heroes flag mines and pick up piles and campfires", flaggedAll > 0 && pickedAll > 0,
            $"mines flagged {flaggedAll}, piles and campfires {pickedAll}");

        // ---- a hero of the horde goes round a stack he would not beat and falls on one he would
        {
            var c = Campaign.NewWorld(7, 36);
            c.AddHero();
            c.Advance(1);
            var p = Heroes(c).First();
            var here = c.CellOf(p);
            // two gold mines two hexes off either way along a row of open ground, nothing else worth a walk about
            MapCell strong = null, weak = null;
            foreach (var h in c.Cells.Where(x => x.Passable && x.Thing == null && x.Sum.Keep.Count == 0 && x.Owner == Owner.None && x.Zone == here.Zone))
            {
                // (open ground of the same zone all along the row, the way not walled)
                bool Clear(MapCell x) => x is { Passable: true, Thing: null, Owner: Owner.None, Pass: false } && x.Sum.Keep.Count == 0 && x.Zone == h.Zone && x.Rivers == 0;
                var l = c.Cell(h.Col - 2, h.Row); var r = c.Cell(h.Col + 2, h.Row);
                if (Clear(l) && Clear(r) && Clear(c.Cell(h.Col - 1, h.Row)) && Clear(c.Cell(h.Col + 1, h.Row)) && Clear(c.Cell(h.Col - 3, h.Row)) && Clear(c.Cell(h.Col - 4, h.Row)))
                { here = h; strong = l; weak = r; break; }
            }
            p.Q = here.Q; p.R = here.R; p.Path = null; p.MovesLeft = p.MovesMax;
            foreach (var x in c.Cells) if (x != strong && x != weak && x.Thing is { Kind: not (ThingKind.HordeTown or ThingKind.Lair or ThingKind.Citadel) } && Campaign.Distance(x.Q, x.R, here.Q, here.R) <= 40) x.Thing = null;
            foreach (var cell in new[] { strong, weak }) cell.Thing = new MapThing { Kind = ThingKind.Mine, Res = Res.Gold, Amount = 60 };
            for (int i = 0; i < 40; i++) strong.Sum.Keep.Add(Campaign.Raider(Kinds.Troll));
            for (int i = 0; i < 2; i++) weak.Sum.Keep.Add(Campaign.Raider(Kinds.Wolf));
            c.ThinkNow(p);
            bool toWeak = p.Moving && p.GoalQ == weak.Q && p.GoalR == weak.R;
            // the way to a hex past the strong stack goes round it
            var beyond = c.Cell(strong.Col - 3, strong.Row);
            p.Path = null;
            var way = beyond != null && beyond.Passable ? c.WayOf(p, beyond) : null;
            bool round = way == null || !way.Contains(strong);
            check("a hero of the horde falls on a stack of the map's keepers he would beat and goes round one he would not",
                toWeak && round, $"went for the weak {toWeak} (aim at {p.GoalQ},{p.GoalR}, weak {weak.Q},{weak.R}, «{p.Errand}», {p.Party.Members.Count} men, at {here.Q},{here.R} zone {here.Zone} of the town {c.CellOf(p)?.Zone}); the way past the strong round it {round} ({way?.Count} hexes)");
        }

        // ---- a hero of the horde fallen is hired anew
        {
            var c = Campaign.NewWorld(7, 36);
            c.AddHero();
            c.Advance(1);
            foreach (var p in Heroes(c).ToList()) c.Parties.Remove(p);
            c.Realm.Stock[(int)Res.Gold] = 5000;
            int hired = c.Realm.Hired;
            c.Advance(DayClock.Cycle * 2);
            check("a hero of the horde lost, another is hired at its town at the next dawn", Heroes(c).Any() && c.Realm.Hired > hired,
                $"heroes now {Heroes(c).Count()}, hired {hired} → {c.Realm.Hired}");
        }

        // ---- the same seed the same; written mid-game and read back, the same going on
        {
            Campaign Play()
            {
                var c = Campaign.NewWorld(13, 36);
                c.AddHero();
                for (int d = 0; d < 20; d++) { c.Advance(DayClock.Cycle); c.News.Clear(); }
                return c;
            }
            var a = Play(); var b = Play();
            string pa = Print(a), pb = Print(b);
            string dir = Path.Combine(Path.GetTempPath(), "vt-hordeai-bench");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            a.Write(dir);
            var back = Campaign.Read(dir);
            string p0 = Print(a), p1 = Print(back);
            for (int d = 0; d < 6; d++) { a.Advance(DayClock.Cycle); back.Advance(DayClock.Cycle); a.News.Clear(); back.News.Clear(); }
            string q0 = Print(a), q1 = Print(back);
            Directory.Delete(dir, true);
            check("the horde plays the same by the same seed; written on its twentieth day and read back, it goes on the same",
                pa == pb && p0 == p1 && q0 == q1, $"same seed {pa == pb}; read back {p0 == p1}; six days on {q0 == q1}" + (q0 == q1 ? "" : $"\n  {q0}\n  {q1}"));
        }

        // ---- a day of the horde on an XL map in a few milliseconds (timed the second time over: the first warms the code up)
        {
            Campaign Thirty(out double sum, out double worst)
            {
                var c = Campaign.NewWorld(17, 144);
                c.AddHero();
                sum = 0; worst = 0;
                for (int d = 0; d < 30; d++)
                {
                    double t0 = BenchClock.CpuMs();
                    c.Advance(DayClock.Cycle);
                    double t = BenchClock.CpuMs() - t0;
                    sum += t; worst = Math.Max(worst, t);
                    c.News.Clear();
                }
                return c;
            }
            Thirty(out double cold, out _);
            var c = Thirty(out double sum, out double worst);
            check("thirty days of the horde's four realms on an XL map, a day in a few milliseconds",
                sum / 30 < 8 && Heroes(c).Count() >= 4,
                $"{sum / 30:0.0} ms a day on average ({cold / 30:0.0} the first time over), {worst:0} ms the worst; heroes {Heroes(c).Count()}, built {c.Realm.Built}");
        }
    }
}
