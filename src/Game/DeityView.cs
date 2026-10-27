using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// Draws the gods (Sim.Deities.cs): each a figure of the valley sculpted as the others are and drawn a quarter of the
/// valley high, in cells of its own body, casting its shadow across the land.
/// The Archangel comes down out of the sky in a pillar of light and goes up into it again; each leg reaches down to where
/// its foot stands or goes, the knee forward; the sword arm reaches to where the sword is, the trunk bending down for a
/// sweep and turning with it, the sword raised to the sky and brought down for the pillar of heaven; its wings, drawn on
/// their own, beat as it comes down and as it flies and when it blows the horde away, and lie folded down its back as it
/// walks; a halo of light over its head; the pillar of heaven a beam of light where it falls.
/// The Great Toad comes up out of its bog and sits with its head up; it crouches before a hop, stretches out its hind
/// legs in the air and splays its forelegs coming down; its tongue shoots out of its mouth to where it catches.
/// Where a god shakes the ground the dust goes up and the view shakes.
/// </summary>
public partial class DeityView : Node3D
{
    sealed class Shown
    {
        public Deity D;
        public BodyPlan Plan;
        public Figure Fig;
        public readonly MeshInstance3D[] Bones = new MeshInstance3D[Figure.Bones], Shades = new MeshInstance3D[Figure.Bones];
        public MeshInstance3D[] Dress = Array.Empty<MeshInstance3D>();
        public MeshInstance3D WingL, WingR, Halo, Beam, Tongue, Tip;
        public OmniLight3D Eyes;
        /// <summary>The figure's voxels to the world's, and the trunk's pose eased toward what is asked of it.</summary>
        public float Scale, Lean, Twist, HeadYaw, HeadPitch, Swing, Flap, Spread, Pitch, Crouch, Hover = 1;
        public Transform3D Root, Head;
    }

