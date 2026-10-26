using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// A world of hexes laid out to show what stands on the big world map, every thing in its own hex and the land about it
/// of every kind: our castle among its farms with a road east, an outpost and the place for one, the mines in a row,
/// the small things in another, the dead places, a pass between mountains; a wood of the meadows, a wood in the snow,
/// a swamp, sand by a lake with reeds and lilies, the horde's burnt land with its town and its lair.
/// </summary>
public sealed class ShowcaseHexes : IHexSource
{
    public int Width => 26;
    public int Height => 20;
    public event Action<int, int> Changed { add { } remove { } }
    readonly HexLook[] _look;
    public readonly Dictionary<HexIcon, (int Col, int Row)> At = new();

    public ShowcaseHexes()
    {
        _look = new HexLook[Width * Height];
        for (int r = 0; r < Height; r++) for (int c = 0; c < Width; c++) _look[r * Width + c] = new HexLook { Ground = HexGround.Grass };
        void Ground(int c0, int r0, int c1, int r1, HexGround g, HexRelief rel = HexRelief.Flat)
        {
            for (int r = r0; r <= r1; r++) for (int c = c0; c <= c1; c++) { ref var l = ref _look[r * Width + c]; l.Ground = g; l.Relief = rel; }
        }
        void Thing(int c, int r, HexIcon icon, byte owner = 0, byte variant = 0)
        {
            ref var l = ref _look[r * Width + c];
            l.Icon = icon; l.Owner = owner; l.Variant = variant;
            if (l.Relief == HexRelief.Forest) l.Relief = HexRelief.Flat;
            At.TryAdd(icon, (c, r));
        }
        // a wood of the meadows to the north, firs in the snow to the south-west, a swamp, sand by a lake, mountains
        Ground(12, 0, 25, 3, HexGround.Grass, HexRelief.Forest);
        Ground(0, 15, 6, 19, HexGround.Snow, HexRelief.Forest);
        Ground(0, 13, 6, 14, HexGround.Snow);
        Ground(7, 16, 11, 19, HexGround.Swamp);
        Ground(8, 17, 9, 18, HexGround.Swamp, HexRelief.Forest);
        Ground(20, 4, 25, 7, HexGround.Sand);
        Ground(22, 5, 24, 6, HexGround.Water);
        Ground(21, 4, 21, 5, HexGround.Sand, HexRelief.Forest);
        Ground(0, 0, 3, 3, HexGround.Dirt, HexRelief.Hills);
        Ground(0, 0, 1, 1, HexGround.Rough, HexRelief.Mountain);
        Ground(12, 16, 25, 19, HexGround.Scorched);
        Ground(20, 14, 25, 15, HexGround.Scorched);
        Ground(17, 17, 18, 18, HexGround.Scorched, HexRelief.Forest);
        Ground(23, 16, 25, 17, HexGround.Lava);
        // the pass: a ridge of mountains with a gate in it
        for (int r = 8; r <= 12; r++) { ref var l = ref _look[r * Width + 23]; l.Relief = HexRelief.Mountain; l.Ground = HexGround.Rough; }
        Thing(23, 10, HexIcon.Pass, 1);
        // our castle, its road east, the farms about it are the drawing's own
        Thing(5, 5, HexIcon.Citadel, 1);
        for (int c = 6; c <= 13; c++) { ref var l = ref _look[5 * Width + c]; l.Roads = (byte)((1 << Hex.East) | (1 << Hex.West)); }
        _look[5 * Width + 5].Roads = 1 << Hex.East;
        Thing(10, 3, HexIcon.Outpost, 1);
        Thing(14, 5, HexIcon.OutpostPlace);
        // the mines in a row
        HexIcon[] mines = { HexIcon.MineLogs, HexIcon.MineStone, HexIcon.MineMetal, HexIcon.MineGold, HexIcon.MineMercury, HexIcon.MineSulfur, HexIcon.MineCrystal, HexIcon.MineGems };
        for (int k = 0; k < mines.Length; k++) { if (mines[k] == HexIcon.MineSulfur) Ground(2 + k * 2, 8, 2 + k * 2, 8, HexGround.Sand); Thing(2 + k * 2, 8, mines[k], (byte)(k % 3 == 0 ? 1 : k % 3 == 1 ? 0 : 2)); }
        // the small things in another
        HexIcon[] small = { HexIcon.Pile, HexIcon.Pile, HexIcon.Pile, HexIcon.Chest, HexIcon.Campfire, HexIcon.Windmill, HexIcon.WaterWheel, HexIcon.Shrine, HexIcon.Dwelling, HexIcon.Signpost, HexIcon.Learning, HexIcon.Stables, HexIcon.Observatory };
        for (int k = 0; k < small.Length; k++) Thing(1 + k * 2 - (k > 9 ? 1 : 0), 11, small[k]);
        _look[11 * Width + 13].Rivers = (byte)((1 << Hex.NorthEast) | (1 << Hex.SouthWest));
        // the dead places, the horde's
        HexIcon[] dead = { HexIcon.DeadMine, HexIcon.Ruins, HexIcon.Den, HexIcon.Trial };
        for (int k = 0; k < dead.Length; k++) Thing(2 + k * 3, 13 + (k % 2), dead[k]);
        Thing(15, 17, HexIcon.HordeTown, 2);
        Thing(21, 15, HexIcon.Lair, 2);
        Thing(19, 18, HexIcon.Monster, 0);
        Thing(12, 13, HexIcon.Monster, 0);
    }

