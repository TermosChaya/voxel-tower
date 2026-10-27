using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using VoxelTower.Core;
using Holder = VoxelTower.Core.Owner;

namespace VoxelTower.Game;

/// <summary>
/// --tutorial-check, the campaign's part (Tutorial.Campaign.cs): its steps gone through on the map and in the valleys as
/// a player would go through them — the map moved, the way planned and walked, the chest opened, the dawn called, the
/// mine flagged and gone into, the garrison left with the architect, the wolves hovered and beaten, home, a hero hired
/// and an artifact bought, the horde's army waited for and fought in the mine's valley, the small map clicked, the
/// campaign saved. A step that begins the scene again leaves it to the check of the next scene, which goes on from
/// the step the tutorial kept.
/// </summary>
public partial class Main
{
    async void CampaignCheck()
    {
        async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        void Run(double seconds) { for (double t = 0; t < seconds; t += .5) { Advance(.5); CampaignTick(.5); } }
        var c = Atlas;
        var map = Hud.MapPane;
        MapCell Cell((int col, int row) at) => c.Cell(at.col, at.row);
        MapParty Hero() => c.Parties.Find(p => p.Owner == Holder.Ours && p.Party.Commander?.Hero?.Name == Tutorial.HeroName);
        // the hero walked on the map till he stands where he was sent, the dawn called when his moves run out
        async Task Walk((int col, int row) to, Func<bool> done)
        {
            for (int i = 0; i < 400 && !done(); i++)
            {
                if (Hero() is { } h && !h.Moving && c.CellOf(h) != Cell(to) && c.Joiners.Count == 0) { map.Choose(h); map.GoTo(to.col, to.row); }
                if (Hero() is { Tired: true }) ToDawn();
                Run(1);
                await Frames(1);
            }
        }
        // the battle for a point of ours goes on in its valley's own scene: the map waits for it
        _selfDefence = true;
        await Frames(10);
        for (int guard = 0; guard < 60 && Tutor is { Over: false }; guard++)
        {
            int at = Tutor.At;
            string title = Tutor.Title;
            await StepPicture($"c{at + 1:00}");
            if (at == Tutor.Count - 1)
            {
                GD.Print($"[tutorial] ok   c{at + 1:00} {title}");
                break;
            }
            switch (title)
            {
                case "Обзор карты":
                {
                    var v = WorldMap;
                    float dx = v.Look.X < v.Layout.SX / 2 ? 80 : -80;
                    v.Snap(v.Look.X + dx, v.Look.Y, v.Distance * 1.6f);
                    break;
                }
                case "Путь": if (Hero() is { } h0) map.Choose(h0); map.PlanTo(Tutorial.ChestAt.col, Tutorial.ChestAt.row); break;
                case "Идти": map.GoTo(Tutorial.ChestAt.col, Tutorial.ChestAt.row); break;
                case "Сундук":
                {
                    var chest = Cell(Tutorial.ChestAt);
                    await Walk(Tutorial.ChestAt, () => c.Chests.Exists(x => x.cell == chest) || chest.Thing is null or { Taken: true });
                    if (c.Chests.Exists(x => x.cell == chest)) Hud.Log(c.OpenChest(chest, gold: true), Hud.Kind.Ok);
                    break;
                }
                case "Ход и рассвет": ToDawn(); break;
                case "Рудник": await Walk(Tutorial.MineAt, () => Cell(Tutorial.MineAt).Thing is { Owner: Holder.Ours }); break;
                case "Войти в клетку":
                    // (the scene begins again on the mine's valley: its check goes on from here)
                    if (!AtlasOpen) break;
                    if (Hero() is { } h1) map.Choose(h1);
                    GD.Print($"[tutorial]      into the mine's valley at {Tutorial.MineAt}");
                    map.EnterChosen();
                    return;
                case "Гарнизон":
                {
                    var ours = Sim.Units.FindAll(u => !u.Dead && u.Team == Team.Player);
                    var stay = ours.FindAll(u => u.T == Kinds.Architect).Take(1)
                        .Concat(ours.FindAll(u => u.T == Kinds.Worker).Take(2))
                        .Concat(ours.FindAll(u => u.T == Kinds.Archer).Take(2)).ToList();
                    Selected.Clear(); foreach (var u in stay) Selected.Add(u);
                    Hud.OnSelectionChanged();
                    Command.GarrisonSelected();
                    break;
                }
                case "Архитектор":
                    for (int i = 0; i < 240 && Sim.ArchMarked == 0; i++) { Run(1); if (i % 10 == 0) await Frames(1); }
                    GD.Print($"[tutorial]      architect: marked {Sim.ArchMarked}, «{Sim.ArchDoing}», lacks «{Sim.ArchLacks}»");
                    break;
                case "На карту":
                {
                    string no = LeaveToMap(Sim.Units.FindAll(u => !u.Dead && u.Team == Team.Player));
                    if (no != null) GD.Print($"[tutorial]      out to the map: «{no}»");
                    break;
                }
                case "Существа":
                {
                    var v = WorldMap;
                    var (x, z) = v.Layout.Center(Tutorial.WolvesAt.col, Tutorial.WolvesAt.row);
                    v.Snap(x, z, v.Distance);
                    await Frames(3);
                    v.HoverAt(v.Cam.UnprojectPosition(v.HexToScene(Tutorial.WolvesAt.col, Tutorial.WolvesAt.row)));
                    await Frames(3);
                    break;
                }
                case "Бой":
                {
                    WorldMap.HoverAt(null);
                    var wolves = Cell(Tutorial.WolvesAt);
                    GD.Print($"[tutorial]      wolves: {Bestiary.Label(wolves.Sum.Keep)}, {MapBoard.Against(Hero()?.Party.Members, wolves)}");
                    await Walk(Tutorial.WolvesAt, () => wolves.Sum.Keep.Count == 0 || Hero() is { Moving: false } h && c.CellOf(h) == wolves || c.Joiners.Count > 0);
                    if (c.Joiners.Count > 0) Hud.Log(c.Join(wolves, take: false) ?? "", Hud.Kind.Ok);
                    else if (wolves.Sum.Keep.Count > 0 && Hero() is { } h2)
                    {
                        var r = map.FightWith(h2);
                        GD.Print($"[tutorial]      quick battle: won {r?.AttackerWon}, odds {r?.Odds:0.00}");
                    }
                    break;
                }
                case "Домой":
                    // (the scene begins again on the first valley)
                    if (c.Here == c.Home) break;
                    GoToValley(c.Home);
                    return;
                case "Таверна":
                {
                    OpenAtlas();
                    await Frames(2);
                    map.Choose(null);
                    map.HexClicked(c.Home.Col, c.Home.Row, MouseButton.Left);
                    map.ShowTavern();
                    var p = c.HireHero(0);
                    if (p != null) map.Choose(p, focus: true);
                    else GD.Print($"[tutorial]      no hero hired: «{c.CantHireHero(0)}»");
                    break;
                }
                case "Торговец":
                {
                    var p = c.Parties.Find(x => c.AtCitadel(x));
                    if (p == null) { GD.Print("[tutorial]      no hero by the citadel"); break; }
                    map.ShowMerchant(p);
                    var buy = c.MarketNow().FirstOrDefault(k => c.CantBuy(p, k) == null);
                    if (buy != null) c.Buy(p, buy);
                    else GD.Print($"[tutorial]      nothing to buy: {string.Join(", ", c.MarketNow().Select(k => $"{k} «{c.CantBuy(p, k)}»"))}");
                    map.Refresh();
                    break;
                }
                case "Карточка героя": if (map.Hero is { } h3) Hud.HeroPane.Show(h3); break;
                case "Бой за точку":
                {
                    var mine = Cell(Tutorial.MineAt);
                    if (c.Here != mine)
                    {
                        // waited for at home: the scene begins again on the mine when the army gets there
                        if (AtlasOpen) CloseAtlas();
                        for (int i = 0; i < 900 && _defending == null; i++) { Run(1); if (i % 5 == 0) await Frames(1); }
                        if (_defending != null) { GD.Print($"[tutorial]      the horde at the mine, day {c.Clock.Day}: into its valley"); return; }
                        GD.Print($"[tutorial]      the horde never came: frozen {c.Frozen != null}, parties {string.Join(", ", c.Parties.Where(x => x.Owner == Holder.Horde).Select(x => $"{x.Id} at {x.Q},{x.R} moving {x.Moving}"))}");
                        break;
                    }
                    // in the mine's valley: the garrison set on the nearest raider now and then, as the step says
                    for (int t = 0; t < 400 && Main.DefencesHeld <= Tutorial.HeldFrom; t++)
                    {
                        Run(1);
                        if (t % 5 == 0)
                        {
                            var soldiers = Sim.Units.FindAll(u => !u.Dead && u.Team == Team.Player && Fortification.Fighter(u));
                            Unit foe = null; float fd = float.MaxValue;
                            foreach (var u in Sim.Units)
                                if (!u.Dead && u.Team == Team.Horde && soldiers.Count > 0 && (u.X - soldiers[0].X) * (u.X - soldiers[0].X) + (u.Z - soldiers[0].Z) * (u.Z - soldiers[0].Z) is var d && d < fd) { fd = d; foe = u; }
                            if (foe != null) Sim.Hunt(soldiers, foe);
                            await Frames(1);
                        }
                    }
                    GD.Print($"[tutorial]      battle for the mine: held {Main.DefencesHeld - Tutorial.HeldFrom}, raid {Sim.Raids.Active}, frozen {c.Frozen != null}, ours {Sim.Units.Count(u => !u.Dead && u.Team == Team.Player)}");
                    break;
                }
                case "Туман и мини-карта":
                {
                    OpenAtlas();
                    await Frames(3);
                    var mini = WorldMap.MiniMap;
                    mini._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = mini.Size / 2 });
                    mini._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = mini.Size / 2 });
                    break;
                }
                case "Сохранение": SaveCampaign(); break;
                default: Tutor.Next(); break;
            }
            for (int f = 0; f < 240 && Tutor.At == at; f++) await Frames(1);
            bool ok = Tutor.At != at;
            GD.Print($"[tutorial] {(ok ? "ok  " : "FAIL")} c{at + 1:00} {title}");
            if (!ok) { _tutFails++; Tutor.Next(); await Frames(2); }
        }
        _selfDefence = false;
        // the lesson's campaign gone with the check, as the self-test's are
        string dir = Saves.CampaignDir(Tutorial.Slot);
        Tutorial.CampaignAt = -1;
        LeaveCampaign();
        if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true);
        GD.Print(_tutFails == 0 ? "[tutorial] all steps went" : $"[tutorial] {_tutFails} steps would not go");
        GetTree().Quit(_tutFails == 0 ? 0 : 1);
    }
}
