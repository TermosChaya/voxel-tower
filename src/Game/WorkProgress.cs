using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The work the player marked, followed to its end. Over every stroke of a brush or shape laid hangs what it is and how
/// much of it the pawns have done ("Камень · 63 %"), the nearest few to the middle of the view; when the last of it is
/// done the chronicle says so, with a bell. Work taken back again (cancelled, or built over) is let go of quietly.
/// Workers standing about for half a minute are spoken of too, now and then: with nothing marked, what to mark them;
/// with work marked that they cannot get to, why not.
/// </summary>
public partial class WorkProgress : CanvasLayer
{
    sealed class Job
    {
        public string Name;
        public bool Dig;
        // the voxels it marked (packed), so many of them as are looked at again: every one, up to a few thousand
        public int[] V;
        public int Done, Left;
        public Vector3 At;
        public Label Tag;
    }

    const int Sample = 2500, Shown = 6, Least = 12;
    Main _m;
    Control _root;
    readonly List<Job> _jobs = new();
    double _next;

    /// <summary>Works being followed, and how many have been told done since the start (the self-test asks).</summary>
    public int Following => _jobs.Count;
    public int Finished { get; private set; }

    public void Init(Main m)
    {
        _m = m;
        Layer = 2;
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);
    }

    static int Pack(int x, int y, int z) => (y << 20) | (z << 10) | x;

    /// <summary>A stroke or shape just marked: what of it the orders took, followed from now on.</summary>
    public void Track(string name, VoxelList plan, bool dig)
    {
        if (plan == null || plan.Count == 0) return;
        var orders = _m.Sim.Orders;
        var marked = new List<int>();
        double sx = 0, sz = 0; int top = 0;
        int step = Math.Max(1, plan.Count / Sample);
        for (int i = 0; i < plan.Count; i += step)
        {
            int x = plan.X(i), y = plan.Y(i), z = plan.Z(i);
            if (orders.At(x, y, z) == 0) continue;
            marked.Add(Pack(x, y, z));
            sx += x; sz += z; top = Math.Max(top, y);
        }
        if (marked.Count * step < Least) return;
        float cx = (float)(sx / marked.Count), cz = (float)(sz / marked.Count);
        var w = _m.World;
        var tag = Hud.Text("", Ornament.SerifBold, 15, Ornament.Parchment, outline: 6);
        tag.MouseFilter = Control.MouseFilterEnum.Ignore;
        tag.Visible = false;
        _root.AddChild(tag);
        _jobs.Add(new Job { Name = name, Dig = dig, V = marked.ToArray(), At = new Vector3(w.WX(cx), w.WY(top + 6), w.WZ(cz)), Tag = tag, Left = marked.Count });
        // a great many at once: the oldest let go
        if (_jobs.Count > 40) { _jobs[0].Tag.QueueFree(); _jobs.RemoveAt(0); }
        _next = 0;
    }

    public override void _Process(double delta)
    {
        using var prof = Prof.Time("WorkProgress");
        var now = Time.GetTicksMsec() / 1000.0;
        if (now >= _next)
        {
            _next = now + .5;
            Look();
            Idle();
        }
        if (_jobs.Count > 0) Place();
    }

    double _idleSince = -1, _toldIdle = -999;

    /// <summary>Two or more workers about with nothing to do for half a minute: told what to give them, or why they cannot get to what is given.</summary>
    void Idle()
    {
        var sim = _m.Sim;
        if (_m.Scripted || _m.Tutor != null || _m.Paused || sim.Waves.Active) { _idleSince = -1; return; }
        int idle = 0;
        string why = "";
        foreach (var u in sim.Units)
        {
            if (u.Dead || u.Inside || u.T != Kinds.Worker || u.Hold || u.State != UnitState.Idle || u.Duty != Stockpile.None) continue;
            idle++;
            if (why == "" && u.Why != "") why = u.Why;
        }
        if (idle < 2) { _idleSince = -1; return; }
        if (_idleSince < 0) { _idleSince = sim.Time; return; }
        if (sim.Time - _idleSince < 30 || sim.Time - _toldIdle < 240) return;
        _toldIdle = sim.Time;
        if (sim.Orders.Total == 0)
            _m.Hud.Log($"Рабочие без дела ({idle}). Разметьте работу кистями или делянкой ({{ToolGather}}).", Hud.Kind.Warn);
        else if (why != "")
            _m.Hud.Log($"Рабочие стоят без дела ({idle}): {why}.", Hud.Kind.Warn);
    }

    /// <summary>How far each work has got; the finished told and let go, and the taken back let go quietly.</summary>
    void Look()
    {
        var orders = _m.Sim.Orders;
        var w = _m.World;
        for (int j = _jobs.Count - 1; j >= 0; j--)
        {
            var job = _jobs[j];
            int done = 0, left = 0;
            foreach (int p in job.V)
            {
                int x = p & 1023, z = (p >> 10) & 1023, y = p >> 20;
                if (orders.At(x, y, z) != 0) { left++; continue; }
                // the order gone: done if the world is as it was meant to be, else taken back
                if ((w.Get(x, y, z) == 0) == job.Dig) done++;
            }
            job.Done = done; job.Left = left;
            if (left > 0) continue;
            if (done * 2 >= job.V.Length)
            {
                Finished++;
                _m.Hud.Log(job.Dig ? "Выкопано всё, что размечено." : $"Готово: {job.Name.ToLowerInvariant()}.", Hud.Kind.Ok, w.VX(job.At.X), w.VZ(job.At.Z));
                Sound.Ui("done");
            }
            job.Tag.QueueFree();
            _jobs.RemoveAt(j);
        }
    }

    /// <summary>The tags over the works nearest the middle of the view, as the camera sees them (none from someone's eyes, nor over the menu).</summary>
    void Place()
    {
        var cam = GetViewport().GetCamera3D();
        bool hidden = cam == null || _m.FP.On || _m.Hud.MenuOpen || _m.Hud.TitleOpen;
        var mid = GetViewport().GetVisibleRect().Size / 2;
        var order = new List<(float d, Job j, Vector2 at)>();
        foreach (var job in _jobs)
        {
            if (hidden || cam.IsPositionBehind(job.At)) { job.Tag.Visible = false; continue; }
            var at = cam.UnprojectPosition(job.At);
            order.Add(((at - mid).LengthSquared(), job, at));
        }
        order.Sort((a, b) => a.d.CompareTo(b.d));
        for (int i = 0; i < order.Count; i++)
        {
            var (_, job, at) = order[i];
            job.Tag.Visible = i < Shown;
            if (i >= Shown) continue;
            int pct = (int)(100L * job.Done / Math.Max(1, job.V.Length));
            job.Tag.Text = $"{job.Name} · {pct} %";
            job.Tag.ResetSize();
            job.Tag.Position = at - new Vector2(job.Tag.Size.X / 2, job.Tag.Size.Y);
        }
    }
}
