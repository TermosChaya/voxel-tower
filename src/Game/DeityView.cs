using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// Draws the gods (Sim.Deities.cs): each a figure of the valley sculpted as the others are and drawn a quarter of the
/// valley high, in cells of its own body, casting its shadow across the land. It is posed by what the simulation has
/// it at: each leg reaches down to where its foot stands or goes, the knee forward; the striking arm reaches to where
/// the hand is, the trunk bending down to the ground for a sweep and turning with it as the hand goes round, rearing
/// back for the roar; climbing out of the ground its arms reach up over the pit's rim. Its eyes light the ground before
/// it. Where it shakes the ground the dust goes up and the view shakes.
/// </summary>
public partial class DeityView : Node3D
{
    sealed class Shown
    {
        public Deity D;
        public BodyPlan Plan;
        public Figure Fig;
        public readonly MeshInstance3D[] Bones = new MeshInstance3D[Figure.Bones], Shades = new MeshInstance3D[Figure.Bones];
        public OmniLight3D Eyes;
        /// <summary>The figure's voxels to the world's, and the trunk's pose eased toward what is asked of it.</summary>
        public float Scale, Lean, Twist, HeadYaw, HeadPitch, Swing;
        public Transform3D Root, Head;
    }

    Main _m;
    VoxelWorld _w;
    readonly Dictionary<int, Shown> _shown = new();
    readonly Transform3D[] _xf = new Transform3D[Figure.Bones];

