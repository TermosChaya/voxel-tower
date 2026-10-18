using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// What the chosen archers can shoot at: the ground within their reach that they can see from where they stand,
/// washed in ember a metre at a time, and the edge of their reach as a thin ring. At night the dark ground is theirs
/// only as far as they can make a man out in it; what is lit they reach all the way. Worked out again when one of them
/// moves, or every few seconds while the walls and the light change round them.
/// </summary>
public partial class CoverView : Node3D
{
    Main _m;
    MeshInstance3D _mesh;
    string _key = "";
    double _at;

    public void Init(Main m)
    {
        _m = m;
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            VertexColorUseAsAlbedo = true,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            RenderPriority = 1,
        };
        _mesh = new MeshInstance3D { MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_mesh);
    }

    public override void _Process(double delta)
    {
        if (_m?.Sim == null) return;
        var archers = new List<Unit>();
        foreach (var u in _m.Selected) if (!u.Dead && u.T.Ranged && archers.Count < 8) archers.Add(u);
        archers.Sort((a, b) => a.Id.CompareTo(b.Id));
        string key = string.Join(",", archers.ConvertAll(u => $"{u.Id}@{u.Cell}"));
        double now = _m.Sim.Time;
        if (key == _key && (archers.Count == 0 || now - _at < 3)) return;
        _key = key; _at = now;
        if (archers.Count == 0) { _mesh.Mesh = null; return; }
        Rebuild(archers);
    }

    void Rebuild(List<Unit> archers)
    {
        var w = _m.World; var sim = _m.Sim;
        var seen = new HashSet<(int, int)>();
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        // fainter at night, when there is little else on the ground to wash over
        var lit = new Color(Ornament.EmberGlow, .11f - .045f * sim.Waves.Darkness);
        const int Cell = VoxelWorld.PerMetre;
        foreach (var a in archers)
        {
            float range = a.T.Range;
            int r = (int)(range / Cell), ci = (int)a.X / Cell, ck = (int)a.Z / Cell;
            for (int dk = -r; dk <= r; dk++)
                for (int di = -r; di <= r; di++)
                {
                    if (di * di + dk * dk > r * r) continue;
                    int i = ci + di, k = ck + dk;
                    if (i < 0 || k < 0 || i * Cell >= w.SX || k * Cell >= w.SZ || seen.Contains((i, k))) continue;
                    int x = i * Cell + Cell / 2, z = k * Cell + Cell / 2, y = w.SurfAt(x, z);
                    // in the dark only as far as a man can be made out, unless it is lit
                    float far = sim.RangeAt(a, x, z);
                    if ((x - a.X) * (x - a.X) + (z - a.Z) * (z - a.Z) > far * far) continue;
                    // a man's chest over that ground, as an arrow would find him
                    if (!sim.Sees(a, x, y + 8, z)) continue;
                    seen.Add((i, k));
                    float y0 = y + .4f;
                    Quad(st, w, new Vector3(i * Cell, y0, k * Cell), new Vector3((i + 1) * Cell, y0, k * Cell),
                        new Vector3((i + 1) * Cell, y0, (k + 1) * Cell), new Vector3(i * Cell, y0, (k + 1) * Cell), lit);
                }
            // the edge of its reach
            var edge = new Color(Ornament.EmberGlow, .55f);
            const int Steps = 96;
            for (int s = 0; s < Steps; s++)
            {
                float t0 = s * MathF.Tau / Steps, t1 = (s + 1) * MathF.Tau / Steps;
                Vector3 P(float t, float rr)
                {
                    float x = a.X + MathF.Cos(t) * rr, z = a.Z + MathF.Sin(t) * rr;
                    int gx = Math.Clamp((int)x, 0, w.SX - 1), gz = Math.Clamp((int)z, 0, w.SZ - 1);
                    return new Vector3(x, w.SurfAt(gx, gz) + .8f, z);
                }
                Quad(st, w, P(t0, range - 2), P(t0, range), P(t1, range), P(t1, range - 2), edge);
            }
        }
        _mesh.Mesh = st.Commit();
    }

    static void Quad(SurfaceTool st, VoxelWorld w, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col)
    {
        foreach (var v in new[] { a, b, c, a, c, d })
        {
            st.SetColor(col);
            st.AddVertex(new Vector3(w.WX(v.X), w.WY(v.Y), w.WZ(v.Z)));
        }
    }
}
