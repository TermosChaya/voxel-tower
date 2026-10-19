using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace VoxelTower.Core;

/// <summary>
/// The walkable world on metre cells of 8³ voxels: how full each cell is and of what, and where feet rest in it.
/// Leaves, bushes, grass, ladders and water don't block; trunks, earth, masonry and rock do once a cell is a quarter full.
/// A cell with a ladder in it can be hung on and climbed; a cell more than a third full of water drowns whoever stands there.
/// </summary>
public sealed class NavGrid
{
    public const int CS = 8, Shift = 3;
    /// <summary>A cell this full (of 512 voxels) blocks the way: a wall two voxels thick across it is enough.</summary>
    public const int SolidT = 128;

    public readonly VoxelWorld World;
    public readonly int NX, NY, NZ, Layer, Count;
    /// <summary>Blocking voxels in the cell, and how many of them are earth, wood, stone and rock.</summary>
    public readonly ushort[] Fill, Earth, Wood, Stone, Rock;
    /// <summary>The cell holds something nobody can hack through (bedrock, iron); Keep: part of the citadel.</summary>
    public readonly byte[] Hard, Keep;
    /// <summary>Height (voxels) where the feet of someone standing in the cell rest.</summary>
    public readonly ushort[] Floor;
    /// <summary>The cell holds a ladder (the player's or the horde's): bodies hang in it and climb through it.</summary>
    public readonly byte[] Ladder;
    /// <summary>Water voxels in the cell.</summary>
    public readonly ushort[] Water;
    /// <summary>Voxels of stakes in the cell: whoever goes through it goes slowly and is torn.</summary>
    public readonly ushort[] Spikes;
    /// <summary>
    /// Gate leaves our people can open: for them those don't block (they knock and wait), for the horde they do. A leaf
    /// voxel weighs double, so a leaf hung across a cell boundary still shuts both cells.
    /// </summary>
    public readonly ushort[] GateFill;
    /// <summary>A cell with this much water in it is too deep to stand in.</summary>
    public const int DeepT = 192;

    readonly bool[] _dirty;
    readonly List<int> _dirtyList = new();

    /// <summary>Bumped whenever a cell turns from open to blocked or back.</summary>
    public int Version { get; private set; }

    // what each material is to someone walking or hacking: 0 nothing, 1 earth, 2 wood, 3 stone, 4 rock, 5 hard, 6 citadel
    static readonly byte[] Cls = new byte[Mat.Count];

    static NavGrid()
    {
        for (int m = 1; m < Mat.Count; m++)
        {
            if (Mat.Def[m].Name == null || Mat.Passable[m]) continue;
            Cls[m] = Mat.Def[m].Group switch { MatGroup.Earth => 1, MatGroup.Wood => 2, MatGroup.Stone => 3, MatGroup.Rock => 4, _ => 5 };
        }
        foreach (var m in new[] { Mat.Keep, Mat.KeepShade, Mat.KeepDark, Mat.Roof }) Cls[m] = 6;
    }

    public NavGrid(VoxelWorld w)
    {
        World = w;
        NX = w.SX >> Shift; NY = w.SY >> Shift; NZ = w.SZ >> Shift; Layer = NX * NZ; Count = Layer * NY;
        Fill = new ushort[Count]; Earth = new ushort[Count]; Wood = new ushort[Count]; Stone = new ushort[Count]; Rock = new ushort[Count];
        Hard = new byte[Count]; Keep = new byte[Count]; Floor = new ushort[Count];
        Ladder = new byte[Count]; Water = new ushort[Count]; GateFill = new ushort[Count]; Spikes = new ushort[Count];
        _dirty = new bool[Count];
        w.Edited += OnEdit;
    }

    public int Id(int i, int y, int k) => (y * NZ + k) * NX + i;
    public int I(int c) => c % NX;
    public int K(int c) => c / NX % NZ;
    public int Y(int c) => c / Layer;
    public bool In(int i, int y, int k) => (uint)i < (uint)NX && (uint)y < (uint)NY && (uint)k < (uint)NZ;

