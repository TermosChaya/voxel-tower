using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// What is seen of the old powers at work. When a life is given at the altar, a flash of dark violet light over it and
/// a swarm of pale motes rising from it. And the spells: gobbets of burning pitch coming down out of the sky, a bolt of
/// lightning with its flash, a bog dark on the ground, a ring of dread running out over the field, iron rings about the
/// feet of those held in chains, a red mark over every raider while the Eye is open, dust where earth rises or falls,
/// a green glimmer where the dead get up.
/// </summary>
public partial class MagicView : Node3D
{
    sealed class Flash { public OmniLight3D Light; public Node3D Thing; public double At; public float Energy, Life; }
    sealed class Ring { public MeshInstance3D Node; public StandardMaterial3D Mat; public double At; public float R; }

    Main _m;
    readonly List<Flash> _flashes = new();
    readonly List<Ring> _rings = new();
    readonly Dictionary<Mire, (MeshInstance3D node, StandardMaterial3D mat)> _mires = new();
    MultiMeshInstance3D _drops, _chains, _marks;
    StandardMaterial3D _mote, _bolt;
    double _t;
    static readonly Color Violet = new(.58f, .32f, .98f), Pale = new(.68f, .98f, .82f), Pitch = new(1f, .52f, .16f), Dust = new(.47f, .4f, .33f);

