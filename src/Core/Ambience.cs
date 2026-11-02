using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>The kinds of small life about a valley that the simulation does not keep (<see cref="Ambience"/>).</summary>
public enum Critter : byte { Sparrow, Pigeon, Crow, Bat, Butterfly, Bee, Firefly, Fish, Midge, Frog }

/// <summary>One small creature about the view: where it is and which way it looks (voxels, radians), its wings, its colour.</summary>
public sealed class Mote
{
    public Critter Kind;
    public float X, Y, Z, Yaw, Pitch;
    /// <summary>The wings' angle now, radians (up positive, folded far down); a firefly's glow, 0 to 1.</summary>
    public float Wing, Glow;
    /// <summary>Which of its kind's colours.</summary>
    public byte Tint;
    public bool Perched;
    internal float Tx, Ty, Tz, Vx, Vy, Vz, Wait, Beat, Phase, Hx, Hy, Hz;
    internal int Flock = -1;
    internal byte Stage;
}

/// <summary>
/// The small life of the world, about wherever the view is; the simulation keeps none of it and nothing of it changes
/// anything there, and it draws its own dice, so it is the same whatever it does. Flocks of sparrows settle in the trees,
/// the bushes and on the fences, pigeons on the roofs and the walls of a town; anyone coming near, a blast, a fall of
/// stone or a quake puts them up, and they fly off over the valley and settle again further on. Crows come by day to
/// where the dead lie, wheel over them a while, come down to them, and go up and wheel again when someone comes. Bats
/// flit over the water and the roofs at dusk and in the night. Near the view by day butterflies go from flower to flower
/// and bees hum over them; at dusk fireflies glow over the water and the bog, and the frogs croak there all night. A fish
/// jumps now and then where the water is deep, and midges dance in clouds over the pools and the reeds. Rain grounds the
/// insects, a strong wind the midges; the night puts the birds to sleep where they sit. The view draws it all
/// (src/Game/AmbienceView.cs) and the sound hears the calls (src/Game/Sound.Wildlife.cs).
/// </summary>
public sealed class Ambience
{
    const float Pm = VoxelWorld.PerMetre;

    /// <summary>
    /// A flock of birds: its kind and birds; settled on its perches, in the air making for where it settles next, or
    /// (crows) wheeling over where the dead lie; the middle of it, and where it makes for.
    /// </summary>
    sealed class Flock
    {
        public Critter Kind;
        public readonly List<Mote> Birds = new();
        public byte State;
        public float X, Y, Z, Tx, Tz, Lx, Ly, Lz, Timer, Heading, Spin;
        public int Scares;
    }

    const byte Settled = 0, Flying = 1, Wheeling = 2, Leaving = 3, Landing = 4;

    readonly Sim _s;
    readonly Random _r;
    public readonly List<Mote> Motes = new();
    readonly List<Flock> _flocks = new();
    readonly List<(float x, float y, float z, double at)> _carrion = new();
    readonly HashSet<int> _seenDead = new();
    readonly List<(float x, float y, float z, float r)> _noises = new();
    readonly List<(float x, float y, float z)> _flowers = new(), _damp = new();
    float _fx, _fy, _fz, _reach;
    double _t, _nextLook, _nextLeap, _nextCroak, _nextFlowers, _nextDamp;
    bool _small;

    /// <summary>Flocks put up, calls made (a chirp, a coo, a caw, a croak), fish that jumped, since the start (the bench asks).</summary>
    public int TakeOffs, Calls, Caws, Croaks, Leaps;
    /// <summary>A flock took off: its kind, where, how many.</summary>
    public event Action<Critter, float, float, float, int> TookOff;
    /// <summary>A call: a sparrow's chirp, a pigeon's coo, a crow's caw, a frog's croak.</summary>
    public event Action<Critter, float, float, float> Called;
    /// <summary>A fish broke the water going up or coming down: where.</summary>
    public event Action<float, float, float> Splashed;

    public Ambience(Sim sim, int seed = 7)
    {
        _s = sim;
        _r = new Random(seed);
    }

    VoxelWorld W => _s.World;
    float Dark => _s.Clock.Darkness;
    bool Wet => _s.Weather == Weather.Rain;
    float Rnd => (float)_r.NextDouble();
    float Rnd2 => (float)_r.NextDouble() * 2 - 1;

    /// <summary>A loud noise (a blast, stone coming down, a quake, a great one's step): the birds about it go up.</summary>
    public void Startle(float x, float y, float z, float r) => _noises.Add((x, y, z, r));

    /// <summary>How many of a kind are about now (the bench asks).</summary>
    public int Count(Critter k)
    {
        int n = 0;
        foreach (var m in Motes) if (m.Kind == k) n++;
        return n;
    }

    /// <summary>The flocks of a kind settled now, as their middles and how many sit (the bench asks).</summary>
    public List<(float x, float y, float z, int n)> SettledFlocks(Critter k)
    {
        var list = new List<(float, float, float, int)>();
        foreach (var f in _flocks) if (f.Kind == k && f.State == Settled) list.Add((f.X, f.Y, f.Z, f.Birds.Count));
        return list;
    }

