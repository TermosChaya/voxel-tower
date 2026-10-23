using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// Building so that nobody is walled in. Before a pawn lays the voxel that shuts a cell of the way — a course of a
/// wall grown past what the legs climb, the last of a gap — it tries the wall as it will stand: whether each of ours
/// near it could still walk out to the open valley, or to the citadel's door, with that cell shut. If the pawn itself
/// would be shut in, it lays all the rest first, goes out through the gap left and fills it from outside; if another
/// would be, the gap waits till that one is out, and a pawn with nothing more to do in a space being walled shut walks
/// out of it by itself. So a ring of wall goes up all round, and its last gap is shut from outside. Shut in is as in
/// Sim.Escape.cs: fewer cells to walk to than <see cref="RoomEnough"/>, and the citadel's door not among them. Nothing
/// of it is kept in a save: the wall is tried again as it goes up.
/// </summary>
public sealed partial class Sim
{
    // the spaces being walled shut, cell by cell, and till when: nobody stays in them, nor stands in them to build
    readonly Dictionary<int, double> _closing = new();
    // the cells whose shutting waits, till when, and why (the worker's word)
    readonly Dictionary<int, (double until, string why)> _gaps = new();
    readonly List<int> _shutRoom = new();
    // what blocks each cell the pawns lay into this step: the grid counts the work a step late, and one blow of a tool
    // can lay a hundred voxels
    readonly Dictionary<int, int> _filling = new();
    double _fillingAt = -1;
    readonly HashSet<int> _freeCells = new(), _walked = new();
    readonly Queue<int> _walk = new();

    bool Closing(int c) => _closing.Count > 0 && _closing.TryGetValue(c, out double until) && Time < until;

    bool Held(int c, out string why)
    {
        why = "";
        if (_gaps.Count == 0 || !_gaps.TryGetValue(c, out var h) || Time >= h.until) return false;
        why = h.why;
        return true;
    }

    /// <summary>
    /// Whether laying this voxel now would shut one of ours in: it is the voxel that shuts its cell, and with that cell
    /// shut someone near could no longer walk out. Then the cell waits a little, and the space is marked to be left.
    /// </summary>
    bool ShutsIn(Unit u, int x, int y, int z, byte m)
    {
        if (!NavGrid.Blocks(m) || m == Mat.Gate) return false;
        int c = Nav.CellOf(x, y, z);
        if (Held(c, out _)) return true;
        // only the voxel that tips the cell from open to shut counts
        int now = Filling(c);
        if (now + 1 < NavGrid.SolidT || now >= NavGrid.SolidT) return false;
        return WouldShut(u, c);
    }

    /// <summary>What blocks a cell now: the grid's count at the start of the step, and what was laid into it since.</summary>
    int Filling(int c)
    {
        if (_fillingAt != Time) { _filling.Clear(); _fillingAt = Time; }
        return _filling.TryGetValue(c, out int n) ? n : Nav.Fill[c] - Nav.GateFill[c];
    }

    /// <summary>A voxel laid by a pawn, counted into its cell for the rest of the step.</summary>
    void Filled(int x, int y, int z, byte m)
    {
        if (!NavGrid.Blocks(m) || m == Mat.Gate) return;
        int c = Nav.CellOf(x, y, z);
        _filling[c] = Filling(c) + (Mat.IsLeaf(m) ? 2 : 1);
    }

    /// <summary>Whether shutting cell c would shut the one laying, or one of ours near it, in; if so the cell is held and the space marked.</summary>
    bool WouldShut(Unit u, int c)
    {
        int ci = Nav.I(c), ck = Nav.K(c), cy = Nav.Y(c);
        _freeCells.Clear();
        Unit shut = ShutBy(u, c) ? u : null;
        if (shut == null)
            foreach (var o in Units)
            {
                if (o == u || o.Dead || o.Team != Team.Player || o.Inside || o.Airborne != null || o.Captor != null) continue;
                if (Math.Abs(Nav.I(o.Cell) - ci) > 12 || Math.Abs(Nav.K(o.Cell) - ck) > 12 || Math.Abs(Nav.Y(o.Cell) - cy) > 6) continue;
                if (_freeCells.Contains(o.Cell) || !ShutBy(o, c)) continue;
                shut = o;
                break;
            }
        if (shut == null) return false;
        // the old marks go: a space shut long since, a gap filled
        if (_closing.Count > 2000) _closing.Clear();
        foreach (int r in _shutRoom) _closing[r] = Time + 30;
        _gaps[c] = (Time + 3, shut == u ? "оставляет проход, чтобы не замуровать себя" : $"оставляет проход: внутри {shut.T.Name.ToLowerInvariant()}");
        if (_gaps.Count > 200) foreach (var key in new List<int>(_gaps.Keys)) if (Time >= _gaps[key].until) _gaps.Remove(key);
        return true;
    }

