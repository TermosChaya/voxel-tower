using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using VoxelTower.Core;
using Holder = VoxelTower.Core.Owner;

namespace VoxelTower.Game;

/// <summary>
/// The campaign in play: the map of valleys (Campaign.cs) held while the scene begins again for each valley gone into.
/// The valley on the screen is the cell we are in; the map runs with it on one clock (Campaign.Turn.cs: parties walk,
/// the horde musters at dawn and sends its armies, the valleys we are not in pay), and the valley's stores hold the
/// realm's gold while we are in it. Ours sent out over an edge go onto the map toward the cell they were sent to;
/// arrived, they go into its valley (the scene begins again on it, they come in by the side they came over) or fight it
/// out at once. An army of the horde walking into the valley we are in is a raid; what is left of it goes home after.
/// The horde's town beaten is nobody's, a valley nobody holds is ours once its outpost stands; the last of ours gone out
/// of a valley, the game goes back to the first one with the board open.
/// </summary>
public partial class Main
{
    /// <summary>The campaign in play, null outside one; and its slot. Kept while the scene begins again.</summary>
    public static Campaign Atlas;
    public static string CampSlot;
    /// <summary>A party of the map to come into the valley the scene opens on, and whether the board opens with it.</summary>
    static MapParty _entering;
    static bool _boardAtStart;
    /// <summary>--campaign[=seed]: a new campaign at once, in the third slot (screenshots, tests).</summary>
    bool _campaignArg;

    /// <summary>Where each side's party sent out of this valley is going, till it is over the edge; whether the board opens on it then (it went out in first person).</summary>
    readonly MapCell[] _outbound = new MapCell[6];
    readonly bool[] _boardOnOut = new bool[6];
    /// <summary>The horde's armies of the map brought into the valley as the raid now in it.</summary>
    readonly List<MapParty> _raiding = new();
    /// <summary>Seconds the horde's town has had nobody of the horde standing in it; seconds the valley has been empty of ours.</summary>
    double _clearFor, _emptyFor, _outpostLook;
    bool _campaignOver;

    /// <summary>Whether this scene is a campaign's: then the valley's seed, kind and sides are its cell's, and a valley we were in is read back from its file.</summary>
    bool CampaignValley()
    {
        if (Atlas == null && _campaignArg)
        {
            // (the map's content staged on the big map)
            Atlas = Campaign.New(_seed, _pose is "troves" or "trove" ? 3 : 2);
            CampSlot = Saves.CampaignSlots[2];
        }
        CampaignPoseBefore();
        if (Atlas?.Here is not { } cell) return false;
        var mp = Atlas.ParamsOf(cell);
        _seed = mp.Seed; _valley = mp.Kind; _classic = false; _hexagon = true; _size = 0;
        _entry = mp.EntrySide; _holding = mp.Holding; _edges = mp.Edges; _trove = mp.Trove; _troveTaken = mp.TroveTaken;
        string file = Path.Combine(Saves.CampaignDir(CampSlot), cell.File);
        if (cell.Visited && File.Exists(file)) PendingLoad = file;
        return true;
    }

