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
/// a green glimmer where the dead get up. And what flies: the pieces of the world broken loose, voxel by voxel as they
/// stood, tumbling on their arcs and raising dust where they come down; the violet glow of the hand that holds one; the
/// flash and the cloud of a blast. A stone skin glimmers on our walls, the fog of the dead creeps over the ground,
/// sickly motes hang over the plague-struck and a drop of blood over those under the oath. A blast is a ball of fire
/// swelling and gone, sparks flung every way and a column of smoke going up; a great piece flying or a ball rolling
/// leaves a trail of dust, and one coming down hard throws up chips of what it struck. The earth shaken throws up dust
/// all over its circle; a falling star burns and smokes all the way down; a whirlwind is a funnel of dust and grit
/// turning on itself, walking the field.
/// </summary>
public partial class MagicView : Node3D
{
    sealed class Flash { public OmniLight3D Light; public Node3D Thing; public double At; public float Energy, Life; }
    sealed class Ring { public MeshInstance3D Node; public StandardMaterial3D Mat; public double At; public float R; }
    /// <summary>The ball of fire of a blast: swelling, paling from white through orange to smoke, and gone.</summary>
    sealed class Fireball { public MeshInstance3D Node; public StandardMaterial3D Mat; public double At; public float R; }
    readonly List<Fireball> _fireballs = new();
    /// <summary>When each piece in the air last left a puff of dust behind it, and how many puffs may be made now.</summary>
    readonly Dictionary<int, double> _trailAt = new();
    float _trailBudget;