    public HexLook Look(int col, int row) => _look[row * Width + col];
    public bool Seen(int col, int row) => true;
}

public partial class Main
{
    /// <summary>
    /// What stands on the big world map, shown (--demo --pose=mapthings): --look=0 our castle among its farms; 1 the mines
    /// of logs, stone, iron and gold; 2 those of quicksilver, sulphur, crystal and gems; 3 the small things (piles, a chest,
    /// a campfire, the windmill and the water mill); 4 the shrine, a dwelling, the signpost, the stone of learning, the
    /// stables, the observatory; 5 the horde's town in its burnt land with its army; 6 the lair and the pass; 7 the dead
    /// places; 8 a wood's edge and the snowy firs; 9 the lake, the sand, the swamp; 10 the whole showcase; 11 from far
    /// off (the coarse models standing for the things); 12 the hero and the horde's warlord close up.
    /// </summary>
    void MapThingsPose()
    {
        var src = new ShowcaseHexes();
        var lay = new HexMapLayout(src.Width, src.Height);
        var v = OpenWorld(src, lay.SX / 2f, lay.SZ / 2f);
        v.HoverAt(new Vector2(-9999, -9999));
        Hud.Visible = false;
        v.Without(_perfWithout);
        _shotFrames = Math.Max(_shotFrames, 420);
        void At(int c, int r, float dist, float dx = 0, float dz = 0)
        {
            var (x, z) = lay.Center(c, r);
            v.Snap(x + dx, z + dz, dist);
        }
        (int, int) Of(HexIcon i) => src.At[i];
        var hero = new MapWalker { Col = 7, Row = 6, Owner = 1, Body = Bodies.HeroRider(0) };
        WorldFigures.Walkers.Add(hero);
        WorldFigures.Chosen = hero;
        switch (_look)
        {
            case 0: { var (c, r) = Of(HexIcon.Citadel); At(c, r, 15, 6, 14); break; }
            case 1: At(5, 8, 17, 4, 12); break;
            case 2: At(13, 8, 17, 4, 12); break;
            case 3: At(6, 11, 17, 4, 12); break;
            case 4: At(17, 11, 18, 2, 12); break;
            case 5:
            {
                var (c, r) = Of(HexIcon.HordeTown); At(c, r, 15, 4, 14);
                WorldFigures.Walkers.Add(new MapWalker { Col = c + 1, Row = r - 1, Owner = 2, Body = Bodies.Plan(Kinds.Orc, 0), Count = 23 });
                break;
            }
            case 6: At(22, 12, 20, 0, 14); break;
            case 7: At(6, 13, 18, 2, 14); break;
            case 8: At(9, 2, 18, 0, 20); break;
            case 9: At(16, 17, 20, -40, -40); At(22, 5, 18, -8, 16); break;
            case 10: At(12, 10, 46, 0, 30); break;
            case 11: At(12, 10, 80, 0, 50); break;
            case 12:
            {
                At(7, 6, 10, 0, 10);
                WorldFigures.Walkers.Add(new MapWalker { Col = 8, Row = 6, Owner = 2, Body = Bodies.Plan(Kinds.Orc, 0), Count = 17 });
                break;
            }
            default: At(12, 10, 34, 0, 24); break;
        }
        _poseLater = () => { var (t, d, sh) = v.Drawn(); GD.Print($"[map things] {Engine.GetFramesPerSecond()} fps; {t} k triangles in {d} draw calls, {sh} k for shadows; {v.DressedPieces} pieces dressed"); };
    }
}
