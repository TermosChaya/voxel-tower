using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The dead as bodies. One who falls goes limp: a bag of its bones held together at the joints — a man fifteen points
/// (the pelvis, the neck, the crown, each shoulder, elbow, hand, hip, knee and foot), a beast sixteen (the pelvis, the
/// chest, the neck's root, the muzzle, each shoulder or hip, knee and foot) — with sticks between them that keep the
/// bones their lengths and the trunk its shape. It falls under its own weight from the pose it died in, carried on by
/// the blow that felled it and the way it was going, settles on whatever ground is under each point of it, slopes and
/// steps and all, and is thrown about again by a blast near it. It lies still once it has lain quiet a while, and sinks
/// into the ground as the dead always have; if the ground under it goes (a wall blown or fallen), it falls again. A
/// horse with its rider and an engine fall as they did; one thrown through the air goes limp where it comes down.
/// </summary>
public partial class UnitView
{
    sealed class Corpse
    {
        /// <summary>The points now and a step ago (world voxels), and how far each keeps off the ground.</summary>
        public Vector3[] P, Was;
        public float[] R;
        /// <summary>The sticks: two points, the length they keep, and for a loose one only how near they may come (0: as long as it is).</summary>
        public (int a, int b, float len, float least)[] Sticks;
        /// <summary>Each bone's two points, and which way it lies between them as the figure is made.</summary>
        public (Bone bone, int from, int to, Vector3 rest)[] Bones;
        public Vector3[] Fig;
        /// <summary>The ground under each point when it came to lie still, to tell when it goes.</summary>
        public float[] Under;
        public float Quiet, Due;
        public double Looked;
        public bool Still;
    }

    readonly Dictionary<int, Corpse> _rags = new();
    // the last blow on each: which way the blade went, and when
    readonly Dictionary<int, (Vector3 dir, double at)> _felling = new();
    bool _ragsHooked;

    /// <summary>Gravity, voxels a second a second: 9.8 m/s² at eight voxels to the metre.</summary>
    const float Weight = 78.4f;
    /// <summary>A body is moved on in steps of this long (seconds), however the frames come.</summary>
    const float Tick = 1 / 120f;

    void HookRags(Sim sim)
    {
        if (_ragsHooked) return;
        _ragsHooked = true;
        sim.Smote += h => { if (h.On != null) _felling[h.On.Id] = (new Vector3(h.DX, 0, h.DZ), sim.Time); };
        // a blast throws the dead lying near it outward and up, as it throws the living (Sim.Detonate), each point of a
        // body by how near it is, so that one lying across the blast turns over as it goes
        sim.Exploded += (x, y, z, r) =>
        {
            var c = new Vector3(x, y, z);
            float reach = r * 1.8f, strength = Math.Clamp(r / (2.5f * VoxelWorld.PerMetre), .5f, 1.2f);
            foreach (var rag in _rags.Values)
            {
                if (rag.P[0].DistanceSquaredTo(c) > reach * reach * 4) continue;
                bool hit = false;
                for (int i = 0; i < rag.P.Length; i++)
                {
                    var d = rag.P[i] - c;
                    float l = d.Length(), h = MathF.Sqrt(d.X * d.X + d.Z * d.Z);
                    if (l > reach) continue;
                    float push = (6 + 9 * (1 - l / reach)) * VoxelWorld.PerMetre * strength;
                    var away = h > .3f ? new Vector3(d.X / h, 0, d.Z / h) * .8f : Vector3.Zero;
                    rag.Was[i] -= (away + Vector3.Up * .75f) * push * Tick;
                    hit = true;
                }
                if (hit) { rag.Still = false; rag.Quiet = 0; }
            }
        };
    }