    /// <summary>Whether a flock of a kind is in the air (flying or wheeling) within so far of a point, voxels.</summary>
    public bool Aloft(Critter k, float x, float z, float within)
    {
        foreach (var f in _flocks)
            if (f.Kind == k && f.State != Settled && Flat(f.X - x, f.Z - z) < within) return true;
        return false;
    }

    static float Flat(float dx, float dz) => MathF.Sqrt(dx * dx + dz * dz);

    /// <summary>What it was last told and how it stands, for the scenes staged to print.</summary>
    public string Said()
    {
        int perches = 0;
        for (int i = 0; i < 20; i++) if (Perch(Critter.Sparrow, _fx, _fz, _reach * .8f, out _, out _, out _)) perches++;
        return $"about {_fx:0},{_fz:0} reach {_reach / Pm:0} m, small {_small}, dark {Dark:0.00}, flocks {_flocks.Count}, sparrow perches found {perches} of 20";
    }

    /// <summary>
    /// A frame: about the view's middle on the ground (or the eyes), so far round it (voxels), the insects only when the
    /// view is close enough to see them.
    /// </summary>
    public void Tick(float dt, float fx, float fy, float fz, float reach, bool small)
    {
        if (dt <= 0) return;
        dt = MathF.Min(dt, .1f);
        _t += dt;
        _fx = fx; _fy = fy; _fz = fz; _reach = reach; _small = small;
        if (_t >= _nextLook)
        {
            _nextLook = _t + .2;
            Carrion();
            Scare();
            _noises.Clear();
        }
        KeepFlocks();
        foreach (var f in _flocks) FlockTick(f, dt);
        _flocks.RemoveAll(f => f.Birds.Count == 0);
        Bats(dt);
        Flowers(dt);
        Glowing(dt);
        Fish(dt);
        Frogs();
        Midges(dt);
        Motes.RemoveAll(m => m.Stage == 255);
    }

    // ------------------------------------------------------------ the ground under it all

    /// <summary>The top of anything in a column (plants and water too) and what it is; false off the world or the valley.</summary>
    bool TopOf(int x, int z, out int y, out byte m)
    {
        y = 0; m = 0;
        if (!W.InBounds(x, 1, z) || _s.Site.Hex != null && !_s.Site.Inland(x, z, 16)) return false;
        y = W.ColTop(x, z);
        if (y < 2 || y >= W.SY - 3) return false;
        m = W.Get(x, y - 1, z);
        return true;
    }

    static bool Wateryish(byte m) => m is Mat.Water or Mat.Reed or Mat.Lily or Mat.Moss or Mat.Peat;
    static bool Flowerish(byte m) => m is Mat.Flower or Mat.Bloom or Mat.FlowerRed or Mat.FlowerWhite or Mat.FlowerBlue;

    /// <summary>A spot so far round a point, at random in a ring between two distances.</summary>
    (int x, int z) Around(float x, float z, float near, float far)
    {
        float a = Rnd * MathF.Tau, d = near + Rnd * (far - near);
        return ((int)(x + MathF.Cos(a) * d), (int)(z + MathF.Sin(a) * d));
    }

    /// <summary>
    /// Somewhere a bird of a kind sits, near a point: in the crown of a tree or on a bush (a sparrow, on a fence or a
    /// roof's ridge too, and on the turf to peck), on a roof or the top of a wall or the keep (a pigeon), in a tree or on
    /// the ground by the dead (a crow). The top of the column with room over it.
    /// </summary>
    bool Perch(Critter k, float x, float z, float within, out float px, out float py, out float pz, bool ground = false)
    {
        px = py = pz = 0;
        for (int tries = 0; tries < 18; tries++)
        {
            var (cx, cz) = Around(x, z, 0, within);
            if (!TopOf(cx, cz, out int y, out byte m) || W.Get(cx, y, cz) != 0 || W.Get(cx, y + 1, cz) != 0) continue;
            bool ok = k switch
            {
                Critter.Sparrow => Mat.IsLeafy(m) || m == Mat.Wood || Mat.IsRoofing(m) || ground && (Mat.IsGrass(m) || m is Mat.Trodden or Mat.DirtPath),
                Critter.Pigeon => Mat.IsRoofing(m) || m is Mat.Keep or Mat.KeepDark || Mat.IsPlayer[m] && !Mat.IsDecor[m] && !Mat.Passable[m] && y - W.GroundTop(cx, cz) >= 3 * Pm,
                _ => Mat.IsLeafy(m) || ground && !Wateryish(m) && !Mat.IsPlayer[m],
            };
            if (!ok) continue;
            px = cx + .5f; py = y; pz = cz + .5f;
            return true;
        }
        return false;
    }

    // ------------------------------------------------------------ the birds

