using System;
using Godot;

namespace VoxelTower.Game;

/// <summary>
/// The smear a blade leaves in the eye as it goes through a blow: a ribbon over the outer part of the blade along the
/// way it came the last few hundredths of a second, bright where the blade is now and gone behind. The places are worked out from
/// the stroke itself, not remembered from frame to frame, so a blow seen with the game stopped still shows its smear.
/// </summary>
public partial class BladeTrail : MeshInstance3D
{
    ImmediateMesh _mesh;
    bool _begun;
    int _drawn;

    /// <summary>Ribbons drawn in the last frame, and in all (the self-test asks).</summary>
    public int Drawn { get; private set; }
    public int Ever { get; private set; }

    public void Init()
    {
        _mesh = new ImmediateMesh();
        Mesh = _mesh;
        CastShadow = ShadowCastingSetting.Off;
        MaterialOverride = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        CustomAabb = new Aabb(new Vector3(-1e4f, -1e3f, -1e4f), new Vector3(2e4f, 2e3f, 2e4f));
    }

    /// <summary>A new frame: last frame's ribbons are gone.</summary>
    public void Clear()
    {
        if (_begun) _mesh.SurfaceEnd();
        _begun = false;
        _mesh.ClearSurfaces();
        Drawn = _drawn;
        _drawn = 0;
    }

    /// <summary>
    /// One blade's ribbon: where its hilt end and its point were (world metres), newest first, n of them, and how it
    /// shines (steel pale, a club dim) and how strongly.
    /// </summary>
    public void Ribbon(ReadOnlySpan<Vector3> roots, ReadOnlySpan<Vector3> tips, int n, Color c, float strength)
    {
        if (n < 2) return;
        if (!_begun) { _mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles); _begun = true; }
        _drawn++; Ever++;
        for (int i = 1; i < n; i++)
        {
            // the older the fainter, and fainter toward the hilt
            float a0 = (1 - (i - 1) / (float)(n - 1)) * strength, a1 = (1 - i / (float)(n - 1)) * strength;
            Color Tip(float a) => new(c.R * a, c.G * a, c.B * a, a);
            Color Root(float a) => new(c.R * a * .1f, c.G * a * .1f, c.B * a * .1f, a * .1f);
            Vector3 r0 = roots[i - 1].Lerp(tips[i - 1], .55f), r1 = roots[i].Lerp(tips[i], .55f);
            _mesh.SurfaceSetColor(Root(a0)); _mesh.SurfaceAddVertex(r0);
            _mesh.SurfaceSetColor(Tip(a0)); _mesh.SurfaceAddVertex(tips[i - 1]);
            _mesh.SurfaceSetColor(Tip(a1)); _mesh.SurfaceAddVertex(tips[i]);
            _mesh.SurfaceSetColor(Root(a0)); _mesh.SurfaceAddVertex(r0);
            _mesh.SurfaceSetColor(Tip(a1)); _mesh.SurfaceAddVertex(tips[i]);
            _mesh.SurfaceSetColor(Root(a1)); _mesh.SurfaceAddVertex(r1);
        }
    }

    /// <summary>The frame's ribbons are all given.</summary>
    public void Done()
    {
        if (_begun) _mesh.SurfaceEnd();
        _begun = false;
    }
}
