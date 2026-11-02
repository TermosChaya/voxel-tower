using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>A storey waiting to go up on a house: the house's rectangle, its walls' top, the walls' material, and since when.</summary>
public struct StoreyPlan { public int X0, Z0, X1, Z1, Top; public byte Mat; public double Since; }

/// <summary>
/// A house drawn over a house: a second storey. The rectangle drawn is taken as the house under it when it comes within
/// half a metre of its walls; otherwise, or over a dwelling framed by hand, or over a house of two storeys already, the
/// player is told why not and nothing is marked. First the roof and the gables are taken down; once nothing is left
/// over the walls' top, the storey is marked: a ladder against a long wall inside, a floor of logs over the room below
/// in the top courses of its walls with a hatch over the ladder, the walls going on up, and a roof of their own
/// (Shapes.Storey). It is one dwelling with the floor below, furnished on both floors.
/// </summary>
public sealed partial class Sim
{
    public readonly List<StoreyPlan> Storeys = new();
    double _nextStoreys;

    /// <summary>What of a house drawn falls on a dwelling already: the dwelling, or null if it falls on none.</summary>
    public Zone HouseUnder(int x0, int z0, int x1, int z1)
    {
        if (x1 < x0) (x0, x1) = (x1, x0);
        if (z1 < z0) (z0, z1) = (z1, z0);
        foreach (var z in Zones)
            if (z.Kind == Zone.Dwelling && z.X0 <= x1 && z.X1 >= x0 && z.Z0 <= z1 && z.Z1 >= z0) return z;
        return null;
    }

    /// <summary>
    /// A storey over the house a rectangle was drawn on, if one can go up there: its roof marked to be taken down, the
    /// storey waiting for that. Null if so, else why not. A house has two storeys at most; the town hall raised to the
    /// Capitol has three (<paramref name="storeys"/>, Sim.Town.cs).
    /// </summary>
    public string PlanStorey(Zone house, int x0, int z0, int x1, int z1, byte mat, int storeys = 2)
    {
        if (x1 < x0) (x0, x1) = (x1, x0);
        if (z1 < z0) (z0, z1) = (z1, z0);
        const int Near = 4;
        if (Math.Abs(x0 - house.X0) > Near || Math.Abs(x1 - house.X1) > Near || Math.Abs(z0 - house.Z0) > Near || Math.Abs(z1 - house.Z1) > Near)
            return "второй этаж встаёт ровно на дом: рисуйте по его стенам";
        int top = house.Depth;
        int ground = World.GroundTop((house.X0 + house.X1) / 2, (house.Z0 + house.Z1) / 2);
        // (a town building's first walls may stand lower or higher than a house's, Sim.Town.cs)
        if (top <= 1 || top - ground < Shapes.HouseHeight - 4) return "надстроить можно дом, нарисованный прямоугольником";
        if (top - ground > (storeys - 2) * Shapes.HouseHeight + Shapes.TallestRise + 8) return storeys > 2 ? "выше трёх этажей не строят" : "выше двух этажей не строят";
        foreach (var s in Storeys) if (s.X0 == house.X0 && s.Z0 == house.Z0) return "второй этаж здесь уже размечен";
        // what is still marked to be laid up there is let go: it is coming down
        for (int z = house.Z0 - Shapes.Eaves; z <= house.Z1 + Shapes.Eaves; z++)
            for (int x = house.X0 - Shapes.Eaves; x <= house.X1 + Shapes.Eaves; x++)
                for (int y = top; y < Math.Min(World.SY - 1, top + Shapes.HouseHeight + 4); y++)
                {
                    byte o = Orders.At(x, y, z);
                    if (o != 0 && !Orders.IsRemove(o)) Orders.Clear(x, y, z);
                }
        DesignateRemove(Shapes.OverHouse(World, house.X0, house.Z0, house.X1, house.Z1, top));
        var plan = new StoreyPlan { X0 = house.X0, Z0 = house.Z0, X1 = house.X1, Z1 = house.Z1, Top = top, Mat = mat, Since = Time };
        Storeys.Add(plan);
        // the storey's ladder and its floor of logs go in first, under the roof still on: from that floor the workers
        // take the roof off from its ridge down (from the ground under the eaves the ridge of a wide or steep roof was
        // out of reach, and what came off it came off only while those who had built it still stood up there)
        var shape = StoreyShape(plan, house, out var arched, out var ladder, out _);
        var floor = new VoxelList();
        for (int i = 0; i < shape.Count; i++) if (shape.Y(i) < top) floor.Add(shape.X(i), shape.Y(i), shape.Z(i), shape.M(i));
        var floorArched = new VoxelList();
        for (int i = 0; i < arched.Count; i++) if (arched.Y(i) < top) floorArched.Add(arched.X(i), arched.Y(i), arched.Z(i), arched.M(i));
        DesignateBuild(ladder);
        Arch(floorArched);
        DesignateBuild(floor);
        return null;
    }

