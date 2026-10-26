using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;
using Holder = VoxelTower.Core.Owner;
using Land = VoxelTower.Core.Atlas;

namespace VoxelTower.Game;

/// <summary>
/// The campaign played on the map's land, as in «Heroes»: a new campaign opens on the map, our first hero by the first
/// valley's citadel. The land is made once a campaign and kept while the scene begins again for each valley; the valley
/// of the cell we are in runs under it all the while, on the same clock. On the map our heroes walk (Ui/AtlasHud.cs);
/// «Войти в клетку» takes a hero and his army into the valley he stands at — the one we are in at once, any other with
/// the scene begun again on it — and the army is seen there. Out of a valley they come back onto the map by the side
/// they went out over: walked over the edge, or at once with «Вывести на карту». Our own valleys are gone into from the
/// map without a hero, as a town is.
/// </summary>
public partial class Main
{
    /// <summary>The land of the campaign's map, kept while the scene begins again; made anew for another campaign.</summary>
    static Land _land;
    /// <summary>The map's land on the screen, in a campaign.</summary>
    public AtlasView AtlasLand { get; private set; }
    /// <summary>The scene begins with the map open (a new campaign, one read back, back from a valley left empty).</summary>
    static bool _atlasAtStart;
    /// <summary>The side a hero going in from the map comes in by (−1: the side he came over from the cell before).</summary>
    static int _enteringSide = -1;
    public bool AtlasOpen { get; private set; }

    /// <summary>The land made (or kept) for the campaign, the parties put on it, its view under the valley.</summary>
    void AtlasStart()
    {
        // the big map is drawn by its own view of hexes (its source CampaignHexes); the land of the small maps is not made for it
        if (Atlas.Big)
        {
            _land = null;
            Hud.InitAtlas(this, null);
            return;
        }
        if (_land?.Campaign != Atlas) _land = Land.Build(Atlas);
        Atlas.Attach(_land);
        AtlasLand = new AtlasView();
        AddChild(AtlasLand);
        AtlasLand.Atlas = _land;
        Hud.InitAtlas(this, AtlasLand);
        // the valley's sun lights none of the map's land, which keeps a sun of its own
        if (Atmos?.Sun != null) Atmos.Sun.LightCullMask &= ~AtlasView.Layer;
    }

    public void ToggleAtlas() { if (AtlasOpen) CloseAtlas(); else OpenAtlas(); }

    /// <summary>The map over the valley: the valley's camera and frame put by, the map's land and its own frame up.</summary>
    public void OpenAtlas()
    {
        if (Atlas == null || AtlasLand == null && !Atlas.Big || AtlasOpen) return;
        if (FP.On) FP.Exit();
        Tools.SetTool(Tool.None);
        AtlasLand?.Open(Atlas);
        AtlasOpen = true;
        Rig.Held = true;
        Hud.ShowAtlas(true);
        Hud.MapPane.Open();
    }

    /// <summary>Back into the valley we are in.</summary>
    public void CloseAtlas()
    {
        if (!AtlasOpen) return;
        AtlasLand?.Close();
        AtlasOpen = false;
        Rig.Held = FP.On;
        Rig.Cam.Current = true;
        Hud.ShowAtlas(false);
        Hud.MapPane.Close();
    }

    /// <summary>The side of its cell a party standing on the map's land is nearest, the way it would go in.</summary>
    public int SideNear(MapParty p)
    {
        var cell = Atlas.CellOf(p);
        if (_land == null || !p.Placed || cell == null) return Atlas.EntrySideOf(p);
        var (cx, cz) = _land.Center(cell);
        // (by the middle itself: the side it came over)
        if (MathF.Sqrt((p.AtX - cx) * (p.AtX - cx) + (p.AtZ - cz) * (p.AtZ - cz)) < AtlasWalk.Spacing(_land) * .12f) return Atlas.EntrySideOf(p);
        float a = MathF.Atan2(p.AtZ - cz, p.AtX - cx) * 180 / MathF.PI;
        // the middle of side s lies at −60 + 60 s degrees from the east, round toward the south
        int side = (int)MathF.Round((a + 60) / 60);
        return ((side % 6) + 6) % 6;
    }

    /// <summary>
    /// «Войти в клетку»: a hero and his army into the valley of the cell they stand at, by the side they stand nearest —
    /// the valley we are in at once, any other with the scene begun again on it.
    /// </summary>
    public void EnterHero(MapParty p)
    {
        if (Atlas == null || p == null || !Atlas.Parties.Contains(p)) return;
        var cell = Atlas.CellOf(p);
        int side = SideNear(p);
        if (cell == Atlas.Here)
        {
            Atlas.Parties.Remove(p);
            CloseAtlas();
            Come(p.Party, side, select: true);
            Hud.MapPane.Refresh();
            return;
        }
        _enteringSide = side;
        EnterCell(p);
    }