    /// <summary>
    /// The flocks kept about the view: three of sparrows, one of pigeons where there is a town to sit on, the crows where
    /// the dead lie by day; one too far from the view goes, and another flies in from beyond it.
    /// </summary>
    void KeepFlocks()
    {
        foreach (var f in _flocks)
        {
            float d = Flat(f.X - _fx, f.Z - _fz);
            if (f.State == Leaving ? d > _reach * 2.2f : d > _reach * 1.8f && f.State == Settled) Drop(f);
        }
        int sparrows = 0, pigeons = 0, crows = 0;
        foreach (var f in _flocks)
        {
            if (f.Kind == Critter.Sparrow && f.State != Leaving) sparrows++;
            else if (f.Kind == Critter.Pigeon && f.State != Leaving) pigeons++;
            else if (f.Kind == Critter.Crow && f.State != Leaving) crows++;
        }
        // (a bird flies in only by day: at night the flocks about sleep where they sit)
        bool day = Dark < .6f;
        if (day && sparrows < 3 && _r.NextDouble() < .05) FlyIn(Critter.Sparrow, 5 + _r.Next(5));
        if (day && pigeons < 1 && _r.NextDouble() < .03) FlyIn(Critter.Pigeon, 4 + _r.Next(4));
        var dead = DeadNear();
        if (dead is { } c && Dark < .4f && crows < 1) FlyIn(Critter.Crow, 4 + _r.Next(3), c.x, c.z);
        // the dead gone, or night come: the crows go
        if (dead == null || Dark >= .5f)
            foreach (var f in _flocks)
                if (f.Kind == Critter.Crow && f.State != Leaving) Leave(f);
    }

    /// <summary>A flock flies in from beyond the view toward somewhere to settle near it (the crows toward the dead).</summary>
    void FlyIn(Critter k, int n, float atX = float.NaN, float atZ = float.NaN)
    {
        bool toDead = !float.IsNaN(atX);
        float tx = toDead ? atX : _fx, tz = toDead ? atZ : _fz;
        if (!toDead && !Perch(k, _fx, _fz, _reach * .8f, out tx, out _, out tz)) return;
        float a = Rnd * MathF.Tau;
        var f = new Flock
        {
            Kind = k, State = toDead ? Wheeling : Flying, Tx = tx, Tz = tz,
            Lx = tx + MathF.Cos(a) * _reach * 1.3f, Lz = tz + MathF.Sin(a) * _reach * 1.3f, Timer = 15 + Rnd * 20, Spin = Rnd < .5f ? 1 : -1,
        };
        f.Ly = Ground(f.Lx, f.Lz) + Cruise(k);
        (f.X, f.Y, f.Z) = (f.Lx, f.Ly, f.Lz);
        for (int i = 0; i < n; i++)
        {
            var m = new Mote
            {
                Kind = k, X = f.Lx + Rnd2 * 12, Y = f.Ly + Rnd2 * 4, Z = f.Lz + Rnd2 * 12, Flock = 0, Phase = Rnd * MathF.Tau, Beat = Rnd * MathF.Tau,
                Tint = (byte)_r.Next(k == Critter.Pigeon ? 3 : 2),
            };
            f.Birds.Add(m);
            Motes.Add(m);
        }
        _flocks.Add(f);
    }

    void Drop(Flock f)
    {
        foreach (var m in f.Birds) m.Stage = 255;
        f.Birds.Clear();
    }

    void Leave(Flock f)
    {
        f.State = Leaving;
        float a = MathF.Atan2(f.Z - _fz, f.X - _fx) + Rnd2 * .6f;
        f.Tx = f.X + MathF.Cos(a) * _reach * 3; f.Tz = f.Z + MathF.Sin(a) * _reach * 3;
        Up(f);
    }

    /// <summary>How high a kind flies over the ground, voxels: sparrows low, pigeons and crows higher.</summary>
    float Cruise(Critter k) => k switch { Critter.Sparrow => 7 * Pm, Critter.Pigeon => 12 * Pm, Critter.Crow => 18 * Pm, _ => 6 * Pm };

    float Speed(Critter k) => k switch { Critter.Sparrow => 9 * Pm, Critter.Pigeon => 12 * Pm, Critter.Crow => 9 * Pm, _ => 6 * Pm };

    float Ground(float x, float z)
    {
        int ix = Math.Clamp((int)x, 0, W.SX - 1), iz = Math.Clamp((int)z, 0, W.SZ - 1);
        return W.ColTop(ix, iz);
    }

    /// <summary>The whole flock up off its perches, each bird away with a jump and a beat of the wings.</summary>
    void Up(Flock f)
    {
        f.Lx = f.X; f.Ly = f.Y + 2 * Pm; f.Lz = f.Z;
        foreach (var m in f.Birds)
        {
            m.Perched = false;
            m.Stage = 0;
            m.Vy = (2.5f + Rnd * 2) * Pm;
            m.Vx = Rnd2 * 2 * Pm; m.Vz = Rnd2 * 2 * Pm;
        }
    }

