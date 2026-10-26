using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using VoxelTower.Core;

/// <summary>
/// A campaign on the big map played by itself (--autoplay seed S|M|L|XL days [dir]): our side by the
/// <see cref="AutoPlayer"/>, the horde by its own; each day a line of how it stands (gold, the rare resources, our
/// heroes, the hexes and mines each side holds, the strength of both, the fog lifted), each day a picture of the
/// whole map as the player sees it, the news of the day; at the end the pictures run together into a film (mp4 and
/// gif, by ffmpeg if it is there), the table (days.csv) for the charts, and the story of the game (story.txt).
/// </summary>
static class AutoPlayBench
{
    public static void Run(uint seed, string size, int days, string dir)
    {
        int side = size.ToUpperInvariant() switch { "S" => 36, "M" => 72, "L" => 108, "XL" => 144, _ => int.Parse(size) };
        dir ??= Path.Combine(Path.GetTempPath(), $"vt-autoplay-{Campaign.SizeName(side)}-{seed}");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(Path.Combine(dir, "frames"));
        var c = Campaign.NewWorld(seed, side);
        c.StartHeroes();
        var ap = new AutoPlayer(c);
        ap.Begin();
        Console.WriteLine($"autoplay: seed {seed}, {Campaign.SizeName(side)} {side}×{side}, {days} days, {c.Zones.Count} zones, {c.Lairs.Count} towns and lairs of the horde → {dir}");

        var log = new StringBuilder();
        var csv = new StringBuilder("day,gold,mercury,sulfur,crystal,gems,logs,stone,metal,heroes,best_level,our_strength,our_men,keep,our_cells,our_mines,horde_cells,horde_mines,horde_strength,horde_out,explored,fights,fights_won\n");
        var moments = new List<string>();
        int firstMine = -1, firstFight = -1, firstRaid = -1, peakCells = 0, peakDay = 0;
        int said = 0, fightsSeen = 0;
        double stepped = 0;
        var watch = Stopwatch.StartNew();
        Frame(c, ap, dir, 1);
        Line(c, ap, csv);
        for (int day = 1; day <= days && !c.Won && !c.Defeated; )
        {
            c.Advance(5);
            stepped += 5;
            ap.Tick();
            foreach (var n in c.News)
            {
                string at = n.Cell != null ? $" [{n.Cell.Col},{n.Cell.Row}]" : "";
                log.AppendLine($"д{c.Clock.Day} {c.Clock.At / DayClock.Cycle * 24:00}ч  {Kind(n.Kind)} {n.Text}{at}");
                if (n.Kind == NewsKind.RaidComing || n.Kind == NewsKind.Lost && n.Text.Contains("первую долину")) { if (firstRaid < 0) { firstRaid = c.Clock.Day; moments.Add($"день {c.Clock.Day}: первая угроза дому — {n.Text}"); } }
                if (n.Kind == NewsKind.Fought && n.Cell == c.Home) moments.Add($"день {c.Clock.Day}: бой у цитадели — {n.Text}");
                if (n.Kind == NewsKind.Lost) moments.Add($"день {c.Clock.Day}: {n.Text}");
            }
            c.News.Clear();
            for (; said < ap.Said.Count; said++) log.AppendLine("   ▸ " + ap.Said[said]);
            for (; fightsSeen < ap.Fights.Count; fightsSeen++)
            {
                var f = ap.Fights[fightsSeen];
                if (firstFight < 0) { firstFight = f.day; moments.Add($"день {f.day}: первый бой героя у {f.where}, шансы {f.odds:0.00}, {(f.won ? "победа" : "поражение")}"); }
            }
            if (firstMine < 0 && c.Cells.Any(x => x.Thing is { Kind: ThingKind.Mine, Owner: Owner.Ours })) { firstMine = c.Clock.Day; moments.Add($"день {c.Clock.Day}: первый рудник наш — {c.Cells.First(x => x.Thing is { Kind: ThingKind.Mine, Owner: Owner.Ours }).Thing.Name}"); }
            if (c.Clock.Day > day)
            {
                day = c.Clock.Day;
                int cells = c.Cells.Count(x => x.Owner == Owner.Ours) + c.Cells.Count(x => x.Thing is { Kind: ThingKind.Mine, Owner: Owner.Ours });
                if (cells > peakCells) { peakCells = cells; peakDay = day; }
                Frame(c, ap, dir, day);
                Line(c, ap, csv);
                Console.WriteLine(Brief(c, ap));
            }
        }
        string end = c.Won ? $"Победа на {c.Clock.Day}-й день: взяты все логова орды." : c.Defeated ? $"Поражение на {c.Clock.Day}-й день: первая долина пала, другой нет." : $"После {days} дней партия идёт: {Brief(c, ap)}";
        moments.Add($"пик: {peakCells} наших клеток и рудников на {peakDay}-й день");
        moments.Add(end);
        File.WriteAllText(Path.Combine(dir, "log.txt"), log.ToString());
        File.WriteAllText(Path.Combine(dir, "days.csv"), csv.ToString());
        File.WriteAllText(Path.Combine(dir, "story.txt"), string.Join("\n", moments) + "\n");
        Console.WriteLine($"autoplay: {c.Clock.Day} days in {watch.Elapsed.TotalSeconds:0.0} s; {end}");
        foreach (var m in moments) Console.WriteLine("  " + m);
        Film(dir, side);
    }