    Main _m;
    readonly List<Flash> _flashes = new();
    readonly List<Ring> _rings = new();
    readonly Dictionary<Mire, (MeshInstance3D node, StandardMaterial3D mat)> _mires = new();
    readonly Dictionary<Curse, (MeshInstance3D node, StandardMaterial3D mat)> _curses = new();
    static readonly Color Hex = new(.45f, .85f, .28f);
    MultiMeshInstance3D _drops, _chains, _marks, _sick, _sworn, _sparks, _whirls;
    double _starAt;
    /// <summary>The fire of a falling star, lighting the field under it as it comes down.</summary>
    OmniLight3D _starLight;
    StandardMaterial3D _mote, _bolt, _fogMat;
    /// <summary>Sparks of a blast: burning bright enough to glow.</summary>
    StandardMaterial3D _ember;
    /// <summary>The voxels of the pieces in the air, one box each; their buffer, written whole every frame.</summary>
    MultiMeshInstance3D _bits;
    float[] _bitBuf = new float[0];
    /// <summary>The glow of the hand of telekinesis, and the ring under what it holds.</summary>
    OmniLight3D _grip;
    MeshInstance3D _gripRing;
    readonly Dictionary<Patch, (MeshInstance3D node, StandardMaterial3D mat)> _skins = new();
    /// <summary>The puffs of the fog of the dead, flat and soft, lying low over the ground.</summary>
    MultiMeshInstance3D _fog;
    /// <summary>Dust and chips that go when they are done.</summary>
    readonly List<(Node3D node, double until)> _passing = new();
    double _dustAt;
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
        _ember = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(3.2f, 1.6f, .45f) };
        _drops = Multi(new SphereMesh { Radius = .2f, Height = .4f, RadialSegments = 8, Rings = 4 },
            new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = Pitch });
        _chains = Multi(new TorusMesh { InnerRadius = .82f, OuterRadius = 1f, Rings = 18, RingSegments = 5 },
            new StandardMaterial3D { AlbedoColor = new Color(.16f, .15f, .17f), Metallic = .7f, Roughness = .45f });
        _marks = Multi(new BoxMesh { Size = Vector3.One * .14f },
            new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(1f, .16f, .1f), NoDepthTest = true, RenderPriority = 5 });
        _sick = Multi(new BoxMesh { Size = Vector3.One * .07f },
            new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(.78f, .84f, .26f) });
        _sworn = Multi(new SphereMesh { Radius = .07f, Height = .2f, RadialSegments = 8, Rings = 4 },
            new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(.72f, .05f, .06f) });
        _sparks = Multi(new BoxMesh { Size = Vector3.One * .05f },
            new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(1f, .78f, .3f) });
        _whirls = Multi(new BoxMesh { Size = Vector3.One * .22f }, new StandardMaterial3D { AlbedoColor = new Color(.3f, .27f, .24f), Roughness = 1 });
        _starLight = new OmniLight3D { LightColor = new Color(1f, .55f, .22f), LightEnergy = 0, OmniRange = 18, ShadowEnabled = false, Visible = false };
        AddChild(_starLight);
        _bits = new MultiMeshInstance3D
        {
            Multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, InstanceCount = 0,
                Mesh = new BoxMesh { Size = Vector3.One * VoxelWorld.Vox, Material = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = .92f } },
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On, CustomAabb = new Aabb(new Vector3(-1e4f, -1e4f, -1e4f), new Vector3(2e4f, 2e4f, 2e4f)),
        };
        AddChild(_bits);
        _grip = new OmniLight3D { LightColor = Violet, LightEnergy = 0, OmniRange = 7, ShadowEnabled = false, Visible = false };
        AddChild(_grip);
        _gripRing = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = .9f, OuterRadius = 1, Rings = 40, RingSegments = 4 }, Visible = false,
            MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(Violet, .55f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_gripRing);
        // a soft round puff, thick in the middle and thinning to nothing at its edge
        var puff = new GradientTexture2D
        {
            Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(.5f, .5f), FillTo = new Vector2(1f, .5f), Width = 64, Height = 64,
            Gradient = new Gradient { Colors = new[] { Colors.White, new Color(1, 1, 1, 0) }, Offsets = new[] { 0f, 1f } },
        };
        _fogMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled, AlbedoTexture = puff, DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
        };
        _fog = new MultiMeshInstance3D
        {
            Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, InstanceCount = Puffs * 2, VisibleInstanceCount = 0,
                Mesh = new PlaneMesh { Size = Vector2.One, Material = _fogMat } },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, CustomAabb = new Aabb(new Vector3(-1e4f, -1e4f, -1e4f), new Vector3(2e4f, 2e4f, 2e4f)),
        };
        AddChild(_fog);
        m.Sim.Landed += Landed;
        m.Sim.Exploded += Boom;
        m.Sim.Doused += Steam;
        m.Sim.Darkened += Smother;
        m.Sim.Woken += Wake;
        m.Sim.RiteDone += Given;
        m.Sim.Spelled += Cast;
        m.Sim.Struck += Bolt;
        m.Sim.Healed += Mended;
        m.Sim.Hexed += Cursed;
        m.Sim.Quaked += Quake;
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
    CpuParticles3D Motes(Vector3 at, Color c, int n, float up, float spread, float size = .06f, float life = 2, Material look = null)
    {
        var ramp = new Gradient();
        ramp.SetColor(0, new Color(c, .95f));
        ramp.SetColor(1, new Color(c, 0));
        var p = new CpuParticles3D
        {
            Position = at, Amount = n, Lifetime = life, OneShot = true, Explosiveness = .85f, Emitting = true,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere, EmissionSphereRadius = spread,
            Direction = Vector3.Up, Spread = 40, InitialVelocityMin = up * .3f, InitialVelocityMax = up, Gravity = new Vector3(0, up > 0 ? .3f : -6, 0),
            DampingMin = .4f, DampingMax = 1, ScaleAmountMin = .6f, ScaleAmountMax = 1.3f, ColorRamp = ramp,
            Mesh = new BoxMesh { Size = Vector3.One * size, Material = look ?? _mote }, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
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

    /// <summary>A shaman mends those about it: a sickly green glimmer round it.</summary>
    void Mended(float x, float y, float z)
    {
        var at = At(x, y + 6, z);
        Flare(at, Hex, 2.5f, 7, 1.2f, Motes(at, Hex, 26, 1.1f, .6f));
    }

    /// <summary>A curse laid on our walls: a green flash where it took.</summary>
    void Cursed(float x, float y, float z) => Flare(At(x, y + 2, z), Hex, 5, 9, 1.5f, Motes(At(x, y, z), Hex, 40, .8f, .8f));

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
            case SpellKind.Rift:
                Flare(ground + Vector3.Up * 2, new Color(.7f, .55f, .4f), 3, 10, 2.5f, Motes(ground, Dust, 110, 1.5f, sp.Radius * VoxelWorld.Vox * .6f, .14f));
                break;
            case SpellKind.Skin:
                Flare(At(x, y + 4, z), new Color(.82f, .76f, .62f), 4, 10, 2, Motes(At(x, y, z), new Color(.75f, .72f, .66f), 50, .7f, sp.Radius * VoxelWorld.Vox * .5f, .08f));
                break;
            case SpellKind.Weather:
                Flare(ground + Vector3.Up * 12, new Color(.7f, .78f, .9f), 5, 40, 2.5f);
                break;
            case SpellKind.BlackWind:
                Spread(ground, sp.Radius, new Color(.12f, .1f, .16f));
                Flare(ground + Vector3.Up * 3, new Color(.36f, .26f, .5f), 4, 14, 1.5f, Motes(ground, new Color(.14f, .12f, .16f), 90, 3.2f, sp.Radius * VoxelWorld.Vox * .5f, .12f));
                break;
            case SpellKind.DeadFog:
                Flare(ground + Vector3.Up * 2, Pale, 3, 14, 2);
                break;
            case SpellKind.Plague:
                Spread(ground, sp.Radius, new Color(.6f, .66f, .2f));
                Flare(ground + Vector3.Up * 2, new Color(.7f, .78f, .25f), 3, 12, 2, Motes(ground, new Color(.7f, .76f, .24f), 60, .9f, sp.Radius * VoxelWorld.Vox * .5f));
                break;
            case SpellKind.Oath:
                Spread(ground, sp.Radius, new Color(.6f, .04f, .05f));
                Flare(ground + Vector3.Up * 2, new Color(.9f, .15f, .12f), 4, 12, 2, Motes(ground, new Color(.75f, .06f, .06f), 70, 1.6f, sp.Radius * VoxelWorld.Vox * .5f));
                break;
            case SpellKind.Recall:
            {
                Flare(ground + Vector3.Up * 1.5f, Pale, 5, 9, 1.2f, Motes(ground, Pale, 40, 1.4f, .5f));
                var (gx, gy, gz) = _m.Sim.Nav.Feet(_m.Sim.Gate);
                Flare(At(gx, gy + 8, gz), Pale, 5, 9, 1.2f, Motes(At(gx, gy, gz), Pale, 40, 1.4f, .5f));
                break;
            }
            case SpellKind.Telekinesis:
                Flare(At(x, y + 2, z), Violet, 6, 9, 1, Motes(At(x, y, z), Violet, 40, 1.2f, .7f));
                break;
            case SpellKind.Meteor:
                // the sky lights up red over the spot as the star comes in
                Flare(ground + Vector3.Up * 14, new Color(1f, .45f, .2f), 6, 30, 2.5f);
                break;
            case SpellKind.Whirlwind:
                Spread(ground, sp.Radius * 2, new Color(.25f, .22f, .2f));
                Flare(ground + Vector3.Up * 3, new Color(.55f, .5f, .62f), 3, 12, 1.5f, Motes(ground, Dust, 60, 2.6f, sp.Radius * VoxelWorld.Vox * .4f, .12f));
                break;
        }
    }

    /// <summary>The earth shaken: dust thrown up all over the circle, a ring of it running out, and the ground's own colour flying.</summary>
    void Quake(float x, float z, float r)
    {
        var w = _m.World;
        var ground = At(x, w.SurfAt(Math.Clamp((int)x, 0, w.SX - 1), Math.Clamp((int)z, 0, w.SZ - 1)), z);
        Spread(ground, r, new Color(.45f, .36f, .26f));
        Spread(ground, r * .6f, new Color(.35f, .28f, .2f));
        float reach = r * VoxelWorld.Vox;
        Pass(Motes(ground, Dust, 160, 2.2f, reach * .7f, .14f, 2.5f), 3.5);
        Pass(Motes(ground + Vector3.Up * .4f, new Color(.25f, .21f, .17f), 60, 1.2f, reach * .5f, .26f, 3.5f), 4.5);
        for (int i = 0; i < 6; i++)
        {
            float a = i * MathF.Tau / 6, d = r * (.3f + .1f * i);
            int gx = Math.Clamp((int)(x + MathF.Cos(a) * d), 0, w.SX - 1), gz = Math.Clamp((int)(z + MathF.Sin(a) * d), 0, w.SZ - 1), gy = w.SurfAt(gx, gz) - 1;
            byte m = w.Get(gx, gy, gz);
            if (m != Mat.Air && m != Mat.Water) _m.Debris.Spawn(m, gx, gy, gz, 8, 2.5f);
        }
    }

    /// <summary>A falling star burns all the way down: fire streaming off it and smoke behind.</summary>
    void Stars(Sim sim)
    {
        Flier star = sim.Fliers.Find(f => f.Star);
        _starLight.Visible = star != null;
        if (star != null) { _starLight.Position = At(star.X, star.Y, star.Z); _starLight.LightEnergy = 9 + 2 * MathF.Sin((float)_t * 40); }
        if (_t - _starAt < .03) return;
        _starAt = _t;
        foreach (var f in sim.Fliers)
        {
            if (!f.Star) continue;
            var at = At(f.X, f.Y, f.Z);
            float big = f.R * VoxelWorld.Vox;
            var fire = Motes(at, new Color(1f, .58f, .2f), 10, 1.4f, big * .9f, .09f, .5f, _ember);
            fire.Spread = 180;
            Pass(fire, 1);
            Pass(Motes(at, new Color(.12f, .1f, .09f), 4, .5f, big * .7f, .3f, 2.2f), 3);
        }
    }

    /// <summary>The whirlwinds: a funnel of dust and grit, narrow at the foot and wide at the top, turning fast on itself.</summary>
    void Whirls(Sim sim)
    {
        var mm = _whirls.Multimesh;
        int n = 0;
        var w = _m.World;
        foreach (var wh in sim.Whirls)
        {
            int gx = Math.Clamp((int)wh.X, 0, w.SX - 1), gz = Math.Clamp((int)wh.Z, 0, w.SZ - 1);
            float foot = w.SurfAt(gx, gz), left = (float)Math.Clamp(wh.Until - sim.Time, 0, 1);
            const int Grains = 220;
            for (int i = 0; i < Grains; i++)
            {
                float h = i / (float)Grains, height = h * 12 * VoxelWorld.PerMetre;
                float rad = (.15f + h * h * .9f + .08f * MathF.Sin(i * 1.7f)) * wh.R * 1.3f * left;
                float a = i * 2.39996f + (float)_t * (9 - h * 4);
                if (n >= mm.InstanceCount) mm.InstanceCount *= 2;
                float s = .6f + h * 1.4f;
                mm.SetInstanceTransform(n++, new Transform3D(Basis.Identity.Scaled(Vector3.One * s), At(wh.X + MathF.Cos(a) * rad, foot + height, wh.Z + MathF.Sin(a) * rad)));
            }
            if (_t - _dustAt > .08)
            {
                _dustAt = _t;
                Pass(Motes(At(wh.X, foot, wh.Z), Dust, 6, 1.8f, wh.R * VoxelWorld.Vox * .4f, .12f, 1.2f), 2);
            }
        }
        mm.VisibleInstanceCount = n;
    }

    /// <summary>A blast, of a spell or of powder: the flash, the cloud of dust and smoke, the ring of it running out, all as big as the blast.</summary>
    void Boom(float x, float y, float z, float r)
    {
        var at = At(x, y, z);
        float k = r / (2.5f * VoxelWorld.PerMetre), reach = r * VoxelWorld.Vox;
        Flare(at + Vector3.Up * .5f, new Color(1f, .78f, .5f), 8 + 10 * k, 12 + 18 * k, 1.2f + .5f * k,
            Motes(at, new Color(.2f, .16f, .13f), (int)(40 + 80 * k), 2.4f + 1.4f * k, reach * .5f, .12f + .05f * k));
        Pass(Motes(at + Vector3.Up * .8f, new Color(.05f, .045f, .04f), (int)(20 + 30 * k), 1.4f, reach * .4f, .22f + .08f * k), 3);
        Spread(at, r * 1.2f, new Color(1f, .7f, .4f));
        // the ball of fire, swelling out and gone
        var fire = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(4f, 3f, 1.8f, .95f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        var ball = new MeshInstance3D { Mesh = new SphereMesh { Radius = 1, Height = 2, RadialSegments = 20, Rings = 10 }, MaterialOverride = fire,
            Position = at + Vector3.Up * reach * .2f, Scale = Vector3.One * .05f, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(ball);
        _fireballs.Add(new Fireball { Node = ball, Mat = fire, At = _t, R = reach * 1.05f });
        // sparks flung every way, falling burning
        var sparks = Motes(at + Vector3.Up * .3f, new Color(1f, .62f, .22f), (int)(50 + 90 * k), 9 + 4 * k, reach * .2f, .045f, 1.4f, _ember);
        sparks.Spread = 180; sparks.Gravity = new Vector3(0, -9.8f, 0); sparks.DampingMin = .1f; sparks.DampingMax = .4f;
        Pass(sparks, 2);
        // and a column of smoke going up after, slow and long
        Pass(Motes(at + Vector3.Up * reach * .4f, new Color(.1f, .09f, .08f), (int)(14 + 22 * k), .9f + .4f * k, reach * .3f, .35f + .15f * k, 4.5f), 6);
    }

    /// <summary>The fuses burning down to powder: sparks spitting where each charge is.</summary>
    void Fuses(Sim sim)
    {
        var mm = _sparks.Multimesh;
        int n = 0;
        float t = (float)_t;
        foreach (var c in sim.Charges)
        {
            if (c.BlowAt <= sim.Time) continue;
            for (int j = 0; j < (c.Planted ? 6 : 3); j++)
            {
                if (n >= mm.InstanceCount) mm.InstanceCount *= 2;
                float a = t * 23 + j * 2.4f + c.X, h = (t * 3.1f + j * .37f) % 1;
                mm.SetInstanceTransform(n++, new Transform3D(Basis.Identity, At(c.X + MathF.Cos(a) * (1 + h * 2), c.Y + 3 + h * 5, c.Z + MathF.Sin(a) * (1 + h * 2))));
            }
        }
        mm.VisibleInstanceCount = n;
    }

    /// <summary>A witch puts fires out about her: a puff of grey steam where she stands.</summary>
    void Steam(float x, float y, float z) => Pass(Motes(At(x, y + 8, z), new Color(.62f, .64f, .66f), 30, .9f, .9f, .16f), 2.5);

    /// <summary>A witch puts our torches out: a dark breath going out from her.</summary>
    void Smother(float x, float y, float z)
    {
        var at = At(x, y + 4, z);
        Spread(at - Vector3.Up * .3f, 16 * VoxelWorld.PerMetre * .4f, new Color(.08f, .07f, .12f));
        Pass(Motes(at, new Color(.12f, .1f, .16f), 40, 1.2f, .7f, .14f), 2.5);
    }

    /// <summary>A necromancer gets the fallen up: a sickly green glow and motes rising about it.</summary>
    void Wake(float x, float y, float z)
    {
        var at = At(x, y + 6, z);
        Flare(at, new Color(.5f, .95f, .6f), 5, 12, 2.5f, Motes(at, new Color(.2f, .5f, .28f), 36, 1.4f, 1.5f));
    }

    /// <summary>A piece of the world comes down: dust where it lands, and chips of it flying, more for a bigger one.</summary>
    void Landed(float x, float y, float z, int n, float speed)
    {
        if (n < 24 || _t - _dustAt < .04) return;
        _dustAt = _t;
        var at = At(x, y, z);
        float r = MathF.Cbrt(n) * VoxelWorld.Vox * .5f;
        Pass(Motes(at, new Color(.22f, .19f, .15f), Math.Clamp(n / 24, 6, 50), 1 + Math.Min(2, speed / 120), r, .08f + Math.Min(.1f, n / 9000f)), 2.5);
        // heavy and hard: chips of what it struck thrown up
        float mps = speed / VoxelWorld.PerMetre;
        if (n < 60 || mps < 4) return;
        var w = _m.World;
        int gx = Math.Clamp((int)x, 0, w.SX - 1), gz = Math.Clamp((int)z, 0, w.SZ - 1), gy = Math.Clamp((int)(y - r / VoxelWorld.Vox), 1, w.SY - 1);
        for (int k = 0; k < 4 && w.Get(gx, gy, gz) == Mat.Air && gy > 1; k++) gy--;
        byte struck = w.Get(gx, gy, gz);
        if (struck != Mat.Air && struck != Mat.Water) _m.Debris.Spawn(struck, gx, gy, gz, Math.Clamp(n / 40, 3, 14), Math.Min(4.5f, 1.2f + mps * .25f));
    }

    /// <summary>
    /// A great piece flying or a ball or log rolling leaves dust behind it as it goes, a puff now and then — no more of
    /// them in all than the eye can take.
    /// </summary>
    void Trails(Sim sim, float dt)
    {
        _trailBudget = Math.Min(30, _trailBudget + dt * 30);
        foreach (var f in sim.Fliers)
        {
            if (_trailBudget < 1) break;
            if (f.Unit != null || f.Voxels.Count < 60 || f.Held) continue;
            float hs = MathF.Sqrt(f.VX * f.VX + f.VZ * f.VZ) / VoxelWorld.PerMetre, s = MathF.Sqrt(f.VX * f.VX + f.VY * f.VY + f.VZ * f.VZ) / VoxelWorld.PerMetre;
            bool rolling = f.Rolling && hs > 1.2f, flying = !f.Rolling && !f.Tipping && s > 5;
            if (!rolling && !flying) continue;
            if (_trailAt.TryGetValue(f.Id, out double last) && _t - last < (rolling ? .09 : .06)) continue;
            _trailAt[f.Id] = _t;
            _trailBudget -= 1;
            float big = MathF.Cbrt(f.Voxels.Count) * VoxelWorld.Vox;
            var at = At(f.X, f.Rolling ? f.Y - f.RollR : f.Y, f.Z);
            Pass(Motes(at, rolling ? new Color(.36f, .31f, .25f) : new Color(.3f, .27f, .23f), rolling ? 5 : 3, rolling ? .6f : .2f, big * .3f, .1f + big * .03f, 1.6f), 2);
        }
        if (_trailAt.Count > 256) _trailAt.Clear();
    }

    /// <summary>Something that goes by itself after a while (dust, smoke).</summary>
    void Pass(Node3D node, double seconds) => _passing.Add((node, _t + seconds));

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
        for (int i = _passing.Count - 1; i >= 0; i--)
            if (_t > _passing[i].until) { _passing[i].node.QueueFree(); _passing.RemoveAt(i); }
        for (int i = _fireballs.Count - 1; i >= 0; i--)
        {
            var b = _fireballs[i];
            float age = (float)(_t - b.At), k = Math.Clamp(age / .7f, 0, 1);
            // out fast, then slower; white hot, then orange, then the brown of smoke, thinning to nothing
            b.Node.Scale = Vector3.One * Math.Max(.05f, b.R * (.25f + .75f * (1 - (1 - k) * (1 - k) * (1 - k))));
            var c = k < .35f ? new Color(4f, 3f, 1.8f).Lerp(new Color(2.6f, 1.1f, .3f), k / .35f) : new Color(2.6f, 1.1f, .3f).Lerp(new Color(.22f, .16f, .11f), (k - .35f) / .65f);
            b.Mat.AlbedoColor = new Color(c.R, c.G, c.B, .95f * (1 - k * k));
            if (k < 1) continue;
            b.Node.QueueFree();
            _fireballs.RemoveAt(i);
        }
        Bogs(sim);
        Curses(sim);
        Drops(sim);
        Chains(sim);
        Marks(sim);
        Pieces(sim);
        Trails(sim, (float)delta);
        Grip(sim);
        Skins(sim);
        Fogs(sim);
        Afflicted(sim);
        Fuses(sim);
        Stars(sim);
        Whirls(sim);
    }

    /// <summary>Voxels of the pieces in the air drawn this frame (the self-test asks).</summary>
    public int PiecesDrawn => _bits.Multimesh.VisibleInstanceCount;

    /// <summary>Every piece in the air, every voxel of it a box of its colour, turned as the piece tumbles.</summary>
    void Pieces(Sim sim)
    {
        var mm = _bits.Multimesh;
        int total = 0;
        // a tuft or a flower carried off is drawn as nothing: it is blades, not a cube, and is gone where it comes down
        foreach (var f in sim.Fliers) foreach (var (_, _, _, m) in f.Voxels) if (!Mat.IsSprig(m)) total++;
        if (total == 0) { if (mm.VisibleInstanceCount != 0) mm.VisibleInstanceCount = 0; return; }
        if (total > mm.InstanceCount)
        {
            mm.InstanceCount = Math.Max(total, Math.Max(2048, mm.InstanceCount * 2));
            _bitBuf = new float[mm.InstanceCount * 16];
        }
        float v = VoxelWorld.Vox;
        int o = 0;
        foreach (var f in sim.Fliers)
        {
            if (f.Voxels.Count == 0) continue;
            var axis = new Vector3(f.AX, f.AY, f.AZ);
            var b = axis.LengthSquared() > 1e-4f ? new Basis(axis.Normalized(), f.Turn) : Basis.Identity;
            var c = At(f.X, f.Y, f.Z);
            foreach (var (dx, dy, dz, m) in f.Voxels)
            {
                if (Mat.IsSprig(m)) continue;
                var p = c + b * new Vector3(dx * v, dy * v, dz * v);
                // each voxel a little lighter or darker, as in the world
                uint h = (uint)(dx * 73856093 ^ dy * 19349663 ^ dz * 83492791 ^ f.Id * 2654435761);
                float shade = 1 + ((h >> 8 & 255) / 255f - .5f) * Mat.Variance[m] * 1.2f;
                _bitBuf[o] = b.X.X; _bitBuf[o + 1] = b.Y.X; _bitBuf[o + 2] = b.Z.X; _bitBuf[o + 3] = p.X;
                _bitBuf[o + 4] = b.X.Y; _bitBuf[o + 5] = b.Y.Y; _bitBuf[o + 6] = b.Z.Y; _bitBuf[o + 7] = p.Y;
                _bitBuf[o + 8] = b.X.Z; _bitBuf[o + 9] = b.Y.Z; _bitBuf[o + 10] = b.Z.Z; _bitBuf[o + 11] = p.Z;
                _bitBuf[o + 12] = Mat.Lin[m * 3] * shade; _bitBuf[o + 13] = Mat.Lin[m * 3 + 1] * shade; _bitBuf[o + 14] = Mat.Lin[m * 3 + 2] * shade;
                _bitBuf[o + 15] = 1;
                o += 16;
            }
        }
        RenderingServer.MultimeshSetBuffer(mm.GetRid(), _bitBuf);
        mm.VisibleInstanceCount = total;
    }

    /// <summary>The hand of telekinesis: a violet glow about what it holds, and a ring on the ground under it, to aim by.</summary>
    void Grip(Sim sim)
    {
        var f = sim.HeldNow;
        _grip.Visible = _gripRing.Visible = f != null;
        if (f == null) return;
        var w = _m.World;
        _grip.Position = At(f.X, f.Y, f.Z);
        _grip.LightEnergy = 2.5f + .8f * MathF.Sin((float)_t * 9);
        int gx = Math.Clamp((int)f.X, 0, w.SX - 1), gz = Math.Clamp((int)f.Z, 0, w.SZ - 1);
        float r = (f.R + 2) * VoxelWorld.Vox;
        _gripRing.Position = At(f.X, w.SurfAt(gx, gz), f.Z) + Vector3.Up * .1f;
        _gripRing.Scale = new Vector3(r, r * .3f, r);
    }

    /// <summary>Each stone skin a faint glimmer over our walls while it holds.</summary>
    void Skins(Sim sim)
    {
        var stone = new Color(.86f, .8f, .66f);
        foreach (var s in sim.Skins)
        {
            if (_skins.ContainsKey(s)) continue;
            var mat = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(stone, 0), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
            float r = s.R * VoxelWorld.Vox * 1.1f;
            var node = new MeshInstance3D { Mesh = new SphereMesh { Radius = r, Height = r * 2, RadialSegments = 20, Rings = 10 }, MaterialOverride = mat, Position = At(s.X, s.Y, s.Z), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            AddChild(node);
            _skins[s] = (node, mat);
        }
        List<Patch> gone = null;
        foreach (var (s, (node, mat)) in _skins)
        {
            double left = s.Until - sim.Time;
            if (!sim.Skins.Contains(s) || left <= 0) { node.QueueFree(); (gone ??= new()).Add(s); continue; }
            mat.AlbedoColor = new Color(stone, (float)Math.Clamp(left / 3, 0, 1) * (.02f + .01f * MathF.Sin((float)_t * 2)));
        }
        if (gone != null) foreach (var s in gone) _skins.Remove(s);
    }

    /// <summary>Puffs of the fog of the dead to each circle of it.</summary>
    const int Puffs = 28;

    /// <summary>
    /// Each fog of the dead: soft grey-green puffs lying low over its circle, drifting and thinning and thickening as they
    /// go, coming on as it falls and fading out as it lifts.
    /// </summary>
    void Fogs(Sim sim)
    {
        var w = _m.World;
        var mm = _fog.Multimesh;
        int need = sim.DeadFogs.Count * Puffs;
        if (need > mm.InstanceCount) mm.InstanceCount = need + Puffs * 2;
        int n = 0;
        float t = (float)_t;
        foreach (var f in sim.DeadFogs)
        {
            double left = f.Until - sim.Time, age = 45 - left;
            float fade = (float)Math.Clamp(Math.Min(age / 2, left / 3), 0, 1), r = f.R * VoxelWorld.Vox;
            int seed = (int)(f.X * 7 + f.Z * 13);
            for (int i = 0; i < Puffs; i++)
            {
                // strewn through the circle, more of them toward its middle
                float a = (seed + i * 2.39996f) % MathF.Tau, d = r * MathF.Sqrt((i + .5f) / Puffs) * .9f;
                float x = f.X * VoxelWorld.Vox + MathF.Cos(a) * d + MathF.Sin(t * .23f + i) * .5f, z = f.Z * VoxelWorld.Vox + MathF.Sin(a) * d + MathF.Cos(t * .19f + i * 1.3f) * .5f;
                int vx = Math.Clamp((int)(x / VoxelWorld.Vox), 0, w.SX - 1), vz = Math.Clamp((int)(z / VoxelWorld.Vox), 0, w.SZ - 1);
                float size = 2.2f + (i * 37 % 11) * .16f, h = .25f + (i % 4) * .22f;
                var at = new Vector3(w.WX(x / VoxelWorld.Vox), w.WY(w.SurfAt(vx, vz)) + h, w.WZ(z / VoxelWorld.Vox));
                var basis = new Basis(Vector3.Up, t * .04f * (i % 2 == 0 ? 1 : -1) + i).Scaled(new Vector3(size, 1, size));
                mm.SetInstanceTransform(n, new Transform3D(basis, at));
                mm.SetInstanceColor(n, new Color(.34f, .44f, .38f, fade * (.1f + .05f * MathF.Sin(t * .5f + i * 1.7f))));
                n++;
            }
        }
        mm.VisibleInstanceCount = n;
    }

    /// <summary>Sickly motes circling over the heads of the plague-struck; a drop of blood over each of ours under the oath.</summary>
    void Afflicted(Sim sim)
    {
        var sick = _sick.Multimesh; var sworn = _sworn.Multimesh;
        int n = 0, k = 0;
        float t = (float)_t;
        foreach (var u in sim.Units)
        {
            if (u.Dead || u.Inside) continue;
            if (u.PlagueUntil > sim.Time)
                for (int j = 0; j < 3; j++)
                {
                    if (n >= sick.InstanceCount) sick.InstanceCount *= 2;
                    float a = t * 2.2f + j * 2.1f + u.Id;
                    sick.SetInstanceTransform(n++, new Transform3D(Basis.Identity, At(u.X + MathF.Cos(a) * 2.2f, u.Y + u.T.BodyH + 2 + MathF.Sin(a * 1.7f), u.Z + MathF.Sin(a) * 2.2f)));
                }
            if (u.OathUntil > sim.Time)
            {
                if (k >= sworn.InstanceCount) sworn.InstanceCount *= 2;
                sworn.SetInstanceTransform(k++, new Transform3D(Basis.Identity, At(u.X, u.Y + u.T.BodyH + 3 + MathF.Sin(t * 3 + u.Id) * .6f, u.Z)));
            }
        }
        sick.VisibleInstanceCount = n;
        sworn.VisibleInstanceCount = k;
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

    /// <summary>Each cursed patch of our walls under a faint green haze while the curse lasts.</summary>
    void Curses(Sim sim)
    {
        foreach (var c in sim.Curses)
        {
            if (_curses.ContainsKey(c)) continue;
            var mat = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(Hex, 0), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
            float r = c.R * VoxelWorld.Vox * 1.3f;
            var node = new MeshInstance3D { Mesh = new SphereMesh { Radius = r, Height = r * 2, RadialSegments = 20, Rings = 10 }, MaterialOverride = mat, Position = At(c.X, c.Y, c.Z), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            AddChild(node);
            _curses[c] = (node, mat);
        }
        List<Curse> gone = null;
        foreach (var (c, (node, mat)) in _curses)
        {
            double left = c.Until - sim.Time;
            if (!sim.Curses.Contains(c) || left <= 0) { node.QueueFree(); (gone ??= new()).Add(c); continue; }
            mat.AlbedoColor = new Color(Hex, (float)Math.Clamp(left / 3, 0, 1) * (.16f + .05f * MathF.Sin((float)_t * 3)));
        }
        if (gone != null) foreach (var c in gone) _curses.Remove(c);
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
        _m.Sim.Healed -= Mended;
        _m.Sim.Hexed -= Cursed;
        _m.Sim.Landed -= Landed;
        _m.Sim.Exploded -= Boom;
        _m.Sim.Doused -= Steam;
        _m.Sim.Darkened -= Smother;
        _m.Sim.Woken -= Wake;
        _m.Sim.Quaked -= Quake;
    }
}
