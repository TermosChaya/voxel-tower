using System;

namespace VoxelTower.Core;

/// <summary>
/// The big map as the drawing reads it (HexLook.cs, the contract with the map's drawing): each hex's ground and relief,
/// its roads and rivers, the one thing it shows and whose that is, the fog. Variant: bit 0 the thing is spent (picked
/// up, opened, its week's given, its treasure taken), bit 1 it is kept by a stack.
/// </summary>
public sealed class CampaignHexes : IHexSource
{
    readonly Campaign _c;

    public CampaignHexes(Campaign c)
    {
        _c = c;
        c.HexChangedAt += (col, row) => Changed?.Invoke(col, row);
    }

    public int Width => _c.Width;
    public int Height => _c.Height;
    public event Action<int, int> Changed;

    public bool Seen(int col, int row) => _c.Cell(col, row) is { } h && _c.Seen(h);

    public HexLook Look(int col, int row)
    {
        var h = _c.Cell(col, row);
        if (h == null) return new HexLook { Ground = HexGround.DeepWater, Zone = -1 };
        var look = new HexLook
        {
            Ground = h.Ground, Relief = h.Relief, Height = h.Height, Roads = h.Roads, Rivers = h.Rivers, Zone = h.Zone,
            Owner = (byte)h.Owner,
        };
        var t = h.Thing;
        bool kept = h.Sum.Keep.Count > 0 && h.Owner != Owner.Ours;
        look.Icon = IconOf(h);
        if (t != null && t.Owner != Owner.None) look.Owner = (byte)t.Owner;
        if (t != null && (t.Taken || t.Kind is ThingKind.Windmill or ThingKind.WaterWheel && t.Week == _c.Week || t.Kind == ThingKind.Trove && h.TroveTaken)) look.Variant |= 1;
        if (kept) look.Variant |= 2;
        return look;
    }

    /// <summary>The icon a hex shows: its thing's, a pass's once its stack is beaten, an outpost raised.</summary>
    public static HexIcon IconOf(MapCell h)
    {
        var t = h.Thing;
        if (t == null)
        {
            if (h.Owner == Owner.Ours && h.Sum.Raised > 0 && !h.Home) return HexIcon.Outpost;
            return h.Pass ? HexIcon.Pass : HexIcon.None;
        }
        return t.Kind switch
        {
            ThingKind.Citadel => HexIcon.Citadel,
            ThingKind.HordeTown => h.Owner == Owner.Horde ? HexIcon.HordeTown : HexIcon.OutpostPlace,
            ThingKind.Lair => h.Owner == Owner.Horde ? HexIcon.Lair : HexIcon.OutpostPlace,
            ThingKind.Mine => t.Res switch
            {
                Res.Logs => HexIcon.MineLogs, Res.Stone => HexIcon.MineStone, Res.Metal => HexIcon.MineMetal, Res.Gold => HexIcon.MineGold,
                Res.Mercury => HexIcon.MineMercury, Res.Sulfur => HexIcon.MineSulfur, Res.Crystal => HexIcon.MineCrystal, _ => HexIcon.MineGems,
            },
            ThingKind.Pile => t.Taken ? HexIcon.None : HexIcon.Pile,
            ThingKind.Chest => t.Taken ? HexIcon.None : HexIcon.Chest,
            ThingKind.Campfire => t.Taken ? HexIcon.None : HexIcon.Campfire,
            ThingKind.Windmill => HexIcon.Windmill,
            ThingKind.WaterWheel => HexIcon.WaterWheel,
            ThingKind.Shrine => HexIcon.Shrine,
            ThingKind.Dwelling => HexIcon.Dwelling,
            ThingKind.Monster => h.Sum.Keep.Count > 0 ? HexIcon.Monster : h.Pass ? HexIcon.Pass : HexIcon.None,
            ThingKind.Trove => h.Trove switch { TroveKind.DeadMine => HexIcon.DeadMine, TroveKind.Ruins => HexIcon.Ruins, TroveKind.Den => HexIcon.Den, _ => HexIcon.Trial },
            ThingKind.Signpost => HexIcon.Signpost,
            ThingKind.Learning => HexIcon.Learning,
            ThingKind.Stables => HexIcon.Stables,
            _ => HexIcon.Observatory,
        };
    }
}
