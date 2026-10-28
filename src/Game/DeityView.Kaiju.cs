using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The gods' wounds as they are seen (Sim.Kaiju.cs): what a god has lost is gone from its figure — the toad's leg, the
/// archangel's wing — and flies off it whole, turning over, comes down and lies there; blood out of the stump, green for
/// the toad, gold for the archangel. The archangel's eye torn out goes off on the toad's tongue into its mouth, a dark
/// socket left in the face.
/// </summary>
public partial class DeityView
{
    sealed class Flying { public Node3D Node; public Vector3 Axis; public double From; public float Angle; }
    readonly Dictionary<int, Flying> _pieces = new();
    /// <summary>An eye on its way to the toad's mouth: the gem, where it was torn from, the toad, and when.</summary>
    readonly List<(MeshInstance3D gem, Vector3 from, Deity toad, double at)> _eyes = new();
    /// <summary>The dark sockets where an archangel's eye was, by the god.</summary>
    readonly Dictionary<int, MeshInstance3D> _sockets = new();
    static readonly Color ToadBlood = new(.35f, .5f, .12f), Ichor = new(1f, .78f, .3f);

    /// <summary>The figure's bones a wound takes off it.</summary>
    static Bone[] BonesOf(GodWound w) => w switch
    {
        GodWound.ForeRight => new[] { Bone.UpperR, Bone.LowerR }, GodWound.ForeLeft => new[] { Bone.UpperL, Bone.LowerL },
        GodWound.HindRight => new[] { Bone.ThighR, Bone.ShinR }, GodWound.HindLeft => new[] { Bone.ThighL, Bone.ShinL },
        _ => Array.Empty<Bone>(),
    };

    /// <summary>Whether a god's figure has lost this bone.</summary>
    static bool Lost(Deity d, Bone b)
    {
        if (d.Wounds == GodWound.None) return false;
        foreach (var w in new[] { GodWound.ForeRight, GodWound.ForeLeft, GodWound.HindRight, GodWound.HindLeft })
            if ((d.Wounds & w) != 0 && Array.IndexOf(BonesOf(w), b) >= 0) return true;
        return false;
    }