    /// <summary>
    /// The dead one's bones from its body as it falls and lies (made from this frame's pose the first time), into _xf;
    /// nothing for a figure that is not a man's or a beast's, or one still in the air.
    /// </summary>
    void Limp(Unit u, Figure f, Look look)
    {
        if (!f.Jointed && !f.Legged || u.Airborne != null) return;
        var sim = _m.Sim;
        HookRags(sim);
        if (!_rags.TryGetValue(u.Id, out var rag))
        {
            _rags[u.Id] = rag = MakeRag(u, f, look);
            // one dead a while already (a game read back): it lies as it would have come to lie, without falling again
            if (sim.Time - u.DiedAt > 1.5) { for (int i = 0; i < 480 && !rag.Still; i++) Settle(rag); }
        }
        float dt = _dt * _m.Speed;
        if (!rag.Still && dt > 0)
        {
            rag.Due = Math.Min(rag.Due + dt, 8 * Tick);
            for (; rag.Due >= Tick; rag.Due -= Tick) Settle(rag);
        }
        else if (rag.Still && sim.Time >= rag.Looked)
        {
            // the ground under it gone: it falls again
            rag.Looked = sim.Time + .5;
            for (int i = 0; i < rag.P.Length; i++)
                if (FloorUnder(rag.P[i].X, rag.P[i].Z, rag.P[i].Y + rag.R[i]) < rag.Under[i] - 1) { rag.Still = false; rag.Quiet = 0; break; }
        }
        // as the dead do, it goes down into the ground after a while
        float sink = Math.Max(0, (float)(sim.Time - u.DiedAt) - 18) * 1.4f;
        Side(rag, out var side);
        foreach (var (bone, from, to, rest) in rag.Bones)
        {
            var d = rag.P[to] - rag.P[from];
            if (d.LengthSquared() < 1e-6f) continue;
            var turn = Turning(rest, d.Normalized(), side);
            var at = rag.P[from] - new Vector3(0, sink, 0);
            var basis = turn.Scaled(Vector3.One * VoxelWorld.Vox);
            var origin = new Vector3(_m.World.WX(at.X), _m.World.WY(at.Y), _m.World.WZ(at.Z));
            _xf[(int)bone] = new Transform3D(basis, origin - basis * rag.Fig[from]);
        }
        look.Body = _xf[(int)Bone.Torso];
    }

    /// <summary>
    /// The turn that lays a bone's way as the figure is made along the way it lies now, with the figure's left (+x) as
    /// near the body's left now as it can be.
    /// </summary>
    static Basis Turning(Vector3 rest, Vector3 now, Vector3 side)
    {
        var e2 = Vector3.Right - rest * rest.Dot(Vector3.Right);
        if (e2.LengthSquared() < 1e-4f) e2 = Vector3.Back - rest * rest.Dot(Vector3.Back);
        e2 = e2.Normalized();
        var f2 = side - now * now.Dot(side);
        if (f2.LengthSquared() < 1e-4f) f2 = (Vector3.Up.Cross(now).LengthSquared() > 1e-4f ? Vector3.Up.Cross(now) : Vector3.Right) - now * now.Dot(Vector3.Right);
        f2 = f2.Normalized();
        var from = new Basis(rest, e2, rest.Cross(e2));
        var to = new Basis(now, f2, now.Cross(f2));
        return to * from.Transposed();
    }

    /// <summary>The body's left as it lies: across the hips and the shoulders.</summary>
    static void Side(Corpse rag, out Vector3 side)
    {
        // points 1, 2: a beast's chest and neck; the hips and shoulders are at the same places in both rigs by name
        var (hr, hl, sr, sl) = rag.P.Length == 15 ? (9, 12, 3, 6) : (10, 13, 4, 7);
        side = rag.P[hl] - rag.P[hr] + rag.P[sl] - rag.P[sr];
        if (side.LengthSquared() < 1e-4f) side = Vector3.Right;
    }

