using System;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The wounded as they are cared for (Sim.Carry.cs): one carried lies across its comrade's right shoulder face down,
/// the legs hanging before it and the head and arms down its back, the carrier's left arm round the legs; one limping
/// goes with its right arm over a comrade's shoulder, the comrade's arm round it. A bone splinted shows its splints,
/// two sticks bound on either side of the shin or the forearm, and an arm splinted is carried in a sling.
/// </summary>
public partial class UnitView
{
    MultiMeshInstance3D _splints;
    float[] _splintBuf = Array.Empty<float>();
    int _splintN;

    /// <summary>Splints, bindings and slings drawn in the last frame (the self-test).</summary>
    public int SplintsDrawn { get; private set; }

    static readonly Color Wood = new("8a6a44"), Linen = new("d8ceb5");

    void InitSplints()
    {
        var mat = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = .95f };
        _splints = Multi(new BoxMesh { Size = Vector3.One }, mat, true);
    }

    /// <summary>
    /// What it holds is put away: one carrying a comrade has its hands full (the pick or the weapon slung), and one carried
    /// has let go of its own; at the usual height the sticking-out weapons made a tangle of the two.
    /// </summary>
    static bool Stowed(Unit u) => u.Carrying is { Aloft: true } || Sim.OnShoulder(u);

    /// <summary>Up on a comrade's shoulder: where it lies, the waist on the shoulder, face down across it.</summary>
    Transform3D Shouldered(Unit u, Figure f)
    {
        var c = u.CarriedBy;
        var cf = KindOf(c).Fig;
        float face = _looks.TryGetValue(c.Id, out var cl) ? cl.Face : c.Facing;
        var cb = new Basis(Vector3.Up, MathF.PI / 2 - face);
        // over the carrier's right shoulder (its right is on −x), as high as the body is thick
        var sh = cf.Joint[(int)Part.Main];
        var on = new Vector3(sh.X * 1.05f, sh.Y + u.T.BodyR * 1.15f + 1.5f, sh.Z);
        var at = new Vector3(_w.WX(c.X), _w.WY(c.Y), _w.WZ(c.Z)) + cb * on * VoxelWorld.Vox;
        // the head toward the carrier's back, the front turned down
        var b = new Basis(Vector3.Up, MathF.PI / 2 - (face + MathF.PI)).Scaled(Vector3.One * VoxelWorld.Vox);
        var waist = f.Pivot[(int)Bone.Torso];
        return new Transform3D(b, at) * new Transform3D(new Basis(Vector3.Right, MathF.PI / 2), Vector3.Zero) * new Transform3D(Basis.Identity, -waist);
    }

    /// <summary>
    /// The limbs of the wounded and of those who see to them: carried, the arms and the legs hanging down from the
    /// shoulder and the trunk drooping down the carrier's back; carrying, the left arm round the legs; helped along, the
    /// right arm over the comrade's shoulder; helping, the left arm round the one it helps.
    /// </summary>
    void Aiding(Unit u, Figure f, ref Turns turns, ref float kneeR, ref float kneeL, ref float elbowR, ref float elbowL, ref float legR, ref float legL, ref float lean)
    {
        if (!f.Jointed) return;
        if (Sim.OnShoulder(u))
        {
            // (forward in its own frame is down, now that it lies face down)
            turns.Main = new Quaternion(Vector3.Right, -1.45f) * new Quaternion(Vector3.Back, .1f);
            turns.Off = new Quaternion(Vector3.Right, -1.45f) * new Quaternion(Vector3.Back, -.1f);
            elbowR = elbowL = -.25f;
            legR = -1.1f; legL = -.95f;
            kneeR = .55f; kneeL = .7f;
            lean = .75f;
            return;
        }
        if (u.Carrying is { Aloft: true })
        {
            turns.Off = turns.Off.Slerp(new Quaternion(Vector3.Right, -1.3f) * new Quaternion(Vector3.Back, -.25f), .9f);
            elbowL = -1.55f;
            lean += .1f;
        }
        if (Sim.Propped(u) && (Sim.Broken(u) & Break.Main) == 0)
        {
            // the right arm out over the comrade's shoulders (outward for the right arm is about the forward axis, negative)
            turns.Main = turns.Main.Slerp(new Quaternion(Vector3.Back, -1.75f), .9f);
            elbowR = -.7f;
        }
        if (u.Propping is { } p && Sim.Propped(p))
        {
            turns.Off = turns.Off.Slerp(new Quaternion(Vector3.Back, .7f) * new Quaternion(Vector3.Right, -.3f), .85f);
            elbowL = -1.25f;
        }
    }

    /// <summary>The splints on what is splinted of its bones, and the sling of an arm splinted.</summary>
    void Splints(Unit u, Figure f)
    {
        var w = u.Wounds;
        if (w == null || !f.Jointed || u.Dead) return;
        var s = w.Splinted & w.Broken;
        if (s == Break.None) return;
        if ((s & Break.LegR) != 0) Splint(f, Bone.ShinR, f.Shin, true);
        if ((s & Break.LegL) != 0) Splint(f, Bone.ShinL, f.Shin, true);
        if ((s & Break.Main) != 0) { Splint(f, Bone.LowerR, f.Forearm, false); Sling(f, Bone.LowerR, Part.Off); }
        if ((s & Break.Off) != 0) { Splint(f, Bone.LowerL, f.Forearm, false); Sling(f, Bone.LowerL, Part.Main); }
    }

    /// <summary>Two sticks either side of a shin or a forearm, from by the joint to by its end, bound round in two places.</summary>
    void Splint(Figure f, Bone bone, float len, bool leg)
    {
        var xf = _xf[(int)bone];
        var p = f.Pivot[(int)bone];
        float half = leg ? .95f : .8f;
        // from further off the sticks and their bindings are under a pixel: the limb is shown wrapped white its length
        if (_lod > 0)
        {
            Box(xf * new Transform3D(Basis.FromScale(new Vector3(half * 2 + .6f, len * .8f, half * 2 + .6f)), p + new Vector3(0, -len * .5f, 0)), Linen);
            return;
        }
        for (int s = -1; s <= 1; s += 2)
        {
            var off = leg ? new Vector3(s * half, 0, 0) : new Vector3(0, 0, s * half);
            Box(xf * new Transform3D(Basis.FromScale(new Vector3(.3f, len * .85f, .3f)), p + off + new Vector3(0, -len * .5f, 0)), Wood);
        }
        foreach (float k in new[] { .28f, .72f })
            Box(xf * new Transform3D(Basis.FromScale(new Vector3(half * 2 + .4f, .4f, half * 2 + .4f)), p + new Vector3(0, -len * k, 0)), Linen);
    }

    /// <summary>A sling: the forearm wrapped in linen, and the band from it over the other shoulder.</summary>
    void Sling(Figure f, Bone forearm, Part other)
    {
        var xf = _xf[(int)forearm];
        var p = f.Pivot[(int)forearm];
        Box(xf * new Transform3D(Basis.FromScale(new Vector3(2.1f, f.Forearm * .75f, 2.1f)), p + new Vector3(0, -f.Forearm * .5f, 0)), Linen);
        var a = _xf[(int)Bone.Torso] * (f.Joint[(int)other] + new Vector3(0, 1.2f, 0));
        var b = xf * (p + new Vector3(0, -f.Forearm * .6f, 0));
        var along = b - a;
        float len = along.Length();
        if (len < 1e-4f) return;
        var y = along / len;
        var x = y.Cross(Vector3.Up);
        if (x.LengthSquared() < 1e-6f) x = Vector3.Right;
        x = x.Normalized();
        var z = x.Cross(y);
        float thick = (_lod > 0 ? .9f : .35f) * VoxelWorld.Vox;
        Box(new Transform3D(new Basis(x * thick, y * len, z * thick), (a + b) / 2), Linen);
    }

    void Box(Transform3D t, Color c)
    {
        int o = _splintN++ * 16;
        if (_splintBuf.Length < o + 16) Array.Resize(ref _splintBuf, Math.Max(64 * 16, _splintBuf.Length * 2));
        Write(_splintBuf, o, t, c);
    }

    /// <summary>The splints of this frame to the card.</summary>
    void DrawSplints()
    {
        var mm = _splints.Multimesh;
        if (mm.InstanceCount < _splintN)
        {
            mm.InstanceCount = Math.Max(64, _splintBuf.Length / 16);
            if (_splintBuf.Length < mm.InstanceCount * 16) Array.Resize(ref _splintBuf, mm.InstanceCount * 16);
        }
        if (_splintN > 0)
        {
            // the buffer the card takes is the whole of the instances
            if (_splintBuf.Length != mm.InstanceCount * 16) Array.Resize(ref _splintBuf, mm.InstanceCount * 16);
            RenderingServer.MultimeshSetBuffer(mm.GetRid(), _splintBuf);
        }
        mm.VisibleInstanceCount = _splintN;
        SplintsDrawn = _splintN;
        _splintN = 0;
    }
}
