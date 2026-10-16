using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public enum Tool { None, Wall, Tower, Stairs, Ladder, Brush, Dig, Fill, Remove, Capture, Paste }

public sealed class ToolOptions
{
    public WallProfile Profile = Brushes.Rampart;
    public int TowerR = 16, TowerH = 56;
    public byte Material = Mat.Stone;
    public int BrushR = 3;
    public int DigDepth = 16;
}

/// <summary>
/// The player's hands: finds the voxel under the cursor, shows what a tool would build as a ghost
/// (ember when the treasury can pay, red when it cannot) and builds it at once on release.
/// </summary>
public partial class BuildTools : Node3D
{
    public static readonly Dictionary<Tool, string> Hints = new()
    {
        [Tool.Wall] = "Стена: зажмите кнопку на земле и протяните линию. Профиль выбирается в панели.",
        [Tool.Tower] = "Башня: клик по земле.",
        [Tool.Stairs] = "Ступени: нажмите на верх стены и тяните туда, куда они должны спуститься.",
        [Tool.Ladder] = "Лестницы вернутся вместе с воинами: их перенесу следующим шагом.",
        [Tool.Brush] = "Кисть: клик кладёт материал. Shift+клик убирает своё.",
        [Tool.Dig] = "Копать: клик или протяжка. Каждый кубометр даёт землю для вала.",
        [Tool.Fill] = "Насыпать: поднимает землю на полметра. Нужна выкопанная земля.",
        [Tool.Remove] = "Разобрать: клик по своей кладке, вернётся половина цены.",
        [Tool.Capture] = "Чертёж: обведите рамкой свою постройку.",
        [Tool.Paste] = "Чертёж: клик ставит копию, R поворачивает.",
    };

    const int PaintRadius = 7;

    Main _m;
    public Tool Tool { get; private set; }
    public readonly ToolOptions Opt = new();
    public Blueprint PasteBp { get; private set; }
    public int PasteRot { get; private set; }

    VoxelHit _hover;
    bool _hasHover;
    bool _lmb, _paint, _shift, _hasStart;
    Vector2 _pressPos;
    (int x, int y, int z) _startPt;
    int _stairsTop, _digFloor, _fillTarget;
    (int x, int z) _lastPaint;

    Node3D _voxSpace;
    MeshInstance3D _ghost, _shape, _cursor;
    ShaderMaterial _ghostMat;
    StandardMaterial3D _shapeMat;
    readonly GreedyMesher _mesher = new();
    byte[] _grid = Array.Empty<byte>();

    VoxelList _plan;
    string _planKey = "~";
    double _planClock, _clock;

    /// <summary>What the current plan would cost and whether it can be paid; null text when there is no plan.</summary>
    public string PlanText { get; private set; }
    public bool PlanAffordable { get; private set; }
    public string HoverText { get; private set; } = "";

