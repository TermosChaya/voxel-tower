using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// The town furnishes itself as it grows (the author: «Больше декораций богу декораций»). Once three houses or more are
/// lived in, now and then, while the workers have little else marked to do and the stores are not low, one more thing of
/// the «Обустройство» tool's is set out between the houses, as many of each as the town has houses for: benches and lamps
/// first, flower beds and pots, a well, a notice board, a shrine, hay and a trough by the stables, a stall by the
/// market, a coop, a barrow, a statue once it is a town. Each is marked for the workers like the player's and built of
/// the stores; it stands a metre or so from walls and stairs, off the ways and the roads, clear of the doors, apart from
/// the others, on level ground. The player's own count with the rest.
/// </summary>
public sealed partial class Sim
{
    double _nextTownDecor;
    /// <summary>Whether the town sets things out by itself (the switch in the town's tab, on unless turned off).</summary>
    public bool TownDecorAuto = true;
    /// <summary>The houses lived in that make a town that furnishes itself.</summary>
    public const int TownDecorHouses = 3;
    /// <summary>Things the town set out by itself this game (the tests ask).</summary>
    public int TownDecorSet;
    bool _townDecorSaid;

    /// <summary>Every half a minute or so: one more thing set out, if the town wants one and has room for it.</summary>
    void TickTownDecor()
    {
        if (Time < _nextTownDecor) return;
        // (not of the sim's dice: a thing set out or not changes nothing else drawn from them)
        _nextTownDecor = Time + 30 + Hash.H3((int)Time, 61, 7) * 15;
        if (!TownDecorAuto || Alarm || Raids.Active || Orders.Total > 60) return;
        if (Stock[Stockpile.Wood] < 12 * VoxelWorld.PerM3 || Stock[Stockpile.Stone] < 12 * VoxelWorld.PerM3) return;
        SetOutTownThing();
    }

