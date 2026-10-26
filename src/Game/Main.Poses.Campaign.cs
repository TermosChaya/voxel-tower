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

    /// <summary>A party of ours as the map keeps it: a commander and these kinds, a look each.</summary>
    static Party PartyOf(params UnitType[] kinds)
    {
        var party = new Party();
        foreach (var k in kinds) party.Members.Add(new Traveller { Kind = k, Look = 7 + party.Members.Count * 11, Hp = k.Hp, MaxHp = k.Hp });
        return party;
    }

    /// <summary>
    /// --campaign --pose=lairfight: the scene opens on the horde's town of the map, a party of ours of thirteen come in by
    /// the side facing the first valley (set before the valley is made).
    /// </summary>
    void CampaignPoseBefore()
    {
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
    /// the first valley; «lairfight» ours fighting in the horde's town (--at= seconds, 26 by itself).
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
