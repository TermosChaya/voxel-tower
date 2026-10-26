using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;
using Holder = VoxelTower.Core.Owner;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// What stands in the middle of a valley of the map (--pose=holding with --holding=outpost or --holding=horde): the
    /// outpost's place, levelled and marked out round its stone, or the horde's town and its garrison at their posts; seen
    /// from the side the enemy comes by. --look=1 nearer.
    /// </summary>
    void HoldingPose()
    {
        var w = World; var s = Site; var sim = Sim;
        sim.Waves.Countdown = 1e9;
        sim.WildTarget = 0;
        Advance(2);
        var (ix, iz) = (s.Hex ?? Hex.In(w.SX, w.SZ)).Inward(s.EntrySide);
        float zoom = s.Holding == Holding.HordeTown ? 1.2f : 1.7f;
        Rig.SetAt(w.WX(s.KeepX), w.WY(s.KeepBase), w.WZ(s.KeepZ), MathF.Atan2(ix, iz) + .5f, .62f, _look == 1 ? zoom * 1.8f : zoom);
    }

    /// <summary>
    /// A party of ours coming into the valley over a side (--pose=arrive, --look=0…5 the side, the east by itself): ten of
    /// them with their commander, a swordsman hurt, walking in from the edge in a file and forming up some way in; seen
    /// from inland (--at= seconds after they came, 9 by itself).
    /// </summary>
    void ArrivePose()
    {
        var w = World; var s = Site; var sim = Sim;
        sim.Waves.Countdown = 1e9;
        sim.WildTarget = 0;
        int side = _look is >= 0 and < 6 ? _look : Hex.East;
        var hex = s.Hex ?? Hex.In(w.SX, w.SZ);
        var party = new Party();
        foreach (var k in new[] { Kinds.Swordsman, Kinds.Spearman, Kinds.Archer, Kinds.Swordsman, Kinds.Captain, Kinds.Spearman, Kinds.Archer, Kinds.Swordsman, Kinds.Worker, Kinds.Worker })
            party.Members.Add(new Traveller { Kind = k, Look = 11 + party.Members.Count * 13, Hp = k.Hp, MaxHp = k.Hp });
        party.Members[3].Hp = party.Members[3].MaxHp * .45f;
        // (the woods about the way in cleared, so they are to be seen)
        var (mx, mz) = hex.SidePoint(side, .5f, 20 * VoxelWorld.PerMetre);
        for (int z = (int)mz - 200; z < (int)mz + 200; z++)
            for (int x = (int)mx - 200; x < (int)mx + 200; x++)
                if (w.OnLand(x, z))
                    for (int y = w.SurfAt(x, z); y < w.SY; y++)
                        if (Mat.IsTrunk(w.Get(x, y, z)) || Mat.IsLeafy(w.Get(x, y, z))) w.Set(x, y, z, Mat.Air);
        var come = sim.Arrive(party, side);
        Advance(_poseSeconds == 14 ? 9 : _poseSeconds);
        float cx = 0, cz = 0;
        foreach (var u in come) { cx += u.X / come.Count; cz += u.Z / come.Count; }
        var (ix, iz) = hex.Inward(side);
        Rig.SetAt(w.WX(cx), w.WY(w.SurfAt((int)cx, (int)cz)), w.WZ(cz), MathF.Atan2(-ix, -iz) + .5f, .55f, 2f);
    }

    /// <summary>The treasure staged by --look: 0 the abandoned mine, 1 the ruins, 2 the den, 3 the shrine's trial.</summary>
    TroveKind PoseTrove => _look switch { 1 => TroveKind.Ruins, 2 => TroveKind.Den, 3 => TroveKind.Shrine, _ => TroveKind.DeadMine };

    /// <summary>
    /// --campaign --pose=trove --look=0…3: the scene opens on a treasure's valley of the big map, a party of ours of twelve
    /// come in by the side facing the first valley (set before the valley is made).
    /// </summary>
    void TrovePoseBefore()
    {
        var cell = Atlas.Cells.Find(c => c.Trove == PoseTrove) ?? Atlas.Cells.Find(c => c.Trove != TroveKind.None);
        Atlas.HereQ = cell.Q; Atlas.HereR = cell.R;
        var back = Atlas.Neighbour(cell, Campaign.SideToward(cell, 0, 0));
        var p = new MapParty { Q = cell.Q, R = cell.R, Owner = Holder.Ours, CameQ = back.Q, CameR = back.R,
            Party = PartyOf(Kinds.Captain, Kinds.Swordsman, Kinds.Swordsman, Kinds.Swordsman, Kinds.Spearman, Kinds.Spearman, Kinds.Shieldbearer,
                Kinds.Archer, Kinds.Archer, Kinds.Archer, Kinds.Swordsman, Kinds.Spearman) };
        Atlas.Parties.Add(p);
        _entering = p;
    }

    /// <summary>
    /// A treasure's valley (--pose=trove): the abandoned mine, the ruins or the den with their keepers at their spots,
    /// seen from before the mouth or the doorway, ours come near (--look=0…2); the shrine's trial going on, ours at the
    /// altar and the dead come for it (--look=3, --at= seconds, 40 by itself).
    /// </summary>
    void TrovePose()
    {
        var s = Site; var w = World;
        Sim.WildTarget = 0;
        // (in the light of the late morning, nobody chosen)
        Sim.SetClock(new ClockTime(Atlas.Clock.Day, DayClock.DayLength * .35));
        Atlas.Clock = Sim.Clock.Now;
        Selected.Clear();
        Hud.OnSelectionChanged();
        var ours = Sim.Units.FindAll(u => !u.Dead && u.Team == Team.Player);
        int chest = Sim.StandNear(s.TroveX, s.TroveY, s.TroveZ, 2);
        float dx = s.KeepX - s.TroveX, dz = s.KeepZ - s.TroveZ, dl = MathF.Max(1, MathF.Sqrt(dx * dx + dz * dz));
        dx /= dl; dz /= dl;
        if (s.Trove == TroveKind.Shrine)
        {
            // ours at the altar, and the trial going on
            Sim.Command(ours, chest, stay: true);
            for (double t = 0; t < (_poseSeconds == 14 ? 40 : _poseSeconds); t += .5) { Advance(.5); CampaignTick(.5); }
            Rig.SetAt(w.WX(s.TroveX + dx * 30), w.WY(s.TroveY), w.WZ(s.TroveZ + dz * 30), MathF.Atan2(dx, dz) + .6f, .6f, 1.25f);
            return;
        }
        // the way in faces the valley's middle along x or z (Troves.Facing): the mouth or the doorway so far out from the chest
        float ax = MathF.Abs(dx) >= MathF.Abs(dz) ? MathF.Sign(dx) : 0, az = ax == 0 ? MathF.Sign(dz) : 0;
        float mouth = s.Trove switch { TroveKind.DeadMine => 56, TroveKind.Den => 48, _ => 36 };
        // ours where they came in, the keepers at their spots (--at= seconds: ours led up to them and the fight on)
        if (_poseSeconds != 14) Sim.Command(ours, Sim.NearestStand((int)(s.TroveX + ax * (mouth + 20)), (int)(s.TroveZ + az * (mouth + 20)), 2), stay: true);
        Advance(_poseSeconds == 14 ? 4 : _poseSeconds);
        // seen from outside, looking in at the mouth, a little from aside
        float mx = s.TroveX + ax * mouth, mz = s.TroveZ + az * mouth;
        Rig.SetAt(w.WX(mx), w.WY(w.SurfAt((int)mx, (int)mz)), w.WZ(mz), MathF.Atan2(ax, az) + .45f, .62f, s.Trove == TroveKind.Ruins ? 1.25f : 1.45f);
    }

    /// <summary>A party of ours as the map keeps it: a commander and these kinds, a look each.</summary>
    static Party PartyOf(params UnitType[] kinds)
    {
        var party = new Party();
        foreach (var k in kinds) party.Members.Add(new Traveller { Kind = k, Look = 7 + party.Members.Count * 11, Hp = k.Hp, MaxHp = k.Hp, Hero = k == Kinds.Captain ? Heroes.Fresh(5 + party.Members.Count * 31) : null });
        return party;
    }

    /// <summary>
    /// --campaign --pose=lairfight: the scene opens on the horde's town of the map, a party of ours of thirteen come in by
    /// the side facing the first valley (set before the valley is made).
    /// </summary>
    void CampaignPoseBefore()
    {
        if (_pose == "trove" && Atlas != null) { TrovePoseBefore(); return; }
        if (_pose != "lairfight" || Atlas == null) return;
        var lair = Atlas.Cells.Find(c => c.Holding == Holding.HordeTown);
        Atlas.HereQ = lair.Q; Atlas.HereR = lair.R;
        var p = new MapParty { Q = lair.Q, R = lair.R, Owner = Holder.Ours, CameQ = Atlas.Neighbour(lair, Campaign.SideToward(lair, 0, 0)).Q, CameR = Atlas.Neighbour(lair, Campaign.SideToward(lair, 0, 0)).R,
            Party = PartyOf(Kinds.Captain, Kinds.Swordsman, Kinds.Swordsman, Kinds.Swordsman, Kinds.Spearman, Kinds.Spearman, Kinds.Spearman,
                Kinds.Archer, Kinds.Archer, Kinds.Archer, Kinds.Swordsman, Kinds.Spearman, Kinds.Archer) };
        Atlas.Parties.Add(p);
        _entering = p;
    }

    /// <summary>
    /// What a campaign's scene is staged as for a picture (--campaign with --pose=): «march» a party of ours halfway to the
    /// ore valley on the board; «arrived» it come there, its card up; «taken» the ore valley ours with its outpost, paying,
    /// a party on its way on toward the lair; «horde» the map left to itself until an army of the horde is on its way to
    /// the first valley; «lairfight» ours fighting in the horde's town (--at= seconds, 26 by itself); «places» the first
    /// valley's places with three of its own out on the map and three guests of the ore valley come in, its town hall
    /// standing (the town tab and the citadel's muster open); «edge» a commander gone into at the side toward the ore
    /// valley, his men about him, the offer to go out to the map in his eyes; «heroboard» a party on its way, its hero risen
    /// a level with a skill to choose, his card opened from the board.
    /// </summary>
    void CampaignPose()
    {
        if (Atlas == null || _pose == "") return;
        Sim.Waves.Countdown = 1e9;
        var ore = Atlas.Cells.Find(c => c.Kind == ValleyKind.Ore && Campaign.Distance(0, 0, c.Q, c.R) == 1);
        MapParty Walk(MapCell to, double share)
        {
            var p = Atlas.Out(PartyOf(Kinds.Captain, Kinds.Swordsman, Kinds.Swordsman, Kinds.Archer, Kinds.Archer, Kinds.Worker), to);
            p.Party.Side = Campaign.SideToward(Atlas.Here, to.Q, to.R);
            if (p.Moving) p.Progress = share * MapPath.StepSeconds(p, Atlas.CellOf(p), p.Path[0]);
            return p;
        }
        switch (_pose)
        {
            case "march":
            {
                var p = Walk(ore, .45);
                Walk(Atlas.Cells.Find(c => c.Kind == ValleyKind.Forest && Campaign.Distance(0, 0, c.Q, c.R) == 1), .2);
                Hud.Board?.Open();
                Hud.Board?.ChooseParty(p);
                break;
            }
            case "arrived":
            {
                var p = Walk(ore, 0);
                Atlas.Advance(Atlas.WalkLeft(p) + 1);
                Atlas.Clock = Sim.Clock.Now;
                CampaignTick(0);
                break;
            }
            case "heroboard":
            {
                // a party on its way, its hero risen to level 3 with a skill to choose; chosen twice on the board, his card
                var p = Walk(ore, .45);
                var h = p.Party.Commander.Hero;
                Heroes.Gain(h, Heroes.XpFor(2));
                Heroes.Choose(h, h.Offer[0]);
                Heroes.Gain(h, Heroes.XpFor(3) + 40 - h.Xp);
                Hud.Board?.Open();
                Hud.Board?.ChooseParty(p);
                Hud.Board?.ChooseParty(p);
                break;
            }
            case "taken":
            {
                ore.Outpost = true;
                Atlas.OutpostStands(ore);
                ore.Sum.Metal = .6;
                Atlas.News.Clear();
                var lair = Atlas.Cells.Find(c => c.Holding == Holding.HordeTown);
                var p = Atlas.Out(PartyOf(Kinds.Captain, Kinds.Swordsman, Kinds.Swordsman, Kinds.Spearman, Kinds.Archer), lair);
                p.Q = ore.Q; p.R = ore.R;
                Atlas.Send(p, lair);
                p.Progress = .5 * MapPath.StepSeconds(p, ore, p.Path[0]);
                Hud.Board?.Open();
                Hud.Board?.ChooseParty(p);
                break;
            }
            case "horde":
            {
                // the map left to itself for days: the lair musters and sends its armies, one of them on the first valley
                for (int d = 0; d < 40 && Atlas.Coming().Count == 0; d++) Atlas.Advance(DayClock.Cycle / 2);
                Sim.SetClock(Atlas.Clock);
                Atlas.News.Clear();
                Hud.Board?.Open();
                if (Atlas.Coming() is { Count: > 0 } coming) Hud.Board?.Choose(Atlas.At(coming[0].GoalQ, coming[0].GoalR));
                break;
            }
            case "places":
            {
                // a town hall standing, put up at once
                Sim.Knowledge += 10;
                Sim.Learn("b_hall");
                var (hx, hz) = LevelNearKeep();
                Sim.InstantLabour = true;
                if (hx >= 0) Sim.PutUp(Sim.TownKindOf("hall"), hx, hz, false, out _);
                for (int i = 0; i < 60 && Sim.HallLevel() == 0; i++) { Sim.CompleteAll(); Advance(1); foreach (var b in Sim.Buildings) if (Sim.ZoneOf(b) is { } z) Sim.LookOver(z); }
                Sim.InstantLabour = false;
                // three of the household out over the side toward the forest and onto the map
                var forest = Atlas.Cells.Find(c => c.Kind == ValleyKind.Forest && Campaign.Distance(0, 0, c.Q, c.R) == 1);
                int side = Campaign.SideToward(Atlas.Here, forest.Q, forest.R);
                var three = new List<Unit> { Sim.Units.Find(u => !u.Dead && u.T == Kinds.Captain), Sim.Units.Find(u => !u.Dead && u.T == Kinds.Swordsman), Sim.Units.Find(u => !u.Dead && u.T == Kinds.Archer) };
                Sim.Depart(three, side);
                _outbound[side] = forest;
                for (int i = 0; i < 400 && Sim.Units.Exists(u => !u.Dead && u.LeaveBy >= 0); i++) { Advance(1); CampaignTick(0); }
                CampaignTick(0);
                // three guests of the ore valley come in
                var guests = PartyOf(Kinds.Spearman, Kinds.Spearman, Kinds.Archer);
                foreach (var m in guests.Members) m.Billet = Campaign.KeyOf(ore);
                Come(guests, Campaign.SideToward(Atlas.Here, ore.Q, ore.R), select: false);
                Advance(4);
                Atlas.News.Clear();
                Hud.OpenPanel("town");
                Selected.Clear();
                KeepChosen = true;
                Hud.OnSelectionChanged();
                Hud.Refresh();
                Rig.SetAt(World.WX(Site.KeepX), World.WY(Site.KeepBase), World.WZ(Site.KeepZ), Rig.Aim.yaw, .62f, 1.4f);
                break;
            }
            case "edge":
            {
                // a commander and three of his men by the side toward the ore valley; gone into him, looking out
                int side = Campaign.SideToward(Atlas.Here, ore.Q, ore.R);
                var hex = Site.Hex;
                var four = new List<Unit> { Sim.Units.Find(u => !u.Dead && u.T == Kinds.Captain), Sim.Units.Find(u => !u.Dead && u.T == Kinds.Swordsman),
                    Sim.Units.Find(u => !u.Dead && u.T == Kinds.Archer), Sim.Units.Find(u => !u.Dead && u.T == Kinds.Worker) };
                for (int i = 0; i < four.Count; i++)
                {
                    var (px, pz) = hex.SidePoint(side, .47f + .02f * i, (3 + (i == 0 ? 0 : 3)) * VoxelWorld.PerMetre);
                    var u = four[i];
                    u.Cell = Sim.NearestStand((int)px, (int)pz, u.T.Move.Height); (u.X, u.Y, u.Z) = Sim.Nav.Feet(u.Cell); u.Path = null; u.Hold = true;
                }
                var (ix, iz) = hex.Inward(side);
                four[0].Facing = MathF.Atan2(-iz, -ix);
                Advance(1);
                FP.Enter(four[0]);
                FP.Face(MathF.Atan2(-iz, -ix) + .35f, -.05f);
                break;
            }
            case "troves":
            {
                // the big map with its fog lifted: the lairs, the treasures and their keepers, a party of ours chosen
                Atlas.RevealAll = true;
                var mine = Atlas.Cells.Find(c => c.Trove == TroveKind.DeadMine);
                var p = Walk(Atlas.Cells.Find(c => Campaign.Distance(0, 0, c.Q, c.R) == 1 && Campaign.Distance(c.Q, c.R, mine.Q, mine.R) <= 2) ?? ore, .3);
                Atlas.Advance(0);
                Atlas.News.Clear();
                Hud.Board?.Open();
                Hud.Board?.ChooseParty(p);
                Hud.Board?.Choose(mine);
                break;
            }
            case "trove":
            {
                TrovePose();
                break;
            }
            case "lairfight":
            {
                // ours led in on the fire, as a player would send them
                var ours = Sim.Units.FindAll(u => !u.Dead && u.Team == Team.Player);
                Sim.Command(ours, Sim.NearestStand(Site.KeepX, Site.KeepZ, 3), stay: false);
                Advance(_poseSeconds == 14 ? 26 : _poseSeconds);
                var hex = Site.Hex;
                var (ix, iz) = hex.Inward(Site.EntrySide);
                Rig.SetAt(World.WX(Site.KeepX - ix * 100), World.WY(Site.KeepBase), World.WZ(Site.KeepZ - iz * 100), MathF.Atan2(ix, iz) + .4f, .58f, 1.5f);
                break;
            }
        }
    }
}