    /// <summary>
    /// Every fifth of a second: a settled flock (or the crows down by the dead) that anyone comes near, or a noise
    /// reaches, goes up — the sparrows within 5 m, pigeons 4, crows 9; it makes for somewhere to settle away from what
    /// put it up (crows wheel over the dead again, and the third time leave them).
    /// </summary>
    void Scare()
    {
        foreach (var f in _flocks)
        {
            if (f.State is not (Settled or Landing)) continue;
            float scare = f.Kind switch { Critter.Sparrow => 5, Critter.Pigeon => 4, _ => 9 } * Pm;
            float awayX = 0, awayZ = 0;
            bool up = false;
            foreach (var u in _s.Units)
            {
                if (u.Dead || u.Inside || u.Airborne != null) continue;
                float dx = f.X - u.X, dz = f.Z - u.Z;
                if (MathF.Abs(dx) > scare || MathF.Abs(dz) > scare || dx * dx + dz * dz > scare * scare || MathF.Abs(f.Y - u.Y) > 8 * Pm) continue;
                up = true; awayX = dx; awayZ = dz;
                break;
            }
            foreach (var (x, y, z, r) in _noises)
            {
                float dx = f.X - x, dz = f.Z - z;
                if (dx * dx + dz * dz > (r + 10 * Pm) * (r + 10 * Pm)) continue;
                up = true; awayX = dx; awayZ = dz;
            }
            if (!up) continue;
            TakeOffs++;
            TookOff?.Invoke(f.Kind, f.X, f.Y, f.Z, f.Birds.Count);
            Up(f);
            if (f.Kind == Critter.Crow && ++f.Scares < 3)
            {
                f.State = Wheeling;
                f.Timer = 10 + Rnd * 15;
                continue;
            }
            if (f.Kind == Critter.Crow) { Leave(f); continue; }
            // away from what put it up, 25 to 70 m, somewhere it can settle (or off beyond the view)
            float len = MathF.Max(.01f, Flat(awayX, awayZ));
            float bx = f.X + awayX / len * 40 * Pm, bz = f.Z + awayZ / len * 40 * Pm;
            // (back toward the view if that would take it out of sight)
            if (Flat(bx - _fx, bz - _fz) > _reach) { bx = (bx + _fx) / 2; bz = (bz + _fz) / 2; }
            if (Perch(f.Kind, bx, bz, 25 * Pm, out float tx, out _, out float tz)) { f.State = Flying; f.Tx = tx; f.Tz = tz; }
            else Leave(f);
        }
    }

    void FlockTick(Flock f, float dt)
    {
        float speed = Speed(f.Kind);
        switch (f.State)
        {
            case Settled:
                SitTick(f, dt);
                // by day now and then the flock moves on to somewhere else near
                if (Dark < .5f && f.Kind != Critter.Crow && (f.Timer -= dt) <= 0)
                {
                    f.Timer = 25 + Rnd * 60;
                    if (Perch(f.Kind, f.X, f.Z, 35 * Pm, out float tx, out _, out float tz) && Flat(tx - f.X, tz - f.Z) > 8 * Pm)
                    {
                        Up(f);
                        f.State = Flying; f.Tx = tx; f.Tz = tz;
                    }
                }
                // and now and then one of it calls
                if (f.Kind != Critter.Crow && Dark < .5f && _r.NextDouble() < dt * .25 && f.Birds.Count > 0) Call(f.Kind, f.Birds[_r.Next(f.Birds.Count)]);
                if (f.Kind == Critter.Crow && _r.NextDouble() < dt * .12 && f.Birds.Count > 0) Call(Critter.Crow, f.Birds[_r.Next(f.Birds.Count)]);
                return;
            case Flying:
            case Leaving:
            {
                // the leader makes for the place over where it settles, at its height over the ground
                float dx = f.Tx - f.Lx, dz = f.Tz - f.Lz, d = Flat(dx, dz);
                if (d > 1)
                {
                    float step = MathF.Min(d, speed * dt);
                    f.Lx += dx / d * step; f.Lz += dz / d * step;
                    f.Heading = MathF.Atan2(dx, dz);
                }
                float want = Ground(f.Lx, f.Lz) + Cruise(f.Kind) * MathF.Min(1, d / (12 * Pm) + .15f);
                f.Ly += (want - f.Ly) * MathF.Min(1, dt * 1.5f);
                (f.X, f.Y, f.Z) = (f.Lx, f.Ly, f.Lz);
                if (f.State == Flying && d < 6 * Pm) Land(f);
                foreach (var m in f.Birds) FollowTick(f, m, dt, speed);
                return;
            }
            case Landing:
                foreach (var m in f.Birds) FollowTick(f, m, dt, speed);
                return;
            case Wheeling:
            {
                // crows: round and round over the dead, high, each at its own place in the ring
                f.Spin += dt * .35f * (f.Spin >= 0 ? 1 : -1);
                float r = 12 * Pm;
                f.Lx = f.Tx + MathF.Cos(f.Spin) * r; f.Lz = f.Tz + MathF.Sin(f.Spin) * r;
                float want = Ground(f.Tx, f.Tz) + Cruise(f.Kind);
                f.Ly += (want - f.Ly) * MathF.Min(1, dt * .8f);
                (f.X, f.Y, f.Z) = (f.Tx, f.Ly, f.Tz);
                for (int i = 0; i < f.Birds.Count; i++)
                {
                    var m = f.Birds[i];
                    float a = f.Spin + i * MathF.Tau / f.Birds.Count;
                    float rr = r * (.8f + .3f * MathF.Sin(m.Phase + (float)_t * .3f));
                    Steer(m, f.Tx + MathF.Cos(a) * rr, f.Ly + MathF.Sin(m.Phase + (float)_t) * 2 * Pm, f.Tz + MathF.Sin(a) * rr, speed, dt, glide: true);
                }
                if (_r.NextDouble() < dt * .5 && f.Birds.Count > 0) Call(Critter.Crow, f.Birds[_r.Next(f.Birds.Count)]);
                if ((f.Timer -= dt) <= 0) Land(f, ground: true);
                return;
            }
        }
    }