    /// <summary>A body's points and sticks, from the bones as they stand this frame.</summary>
    Corpse MakeRag(Unit u, Figure f, Look look)
    {
        var points = new List<(Vector3 fig, Bone on, float r)>();
        var bones = new List<(Bone, int, int)>();
        var braces = new List<(int, int, bool)>();
        if (f.Jointed)
        {
            var pelvis = f.Pivot[(int)Bone.Torso];
            var neck = f.Pivot[(int)Bone.Head];
            var crown = new Vector3(neck.X, Math.Max(neck.Y + 1.5f, f.Plan.Top), neck.Z);
            points.Add((pelvis, Bone.Torso, 1.4f)); points.Add((neck, Bone.Head, 1.3f)); points.Add((crown, Bone.Head, 1.1f));
            foreach (var (part, upper, lower) in new[] { (Part.Main, Bone.UpperR, Bone.LowerR), (Part.Off, Bone.UpperL, Bone.LowerL) })
            {
                var sh = f.Joint[(int)part]; var el = f.Pivot[(int)lower];
                points.Add((sh, upper, .7f)); points.Add((el, lower, .5f)); points.Add((el - new Vector3(0, f.Forearm, 0), lower, .5f));
            }
            foreach (var (part, upper, lower) in new[] { (Part.LegR, Bone.ThighR, Bone.ShinR), (Part.LegL, Bone.ThighL, Bone.ShinL) })
            {
                var hip = f.Joint[(int)part]; var kn = f.Pivot[(int)lower];
                points.Add((hip, upper, .8f)); points.Add((kn, lower, .6f)); points.Add((kn - new Vector3(0, f.Shin, 0), lower, .5f));
            }
            bones.AddRange(new[] { (Bone.Torso, 0, 1), (Bone.Head, 1, 2), (Bone.UpperR, 3, 4), (Bone.LowerR, 4, 5), (Bone.UpperL, 6, 7), (Bone.LowerL, 7, 8),
                (Bone.ThighR, 9, 10), (Bone.ShinR, 10, 11), (Bone.ThighL, 12, 13), (Bone.ShinL, 13, 14) });
            // the trunk held in its shape: the hips and the shoulders to the spine and across and from corner to corner
            braces.AddRange(new[] { (0, 9, false), (0, 12, false), (9, 12, false), (1, 3, false), (1, 6, false), (3, 6, false), (3, 9, false), (6, 12, false),
                (3, 12, false), (6, 9, false), (0, 2, false), (3, 2, true), (6, 2, true), (9, 11, true), (12, 14, true), (3, 5, true), (6, 8, true) });
        }
        else
        {
            var hind = (f.Joint[(int)Part.LegR] + f.Joint[(int)Part.LegL]) / 2;
            var fore = (f.Joint[(int)Part.Main] + f.Joint[(int)Part.Off]) / 2;
            var root = f.Pivot[(int)Bone.Head];
            var muzzle = root + new Vector3(0, 1.4f, 2.6f);
            float body = Math.Max(.8f, MathF.Abs(f.Joint[(int)Part.Main].X) + .4f);
            points.Add((hind, Bone.Torso, body)); points.Add((fore, Bone.Torso, body)); points.Add((root, Bone.Head, body * .8f)); points.Add((muzzle, Bone.Head, .6f));
            var legs = new[] { (Part.Main, Bone.UpperR, Bone.LowerR, 0), (Part.Off, Bone.UpperL, Bone.LowerL, 1), (Part.LegR, Bone.ThighR, Bone.ShinR, 2), (Part.LegL, Bone.ThighL, Bone.ShinL, 3) };
            foreach (var (part, upper, lower, l) in legs)
            {
                var top = f.Joint[(int)part]; var kn = f.Pivot[(int)lower];
                points.Add((top, upper, .5f)); points.Add((kn, lower, .4f)); points.Add((kn - new Vector3(0, f.LegLower[l], 0), lower, .4f));
            }
            bones.AddRange(new[] { (Bone.Torso, 0, 1), (Bone.Head, 2, 3), (Bone.UpperR, 4, 5), (Bone.LowerR, 5, 6), (Bone.UpperL, 7, 8), (Bone.LowerL, 8, 9),
                (Bone.ThighR, 10, 11), (Bone.ShinR, 11, 12), (Bone.ThighL, 13, 14), (Bone.ShinL, 14, 15) });
            braces.AddRange(new[] { (0, 10, false), (0, 13, false), (10, 13, false), (1, 4, false), (1, 7, false), (4, 7, false), (4, 10, false), (7, 13, false),
                (4, 13, false), (7, 10, false), (1, 2, false), (0, 2, false), (1, 3, true), (4, 6, true), (7, 9, true), (10, 12, true), (13, 15, true) });
        }
        int n = points.Count;
        var rag = new Corpse { P = new Vector3[n], Was = new Vector3[n], R = new float[n], Fig = new Vector3[n] };
        // where each point is now, by the bone it goes with as this frame posed it
        for (int i = 0; i < n; i++)
        {
            var (fig, on, r) = points[i];
            var w = _xf[(int)on] * fig;
            rag.P[i] = new Vector3((w.X - _m.World.OriginX) / VoxelWorld.Vox, w.Y / VoxelWorld.Vox, (w.Z - _m.World.OriginZ) / VoxelWorld.Vox);
            rag.Fig[i] = fig;
            rag.R[i] = r;
        }
        // nothing of it in the ground: a foot sunk in it by the pose it died in lifts the whole body to stand on it
        float lift = 0;
        for (int i = 0; i < n; i++)
        {
            var p = rag.P[i];
            lift = MathF.Max(lift, FloorUnder(p.X, p.Z, p.Y + rag.R[i]) + rag.R[i] - p.Y);
        }
        if (lift > 0) for (int i = 0; i < n; i++) rag.P[i].Y += MathF.Min(lift, 4);
        // carried on by the way it was going, and by the blow that felled it, the upper body the most
        var going = new Vector3(MathF.Cos(look.Face), 0, MathF.Sin(look.Face)) * look.Speed * .5f;
        var blow = Vector3.Zero;
        if (_felling.TryGetValue(u.Id, out var hit) && _m.Sim.Time - hit.at < .8 && hit.dir.LengthSquared() > 1e-4f) blow = hit.dir.Normalized() * 14;
        float low = rag.P[0].Y, high = rag.P[0].Y;
        foreach (var p in rag.P) { low = MathF.Min(low, p.Y); high = MathF.Max(high, p.Y); }
        for (int i = 0; i < n; i++)
        {
            float up = high > low ? (rag.P[i].Y - low) / (high - low) : 1;
            rag.Was[i] = rag.P[i] - (going + blow * up + Vector3.Up * 1.5f * up) * Tick;
        }
        var sticks = new List<(int, int, float, float)>();
        void Stick(int a, int b, bool loose)
        {
            float len = rag.P[a].DistanceTo(rag.P[b]);
            if (len > .01f) sticks.Add((a, b, len, loose ? len * .55f : 0));
        }
        foreach (var (_, a, b) in bones) Stick(a, b, false);
        foreach (var (a, b, loose) in braces) Stick(a, b, loose);
        rag.Sticks = sticks.ToArray();
        rag.Bones = new (Bone, int, int, Vector3)[bones.Count];
        for (int i = 0; i < bones.Count; i++)
        {
            var (bone, a, b) = bones[i];
            var d = rag.Fig[b] - rag.Fig[a];
            rag.Bones[i] = (bone, a, b, d.LengthSquared() > 1e-6f ? d.Normalized() : Vector3.Up);
        }
        return rag;
    }

