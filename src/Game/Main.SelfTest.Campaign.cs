using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// The ground of the map of valleys in the game (Sim.Campaign.cs): a party comes into the valley over its north-eastern
    /// side and stands some way in, and the chronicle says so; sent out over the same side, it goes over the edge, off the
    /// valley, handed over for the map as it was, and that is said too. A campaign written to its slot — the valley in play
    /// into its cell's file, the map's file beside it — lies in a folder of its own, apart from the saves of a single
    /// valley, and reads back. Cleared away after.
    /// </summary>
    async Task SelfTestCampaign(Action<string, bool, string> check)
    {
        async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        var sim = Sim; var hex = Site.Hex;
        if (sim.Alarm) sim.StandDown();
        var party = new Party();
        foreach (var k in new[] { Kinds.Captain, Kinds.Swordsman, Kinds.Swordsman, Kinds.Archer })
            party.Members.Add(new Traveller { Kind = k, Look = 3 + party.Members.Count * 5, Hp = k.Hp, MaxHp = k.Hp });
        party.Members[1].Hp = party.Members[1].MaxHp * .5f;
        var come = sim.Arrive(party, Hex.NorthEast);
        float hurt = come.Find(u => u.T == Kinds.Swordsman) is { } sw ? sw.Hp / sw.MaxHp : 0;
        var (sx, sz) = hex.SidePoint(Hex.NorthEast, .5f, Sim.ArriveIn);
        Rig.SetAt(World.WX(sx), World.WY(World.SurfAt((int)sx, (int)sz)), World.WZ(sz), -.66f, .6f, 1.4f);
        await Frames(4);
        Advance(45);
        await Frames(4);
        float least = come.Count == 0 ? 0 : come.Min(u => hex.FromSide(Hex.NorthEast, u.X, u.Z).d);
        bool cameSaid = Hud.Lines.TakeLast(60).Any(l => l.text.StartsWith("Отряд пришёл с северо-востока"));
        check("a party comes into the valley over its north-eastern side, its commander and its hurt swordsman as they were, stands some way in, and the chronicle says so",
            come.Count == 4 && come.All(u => !u.Dead) && come.Count(u => u.T == Kinds.Captain) == 1 && Math.Abs(hurt - .5f) < .001f && least > 15 * VoxelWorld.PerMetre && cameSaid,
            $"{come.Count(u => !u.Dead)} of {come.Count} standing, {least / VoxelWorld.PerMetre:0} m in at the least; the swordsman came {hurt:0.00}; said {cameSaid}");

        int went = sim.Depart(come, Hex.NorthEast);
        Advance(45);
        await Frames(2);
        var gone = sim.TakeDeparted();
        bool goneSaid = Hud.Lines.TakeLast(60).Any(l => l.text.StartsWith("Отряд ушёл из долины на северо-восток"));
        check("sent out over the same side it goes to the edge and over it: off the valley, handed over for the map as it was, and said",
            went == 4 && gone.Count == 1 && gone[0].Side == Hex.NorthEast && gone[0].Members.Count == 4 && gone[0].Commander != null && come.All(u => u.Dead && !sim.Units.Contains(u)) && goneSaid,
            $"{went} sent; {gone.Count} parties out{(gone.Count > 0 ? $", {gone[0].Members.Count} in it by side {gone[0].Side}, commander {gone[0].Commander != null}" : "")}; all off {come.All(u => u.Dead)}; said {goneSaid}");

        // a campaign's slot: a folder of its own
        const string slot = "campaign3";
        string dir = Saves.CampaignDir(slot);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        var camp = Campaign.New(Seed);
        bool wrote = Saves.WriteCampaign(this, slot, camp, camp.Home);
        var peek = Saves.PeekCampaign(slot);
        var back = Campaign.Exists(dir) ? Campaign.Read(dir) : null;
        bool valley = File.Exists(Path.Combine(dir, camp.Home.File));
        bool apart = !Saves.Slots.Concat(new[] { Saves.Quick, Saves.Auto }).Any(s => Saves.PathOf(s).StartsWith(dir));
        check("a campaign written to its slot lies in a folder of its own, the map's file and the valley in play in it, apart from the saves of one valley, and reads back",
            wrote && peek is { } p && p.day == Sim.Clock.Day && back != null && back.Cells.Count == camp.Cells.Count && back.Home.Visited && valley && apart,
            $"written {wrote}; slot reads day {peek?.day}; cells {back?.Cells.Count}; the valley's file {valley}; apart {apart}");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }
}