    /// <summary>Each bird of a flock in the air keeps its own place about the leader, beating its wings (a sparrow's flight dips between beats).</summary>
    void FollowTick(Flock f, Mote m, float dt, float speed)
    {
        // (one down on its perch already waits there for the rest)
        if (m.Perched) { m.Wing = -1.35f; return; }
        if (m.Stage == 1)
        {
            // coming down to its perch
            Steer(m, m.Tx, m.Ty, m.Tz, speed * .6f, dt, glide: false);
            if (MathF.Abs(m.X - m.Tx) + MathF.Abs(m.Y - m.Ty) + MathF.Abs(m.Z - m.Tz) < 1.2f)
            {
                (m.X, m.Y, m.Z) = (m.Tx, m.Ty, m.Tz);
                m.Perched = true;
                m.Stage = 2;
                m.Vx = m.Vy = m.Vz = 0;
                if (f.Birds.TrueForAll(b => b.Perched)) Settle(f);
            }
            return;
        }
        float sway = 3 * Pm;
        float ox = MathF.Cos(m.Phase) * sway + MathF.Sin((float)_t * .7f + m.Phase) * Pm, oz = MathF.Sin(m.Phase) * sway, oy = MathF.Sin(m.Phase * 2) * Pm;
        if (f.Kind == Critter.Sparrow) oy += MathF.Sin((float)_t * 6 + m.Phase) * .6f * Pm;
        Steer(m, f.Lx + ox, f.Ly + oy, f.Lz + oz, speed * 1.25f, dt, glide: f.Kind == Critter.Crow);
    }

    /// <summary>A bird flies toward a point: its speed eased toward the way there, its heading and its pitch from it, its wings beating.</summary>
    void Steer(Mote m, float x, float y, float z, float speed, float dt, bool glide)
    {
        float dx = x - m.X, dy = y - m.Y, dz = z - m.Z, d = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        float vx = 0, vy = 0, vz = 0;
        if (d > .01f) { float k = MathF.Min(speed, d * 3) / d; vx = dx * k; vy = dy * k; vz = dz * k; }
        float ease = MathF.Min(1, dt * 4);
        m.Vx += (vx - m.Vx) * ease; m.Vy += (vy - m.Vy) * ease; m.Vz += (vz - m.Vz) * ease;
        m.X += m.Vx * dt; m.Y += m.Vy * dt; m.Z += m.Vz * dt;
        float flat = Flat(m.Vx, m.Vz);
        if (flat > .5f) m.Yaw = MathF.Atan2(m.Vx, m.Vz);
        m.Pitch = Math.Clamp(-MathF.Atan2(m.Vy, MathF.Max(1, flat)), -.6f, .6f);
        // the beat: quick for the little ones, slow for a crow, which glides between
        float rate = m.Kind switch { Critter.Sparrow => 15, Critter.Pigeon => 9, Critter.Crow => 5, Critter.Bat => 13, _ => 10 };
        m.Beat += dt * rate;
        bool gliding = glide && MathF.Sin(m.Beat * .13f + m.Phase) > .2f;
        m.Wing = gliding ? .12f : MathF.Sin(m.Beat) * .9f + .1f;
    }

    /// <summary>The flock comes down: each bird to a perch of its own about where it was making for (the crows to the ground by the dead).</summary>
    void Land(Flock f, bool ground = false)
    {
        f.State = Landing;
        int got = 0;
        foreach (var m in f.Birds)
        {
            if (!Perch(f.Kind, f.Tx, f.Tz, (ground ? 4 : 6) * Pm, out float px, out float py, out float pz, ground: ground || f.Kind == Critter.Sparrow && _r.NextDouble() < .3))
            {
                // nowhere to sit: the flock goes on
                continue;
            }
            m.Tx = px; m.Ty = py; m.Tz = pz;
            m.Stage = 1;
            got++;
        }
        if (got == 0) Leave(f);
        else foreach (var m in f.Birds) if (m.Stage != 1) { m.Stage = 1; m.Tx = f.Tx; m.Ty = Ground(f.Tx, f.Tz); m.Tz = f.Tz; }
    }

    void Settle(Flock f)
    {
        f.State = Settled;
        float x = 0, y = 0, z = 0;
        foreach (var m in f.Birds) { x += m.X; y += m.Y; z += m.Z; }
        int n = Math.Max(1, f.Birds.Count);
        (f.X, f.Y, f.Z) = (x / n, y / n, z / n);
        f.Timer = 25 + Rnd * 60;
    }

    /// <summary>A bird on its perch: wings folded, a hop round now and then, a peck, a look about.</summary>
    void SitTick(Flock f, float dt)
    {
        foreach (var m in f.Birds)
        {
            m.Wing = -1.35f;
            m.Wait -= dt;
            if (m.Wait > 0)
            {
                m.Pitch += (0 - m.Pitch) * MathF.Min(1, dt * 6);
                continue;
            }
            m.Wait = .4f + Rnd * (Dark > .6f ? 8 : 2.5f);
            if (Dark > .6f) continue;
            float pick = Rnd;
            if (pick < .4f) m.Yaw += Rnd2 * 1.4f;
            else if (pick < .75f) m.Pitch = .7f;
        }
    }