    /// <summary>Blocked. Below the field everything is solid. With <paramref name="gates"/>, gates our people open don't count.</summary>
    public bool Solid(int i, int y, int k, bool gates = false)
    {
        if (y < 0) return true;
        if (!In(i, y, k)) return false;
        int c = Id(i, y, k);
        return Fill[c] - (gates ? GateFill[c] : 0) >= SolidT;
    }

    /// <summary>Free to be in. Outside the field's sides nothing is; above the field the sky is.</summary>
    public bool Open(int i, int y, int k, bool gates = false)
    {
        if ((uint)i >= (uint)NX || (uint)k >= (uint)NZ || y < 0) return false;
        if (y >= NY) return true;
        int c = Id(i, y, k);
        return Fill[c] - (gates ? GateFill[c] : 0) < SolidT;
    }

    /// <summary>Someone h cells tall can stand here: ground below, room for the body, no deep water.</summary>
    public bool Stand(int i, int y, int k, int h, bool gates = false)
    {
        if (!In(i, y, k) || y < 1 || !Solid(i, y - 1, k, gates) || Water[Id(i, y, k)] >= DeepT) return false;
        for (int d = 0; d < h; d++) if (!Open(i, y + d, k, gates)) return false;
        return true;
    }

    /// <summary>Someone h cells tall can be here: standing on the ground or (if it takes ladders) hanging on a ladder.</summary>
    public bool CanBe(int i, int y, int k, int h, bool gates = false, bool ladders = true)
    {
        if (!In(i, y, k) || y < 1) return false;
        int c = Id(i, y, k);
        if (Water[c] >= DeepT || (!(ladders && Ladder[c] != 0) && !Solid(i, y - 1, k, gates))) return false;
        for (int d = 0; d < h; d++) if (!Open(i, y + d, k, gates)) return false;
        return true;
    }

    public bool Deep(int c) => Water[c] >= DeepT;

    /// <summary>The cell a voxel position belongs to.</summary>
    public int CellOf(float x, float y, float z) => Id(Math.Clamp((int)x >> Shift, 0, NX - 1), Math.Clamp((int)y >> Shift, 0, NY - 1), Math.Clamp((int)z >> Shift, 0, NZ - 1));

    /// <summary>Where the feet of someone in cell c are, in voxel coordinates.</summary>
    public (float x, float y, float z) Feet(int c) => ((I(c) << Shift) + 4f, Floor[c], (K(c) << Shift) + 4f);

    void OnEdit(int x, int y, int z)
    {
        int i = x >> Shift, cy = y >> Shift, k = z >> Shift;
        Mark(Id(i, cy, k));
        if (cy + 1 < NY) Mark(Id(i, cy + 1, k));
    }

    void Mark(int c)
    {
        if (_dirty[c]) return;
        _dirty[c] = true;
        _dirtyList.Add(c);
    }

    public void BuildAll()
    {
        Parallel.For(0, NY, y =>
        {
            for (int k = 0; k < NZ; k++)
                for (int i = 0; i < NX; i++) Recompute(Id(i, y, k));
        });
        Version++;
    }

    /// <summary>Recounts the cells edited since the last call; returns how many of them opened or closed.</summary>
    public int Flush()
    {
        int changed = 0;
        foreach (int c in _dirtyList)
        {
            _dirty[c] = false;
            bool was = Fill[c] >= SolidT, ladder = Ladder[c] != 0, deep = Water[c] >= DeepT, spiked = Spikes[c] != 0;
            Recompute(c);
            if ((Fill[c] >= SolidT) != was || (Ladder[c] != 0) != ladder || (Water[c] >= DeepT) != deep || (Spikes[c] != 0) != spiked) changed++;
        }
        _dirtyList.Clear();
        if (changed > 0) Version++;
        return changed;
    }

    public int PendingEdits => _dirtyList.Count;