    void Maimed(Deity d, GodWound w, GodPiece piece)
    {
        if (!_shown.TryGetValue(d.Id, out var s)) return;
        var blood = d.T.Hops ? Mat.Brew : Mat.Brass;
        if (piece == null)
        {
            // the eye: out on the tongue to the toad's mouth
            var eye = EyeAt(s, w);
            var gem = new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = .7f, Height = 1.4f, RadialSegments = 10, Rings = 5 },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(.5f, .9f, 1f), EmissionEnabled = true, Emission = new Color(.5f, .9f, 1f), EmissionEnergyMultiplier = 3 },
                Position = eye,
            };
            AddChild(gem);
            _eyes.Add((gem, eye, d.Foe, _m.Sim.Time));
            _m.Debris.Spray(blood, eye, Vector3.Up, 70, 4, 1.4f, 3, life: 1.8f);
            _m.Spectacle?.Felt(d.X, d.Z, .8f, flash: false);
            return;
        }
        // the piece off the figure, flying (drawn by Kaiju), and the blood out of the stump
        var at = new Vector3(_w.WX(piece.X), _w.WY(piece.Y), _w.WZ(piece.Z));
        Fly(s, piece);
        _m.Debris.Spray(blood, at, Vector3.Up, 120, 6, 1.6f, 4, life: 2.2f);
        _m.Spectacle?.Felt(piece.X, piece.Z, 1f, flash: false);
    }

    /// <summary>
    /// A piece off a god drawn whole, as it stood on the figure: its bones (or the wing) under a node at where the sim has
    /// it. (One the view did not see go, cut off before it was drawn, is taken up as it is first seen.)
    /// </summary>
    void Fly(Shown s, GodPiece piece)
    {
        if (_pieces.ContainsKey(piece.Id)) return;
        var parts = new List<MeshInstance3D>();
        foreach (var b in BonesOf(piece.Part)) if (s.Bones[(int)b] is { } n) parts.Add(n);
        if (piece.Part == GodWound.LeftWing && s.WingL != null) parts.Add(s.WingL);
        if (piece.Part == GodWound.RightWing && s.WingR != null) parts.Add(s.WingR);
        // (turning about the middle of what it is made of, wherever the figure has those now)
        var mid = Vector3.Zero;
        foreach (var n in parts) mid += n.Transform.Origin / parts.Count;
        var node = new Node3D { Position = new Vector3(_w.WX(piece.X), _w.WY(piece.Y), _w.WZ(piece.Z)) };
        AddChild(node);
        foreach (var n in parts)
            node.AddChild(new MeshInstance3D { Mesh = n.Mesh, MaterialOverride = n.MaterialOverride, CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
                Transform = new Transform3D(n.Transform.Basis, n.Transform.Origin - mid) });
        var v = new Vector3(piece.VX, 0, piece.VZ);
        var axis = v.LengthSquared() > 1e-4f ? Vector3.Up.Cross(v).Normalized() : Vector3.Right;
        _pieces[piece.Id] = new Flying { Node = node, Axis = axis, From = _m.Sim.Time };
    }

    /// <summary>Where an archangel's eye is, in the world (the right eye, or the left): on its face, a little before it.</summary>
    Vector3 EyeAt(Shown s, GodWound w)
    {
        var at = EyeCells(s.Plan, w != GodWound.LeftEye);
        if (at != Vector3.Zero) return s.Head * at;
        var neck = s.Fig.Pivot[(int)Bone.Head];
        return s.Head * (neck + new Vector3(0, s.Fig.NeckUp * .45f + (s.Plan.Top - neck.Y) * .35f, 8));
    }

    static readonly Dictionary<(BodyPlan, bool), Vector3> _eyeCells = new();

    /// <summary>
    /// The middle of an eye's cells on a figure's face, as the sculpt drew them (alight: crystal, or embers for the dark
    /// angel; the right eye on the figure's −x side), in the figure's measure and just before the face; none found, zero.
    /// </summary>
    static Vector3 EyeCells(BodyPlan p, bool right)
    {
        if (_eyeCells.TryGetValue((p, right), out var v)) return v;
        var g = p.Grid[(int)global::VoxelTower.Game.Part.Body];
        float sx = 0, sy = 0, front = float.MinValue; int n = 0;
        for (int y = Math.Max(g.Y0, p.HeadY0); y < g.Y0 + g.SY; y++)
            for (int z = g.Z0; z < g.Z0 + g.SZ; z++)
                for (int x = g.X0; x < g.X0 + g.SX; x++)
                {
                    if (g.M[g.Index(x, y, z)] is not (Mat.Crystal or Mat.Ember) || x < 0 != right) continue;
                    sx += x + .5f; sy += y + .5f; n++; front = Math.Max(front, z + 1);
                }
        const float S = BodyPlan.Sub;
        v = n > 0 ? new Vector3(sx / n / S, sy / n / S, front / S + .1f) : Vector3.Zero;
        _eyeCells[(p, right)] = v;
        return v;
    }

    /// <summary>The pieces in the air turning over and coming down, lying where they fell, gone with their time; the eyes on their way; the sockets.</summary>
    void Kaiju()
    {
        var sim = _m.Sim;
        foreach (var p in sim.GodPieces)
            if (!_pieces.ContainsKey(p.Id) && _shown.TryGetValue(p.Of.Id, out var of) && of.Bones[(int)Bone.Torso] != null) Fly(of, p);
        List<int> gone = null;
        foreach (var (id, fl) in _pieces)
        {
            var p = sim.GodPieces.Find(q => q.Id == id);
            if (p == null) { fl.Node.QueueFree(); (gone ??= new()).Add(id); continue; }
            if (!p.Landed) fl.Angle = (float)(sim.Time - fl.From) * p.Spin;
            fl.Node.Transform = new Transform3D(new Basis(fl.Axis, fl.Angle), new Vector3(_w.WX(p.X), _w.WY(p.Y), _w.WZ(p.Z)));
        }
        if (gone != null) foreach (int id in gone) _pieces.Remove(id);
        for (int i = _eyes.Count - 1; i >= 0; i--)
        {
            var (gem, from, toad, at) = _eyes[i];
            float k = (float)((sim.Time - at) / .6);
            if (k >= 1 || toad == null) { gem.QueueFree(); _eyes.RemoveAt(i); continue; }
            var (mx, my, mz) = Sim.Mouth(toad);
            gem.Position = from.Lerp(new Vector3(_w.WX(mx), _w.WY(my), _w.WZ(mz)), k * k);
        }
        foreach (var (id, s) in _shown)
        {
            if ((s.D.Wounds & GodWound.RightEye) == 0 || s.D.Gone) continue;
            if (!_sockets.TryGetValue(id, out var socket))
            {
                socket = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(.9f, 1.3f, .25f) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(.08f, .02f, .02f), Roughness = 1 } };
                AddChild(socket);
                _sockets[id] = socket;
            }
            var eye = EyeAt(s, GodWound.RightEye);
            socket.Transform = new Transform3D(s.Head.Basis, eye);
            socket.Visible = s.Bones[(int)Bone.Head]?.Visible ?? true;
        }
        List<int> shut = null;
        foreach (var (id, socket) in _sockets) if (!_shown.ContainsKey(id)) { socket.QueueFree(); (shut ??= new()).Add(id); }
        if (shut != null) foreach (int id in shut) _sockets.Remove(id);
    }
}