    void Call(Critter k, Mote m)
    {
        Calls++;
        if (k == Critter.Crow) Caws++;
        Called?.Invoke(k, m.X, m.Y, m.Z);
    }

    // ------------------------------------------------------------ the dead and the crows

    /// <summary>The dead the valley has seen lately (four minutes): a crow's meal, where they fell.</summary>
    void Carrion()
    {
        foreach (var u in _s.Units)
        {
            if (!u.Dead || u.Inside || Kinds.IsEngine(u.T) || u.T.Called || !_seenDead.Add(u.Id)) continue;
            _carrion.Add((u.X, u.Y, u.Z, _t));
        }
        _carrion.RemoveAll(c => _t - c.at > 240);
    }

    /// <summary>Where most of the dead near the view lie: the middle of those within the view's reach and a half.</summary>
    (float x, float z)? DeadNear()
    {
        float x = 0, z = 0;
        int n = 0;
        foreach (var c in _carrion)
            if (Flat(c.x - _fx, c.z - _fz) < _reach * 1.5f) { x += c.x; z += c.z; n++; }
        return n == 0 ? null : (x / n, z / n);
    }

    /// <summary>The dead lying now (the bench asks): where, and since when.</summary>
    public int CarrionCount => _carrion.Count;

    // ------------------------------------------------------------ bats

    /// <summary>At dusk and through the night bats flit about over the water and the roofs near the view, each turning every moment.</summary>
    void Bats(float dt)
    {
        int n = 0;
        foreach (var m in Motes) if (m.Kind == Critter.Bat) n++;
        bool out_ = Dark > .3f && !Wet;
        if (out_ && n < 7 && _r.NextDouble() < dt * 2)
        {
            var (x, z) = Around(_fx, _fz, 0, _reach * .6f);
            float y = Ground(x, z) + (3 + Rnd * 5) * Pm;
            var m = new Mote { Kind = Critter.Bat, X = x, Y = y, Z = z, Hx = x, Hy = y, Hz = z, Phase = Rnd * MathF.Tau };
            Motes.Add(m);
        }
        foreach (var m in Motes)
        {
            if (m.Kind != Critter.Bat) continue;
            if (!out_ || Flat(m.X - _fx, m.Z - _fz) > _reach * 1.5f) { m.Stage = 255; continue; }
            if ((m.Wait -= dt) <= 0)
            {
                m.Wait = .3f + Rnd * .7f;
                m.Tx = m.Hx + Rnd2 * 14 * Pm; m.Tz = m.Hz + Rnd2 * 14 * Pm;
                m.Ty = Ground(m.Tx, m.Tz) + (2.5f + Rnd * 6) * Pm;
            }
            Steer(m, m.Tx, m.Ty, m.Tz, 7 * Pm, dt, glide: false);
        }
    }

    // ------------------------------------------------------------ butterflies and bees

    /// <summary>
    /// Flowers near the view, a few columns looked at each moment and the far ones forgotten; butterflies and bees by
    /// day where there are flowers (none in the rain), a butterfly going flower to flower and sitting on each a while with
    /// its wings opening and closing, a bee hovering over each a moment.
    /// </summary>
    void Flowers(float dt)
    {
        float near = 22 * Pm;
        bool out_ = _small && Dark < .25f && !Wet;
        if (out_ && _t >= _nextFlowers)
        {
            _nextFlowers = _t + .1;
            _flowers.RemoveAll(p => Flat(p.x - _fx, p.z - _fz) > near * 1.3f);
            for (int i = 0; i < 10 && _flowers.Count < 96; i++)
            {
                var (x, z) = Around(_fx, _fz, 0, near);
                if (TopOf(x, z, out int y, out byte m) && Flowerish(m)) _flowers.Add((x + .5f, y, z + .5f));
            }
        }
        int fl = 0, bees = 0;
        foreach (var m in Motes) { if (m.Kind == Critter.Butterfly) fl++; else if (m.Kind == Critter.Bee) bees++; }
        if (out_ && _flowers.Count >= 2)
        {
            if (fl < Math.Min(14, _flowers.Count / 2) && _r.NextDouble() < dt * 3) Motes.Add(NewFlier(Critter.Butterfly, (byte)_r.Next(4)));
            if (bees < Math.Min(10, _flowers.Count / 3) && _r.NextDouble() < dt * 3) Motes.Add(NewFlier(Critter.Bee, 0));
        }
        foreach (var m in Motes)
        {
            if (m.Kind is not (Critter.Butterfly or Critter.Bee)) continue;
            if (!out_ || Flat(m.X - _fx, m.Z - _fz) > near * 1.5f) { m.Stage = 255; continue; }
            bool bee = m.Kind == Critter.Bee;
            if (m.Perched)
            {
                // on its flower: a butterfly's wings open and close slowly; anyone near puts it up
                m.Beat += dt * 2.2f;
                m.Wing = bee ? MathF.Sin(m.Beat * 20) * .4f : .6f + MathF.Sin(m.Beat) * .7f;
                if ((m.Wait -= dt) <= 0 || SomeoneNear(m.X, m.Z, 1.2f * Pm)) { m.Perched = false; Next(m); }
                continue;
            }
            float speed = (bee ? 2.6f : .9f) * Pm;
            float bob = bee ? 0 : MathF.Sin((float)_t * 7 + m.Phase) * .5f;
            m.Beat += dt * (bee ? 60 : 14);
            float dx = m.Tx - m.X, dy = m.Ty + bob - m.Y, dz = m.Tz - m.Z, d = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
            if (d < .4f)
            {
                (m.X, m.Y, m.Z) = (m.Tx, m.Ty, m.Tz);
                m.Perched = true;
                m.Wait = bee ? .6f + Rnd * 1.5f : 2 + Rnd * 5;
                continue;
            }
            float k = MathF.Min(speed, d * 4) / d;
            float jx = bee ? 0 : MathF.Sin((float)_t * 3.1f + m.Phase) * .8f, jz = bee ? 0 : MathF.Cos((float)_t * 2.3f + m.Phase) * .8f;
            m.X += (dx * k + jx) * dt; m.Y += dy * k * dt; m.Z += (dz * k + jz) * dt;
            m.Yaw = MathF.Atan2(dx, dz);
            m.Wing = bee ? MathF.Sin(m.Beat) * .5f : MathF.Sin(m.Beat) * 1.1f;
        }
    }

