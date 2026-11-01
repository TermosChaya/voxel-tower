using System;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// The gods (--pose=god): the Archangel called down into the open ground west of the citadel with a band of the horde
    /// coming at it. --look=0 its sword through the band, halfway round; 1 it comes down in the light; 2 the pillar of
    /// heaven comes down on the band; 3 it stands; 4 the Great Toad against it; 5 the player in it, the sword going
    /// through the band below; 6 the Great Toad alone; 7 the intervention's page of the tree with every circle learnt to
    /// its end, the appearance at its heart to be learnt; 8 the toad's tongue out at a file of ours; 9 the Archangel in
    /// flight over the band; the kaiju's wounds (Sim.Kaiju.cs): 10 the toad's foreleg cut off by the sword, flying over
    /// the band; 11 after, the leg lying on the crushed band, the archangel with an eye torn out and a wing torn off;
    /// 12 its face close, the socket; 13 the player in it, one eye gone; their bodies by themselves, standing still,
    /// with a camera of their own (--cam=yaw,how far in heights,how high it looks in heights): 14 the archangel before,
    /// 15 behind, 18 from the side; 16 the toad before, 17 from the side, 19 behind.
    /// </summary>
    void GodPose()
    {
        var sim = Sim; var w = World;
        sim.Waves.Countdown = 1e9; sim.WildTarget = 0;
        if (_look == 7)
        {
            foreach (var l in Sim.LoresOn(2)) if (l.Tier < Sim.Heart) { if (l.Spell is { } k) sim.Known.Add(k); else sim.Grant(l); }
            sim.Revelations = 9; sim.Favour = 150;
            Hud.OpenPanel("lore");
            Hud.Lore.ShowPage(2);
            return;
        }
        const float M = VoxelWorld.PerMetre;
        int gx = Site.KeepX - Site.KeepR - (int)(46 * M), gz = Site.KeepZ;
        ClearTrees(gx - 420, gz - 260, gx + 160, gz + 260);
        bool sheet = _look is >= 14 and <= 19, toad = _look is 6 or 8 or 16 or 17 or 19, kaiju = _look is >= 10 and <= 13;
        // (the gods' fight nearer the citadel, the toad before the archangel and the leg flying on beyond it)
        int ax = kaiju ? gx + (int)(25 * M) : gx;
        // a band of the horde coming from the west, or for the toad a file of ours; for the gods' fight beyond the toad,
        // where the leg comes down
        int band = _look is 3 or 6 || sheet ? 0 : 42, bandX = ax - (int)((kaiju ? 58 : 22) * M);
        for (int i = 0; i < band; i++)
        {
            var t = toad ? (i % 3 == 0 ? Kinds.Archer : Kinds.Swordsman) : i % 7 == 0 ? Kinds.Troll : i % 3 == 0 ? Kinds.Orc : Kinds.Goblin;
            int c = sim.NearestStand(bandX - i % 6 * 12, gz - 50 + i / 6 * 14, t.Move.Height);
            if (c < 0) continue;
            var u = sim.Spawn(t, c);
            (u.X, u.Y, u.Z) = sim.Nav.Feet(c);
            if (toad) { u.Hold = true; u.Dest = u.Cell; u.Post = u.Cell; } else { u.Guard = true; u.Home = u.Cell; }
        }
        if (!toad && band > 0) sim.Spawn(Kinds.Ram, sim.NearestStand(gx - (int)(30 * M), gz + 60, 2)).Guard = true;
        // (the toad to the archangel's side, well inside the valley)
        if (_look == 4) sim.Awaken(DeityKind.Toad, gx, gz + 45 * M, lasts: 600);
        if (kaiju) sim.Awaken(DeityKind.Toad, ax - 36 * M, gz, lasts: 600);
        sim.Awaken(toad ? DeityKind.Toad : DeityKind.Archangel, ax, gz, lasts: 600);
        var god = sim.Gods.Find(g => !g.Gone && g.T.Kind == (toad ? DeityKind.Toad : DeityKind.Archangel));
        void Run(double s) { for (double t = 0; t < s; t += .05) sim.Step(.05f); }
        if (_look == 1) Run(5.5);
        else
        {
            Run(Sim.GodRiseFor + .3);
            // (the archangel turned to the band; the toad left facing whatever it went for)
            if (!toad) god.Facing = MathF.PI;
            if (_look == 5)
            {
                Gods.Enter(god);
                Gods.Face(MathF.PI, -.55f);
                sim.GodWishSweep = 1;
                Run(.1);
                while (god.Act == DeityAct.Sweep && god.Into(sim.Time) < 1.2) sim.Step(.05f);
            }
            else if (_look is 0 or 2 or 9)
            {
                sim.Ride(god);
                sim.GodLookYaw = MathF.PI;
                if (_look == 0) sim.GodWishSweep = 1;
                else if (_look == 2) { sim.GodWishSlam = true; sim.GodAimX = gx - 26 * M; sim.GodAimZ = gz; sim.GodAimY = w.SurfAt((int)sim.GodAimX, gz); }
                else { sim.GodWishLeap = true; sim.GodAimX = gx - 50 * M; sim.GodAimZ = gz + 10 * M; sim.GodAimY = w.SurfAt((int)sim.GodAimX, (int)sim.GodAimZ); }
                Run(.1);
                double at = _look == 0 ? 1.25 : _look == 2 ? Sim.SlamHit + .08 : Sim.LeapCrouch + Sim.FlightFor * .45;
                while (god.Act is DeityAct.Sweep or DeityAct.Slam or DeityAct.Leap && god.Into(sim.Time) < at) sim.Step(.05f);
            }
            else if (_look == 8)
            {
                while (god.Act != DeityAct.Tongue && sim.Time < 400) sim.Step(.05f);
                while (god.Act == DeityAct.Tongue && god.Into(sim.Time) < Sim.TongueGrab + .05) sim.Step(.05f);
            }
            else if (_look == 4) Run(6);
            else if (kaiju) Kaiju(god);
            else if (sheet)
            {
                // standing still, facing west
                god.NextThink = sim.Time + 1e6;
                god.Act = DeityAct.Standing; god.Facing = MathF.PI;
                Run(.3);
            }
        }
        Paused = true;
        _shotFrames = Math.Max(_shotFrames, 70);
        // (the camera stands off toward (sin yaw, cos yaw) of what it looks at: before the god, which faces west, it is west)
        float y = w.WY(w.SurfAt(gx, gz)) + (_look == 1 ? 26 : 18);
        float yaw = _look == 4 ? -1.2f : -1.05f;
        if (_cam is { Length: > 0 }) yaw = _cam[0];
        float ground = w.WY(w.SurfAt(gx, gz)), tall = god.T.Height * VoxelWorld.Vox;
        float Cam(int i, float or) => _cam is { } c && c.Length > i ? c[i] : or;
        if (sheet)
        {
            // at the middle of its height (the toad's a little lower), standing off before it, behind it or beside it
            float yaw0 = _look switch { 15 or 19 => 2.3f, 18 or 17 => .15f, _ => -1.05f };
            PoseCamera(new Vector3(w.WX(god.X), ground + tall * Cam(2, toad ? .4f : .56f), w.WZ(god.Z)), Cam(0, yaw0), Cam(1, 2.4f) * tall);
        }
        else if (_look is 10 or 11)
        {
            // from the side, both gods and what flew off between them across the view
            var toadGod = sim.Gods.Find(g => !g.Gone && g.T.Kind == DeityKind.Toad);
            float mx = toadGod == null ? god.X : (god.X * .45f + toadGod.X * .55f) - 6 * M;
            PoseCamera(new Vector3(w.WX(mx), ground + tall * Cam(2, .36f), w.WZ(god.Z)), Cam(0, .1f), Cam(1, 2.3f) * tall);
        }
        else if (_look == 12)
        {
            // before its face, once the view has drawn it and the eyes are known
            float yaw12 = Cam(0, -1.0f), far12 = Cam(2, .5f) * tall;
            PoseCamera(new Vector3(w.WX(god.X), ground + tall * .95f, w.WZ(god.Z)), yaw12, far12);
            GetTree().CreateTimer(.4).Timeout += () => { if (GodView.TryEyes(god, out var e)) PoseCamera(e, yaw12, far12, .1f); };
        }
        else if (_look != 13) Rig.SetAt(w.WX(gx - 8 * M), y, w.WZ(gz + (_look == 4 ? 22 * M : 0)), yaw, .38f, .3f);
        if (sheet || kaiju && _look != 13) { Hud.Visible = false; Gods.NoPlate = true; }
        Hud.Refresh();
    }

    /// <summary>
    /// A camera of the pictures' own on a point of the world, standing off from it along a way (yaw as the rig's) as far,
    /// a little above it; the rig brought to the same spot, so the land about it is drawn as near as it is seen.
    /// </summary>
    void PoseCamera(Vector3 at, float yaw, float far, float rise = .3f)
    {
        Rig.SetAt(at.X, at.Y, at.Z, yaw, .4f, 1.6f);
        _poseCam ??= new Camera3D { Fov = 40, Near = .5f, Far = 3000 };
        if (!_poseCam.IsInsideTree()) AddChild(_poseCam);
        _poseCam.LookAtFromPosition(at + new Vector3(MathF.Sin(yaw), rise, MathF.Cos(yaw)).Normalized() * far, at, Vector3.Up);
        _poseCam.MakeCurrent();
    }

    Camera3D _poseCam;

    /// <summary>
    /// The gods' fight for the pictures: the toad struck low before the archangel's sword (the player in it) has a foreleg
    /// cut off, which flies over the band (--look=10 caught in the air); then the toad at the archangel's head tears an
    /// eye out and a wing off (11 to 13), the leg left lying.
    /// </summary>
    void Kaiju(Deity god)
    {
        var sim = Sim;
        const float M = VoxelWorld.PerMetre;
        void Run(double s) { for (double t = 0; t < s; t += .05) sim.Step(.05f); }
        var toad = sim.Gods.Find(g => !g.Gone && g.T.Kind == DeityKind.Toad);
        if (toad == null) return;
        sim.Ride(god);
        toad.Act = god.Act = DeityAct.Standing;
        float fa = god.Facing = MathF.PI;
        toad.X = god.X + MathF.Cos(fa) * god.T.Reach; toad.Z = god.Z + MathF.Sin(fa) * god.T.Reach;
        toad.Facing = 0;
        toad.Hp = toad.T.Hp * .55f; toad.NextThink = sim.Time + 30;
        Run(.2);
        sim.GodLookYaw = fa; sim.GodWishSweep = 1;
        for (int i = 0; i < 80 && sim.GodPieces.Count == 0; i++) Run(.05);
        var leg = sim.GodPieces.Find(p => p.Of == toad);
        if (leg == null) return;
        if (_look == 10) { Run(1.1); return; }
        for (int i = 0; i < 120 && !leg.Landed; i++) Run(.05);
        leg.Until = sim.Time + 1e6;
        // the toad at the archangel's head: an eye, then a wing
        toad.NextThink = 0;
        toad.X = god.X + MathF.Cos(fa) * 30 * M; toad.Z = god.Z + MathF.Sin(fa) * 30 * M + 12 * M;
        for (int i = 0; i < 1600 && (god.Wounds & GodWound.RightEye) == 0; i++) { god.Hp = Math.Clamp(god.Hp, god.T.Hp * .45f, god.T.Hp * .59f); toad.Hp = Math.Max(toad.Hp, toad.T.Hp * .4f); Run(.05); }
        for (int i = 0; i < 1600 && (god.Wounds & GodWound.LeftWing) == 0; i++) { god.Hp = Math.Clamp(god.Hp, god.T.Hp * .15f, god.T.Hp * .29f); toad.Hp = Math.Max(toad.Hp, toad.T.Hp * .4f); Run(.05); }
        var wing = sim.GodPieces.Find(p => p.Of == god);
        for (int i = 0; i < 120 && wing is { Landed: false }; i++) Run(.05);
        if (wing != null) wing.Until = sim.Time + 1e6;
        // (both held standing for the looks after: the toad before the archangel, or behind it for the close look at its
        // face, the archangel kept on its feet)
        toad.NextThink = sim.Time + 1e6;
        god.Hp = Math.Max(god.Hp, god.T.Hp * .2f);
        toad.Act = god.Act = DeityAct.Standing;
        god.Facing = fa;
        float off = _look == 12 ? -45 * M : 50 * M;
        toad.X = god.X + MathF.Cos(fa) * off; toad.Z = god.Z + MathF.Sin(fa) * off;
        toad.Facing = MathF.Atan2(god.Z - toad.Z, god.X - toad.X);
        Run(.6);
        // (for the close look, turned so that the face's right side, where the eye was, is to the camera)
        if (_look == 12) { god.Facing = fa + (_cam is { Length: > 1 } ? _cam[1] : -.9f); sim.GodLookYaw = god.Facing; Run(.1); }
        if (_look == 13)
        {
            Gods.Enter(god);
            Gods.Face(fa, -.12f);
        }
    }
}
