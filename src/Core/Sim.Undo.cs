using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// One stroke of the player's hand in the marking, written down so that Ctrl+Z can take it back: every order it set,
/// as it was before and as the stroke left it, the plots it laid out or took away, the gates it hung or mended. Only
/// the player's own hand goes in: the pawns finishing their work, the horde breaking things and the plots feeding out
/// their trees are none of it, and what is done stays done.
/// </summary>
public sealed class Marking
{
    /// <summary>What it was, for the chronicle: "Стена", "Копать", "Делянка".</summary>
    public string What = "";
    readonly Dictionary<int, int> _index = new();
    internal readonly List<int> At = new();
    internal readonly List<byte> Was = new(), Now = new();
    internal readonly List<Zone> Added = new(), Removed = new();
    internal readonly List<Gateway> Hung = new(), Mended = new();
    internal readonly List<Drawbridge> Drawn = new();
    internal readonly List<int> Relaid = new();

    public bool Empty => At.Count == 0 && Added.Count == 0 && Removed.Count == 0 && Hung.Count == 0 && Mended.Count == 0 && Drawn.Count == 0;
    /// <summary>How many voxels' orders it changed.</summary>
    public int Voxels => At.Count;

    /// <summary>An order changed at a voxel: the first time, what it was is kept; every time, what it is now.</summary>
    internal void Note(int at, byte was, byte now)
    {
        if (_index.TryGetValue(at, out int i)) { Now[i] = now; return; }
        _index[at] = At.Count;
        At.Add(at); Was.Add(was); Now.Add(now);
    }
}

public sealed partial class Sim
{
    readonly List<Marking> _undo = new();
    Marking _marking;
    /// <summary>How many of the player's markings are kept to be taken back.</summary>
    const int UndoKept = 40;

    /// <summary>How many of the player's markings can still be taken back.</summary>
    public int UndoDepth => _undo.Count + (_marking is { Empty: false } ? 1 : 0);

    /// <summary>The player's hand goes to the marking: what it does until <see cref="EndMarking"/> is one step to take back.</summary>
    public void BeginMarking()
    {
        EndMarking();
        _marking = new Marking();
        Orders.Writing = _marking;
    }

    /// <summary>The hand is lifted: the marking is kept for Ctrl+Z if it changed anything at all.</summary>
    public void EndMarking(string what = null)
    {
        if (_marking == null) return;
        if (what != null) _marking.What = what;
        Orders.Writing = null;
        if (!_marking.Empty)
        {
            _undo.Add(_marking);
            if (_undo.Count > UndoKept) _undo.RemoveAt(0);
        }
        _marking = null;
    }

    /// <summary>
    /// Takes back the player's last marking as far as it still can be: orders as they were, plots laid out taken away
    /// again (with the felling they had begun to mark) and plots taken away laid out again, gates not yet begun unhung.
    /// Returns the marking and how many voxels' orders changed, or null when there is nothing left to take back.
    /// </summary>
    public Marking UndoMarking(out int voxels)
    {
        EndMarking();
        voxels = 0;
        if (_undo.Count == 0) return null;
        var mk = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        voxels = Orders.Undo(mk);
        foreach (var z in mk.Added)
        {
            if (!Zones.Remove(z)) continue;
            voxels += Orders.CancelRemovals(z.X0, z.Z0, z.X1, z.Z1);
            ZonesVersion++;
        }
        foreach (var z in mk.Removed)
            if (!Zones.Contains(z)) { Zones.Add(z); ZonesVersion++; }
        foreach (int p in mk.Relaid) _relay.Remove(p);
        // a gate nothing of whose leaves is laid yet is no gate: hung, it goes; mended, it is broken again
        foreach (var g in mk.Hung)
            if (!g.Ready && !Begun(g)) Gates.Remove(g);
        foreach (var g in mk.Mended)
            if (!g.Ready && !Begun(g)) { g.Broken = true; g.Ready = true; }
        // so a drawbridge none of whose planks is laid yet
        foreach (var b in mk.Drawn)
            if (!b.Ready && !Begun(b)) Drawbridges.Remove(b);
        return mk;
    }

    /// <summary>Whether any of a drawbridge's planks is laid where it lies down.</summary>
    bool Begun(Drawbridge b)
    {
        for (int i = 0; i < b.Voxels; i++)
        {
            var (x, y, z) = b.Place(i, 0);
            if (World.Get(x, y, z) == Mat.DrawDeck) return true;
        }
        return false;
    }

    /// <summary>Whether any of a gate's leaves stands laid in its shut place.</summary>
    bool Begun(Gateway g)
    {
        for (int i = 0; i < Gateway.Voxels; i++)
        {
            var (x, y, z) = g.Place(i, 0);
            if (Mat.IsLeaf(World.Get(x, y, z))) return true;
        }
        return false;
    }
}
