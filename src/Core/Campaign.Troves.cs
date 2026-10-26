using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// What a shrine of trial blesses the realm with, held to the end: ours hardier in every valley (a sixth more health),
/// the realm's coffers fuller (4 gold a minute more), the hurt mending twice as fast at every dawn.
/// </summary>
public enum Blessing : byte { None, Hardy, Plenty, Mending }

public sealed partial class MapCell
{
    /// <summary>The treasure of a cell nobody holds (Troves.cs), and whether it was taken; a shrine's blessing, by the seed.</summary>
    public TroveKind Trove;
    public bool TroveTaken;
    public Blessing Blessing;
    /// <summary>A shrine's trial while it goes on in the valley open now: begun, seconds the altar held, waves of the dead sent, seconds nobody of ours at it.</summary>
    public bool TrialOn;
    public double TrialHeld, TrialAway;
    public int TrialWave;
}

public sealed partial class MapParty
{
    /// <summary>
    /// A scouting party: no commander, and no more than three, all of them light — archers, slingers, riders, horse
    /// archers or hounds. It goes out without a commander, walks the map faster and sees two cells round it.
    /// </summary>
    public bool Scouting => Owner == Owner.Ours && Party.Commander == null && Party.Members.Count is > 0 and <= Campaign.ScoutMost
        && Party.Members.TrueForAll(m => Campaign.Light(m.Kind));
}

/// <summary>
/// The treasures of the map (docs/handoff/13-world-map.md, phase 2) and how far it is seen. In cells nobody holds, out
/// past the first ring, lie an abandoned mine kept by the dead, old ruins and a den kept by wolves and bears, and shrines
/// of trial: their keepers fight whoever comes, a quick battle or in the valley; beaten, the chest is ours — gold to the
/// realm, the mine's silver and metal to the first valley — and a shrine held against the dead for three minutes, or its
/// trial fought at once, blesses the realm for good. The horde leaves them be: it neither loots them nor walks through
/// their keepers if it can go round. Our cells with a town hall of the second level see two cells round them, a
/// scouting party of light men two round it.
/// </summary>
public sealed partial class Campaign
{
    /// <summary>How many rings round the first valley the map has: 2 the small map (19 cells), 3 the big one (37).</summary>
    public int Rings = 2;
    /// <summary>What the shrines of trial held to the end have blessed the realm with.</summary>
    public readonly List<Blessing> Blessings = new();

    /// <summary>The most men a scouting party has, and how much of the time a party on foot takes it takes.</summary>
    public const int ScoutMost = 3;
    public const double ScoutPace = .6;
    /// <summary>How long the altar of a shrine is to be held, seconds, and in how many waves the dead come for it.</summary>
    public const double TrialLength = 180;
    public const int TrialWaves = 3;
    /// <summary>How long nobody of ours may stand at the altar before the trial is lost, seconds.</summary>
    const double TrialLeft = 20;
    /// <summary>How near the chest one of ours must come to take it (3 m), and how far round it no keeper may stand (40 m).</summary>
    const float ChestReach = 3 * VoxelWorld.PerMetre, KeptReach = 40 * VoxelWorld.PerMetre;
    /// <summary>A blessing of plenty's gold a minute, and a blessing of hardiness on the health of ours.</summary>
    public const double PlentyGold = 4;
    public const float HardyShare = 1.15f;

    /// <summary>A light man of ours, of those who go scouting.</summary>
    public static bool Light(UnitType t) => t == Kinds.Archer || t == Kinds.Slinger || t == Kinds.Rider || t == Kinds.HorseArcher || t == Kinds.Hound;

    public bool Blessed(Blessing b) => Blessings.Contains(b);

    // ------------------------------------------------------------ laid with the map