    /// <summary>
    /// The valley of the campaign set going, the sim made (and read back, if we were here): it runs by the map's clock and
    /// holds the realm's gold, and gets what the map kept for it (its stores, its keepers, those of ours fallen away). A
    /// new first valley finds its household and the commanders; a valley nobody holds, its beasts; a party come from the
    /// map walks in by its side.
    /// </summary>
    void CampaignStart(string extra)
    {
        var cell = Atlas.Here;
        bool fresh = !_loaded;
        Sim.Campaign = true;
        if (_loaded) Saves.Apply(this, extra);
        else
        {
            Sim.SetSource(new MapRaids());
            // the siege's first raid, drawn up as the valley was made, is not the map's
            Sim.Bands.RemoveAll(b => b.State == BandState.Coming);
            if (cell.Home)
            {
                foreach (var t in new[] { Kinds.Worker, Kinds.Worker, Kinds.Worker, Kinds.Worker, Kinds.Archer, Kinds.Swordsman }) Sim.Spawn(t, Sim.Gate);
                for (int i = 0; i < Campaign.StartCommanders; i++) Sim.Spawn(Kinds.Captain, Sim.Gate);
            }
            Sim.SeedWildlife();
        }
        _repelledSeen = Sim.Raids.Repelled;
        Hud.Unfold.OpenAll();
        // (the keepers of the map given before its first turn, if this is the first valley it ever opened on)
        Atlas.Prepare();
        Atlas.Enter(Sim);
        Atlas.Settle(cell, Sim);
        // the beasts that keep a valley nobody holds are the map's: they stand at its middle when we first come; the keepers
        // of its treasure at the treasure (the dead of a shrine come only when its altar is held)
        if (fresh && cell.Owner == Holder.None && !(Campaign.Untaken(cell) && cell.Trove == TroveKind.Shrine))
        {
            bool trove = Campaign.Untaken(cell) && Site.TrovePosts.Count > 0;
            int k = 0;
            foreach (var m in cell.Sum.Keep)
            {
                var (px, py, pz) = trove ? Site.TrovePosts[k++ % Site.TrovePosts.Count] : (Site.KeepX, -1, Site.KeepZ);
                int spread = trove ? 4 : 60, ox = px + Sim.Rng.Next(-spread, spread + 1), oz = pz + Sim.Rng.Next(-spread, spread + 1);
                int at = py >= 0 ? Sim.StandNear(ox, py, oz, m.Kind.Move.Height) : Sim.NearestStand(ox, oz, m.Kind.Move.Height);
                if (at < 0) continue;
                var u = Sim.Spawn(m.Kind, at);
                u.Guard = true; u.Home = at;
                // (hurt as a battle counted on the map left them)
                u.Hp = Math.Min(u.MaxHp, m.Hp);
            }
        }
        if (_entering != null)
        {
            var p = _entering;
            _entering = null;
            Atlas.Parties.Remove(p);
            Come(p.Party, Atlas.EntrySideOf(p), select: true);
        }
        string where = cell.Home ? "первая долина" : Campaign.Names(cell).nom;
        Hud.Log($"Кампания, день {Atlas.Clock.Day}: {where}. {{Board}} — карта долин.", Hud.Kind.Ok);
        if (cell.Owner == Holder.Horde) Hud.Log(cell.Holding == Holding.HordeTown ? "Логово орды. Перебейте гарнизон." : "Долина во власти орды. Перебейте её гарнизон.", Hud.Kind.Warn);
        else if (cell.Owner == Holder.None) Hud.Log("Долина ничья. Поставьте заставу: {Board}, «Поставить заставу». Когда башня встанет, долина ваша.", Hud.Kind.Ok);
        if (_boardAtStart) { _boardAtStart = false; Hud.Board?.Open(); }
    }

    /// <summary>A party of ours walks into the valley by a side; chosen, the camera goes to it.</summary>
    void Come(Party party, int side, bool select)
    {
        var come = Sim.Arrive(party, side);
        if (come.Count == 0 || !select) return;
        float cx = 0, cz = 0;
        Selected.Clear();
        foreach (var u in come) { cx += u.X / come.Count; cz += u.Z / come.Count; Selected.Add(u); }
        Rig.SetAt(World.WX(cx), World.WY(World.SurfAt((int)cx, (int)cz)), World.WZ(cz), Rig.Aim.yaw, .62f, 1.6f);
        Hud.OnSelectionChanged();
    }

