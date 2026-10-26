using System;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The «Обустройство» tool: the settlement's ways and furnishing. Its palette (in the bottom panel, {Form} goes on to
/// the next) has the kinds of way first — a way is drawn like a line along the ground, as wide as the width, and the
/// workers take the turf up under it and lay the way flush in its place (Sim.Paths.cs) — and then the things — one is
/// set out with a click where the cursor is, {Rotate} turning it, and the workers put it together (Sim.Yard.cs).
/// </summary>
public partial class BuildTools
{
    /// <summary>How many of the palette's picks are ways; the rest are things.</summary>
    public static readonly int WayCount = Enum.GetValues(typeof(WayKind)).Length;
    public static int YardCount => WayCount + Yard.All.Length;

    /// <summary>The tool in hand draws a way.</summary>
    bool Paving => Tool == Tool.Yard && Opt.YardPick < WayCount;
    WayKind WayPick => (WayKind)Math.Min(Opt.YardPick, WayCount - 1);
    DecorKind DecorPick => Yard.All[Math.Clamp(Opt.YardPick - WayCount, 0, Yard.All.Length - 1)];

    public static string YardName(int pick) => pick < WayCount ? Sim.WayName((WayKind)pick) : Yard.Name(Yard.All[pick - WayCount]);

    /// <summary>What a pick of the palette is, for its tooltip.</summary>
    public static string YardAbout(int pick) => pick < WayCount
        ? ((WayKind)pick switch
        {
            WayKind.Dirt => "Утоптанная земля. Дешевле всего, из земли.",
            WayKind.Gravel => "Гравий из камня.",
            WayKind.Cobble => "Булыжник из камня. По нему ходят быстрее всего.",
            WayKind.Flags => "Плиты из камня, для площадей.",
            _ => "Дощатые мостки из брёвен, для сырых мест.",
        }) + " Протяните по земле: рабочие снимут дёрн и выложат дорожку вровень с землёй. Дом у дорожки платит больше подати."
        : Yard.About(Yard.All[pick - WayCount]) + " Ставится щелчком, {Rotate} поворачивает.";

    public void StepYard(int dir) { Opt.YardPick = (Opt.YardPick + dir + YardCount) % YardCount; Changed(); }

    public void PickYard(int pick) { Opt.YardPick = Math.Clamp(pick, 0, YardCount - 1); Changed(); }

    VoxelList WayAt((int x, int y, int z) a, (int x, int y, int z) b) => Sim.WayPlan(_m.World, _m.Site, a.x, a.z, b.x, b.z, Opt.Width, WayPick);

    VoxelList DecorAtHover(out string why)
    {
        var g = HoverGround();
        return Yard.Plan(_m.World, _m.Site, DecorPick, g.x, g.z, Opt.YardRot, out why);
    }

    /// <summary>A way drawn from one point to another: the turf under it marked to be taken up and the way laid in its place.</summary>
    void MarkWay((int x, int y, int z) a, (int x, int y, int z) b)
    {
        var plan = WayAt(a, b);
        if (plan.Count == 0) { Refuse("дорожку кладут по земле, не по постройке и не по воде"); return; }
        double sec = Labour.Estimate(_m.World, plan, false, _est);
        int n = _m.Sim.Pave(plan);
        if (n == 0) { Refuse("здесь уже такая дорожка"); return; }
        string what = Sim.WayName(WayPick);
        _markName = what;
        _m.Hud.Log($"{what}: {Summary(_est, false, sec)}");
        Marked(what, plan, false, plan.Count);
        _planKey = "~";
    }

    /// <summary>The thing picked set out where the cursor is.</summary>
    void PlaceYard()
    {
        var plan = DecorAtHover(out string why);
        string what = Yard.Name(DecorPick);
        if (plan == null) { Refuse($"{what}: {why}"); return; }
        double sec = Labour.Estimate(_m.World, plan, false, _est);
        var g = HoverGround();
        why = _m.Sim.PlaceDecor(DecorPick, g.x, g.z, Opt.YardRot);
        if (why != null) { Refuse($"{what}: {why}"); return; }
        _markName = what;
        _m.Hud.Log($"{what}: {Summary(_est, false, sec)}");
        Marked(what, plan, false, plan.Count);
        _planKey = "~";
    }
}