    public void Init(Main m)
    {
        _m = m;
        _w = m.World;
        m.Sim.GodShook += Shook;
        // (sculpted ahead on a worker thread: a god's body is many times a man's)
        System.Threading.Tasks.Task.Run(() => { Bodies.God(DeityKind.Colossus); Bodies.God(DeityKind.Devourer); });
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

    void Shook(Deity d, float x, float y, float z, float k)
    {
        var at = new Vector3(_w.WX(x), _w.WY(y), _w.WZ(z));
        _m.Debris.Spray(Mat.Soil, at, Vector3.Up, (int)(30 * k), 3 + 2 * k, 1.1f, 2.5f + k, life: 1.6f);
        _m.Debris.Spray(Mat.Rubble, at, Vector3.Up, (int)(12 * k), 5 + 3 * k, .9f, 3 + k);
        _m.Spectacle?.Felt(x, z, Math.Min(1.2f, .25f * k), flash: false);
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
            Pose(s, dt);
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
                s.Eyes?.QueueFree();
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
        var glow = d.T.Fiery ? new Color(1f, .42f, .12f) : new Color(1f, .62f, .25f);
        s.Eyes = new OmniLight3D { LightColor = glow, LightEnergy = 3.5f, OmniRange = 30, ShadowEnabled = false };
        AddChild(s.Eyes);
        return s;
    }

    // ------------------------------------------------------------ the pose

    void Pose(Shown s, float dt)
    {
        var d = s.D;
        var f = s.Fig;
        var sim = _m.Sim;
        float t = d.Into(sim.Time), k = Math.Min(1, dt * 5);
        bool under = d.Act is DeityAct.Rising or DeityAct.Sinking;
        // the root: between its feet on the ground, sunk under it while it is coming up or going down
        float sunk = (1 - d.Up) * d.T.Height * 1.02f;
        var root = new Transform3D(new Basis(Vector3.Up, MathF.PI / 2 - d.Facing).Scaled(Vector3.One * VoxelWorld.Vox * s.Scale),
            new Vector3(_w.WX(d.X), _w.WY(d.Y - sunk), _w.WZ(d.Z)));
        // walking, it sinks a little at the knees and rocks from foot to foot
        if (d.Lifted >= 0 && !under)
        {
            float p = Math.Clamp((float)((sim.Time - d.StepFrom) / d.StepFor), 0, 1);
            root.Origin += Vector3.Down * MathF.Sin(MathF.PI * p) * f.Thigh * .06f * VoxelWorld.Vox * s.Scale;
            s.Swing += dt * MathF.PI / (float)d.StepFor * (d.Lifted == 0 ? 1 : -1);
        }
        s.Root = root;
        var toFig = root.AffineInverse();
        Vector3 Fig(float x, float y, float z) => toFig * new Vector3(_w.WX(x), _w.WY(y), _w.WZ(z));

        // what the trunk is asked to do by the act
        var hand = Fig(d.HandX, d.HandY, d.HandZ);
        float handYaw = MathF.Atan2(hand.X, MathF.Max(1, hand.Z));
        float lean = .08f, twist = 0, headPitch = .15f, headYaw = 0;
        int arm = d.Side >= 0 ? 0 : 1;
        bool reach = false;
        switch (d.Act)
        {
            case DeityAct.Rising: lean = .35f * (1 - d.Up); headPitch = -.2f; break;
            case DeityAct.Sinking: lean = .2f; headPitch = -.35f; break;
            case DeityAct.Walking: lean = .14f; break;
            case DeityAct.Sweep:
                reach = true;
                lean = t < Sim.SweepFrom ? .35f + .45f * Smooth(t / Sim.SweepFrom) : t <= Sim.SweepTo ? .95f : .95f * (1 - Smooth((t - Sim.SweepTo) / (Sim.SweepDone - Sim.SweepTo)));
                twist = Math.Clamp(handYaw * .85f, -1.35f, 1.35f);
                headYaw = handYaw * .3f; headPitch = .45f;
                break;
            case DeityAct.Slam:
                reach = true; arm = 0;
                lean = t < 1.1f ? -.1f * Smooth(t / 1.1f) : t < Sim.SlamHit ? .75f : .75f * (1 - Smooth((t - Sim.SlamHit) / (Sim.SlamDone - Sim.SlamHit)));
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

        // the arms: the striking one reaches to the hand, the other hangs and swings, or both reach as the act asks
        for (int a = 0; a < 2; a++)
        {
            Bone upper = a == 0 ? Bone.UpperR : Bone.UpperL, lower = a == 0 ? Bone.LowerR : Bone.LowerL;
            var shoulder = f.Pivot[(int)upper];
            var now = torso * shoulder;
            float len = f.UpperArm + f.Forearm, side = a == 0 ? -1 : 1;
            Vector3 target;
            if (reach && a == arm) target = hand;
            else if (d.Act == DeityAct.Rising || d.Act == DeityAct.Sinking)
                // over the rim of the pit, clawing at the ground
                target = now + new Vector3(side * len * .75f, len * (d.Act == DeityAct.Rising ? .5f - .6f * d.Up : .55f), len * .35f);
            else if (d.Act == DeityAct.Roar)
                target = now + new Vector3(side * len * .8f, len * .35f, len * .25f);
            else
            {
                // hanging, out a little from its sides, swinging against the step
                float sw = MathF.Sin(s.Swing) * (a == 0 ? 1 : -1) * (d.Act == DeityAct.Walking ? .3f : 0);
                target = now + new Vector3(side * len * .18f, -len * .92f, len * (.08f + sw));
            }
            Limb(f, upper, lower, shoulder, now, target, f.UpperArm, f.Forearm, -fwd + Vector3.Right * side * .6f, fwd);
        }

        // the legs: each reaches down to where its foot is, the knee forward; under the ground, straight
        for (int l = 0; l < 2; l++)
        {
            Bone thigh = l == 0 ? Bone.ThighR : Bone.ThighL, shin = l == 0 ? Bone.ShinR : Bone.ShinL;
            var hip = f.Pivot[(int)thigh];
            // (the sim's first foot is on the right: −x of the figure)
            int foot = l;
            var target = under ? hip + Vector3.Down * (f.Thigh + f.Shin) : Fig(d.FootX[foot], d.FootY[foot], d.FootZ[foot]);
            target.X = under ? target.X : MathF.Abs(target.X) < MathF.Abs(hip.X) * .5f ? hip.X : target.X;
            Limb(f, thigh, shin, hip, hip, target, f.Thigh, f.Shin, Vector3.Back, Vector3.Back);
        }

        for (int b = 0; b < Figure.Bones; b++)
        {
            var xf = root * _xf[b];
            bool on = s.D.Id != HiddenId || (Bone)b is not (Bone.Head or Bone.Torso);
            if (s.Bones[b] is { } n) { n.Transform = xf; n.Visible = on; }
            if (s.Shades[b] is { } sh) sh.Transform = xf;
        }
        s.Head = root * _xf[(int)Bone.Head];
        // the eyes' light, before the face
        var eyes = s.Head * (neck + new Vector3(0, f.NeckUp * .45f + (s.Plan.Top - neck.Y) * .35f, 8));
        s.Eyes.Position = eyes;
        s.Eyes.Visible = d.Up > .3f;
        s.Eyes.LightEnergy = d.Act == DeityAct.Roar ? 6 : 3.5f;
    }

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
        _xf[(int)lower] = Aim(restJoint, joint, (end - joint).Normalized(), hint);
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
