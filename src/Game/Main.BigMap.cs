using System.Collections.Generic;
using Godot;
using VoxelTower.Core;
using Holder = VoxelTower.Core.Owner;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>The big map's hexes as its drawing reads them, for the campaign it was made for.</summary>
    CampaignHexes _hexes;
    Campaign _hexesOf;
    readonly Dictionary<MapParty, MapWalker> _walkers = new();
    // the way last laid out in arrows, so it is laid again only when it changes
    List<HexStep> _arrowsOf;
    int _arrowsCount = -1, _arrowsFrom = -1;
    long _reachKey = -2;

    /// <summary>
    /// The big map opened (Atlas.Big): its drawing of hexes over everything, fed by the campaign — the hexes and their fog
    /// (CampaignHexes), our heroes and the horde's armies walking, the stacks keeping hexes, the way of the hero chosen;
    /// a click on a hex goes to the map's frame (Ui/AtlasHud.cs), which plans the way and sends the hero.
    /// </summary>
    void OpenBigMap()
    {
        if (_hexes == null || _hexesOf != Atlas) { _hexes = new CampaignHexes(Atlas); _hexesOf = Atlas; _walkers.Clear(); }
        var hero = Hud.MapPane.Hero ?? Atlas.Parties.Find(p => p.Owner == Holder.Ours && p.Party.Commander != null);
        var lay = new HexMapLayout(Atlas.Width, Atlas.Height);
        var (x, z) = hero != null ? lay.Center(hero.Col, hero.Row) : lay.Center(Atlas.Here.Col, Atlas.Here.Row);
        bool first = WorldMap == null;
        var v = OpenWorld(_hexes, x, z);
        if (first)
        {
            v.ClickedHex += (c, r, b) => Hud.MapPane?.HexClicked(c, r, b);
            WorldFigures.Sync = SyncBigMap;
            // (a garrison of ours shown by one who fights in it, not its workers)
            WorldFigures.KindOf = (c, r) => Atlas?.Cell(c, r) is { } h && h.Sum.Keep.Count > 0 ? (h.Sum.Keep.Find(Campaign.Fights) ?? h.Sum.Keep[0]).Kind : null;
            WorldFigures.CountOf = (c, r) => Atlas?.Cell(c, r) is { } h ? h.Sum.Keep.Count : 0;
            v.Describe = DescribeHex;
        }
        Hud.MapPane.FocusOn = p => { if (WorldMap?.Layout != null) WorldMap.FocusHex(p.Col, p.Row); };
        v.Focus(x, z);
        _arrowsOf = null; _arrowsCount = -1; _reachKey = -2;
    }

    /// <summary>
    /// A hex of the big map as the hover says it: its ground and its thing (HexMapView.Tell), and a stack of the wild
    /// keeping it by its name — «Стая волков: 12» — and how it stands against the hero chosen.
    /// </summary>
    string DescribeHex(int col, int row)
    {
        string said = WorldMap.Tell(col, row);
        if (_hexes == null || !_hexes.Seen(col, row) || Atlas?.Cell(col, row) is not { } h) return said;
        if (h.Owner != Holder.None || h.Sum.Keep.Count == 0 || Bestiary.Of(h.Sum.Keep[0].Kind) == null) return said;
        // a stack on a hex of its own is named in place of the bare «Стража»; one keeping a thing after what it keeps
        int nl = said.IndexOf('\n');
        if (h.Thing?.Kind == ThingKind.Monster && nl > 0) said = $"{Bestiary.Label(h.Sum.Keep)}.{said[nl..]}";
        else said += $"\n{Bestiary.Label(h.Sum.Keep)}.";
        var hero = Hud.MapPane?.Hero;
        if (hero != null && Atlas.Parties.Contains(hero)) said += $"\n{MapBoard.Against(hero.Party.Members, h)}";
        return said;
    }

    /// <summary>The figures and the arrows brought up to the campaign each frame the big map is open.</summary>
    void SyncBigMap()
    {
        var c = Atlas;
        if (c == null || !c.Big || _hexesOf != c) return;
        var figs = WorldFigures;
        foreach (var p in c.Parties)
        {
            if (!_walkers.TryGetValue(p, out var w))
            {
                w = new MapWalker { Owner = (byte)(p.Owner == Holder.Ours ? 1 : 2) };
                if (p.Owner == Holder.Ours && p.Party.Commander is { } cm) w.Body = Bodies.HeroRider(cm.Look);
                // (a convoy's carters on foot, Campaign.Economy.cs)
                else if (p.Convoy) w.Body = Bodies.Plan(Kinds.Worker, 0);
                else if (p.Owner == Holder.Ours)
                {
                    var rider = p.Party.Members.Find(m => m.Kind.Mounted);
                    w.Body = Bodies.Plan(rider?.Kind ?? Kinds.Rider, 0);
                }
                else w.Body = Bodies.Plan(p.Lord?.Kind ?? Kinds.Orc, 0);
                _walkers[p] = w;
                figs.Walkers.Add(w);
            }
            w.Col = p.Col; w.Row = p.Row;
            w.Steps.Clear();
            if (p.Moving && p.Path.Count > 0) w.Steps.Add((p.Path[0].Col, p.Path[0].Row, 0));
            w.Progress = c.StepShare(p);
            w.Count = p.Party.Members.Count + (p.Lord != null ? 1 : 0);
        }
        foreach (var p in new List<MapParty>(_walkers.Keys))
            if (!c.Parties.Contains(p)) { figs.Walkers.Remove(_walkers[p]); _walkers.Remove(p); }
        var hero = Hud.MapPane?.Hero;
        figs.Chosen = hero != null && _walkers.TryGetValue(hero, out var hw) ? hw : null;
        // the hexes the hero chosen can still get to today: their sides drawn plainly on the land, as in «Heroes»
        long reach = hero == null || hero.Moving || hero.MovesLeft <= 0 ? -1 : ((long)hero.Row * c.Width + hero.Col) * 100000 + hero.MovesLeft;
        if (reach != _reachKey)
        {
            _reachKey = reach;
            if (reach < 0) WorldMap.ShowReach(null);
            else
            {
                int stamp = MapWalk.Spread(c, c.Cell(hero.Col, hero.Row), Holder.Ours, hero.MovesLeft);
                var buf = c.WalkBuffers(c.Width * c.Height);
                var hexes = new List<(int, int)>();
                for (int i = 0; i < c.Width * c.Height; i++) if (buf.Mark[i] >= stamp) hexes.Add((i % c.Width, i / c.Width));
                WorldMap.ShowReach(hexes);
            }
        }
        // the way laid out: planned (green as far as today), or what is left of the way he walks
        var plan = Hud.MapPane?.Planned;
        List<HexStep> show = plan ?? (hero != null && hero.Steps.Count > 0 ? hero.Steps : null);
        int from = hero == null ? -1 : hero.Row * c.Width + hero.Col;
        if (show == null || hero == null) { if (_arrowsCount != 0) { WorldMap.HidePath(); _arrowsCount = 0; _arrowsOf = null; } return; }
        if (show == _arrowsOf && show.Count == _arrowsCount && from == _arrowsFrom) return;
        _arrowsOf = show; _arrowsCount = show.Count; _arrowsFrom = from;
        var steps = new List<(int, int, int)>(show.Count);
        foreach (var s in show) steps.Add((s.Col, s.Row, s.Day));
        WorldMap.ShowPath(hero.Col, hero.Row, steps);
    }
}