    /// <summary>
    /// The thing the town wants most now and a spot for it, set out: true if one was. How many of each it wants grows
    /// with its houses (dwellings drawn and closed) and its buildings.
    /// </summary>
    public bool SetOutTownThing()
    {
        // (a town of houses lived in: three at least, drawn by the player, the town's buildings by themselves not a town)
        int houses = 0;
        foreach (var z in Zones) if (LivedIn(z)) houses++;
        if (houses < TownDecorHouses) return false;
        var have = new Dictionary<DecorKind, int>();
        foreach (var s in Yards) have[s.Kind] = have.GetValueOrDefault(s.Kind) + 1;
        bool Has(string kind) => Buildings.Exists(b => b.Ready && b.Kind == kind);
        // what it wants of each: so many to its houses, a few things once it is big enough
        var wants = new List<(DecorKind k, int n)>
        {
            (DecorKind.Bench, (houses + 2) / 3), (DecorKind.LampPost, (houses + 1) / 2), (DecorKind.FlowerBed, houses / 3),
            (DecorKind.Pots, houses / 4), (DecorKind.Well, houses >= 4 ? 1 + houses / 10 : 0), (DecorKind.NoticeBoard, houses >= 6 || Has("hall") ? 1 : 0),
            (DecorKind.Shrine, houses >= 5 ? 1 : 0), (DecorKind.Coop, houses / 5), (DecorKind.Barrow, houses / 6),
            (DecorKind.HayBales, Has("stables") ? 2 : houses / 7), (DecorKind.Trough, Has("stables") || Has("kennel") ? 1 : houses / 9),
            (DecorKind.Stall, Has("market") ? 2 : houses >= 8 ? 1 : 0), (DecorKind.Statue, houses >= 10 ? 1 : 0),
            (DecorKind.Signpost, houses >= 6 ? 1 : 0), (DecorKind.Woodpile, houses / 6),
        };
        // the most wanted first (the most short of what it wants, the list's order among equals)
        wants.Sort((a, b) => (b.n - have.GetValueOrDefault(b.k)).CompareTo(a.n - have.GetValueOrDefault(a.k)));
        foreach (var (k, n) in wants)
        {
            if (have.GetValueOrDefault(k) >= n) break;
            if (TownSpot(k, out int x, out int z, out int rot) && PlaceDecor(k, x, z, rot) == null)
            {
                TownDecorSet++;
                if (!_townDecorSaid)
                {
                    _townDecorSaid = true;
                    Say("Город растёт: рабочие сами ставят между домами скамьи, фонари, клумбы и другие вещи. Лишнее можно снести.", 1, x, z);
                }
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// A spot for a thing of this kind: about a house or a building it suits (a stall by the market, hay and a trough by
    /// the stables, the rest by the houses), a few metres out from its walls, where the thing has room. The nearest the
    /// citadel of those found, turned to face the house or the building.
    /// </summary>
    bool TownSpot(DecorKind k, out int bx, out int bz, out int brot)
    {
        bx = bz = brot = 0;
        var around = new List<(int x0, int z0, int x1, int z1)>();
        string near = k switch { DecorKind.Stall => "market", DecorKind.HayBales or DecorKind.Trough => "stables", DecorKind.NoticeBoard => "hall", _ => null };
        if (near != null) foreach (var b in Buildings) if (b.Ready && b.Kind == near) around.Add((b.X0, b.Z0, b.X1, b.Z1));
        if (around.Count == 0)
            foreach (var z in Zones) if (LivedIn(z)) around.Add((z.X0, z.Z0, z.X1, z.Z1));
        float best = float.MaxValue;
        bool found = false;
        // (each of them looked about in turn, starting from one the clock picks, so that the things spread over the town)
        int from = around.Count == 0 ? 0 : (int)(Time / 7) % around.Count;
        for (int i = 0; i < around.Count; i++)
        {
            var (x0, z0, x1, z1) = around[(from + i) % around.Count];
            for (int d = 20; d <= 52; d += 16)
                for (int side = 0; side < 4; side++)
                {
                    var (nx, nz) = Shapes.SideNormal(side);
                    bool alongX = nz != 0;
                    int a0 = alongX ? x0 : z0, a1 = alongX ? x1 : z1;
                    for (int a = a0; a <= a1; a += 12)
                    {
                        int x = alongX ? a : (nx < 0 ? x0 - d : x1 + d), z = alongX ? (nz < 0 ? z0 - d : z1 + d) : a;
                        // turned to face the house: its +z toward it
                        int rot = side switch { 0 => 0, 1 => 2, 2 => 3, _ => 1 };
                        if (!TownRoom(k, x, z, rot)) continue;
                        float dk = (x - Site.KeepX) * (x - Site.KeepX) + (z - Site.KeepZ) * (z - Site.KeepZ);
                        if (dk < best) { best = dk; bx = x; bz = z; brot = rot; found = true; }
                    }
                }
            if (found) return true;
        }
        return found;
    }

    /// <summary>Whether a thing set out here, so turned, would stand: its plan good, off the ways, apart from the rest, clear of walls and doors.</summary>
    bool TownRoom(DecorKind k, int x, int z, int rot)
    {
        if (!World.InBounds(x, 1, z) || !World.OnLand(x, z) || Site.Forbidden(x, z) || Site.InStrip(x + .5f, z + .5f)) return false;
        // apart from the others: five metres, eight from one of its own kind
        foreach (var s in Yards)
        {
            float dx = s.X - x, dz = s.Z - z, apart = s.Kind == k ? 64 : 40;
            if (dx * dx + dz * dz < apart * apart) return false;
        }
        var plan = Yard.Plan(World, Site, k, x, z, rot, out _);
        if (plan == null) return false;
        int floor = World.SurfAt(x, z);
        for (int i = 0; i < plan.Count; i++)
        {
            int px = plan.X(i), pz = plan.Z(i);
            if (KeptClear(px, pz) || InYard(px, pz) || ZoneAt(px, pz) != null || BuildingAt(px, pz) != null) return false;
            // three quarters of a metre from anything built or marked, so that it closes no way between them nor the
            // foot of a stair or a ladder
            for (int dz = -6; dz <= 6; dz += 3)
                for (int dx = -6; dx <= 6; dx += 3)
                    for (int y = floor; y < floor + 16; y += 3)
                    {
                        byte m = World.Get(px + dx, y, pz + dz);
                        if (m != Mat.Air && Mat.IsPlayer[m] && !Mat.IsDecor[m] || Orders.At(px + dx, y, pz + dz) != 0) return false;
                    }
        }
        return true;
    }
}