    /// <summary>
    /// Whether anything of the old roof (or else laid up over the walls) still stands to be taken down: all of it but the
    /// storey's own ladder, which goes up through its hatch into the room under the roof before the roof comes off.
    /// </summary>
    bool RoofLeft(StoreyPlan s)
    {
        var over = Shapes.OverHouse(World, s.X0, s.Z0, s.X1, s.Z1, s.Top);
        const int t = Shapes.HouseWall;
        for (int i = 0; i < over.Count; i++)
        {
            int x = over.X(i), z = over.Z(i);
            bool room = x - s.X0 >= t && s.X1 - x >= t && z - s.Z0 >= t && s.Z1 - z >= t;
            if (!(room && World.Get(x, over.Y(i), z) == Mat.Ladder)) return true;
        }
        return false;
    }

    /// <summary>The scaffolding standing within a storey's walls over the house's top, where the storey goes up.</summary>
    VoxelList ScaffoldOver(StoreyPlan s)
    {
        var L = new VoxelList();
        if (Scaffolds.Count == 0) return L;
        for (int y = s.Top; y < Math.Min(World.SY - 1, s.Top + Shapes.HouseHeight + 2); y++)
            for (int z = s.Z0; z <= s.Z1; z++)
                for (int x = s.X0; x <= s.X1; x++)
                    if (World.Get(x, y, z) == Mat.Scaffold) L.Add(x, y, z, Mat.Air);
        return L;
    }

    /// <summary>The storey a plan raises on its house as things stand: its ladder, its floor, its walls and roof (Shapes.Storey).</summary>
    VoxelList StoreyShape(StoreyPlan s, Zone zone, out VoxelList arched, out VoxelList ladder, out int newTop)
    {
        // its ladder against a long wall with no door in it, no eaves out over a house beside it (Sim.Houses.cs)
        // (a town building's walls go on up and its roof goes back up as its look has them)
        var b = BuildingOn(zone);
        // (a house drawn with the rectangle goes on up in the look its plot gives it, Sim.HouseLooks.cs)
        var look = b?.Look ?? HouseLookOf(zone, s.Mat);
        byte covering = look != null && look.Covering != 0 ? look.Covering : b?.K.Roof ?? 0;
        return Shapes.Storey(World, Site, s.X0, s.Z0, s.X1, s.Z1, s.Top, s.Mat, DoorSidesOf(zone), SharedSidesOf(zone), out arched, out ladder, out newTop, covering, look);
    }

    /// <summary>Every couple of seconds: a storey whose house has its roof off is marked out.</summary>
    void TickStoreys()
    {
        if (Storeys.Count == 0 || Time < _nextStoreys) return;
        _nextStoreys = Time + 2;
        for (int i = Storeys.Count - 1; i >= 0; i--)
        {
            var s = Storeys[i];
            var zone = Zones.Find(z => z.Kind == Zone.Dwelling && z.X0 == s.X0 && z.Z0 == s.Z0 && z.X1 == s.X1 && z.Z1 == s.Z1);
            // the house taken back meanwhile: nothing to go up on
            if (zone == null) { Storeys.RemoveAt(i); continue; }
            if (RoofLeft(s)) continue;
            // scaffolding still lashed up inside the walls' line to take the roof off comes down first: marked round it,
            // the walls went up with a gap of it in them, open once it came down (a trolls' den never closed again)
            var lashed = ScaffoldOver(s);
            if (lashed.Count > 0) { DesignateRemove(lashed); continue; }
            // (what of its ladder and floor is laid already, laid first under the roof, is left as it is)
            var plan = StoreyShape(s, zone, out var arched, out var ladder, out int newTop);
            // what of the roof came down into the room, or onto the new floor, as it was taken apart is carried off
            var debris = new VoxelList();
            for (int z = s.Z0; z <= s.Z1; z++)
                for (int x = s.X0; x <= s.X1; x++)
                    for (int y = s.Top - Shapes.TallestRise - 4; y < s.Top + 4; y++)
                        if (World.Get(x, y, z) is Mat.Lumber or Mat.Rubble) debris.Add(x, y, z, Mat.Air);
            if (debris.Count > 0) DesignateRemove(debris);
            DesignateBuild(ladder);
            Arch(arched);
            DesignateBuild(plan);
            zone.Depth = newTop;
            // (the old layout is kept, so that the new one, laid out for both floors once they are closed, takes away what
            // of it stands in its way: let go, a chest of the floor below stood for good where the new layout put a table)
            Storeys.RemoveAt(i);
            Say("Крыша снята: рабочие поднимают стены этажа.", 1, (s.X0 + s.X1) / 2f, (s.Z0 + s.Z1) / 2f);
        }
    }

    void WriteStoreys(System.IO.BinaryWriter w)
    {
        w.Write(Storeys.Count);
        foreach (var s in Storeys) { w.Write(s.X0); w.Write(s.Z0); w.Write(s.X1); w.Write(s.Z1); w.Write(s.Top); w.Write(s.Mat); w.Write(s.Since); }
    }

    void ReadStoreys(System.IO.BinaryReader r)
    {
        Storeys.Clear();
        for (int i = r.ReadInt32(); i > 0; i--)
            Storeys.Add(new StoreyPlan { X0 = r.ReadInt32(), Z0 = r.ReadInt32(), X1 = r.ReadInt32(), Z1 = r.ReadInt32(), Top = r.ReadInt32(), Mat = r.ReadByte(), Since = r.ReadDouble() });
    }
}