    Mote NewFlier(Critter k, byte tint)
    {
        var (x, y, z) = _flowers[_r.Next(_flowers.Count)];
        var m = new Mote { Kind = k, X = x + Rnd2 * 3 * Pm, Y = y + (1 + Rnd) * Pm, Z = z + Rnd2 * 3 * Pm, Tint = tint, Phase = Rnd * MathF.Tau };
        Next(m);
        return m;
    }

    /// <summary>The next flower for a butterfly or a bee: one near it (within 6 m) if any, else any near the view.</summary>
    void Next(Mote m)
    {
        if (_flowers.Count == 0) { m.Tx = m.X; m.Ty = m.Y + Pm; m.Tz = m.Z; return; }
        var best = _flowers[_r.Next(_flowers.Count)];
        for (int i = 0; i < 6; i++)
        {
            var p = _flowers[_r.Next(_flowers.Count)];
            if (Flat(p.x - m.X, p.z - m.Z) < 6 * Pm) { best = p; break; }
        }
        m.Tx = best.x + Rnd2 * .3f; m.Ty = best.y + .3f; m.Tz = best.z + Rnd2 * .3f;
    }

    bool SomeoneNear(float x, float z, float r)
    {
        foreach (var u in _s.Units)
        {
            if (u.Dead || u.Inside) continue;
            float dx = u.X - x, dz = u.Z - z;
            if (MathF.Abs(dx) < r && MathF.Abs(dz) < r && dx * dx + dz * dz < r * r) return true;
        }
        return false;
    }

    // ------------------------------------------------------------ fireflies, midges, frogs

    /// <summary>Damp places near the view — open water, reeds, lilies, moss, peat — a few columns looked at each moment.</summary>
    void Damp()
    {
        if (_t < _nextDamp) return;
        _nextDamp = _t + .15;
        float near = 30 * Pm;
        _damp.RemoveAll(p => Flat(p.x - _fx, p.z - _fz) > near * 1.3f);
        for (int i = 0; i < 10 && _damp.Count < 96; i++)
        {
            var (x, z) = Around(_fx, _fz, 0, near);
            if (TopOf(x, z, out int y, out byte m) && Wateryish(m)) _damp.Add((x + .5f, y, z + .5f));
        }
    }

    /// <summary>From dusk on, fireflies drift and glow over the damp places, each blinking in its own time; none in the rain.</summary>
    void Glowing(float dt)
    {
        bool out_ = _small && Dark > .45f && !Wet;
        if (out_ || _s.Clock.Darkness < .85f) Damp();
        int n = 0;
        foreach (var m in Motes) if (m.Kind == Critter.Firefly) n++;
        if (out_ && _damp.Count > 0 && n < Math.Min(36, _damp.Count * 2) && _r.NextDouble() < dt * 6)
        {
            var (x, y, z) = _damp[_r.Next(_damp.Count)];
            float h = y + (.4f + Rnd * 2) * Pm;
            Motes.Add(new Mote { Kind = Critter.Firefly, X = x, Y = h, Z = z, Hx = x, Hy = h, Hz = z, Phase = Rnd * MathF.Tau, Beat = .5f + Rnd * .8f });
        }
        foreach (var m in Motes)
        {
            if (m.Kind != Critter.Firefly) continue;
            if (!out_ || Flat(m.X - _fx, m.Z - _fz) > 40 * Pm) { m.Stage = 255; continue; }
            float t = (float)_t;
            m.X = m.Hx + MathF.Sin(t * .31f + m.Phase) * 1.6f * Pm;
            m.Z = m.Hz + MathF.Cos(t * .23f + m.Phase * 1.3f) * 1.6f * Pm;
            m.Y = m.Hy + MathF.Sin(t * .41f + m.Phase * .7f) * .6f * Pm;
            float s = MathF.Sin(t * m.Beat * MathF.Tau / 3 + m.Phase);
            m.Glow = s > 0 ? MathF.Pow(s, 4) : 0;
        }
    }

