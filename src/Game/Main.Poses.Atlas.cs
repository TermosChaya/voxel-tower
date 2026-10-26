using System;
using System.Linq;
using Godot;
using VoxelTower.Core;
using Holder = VoxelTower.Core.Owner;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// The campaign on the map's land (--campaign --pose=atlas): --look=0 a new campaign, its first hero by the first
    /// valley's citadel, chosen; 1 the way to a valley two cells off laid out before him, green as far as he gets by
    /// nightfall; 2 him on his way to the ore valley, a third of it walked; 3 him come into the ore valley, «Войти в
    /// клетку» under the middle; 4 him gone into the first valley from the map, his army walking in by its side; 5 an
    /// army of the horde on its way to the first valley, seen, and its card.
    /// </summary>
    void AtlasPose()
    {
        var hero = Atlas.Parties.Find(p => p.Owner == Holder.Ours && p.Party.Commander?.Hero != null);
        if (hero == null) return;
        OpenAtlas();
        var pane = Hud.MapPane;
        pane.Choose(hero, focus: true);
        var ore = Atlas.Cells.Find(c => c.Kind == ValleyKind.Ore && Campaign.Distance(0, 0, c.Q, c.R) == 1);
        var far = Atlas.Cells.Where(c => Campaign.Distance(0, 0, c.Q, c.R) == 2).OrderBy(c => Campaign.Distance(ore.Q, ore.R, c.Q, c.R)).First();
        var (fx, fz) = _land.Center(far);
        var (ox, oz) = _land.Center(ore);
        switch (_look)
        {
            case 1:
                pane.Preview(new Vector2(fx, fz));
                AtlasLand.Focus((hero.AtX + fx) / 2, (hero.AtZ + fz) / 2);
                break;
            case 2:
            {
                pane.Go(new Vector2(ox, oz));
                double all = Atlas.TrailLeft(hero);
                Atlas.Advance(all * .35);
                pane.Preview(new Vector2(ox, oz));
                AtlasLand.Focus((hero.AtX + ox) / 2, (hero.AtZ + oz) / 2);
                break;
            }
            case 3:
            {
                pane.Go(new Vector2(ox, oz));
                Atlas.Advance(Atlas.TrailLeft(hero) + 1);
                CampaignTick(0);
                pane.Choose(hero, focus: true);
                break;
            }
            case 5:
            {
                // an army of the horde out of the lair's side on its way to the first valley, seen, and its card
                var from = Atlas.Neighbour(Atlas.Home, Campaign.SideToward(Atlas.Home, Atlas.Lair.Q, Atlas.Lair.R));
                var foe = new MapParty { Q = from.Q, R = from.R, CameQ = from.Q, CameR = from.R, Owner = Holder.Horde, Aim = PartyAim.Strike, Party = new Party() };
                foreach (var k in new[] { Kinds.Orc, Kinds.Orc, Kinds.Goblin, Kinds.Goblin, Kinds.Goblin, Kinds.Goblin, Kinds.Troll, Kinds.Orc, Kinds.Goblin })
                    foe.Party.Members.Add(new Traveller { Kind = k, Hp = k.Hp, MaxHp = k.Hp });
                foe.Lord = new Warlord { Name = "Угрюм", Kind = Kinds.Troll, Level = 2 };
                Atlas.Parties.Add(foe);
                Atlas.PlaceIn(foe, from);
                Atlas.Send(foe, Atlas.Home);
                foe.Progress = MapPath.StepSeconds(foe, from, Atlas.Home) * .3;
                Hud.MapPane.Warn(foe);
                var (hx, hz) = _land.Center(Atlas.Home);
                var (fx2, fz2) = _land.Center(from);
                AtlasLand.Focus((hx + fx2) / 2, (hz + fz2) / 2);
                break;
            }
            case 4:
            {
                EnterHero(hero);
                Advance(_poseSeconds == 14 ? 8 : _poseSeconds);
                var ours = Sim.Units.FindAll(u => !u.Dead && u.Team == Team.Player && u.T is { } t && (t == Kinds.Captain || t == Kinds.Swordsman || t == Kinds.Archer));
                if (ours.Count == 0) break;
                float cx = ours.Average(u => u.X), cz = ours.Average(u => u.Z);
                Rig.SetAt(World.WX(cx), World.WY(World.SurfAt((int)cx, (int)cz)), World.WZ(cz), Rig.Aim.yaw, .6f, 2.2f);
                break;
            }
        }
    }
}