    /// <summary>
    /// The treasures laid over the cells nobody holds past the first ring: a third of them, each of the kind its valley
    /// suits (an abandoned mine in an ore valley, a shrine in a holy one, a den in the woods and the mountains, ruins
    /// elsewhere), on the big map one of each kind at least.
    /// </summary>
    void LayTroves(Mulberry32 rnd)
    {
        var free = Cells.FindAll(c => c.Owner == Owner.None && !c.Home && Distance(0, 0, c.Q, c.R) >= 2);
        for (int i = free.Count - 1; i > 0; i--) { int j = (int)(rnd.Next() * (i + 1)); (free[i], free[j]) = (free[j], free[i]); }
        int n = Math.Max(Rings >= 3 ? 4 : 2, free.Count / 3);
        var laid = new List<MapCell>();
        for (int i = 0; i < Math.Min(n, free.Count); i++)
        {
            var c = free[i];
            c.Trove = c.Kind switch
            {
                ValleyKind.Ore => TroveKind.DeadMine,
                ValleyKind.Sacred => TroveKind.Shrine,
                ValleyKind.Forest or ValleyKind.Mountain => TroveKind.Den,
                _ => TroveKind.Ruins,
            };
            laid.Add(c);
        }
        // on the big map, each kind once at least: the kind missing put on a treasure of a kind there is more than one of
        if (Rings >= 3)
            foreach (var k in new[] { TroveKind.DeadMine, TroveKind.Ruins, TroveKind.Den, TroveKind.Shrine })
            {
                if (laid.Exists(c => c.Trove == k)) continue;
                var spare = laid.Find(c => laid.FindAll(o => o.Trove == c.Trove).Count > 1);
                if (spare != null) spare.Trove = k;
            }
        var kinds = new[] { Blessing.Hardy, Blessing.Plenty, Blessing.Mending };
        int b = (int)(rnd.Next() * kinds.Length);
        foreach (var c in laid) if (c.Trove == TroveKind.Shrine) c.Blessing = kinds[b++ % kinds.Length];
    }

    /// <summary>Whether a cell's treasure is still there to be taken.</summary>
    public static bool Untaken(MapCell c) => c.Trove != TroveKind.None && !c.TroveTaken;

    /// <summary>
    /// Who keeps a treasure, the further from the first valley the more: the dead of a mine, the wolves (and a dead or
    /// two) of the ruins, the bears and wolves of a den; a shrine's are the dead that come for its altar, all of its waves.
    /// </summary>
    public static List<UnitType> KeepersOf(TroveKind k, int ring)
    {
        int far = Math.Max(0, ring - 2);
        var list = new List<UnitType>();
        void Add(UnitType t, int n) { for (int i = 0; i < n; i++) list.Add(t); }
        switch (k)
        {
            case TroveKind.DeadMine: Add(Kinds.Skeleton, 6 + 2 * far); break;
            case TroveKind.Ruins: Add(Kinds.Wolf, 4 + 2 * far); Add(Kinds.Skeleton, 2); break;
            case TroveKind.Den: Add(Kinds.Bear, 2 + far); Add(Kinds.Wolf, 4); break;
            case TroveKind.Shrine: Add(Kinds.Skeleton, 12 + 3 * far); break;
        }
        return list;
    }

    /// <summary>What a treasure gives: gold to the realm, metal (cubic metres) to the first valley.</summary>
    public static (double gold, double metal) RewardOf(TroveKind k, int ring)
    {
        int far = Math.Max(0, ring - 2);
        return k switch
        {
            TroveKind.DeadMine => (250 + 120 * far, 1.5 + .5 * far),
            TroveKind.Ruins => (400 + 150 * far, 0),
            TroveKind.Den => (200 + 100 * far, 0),
            _ => (0, 0),
        };
    }

    /// <summary>What a cell's treasure gives, said: «250 золота и 1,5 м³ металла», «благословение: …».</summary>
    public static string RewardText(MapCell c)
    {
        if (c.Trove == TroveKind.Shrine) return $"благословение: {BlessingText(c.Blessing)}";
        var (g, m) = RewardOf(c.Trove, RingOf(c));
        return m > 0 ? $"{g:0} золота и {m:0.#} м³ металла" : $"{g:0} золота";
    }

    public static string BlessingText(Blessing b) => b switch
    {
        Blessing.Hardy => "наши во всех долинах на 15 % крепче",
        Blessing.Plenty => $"казна получает ещё {PlentyGold:0} золота в минуту",
        Blessing.Mending => "раненые на карте заживают вдвое быстрее",
        _ => "",
    };

    /// <summary>The keepers of each treasure, given once as the map is made ready for its turn (the beasts of the other cells are given there too).</summary>
    void KeepTroves()
    {
        foreach (var c in Cells)
        {
            if (!Untaken(c) || c.Sum.Keep.Count > 0) continue;
            foreach (var t in KeepersOf(c.Trove, RingOf(c))) c.Sum.Keep.Add(Raider(t));
        }
    }

    // ------------------------------------------------------------ taken