    Main _m;
    VoxelWorld _w;
    readonly Dictionary<int, Shown> _shown = new();
    readonly Transform3D[] _xf = new Transform3D[Figure.Bones];
    /// <summary>The pillars of heaven come down, fading: the beam and how much of it is left.</summary>
    readonly List<(MeshInstance3D beam, float left)> _beams = new();
    static readonly Mesh WingMeshL, WingMeshR, HaloMesh;
    static readonly StandardMaterial3D Light = new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled, AlbedoColor = new Color(1f, .86f, .55f, .55f), NoDepthTest = false,
    };

    static DeityView()
    {
        var wing = Bodies.Wing();
        WingMeshL = Figure.MeshOf(wing);
        WingMeshR = Figure.MeshOf(Mirror(wing));
        HaloMesh = Figure.MeshOf(Bodies.Halo());
    }

    public void Init(Main m)
    {
        _m = m;
        _w = m.World;
        m.Sim.GodShook += Shook;
        m.Sim.PillarFell += (d, x, y, z) => Beam(x, y, z, d.Act == DeityAct.Rising ? 0 : 1.4f);
        m.Sim.VenomSplashed += (x, y, z) => _m.Debris.Spray(Mat.Brew, new Vector3(_w.WX(x), _w.WY(y), _w.WZ(z)), Vector3.Up, 60, 5, 1.2f, 3, life: 1.4f);
        // (sculpted ahead on a worker thread: a god's body is many times a man's)
        System.Threading.Tasks.Task.Run(() => { Bodies.God(DeityKind.Archangel); Bodies.God(DeityKind.Toad); });
    }

    /// <summary>Whether a god is drawn now, and how tall (metres, its figure's top as drawn).</summary>
    public bool Drawn(Deity d, out float tall)
    {
        tall = 0;
        if (!_shown.TryGetValue(d.Id, out var s) || s.Bones[(int)Bone.Torso] == null) return false;
        tall = s.Plan.Top * s.Scale / VoxelWorld.PerMetre;
        return true;
    }

    /// <summary>A god's figure this frame, for the eyes of the one in it: where its head is and how it is turned.</summary>
    public bool TryHead(Deity d, out Transform3D head)
    {
        if (_shown.TryGetValue(d.Id, out var s)) { head = s.Head; return true; }
        head = Transform3D.Identity;
        return false;
    }

    /// <summary>The one the player is in: its head and trunk not drawn (its eyes are the view), its arms and legs seen.</summary>
    public int HiddenId = -1;
    /// <summary>The wings of a god as drawn now, for the self-test: drawn, and how far open (0 folded, 1 spread).</summary>
    public (bool drawn, float spread) WingsOf(Deity d) => _shown.TryGetValue(d.Id, out var s) && s.WingL != null ? (s.WingL.Visible, s.Spread) : (false, 0);

    void Shook(Deity d, float x, float y, float z, float k)
    {
        var at = new Vector3(_w.WX(x), _w.WY(y), _w.WZ(z));
        byte dust = d.T.Hops ? Mat.Soil : Mat.Soil;
        _m.Debris.Spray(dust, at, Vector3.Up, (int)(30 * k), 3 + 2 * k, 1.1f, 2.5f + k, life: 1.6f);
        _m.Debris.Spray(d.T.Hops ? Mat.Moss : Mat.Rubble, at, Vector3.Up, (int)(12 * k), 5 + 3 * k, .9f, 3 + k);
        _m.Spectacle?.Felt(x, z, Math.Min(1.2f, .25f * k), flash: false);
    }

    /// <summary>A beam of light out of the sky at a spot (voxels), fading over so many seconds (0: held, for one coming down).</summary>
    void Beam(float x, float y, float z, float fade)
    {
        if (fade <= 0) return;
        var b = BeamNode();
        b.Position = new Vector3(_w.WX(x), _w.WY(y) + 120, _w.WZ(z));
        _beams.Add((b, fade));
    }

    MeshInstance3D BeamNode()
    {
        var b = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 4.5f, BottomRadius = 3.2f, Height = 240, RadialSegments = 16, Rings = 1 },
            MaterialOverride = (Material)Light.Duplicate(), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(b);
        return b;
    }

    public override void _Process(double delta)
    {
        using var prof = Prof.Time("Gods");
        var sim = _m.Sim;
        float dt = (float)delta;
        foreach (var d in sim.Gods)
        {
            if (d.Gone) continue;
            if (!_shown.TryGetValue(d.Id, out var s)) { s = Make(d); if (s == null) continue; _shown[d.Id] = s; }
            if (d.T.Hops) PoseToad(s, dt); else Pose(s, dt);
        }
        for (int i = _beams.Count - 1; i >= 0; i--)
        {
            var (b, left) = _beams[i];
            left -= dt;
            if (left <= 0) { b.QueueFree(); _beams.RemoveAt(i); continue; }
            ((StandardMaterial3D)b.MaterialOverride).AlbedoColor = new Color(1f, .86f, .55f, .6f * Math.Min(1, left));
            _beams[i] = (b, left);
        }
        // the gone: let go
        List<int> gone = null;
        foreach (var (id, s) in _shown)
            if (s.D.Gone || !sim.Gods.Contains(s.D)) (gone ??= new List<int>()).Add(id);
        if (gone != null)
            foreach (int id in gone)
            {
                var s = _shown[id];
                foreach (var n in s.Bones) n?.QueueFree();
                foreach (var n in s.Shades) n?.QueueFree();
                foreach (var n in s.Dress) n?.QueueFree();
                foreach (var n in new Node3D[] { s.WingL, s.WingR, s.Halo, s.Beam, s.Tongue, s.Tip, s.Eyes }) n?.QueueFree();
                _shown.Remove(id);
            }
    }

    Shown Make(Deity d)
    {
        var plan = Bodies.God(d.T.Kind);
        var fig = Figure.For(plan);
        var s = new Shown { D = d, Plan = plan, Fig = fig, Scale = d.T.Height / Math.Max(1, plan.Top) };
        for (int b = 0; b < Figure.Bones; b++)
        {
            if (fig.BoneNear[b] == null) continue;
            s.Bones[b] = new MeshInstance3D { Mesh = fig.BoneNear[b], MaterialOverride = Figure.Material(BodyPlan.FineSub), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            AddChild(s.Bones[b]);
            if (fig.BoneMid[b] != null)
            {
                s.Shades[b] = new MeshInstance3D { Mesh = fig.BoneMid[b], MaterialOverride = Figure.Material(BodyPlan.Sub), CastShadow = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly };
                AddChild(s.Shades[b]);
            }
        }
        s.Dress = new MeshInstance3D[fig.DressNear.Length];
        for (int i = 0; i < s.Dress.Length; i++)
        {
            if (fig.DressNear[i] == null) continue;
            s.Dress[i] = new MeshInstance3D { Mesh = fig.DressNear[i], MaterialOverride = Figure.Material(plan.Dress[i].Sub), CastShadow = GeometryInstance3D.ShadowCastingSetting.On };
            AddChild(s.Dress[i]);
        }
        if (d.T.Winged)
        {
            s.WingL = Part(WingMeshL, BodyPlan.Sub, true);
            s.WingR = Part(WingMeshR, BodyPlan.Sub, true);
            s.Halo = Part(HaloMesh, BodyPlan.Sub, false);
            s.Beam = BeamNode();
            s.Beam.Visible = false;
        }
        if (d.T.Hops)
        {
            s.Tongue = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = .55f, BottomRadius = .75f, Height = 1, RadialSegments = 10, Rings = 1 },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(.78f, .36f, .4f), Roughness = .4f } };
            s.Tip = new MeshInstance3D { Mesh = new SphereMesh { Radius = 1.4f, Height = 2.4f, RadialSegments = 12, Rings = 6 },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(.82f, .42f, .46f), Roughness = .3f } };
            foreach (var n in new[] { s.Tongue, s.Tip }) { n.Visible = false; AddChild(n); }
        }
        var glow = d.T.Winged ? new Color(1f, .82f, .45f) : d.T.Hops ? new Color(.9f, .8f, .2f) : new Color(1f, .62f, .25f);
        s.Eyes = new OmniLight3D { LightColor = glow, LightEnergy = 3.5f, OmniRange = 30, ShadowEnabled = false };
        AddChild(s.Eyes);
        return s;
    }

    MeshInstance3D Part(Mesh mesh, int grid, bool shadow)
    {
        var n = new MeshInstance3D { Mesh = mesh, MaterialOverride = Figure.Material(grid), CastShadow = shadow ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(n);
        return n;
    }

    /// <summary>A part's cells turned about across x (the other wing).</summary>
    static PartGrid Mirror(PartGrid g)
    {
        var m = new PartGrid { X0 = -(g.X0 + g.SX), Y0 = g.Y0, Z0 = g.Z0, SX = g.SX, SY = g.SY, SZ = g.SZ, M = new byte[g.M.Length], T = new byte[g.T.Length], Count = g.Count };
        for (int y = 0; y < g.SY; y++)
            for (int z = 0; z < g.SZ; z++)
                for (int x = 0; x < g.SX; x++)
                {
                    int from = (y * g.SZ + z) * g.SX + x, to = (y * g.SZ + z) * g.SX + (g.SX - 1 - x);
                    m.M[to] = g.M[from]; m.T[to] = g.T[from];
                }
        return m;
    }

    // ------------------------------------------------------------ the pose of one on two legs

    void Pose(Shown s, float dt)
    {
        var d = s.D;
        var f = s.Fig;
        var sim = _m.Sim;
        float t = d.Into(sim.Time), k = Math.Min(1, dt * 5);
        bool going = d.Act is DeityAct.Rising or DeityAct.Sinking, sky = d.T.Arrives == Arrival.Sky;
        bool under = going && !sky, aloft = going && sky || d.Act == DeityAct.Leap && t > Sim.LeapCrouch;
        // (set again below for one that hangs in the air)
        // the root: between its feet on the ground; sunk under it, or high in the sky, while it comes and goes
        float off = going ? (sky ? (1 - d.Up) * 110 * VoxelWorld.PerMetre : -(1 - d.Up) * d.T.Height * 1.02f) : 0;
        // the archangel hangs in the air over the ground, lower as it strikes, rising and falling a little with its wings
        if (d.T.Winged && !going)
        {
            float low = d.Act is DeityAct.Sweep or DeityAct.Slam or DeityAct.Gust ? .35f : d.Act == DeityAct.Leap ? 0 : 1;
            s.Hover += (low - s.Hover) * Math.Min(1, dt * 2);
            off += s.Hover * (6 + .6f * MathF.Sin((float)sim.Time * 1.7f)) * VoxelWorld.PerMetre;
            aloft = true;
        }
        var root = new Transform3D(new Basis(Vector3.Up, MathF.PI / 2 - d.Facing).Scaled(Vector3.One * VoxelWorld.Vox * s.Scale),
            new Vector3(_w.WX(d.X), _w.WY(d.Y + off), _w.WZ(d.Z)));
        // walking, it sinks a little at the knees and rocks from foot to foot
        if (d.Lifted >= 0 && !going)
        {
            float p = Math.Clamp((float)((sim.Time - d.StepFrom) / d.StepFor), 0, 1);
            root.Origin += Vector3.Down * MathF.Sin(MathF.PI * p) * f.Thigh * .06f * VoxelWorld.Vox * s.Scale;
            s.Swing += dt * MathF.PI / (float)d.StepFor * (d.Lifted == 0 ? 1 : -1);
        }
        // crouched before a flight
        if (d.Act == DeityAct.Leap && t < Sim.LeapCrouch) root.Origin += Vector3.Down * Smooth(t / Sim.LeapCrouch) * f.Thigh * .3f * VoxelWorld.Vox * s.Scale;
        s.Root = root;
        var toFig = root.AffineInverse();
        Vector3 Fig(float x, float y, float z) => toFig * new Vector3(_w.WX(x), _w.WY(y), _w.WZ(z));

        // what the trunk is asked to do by the act
        var hand = Fig(d.HandX, d.HandY, d.HandZ);
        float handYaw = MathF.Atan2(hand.X, MathF.Max(1, hand.Z));
        float lean = .08f, twist = 0, headPitch = .15f, headYaw = 0;
        // (the archangel's sword is in its right hand, whichever way it cuts)
        int arm = d.T.Winged || d.Side >= 0 ? 0 : 1;
        bool reach = false, spread = false;
        switch (d.Act)
        {
            case DeityAct.Rising: lean = sky ? -.05f : .35f * (1 - d.Up); headPitch = sky ? .3f : -.2f; spread = sky; break;
            case DeityAct.Sinking: lean = sky ? -.1f : .2f; headPitch = -.35f; spread = sky; break;
            case DeityAct.Walking: lean = .14f; break;
            case DeityAct.Sweep:
                reach = true;
                // (the archangel cuts upright, its sword long enough; the colossus bends to the ground)
                float deep = d.T.Winged ? .15f : .95f;
                lean = t < Sim.SweepFrom ? deep * (.35f + .65f * Smooth(t / Sim.SweepFrom)) : t <= Sim.SweepTo ? deep : deep * (1 - Smooth((t - Sim.SweepTo) / (Sim.SweepDone - Sim.SweepTo)));
                twist = Math.Clamp(handYaw * .85f, -1.35f, 1.35f);
                headYaw = handYaw * .3f; headPitch = .45f;
                break;
            case DeityAct.Slam:
                reach = true; arm = 0;
                float bow = d.T.Winged ? .2f : .75f;
                lean = t < 1.1f ? -.1f * Smooth(t / 1.1f) : t < Sim.SlamHit ? bow : bow * (1 - Smooth((t - Sim.SlamHit) / (Sim.SlamDone - Sim.SlamHit)));
                twist = Math.Clamp(handYaw * .5f, -.8f, .8f); headPitch = .5f;
                break;
            case DeityAct.Lift:
                reach = true; arm = 0;
                lean = t < Sim.LiftGrab ? 1f * Smooth(t / Sim.LiftGrab) : 1f - .9f * Smooth((t - Sim.LiftGrab) / (Sim.LiftDone - Sim.LiftGrab));
                twist = Math.Clamp(handYaw * .6f, -.9f, .9f); headPitch = .5f;
                break;
            case DeityAct.Throw:
                reach = true; arm = 0;
                lean = t < Sim.ThrowLoose ? -.2f * Smooth(t / Sim.ThrowLoose) : .45f * Smooth((t - Sim.ThrowLoose) / (Sim.ThrowDone - Sim.ThrowLoose));
                twist = t < Sim.ThrowLoose ? -.5f * Smooth(t / Sim.ThrowLoose) : .3f;
                break;
            case DeityAct.Roar: lean = -.25f; headPitch = -.6f; break;
            case DeityAct.Gust: lean = t < Sim.GustAt ? -.2f : .3f; spread = true; break;
            case DeityAct.Leap: lean = t < Sim.LeapCrouch ? .4f : .25f; spread = true; headPitch = .3f; break;
            default:
                if (d.Held != null) { reach = true; arm = 0; }
                break;
        }
        s.Lean += (lean - s.Lean) * k; s.Twist += (twist - s.Twist) * k;
        s.HeadYaw += (headYaw - s.HeadYaw) * k; s.HeadPitch += (headPitch - s.HeadPitch) * k;

        // the trunk and the head
        var waist = f.Pivot[(int)Bone.Torso];
        var torso = RotAt(waist, new Basis(Vector3.Up, s.Twist) * new Basis(Vector3.Right, s.Lean));
        _xf[(int)Bone.Torso] = torso;
        var neck = f.Pivot[(int)Bone.Head];
        _xf[(int)Bone.Head] = torso * RotAt(neck, new Basis(Vector3.Up, s.HeadYaw) * new Basis(Vector3.Right, s.HeadPitch));
        var fwd = torso.Basis * Vector3.Back;

        // the arms: the striking one reaches to the hand (the sword raised to the sky before the pillar comes down),
        // the other hangs and swings, or both reach as the act asks
        for (int a = 0; a < 2; a++)
        {
            Bone upper = a == 0 ? Bone.UpperR : Bone.UpperL, lower = a == 0 ? Bone.LowerR : Bone.LowerL;
            var shoulder = f.Pivot[(int)upper];
            var now = torso * shoulder;
            float len = f.UpperArm + f.Forearm, side = a == 0 ? -1 : 1;
            Vector3 target;
            if (d.T.Winged && a == 0)
            {
                // the sword arm, the sword going on from the fist the way the forearm goes: held up before the chest, the
                // blade to the sky; raised straight up before the pillar; in a cut, the arm and the blade one line out to
                // where the blade's point is
                bool cut = d.Act == DeityAct.Sweep || d.Act == DeityAct.Slam && t >= Sim.SlamHit - .1f;
                if (cut) target = now + (hand - now).Normalized() * len * .98f;
                else if (d.Act == DeityAct.Slam || d.Act == DeityAct.Roar) target = now + new Vector3(0, len * .97f, len * .1f);
                else target = now + new Vector3(len * .08f, len * .28f, len * .6f);
            }
            else if (reach && a == arm) target = hand;
            else if (going && !sky)
                // over the rim of the pit, clawing at the ground
                target = now + new Vector3(side * len * .75f, len * (d.Act == DeityAct.Rising ? .5f - .6f * d.Up : .55f), len * .35f);
            else if (d.Act == DeityAct.Roar || spread)
                target = now + new Vector3(side * len * .8f, len * (d.T.Winged && d.Act == DeityAct.Roar ? .55f : .2f), len * .3f);
            else
            {
                // hanging, out a little from its sides, swinging against the step
                float sw = MathF.Sin(s.Swing) * (a == 0 ? 1 : -1) * (d.Act == DeityAct.Walking ? .3f : 0);
                target = now + new Vector3(side * len * .18f, -len * .92f, len * (.08f + sw));
            }
            Limb(f, upper, lower, shoulder, now, target, f.UpperArm, f.Forearm, -fwd + Vector3.Right * side * .6f, fwd);
        }

        // the legs: each reaches down to where its foot is, the knee forward; under the ground or in the sky, hanging
        for (int l = 0; l < 2; l++)
        {
            Bone thigh = l == 0 ? Bone.ThighR : Bone.ThighL, shin = l == 0 ? Bone.ShinR : Bone.ShinL;
            var hip = f.Pivot[(int)thigh];
            // (the sim's first foot is on the right: −x of the figure)
            Vector3 target;
            if (under) target = hip + Vector3.Down * (f.Thigh + f.Shin);
            // in the air the legs hang, one nearly straight, the other drawn up and back at the knee
            else if (aloft) target = hip + (l == 0 ? new Vector3(0, -(f.Thigh + f.Shin) * .95f, (f.Thigh + f.Shin) * .12f) : new Vector3(0, -(f.Thigh + f.Shin) * .62f, -(f.Thigh + f.Shin) * .42f));
            else
            {
                target = Fig(d.FootX[l], d.FootY[l], d.FootZ[l]);
                if (MathF.Abs(target.X) < MathF.Abs(hip.X) * .5f) target.X = hip.X;
            }
            Limb(f, thigh, shin, hip, hip, target, f.Thigh, f.Shin, Vector3.Back, Vector3.Back);
        }

        Place(s, root);
        s.Head = root * _xf[(int)Bone.Head];
        // the eyes' light, before the face
        var eyes = s.Head * (neck + new Vector3(0, f.NeckUp * .45f + (s.Plan.Top - neck.Y) * .35f, 8));
        s.Eyes.Position = eyes;
        s.Eyes.Visible = d.Up > .3f || sky;
        s.Eyes.LightEnergy = d.Act == DeityAct.Roar ? 6 : 3.5f;
        // (hanging in the air its wings are open, beating slowly; struck low, half open)
        bool wings = spread || d.T.Winged && !going && d.Act is DeityAct.Standing or DeityAct.Walking or DeityAct.Roar;
        if (d.T.Winged) Heavenly(s, torso, neck, t, dt, wings, going, sky);
    }

    /// <summary>
    /// The archangel's wings, halo and light: the wings beating as it comes down and flies and blows, folded down its back
    /// as it walks; the halo level over its head; the pillar of light about it as it comes down and goes up.
    /// </summary>
    void Heavenly(Shown s, Transform3D torso, Vector3 neck, float t, float dt, bool spread, bool going, bool sky)
    {
        var d = s.D;
        var f = s.Fig;
        // how open they are, and how they beat
        float open = spread ? 1 : d.Act is DeityAct.Sweep or DeityAct.Slam ? .45f : .12f;
        s.Spread += (open - s.Spread) * Math.Min(1, dt * 3);
        bool flying = d.Act == DeityAct.Leap || going;
        float rate = d.Act == DeityAct.Gust ? 0 : flying ? 1.1f : spread ? .45f : .3f;
        s.Flap += dt * rate;
        float beat = MathF.Sin(s.Flap * MathF.Tau) * (flying ? .55f : spread ? .22f : .05f);
        // the gust: the wings drawn back, then swept hard forward
        if (d.Act == DeityAct.Gust) beat = t < Sim.GustAt ? .9f * Smooth(t / Sim.GustAt) : .9f - 1.8f * Smooth((t - Sim.GustAt) / .3f);
        float up = Mathf.Lerp(-1.2f, .35f, s.Spread) + beat * s.Spread, back = Mathf.Lerp(1.15f, .25f, s.Spread);
        if (d.Act == DeityAct.Gust) back = Mathf.Lerp(back, -.6f, Math.Clamp((t - Sim.GustAt + .2f) / .4f, 0, 1));
        var shoulder = f.Pivot[(int)Bone.UpperR];
        float bx = MathF.Abs(shoulder.X) * .3f, by = shoulder.Y - 3, bz = -(f.UpperArm * .12f) - 2;
        // (a wing's own +x goes out from its root: the left's to the figure's left, +x; the right's, mirrored, to −x)
        var left = torso * new Transform3D(new Basis(Vector3.Up, back) * new Basis(Vector3.Back, up), new Vector3(bx, by, bz));
        var right = torso * new Transform3D(new Basis(Vector3.Up, -back) * new Basis(Vector3.Back, -up), new Vector3(-bx, by, bz));
        s.WingL.Transform = s.Root * left;
        s.WingR.Transform = s.Root * right;
        bool hide = d.Id == HiddenId && s.Spread < .3f;
        s.WingL.Visible = s.WingR.Visible = !hide;
        // the light behind the head, upright
        float mid = (s.Plan.HeadY0 + s.Plan.HeadY1) / 4f + 1;
        s.Halo.Transform = s.Root * _xf[(int)Bone.Head] * new Transform3D(Basis.Identity, new Vector3(0, mid, neck.Z - 4.5f));
        s.Halo.Visible = d.Id != HiddenId;
        // the light it comes down and goes up in
        s.Beam.Visible = going && sky;
        if (s.Beam.Visible)
        {
            s.Beam.Position = new Vector3(_w.WX(d.X), _w.WY(d.Y) + 120, _w.WZ(d.Z));
            float a = d.Act == DeityAct.Rising ? 1 - Smooth((t - Sim.GodRiseFor + 1.5f) / 1.5f) : Smooth(t / 1.5f);
            ((StandardMaterial3D)s.Beam.MaterialOverride).AlbedoColor = new Color(1f, .86f, .55f, .55f * Math.Clamp(a, 0, 1));
        }
    }

    /// <summary>The bones, their shadows and what the figure holds, where the pose put them.</summary>
    void Place(Shown s, Transform3D root)
    {
        for (int b = 0; b < Figure.Bones; b++)
        {
            var xf = root * _xf[b];
            bool on = s.D.Id != HiddenId || (Bone)b is not (Bone.Head or Bone.Torso);
            if (s.Bones[b] is { } n) { n.Transform = xf; n.Visible = on; }
            if (s.Shades[b] is { } sh) sh.Transform = xf;
        }
        for (int i = 0; i < s.Dress.Length; i++)
            if (s.Dress[i] is { } n) n.Transform = root * _xf[(int)s.Fig.DressBone[i]];
    }

    // ------------------------------------------------------------ the toad

    /// <summary>
    /// The toad posed: up out of its bog as it comes, sitting with its head up; crouched before a hop, its body tipped up
    /// and its hind legs stretched out behind it in the air, its forelegs splayed coming down; its tongue out of its mouth
    /// to the tip.
    /// </summary>
    void PoseToad(Shown s, float dt)
    {
        var d = s.D;
        var f = s.Fig;
        var sim = _m.Sim;
        float t = d.Into(sim.Time), k = Math.Min(1, dt * 6);
        bool going = d.Act is DeityAct.Rising or DeityAct.Sinking;
        float off = going ? -(1 - d.Up) * d.T.Height * 1.1f : 0;
        float crouch = 0, pitch = 0, hind = 0, fore = 0;
        if (d.Act == DeityAct.Leap)
        {
            if (t < Sim.LeapCrouch) { crouch = Smooth(t / Sim.LeapCrouch); hind = -.3f * crouch; }
            else
            {
                float p = Math.Clamp((t - Sim.LeapCrouch) / Sim.HopFor, 0, 1);
                pitch = -.45f * MathF.Cos(MathF.PI * p);
                hind = 1.1f * MathF.Sin(MathF.PI * Math.Min(1, p * 1.4f));
                fore = -.6f * p;
                if (p >= 1) { crouch = .6f * (1 - Smooth((t - Sim.LeapCrouch - Sim.HopFor) / .4f)); fore = -.4f * crouch; }
            }
        }
        // sitting, its head up
        else if (d.Act == DeityAct.Tongue) pitch = -.1f;
        else if (d.Act == DeityAct.Spit) pitch = t < Sim.SpitAt ? -.4f * Smooth(t / Sim.SpitAt) : -.1f;
        else pitch = -.22f;
        s.Crouch += (crouch - s.Crouch) * k; s.Pitch += (pitch - s.Pitch) * k;
        var root = new Transform3D(new Basis(Vector3.Up, MathF.PI / 2 - d.Facing).Scaled(Vector3.One * VoxelWorld.Vox * s.Scale),
            new Vector3(_w.WX(d.X), _w.WY(d.Y + off), _w.WZ(d.Z)));
        root.Origin += Vector3.Down * s.Crouch * s.Plan.Top * .12f * VoxelWorld.Vox * s.Scale;
        s.Root = root;
        var body = RotAt(f.Pivot[(int)Bone.Torso], new Basis(Vector3.Right, s.Pitch));
        _xf[(int)Bone.Torso] = body;
        _xf[(int)Bone.Head] = body;
        // the legs: the forelegs from the shoulder, the hind legs from the hip, each bent at its knee
        foreach (var (upper, lower, a, knee) in new[]
        {
            (Bone.UpperR, Bone.LowerR, fore, -fore * .5f), (Bone.UpperL, Bone.LowerL, fore, -fore * .5f),
            (Bone.ThighR, Bone.ShinR, hind, -hind * .3f), (Bone.ThighL, Bone.ShinL, hind, -hind * .3f),
        })
        {
            var u = body * RotAt(f.Pivot[(int)upper], new Basis(Vector3.Right, a));
            _xf[(int)upper] = u;
            _xf[(int)lower] = u * RotAt(f.Pivot[(int)lower], new Basis(Vector3.Right, knee));
        }
        Place(s, root);
        s.Head = root * _xf[(int)Bone.Head];
        // the tongue, from the mouth of the figure as it is posed (low at the front of its head) to the tip
        var mouth = s.Root * _xf[(int)Bone.Head] * new Vector3(0, 13f, 17.5f);
        float out_ = d.Act == DeityAct.Tongue ? Sim.TongueOut(t) : 0;
        bool shown = out_ > .02f;
        s.Tongue.Visible = s.Tip.Visible = shown;
        if (shown)
        {
            var tip = new Vector3(_w.WX(d.HandX), _w.WY(d.HandY), _w.WZ(d.HandZ));
            var way = tip - mouth;
            float len = Math.Max(.1f, way.Length());
            var y = way / len;
            var x = y.Cross(Mathf.Abs(y.Y) > .9f ? Vector3.Right : Vector3.Up).Normalized();
            var z = x.Cross(y);
            s.Tongue.Transform = new Transform3D(new Basis(x, y * len, z), mouth + way * .5f);
            s.Tip.Position = tip;
        }
        // its eyes' light, over the head
        s.Eyes.Position = mouth + Vector3.Up * d.T.Height * VoxelWorld.Vox * .35f;
        s.Eyes.Visible = d.Up > .3f;
    }

    // ------------------------------------------------------------ limbs

    /// <summary>
    /// A limb of two bones reaching from its root (where it is now) to a point, bending at its middle joint toward the
    /// pole; each bone turned from hanging straight down to its new way, its front kept toward the hint.
    /// </summary>
    void Limb(Figure f, Bone upper, Bone lower, Vector3 restRoot, Vector3 root, Vector3 target, float a, float b, Vector3 pole, Vector3 hint)
    {
        var to = target - root;
        float dist = Math.Clamp(to.Length(), MathF.Abs(a - b) + .01f, (a + b) * .999f);
        var dir = to.LengthSquared() > 1e-4f ? to.Normalized() : Vector3.Down;
        // the joint: off the line by the triangle's height, toward the pole
        float along = (a * a - b * b + dist * dist) / (2 * dist), up = MathF.Sqrt(MathF.Max(0, a * a - along * along));
        var side = pole - dir * pole.Dot(dir);
        side = side.LengthSquared() > 1e-4f ? side.Normalized() : new Vector3(0, 0, 1);
        var joint = root + dir * along + side * up;
        var end = root + dir * dist;
        var restJoint = f.Pivot[(int)lower];
        _xf[(int)upper] = Aim(restRoot, root, (joint - root).Normalized(), hint);
        // (the lower bone hung from where the upper one's joint has gone, so the two meet there)
        var met = _xf[(int)upper] * restJoint;
        _xf[(int)lower] = Aim(restJoint, met, (end - met).Normalized(), hint);
    }

    /// <summary>A bone hanging straight down from <paramref name="rest"/> moved to hang from <paramref name="at"/> along a way, its front toward the hint.</summary>
    static Transform3D Aim(Vector3 rest, Vector3 at, Vector3 way, Vector3 hint)
    {
        var y = -way;
        var z = hint - y * hint.Dot(y);
        if (z.LengthSquared() < 1e-4f) z = Vector3.Back - y * y.Z;
        z = z.Normalized();
        var x = y.Cross(z).Normalized();
        var basis = new Basis(x, y, z);
        return new Transform3D(basis, at - basis * rest);
    }

    static Transform3D RotAt(Vector3 at, Basis b) => new(b, at - b * at);

    static float Smooth(float k) { k = Math.Clamp(k, 0, 1); return k * k * (3 - 2 * k); }
}