    public void Init(Main m)
    {
        _m = m;
        var w = m.World;
        _voxSpace = new Node3D { Position = new Vector3(w.OriginX, 0, w.OriginZ), Scale = Vector3.One * VoxelWorld.Vox };
        AddChild(_voxSpace);
        _ghostMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/ghost.gdshader") };
        _ghost = new MeshInstance3D { MaterialOverride = _ghostMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
        _voxSpace.AddChild(_ghost);

        _shapeMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(.9f, .55f, .25f, .22f),
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            NoDepthTest = false,
        };
        _shape = new MeshInstance3D { MaterialOverride = _shapeMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
        AddChild(_shape);

        _cursor = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = .07f, OuterRadius = .095f, Rings = 24, RingSegments = 6 },
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = Ui.Accent,
                NoDepthTest = true,
                RenderPriority = 10,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_cursor);
    }

    // ---------------------------------------------------------------- tool state

    public void SetTool(Tool t)
    {
        Tool = Tool == t ? Tool.None : t;
        if (Tool != Tool.Paste) PasteBp = null;
        _lmb = _paint = _hasStart = false;
        _planKey = "~";
        _m.Hud.OnToolChanged();
    }

    public void StartPaste(Blueprint bp)
    {
        PasteBp = bp;
        PasteRot = 0;
        Tool = Tool.Paste;
        _planKey = "~";
        _m.Hud.OnToolChanged();
    }

    public void Cancel()
    {
        if (Tool == Tool.None) return;
        SetTool(Tool.None);
    }

    public void OptionsChanged() { _planKey = "~"; }

    /// <summary>Points the tools at a spot on the map as if the mouse were there (screenshots and tests).</summary>
    public void PointAt(Vector3 world)
    {
        var screen = _m.Rig.Cam.UnprojectPosition(world);
        Pick(screen);
    }

    // ---------------------------------------------------------------- input

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseMotion mm:
                Pick(mm.Position);
                if (_lmb && _paint && _hasHover)
                {
                    int dx = _hover.X - _lastPaint.x, dz = _hover.Z - _lastPaint.z;
                    if (dx * dx + dz * dz >= 25) { _lastPaint = (_hover.X, _hover.Z); PaintAt(_hover, false); }
                }
                break;

            case InputEventMouseButton mb when mb.ButtonIndex == MouseButton.Left && !mb.AltPressed:
                Pick(mb.Position);
                if (mb.Pressed) Press(mb);
                else if (_lmb) Release(mb);
                break;

            case InputEventKey k when k.Pressed && !k.Echo && !k.CtrlPressed && !k.MetaPressed:
                Key(k);
                break;
        }
    }

    void Key(InputEventKey k)
    {
        Tool t = k.PhysicalKeycode switch
        {
            global::Godot.Key.Key1 => Tool.Wall,
            global::Godot.Key.Key2 => Tool.Tower,
            global::Godot.Key.Key3 => Tool.Stairs,
            global::Godot.Key.Key4 => Tool.Ladder,
            global::Godot.Key.Key5 => Tool.Brush,
            global::Godot.Key.Key6 => Tool.Dig,
            global::Godot.Key.Key7 => Tool.Fill,
            global::Godot.Key.Key8 => Tool.Remove,
            global::Godot.Key.B => Tool.Capture,
            _ => Tool.None,
        };
        if (t != Tool.None) { SetTool(t); GetViewport().SetInputAsHandled(); return; }
        if (k.PhysicalKeycode == global::Godot.Key.Escape) { SetTool(Tool.None); return; }
        if (k.PhysicalKeycode == global::Godot.Key.R && Tool == Tool.Paste) { PasteRot = (PasteRot + 1) & 3; _planKey = "~"; _m.Hud.OnToolChanged(); }
    }

    void Press(InputEventMouseButton mb)
    {
        _lmb = true;
        _pressPos = mb.Position;
        _shift = mb.ShiftPressed;
        _hasStart = _hasHover;
        if (_hasHover) _startPt = _hover.Ground(_m.World);
        if (Tool is Tool.Brush or Tool.Dig or Tool.Fill or Tool.Remove)
        {
            _paint = true;
            if (_hasHover) { _lastPaint = (_hover.X, _hover.Z); PaintAt(_hover, true); }
        }
        else if (Tool == Tool.Stairs && _hasHover)
            _stairsTop = _hover.NY == 1 ? _hover.Y + 1 : _m.World.SurfAt(_hover.X, _hover.Z);
        else if (Tool == Tool.Ladder)
            Refuse(Hints[Tool.Ladder]);
    }

    void Release(InputEventMouseButton mb)
    {
        _lmb = false;
        bool wasPaint = _paint;
        _paint = false;
        var w = _m.World;
        if (Tool is Tool.Wall or Tool.Stairs or Tool.Capture)
        {
            if (!_hasStart || !_hasHover) return;
            var a = _startPt; var b = _hover.Ground(w);
            if (Tool == Tool.Capture)
            {
                var bp = Blueprint.Capture(w, a.x, a.z, b.x, b.z, _m.NextBlueprintId, null);
                if (bp == null) { Refuse("в рамке нет ваших построек"); return; }
                _m.NextBlueprintId++;
                _m.AddBlueprint(bp);
                _m.Hud.Log($"Снят {bp.Name}: {bp.Describe()}", Hud.Kind.Ok);
                SetTool(Tool.None);
                return;
            }
            if (Tool == Tool.Wall)
            {
                var L = Brushes.Wall(w, _m.Site, a.x, a.z, b.x, b.z, Opt.Profile);
                if (L == null) { Refuse("протяните стену подлиннее"); return; }
                Build(L, Opt.Profile.Name);
            }
            else
            {
                var L = Brushes.Stairs(w, _m.Site, a.x, a.z, _stairsTop, b.x, b.z, Opt.Material);
                if (L == null) { Refuse("начните сверху и тяните вниз, туда, где будут ступени"); return; }
                Build(L, "Ступени");
            }
            return;
        }
        if (wasPaint || (mb.Position - _pressPos).Length() > 8 || !_hasHover) return;
        if (Tool == Tool.Tower)
        {
            var g = _hover.Ground(w);
            Build(Brushes.Tower(w, _m.Site, g.x, g.z, Opt.TowerR, Opt.TowerH), "Башня");
        }
        else if (Tool == Tool.Paste && PasteBp != null)
            Build(PlanPasteAtHover(), PasteBp.Name);
    }

    void PaintAt(VoxelHit h, bool first)
    {
        var w = _m.World; var site = _m.Site; var wal = _m.Wallet;
        switch (Tool)
        {
            case Tool.Dig:
                if (first) _digFloor = (h.NY == 1 ? h.Y + 1 : h.Y) - Opt.DigDepth;
                var r = Builder.Dig(w, site, wal, h.X, h.Z, _digFloor, PaintRadius, out _, Chips(.02f));
                if (r == Builder.DigResult.NoGold) Refuse("нечем платить землекопам");
                break;
            case Tool.Fill:
                if (first) _fillTarget = w.SurfAt(h.X, h.Z) + 8;
                Build(Brushes.Fill(w, site, h.X, h.Z, _fillTarget, PaintRadius), "Насыпь", quiet: !first);
                break;
            case Tool.Remove:
                int n = Builder.RemoveSphere(w, wal, h.X, h.Y, h.Z, PaintRadius, Chips(.15f));
                if (n == 0 && first) Refuse("разобрать можно только своё");
                break;
            case Tool.Brush:
                int br = Opt.BrushR;
                if (_shift) Builder.RemoveSphere(w, wal, h.X, h.Y, h.Z, br, Chips(.15f));
                else Build(Brushes.Sphere(w, site, h.X + h.NX * br, h.Y + h.NY * br, h.Z + h.NZ * br, br, Opt.Material), "Кисть", quiet: true);
                break;
        }
        _m.Hud.Refresh();
    }

    Action<int, int, int, byte> Chips(float chance) => (x, y, z, m) =>
    {
        if (Random.Shared.NextDouble() < chance) _m.Debris.Spawn(m, x, y, z, 1, 1.4f);
    };

    bool Build(VoxelList L, string what, bool quiet = false)
    {
        var r = Builder.TryBuild(_m.World, _m.Wallet, L, out var c);
        switch (r)
        {
            case BuildResult.Ok:
                if (!quiet) _m.Hud.Log($"{what}: {Describe(c)}");
                _m.Hud.Refresh();
                _planKey = "~";
                return true;
            case BuildResult.NoGold: Refuse($"нужно {c.Gold} золота"); break;
            case BuildResult.NoEarth: Refuse($"нужно {Fmt.One(c.EarthM3)} м³ земли: выкопайте ров"); break;
            case BuildResult.Occupied: if (!quiet) Refuse("здесь уже занято"); break;
            default: if (!quiet) Refuse("здесь не построить"); break;
        }
        return false;
    }

    /// <summary>"камень 2,5 · земля 5,8 м³ · 28 зол."</summary>
    public static string Describe(Cost c)
    {
        var parts = new List<string>();
        if (c.Stone > 0) parts.Add("камень " + Fmt.M3(c.Stone));
        if (c.Wood > 0) parts.Add("брёвна " + Fmt.M3(c.Wood));
        if (c.Earth > 0) parts.Add("земля " + Fmt.M3(c.Earth));
        string vol = parts.Count > 0 ? string.Join(" · ", parts) + " м³" : "";
        return c.Gold > 0 ? $"{vol} · {c.Gold} зол." : vol;
    }

    string _lastRefuse = ""; double _lastRefuseAt = -9;
    void Refuse(string msg)
    {
        if (msg == _lastRefuse && _clock - _lastRefuseAt < .8) return;
        _lastRefuse = msg; _lastRefuseAt = _clock;
        _m.Hud.Float(msg, Ui.Bad);
    }

    // ---------------------------------------------------------------- picking and previews

    void Pick(Vector2 screen)
    {
        var (o, d) = _m.Rig.Ray(screen);
        var w = _m.World;
        _hasHover = Raycast.Cast(w, (o.X - w.OriginX) / VoxelWorld.Vox, o.Y / VoxelWorld.Vox, (o.Z - w.OriginZ) / VoxelWorld.Vox, d.X, d.Y, d.Z, out _hover);
        if (!_hasHover) { HoverText = ""; return; }
        int ground = w.GroundTop(_hover.X, _hover.Z);
        float above = (_hover.Y + 1 - ground) * VoxelWorld.Vox;
        HoverText = Mat.Def[_hover.M].Player && above > .05f
            ? $"{Mat.NameOf(_hover.M)} · {Fmt.One(above)} м над землёй"
            : Mat.NameOf(_hover.M);
    }

    VoxelList PlanPasteAtHover()
    {
        if (PasteBp == null || !_hasHover) return null;
        var g = _hover.Ground(_m.World);
        int w = PasteBp.RotW(PasteRot), d = PasteBp.RotD(PasteRot);
        return PasteBp.PlanPaste(_m.World, _m.Site, g.x - (w >> 1), g.z - (d >> 1), PasteRot, g.y);
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        UpdateCursor();
        UpdateShape();
        UpdatePlan();
    }

    void UpdateCursor()
    {
        _cursor.Visible = _hasHover && Tool != Tool.None;
        if (!_cursor.Visible) return;
        var w = _m.World;
        var p = new Vector3(w.WX(_hover.X + .5f + _hover.NX * .52f), w.WY(_hover.Y + .5f + _hover.NY * .52f), w.WZ(_hover.Z + .5f + _hover.NZ * .52f));
        Basis b = _hover.NX != 0 ? new Basis(Vector3.Forward, Mathf.Pi / 2) : _hover.NZ != 0 ? new Basis(Vector3.Right, Mathf.Pi / 2) : Basis.Identity;
        _cursor.Transform = new Transform3D(b, p);
    }

    void UpdateShape()
    {
        var w = _m.World;
        bool show = false;
        if (_hasHover && Tool is Tool.Dig or Tool.Fill)
        {
            var g = _hover.Ground(w);
            float h = (Tool == Tool.Dig ? Opt.DigDepth : 8) * VoxelWorld.Vox, r = PaintRadius * VoxelWorld.Vox;
            SetShape(new CylinderMesh { TopRadius = r, BottomRadius = r, Height = h, RadialSegments = 32 },
                new Vector3(w.WX(_hover.X + .5f), w.WY(g.y) + (Tool == Tool.Dig ? -h / 2 : h / 2), w.WZ(_hover.Z + .5f)),
                Tool == Tool.Dig ? new Color(.55f, .4f, .28f, .28f) : new Color(.9f, .55f, .25f, .22f));
            show = true;
        }
        else if (_hasHover && (Tool == Tool.Remove || (Tool == Tool.Brush && Input.IsKeyPressed(global::Godot.Key.Shift))))
        {
            float r = ((Tool == Tool.Remove ? PaintRadius : Opt.BrushR) + .5f) * VoxelWorld.Vox;
            SetShape(new SphereMesh { Radius = r, Height = r * 2, RadialSegments = 24, Rings = 12 },
                new Vector3(w.WX(_hover.X + .5f), w.WY(_hover.Y + .5f), w.WZ(_hover.Z + .5f)), new Color(.83f, .32f, .24f, .3f));
            show = true;
        }
        else if (Tool == Tool.Capture && _lmb && _hasStart && _hasHover)
        {
            var a = _startPt; var b = _hover.Ground(w);
            int x0 = Math.Min(a.x, b.x), x1 = Math.Max(a.x, b.x) + 1, z0 = Math.Min(a.z, b.z), z1 = Math.Max(a.z, b.z) + 1;
            float y = w.WY(Math.Max(a.y, b.y)) + .05f;
            SetShape(new BoxMesh { Size = new Vector3((x1 - x0) * VoxelWorld.Vox, .04f, (z1 - z0) * VoxelWorld.Vox) },
                new Vector3(w.WX((x0 + x1) / 2f), y, w.WZ((z0 + z1) / 2f)), new Color(.9f, .55f, .25f, .25f));
            show = true;
        }
        _shape.Visible = show;
    }

    string _shapeKey = "";
    void SetShape(PrimitiveMesh mesh, Vector3 at, Color c)
    {
        string key = mesh.GetType().Name + (mesh is BoxMesh bm ? bm.Size.ToString() : "") + (mesh is CylinderMesh cm ? cm.Height.ToString() : "") + (mesh is SphereMesh sm ? sm.Radius.ToString() : "");
        if (key != _shapeKey) { _shape.Mesh = mesh; _shapeKey = key; }
        _shape.Position = at;
        _shapeMat.AlbedoColor = c;
    }

    void UpdatePlan()
    {
        var w = _m.World;
        string key = "";
        if (_hasHover)
        {
            var g = _hover.Ground(w);
            key = Tool switch
            {
                Tool.Wall or Tool.Stairs when _lmb && _hasStart => $"{Tool}|{_startPt}|{g}|{Opt.Profile.Key}|{Opt.Material}|{_stairsTop}",
                Tool.Tower => $"T|{g}|{Opt.TowerR}|{Opt.TowerH}",
                Tool.Paste when PasteBp != null => $"P|{PasteBp.Id}|{g}|{PasteRot}",
                Tool.Brush when !Input.IsKeyPressed(global::Godot.Key.Shift) => $"B|{_hover.X},{_hover.Y},{_hover.Z}|{_hover.NX}{_hover.NY}{_hover.NZ}|{Opt.BrushR}|{Opt.Material}",
                _ => "",
            };
            if (key.Length > 0) key += "|" + w.Revision + "|" + (int)_m.Wallet.Gold + "|" + (int)(_m.Wallet.Earth * 10);
        }
        if (key == _planKey) return;
        // long walls are heavy to plan; keep up with the mouse without planning every pixel
        if (key.Length > 0 && _planKey.Length > 0 && _clock - _planClock < .05) return;
        _planKey = key;
        _planClock = _clock;
        if (key.Length == 0) { _plan = null; _ghost.Visible = false; PlanText = null; return; }

        var g2 = _hover.Ground(w);
        _plan = Tool switch
        {
            Tool.Wall => Brushes.Wall(w, _m.Site, _startPt.x, _startPt.z, g2.x, g2.z, Opt.Profile),
            Tool.Stairs => Brushes.Stairs(w, _m.Site, _startPt.x, _startPt.z, _stairsTop, g2.x, g2.z, Opt.Material),
            Tool.Tower => Brushes.Tower(w, _m.Site, g2.x, g2.z, Opt.TowerR, Opt.TowerH),
            Tool.Paste => PlanPasteAtHover(),
            Tool.Brush => Brushes.Sphere(w, _m.Site, _hover.X + _hover.NX * Opt.BrushR, _hover.Y + _hover.NY * Opt.BrushR, _hover.Z + _hover.NZ * Opt.BrushR, Opt.BrushR, Opt.Material),
            _ => null,
        };
        var check = Builder.Check(_m.Wallet, _plan, out var cost);
        if (check is BuildResult.Nothing or BuildResult.Occupied) { _ghost.Visible = false; PlanText = null; return; }
        PlanAffordable = check == BuildResult.Ok;
        string name = Tool switch { Tool.Wall => Opt.Profile.Name, Tool.Stairs => "Ступени", Tool.Tower => "Башня", Tool.Paste => PasteBp?.Name ?? "", _ => "Кисть" };
        PlanText = PlanAffordable ? $"{name}: {Describe(cost)}"
            : check == BuildResult.NoGold ? $"{name}: {Describe(cost)} — не хватает золота"
            : $"{name}: {Describe(cost)} — не хватает земли";
        _ghost.Mesh = MeshPlan(_plan);
        _ghost.Visible = _ghost.Mesh != null;
        _ghostMat.SetShaderParameter("tint", PlanAffordable ? new Vector3(.9f, .55f, .25f) : new Vector3(.85f, .2f, .16f));
        _ghostMat.SetShaderParameter("tint_mix", PlanAffordable ? .35f : .6f);
    }

    ArrayMesh MeshPlan(VoxelList L)
    {
        if (L == null || !L.Bounds(out int x0, out int y0, out int z0, out int x1, out int y1, out int z1)) return null;
        int dx = x1 - x0 + 1, dy = y1 - y0 + 1, dz = z1 - z0 + 1, px = dx + 2, pz = dz + 2;
        long size = (long)px * (dy + 2) * pz;
        if (size > 24_000_000) return null;
        if (_grid.Length < size) _grid = new byte[size]; else Array.Clear(_grid, 0, (int)size);
        for (int i = 0; i < L.Count; i++)
        {
            byte m = L.M(i);
            if (m != 0) _grid[((L.Y(i) - y0 + 1) * pz + (L.Z(i) - z0 + 1)) * px + (L.X(i) - x0 + 1)] = m;
        }
        return GodotMesh.From(_mesher.Mesh(_grid, dx, dy, dz, x0, y0, z0));
    }
}
