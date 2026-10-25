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
    /// storey waiting for that. Null if so, else why not.
    /// </summary>
    public string PlanStorey(Zone house, int x0, int z0, int x1, int z1, byte mat)
    {
        if (x1 < x0) (x0, x1) = (x1, x0);
        if (z1 < z0) (z0, z1) = (z1, z0);
        const int Near = 4;
        if (Math.Abs(x0 - house.X0) > Near || Math.Abs(x1 - house.X1) > Near || Math.Abs(z0 - house.Z0) > Near || Math.Abs(z1 - house.Z1) > Near)
            return "второй этаж встаёт ровно на дом: рисуйте по его стенам";
        int top = house.Depth;
        int ground = World.GroundTop((house.X0 + house.X1) / 2, (house.Z0 + house.Z1) / 2);
        if (top <= 1 || top - ground < Shapes.HouseHeight - 4) return "надстроить можно дом, нарисованный прямоугольником";
        if (top - ground > Shapes.HouseHeight + 8) return "выше двух этажей не строят";
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
        Storeys.Add(new StoreyPlan { X0 = house.X0, Z0 = house.Z0, X1 = house.X1, Z1 = house.Z1, Top = top, Mat = mat, Since = Time });
        return null;
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
            if (Shapes.OverHouse(World, s.X0, s.Z0, s.X1, s.Z1, s.Top).Count > 0) continue;
            // its ladder against a long wall with no door in it, no eaves out over a house beside it (Sim.Houses.cs)
            var plan = Shapes.Storey(World, Site, s.X0, s.Z0, s.X1, s.Z1, s.Top, s.Mat, DoorSidesOf(zone), SharedSidesOf(zone), out var arched, out var ladder, out int newTop);
            // what of the roof came down into the room as it was taken apart is carried off
            var debris = new VoxelList();
            for (int z = s.Z0; z <= s.Z1; z++)
                for (int x = s.X0; x <= s.X1; x++)
                    for (int y = s.Top - Shapes.HouseHeight - 4; y < s.Top; y++)
                        if (World.Get(x, y, z) is Mat.Lumber or Mat.Rubble) debris.Add(x, y, z, Mat.Air);
            if (debris.Count > 0) DesignateRemove(debris);
            DesignateBuild(ladder);
            Arch(arched);
            DesignateBuild(plan);
            zone.Depth = newTop;
            _furnishing.Remove(zone.Id);
            Storeys.RemoveAt(i);
            Say("Крыша снята: рабочие ставят лестницу, настилают пол и поднимают второй этаж.", 1, (s.X0 + s.X1) / 2f, (s.Z0 + s.Z1) / 2f);
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