    static string Kind(NewsKind k) => k switch
    {
        NewsKind.Fought => "⚔", NewsKind.Taken => "＋", NewsKind.Lost => "✖", NewsKind.RaidComing => "‼", NewsKind.Grew => "·", _ => "→",
    };

    static string Brief(Campaign c, AutoPlayer ap)
    {
        var heroes = c.Parties.Where(p => p.Owner == Owner.Ours && p.Party.Commander?.Hero != null).ToList();
        int ourCells = c.Cells.Count(x => x.Owner == Owner.Ours), ourMines = c.Cells.Count(x => x.Thing is { Kind: ThingKind.Mine, Owner: Owner.Ours });
        int hordeCells = c.Cells.Count(x => x.Owner == Owner.Horde), hordeMines = c.Cells.Count(x => x.Thing is { Kind: ThingKind.Mine, Owner: Owner.Horde });
        string hs = string.Join("; ", heroes.Select(p => $"{p.Party.Commander.Hero.Name} ур.{p.Party.Commander.Hero.Level} {p.Party.Members.Count}чел. @{p.Col},{p.Row} {(ap.Aims.TryGetValue(p.Id, out var a) ? a : "")}"));
        return $"день {c.Clock.Day}: золото {c.Gold:0}, рудников {ourMines} (орды {hordeMines}), клеток наших {ourCells} (орды {hordeCells}), гарнизон {c.Home?.Sum.Keep.Count}, туман снят {Explored(c):0}%; {hs}";
    }

    static double Explored(Campaign c) => 100.0 * c.Cells.Count(x => c.Explored(x)) / c.Cells.Count;

    static void Line(Campaign c, AutoPlayer ap, StringBuilder csv)
    {
        var heroes = c.Parties.Where(p => p.Owner == Owner.Ours && p.Party.Commander?.Hero != null).ToList();
        float ours = c.Parties.Where(p => p.Owner == Owner.Ours).Sum(p => QuickBattle.Strength(p.Party.Members, null));
        int men = c.Parties.Where(p => p.Owner == Owner.Ours).Sum(p => p.Party.Members.Count);
        float horde = c.Parties.Where(p => p.Owner == Owner.Horde).Sum(p => QuickBattle.Strength(p.Party.Members, p.Lord))
            + c.Cells.Where(x => x.Owner == Owner.Horde).Sum(x => QuickBattle.Strength(x.Sum.Keep, null));
        var inv = CultureInfo.InvariantCulture;
        csv.AppendLine(string.Join(",", new object[]
        {
            c.Clock.Day, c.Gold.ToString("0", inv), c.Rare[0].ToString("0", inv), c.Rare[1].ToString("0", inv), c.Rare[2].ToString("0", inv), c.Rare[3].ToString("0", inv),
            c.StockOf(Res.Logs).ToString("0.0", inv), c.StockOf(Res.Stone).ToString("0.0", inv), c.StockOf(Res.Metal).ToString("0.0", inv),
            heroes.Count, heroes.Count == 0 ? 0 : heroes.Max(p => p.Party.Commander.Hero.Level), ours.ToString("0", inv), men, c.Home?.Sum.Keep.Count ?? 0,
            c.Cells.Count(x => x.Owner == Owner.Ours), c.Cells.Count(x => x.Thing is { Kind: ThingKind.Mine, Owner: Owner.Ours }),
            c.Cells.Count(x => x.Owner == Owner.Horde), c.Cells.Count(x => x.Thing is { Kind: ThingKind.Mine, Owner: Owner.Horde }),
            horde.ToString("0", inv), c.Parties.Count(p => p.Owner == Owner.Horde), Explored(c).ToString("0.0", inv),
            ap.Fights.Count, ap.Fights.Count(f => f.won),
        }));
    }