    /// <summary>Whether cell c shut would shut this one in, as nothing shuts it in now; the space it would be left in, in _shutRoom.</summary>
    bool ShutBy(Unit o, int c)
    {
        _shutRoom.Clear();
        if (WalksOut(o.Cell, o.T.Move, c, _shutRoom)) { foreach (int r in _shutRoom) _freeCells.Add(r); return false; }
        // shut in already (a pit, a tower): not this voxel's doing, and Sim.Escape.cs sees to it
        return WalksOut(o.Cell, o.T.Move, -1, null);
    }

    /// <summary>
    /// Whether a body could walk from a cell to <see cref="RoomEnough"/> cells or to the citadel's door with the cell
    /// <paramref name="shut"/> (if any) shut as well, by the steps <see cref="PathFinder"/> takes, square to the grid (a
    /// corner is cut only where both its sides are open anyway); the cells walked to, into a list if one is given.
    /// </summary>
    bool WalksOut(int start, Mover m, int shut, List<int> into)
    {
        _walked.Clear(); _walk.Clear();
        _walked.Add(start); _walk.Enqueue(start);
        into?.Add(start);
        while (_walk.Count > 0)
        {
            int c = _walk.Dequeue();
            if (c == Gate || _walked.Count >= RoomEnough) return true;
            int i = Nav.I(c), y = Nav.Y(c), k = Nav.K(c);
            for (int dir = 0; dir < 4; dir++)
            {
                int ni = i + WayDX[dir], nk = k + WayDK[dir];
                // level first, then a step up, then the shortest drop, as the legs manage
                for (int s = 0; s <= m.Climb + m.Drop; s++)
                {
                    int dy = s <= m.Climb ? s : m.Climb - s, ny = y + dy;
                    if (!Be(ni, ny, nk, m, shut)) continue;
                    int n = Nav.Id(ni, ny, nk);
                    if (!FlowField.StepsOk(m, FeetAt(c, shut), FeetAt(n, shut))) continue;
                    if (dy > 0 && !Clear(i, k, y + m.Height, y + m.Height + dy - 1, shut)) continue;
                    if (dy < 0 && !Clear(ni, nk, ny + m.Height, y + m.Height - 1, shut)) continue;
                    if (_walked.Add(n)) { _walk.Enqueue(n); into?.Add(n); }
                    break;
                }
            }
            if (!m.Ladders) continue;
            if (y + 1 < Nav.NY && (Nav.Ladder[c] != 0 || Nav.Ladder[c + Nav.Layer] != 0) && Be(i, y + 1, k, m, shut) && _walked.Add(c + Nav.Layer))
            { _walk.Enqueue(c + Nav.Layer); into?.Add(c + Nav.Layer); }
            if (y >= 2 && (Nav.Ladder[c] != 0 || Nav.Ladder[c - Nav.Layer] != 0) && Be(i, y - 1, k, m, shut) && _walked.Add(c - Nav.Layer))
            { _walk.Enqueue(c - Nav.Layer); into?.Add(c - Nav.Layer); }
        }
        return false;
    }

    /// <summary>A body can be in this cell (standing, or on a ladder) with the cell <paramref name="shut"/> shut too: <see cref="NavGrid.CanBe"/> so.</summary>
    bool Be(int i, int y, int k, Mover m, int shut)
    {
        if (!Nav.In(i, y, k) || y < 1) return false;
        int c = Nav.Id(i, y, k);
        if (c == shut || Nav.Water[c] >= NavGrid.DeepT) return false;
        if (!(m.Ladders && Nav.Ladder[c] != 0) && c - Nav.Layer != shut && !Nav.Ground(i, y - 1, k, true)) return false;
        for (int d = 0; d < m.Height; d++) if (!Air(i, y + d, k, shut)) return false;
        return true;
    }

    bool Air(int i, int y, int k, int shut) => Nav.Open(i, y, k, true) && !(y < Nav.NY && Nav.In(i, y, k) && Nav.Id(i, y, k) == shut);

    bool Clear(int i, int k, int y0, int y1, int shut)
    {
        for (int y = y0; y <= y1; y++) if (!Air(i, y, k, shut)) return false;
        return true;
    }

    /// <summary>Where the feet rest in a cell; on a cell shut under it, at its foot.</summary>
    int FeetAt(int c, int shut) => c - Nav.Layer == shut ? Nav.Y(c) << NavGrid.Shift : Nav.Floor[c];

    /// <summary>
    /// A pawn with nothing in hand in a space being walled shut walks out of it, to where it is not shut in by the gap
    /// filled; true when it is on its way.
    /// </summary>
    bool GetOut(Unit u)
    {
        if (!Closing(u.Cell)) return false;
        u.NextSearch = Time + 1;
        var path = Paths.Find(u.Cell, c => !Closing(c) && !Held(c, out _) && !Held(c - Nav.Layer, out _), _ => 0, u.T.Move, 3000, gates: true);
        if (path == null || path.Count == 0) return false;
        u.Path = path; u.PathI = 0;
        u.Why = "выходит, пока проход не заложен";
        return true;
    }
}