    /// <summary>By day and at dusk midges dance in a cloud or two over the damp places (more in a bog); not in the rain or a wind.</summary>
    void Midges(float dt)
    {
        bool bog = _s.Site.Kind is ValleyKind.Bog or ValleyKind.Marsh;
        bool out_ = _small && Dark < .85f && !Wet && _s.Weather != Weather.Wind;
        int swarms = 0;
        foreach (var m in Motes) if (m.Kind == Critter.Midge && m.Tint == 0) swarms++;
        if (out_ && _damp.Count > 0 && swarms < (bog ? 5 : 2) && _r.NextDouble() < dt * .8)
        {
            var (x, y, z) = _damp[_r.Next(_damp.Count)];
            float h = y + (1 + Rnd * 1.2f) * Pm;
            for (int i = 0; i < 22; i++)
                Motes.Add(new Mote { Kind = Critter.Midge, Hx = x, Hy = h, Hz = z, X = x, Y = h, Z = z, Phase = Rnd * MathF.Tau, Beat = .7f + Rnd * 1.6f, Tint = (byte)(i == 0 ? 0 : 1) });
        }
        float t = (float)_t;
        foreach (var m in Motes)
        {
            if (m.Kind != Critter.Midge) continue;
            if (!out_ || Flat(m.Hx - _fx, m.Hz - _fz) > 40 * Pm) { m.Stage = 255; continue; }
            float r = .45f * Pm;
            m.X = m.Hx + MathF.Sin(t * m.Beat * 3 + m.Phase) * r + MathF.Sin(t * .2f + m.Hz) * .5f * Pm;
            m.Y = m.Hy + MathF.Sin(t * m.Beat * 2.3f + m.Phase * 2) * r * .8f;
            m.Z = m.Hz + MathF.Cos(t * m.Beat * 2.7f + m.Phase) * r + MathF.Cos(t * .17f + m.Hx) * .5f * Pm;
        }
    }

    /// <summary>From dusk through the night the frogs croak by the water and in the bog near the view, a few together.</summary>
    void Frogs()
    {
        if (Dark < .5f || _t < _nextCroak) return;
        Damp();
        bool bog = _s.Site.Kind is ValleyKind.Bog or ValleyKind.Marsh;
        _nextCroak = _t + (bog ? .25 : .5) + _r.NextDouble() * (bog ? .8 : 1.6);
        if (_damp.Count == 0) return;
        var (x, y, z) = _damp[_r.Next(_damp.Count)];
        Croaks++;
        Called?.Invoke(Critter.Frog, x, y, z);
    }

    // ------------------------------------------------------------ fish

    /// <summary>
    /// By day and at dusk a fish jumps now and then where the water near the view is three voxels deep and more: up out
    /// of it in an arc a metre or so long and back, a splash where it goes out and one where it comes down.
    /// </summary>
    void Fish(float dt)
    {
        foreach (var m in Motes)
        {
            if (m.Kind != Critter.Fish) continue;
            m.Wait += dt / .75f;
            float k = MathF.Min(1, m.Wait);
            m.X = m.Hx + (m.Tx - m.Hx) * k; m.Z = m.Hz + (m.Tz - m.Hz) * k;
            m.Y = m.Hy + 4 * m.Ty * k * (1 - k);
            m.Pitch = (k - .5f) * 2.4f;
            if (k >= 1)
            {
                m.Stage = 255;
                Splashed?.Invoke(m.X, m.Hy + 1, m.Z);
            }
        }
        if (Dark > .75f || _t < _nextLeap) return;
        _nextLeap = _t + 1.5 + _r.NextDouble() * 4;
        for (int tries = 0; tries < 16; tries++)
        {
            var (x, z) = Around(_fx, _fz, 0, 30 * Pm);
            if (!TopOf(x, z, out int y, out byte m) || m != Mat.Water) continue;
            int deep = 0;
            for (int yy = y - 1; yy > 0 && W.Get(x, yy, z) == Mat.Water && deep < 4; yy--) deep++;
            if (deep < 3) continue;
            float a = Rnd * MathF.Tau, len = 4 + Rnd * 5;
            // (it comes down in the water too)
            if (!TopOf((int)(x + .5f + MathF.Cos(a) * len), (int)(z + .5f + MathF.Sin(a) * len), out _, out byte end) || end != Mat.Water) continue;
            var f = new Mote
            {
                Kind = Critter.Fish, Hx = x + .5f, Hy = y - 1, Hz = z + .5f, Tx = x + .5f + MathF.Cos(a) * len, Tz = z + .5f + MathF.Sin(a) * len,
                Ty = 3 + Rnd * 4, Yaw = MathF.Atan2(MathF.Cos(a), MathF.Sin(a)), Tint = (byte)_r.Next(2),
            };
            (f.X, f.Y, f.Z) = (f.Hx, f.Hy, f.Hz);
            Motes.Add(f);
            Leaps++;
            Splashed?.Invoke(f.Hx, y, f.Hz);
            return;
        }
    }
}
