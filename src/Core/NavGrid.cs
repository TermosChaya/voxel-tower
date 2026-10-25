using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace VoxelTower.Core;

/// <summary>
/// The walkable world on metre cells of 8³ voxels: how full each cell is and of what, and where feet rest in it.
/// Leaves, bushes, grass, ladders and water don't block; trunks, earth, masonry and rock do once a cell is a quarter full,
/// and a standing trunk through the middle of a cell does however thin it is: nobody stands inside a tree. Nor on one: a cell
/// shut by nothing but a tree that still stands is no ground, though a log lying on the ground is. A cell with a ladder in
/// it can be hung on and climbed; a cell more than a third full of water drowns whoever stands there. A floor laid thin —
/// a deck of planks a voxel thick over a pit, a roof of logs — is ground to stand and walk on, though it is no wall.
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
    /// <summary>
    /// The cell is shut by nothing but a tree that still stands: its trunk going on up through the cell, or its trunk or a
    /// bough a metre and more over the ground. That is no ground: nobody stands on it, up in a crown or on top of a butt.
    /// </summary>
    public readonly byte[] Tree;
    /// <summary>A straight run of trunk this tall stands: no log lies so thick.</summary>
    const int StandingRun = 12;
    /// <summary>
    /// The cell holds a floor laid thin: one course of it covers most of the cell (<see cref="DeckT"/> of its 64 columns)
    /// though the cell is not full enough to block. In the cell's upper half (<see cref="DeckHigh"/>) it is ground to the
    /// cell over it, as a full cell would be; in its lower half (<see cref="DeckLow"/>) it is to the cell itself what the
    /// top of the turf is, ground under the cell's own floor, as if the cell under it were full.
    /// </summary>
    public readonly byte[] Deck;
    /// <summary>The cell has a way laid or trodden in it (a quarter of its columns or more): walked faster, and chosen.</summary>
    public readonly byte[] Paved;
    public const byte DeckHigh = 1, DeckLow = 2;
    /// <summary>Columns of 64 one course of a cell must cover to be a floor to stand on.</summary>
    public const int DeckT = 40;

    readonly bool[] _dirty;
    readonly List<int> _dirtyList = new();

    /// <summary>Bumped whenever a cell turns from open to blocked or back.</summary>
    public int Version { get; private set; }

    // what each material is to someone walking or hacking: 0 nothing, 1 earth, 2 wood, 3 stone, 4 rock, 5 hard, 6 citadel,
    // 7 metal (counted as stone thrice over: the horde takes that long to hack through it)
    static readonly byte[] Cls = new byte[Mat.Count];

    static NavGrid()
    {
        for (int m = 1; m < Mat.Count; m++)
        {
            if (Mat.Def[m].Name == null || Mat.Passable[m]) continue;
            Cls[m] = Mat.Def[m].Group switch { MatGroup.Earth => 1, MatGroup.Wood => 2, MatGroup.Stone => 3, MatGroup.Rock => 4, MatGroup.Metal => 7, _ => 5 };
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
        Tree = new byte[Count];
        Deck = new byte[Count];
        Paved = new byte[Count];
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

    /// <summary>Ground to stand on: blocked, or floored with a thin deck, and not by a standing tree. Below the field everything is.</summary>
    public bool Ground(int i, int y, int k, bool gates = false)
    {
        if (y < 0) return true;
        if (!In(i, y, k)) return false;
        int c = Id(i, y, k);
        return (Fill[c] - (gates ? GateFill[c] : 0) >= SolidT || (Deck[c] & DeckHigh) != 0 || y + 1 < NY && (Deck[c + Layer] & DeckLow) != 0) && Tree[c] == 0;
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
        if (!In(i, y, k) || y < 1 || !Ground(i, y - 1, k, gates) || Water[Id(i, y, k)] >= DeepT) return false;
        for (int d = 0; d < h; d++) if (!Open(i, y + d, k, gates)) return false;
        return true;
    }

    /// <summary>Someone h cells tall can be here: standing on the ground or (if it takes ladders) hanging on a ladder.</summary>
    public bool CanBe(int i, int y, int k, int h, bool gates = false, bool ladders = true)
    {
        if (!In(i, y, k) || y < 1) return false;
        int c = Id(i, y, k);
        if (Water[c] >= DeepT || (!(ladders && Ladder[c] != 0) && !Ground(i, y - 1, k, gates))) return false;
        for (int d = 0; d < h; d++) if (!Open(i, y + d, k, gates)) return false;
        return true;
    }

    public bool Deep(int c) => Water[c] >= DeepT;

    /// <summary>Whether a material blocks the way once a cell holds enough of it (earth, wood, masonry, rock, leaves; not grass, ladders, water).</summary>
    public static bool Blocks(byte m) => Cls[m] != 0;

    /// <summary>The cell a voxel position belongs to.</summary>
    public int CellOf(float x, float y, float z) => Id(Math.Clamp((int)x >> Shift, 0, NX - 1), Math.Clamp((int)y >> Shift, 0, NY - 1), Math.Clamp((int)z >> Shift, 0, NZ - 1));

    /// <summary>Where the feet of someone in cell c are, in voxel coordinates.</summary>
    public (float x, float y, float z) Feet(int c) => ((I(c) << Shift) + 4f, Floor[c], (K(c) << Shift) + 4f);

    void OnEdit(int x, int y, int z)
    {
        int i = x >> Shift, cy = y >> Shift, k = z >> Shift;
        Mark(Id(i, cy, k));
        if (cy + 1 < NY) Mark(Id(i, cy + 1, k));
        // whether a cell holds a tree that still stands hangs on the trunk running on through the cells above and below
        // it, and on the ground under it: a trunk cut or a stump dug changes the cells that far round too
        if (Mat.IsTrunk(World.Get(x, y, z)) || Mat.IsTrunk(World.Get(x, y + 1, z)) || Mat.IsTrunk(World.Get(x, y - 1, z)))
            for (int d = -2; d <= 2; d++)
                if (d is < 0 or > 1 && cy + d >= 0 && cy + d < NY) Mark(Id(i, cy + d, k));
    }

    void Mark(int c)
    {
        if (_dirty[c]) return;
        _dirty[c] = true;
        _dirtyList.Add(c);
    }

    public void BuildAll()
    {
        // row by row of cells rather than layer by layer: the layers through the ground's surface are the slow ones, and
        // there are only some two dozen layers to share among the cores (a cell is counted from its own voxels alone)
        Parallel.For(0, NY * NZ, yk =>
        {
            int y = yk / NZ, k = yk % NZ;
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
            bool was = Fill[c] >= SolidT, ladder = Ladder[c] != 0, deep = Water[c] >= DeepT, spiked = Spikes[c] != 0, tree = Tree[c] != 0, deck = Deck[c] != 0;
            Recompute(c);
            if ((Fill[c] >= SolidT) != was || (Ladder[c] != 0) != ladder || (Water[c] >= DeepT) != deep || (Spikes[c] != 0) != spiked
                || (Tree[c] != 0) != tree || (Deck[c] != 0) != deck) changed++;
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
        int fill = 0, e = 0, wd = 0, st = 0, rk = 0, hard = 0, keep = 0, lad = 0, wat = 0, lava = 0, gate = 0, spk = 0, tv = 0, low = 0, high = 0, pav = 0;
        Span<int> trunk = stackalloc int[4];
        if (d == null)
        {
            byte u = World.ChunkUniform(ci), cls = Cls[u];
            if (cls != 0) Tally(cls, CS * CS * CS, ref fill, ref e, ref wd, ref st, ref rk, ref hard, ref keep);
            else if (u == Mat.Water) wat = CS * CS * CS;
        }
        else
        {
            for (int yy = 0; yy < CS; yy++)
            {
                // how many of the course's 64 columns something blocking (and not a tree's trunk) covers
                int covered = 0;
                for (int zz = 0; zz < CS; zz++)
                {
                    int row = (((y0 + yy) & VoxelWorld.Mask) << 10) | (((z0 + zz) & VoxelWorld.Mask) << 5) | (x0 & VoxelWorld.Mask);
                    for (int xx = 0; xx < CS; xx++)
                    {
                        byte v = d[row + xx], cls = Cls[v];
                        if (cls != 0)
                        {
                            Tally(cls, 1, ref fill, ref e, ref wd, ref st, ref rk, ref hard, ref keep);
                            if (!Mat.IsTrunk(v)) covered++;
                            // a leaf voxel shuts twice its share of the cell, but takes no longer to hack
                            if (Mat.IsLeaf(v)) fill++;
                            if (v is Mat.Gate or Mat.IronGate) gate += 2;
                            if (Mat.IsPavement[v]) pav++;
                            if (Mat.IsTrunk(v))
                            {
                                tv++;
                                if ((uint)(xx - 3) < 2 && (uint)(zz - 3) < 2) trunk[(zz - 3) * 2 + xx - 3]++;
                            }
                        }
                        // pitch and acid are waded and drowned in like water; lava is not set foot in at all
                        else if (v == Mat.Water || v == Mat.Pitch || v == Mat.Acid) wat++;
                        else if (v == Mat.Lava) lava++;
                        else if (Mat.IsLadder[v]) lad++;
                        else if (v == Mat.Stakes) spk++;
                    }
                }
                if (yy < CS / 2) low = Math.Max(low, covered); else high = Math.Max(high, covered);
            }
        }
        // a trunk standing up through the middle of the cell (not a root, nor a bough across it)
        bool through = Math.Max(Math.Max(trunk[0], trunk[1]), Math.Max(trunk[2], trunk[3])) >= 6;
        // shut by nothing but the wood of a tree, and that tree still stands
        bool tree = tv > 0 && (through || fill >= SolidT) && fill - tv < SolidT && Standing(x0, y0, z0);
        if (through) fill = Math.Max(fill, SolidT);
        Fill[c] = (ushort)fill; Earth[c] = (ushort)e; Wood[c] = (ushort)wd; Stone[c] = (ushort)st; Rock[c] = (ushort)rk;
        Hard[c] = (byte)(hard > 0 ? 1 : 0);
        Keep[c] = (byte)(keep >= 64 ? 1 : 0);
        Ladder[c] = (byte)(lad >= 6 ? 1 : 0);
        Water[c] = (ushort)(lava > 0 ? Math.Max(wat + lava, DeepT) : wat);
        GateFill[c] = (ushort)gate;
        Spikes[c] = (ushort)spk;
        Paved[c] = (byte)(pav >= CS * CS / 4 ? 1 : 0);
        Tree[c] = (byte)(tree ? 1 : 0);
        Deck[c] = (byte)(fill >= SolidT ? 0 : (high >= DeckT ? DeckHigh : 0) | (low >= DeckT ? DeckLow : 0));
        Floor[c] = (ushort)FloorIn(x0, y0, z0);
    }

    /// <summary>
    /// Whether the wood in a cell is a tree that still stands rather than a log that lies: a trunk going on straight up
    /// through the cell taller than any log lies thick, or wood a metre and more over the ground beneath the cell's middle
    /// (a bough, or a trunk that has leaned away from where it rose).
    /// </summary>
    bool Standing(int x0, int y0, int z0)
    {
        for (int zz = 0; zz < CS; zz++)
            for (int xx = 0; xx < CS; xx++)
            {
                int x = x0 + xx, z = z0 + zz;
                // a run that tall comes out through the cell's top or its bottom
                if (Mat.IsTrunk(World.Get(x, y0, z)) && Run(x, y0, z) >= StandingRun) return true;
                if (Mat.IsTrunk(World.Get(x, y0 + CS - 1, z)) && Run(x, y0 + CS - 1, z) >= StandingRun) return true;
            }
        int ground = 0;
        for (int q = 0; q < 4; q++)
        {
            int x = x0 + 3 + (q & 1), z = z0 + 3 + (q >> 1), y = y0 - 1;
            // down through air, leaves and wood to what the tree grew from
            while (y >= 0 && (Cls[World.Get(x, y, z)] == 0 || Mat.IsTrunk(World.Get(x, y, z)))) y--;
            ground = Math.Max(ground, y + 1);
        }
        return y0 - ground >= CS;
    }

    /// <summary>How tall the straight run of trunk through a trunk voxel is, up to a standing one's.</summary>
    int Run(int x, int y, int z)
    {
        int n = 1;
        for (int yy = y + 1; n < StandingRun && Mat.IsTrunk(World.Get(x, yy, z)); yy++) n++;
        for (int yy = y - 1; n < StandingRun && yy >= 0 && Mat.IsTrunk(World.Get(x, yy, z)); yy--) n++;
        return n;
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
            case 7: st += 3 * n; break;
            default: hard += n; keep += n; break;
        }
    }

    /// <summary>
    /// The highest top of solid ground among the cell's middle columns, from half a cell below to near its top.
    /// When the middle is open (the cell below holds only the edge of a wall) the feet rest on that edge instead.
    /// Wood is a floor only where it lies on the ground: the top of a log, a stump or a root, not a standing trunk nor a
    /// bough or a twig in the air.
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
            byte m = World.Get(x, yy, z);
            if (Cls[m] != 0 && !(Mat.IsTrunk(m) && !Lies(x, yy, z))) return yy + 1;
        }
        return -1;
    }

    /// <summary>
    /// The top of wood lying on the ground, or of a root or a stump: no trunk straight over it, solid ground under it before
    /// a log's thickness, and if it is more than a metre up, no trunk rising on beside it (that is a log's flank, not the
    /// step of a leaning trunk).
    /// </summary>
    bool Lies(int x, int y, int z)
    {
        if (Mat.IsTrunk(World.Get(x, y + 1, z))) return false;
        int n = 1;
        while (n < StandingRun && Mat.IsTrunk(World.Get(x, y - n, z))) n++;
        if (n >= StandingRun || Cls[World.Get(x, y - n, z)] == 0) return false;
        return n <= CS || !Rises(x, y, z, StandingRun - n);
    }

    static readonly (int dx, int dz)[] Aside = { (0, 0), (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1) };

    /// <summary>Whether trunk goes on up from a voxel so many voxels more, straight or a voxel aside at each step.</summary>
    bool Rises(int x, int y, int z, int more)
    {
        for (; more > 0; more--, y++)
        {
            int q = 0;
            while (q < Aside.Length && !Mat.IsTrunk(World.Get(x + Aside[q].dx, y + 1, z + Aside[q].dz))) q++;
            if (q == Aside.Length) return false;
            x += Aside[q].dx; z += Aside[q].dz;
        }
        return true;
    }
}