    void Recompute(int c)
    {
        int i = I(c), k = K(c), y = Y(c), x0 = i << Shift, y0 = y << Shift, z0 = k << Shift;
        int ci = World.ChunkIndex(x0 >> VoxelWorld.Shift, y0 >> VoxelWorld.Shift, z0 >> VoxelWorld.Shift);
        var d = World.ChunkData(ci);
        int fill = 0, e = 0, wd = 0, st = 0, rk = 0, hard = 0, keep = 0, lad = 0, wat = 0, gate = 0, spk = 0;
        if (d == null)
        {
            byte u = World.ChunkUniform(ci), cls = Cls[u];
            if (cls != 0) Tally(cls, CS * CS * CS, ref fill, ref e, ref wd, ref st, ref rk, ref hard, ref keep);
            else if (u == Mat.Water) wat = CS * CS * CS;
        }
        else
        {
            for (int yy = 0; yy < CS; yy++)
                for (int zz = 0; zz < CS; zz++)
                {
                    int row = (((y0 + yy) & VoxelWorld.Mask) << 10) | (((z0 + zz) & VoxelWorld.Mask) << 5) | (x0 & VoxelWorld.Mask);
                    for (int xx = 0; xx < CS; xx++)
                    {
                        byte v = d[row + xx], cls = Cls[v];
                        if (cls != 0)
                        {
                            Tally(cls, 1, ref fill, ref e, ref wd, ref st, ref rk, ref hard, ref keep);
                            // a leaf voxel shuts twice its share of the cell, but takes no longer to hack
                            if (Mat.IsLeaf(v)) fill++;
                            if (v == Mat.Gate) gate += 2;
                        }
                        // pitch is waded and drowned in like water
                        else if (v == Mat.Water || v == Mat.Pitch) wat++;
                        else if (Mat.IsLadder[v]) lad++;
                        else if (v == Mat.Stakes) spk++;
                    }
                }
        }
        Fill[c] = (ushort)fill; Earth[c] = (ushort)e; Wood[c] = (ushort)wd; Stone[c] = (ushort)st; Rock[c] = (ushort)rk;
        Hard[c] = (byte)(hard > 0 ? 1 : 0);
        Keep[c] = (byte)(keep >= 64 ? 1 : 0);
        Ladder[c] = (byte)(lad >= 6 ? 1 : 0);
        Water[c] = (ushort)wat;
        GateFill[c] = (ushort)gate;
        Spikes[c] = (ushort)spk;
        Floor[c] = (ushort)FloorIn(x0, y0, z0);
    }

    static void Tally(byte cls, int n, ref int fill, ref int e, ref int wd, ref int st, ref int rk, ref int hard, ref int keep)
    {
        fill += n;
        switch (cls)
        {
            case 1: e += n; break;
            case 2: wd += n; break;
            case 3: st += n; break;
            case 4: rk += n; break;
            case 5: hard += n; break;
            default: hard += n; keep += n; break;
        }
    }

    /// <summary>
    /// The highest top of solid ground among the cell's middle columns, from half a cell below to near its top.
    /// When the middle is open (the cell below holds only the edge of a wall) the feet rest on that edge instead.
    /// </summary>
    int FloorIn(int x0, int y0, int z0)
    {
        int best = -1;
        for (int q = 0; q < 4; q++) best = Math.Max(best, TopIn(x0 + 3 + (q & 1), z0 + 3 + (q >> 1), y0, best));
        if (best >= 0) return best;
        for (int zz = 0; zz < CS; zz++)
            for (int xx = 0; xx < CS; xx++) best = Math.Max(best, TopIn(x0 + xx, z0 + zz, y0, best));
        return best >= 0 ? best : y0;
    }

    int TopIn(int x, int z, int y0, int best)
    {
        for (int yy = y0 + 5; yy >= y0 - 8 && yy > best - 1; yy--)
        {
            if (yy < 0) return 0;
            if (Cls[World.Get(x, yy, z)] != 0) return yy + 1;
        }
        return -1;
    }
}