    /// <summary>The top of what blocks under a point (voxels), looked for a little above it and a good way down; far below if nothing is.</summary>
    float FloorUnder(float x, float z, float top)
    {
        var w = _m.World;
        int ix = (int)MathF.Floor(x), iz = (int)MathF.Floor(z), y0 = (int)MathF.Floor(top) + 2;
        for (int y = y0; y >= y0 - 14 && y >= 0; y--)
            if (NavGrid.Blocks(w.Get(ix, y, iz))) return y + 1;
        return y0 - 15;
    }

    /// <summary>One step of a falling body: each point on by its own going and its weight, the sticks kept, the ground kept out.</summary>
    void Settle(Corpse rag)
    {
        float moved = 0;
        for (int i = 0; i < rag.P.Length; i++)
        {
            var v = (rag.P[i] - rag.Was[i]) * .998f;
            rag.Was[i] = rag.P[i];
            rag.P[i] += v + new Vector3(0, -Weight * Tick * Tick, 0);
        }
        for (int it = 0; it < 6; it++)
        {
            foreach (var (a, b, len, least) in rag.Sticks)
            {
                var d = rag.P[b] - rag.P[a];
                float l = d.Length();
                if (l < 1e-5f) continue;
                float want = least > 0 ? Math.Clamp(l, least, len) : len;
                if (MathF.Abs(l - want) < 1e-4f) continue;
                var fix = d * ((l - want) / l * .5f);
                rag.P[a] += fix; rag.P[b] -= fix;
            }
            // the ground: each point kept up on what is under it
            for (int i = 0; i < rag.P.Length; i++)
            {
                var p = rag.P[i];
                var was = rag.Was[i];
                float ground = FloorUnder(p.X, p.Z, p.Y + rag.R[i]);
                if (p.Y - rag.R[i] >= ground) continue;
                // come at something higher than its middle from the side: kept out of it, back where it was across (what
                // is lower it rides up on)
                if (p.Y < ground && (MathF.Floor(p.X) != MathF.Floor(was.X) || MathF.Floor(p.Z) != MathF.Floor(was.Z)))
                {
                    p.X = was.X; p.Z = was.Z;
                    ground = FloorUnder(p.X, p.Z, p.Y + rag.R[i]);
                }
                p.Y = MathF.Max(p.Y, ground + rag.R[i]);
                rag.P[i] = p;
                // the ground holds a point up without throwing it
                if (was.Y < p.Y) rag.Was[i] = new Vector3(was.X, p.Y, was.Z);
            }
        }
        // what lies on the ground drags on it, as on rough ground
        for (int i = 0; i < rag.P.Length; i++)
        {
            var p = rag.P[i];
            if (p.Y - rag.R[i] > FloorUnder(p.X, p.Z, p.Y + rag.R[i]) + .05f) continue;
            rag.Was[i] = new Vector3(Mathf.Lerp(rag.Was[i].X, p.X, .3f), rag.Was[i].Y, Mathf.Lerp(rag.Was[i].Z, p.Z, .3f));
        }
        for (int i = 0; i < rag.P.Length; i++) moved = MathF.Max(moved, (rag.P[i] - rag.Was[i]).LengthSquared());
        // still once it has lain quiet a while, the ground under it marked
        rag.Quiet = moved < .0004f ? rag.Quiet + Tick : 0;
        if (rag.Quiet <= 1.2f) return;
        rag.Still = true;
        rag.Under ??= new float[rag.P.Length];
        for (int i = 0; i < rag.P.Length; i++) rag.Under[i] = FloorUnder(rag.P[i].X, rag.P[i].Z, rag.P[i].Y + rag.R[i]);
    }
}