    /// <summary>A valley of ours opened from the map without a hero: the one we are in at once, another with the scene begun again on it.</summary>
    public void GoToValley(MapCell cell)
    {
        if (Atlas == null || cell == null) return;
        if (cell == Atlas.Here) { CloseAtlas(); return; }
        KeepersLeft();
        if (!Saves.WriteCampaign(this, CampSlot, Atlas, Atlas.Here)) return;
        Atlas.HereQ = cell.Q; Atlas.HereR = cell.R;
        Atlas.Write(Saves.CampaignDir(CampSlot));
        _entering = null; _atlasAtStart = false;
        GetTree().ReloadCurrentScene();
    }

    /// <summary>
    /// The chosen of the valley onto the map at once («Вывести на карту»): over the side nearest them, a party with a
    /// commander (or a few light men scouting), and the map open on them. Null, or why not.
    /// </summary>
    public string LeaveToMap(List<Unit> group)
    {
        if (Atlas == null) return "нет кампании";
        group = group.FindAll(u => !u.Dead && u.Team == Team.Player && u.T != Kinds.Risen);
        if (group.Count == 0) return "выберите в долине командира и бойцов";
        if (!group.Exists(u => u.T == Kinds.Captain) && !Scouts(group)) return $"в отряде нужен командир, или до {Campaign.ScoutMost} лёгких бойцов в разведку";
        float cx = 0, cz = 0;
        foreach (var u in group) { cx += u.X / group.Count; cz += u.Z / group.Count; }
        var hex = Site.Hex ?? Hex.In(World.SX, World.SZ);
        int side = -1; float best = float.MaxValue;
        for (int s = 0; s < 6; s++)
        {
            if (Atlas.Neighbour(Atlas.Here, s) == null) continue;
            float d = hex.FromSide(s, cx, cz).d;
            if (d < best) { best = d; side = s; }
        }
        if (side < 0) side = Atlas.Here.EntrySide;
        if (FP.On && group.Contains(Sim.Possessed)) FP.Exit();
        var party = Sim.Withdraw(group, side);
        if (party == null) return "никто из выбранных не может уйти: лежит, придавлен или несёт";
        Selected.Clear();
        Hud.OnSelectionChanged();
        var mp = Atlas.Out(party, null);
        Hud.Log($"Отряд вышел на карту: {MapBoard.Who(mp).ToLowerInvariant()}.", Hud.Kind.Ok);
        OpenAtlas();
        Hud.MapPane.Choose(mp, focus: true);
        return null;
    }

    /// <summary>A commander's party, for his card's «Вывести на карту»: the chosen with him, if he is among them, else those about him.</summary>
    public List<Unit> PartyOf(Unit captain)
    {
        if (Selected.Contains(captain)) return new List<Unit>(Selected);
        return PartyAbout(captain);
    }

    /// <summary>A cell of the map's land laid anew (whose it is, its outpost, its treasure), and the ways read again over it.</summary>
    void RefreshLand(MapCell cell)
    {
        if (_land == null || cell == null) return;
        _land.Refresh(cell);
        AtlasWalk.Forget();
    }

    /// <summary>The valley's day sped up, in the valley, till its dawn («До рассвета»): so many times as fast.</summary>
    public const int RushSpeed = 8;
    int _rushFrom = -1, _rushSpeed;

    /// <summary>
    /// «До рассвета»: the clock on to the next dawn. On the map at once, the valley we are in living through it by its
    /// summary as the others do (left and gone into again); in the valley, its day run eight times as fast till the dawn.
    /// </summary>
    public void ToDawn()
    {
        if (Atlas == null || Sim == null) return;
        if (AtlasOpen || !Atlas.InValley)
        {
            var here = Atlas.Here;
            Atlas.Leave(Sim, here);
            Atlas.InValley = false; Atlas.Live = null;
            Atlas.SkipToDawn();
            Atlas.Enter(Sim, here);
            Paused = false;
            Tell();
            Hud.MapPane?.Refresh();
            return;
        }
        if (_rushFrom >= 0) return;
        _rushFrom = Sim.Clock.Now.Day;
        _rushSpeed = Speed;
        Speed = RushSpeed; Paused = false;
        Hud.Log("До рассвета: время идёт в 8 раз быстрее.", Hud.Kind.Ok, toast: false);
    }

    /// <summary>The rush to the dawn over once the day came (or the map opened).</summary>
    void RushTick()
    {
        if (_rushFrom < 0) return;
        if (Sim.Clock.Now.Day > _rushFrom || AtlasOpen || Sim.Alarm) { Speed = Math.Clamp(_rushSpeed, 1, 3); _rushFrom = -1; }
    }

    /// <summary>The map's news told now (after a fight decided on the map's card).</summary>
    public void TellMap() { if (Tell()) Hud.MapPane?.Refresh(); }
}