    /// <summary>
    /// The map's part of a frame, the valley having run so many seconds: the map runs as long on the same clock; those
    /// gone over an edge go onto the map; the horde's armies come in as raids and what is left of them goes home; parties
    /// of ours come in; the news is told; the valley taken, lost, or left empty.
    /// </summary>
    public void CampaignTick(double dt)
    {
        if (Atlas == null || _campaignOver || Sim == null) return;
        var here = Atlas.Here;
        Atlas.Advance(dt);
        // one clock: the valley's is the map's while we are in it (a raid holding the night holds the map's too)
        Atlas.Clock = Sim.Clock.Now;
        Atlas.Gold = Sim.Stock.Gold;
        // a party gone over an edge goes onto the map whole, once the last of it is over
        foreach (var p in Sim.Departed.ToArray())
        {
            if (Sim.Units.Exists(u => !u.Dead && u.LeaveBy == p.Side)) continue;
            Sim.Departed.Remove(p);
            var to = _outbound[p.Side];
            bool open = _boardOnOut[p.Side];
            _outbound[p.Side] = null; _boardOnOut[p.Side] = false;
            var mp = Atlas.Out(p, to);
            Hud.Log(to != null && mp.Moving ? $"Отряд идёт по карте: {Campaign.Names(to).nom}, {Fmt.Clock(Atlas.WalkLeft(mp))}." : "Отряд вышел на карту.", Hud.Kind.Ok);
            if (open && Hud.Board != null) { Hud.Board.Open(); Hud.Board.ChooseParty(mp); }
        }
        while (Atlas.TakeRaid(out var enemy, out int side))
        {
            Sim.Raid(enemy.Roster(), side, lord: enemy.Lord);
            _raiding.Add(enemy);
        }
        // the raid over: those who got away go home on the map
        if (_raiding.Count > 0 && !Sim.Raids.Active)
        {
            var away = new List<UnitType>();
            bool lordAway = false;
            foreach (var s in Sim.Escaped) { if (s.Lord != null) lordAway = true; else away.Add(s.Kind); }
            Sim.Escaped.Clear();
            for (int i = 0; i < _raiding.Count; i++) Atlas.RaidOver(_raiding[i], i == 0 ? away : new List<UnitType>(), i == 0 && lordAway);
            _raiding.Clear();
        }
        while (Atlas.TakeArrival(out var ours, out int from)) Come(ours.Party, from, select: false);
        // the valley's treasure: a chest taken, a shrine's trial going on
        if (Atlas.TroveTick(Sim, here, dt)) Hud.Board?.Refresh();
        bool told = Tell();
        Hold(here, dt);
        if (Sim.Lost && here.Owner == Holder.Ours)
        {
            Atlas.Fall(here);
            Hud.Log(here.Home ? $"Первая долина потеряна. Займите другую долину заставой за {Campaign.DaysToRetake} дня, иначе кампания проиграна." : "Долина потеряна.", Hud.Kind.Bad);
            told = true;
        }
        if (Atlas.Defeated || Atlas.Won)
        {
            _campaignOver = true;
            if (Atlas.Won) Hud.Finale("Победа", $"Логово орды взято на {Atlas.Clock.Day}-й день.", true);
            else Hud.Finale("Кампания проиграна", $"Первая долина пала, другой за {Campaign.DaysToRetake} дня не занято. День {Atlas.Clock.Day}.", false);
        }
        if (told) Hud.Board?.Refresh();
        // the last of ours gone out of a valley not the first: back to the first, the board open
        bool any = Sim.Units.Exists(u => !u.Dead && u.Team == Team.Player && u.T != Kinds.Risen);
        _emptyFor = !here.Home && !any && Sim.Departed.Count == 0 ? _emptyFor + Math.Max(dt, .02) : 0;
        if (_emptyFor > 2 && !_selfTest) GoHome();
    }