    public void Init(Main m)
    {
        _m = m;
        _mote = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        _bolt = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(.86f, .9f, 1f) };
        _drops = Multi(new SphereMesh { Radius = .2f, Height = .4f, RadialSegments = 8, Rings = 4 },
            new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = Pitch });
        _chains = Multi(new TorusMesh { InnerRadius = .82f, OuterRadius = 1f, Rings = 18, RingSegments = 5 },
            new StandardMaterial3D { AlbedoColor = new Color(.16f, .15f, .17f), Metallic = .7f, Roughness = .45f });
        _marks = Multi(new BoxMesh { Size = Vector3.One * .14f },
            new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(1f, .16f, .1f), NoDepthTest = true, RenderPriority = 5 });
        m.Sim.RiteDone += Given;
        m.Sim.Spelled += Cast;
        m.Sim.Struck += Bolt;
    }

    MultiMeshInstance3D Multi(Mesh mesh, Material mat)
    {
        var mi = new MultiMeshInstance3D
        {
            Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, InstanceCount = 64, VisibleInstanceCount = 0, Mesh = mesh },
            MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, CustomAabb = new Aabb(new Vector3(-1e4f, -1e4f, -1e4f), new Vector3(2e4f, 2e4f, 2e4f)),
        };
        AddChild(mi);
        return mi;
    }

    Vector3 At(float x, float y, float z) => new(_m.World.WX(x), _m.World.WY(y), _m.World.WZ(z));

    /// <summary>A light that flares and fades, with whatever goes with it, gone after a while.</summary>
    void Flare(Vector3 at, Color c, float energy, float range, float life, Node3D thing = null)
    {
        var light = new OmniLight3D { Position = at, LightColor = c, LightEnergy = energy, OmniRange = range, ShadowEnabled = false };
        AddChild(light);
        _flashes.Add(new Flash { Light = light, Thing = thing, At = _t, Energy = energy, Life = life });
    }

    /// <summary>A burst of motes from a spot, rising or falling, in a colour.</summary>
    CpuParticles3D Motes(Vector3 at, Color c, int n, float up, float spread, float size = .06f)
    {
        var ramp = new Gradient();
        ramp.SetColor(0, new Color(c, .95f));
        ramp.SetColor(1, new Color(c, 0));
        var p = new CpuParticles3D
        {
            Position = at, Amount = n, Lifetime = 2, OneShot = true, Explosiveness = .85f, Emitting = true,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere, EmissionSphereRadius = spread,
            Direction = Vector3.Up, Spread = 40, InitialVelocityMin = up * .3f, InitialVelocityMax = up, Gravity = new Vector3(0, up > 0 ? .3f : -6, 0),
            DampingMin = .4f, DampingMax = 1, ScaleAmountMin = .6f, ScaleAmountMax = 1.3f, ColorRamp = ramp,
            Mesh = new BoxMesh { Size = Vector3.One * size, Material = _mote },
        };
        AddChild(p);
        return p;
    }

    /// <summary>A life given at the altar: the flash and the motes over where it knelt.</summary>
    void Given(float x, float y, float z)
    {
        var at = At(x, y + 6, z);
        Flare(at + Vector3.Up * .6f, Violet, 9, 9, 3.5f, Motes(at, Pale, 48, 2.2f, .45f));
    }

    void Cast(SpellKind k, float x, float y, float z)
    {
        var w = _m.World;
        var ground = At(x, w.SurfAt(Math.Clamp((int)x, 0, w.SX - 1), Math.Clamp((int)z, 0, w.SZ - 1)), z);
        var sp = Sim.SpellOf(k);
        switch (k)
        {
            case SpellKind.Hand:
            case SpellKind.Fall:
                Flare(At(x, y + 4, z), new Color(.8f, .66f, .5f), 2, 6, 2.5f, Motes(k == SpellKind.Fall ? At(x, y, z) : ground, Dust, 70, 1.6f, 1.2f, .12f));
                break;
            case SpellKind.Mire:
                Flare(ground + Vector3.Up, new Color(.4f, .6f, .3f), 2, 9, 2, Motes(ground, new Color(.3f, .4f, .22f), 40, 1.2f, sp.Radius * VoxelWorld.Vox * .6f));
                break;
            case SpellKind.Dread:
                Spread(ground, sp.Radius, new Color(.42f, .1f, .62f));
                Flare(ground + Vector3.Up * 2, Violet, 6, 12, 1.5f);
                break;
            case SpellKind.Chains:
                Spread(ground, sp.Radius, new Color(.3f, .3f, .34f));
                Flare(ground + Vector3.Up * 1.5f, new Color(.5f, .48f, .62f), 4, 9, 1.2f);
                break;
            case SpellKind.Raise:
                Flare(ground + Vector3.Up * 1.5f, Pale, 5, 12, 3, Motes(ground, Pale, 80, 1.8f, sp.Radius * VoxelWorld.Vox * .5f));
                break;
            case SpellKind.Eye:
                Flare(ground + Vector3.Up * 3, new Color(1f, .3f, .2f), 3, 10, 1.5f);
                break;
            case SpellKind.FireRain:
                Flare(ground + Vector3.Up * 8, Pitch, 3, 16, 4);
                break;
        }
    }

    /// <summary>A ring running out over the ground from where the spell fell, to its edge, fading as it goes.</summary>
    void Spread(Vector3 at, float r, Color c)
    {
        var mat = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = c, Transparency = BaseMaterial3D.TransparencyEnum.Alpha };
        var node = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = .9f, OuterRadius = 1, Rings = 48, RingSegments = 4 }, MaterialOverride = mat, Position = at + Vector3.Up * .15f };
        AddChild(node);
        _rings.Add(new Ring { Node = node, Mat = mat, At = _t, R = r * VoxelWorld.Vox });
    }

    /// <summary>A bolt out of the sky: a jagged line from high above down to where it struck, flickering, and its flash.</summary>
    void Bolt(float x, float y, float z)
    {
        var holder = new Node3D();
        AddChild(holder);
        var rnd = new Random((int)(x * 7 + z * 13));
        var from = At(x + (float)(rnd.NextDouble() - .5) * 60, y + 320, z + (float)(rnd.NextDouble() - .5) * 60);
        var to = At(x, y + 1, z);
        var prev = from;
        for (int i = 1; i <= 9; i++)
        {
            float t = i / 9f;
            var p = i == 9 ? to : from.Lerp(to, t) + new Vector3((float)(rnd.NextDouble() - .5) * 1.6f, 0, (float)(rnd.NextDouble() - .5) * 1.6f);
            var d = p - prev;
            var seg = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(.09f, .09f, d.Length()) }, MaterialOverride = _bolt };
            holder.AddChild(seg);
            seg.LookAtFromPosition(prev + d / 2, p, Mathf.Abs(d.Normalized().Y) > .99f ? Vector3.Forward : Vector3.Up);
            prev = p;
        }
        Flare(to + Vector3.Up * 2, new Color(.78f, .84f, 1f), 16, 20, .6f, holder);
    }

    public override void _Process(double delta)
    {
        _t += delta;
        var sim = _m.Sim;
        if (sim == null) return;
        for (int i = _flashes.Count - 1; i >= 0; i--)
        {
            var f = _flashes[i];
            float age = (float)(_t - f.At);
            // a hard flash, then a slow fading glow; a bolt flickers and is gone
            f.Light.LightEnergy = age < .12f ? f.Energy : f.Energy * MathF.Exp(-(age - .12f) * 4 / f.Life);
            if (f.Thing is Node3D n && n is not CpuParticles3D) n.Visible = age < .12f || age > .16f && age < .4f;
            if (age < f.Life + 1.5f) continue;
            f.Light.QueueFree(); f.Thing?.QueueFree();
            _flashes.RemoveAt(i);
        }
        for (int i = _rings.Count - 1; i >= 0; i--)
        {
            var r = _rings[i];
            float age = (float)(_t - r.At), k = Math.Clamp(age / .9f, 0, 1);
            r.Node.Scale = Vector3.One * Math.Max(.05f, r.R * (1 - (1 - k) * (1 - k)));
            r.Mat.AlbedoColor = new Color(r.Mat.AlbedoColor, .85f * (1 - k));
            if (k < 1) continue;
            r.Node.QueueFree();
            _rings.RemoveAt(i);
        }
        Bogs(sim);
        Drops(sim);
        Chains(sim);
        Marks(sim);
    }

    /// <summary>Each bog of the spell a dark, wet disc on the ground, fading in and out.</summary>
    void Bogs(Sim sim)
    {
        var w = _m.World;
        foreach (var mire in sim.Mires)
        {
            if (_mires.ContainsKey(mire)) continue;
            var mat = new StandardMaterial3D { AlbedoColor = new Color(.13f, .16f, .09f, 0), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = .15f, Metallic = .1f };
            float r = mire.R * VoxelWorld.Vox;
            var node = new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = r, BottomRadius = r, Height = .05f, RadialSegments = 40 }, MaterialOverride = mat,
                Position = At(mire.X, w.SurfAt(Math.Clamp((int)mire.X, 0, w.SX - 1), Math.Clamp((int)mire.Z, 0, w.SZ - 1)), mire.Z) + Vector3.Up * .07f,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(node);
            _mires[mire] = (node, mat);
        }
        List<Mire> gone = null;
        foreach (var (mire, (node, mat)) in _mires)
        {
            double left = mire.Until - sim.Time, age = 60 - left;
            if (!sim.Mires.Contains(mire) || left <= 0) { node.QueueFree(); (gone ??= new()).Add(mire); continue; }
            float a = (float)Math.Clamp(Math.Min(age / 1.5, left / 3), 0, 1) * .72f;
            mat.AlbedoColor = new Color(.13f, .16f, .09f, a);
        }
        if (gone != null) foreach (var m in gone) _mires.Remove(m);
    }

    /// <summary>The burning pitch, the last second of its fall.</summary>
    void Drops(Sim sim)
    {
        var mm = _drops.Multimesh;
        int n = 0;
        foreach (var d in sim.Drops)
        {
            double left = d.At - sim.Time;
            if (left > 1.1 || left < 0) continue;
            if (n >= mm.InstanceCount) mm.InstanceCount *= 2;
            mm.SetInstanceTransform(n++, new Transform3D(Basis.Identity, At(d.X, d.Y + (float)left * 200, d.Z)));
        }
        mm.VisibleInstanceCount = n;
    }

    /// <summary>Two iron rings about each raider held fast: at its feet and about its middle.</summary>
    void Chains(Sim sim)
    {
        var mm = _chains.Multimesh;
        int n = 0;
        foreach (var u in sim.Units)
        {
            if (u.Dead || u.Inside || u.ChainedUntil <= sim.Time) continue;
            float r = (u.T.BodyR + 1.2f) * VoxelWorld.Vox;
            foreach (float h in new[] { .1f, .48f })
            {
                if (n >= mm.InstanceCount) mm.InstanceCount *= 2;
                mm.SetInstanceTransform(n++, new Transform3D(Basis.Identity.Scaled(new Vector3(r, r * .5f, r)), At(u.X, u.Y + u.T.BodyH * h, u.Z)));
            }
        }
        mm.VisibleInstanceCount = n;
    }

    /// <summary>The Eye open: a red mark over every raider in the valley, seen through walls and woods.</summary>
    void Marks(Sim sim)
    {
        var mm = _marks.Multimesh;
        int n = 0;
        if (sim.Time < sim.EyeUntil)
        {
            var turn = new Basis(Vector3.Up, (float)_t * 2) * new Basis(Vector3.Forward, MathF.PI / 4);
            foreach (var u in sim.Units)
            {
                if (u.Dead || u.Inside || u.Bound || u.Team != Team.Horde) continue;
                if (n >= mm.InstanceCount) mm.InstanceCount *= 2;
                mm.SetInstanceTransform(n++, new Transform3D(turn, At(u.X, u.Y + u.T.BodyH + 7 + MathF.Sin((float)_t * 3 + u.Id) * 1.2f, u.Z)));
            }
        }
        mm.VisibleInstanceCount = n;
    }

    public override void _ExitTree()
    {
        if (_m?.Sim == null) return;
        _m.Sim.RiteDone -= Given;
        _m.Sim.Spelled -= Cast;
        _m.Sim.Struck -= Bolt;
    }
}
