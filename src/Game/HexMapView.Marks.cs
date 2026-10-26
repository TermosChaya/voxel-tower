using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The way a hero would go, laid out as «Heroes» lays it: an arrow on every hex of it toward the next, green as far as
/// he gets today, red beyond; a cross on the hex it ends at, with the days it takes.
/// </summary>
public partial class HexMapView
{
    MultiMeshInstance3D _arrows;
    MeshInstance3D _cross;
    Label3D _days;
    static readonly Color Today = new("2fb52f"), Later = new("d2261a");
    /// <summary>The way shown, for the self-test: how many arrows, how many of them green.</summary>
    public int ArrowsShown { get; private set; }
    public int ArrowsToday { get; private set; }

    void MarksReady()
    {
        var mat = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            NoDepthTest = false, RenderPriority = 1,
        };
        _arrows = new MultiMeshInstance3D
        {
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, MaterialOverride = mat, Visible = false,
            Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = ArrowMesh() },
        };
        _world.AddChild(_arrows);
        _cross = new MeshInstance3D { Mesh = CrossMesh(), MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
        _world.AddChild(_cross);
        _days = new Label3D
        {
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 30, OutlineSize = 9, Modulate = new Color("f3e6c4"),
            OutlineModulate = new Color(0, 0, 0, .85f), NoDepthTest = true, PixelSize = .0006f, FixedSize = true, Font = Ornament.SerifBold,
        };
        _cross.AddChild(_days);
    }

    /// <summary>An arrow lying flat, pointing along +x, a unit long: its shaft and its head.</summary>
    static ArrayMesh ArrowMesh()
    {
        var pts = new List<Vector3>
        {
            // the shaft
            new(-.5f, 0, -.09f), new(.12f, 0, -.09f), new(.12f, 0, .09f), new(-.5f, 0, -.09f), new(.12f, 0, .09f), new(-.5f, 0, .09f),
            // the head
            new(.12f, 0, -.26f), new(.5f, 0, 0), new(.12f, 0, .26f),
        };
        return Flat(pts);
    }

    static ArrayMesh CrossMesh()
    {
        var pts = new List<Vector3>();
        foreach (var a in new[] { MathF.PI / 4, -MathF.PI / 4 })
        {
            float c = MathF.Cos(a), s = MathF.Sin(a);
            Vector3 R(float x, float z) => new(x * c - z * s, 0, x * s + z * c);
            pts.AddRange(new[] { R(-.5f, -.1f), R(.5f, -.1f), R(.5f, .1f), R(-.5f, -.1f), R(.5f, .1f), R(-.5f, .1f) });
        }
        return Flat(pts);
    }

    static ArrayMesh Flat(List<Vector3> pts)
    {
        var mesh = new ArrayMesh();
        using var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        var nrm = new Vector3[pts.Count];
        var col = new Color[pts.Count];
        for (int i = 0; i < pts.Count; i++) { nrm[i] = Vector3.Up; col[i] = Colors.White; }
        arrays[(int)Mesh.ArrayType.Vertex] = pts.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = nrm;
        arrays[(int)Mesh.ArrayType.Color] = col;
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    /// <summary>
    /// The way from a hex laid out: an arrow on it and on every step but the last toward the next, green on the steps
    /// reached today (day 0), red on those beyond; a cross on the last with the days the way takes.
    /// </summary>
    public void ShowPath(int fromCol, int fromRow, IReadOnlyList<(int Col, int Row, int Day)> steps)
    {
        if (steps == null || steps.Count == 0) { HidePath(); return; }
        GridPath(steps);
        var mm = _arrows.Multimesh;
        mm.InstanceCount = steps.Count;
        float len = HexMapLayout.W * .5f * V, lift = 1.3f * V;
        ArrowsToday = 0;
        var prev = HexToScene(fromCol, fromRow);
        for (int i = 0; i < steps.Count; i++)
        {
            var at = i == 0 ? prev : HexToScene(steps[i - 1].Col, steps[i - 1].Row);
            var to = HexToScene(steps[i].Col, steps[i].Row);
            var d = to - at;
            float yaw = MathF.Atan2(-d.Z, d.X);
            // halfway between the two hexes' middles, so each arrow shows the step it stands for
            var mid = at.Lerp(to, .5f);
            var basis = new Basis(Vector3.Up, yaw).Scaled(new Vector3(len, 1, len));
            mm.SetInstanceTransform(i, new Transform3D(basis, mid + Vector3.Up * (lift + MathF.Max(0, MathF.Max(at.Y, to.Y) - mid.Y))));
            bool today = steps[i].Day == 0;
            if (today) ArrowsToday++;
            mm.SetInstanceColor(i, today ? Today : Later);
        }
        ArrowsShown = steps.Count;
        _arrows.Visible = true;
        var last = steps[^1];
        _cross.Position = HexToScene(last.Col, last.Row) + Vector3.Up * lift;
        _cross.Scale = new Vector3(len, 1, len) * 1.1f;
        _cross.Visible = true;
        int days = last.Day + 1;
        _days.Text = days == 1 ? "" : $"{days} {DaysWord(days)}";
        _days.Position = new Vector3(0, .9f, 0);
        _days.Modulate = last.Day == 0 ? new Color("e9f5dc") : new Color("ffd9cf");
        // the cross's colour: as its step's
        _cross.MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor = last.Day == 0 ? Today : Later, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, CullMode = BaseMaterial3D.CullModeEnum.Disabled, RenderPriority = 1,
        };
    }

    public void HidePath()
    {
        if (_gridPath.Count > 0) GridPath(null);
        _arrows.Visible = false;
        _cross.Visible = false;
        ArrowsShown = ArrowsToday = 0;
    }

    static string DaysWord(int n) => (n % 100) is >= 11 and <= 14 ? "дней" : (n % 10) switch { 1 => "день", 2 or 3 or 4 => "дня", _ => "дней" };
}