    /// <summary>The map's news into the chronicle; a party of ours come to where it was going (or stopped by the enemy) gets its card. True if anything was told.</summary>
    bool Tell()
    {
        if (Atlas.News.Count == 0) return false;
        foreach (var n in Atlas.News)
        {
            var kind = n.Kind switch
            {
                NewsKind.Lost or NewsKind.RaidComing => Hud.Kind.Bad,
                NewsKind.Taken when n.Cell?.Owner == Holder.Horde => Hud.Kind.Bad,
                NewsKind.Taken or NewsKind.Arrived => Hud.Kind.Ok,
                NewsKind.Grew => Hud.Kind.Warn,
                _ => Hud.Kind.Plain,
            };
            Hud.Log(n.Text, kind, toast: n.Kind != NewsKind.Grew);
            if (n.Kind == NewsKind.Arrived && n.Party is { Owner: Holder.Ours } p && Atlas.Parties.Contains(p) && !p.Moving) Hud.Board?.Arrived(p);
            if (n.Kind == NewsKind.RaidComing) Hud.Float("войско орды идёт на нашу долину", Ornament.Blood);
        }
        Atlas.News.Clear();
        return true;
    }

    /// <summary>
    /// Whose the valley is while we are in it: the horde's with nobody of the horde left standing for a while, it is taken
    /// from the horde (the first valley ours again, the lair the campaign won, any other nobody's); nobody's with its
    /// outpost standing and nobody hostile left, ours.
    /// </summary>
    void Hold(MapCell here, double dt)
    {
        bool horde = Sim.Units.Exists(u => !u.Dead && u.Team == Team.Horde && !u.Routed && !u.Envoy && !u.Bound && u.Captor == null);
        if (here.Owner == Holder.Horde && !Sim.Lost)
        {
            _clearFor = horde || Sim.Raids.Active ? 0 : _clearFor + dt;
            if (_clearFor >= 3)
            {
                _clearFor = 0;
                Atlas.Summarise(here, Sim);
                Tell();
                // the garrison beaten: to the credit of the heroes who took the valley
                if (here.Owner != Holder.Horde) Sim.GloryAll(Heroes.TakeXp, "долина взята");
                if (here.Owner != Holder.Horde)
                    Hud.Log(here.Owner == Holder.Ours ? "Первая долина снова наша." : "Гарнизон разбит. Долина ничья: поставьте заставу, {Board}, «Поставить заставу».", Hud.Kind.Ok);
                Hud.Board?.Refresh();
            }
        }
        if (here.Owner == Holder.None && here.Outpost && (_outpostLook -= dt) <= 0)
        {
            _outpostLook = 5;
            bool beasts = Sim.Units.Exists(u => !u.Dead && u.Team == Team.Wild && u.Guard);
            if (!horde && !beasts && Sim.OutpostStands())
            {
                Atlas.OutpostStands(here);
                Sim.GloryAll(Heroes.TakeXp, "застава встала");
                Tell();
                Hud.Board?.Refresh();
            }
        }
    }

    /// <summary>
    /// What the map keeps of a valley nobody holds as we leave it: the beasts and the keepers of its treasure still
    /// standing; a shrine's trial left unfinished is lost, its dead crumble, and the dead it sends stay the map's.
    /// </summary>
    void KeepersLeft()
    {
        var here = Atlas.Here;
        if (here.Owner != Holder.None) return;
        if (Campaign.Untaken(here) && here.Trove == TroveKind.Shrine) { Atlas.TrialReset(Sim, here); return; }
        here.Sum.Keep.Clear();
        foreach (var u in Sim.Units) if (!u.Dead && u.Team == Team.Wild && u.Guard) here.Sum.Keep.Add(Traveller.Of(u));
    }

    // ------------------------------------------------------------ what the player does

    /// <summary>
    /// The chosen of ours sent out of the valley toward a cell of the map: over the side facing it, then onto the map
    /// walking there. A party goes with a commander. Null, or why not.
    /// </summary>
    public string SendOut(MapCell to)
    {
        if (Atlas == null || to == null) return "нет кампании";
        if (to == Atlas.Here) return "отряд уже в этой долине";
        var group = new List<Unit>();
        foreach (var u in Selected) if (!u.Dead && u.Team == Team.Player && u.T != Kinds.Risen) group.Add(u);
        if (group.Count == 0) return "выберите в долине командира и бойцов";
        if (!group.Exists(u => u.T == Kinds.Captain) && !Scouts(group)) return $"в отряде нужен командир, или до {Campaign.ScoutMost} лёгких бойцов в разведку";
        int side = Campaign.SideToward(Atlas.Here, to.Q, to.R);
        int n = Sim.Depart(group, side);
        if (n == 0) return "отряду не дойти до края долины";
        _outbound[side] = to;
        return null;
    }