    /// <summary>
    /// The treasure of a cell taken by ours, its keepers beaten: gold to the realm (the open valley's stores, while one
    /// is), metal to the first valley's (waiting for its stores if it is not the one open), a shrine's blessing on the
    /// realm. Said; null if there was nothing to take.
    /// </summary>
    public string TakeTrove(MapCell c, MapParty by = null)
    {
        if (!Untaken(c)) return null;
        c.TroveTaken = true;
        c.TrialOn = false;
        int ring = RingOf(c);
        string said;
        if (c.Trove == TroveKind.Shrine)
        {
            c.Sum.Keep.Clear();
            if (c.Blessing != Blessing.None && !Blessings.Contains(c.Blessing)) Blessings.Add(c.Blessing);
            if (c.Blessing == Blessing.Hardy && InValley && Live != null) Bless(Live);
            said = $"Испытание выдержано. Благословение: {BlessingText(c.Blessing)}.";
        }
        else
        {
            var (gold, metal) = RewardOf(c.Trove, ring);
            if (InValley && Live != null) Live.Stock.Gold += gold; else Gold += gold;
            if (metal > 0)
            {
                if (InValley && Live != null && Here == Home) Live.Stock[Stockpile.Metal] += metal * VoxelWorld.PerM3;
                else if (Home != null) Home.Sum.Metal += metal;
            }
            said = $"{Troves.Name(c.Trove)} {Names(c).prep}: сундук наш, {RewardText(c)}.";
        }
        Tell(NewsKind.Taken, said, c, by);
        return said;
    }

    /// <summary>The blessing of hardiness on a valley: ours in it a sixth hardier, and those to come into it.</summary>
    public void Bless(Sim sim)
    {
        if (!Blessed(Blessing.Hardy) || sim.OursHardy >= HardyShare) return;
        float k = HardyShare / sim.OursHardy;
        sim.OursHardy = HardyShare;
        foreach (var u in sim.Units)
            if (!u.Dead && u.Team == Team.Player && !Kinds.IsEngine(u.T)) { u.MaxHp *= k; u.Hp *= k; }
    }

    // ------------------------------------------------------------ in the valley open now

    /// <summary>
    /// The treasure of the valley we play in, a frame: a chest taken by the first of ours to come to it with no keeper left
    /// near it, its boards carried off; a shrine's trial begun when one of ours stands at the altar, the dead come in their
    /// waves, held to the end it blesses the realm, left it is lost. True if anything was said.
    /// </summary>
    public bool TroveTick(Sim sim, MapCell cell, double dt)
    {
        if (cell == null || !Untaken(cell) || sim.Site.Trove == TroveKind.None) return false;
        var s = sim.Site;
        if (cell.Trove == TroveKind.Shrine) return TrialTick(sim, cell, dt);
        foreach (var u in sim.Units)
            if (!u.Dead && u.Team == Team.Wild && u.Guard && u.T.Aggro > 0 && Flat(u, s.TroveX, s.TroveZ) < KeptReach) return false;
        Unit taker = null;
        foreach (var u in sim.Units)
            if (Ours(u) && Flat(u, s.TroveX, s.TroveZ) < ChestReach + 4 && Math.Abs(u.Y - s.TroveY) < 6) { taker = u; break; }
        if (taker == null) return false;
        // the chest carried off
        for (int y = s.TroveY; y < s.TroveY + 5; y++)
            for (int z = s.TroveZ - 4; z <= s.TroveZ + 4; z++)
                for (int x = s.TroveX - 4; x <= s.TroveX + 4; x++)
                    if (sim.World.InBounds(x, y, z) && sim.World.Get(x, y, z) is Mat.Crate or Mat.BoardDark or Mat.Brass or Mat.Iron) sim.World.Set(x, y, z, Mat.Air);
        cell.Sum.Keep.Clear();
        return TakeTrove(cell) != null;
    }

    static bool Ours(Unit u) => !u.Dead && u.Team == Team.Player && u.T != Kinds.Risen && !u.Inside;

