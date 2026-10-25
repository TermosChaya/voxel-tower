using System;

namespace VoxelTower.Core;

/// <summary>
/// Water is finite and goes wherever the ground lets it, at the pace water runs (Sim.Liquids.cs): a ditch cut into a
/// pond, a bed dug out from under it, a gate opened in a dam let it pour out, run down slopes and over cliffs, and pool
/// in the lowest hollow it reaches; the pond drops by as much as ran out of it. Where it runs is remembered for a while,
/// for the current drawn on it.
/// </summary>
public sealed partial class Sim
{
    /// <summary>Voxels water has run into since the start.</summary>
    public int Flooded { get; private set; }

    /// <summary>
    /// When water last ran into each 4×4 patch of the valley (game seconds; far in the past where it never has): the
    /// way it spreads, earlier to later, is the way the current runs for a while after.
    /// </summary>
    public float[] WaterArrived;
    public const int FlowPatch = 4;
    /// <summary>When water last ran anywhere.</summary>
    public double LastPour = -1e9;

    void MarkArrival(int x, int z)
    {
        int w = World.SX / FlowPatch;
        if (WaterArrived == null) { WaterArrived = new float[w * (World.SZ / FlowPatch)]; Array.Fill(WaterArrived, -1e9f); }
        WaterArrived[z / FlowPatch * w + x / FlowPatch] = (float)Time;
        LastPour = Time;
    }

    /// <summary>Water still finding its level.</summary>
    public bool WaterBusy => Running(LiquidWater);
    /// <summary>Pitch still running.</summary>
    public bool PitchBusy => Running(LiquidPitch);
    /// <summary>Acid still running.</summary>
    public bool AcidBusy => Running(LiquidAcid);
    /// <summary>Lava still running.</summary>
    public bool LavaBusy => Running(LiquidLava);

    /// <summary>Every voxel written: the liquids answer it, and the acid keeps its count.</summary>
    void OnWaterEdit(int x, int y, int z, byte old)
    {
        OnLiquidEdit(x, y, z, old);
        OnAcidEdit(x, y, z, old);
        OnLavaEdit(x, y, z, old);
    }

    /// <summary>The liquids run, and what they meet answers them.</summary>
    void TickWater(float dt)
    {
        TickLiquids(dt);
    }
}

/// <summary>
/// Packs a voxel's place into one number (11 bits of x, 11 of z, 9 of y: a valley up to 2048 across and 512 high) and
/// back. It was 10 bits of x and z, and the valley of 1536 had its eastern and southern thirds folded onto the rest.
/// </summary>
static class WaterPlanner
{
    public static int Pack(int x, int y, int z) => (y << 22) | (z << 11) | x;
    public static (int x, int y, int z) Unpack(int p) => (p & 2047, (p >> 22) & 511, (p >> 11) & 2047);
}