    /// <summary>A scouting party, chosen in the valley: no commander, up to three light men (archers, slingers, riders, horse archers, hounds).</summary>
    public static bool Scouts(List<Unit> group) =>
        group.Count is > 0 and <= Campaign.ScoutMost && group.TrueForAll(u => u.T != Kinds.Captain && Campaign.Light(u.T));

    /// <summary>How near a side the one gone into must stand to be offered the map (the horde's strip and 2 m more), and how far about him those go who go with him (20 m, as far as «За мной!» calls).</summary>
    const float EdgeNear = Site.StripDepth + 2 * VoxelWorld.PerMetre, WithHim = 20 * VoxelWorld.PerMetre;

    /// <summary>The side of the valley the one gone into stands at in a campaign, a cell of the map beyond it; -1 if none.</summary>
    public int EdgeAt(Unit u)
    {
        if (Atlas?.Here is not { } here || u == null || u.Dead || u.Inside) return -1;
        var hex = Site.Hex ?? Hex.In(World.SX, World.SZ);
        for (int s = 0; s < 6; s++)
        {
            var (d, t) = hex.FromSide(s, u.X, u.Z);
            if (d <= EdgeNear && t >= 0 && t <= 1 && Atlas.Neighbour(here, s) != null) return s;
        }
        return -1;
    }

    /// <summary>Those who go out with the one gone into: he, and ours within 20 m of him standing free (not inside, not down, not held).</summary>
    public List<Unit> PartyAbout(Unit u)
    {
        var list = new List<Unit> { u };
        foreach (var o in Sim.Units)
        {
            if (o == u || o.Dead || o.Team != Team.Player || o.T == Kinds.Risen || o.Inside || o.Down || o.Captor != null || o.CarriedBy != null) continue;
            float dx = o.X - u.X, dz = o.Z - u.Z;
            if (dx * dx + dz * dz <= WithHim * WithHim) list.Add(o);
        }
        return list;
    }

    /// <summary>What first person offers at the valley's edge: where the side leads and how many go; empty away from an edge.</summary>
    public string EdgeOffer(Unit u)
    {
        int side = EdgeAt(u);
        if (side < 0) return "";
        var to = Atlas.Neighbour(Atlas.Here, side);
        int n = PartyAbout(u).Count;
        return Bindings.Fill($"Край долины. За ним {Campaign.Names(to).nom}. {{FpLeave}} — уйти на карту: {n} {Sim.Plural(n, "человек", "человека", "человек")}");
    }

    /// <summary>
    /// The one gone into walks out over the side he stands at, those about him with him, toward the cell beyond: the
    /// eyes let go, they go over the edge and onto the map, and the board opens on them. Null, or why not.
    /// </summary>
    public string LeaveInPerson()
    {
        var u = Sim.Possessed;
        int side = EdgeAt(u);
        if (side < 0) return "подойдите к краю долины";
        var to = Atlas.Neighbour(Atlas.Here, side);
        var group = PartyAbout(u);
        if (!group.Exists(o => o.T == Kinds.Captain)) return "в отряде нужен командир: войдите в него или подведите его";
        FP.Exit();
        Selected.Clear();
        Hud.OnSelectionChanged();
        if (Sim.Depart(group, side) == 0) return "отряду не дойти до края долины";
        _outbound[side] = to;
        _boardOnOut[side] = true;
        return null;
    }

