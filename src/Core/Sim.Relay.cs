using System.Collections.Generic;
using System.IO;

namespace VoxelTower.Core;

/// <summary>
/// Laying a building over again in another material (the magic wand with a material in hand): each voxel of it is
/// marked to be taken down, and kept in mind with what is to go in its place; once the workers have it down, the new
/// material is marked there to be laid. Taken back (the marking undone, or cleared) before it came down, it is
/// forgotten. Kept in the save, so that a wall half laid over again is not left half gone.
/// </summary>
public sealed partial class Sim
{
    /// <summary>Where a voxel is to be laid again, and in what, once it is taken down.</summary>
    public readonly Dictionary<int, byte> Relays = new();
    float _relayDue;

    /// <summary>
    /// Marks what of these voxels is ours to be laid again in this material (stone in courses, as stone is laid);
    /// returns how many. The citadel's, nature's, and what is already of that material stay as they are.
    /// </summary>
    public int Relay(VoxelList L, byte mat)
    {
        if (L == null) return 0;
        var take = new VoxelList();
        for (int i = 0; i < L.Count; i++)
        {
            int x = L.X(i), y = L.Y(i), z = L.Z(i);
            byte m = World.Get(x, y, z);
            if (!Mat.IsPlayer[m] || Site.Forbidden(x, z) || Shapes.Family(m) == Shapes.Family(mat)) continue;
            take.Add(x, y, z, m);
        }
        int n = DesignateRemove(take);
        // what was marked is remembered; what was marked to come down already keeps its old wish too
        for (int i = 0; i < take.Count; i++)
        {
            int x = take.X(i), y = take.Y(i), z = take.Z(i);
            if (Orders.IsRemove(Orders.At(x, y, z)) || InstantLabour) Relays[WaterPlanner.Pack(x, y, z)] = Shapes.Lay(mat, x, y, z);
        }
        if (InstantLabour) TickRelay(1);
        return n;
    }

    /// <summary>Twice a second: what has come down is marked to be laid again; what is no longer to come down is let go.</summary>
    void TickRelay(float dt)
    {
        if (Relays.Count == 0) return;
        _relayDue -= dt;
        if (_relayDue > 0 && !InstantLabour) return;
        _relayDue = .5f;
        List<int> done = null;
        var lay = new VoxelList();
        foreach (var (p, m) in Relays)
        {
            var (x, y, z) = WaterPlanner.Unpack(p);
            byte now = World.Get(x, y, z);
            if (Brushes.Free(now))
            {
                lay.Add(x, y, z, m);
                (done ??= new List<int>()).Add(p);
            }
            // still standing and no longer marked to come down: the player took it back
            else if (!Orders.IsRemove(Orders.At(x, y, z))) (done ??= new List<int>()).Add(p);
        }
        if (done != null) foreach (int p in done) Relays.Remove(p);
        if (lay.Count > 0) DesignateBuild(lay);
    }

    void WriteRelays(BinaryWriter w)
    {
        w.Write(Relays.Count);
        foreach (var (p, m) in Relays) { w.Write(p); w.Write(m); }
    }

    void ReadRelays(BinaryReader r)
    {
        Relays.Clear();
        for (int i = r.ReadInt32(); i > 0; i--) { int p = r.ReadInt32(); Relays[p] = r.ReadByte(); }
    }
}
