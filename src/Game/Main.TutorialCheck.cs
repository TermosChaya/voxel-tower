using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// --tutorial-check: the tutorial gone through from its first step to its last as a player would go through it — the
/// view moved, pawns chosen and sent, trees felled, plots and walls marked through the tools' own input, the band of
/// the lesson fought — each step checked to be taken as done and left for the next by itself. Prints a line a step
/// and quits with the number of steps that would not go.
/// </summary>
public partial class Main
{
    bool _tutCheck;

    async void TutorialCheck()
    {
        int fails = 0;
        async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        Vector2 ScreenAt(float x, float y, float z) => Rig.Cam.UnprojectPosition(new Vector3(World.WX(x), World.WY(y), World.WZ(z)));
        Vector2 Screen(int x, int z) => ScreenAt(x + .5f, World.SurfAt(x, z), z + .5f);
        void Move(Vector2 p) => Tools._UnhandledInput(new InputEventMouseMotion { Position = p });
        void Button(Vector2 p, bool down) => Tools._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = down, Position = p });
        async Task ClickAt(Vector2 p) { Move(p); await Frames(2); Button(p, true); Button(p, false); await Frames(2); }
        async Task Look(float vx, float vz) { Rig.Set(World.WX(vx), World.WZ(vz), -.66f, .62f, 1.6f, snap: true); await Frames(10); }
        async Task Drag((int x, int z) a, (int x, int z) b)
        {
            var pa = Screen(a.x, a.z); var pb = Screen(b.x, b.z);
            Move(pa); await Frames(1); Button(pa, true);
            for (int i = 1; i <= 8; i++) { Move(pa.Lerp(pb, i / 8f)); await Frames(1); }
            Button(pb, false);
            await Frames(2);
        }
        Unit First(UnitType t) => Sim.Units.Find(u => !u.Dead && u.Team == Team.Player && u.T == t);
        void Choose(params Unit[] us) { Selected.Clear(); foreach (var u in us) if (u != null) Selected.Add(u); Hud.OnSelectionChanged(); }
        int kx = Site.KeepX, kz = Site.KeepZ;
        // (the nearest tree out of the citadel's clearing, on the plateau or under it, wherever the valley grew one)
        var tree = Nearest(kx - 90, kz + 40, 260, (x, z) => Mat.IsTrunk(World.Get(x, World.SurfAt(x, z) + 2, z)) && (x - kx) * (x - kx) + (z - kz) * (z - kz) > 90 * 90);

        await Frames(10);
        var done = new HashSet<string>();
        for (int guard = 0; guard < 80 && Tutor is { Over: false }; guard++)
        {
            int at = Tutor.At;
            string title = Tutor.Title;
            if (at == Tutor.Count - 1) { GD.Print($"[tutorial] ok   {at + 1:00} {title}"); break; }
            switch (title)
            {
                case "Сдвинуть вид": { var a = Rig.Aim; Rig.Set(a.x + 5, a.z, a.yaw, a.pitch, a.zoom, false); break; }
                case "Повернуть": { var a = Rig.Aim; Rig.Set(a.x, a.z, a.yaw + 1.2f, a.pitch, a.zoom, false); break; }
                case "Ближе и дальше": Rig.ZoomBy(3); await Frames(3); Rig.ZoomBy(1 / 3f); break;
                case "Вид сверху": Rig.ToggleTopDown(); break;
                case "Вернуть вид": Rig.ToggleTopDown(); Rig.Home(); break;
                case "Карта": Hud.Map._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = Hud.Map.Size / 2 }); break;
                case "Лица дружины": Choose(First(Kinds.Worker)); break;
                case "Послать": await Look(kx - 40, kz); Command.OrderTo(Screen(kx - 40, kz + 6)); break;
                case "Рамкой": Choose(Sim.Units.FindAll(u => !u.Dead && u.Team == Team.Player && u.T == Kinds.Worker).ToArray()); break;
                case "Отряд": Command.RememberSquad(0); break;
                case "Снять выбор": Choose(); await Frames(3); Command.CallSquad(0); break;
                case "Охота":
                {
                    var archer = First(Kinds.Archer);
                    Unit beast = null; float bd = float.MaxValue;
                    foreach (var u in Sim.Units) if (!u.Dead && u.Team == Team.Wild && (u.X - archer.X) * (u.X - archer.X) + (u.Z - archer.Z) * (u.Z - archer.Z) is var d && d < bd) { bd = d; beast = u; }
                    Choose(archer);
                    if (beast != null) Sim.Hunt(Selected, beast);
                    break;
                }
                case "Кирка":
                    Choose();
                    await Look(tree.x, tree.z);
                    Tools.SetDig(true);
                    await ClickAt(ScreenAt(tree.x + .5f, World.SurfAt(tree.x, tree.z) + 6, tree.z + .5f));
                    break;
                case "Делянка":
                    Tools.SetTool(Tool.Gather); Tools.SetMaterial(Mat.Wood);
                    await Look(tree.x, tree.z);
                    await Drag((tree.x - 30, tree.z - 30), (tree.x + 30, tree.z + 30));
                    break;
                case "Каменоломня":
                {
                    Tools.SetMaterial(Mat.Stone);
                    Landmark ruin = Site.Landmarks.Find(l => l.Kind == LandmarkKind.Ruin);
                    var (rx, rz) = ruin != null ? (ruin.X, ruin.Z) : (kx - 60, kz - 60);
                    await Look(rx, rz);
                    await Drag((rx - 20, rz - 20), (rx + 20, rz + 20));
                    break;
                }
                case "Железная руда":
                {
                    if (Tools.Tool != Tool.Gather) Tools.SetTool(Tool.Gather);
                    Tools.SetMaterial(Mat.Plate);
                    // (an outcrop clear of the plots drawn before, wherever the valley put them; the next if one will not take)
                    foreach (var o in Site.Outcrops)
                    {
                        if (Sim.OreLeft(o.X, o.Z, o.R) < 40 || Sim.Zones.Exists(z => z.X0 < o.X + 24 && z.X1 > o.X - 24 && z.Z0 < o.Z + 24 && z.Z1 > o.Z - 24)) continue;
                        await Look(o.X, o.Z);
                        await Drag((o.X - 16, o.Z - 16), (o.X + 16, o.Z + 16));
                        GD.Print($"[tutorial]      ore: the outcrop at {o.X},{o.Z}, «{Tools.HoverText}»");
                        if (Sim.Zones.Exists(z => z.Kind == Stockpile.Metal)) break;
                    }
                    break;
                }
                case "Убрать кисть": Tools.SetTool(Tool.None); break;
                case "Старая крепость":
                {
                    Tools.SetTool(Tool.Repair);
                    var (x0, z0, x1, z1) = Sim.FortArea;
                    // a voxel of the fort still standing, on its top
                    var (fx, fz) = Nearest((x0 + x1) / 2, z0, Math.Max(x1 - x0, z1 - z0), (x, z) => Mat.IsMasonry(World.Get(x, World.SurfAt(x, z) - 1, z)) && World.SurfAt(x, z) > Site.HTop[z * World.SX + x] + 6);
                    await Look(fx, fz);
                    await ClickAt(ScreenAt(fx + .5f, World.SurfAt(fx, fz), fz + .5f));
                    break;
                }
                case "Задачи": Hud.OpenPanel("goals"); break;
                case "Линия": Tools.SetTool(Tool.Line); await Look(kx - 120, kz + 60); await Drag((kx - 130, kz + 50), (kx - 110, kz + 70)); break;
                case "Толщина и высота": Tools.StepWidth(1); break;
                case "Сетка": Tools.SetGrid(8); await Drag((kx - 130, kz + 80), (kx - 100, kz + 80)); break;
                case "Материал": Tools.SetMaterial(Mat.Wood); break;
                case "Прямоугольник и круг": Tools.SetTool(Tool.Circle); await Drag((kx - 120, kz + 100), (kx - 110, kz + 100)); break;
                case "Жилой дом":
                {
                    // a flat spot in sight, a little way west of the fort
                    // (the pick, taken up for the moat, is put down: with it the rectangle digs)
                    Tools.SetTool(Tool.Box); Tools.SetDig(false); Tools.Opt.House = true; Tools.Opt.Hollow = true;
                    int hx = kx - 150, hz = kz + 110;
                    await Look(hx + 20, hz + 17);
                    await Drag((hx, hz), (hx + 40, hz + 34));
                    GD.Print($"[tutorial]      house: dwellings {Sim.Zones.FindAll(z => z.Kind == Zone.Dwelling).Count}, doors waiting {Sim.HouseDoors.Count}");
                    Tools.Opt.House = false; Tools.Opt.Hollow = false;
                    break;
                }
                case "Отменить": Tools.Undo(); break;
                case "Ров": Tools.SetTool(Tool.Line); Tools.SetDig(true); await Drag((kx - 140, kz + 40), (kx - 140, kz + 70)); break;
                case "Снять разметку": Tools.SetTool(Tool.Unmark); await Drag((kx - 146, kz + 36), (kx - 134, kz + 74)); break;
                case "Лестница, ворота, мост":
                {
                    // the line's wall is only marked: a ladder against the old fort's side, at the first point of it the view sees side on
                    Tools.SetTool(Tool.Ladder);
                    var (x0, z0, x1, z1) = Sim.FortArea;
                    await Look((x0 + x1) / 2, (z0 + z1) / 2);
                    Vector2? at2 = null;
                    for (int z = z0; z <= z1 && at2 == null; z += 3)
                        for (int x = x0; x <= x1 && at2 == null; x += 3)
                        {
                            var p = Screen(x, z) + new Vector2(0, 14);
                            var (o, d) = Rig.Ray(p);
                            if (Raycast.Cast(World, (o.X - World.OriginX) / VoxelWorld.Vox, o.Y / VoxelWorld.Vox, (o.Z - World.OriginZ) / VoxelWorld.Vox, d.X, d.Y, d.Z, out var hit)
                                && hit.NY == 0 && Mat.IsMasonry(hit.M) && hit.Y > Site.HTop[hit.Z * World.SX + hit.X] + 6) at2 = p;
                        }
                    int made = Sim.MarkingsMade;
                    if (at2 is { } q) await ClickAt(q);
                    GD.Print($"[tutorial]      ladder: {(at2 == null ? "no side of the fort in sight" : Sim.MarkingsMade > made ? "marked" : "nothing marked")}, «{Tools.HoverText}»");
                    break;
                }
                case "Шаблоны":
                {
                    // the round tower from the library, on open ground south of the old works
                    Tools.SetTool(Tool.Template);
                    Tools.PickTemplate(0);
                    await Look(kx - 150, kz + 140);
                    int made = Sim.MarkingsMade;
                    await ClickAt(Screen(kx - 150, kz + 140));
                    GD.Print($"[tutorial]      template: {(Sim.MarkingsMade > made ? "put up" : "nothing put up")}, «{Tools.PlanText}»");
                    break;
                }
                case "Работы": Hud.OpenPanel("works"); break;
                case "Летопись": Hud.OpenPanel("goals"); break;
                case "Часы": Command.TogglePause(); await Frames(3); Command.SetSpeed(3); break;
                case "Рудник":
                {
                    Mine copper = null; long best = long.MaxValue;
                    foreach (var mn in Sim.Mines) if (!mn.Power && (long)(mn.Place.X - kx) * (mn.Place.X - kx) + (long)(mn.Place.Z - kz) * (mn.Place.Z - kz) is var d && d < best) { best = d; copper = mn; }
                    if (copper != null)
                    {
                        var folk = Sim.Units.FindAll(u => !u.Dead && u.Team == Team.Player && u.T == Kinds.Worker).GetRange(0, 2);
                        int cell = Sim.NearestStand(copper.Place.StandX > 0 ? copper.Place.StandX : copper.Place.X, copper.Place.StandZ > 0 ? copper.Place.StandZ : copper.Place.Z, 2);
                        Sim.Command(folk, cell);
                        for (int t = 0; t < 120 && copper.Holder != Holder.Ours; t++) Advance(1);
                        // not taken: where the two are, and who else holds it
                        if (copper.Holder != Holder.Ours)
                        {
                            GD.Print($"[tutorial]      {copper.Name}: ours {copper.Ours}, theirs {copper.Theirs}, grip {copper.Grip:0.00}, sent to {cell}");
                            foreach (var u in folk) GD.Print($"[tutorial]        #{u.Id} at {u.X:0},{u.Y:0},{u.Z:0} {u.State} «{u.Why}» hold {u.Hold} dest {u.Dest} path {u.Path?.Count - u.PathI} from the stand {MathF.Sqrt((u.X - copper.Place.StandX) * (u.X - copper.Place.StandX) + (u.Z - copper.Place.StandZ) * (u.Z - copper.Place.StandZ)):0} keeping out of {(u.Avoiding == null ? "nothing" : $"#{u.Avoiding.By?.Id}'s lane at {u.Avoiding.PX:0},{u.Avoiding.PZ:0} till {u.Avoiding.Until:0} s")}");
                        }
                    }
                    break;
                }
                case "Орда": Hud.OpenPanel("horde"); break;
                case "Тревога": Command.Alarm(); break;
                case "Учебная ватага":
                    await Frames(3);
                    // as the step says: the soldiers chosen and set on the nearest raider, now and then
                    for (int t = 0; t < 1200 && Sim.Waves.Active; t++)
                    {
                        Advance(1);
                        if (t % 5 != 0) continue;
                        var soldiers = Sim.Units.FindAll(u => !u.Dead && u.Team == Team.Player && (u.T == Kinds.Archer || u.T == Kinds.Swordsman));
                        Unit foe = null; float fd = float.MaxValue;
                        foreach (var u in Sim.Units)
                            if (!u.Dead && u.Team == Team.Horde && !u.Guard && soldiers.Count > 0 && (u.X - soldiers[0].X) * (u.X - soldiers[0].X) + (u.Z - soldiers[0].Z) * (u.Z - soldiers[0].Z) is var d && d < fd) { fd = d; foe = u; }
                        if (foe != null) Sim.Hunt(soldiers, foe);
                    }
                    GD.Print($"[tutorial]      wave {Sim.Waves.Number}, active {Sim.Waves.Active}, coming {Sim.Waves.Coming}, raiders {Sim.Units.FindAll(u => !u.Dead && u.Team == Team.Horde && !u.Guard).Count}");
                    foreach (var u in Sim.Units) if (!u.Dead && u.Team == Team.Horde && !u.Guard) GD.Print($"[tutorial]        {u.T.Name} at {u.X:0},{u.Z:0} {u.State}");
                    // the band not beaten: how each of them and each of ours stands, and whether ours can get to them
                    if (Sim.Waves.Active)
                    {
                        GD.Print($"[tutorial]      the keep {Sim.KeepHp:0} of {Sim.KeepMax:0}, alarm {Sim.Alarm}");
                        foreach (var u in Sim.Units)
                            if (!u.Dead && u.Team == Team.Horde)
                                GD.Print($"[tutorial]        #{u.Id} {u.T.Name} at {u.X:0},{u.Y:0},{u.Z:0} {u.State} hp {u.Hp:0}/{u.MaxHp:0} pain {u.Pain:0.00} routed {u.Routed} guard {u.Guard} bound {u.Bound} down {u.Down} inside {u.Inside} target {u.Target?.Id}");
                        foreach (var u in Sim.Units)
                            if (!u.Dead && u.Team == Team.Player && u.T.Work < 1)
                            {
                                var foe = Sim.Units.Find(o => !o.Dead && o.Team == Team.Horde);
                                var way = foe == null || u.Inside ? null : Sim.Paths.Find(u.Cell, c => c == foe.Cell || Math.Abs(Sim.Nav.I(c) - Sim.Nav.I(foe.Cell)) <= 1 && Math.Abs(Sim.Nav.K(c) - Sim.Nav.K(foe.Cell)) <= 1, _ => 0, u.T.Move, 200000, gates: true);
                                GD.Print($"[tutorial]        #{u.Id} {u.T.Name} at {u.X:0},{u.Y:0},{u.Z:0} {u.State} «{u.Why}» hp {u.Hp:0}/{u.MaxHp:0} inside {u.Inside} mending {u.Mending} hold {u.Hold} post {u.Post} prey {u.Prey?.Id} target {u.Target?.Id}; a way to #{foe?.Id}: {(way == null ? "none" : way.Count + " cells")} ({Sim.Paths.Expanded} looked at)");
                            }
                    }
                    break;
                case "Отбой": if (Sim.Alarm) Command.Alarm(); break;
                case "Цитадель": Sim.Enlist(Kinds.Archer); break;
                case "Знания":
                    foreach (var l in Sim.LoresOn(0)) if (Sim.CanLearn(l)) { Sim.Learn(l.Key); break; }
                    break;
                case "Таинства": Hud.OpenPanel("magic"); break;
                case "Вселиться":
                {
                    // the swordsman, as the step says; hurt in the drill and lying up in the citadel (where none can be
                    // gone into), any other of ours (the step is done by going into anyone)
                    Unit Into() => Sim.CanPossess(First(Kinds.Swordsman)) ? First(Kinds.Swordsman) : Sim.Units.Find(u => !u.Dead && u.Team == Team.Player && Sim.CanPossess(u));
                    for (int t = 0; t < 60 && Into() == null; t++) await Frames(30);
                    Choose(Into()); Command.Possess();
                    break;
                }
                case "Выйти": FP.Exit(); break;
                case "Меню и управление": Hud.OpenMenu("controls"); await Frames(3); Hud.CloseMenu(); break;
                default: Tutor.Next(); break;
            }
            for (int f = 0; f < 240 && Tutor.At == at; f++) await Frames(1);
            bool ok = Tutor.At != at;
            GD.Print($"[tutorial] {(ok ? "ok  " : "FAIL")} {at + 1:00} {title}");
            if (!ok) { fails++; Tutor.Next(); await Frames(2); }
            done.Add(title);
        }
        GD.Print(fails == 0 ? "[tutorial] all steps went" : $"[tutorial] {fails} steps would not go");
        GetTree().Quit(fails == 0 ? 0 : 1);
    }
}