    static float Flat(Unit u, float x, float z)
    {
        float dx = u.X - x, dz = u.Z - z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>The dead come for a shrine's altar while its trial goes on (not its keepers: they keep no spot).</summary>
    public static bool Spirit(Unit u) => !u.Dead && u.T == Kinds.Skeleton && !u.Guard;

    bool TrialTick(Sim sim, MapCell cell, double dt)
    {
        var s = sim.Site;
        int holders = 0;
        Unit first = null;
        foreach (var u in sim.Units)
            if (Ours(u) && Flat(u, s.TroveX, s.TroveZ) < Troves.AltarReach) { holders++; first ??= u; }
        if (!cell.TrialOn)
        {
            if (holders == 0) return false;
            cell.TrialOn = true; cell.TrialHeld = 0; cell.TrialAway = 0; cell.TrialWave = 0;
            sim.Say($"Испытание началось: держите людей у алтаря {TrialLength / 60:0} минуты. Мертвецы придут {TrialWaves} раза.", 2, first);
            return true;
        }
        if (holders > 0) { cell.TrialHeld += dt; cell.TrialAway = 0; }
        else cell.TrialAway += dt;
        if (cell.TrialAway > TrialLeft)
        {
            TrialReset(sim, cell);
            sim.Say("У алтаря никого нет. Испытание провалено, его можно начать снова.", 3);
            return true;
        }
        // the dead in their waves: the next when its share of the time is held
        bool said = false;
        if (cell.TrialWave < TrialWaves && cell.TrialHeld >= cell.TrialWave * TrialLength / TrialWaves)
        {
            var roster = cell.Sum.Keep;
            int from = roster.Count * cell.TrialWave / TrialWaves, to = roster.Count * (cell.TrialWave + 1) / TrialWaves;
            cell.TrialWave++;
            int came = 0;
            for (int i = from; i < to; i++)
            {
                var (px, _, pz) = s.TrovePosts.Count > 0 ? s.TrovePosts[(i * 5 + cell.TrialWave) % s.TrovePosts.Count] : (s.TroveX + 80, -1, s.TroveZ);
                int at = sim.NearestStand(px + sim.Rng.Next(-8, 9), pz + sim.Rng.Next(-8, 9), roster[i].Kind.Move.Height);
                if (at < 0) continue;
                var u = sim.Spawn(roster[i].Kind, at);
                u.Home = sim.StandNear(s.TroveX, s.TroveY, s.TroveZ, 2);
                // set on the nearest of ours at the altar, and on it for a long while
                Unit near = null; float nd = float.MaxValue;
                foreach (var o in sim.Units) if (Ours(o) && Sim.Dist(u, o) is var d && d < nd) { nd = d; near = o; }
                u.Target = near; u.LastHurt = sim.Time + 60;
                came++;
            }
            sim.Say($"Мертвецы встают у святилища: {came}. Волна {cell.TrialWave} из {TrialWaves}.", 3, first);
            said = true;
        }
        // held to the end, the last of the dead down
        if (cell.TrialHeld >= TrialLength && cell.TrialWave >= TrialWaves)
        {
            bool left = false;
            foreach (var u in sim.Units) if (Spirit(u)) { left = true; break; }
            if (!left) return TakeTrove(cell) != null;
        }
        return said;
    }

    /// <summary>A shrine's trial ended unfinished (lost, or the valley left): the dead that came for it crumble, it can be begun again.</summary>
    public void TrialReset(Sim sim, MapCell cell)
    {
        cell.TrialOn = false; cell.TrialHeld = 0; cell.TrialAway = 0; cell.TrialWave = 0;
        foreach (var u in sim.Units.ToArray()) if (Spirit(u)) sim.Hurt(u, null, 1e6f, "рассыпался");
    }

    // ------------------------------------------------------------ how far it is seen

    /// <summary>
    /// What of the map we see: round every cell of ours, the one we are in, those our parties stand in or are halfway into,
    /// the next cells; two cells round ours with a town hall of the second level and round a scouting party, further
    /// round a party whose hero has Разведка. The rest lies in the fog.
    /// </summary>
    public bool Seen(MapCell c)
    {
        // on the big map what was once seen stays seen, as in «Heroes» (MapThing.cs)
        if (Big) return RevealAll || Explored(c);
        if (RevealAll || Scouted(c)) return true;
        foreach (var o in Cells)
        {
            int reach = Sight(o);
            if (reach > 0 && Distance(o.Q, o.R, c.Q, c.R) <= reach) return true;
        }
        return false;
    }

    /// <summary>The whole map seen, the fog lifted (for pictures; not kept).</summary>
    public bool RevealAll;

    /// <summary>How many cells round a cell are seen from it: 0, 1 or 2.</summary>
    public int Sight(MapCell o)
    {
        int r = 0;
        if (o.Owner == Owner.Ours || o == Here) r = HallOf(o) >= 2 ? 2 : 1;
        foreach (var p in Parties)
        {
            if (p.Owner != Owner.Ours) continue;
            bool at = p.Q == o.Q && p.R == o.R || p.Moving && p.Path[0] == o && StepShare(p) >= .5;
            if (at) r = Math.Max(r, p.Scouting ? 2 : 1);
        }
        return r;
    }

    /// <summary>The level of a cell's town hall: the open valley's as it stands, another's as the map keeps it.</summary>
    int HallOf(MapCell o) => o == Here && InValley && Live != null ? _hereHall : o.Sum.Hall;

    int _hereHall;
    double _hallLook;

    /// <summary>The open valley's town hall looked at now and then (it is read each frame the board is drawn).</summary>
    void LookAtHall(double dt)
    {
        if (Live == null || !InValley) return;
        if ((_hallLook -= dt) > 0) return;
        _hallLook = 2;
        _hereHall = Live.HallLevel();
    }
}