    // ------------------------------------------------------------ the day's picture

    const int S = 12, Band = 22;

    /// <summary>
    /// The whole map on a day as the player sees it: the grounds (dim under the fog), whose each hex is, the things
    /// (mines in their owner's colour), our heroes with where they go, the horde's armies where the fog is lifted; a band
    /// above with the day and bars of the gold and of the hexes each side holds.
    /// </summary>
    static void Frame(Campaign c, AutoPlayer ap, string dir, int day)
    {
        int side = c.Width, w = side * S + S / 2, h = c.Height * (S - 2) + 2 + Band;
        var px = new byte[w * h * 3];
        void Put(int x, int y, (int r, int g, int b) col)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            int i = (y * w + x) * 3; px[i] = (byte)Math.Clamp(col.r, 0, 255); px[i + 1] = (byte)Math.Clamp(col.g, 0, 255); px[i + 2] = (byte)Math.Clamp(col.b, 0, 255);
        }
        void Disc(int cx, int cy, int r, (int, int, int) col, (int, int, int) ring)
        {
            for (int yy = -r - 1; yy <= r + 1; yy++)
                for (int xx = -r - 1; xx <= r + 1; xx++)
                {
                    int d2 = xx * xx + yy * yy;
                    if (d2 <= r * r) Put(cx + xx, cy + yy, col);
                    else if (d2 <= (r + 1) * (r + 1)) Put(cx + xx, cy + yy, ring);
                }
        }
        (int x, int y) Mid(MapCell x) => (x.Col * S + (x.Row & 1) * S / 2 + S / 2, x.Row * (S - 2) + S / 2 + Band);
        // the band
        for (int y = 0; y < Band; y++) for (int x = 0; x < w; x++) Put(x, y, (24, 22, 20));
        Digits($"D{day}", 4, 4, 3, (235, 220, 190), Put);
        int bx = 70, bw = Math.Max(40, w - bx - 8);
        double gold = Math.Min(1, c.Gold / 3000);
        for (int y = 4; y < 9; y++) for (int x = 0; x < (int)(bw * gold); x++) Put(bx + x, y, (230, 190, 60));
        int ours = c.Cells.Count(x => x.Owner == Owner.Ours) + c.Cells.Count(x => x.Thing is { Kind: ThingKind.Mine, Owner: Owner.Ours });
        int horde = c.Cells.Count(x => x.Owner == Owner.Horde);
        double scale = bw / Math.Max(40.0, ours + horde);
        for (int y = 12; y < 18; y++)
        {
            for (int x = 0; x < (int)(ours * scale); x++) Put(bx + x, y, (70, 120, 255));
            for (int x = 0; x < (int)(horde * scale); x++) Put(bx + (int)(ours * scale) + x, y, (220, 50, 40));
        }
        foreach (var x in c.Cells)
        {
            int ox = x.Col * S + (x.Row & 1) * S / 2, oy = x.Row * (S - 2) + Band;
            var g = Ground(x);
            if (x.Relief == HexRelief.Mountain) g = ((int)(g.Item1 * .45 + 70), (int)(g.Item2 * .45 + 66), (int)(g.Item3 * .45 + 70));
            else if (x.Relief == HexRelief.Forest) g = ((int)(g.Item1 * .55), (int)(g.Item2 * .7), (int)(g.Item3 * .5));
            else if (x.Relief == HexRelief.Hills) g = ((int)(g.Item1 * .85 + 20), (int)(g.Item2 * .85 + 14), (int)(g.Item3 * .8));
            if (x.Owner == Owner.Ours) g = Mix(g, (60, 110, 255), .4);
            else if (x.Owner == Owner.Horde && x.Ground != HexGround.Scorched) g = Mix(g, (230, 40, 30), .35);
            bool seen = c.Explored(x);
            if (!seen) g = Mix(g, (12, 12, 16), .78);
            for (int yy = 0; yy < S; yy++)
                for (int xx = 0; xx < S; xx++)
                {
                    int dy = Math.Min(yy, S - 1 - yy);
                    if (Math.Abs(xx - S / 2.0 + .5) > S / 2.0 - Math.Max(0, 2 - dy)) continue;
                    Put(ox + xx, oy + yy, g);
                }
            if (!seen) continue;
            var (cx, cy) = Mid(x);
            for (int s = 0; s < 6; s++)
            {
                double a = Math.PI / 180 * (-60 + 60 * s);
                if ((x.Roads >> s & 1) != 0) for (int k = 0; k <= S / 2; k++) Put(cx + (int)Math.Round(Math.Cos(a) * k), cy + (int)Math.Round(Math.Sin(a) * k), (120, 84, 46));
                if ((x.Rivers >> s & 1) != 0) for (int k = 0; k <= S / 2; k++) Put(cx + (int)Math.Round(Math.Cos(a) * k) + 1, cy + (int)Math.Round(Math.Sin(a) * k), (60, 120, 230));
            }
            if (x.Thing is { } t && !(t.Taken && t.Kind is ThingKind.Pile or ThingKind.Campfire or ThingKind.Chest or ThingKind.Shrine))
            {
                var col = t.Kind switch
                {
                    ThingKind.Citadel => (40, 90, 255), ThingKind.HordeTown or ThingKind.Lair => (255, 30, 30),
                    ThingKind.Mine => t.Owner == Owner.Ours ? (120, 200, 255) : t.Owner == Owner.Horde ? (255, 90, 60) : (255, 210, 0),
                    ThingKind.Monster => (230, 0, 230), ThingKind.Trove => Campaign.Untaken(x) ? (255, 140, 0) : (120, 90, 60),
                    ThingKind.Chest or ThingKind.Pile or ThingKind.Campfire => (255, 255, 255),
                    _ => (0, 230, 230),
                };
                int r = t.Kind is ThingKind.Citadel or ThingKind.HordeTown ? 3 : t.Kind == ThingKind.Mine ? 2 : 1;
                for (int yy = -r; yy <= r; yy++) for (int xx = -r; xx <= r; xx++) Put(cx + xx, cy + yy, col);
            }
            else if (x.Sum.Keep.Count > 0 && x.Owner == Owner.None) Put(cx, cy, (230, 0, 230));
        }
        // the armies of the horde where the fog is lifted, and where they go
        foreach (var p in c.Parties.Where(p => p.Owner == Owner.Horde))
        {
            var at = c.CellOf(p);
            if (at == null || !c.Explored(at)) continue;
            if (p.Moving) foreach (var s in p.Path) if (c.Explored(s)) { var (sx, sy) = Mid(s); Put(sx, sy, (150, 20, 20)); }
            var (hx, hy) = Mid(at);
            Disc(hx, hy, p.Lord != null ? 4 : 3, (220, 30, 20), (40, 0, 0));
        }
        // our heroes and their ways
        foreach (var p in c.Parties.Where(p => p.Owner == Owner.Ours))
        {
            var at = c.CellOf(p);
            if (at == null) continue;
            if (p.Moving) foreach (var s in p.Path) { var (sx, sy) = Mid(s); Put(sx, sy, (200, 230, 255)); Put(sx + 1, sy, (200, 230, 255)); }
            var (hx, hy) = Mid(at);
            Disc(hx, hy, p.Party.Commander?.Hero != null ? 4 : 2, (60, 140, 255), (255, 255, 255));
        }
        string file = Path.Combine(dir, "frames", $"day_{day:000}.ppm");
        using var f = File.Create(file);
        f.Write(Encoding.ASCII.GetBytes($"P6\n{w} {h}\n255\n"));
        f.Write(px);
    }

    static (int, int, int) Ground(MapCell x) => x.Ground switch
    {
        HexGround.Grass => (96, 150, 64), HexGround.Dirt => (140, 120, 80), HexGround.Sand => (214, 196, 130), HexGround.Snow => (232, 236, 240),
        HexGround.Swamp => (70, 96, 70), HexGround.Rough => (128, 112, 96), HexGround.Lava => (190, 70, 30), HexGround.Scorched => (60, 44, 40),
        HexGround.Water => (90, 140, 200), _ => (30, 60, 120),
    };

    static (int, int, int) Mix((int r, int g, int b) a, (int r, int g, int b) b, double t) =>
        ((int)(a.r + (b.r - a.r) * t), (int)(a.g + (b.g - a.g) * t), (int)(a.b + (b.b - a.b) * t));

    /// <summary>A few letters and the digits, three by five, for the day on the band.</summary>
    static readonly Dictionary<char, string> Font = new()
    {
        ['0'] = "111101101101111", ['1'] = "010110010010111", ['2'] = "111001111100111", ['3'] = "111001111001111", ['4'] = "101101111001001",
        ['5'] = "111100111001111", ['6'] = "111100111101111", ['7'] = "111001010010010", ['8'] = "111101111101111", ['9'] = "111101111001111",
        ['D'] = "110101101101110",
    };

    static void Digits(string s, int x, int y, int k, (int, int, int) col, Action<int, int, (int, int, int)> put)
    {
        foreach (char ch in s)
        {
            if (Font.TryGetValue(ch, out var bits))
                for (int i = 0; i < 15; i++)
                    if (bits[i] == '1')
                        for (int yy = 0; yy < k; yy++) for (int xx = 0; xx < k; xx++) put(x + i % 3 * k + xx, y + i / 3 * k + yy, col);
            x += 4 * k;
        }
    }

    // ------------------------------------------------------------ the film

    /// <summary>The days' pictures run together: an mp4 and a gif, if ffmpeg is there.</summary>
    static void Film(string dir, int side)
    {
        string ff = File.Exists("/opt/homebrew/bin/ffmpeg") ? "/opt/homebrew/bin/ffmpeg" : "ffmpeg";
        int up = side <= 36 ? 2 : 1;
        string frames = Path.Combine(dir, "frames", "day_%03d.ppm");
        bool Run(string args)
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo(ff, args) { RedirectStandardError = true, RedirectStandardOutput = true });
                p.StandardError.ReadToEnd(); p.WaitForExit();
                return p.ExitCode == 0;
            }
            catch (Exception e) { Console.WriteLine($"autoplay: no film ({e.Message})"); return false; }
        }
        string scale = $"scale=iw*{up}:ih*{up}:flags=neighbor";
        bool mp4 = Run($"-y -framerate 4 -i \"{frames}\" -vf \"{scale},pad=ceil(iw/2)*2:ceil(ih/2)*2\" -c:v libx264 -pix_fmt yuv420p \"{Path.Combine(dir, "timelapse.mp4")}\"");
        bool gif = Run($"-y -framerate 4 -i \"{frames}\" -vf \"{scale},split[a][b];[a]palettegen=max_colors=96[p];[b][p]paletteuse=dither=none\" \"{Path.Combine(dir, "timelapse.gif")}\"");
        Console.WriteLine($"autoplay: film {(mp4 ? "timelapse.mp4" : "—")}, {(gif ? "timelapse.gif" : "—")} in {dir}");
    }
}
