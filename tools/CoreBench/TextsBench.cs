using System;
using System.Collections.Generic;
using System.Linq;
using VoxelTower.Core;

/// <summary>
/// The game says what it does (--texts): the words over each kind of ours tell its own numbers — how far it shoots or
/// reaches, the places it takes, the logs it costs, its crew — and every kind has words of its own (the commander and the
/// architect had none); the lore and the buildings tell the shares the sim uses (the mortar, the drill yard, the vigil,
/// the might of the faith said a third where the sim takes 43 %, 30 % and 30 %); a kind costs the same wherever it is
/// hired: at a dwelling of the map, at the citadel and in its building of a valley, a fifth off only with that valley's
/// drill yard, and the price says so (the tester saw the architect at 150 in one place and 120 in another); the
/// architect is offered in his building and nowhere else, and told so at the citadel; no mouse button is written out
/// where the player may have swapped them.
/// </summary>
static class TextsBench
{
    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine();
        // ---- the kinds' words and their numbers
        var kinds = Kinds.Ours.Append(Kinds.Crewman).Distinct().ToList();
        var wrong = new List<string>();
        foreach (var t in kinds)
        {
            string tip = KindTexts.Tip(t);
            var miss = new List<string>();
            if (!tip.StartsWith(t.Name + "\n")) miss.Add("no words of its own");
            if (t.Range > 0 && !tip.Contains($"{KindTexts.M(t.Range)} м")) miss.Add($"range {KindTexts.M(t.Range)} м");
            if (t.Tool is "spear" or "halberd" && !tip.Contains($"{KindTexts.M(t.Reach)} м")) miss.Add($"reach {KindTexts.M(t.Reach)} м");
            if (t.Room > 1 && !tip.Contains($"занимает {t.Room} ")) miss.Add($"{t.Room} places");
            if (t.Logs > 0 && !tip.Contains($"{t.Logs:0.#} м³ брёвен".Replace('.', ','))) miss.Add($"{t.Logs} m³ of logs");
            if (t.Crew > 0 && !tip.Contains($"из {t.Crew} человек")) miss.Add($"a crew of {t.Crew}");
            if (tip.Contains("ПКМ") || tip.Contains("ЛКМ")) miss.Add("a mouse button written out");
            if (miss.Count > 0) wrong.Add($"{t.Key}: {string.Join(", ", miss.Distinct())}");
        }
        check($"every kind of ours ({kinds.Count}) has words of its own, with its own range, reach, places, logs and crew in them",
            wrong.Count == 0, wrong.Count == 0 ? $"e.g. «{KindTexts.Tip(Kinds.Spearman).Split('\n')[1]}»" : string.Join("; ", wrong));
        string nightWords = KindTexts.Tip(Kinds.Archer), night = KindTexts.M(Kinds.Archer.Range * (1 - Sim.DarkLoss));
        check("the archer's words give its reach in the dark as the sim takes it", nightWords.Contains($"Ночью видит врага на {night} м"), $"{night} м");

        // ---- the lore's and the buildings' shares
        string masons = Sim.TownKindOf("masons").About, drill = Sim.TownKindOf("drill").About;
        string vigil = Sim.LoreOf("vigil")?.About ?? "", might = Sim.LoreOf("might")?.About ?? "";
        var shares = new List<(string what, string text, string want)>
        {
            ("the mortar", masons, $"на {KindTexts.Pct(1 / Sim.MortarHack)} % дольше"),
            ("the drill yard", drill, $"Наём на {KindTexts.Pct(Sim.DrillCut)} % дешевле"),
            ("the vigil", vigil, $"на {KindTexts.Pct(Sim.VigilCut)} % быстрее"),
            ("the might of the faith", might, $"на {KindTexts.Pct(Sim.MightMore)} % сильнее"),
        };
        var off = shares.Where(s => !s.text.Contains(s.want)).ToList();
        check("the lore and the buildings tell the shares the sim uses (the mortar 43 %, the drill yard 20 %, the vigil and the might 30 %)",
            off.Count == 0, off.Count == 0 ? string.Join("; ", shares.Select(s => $"{s.what}: {s.want}")) : string.Join("; ", off.Select(s => $"{s.what}: «{s.text}» wants «{s.want}»")));

        // ---- one price wherever a kind is hired
        var mp = new MapParams();
        var w = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        var site = MapGen.Generate(w, mp);
        w.Compact();
        var sim = new Sim(w, site);
        sim.Waves.Countdown = 1e9; sim.WildTarget = 0;
        var breeds = new[] { Kinds.Spearman, Kinds.Archer, Kinds.Slinger, Kinds.Swordsman, Kinds.Crossbowman, Kinds.Hound, Kinds.Halberdier, Kinds.Rider, Kinds.Knight };
        var dear = breeds.Where(t => Campaign.HireCost(t) != sim.HireCost(t) || sim.HireCost(t) != t.Cost || sim.HireNote(t) != "").ToList();
        check("a kind costs the same at a dwelling of the map as in a valley (no drill yard there), and nothing is said of a discount",
            dear.Count == 0, string.Join(", ", (dear.Count > 0 ? dear : breeds.Take(3).ToList()).Select(t => $"{t.Key} map {Campaign.HireCost(t)}, valley {sim.HireCost(t)}, own {t.Cost}")));
        sim.Learnt.Add("drill");
        int arch = sim.HireCost(Kinds.Architect);
        string note = sim.HireNote(Kinds.Architect);
        check("with the valley's drill yard every kind is a fifth cheaper there, and the price says why",
            arch == (int)Math.Round(Kinds.Architect.Cost * Sim.DrillCut) && note.Contains($"было {Kinds.Architect.Cost}") && note.Contains("учебный плац"),
            $"the architect {Kinds.Architect.Cost} → {arch}{note}");
        sim.Learnt.Remove("drill");

        // ---- the architect: offered in his building and nowhere else, the citadel telling where
        string atKeep = sim.CantEnlist(Kinds.Architect) ?? "";
        var hirer = Sim.HirerKind(Kinds.Architect);
        var yard = new Building { Kind = "masons", X0 = site.KeepX + 60, Z0 = site.KeepZ + 60, X1 = site.KeepX + 92, Z1 = site.KeepZ + 100, Level = 1, Ready = true };
        yard.Stock[Kinds.Architect.Key] = 1;
        sim.Buildings.Add(yard);
        sim.Stock.Gold = 1000;
        string atYard = sim.CantHireAt(yard, Kinds.Architect);
        var found = sim.HirerOf(Kinds.Architect);
        sim.Spawn(Kinds.Architect, sim.Gate);
        string second = sim.CantHireAt(yard, Kinds.Architect) ?? "";
        sim.Buildings.Remove(yard);
        check("the architect is hired at the masons' yard: the citadel says so, the yard takes him on, a second is refused there as the one to a valley",
            hirer is { } h && h.kind.Key == "masons" && atKeep.Contains("Двор каменщиков") && atYard == null && found == yard && second == Sim.ArchitectOne,
            $"at the citadel: «{atKeep}»; at the yard: {atYard ?? "hired"}; the second: «{second}»");
    }
}