    /// <summary>
    /// A party of the map goes into the valley of its cell: the valley we are in written into the campaign's folder with
    /// the map, and the scene begun again on the cell, the party coming in by its side.
    /// </summary>
    public void EnterCell(MapParty p)
    {
        if (Atlas == null || p == null) return;
        var cell = Atlas.CellOf(p);
        KeepersLeft();
        if (!Saves.WriteCampaign(this, CampSlot, Atlas, Atlas.Here)) return;
        _entering = p;
        Atlas.HereQ = cell.Q; Atlas.HereR = cell.R;
        Atlas.Write(Saves.CampaignDir(CampSlot));
        GetTree().ReloadCurrentScene();
    }

    /// <summary>A party fights it out at once, by the map's reckoning, without going in.</summary>
    public QuickResult QuickFight(MapParty p)
    {
        var r = Atlas.FightHere(p);
        Tell();
        Hud.Board?.Refresh();
        return r;
    }

    /// <summary>The outpost of this valley marked for the workers. Null, or why not.</summary>
    public string RaiseOutpost()
    {
        var here = Atlas?.Here;
        if (here == null || here.Home || here.Owner == Holder.Horde) return "застава ставится в ничьей или своей долине";
        if (here.Outpost) return "застава уже размечена";
        string why = Sim.RaiseOutpost();
        if (why != null) return why;
        here.Outpost = true;
        _outpostLook = 0;
        bool hands = Sim.Units.Exists(u => !u.Dead && u.Team == Team.Player && u.T == Kinds.Worker);
        Hud.Log(hands ? "Застава размечена: каменная башня у камня. Когда рабочие её построят, долина ваша." : "Застава размечена. Строить некому: приведите рабочих с отрядом.", hands ? Hud.Kind.Ok : Hud.Kind.Warn);
        Hud.Board?.Refresh();
        return null;
    }

    /// <summary>Back to the first valley, the board open: the valley we leave written with the map.</summary>
    public void GoHome()
    {
        if (Atlas == null) return;
        KeepersLeft();
        if (!Saves.WriteCampaign(this, CampSlot, Atlas, Atlas.Here)) return;
        var home = Atlas.Home;
        Atlas.HereQ = home.Q; Atlas.HereR = home.R;
        Atlas.Write(Saves.CampaignDir(CampSlot));
        _boardAtStart = true;
        GetTree().ReloadCurrentScene();
    }

    /// <summary>A new campaign in a slot (whatever was in it gone), begun on its first valley; its map of 3 rings (37 valleys) or 2 (19).</summary>
    public static void NewCampaign(SceneTree tree, string slot, uint seed, int rings = 3)
    {
        string dir = Saves.CampaignDir(slot);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Atlas = Campaign.New(seed, rings);
        CampSlot = slot;
        Atlas.Write(dir);
        _entering = null; _boardAtStart = false;
        PendingStart = null; PendingLoad = null;
        tree.ReloadCurrentScene();
    }

    /// <summary>A campaign read back from its slot, on the valley it was in; false if it cannot be read.</summary>
    public static bool LoadCampaign(SceneTree tree, string slot)
    {
        try { Atlas = Campaign.Read(Saves.CampaignDir(slot)); }
        catch (Exception e) { GD.PrintErr($"[voxel-tower] could not read the campaign {slot}: {e.Message}"); return false; }
        CampSlot = slot;
        _entering = null; _boardAtStart = false;
        PendingStart = null; PendingLoad = null;
        tree.ReloadCurrentScene();
        return true;
    }

    /// <summary>The campaign written to its slot as it stands.</summary>
    public bool SaveCampaign()
    {
        if (Atlas == null) return false;
        KeepersLeft();
        return Saves.WriteCampaign(this, CampSlot, Atlas, Atlas.Here);
    }

    /// <summary>Out of the campaign: a siege, the sandbox, the tutorial or a single valley's save is begun instead.</summary>
    public static void LeaveCampaign() { Atlas = null; CampSlot = null; _entering = null; _boardAtStart = false; }
}
