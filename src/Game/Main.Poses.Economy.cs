using System;
using System.Linq;
using Godot;
using VoxelTower.Core;
using Holder = VoxelTower.Core.Owner;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// The realm's spending staged on a new S campaign's big map (--campaign --pose=economy --look=N): 0 the tavern's two
    /// heroes, 1 the merchant with his week's artifacts, the rare resources' trade and the treasury, 2 the hero's card with
    /// what he wears and carries, 3 a convoy on its way from an outpost home, 4 the convoy's plate.
    /// </summary>
    void EconomyPose()
    {
        var c = Atlas;
        var hero = c.Parties.Find(p => p.Owner == Holder.Ours && p.Party.Commander?.Hero != null);
        OpenAtlas();
        Hud.MapPane.Choose(hero, focus: true);
        var v = WorldMap;
        v.HoverAt(new Vector2(-9999, -9999));
        _shotFrames = Math.Max(_shotFrames, 140);
        c.Give(Res.Gold, 6000);
        for (int i = 0; i < c.Rare.Length; i++) c.Give((Res)((int)Res.Mercury + i), 8 + 3 * i);
        // the hero as a few weeks would have him: some things worn, one in his bag
        var h = hero.Party.Commander.Hero;
        foreach (var key in new[] { "mail", "sharp_sword", "scout_helm", "road_boots", "valor_medal", "tracker_bow" }) Artifacts.Give(h, Artifacts.Of(key));
        Heroes.Gain(h, 300);
        while (h.Pending) Heroes.Choose(h, h.Offer[0]);
        var home = c.Home;
        void Look(MapCell at, float dist, float dz = 26) { var (x, z) = v.Layout.Center(at.Col, at.Row); v.Snap(x, z + dz, dist); }
        MapCell Outpost()
        {
            var post = c.Cells.Where(x => x.Passable && x.Thing == null && x.Sum.Keep.Count == 0 && !x.Pass && x.Zone == home.Zone)
                .Select(x => (x, d: Campaign.Distance(x.Q, x.R, home.Q, home.R))).Where(t => t.d >= 5 && t.d <= 7).OrderBy(t => t.d).First().x;
            post.Owner = Holder.Ours; post.Outpost = true; post.Holding = Holding.Outpost; post.Sum.Raised = 1;
            post.Sum.Stone = 24; post.Sum.Logs = 12; post.Sum.Metal = 1.5;
            for (int s = 0; s < 3; s++) c.Reveal(post, 3);
            return post;
        }
        switch (_look)
        {
            case 0:
                Look(home, 30);
                Hud.MapPane.ShowTavern();
                break;
            case 1:
                c.Vault.Add("lord_crown");
                Look(home, 30);
                Hud.MapPane.ShowMerchant(hero);
                break;
            case 2:
                Look(home, 30);
                Hud.HeroPane.Show(hero);
                break;
            case 3:
            {
                var post = Outpost();
                var cart = c.SendConvoy(post, home, new[] { 12.0, 24.0, 1.5 }, out _);
                c.Advance(55);
                // (the land about the way made before the picture)
                _shotFrames = Math.Max(_shotFrames, 420);
                Hud.MapPane.Choose(null);
                var (ax, az) = v.Layout.Center(post.Col, post.Row);
                var (bx, bz) = v.Layout.Center(home.Col, home.Row);
                v.Snap((ax + bx) / 2, (az + bz) / 2 + 22, 30);
                if (cart != null) Hud.MapPane.Refresh();
                break;
            }
            case 4:
            {
                var post = Outpost();
                Look(post, 30);
                Hud.MapPane.ShowConvoy(post);
                break;
            }
        }
        Hud.MapPane.Refresh();
    }
}
