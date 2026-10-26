using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// The looks of the town's buildings (Looks.cs). Each kind is put up in one of two or three looks of its own, so that no
/// two kinds stand as the same box and a town of them reads from afar: the forge a stone house with an open canopy on
/// posts over its hearth and anvil, the market a timber-framed store with a market hall on posts beside it, the town
/// hall with a belfry on its ridge, the alchemists with a squat round tower, the kennel and the stables long and low
/// under steep thatch with their runs and paddocks fenced. A look is its walls (plain, a stone ground floor under timber
/// framing, framing, daub between timber corners, rough big stones), its roof's form (gable, hip, half-hip, steep) and
/// covering, what stands on its ridge, its length, and the parts built on to it outside its walls: canopies, sheds,
/// a porch, a fenced yard, a tower. All of it is laid by the workers from the stores; the room inside the walls is the
/// building's room as before (closed, furnished, it works), the parts outside are open and are no part of it. The
/// player picks the look while placing ({Form}); by default it goes by where the building stands.
/// </summary>
public sealed partial class Sim
{
    static Annex CanopyOn(Flank at, int depth, int span = 0, int height = 17, bool gabled = false, byte cover = 0, params Prop[] holds) =>
        new() { Kind = AnnexKind.Canopy, At = at, Depth = depth, Span = span, Height = height, Gabled = gabled, Covering = cover, Holds = holds };
    static Annex ShedOn(Flank at, int depth, int span = 0, byte cover = 0, params Prop[] holds) =>
        new() { Kind = AnnexKind.Shed, At = at, Depth = depth, Span = span, Height = 16, Covering = cover, Holds = holds };
    static Annex PorchOn(byte cover = 0) => new() { Kind = AnnexKind.Porch, At = Flank.Front, Depth = 12, Span = 26, Height = 25, Covering = cover };
    static Annex YardOn(Flank at, int depth, int span = 0, params Prop[] holds) =>
        new() { Kind = AnnexKind.Yard, At = at, Depth = depth, Span = span, Height = 9, Holds = holds };
    static Annex TowerOn(Flank at, int size, int height, bool round, byte cover = 0, bool atEnd = false) =>
        new() { Kind = AnnexKind.Tower, At = at, Depth = size, Span = size, Height = height, Round = round, Covering = cover, AtEnd = atEnd };
    static Annex CourtOn(Flank at, int depth, int span = 0, params Prop[] holds) =>
        new() { Kind = AnnexKind.Yard, At = at, Depth = depth, Span = span, Height = 16, Walled = true, Holds = holds };
    static Annex WingOn(Flank at, int depth, int span, int height = 22, bool atEnd = true, byte cover = 0, params Prop[] holds) =>
        new() { Kind = AnnexKind.Wing, At = at, Depth = depth, Span = span, Height = height, AtEnd = atEnd, Covering = cover, Holds = holds };
    static Look Styled(string name, WallStyle walls, RoofForm roof, byte cover = 0, RidgeTop crown = RidgeTop.None, int dd = 0, int dw = 0, int dh = 0, params Annex[] annexes) =>
        new() { Name = name, Walls = walls, Roof = roof, Covering = cover, Crown = crown, DD = dd, DW = dw, DH = dh, Annexes = annexes };

    /// <summary>
    /// Each kind's looks, the first its own as most often seen. A kind's size (Sim.Town.cs: across, along, how high its
    /// first walls stand) gives its body; the looks give its plan with what is built on to it — an L with a wing, a
    /// courtyard, a tower at a corner, a gallery along its long side, a market hall wider than the shop it stands by.
    /// </summary>
    static readonly Dictionary<string, Look[]> _looks = new()
    {
        ["hall"] = new[]
        {
            Styled("с колокольней", WallStyle.StoneBase, RoofForm.HalfHip, crown: RidgeTop.Belfry, annexes: new[] { PorchOn(), WingOn(Flank.Front, 36, 24, 24) }),
            Styled("каменная со шпилем", WallStyle.Plain, RoofForm.Hip, Mat.Slate, RidgeTop.Spire, annexes: new[] { PorchOn(Mat.Slate), CourtOn(Flank.Right, 44, 0, Prop.Casks, Prop.Crates) }),
            Styled("с башней", WallStyle.Plaster, RoofForm.Gable, dd: -8, annexes: new[] { PorchOn(), TowerOn(Flank.Left, 23, 44, false, Mat.Slate, atEnd: true) }),
        },
        ["market"] = new[]
        {
            Styled("крытый рынок", WallStyle.Frame, RoofForm.Gable, annexes: CanopyOn(Flank.Right, 60, 56, gabled: true, height: 20, holds: new[] { Prop.Counter, Prop.Crates, Prop.Casks, Prop.Counter })),
            Styled("торговые ряды", WallStyle.Plain, RoofForm.Hip, annexes: new[] { ShedOn(Flank.Left, 24, holds: new[] { Prop.Counter, Prop.Casks }), ShedOn(Flank.Right, 24, holds: new[] { Prop.Counter, Prop.Crates }) }),
            Styled("лавка с навесом", WallStyle.StoneBase, RoofForm.HalfHip, Mat.Shingle, annexes: new[] { PorchOn(Mat.Shingle), CanopyOn(Flank.Left, 36, holds: new[] { Prop.Counter, Prop.Crates }) }),
        },
        ["smithy"] = new[]
        {
            Styled("кузня под навесом", WallStyle.Plain, RoofForm.Gable, annexes: CanopyOn(Flank.Right, 48, 44, gabled: true, height: 18, cover: Mat.Thatch, holds: new[] { Prop.Forge, Prop.Anvil, Prop.Casks })),
            Styled("кузня с горном во дворе", WallStyle.StoneBase, RoofForm.Hip, annexes: new[] { ShedOn(Flank.Left, 28, cover: Mat.Shingle, holds: new[] { Prop.Forge, Prop.Anvil }), CourtOn(Flank.Right, 36, 0, Prop.Ingots, Prop.Blocks) }),
            Styled("кузня-фахверк", WallStyle.Frame, RoofForm.HalfHip, Mat.Tile, RidgeTop.Louvre, dd: 16, annexes: CanopyOn(Flank.Left, 32, 40, holds: new[] { Prop.Anvil, Prop.Rack })),
        },
        ["carpentry"] = new[]
        {
            Styled("плотницкий сарай", WallStyle.Plain, RoofForm.Gable, annexes: CanopyOn(Flank.Right, 52, 48, holds: new[] { Prop.Sawhorse, Prop.Logs, Prop.Logs })),
            Styled("столярня", WallStyle.Frame, RoofForm.HalfHip, annexes: new[] { ShedOn(Flank.Back, 24, holds: new[] { Prop.Logs, Prop.Logs }), YardOn(Flank.Left, 56, 64, holds: new[] { Prop.Sawhorse, Prop.Logs }) }),
        },
        ["masons"] = new[]
        {
            Styled("двор каменщиков", WallStyle.Plain, RoofForm.Gable, annexes: CourtOn(Flank.Right, 64, 64, Prop.Blocks, Prop.Blocks, Prop.Workbench)),
            Styled("каменотёсня под навесом", WallStyle.Rough, RoofForm.Hip, annexes: new[] { CanopyOn(Flank.Left, 40, 48, holds: new[] { Prop.Blocks, Prop.Workbench }), YardOn(Flank.Back, 32, holds: new[] { Prop.Blocks }) }),
        },
        ["drill"] = new[]
        {
            Styled("учебный двор", WallStyle.Frame, RoofForm.Hip, annexes: YardOn(Flank.Right, 72, 72, holds: new[] { Prop.Dummy, Prop.Dummy, Prop.Target })),
            Styled("плац с крыльцом", WallStyle.Plain, RoofForm.HalfHip, dd: 16, annexes: new[] { PorchOn(), YardOn(Flank.Left, 56, 64, holds: new[] { Prop.Dummy, Prop.Rack, Prop.Dummy }) }),
        },
        ["infirmary"] = new[]
        {
            Styled("лазарет с галереей", WallStyle.Plaster, RoofForm.Gable, annexes: new[] { PorchOn(), CanopyOn(Flank.Back, 20, holds: new[] { Prop.Bunk, Prop.Bunk, Prop.Bunk }) }),
            Styled("больничная изба", WallStyle.Plain, RoofForm.Hip, dd: -24, annexes: new[] { WingOn(Flank.Front, 36, 24, 24, holds: new[] { Prop.Bunk }), CourtOn(Flank.Right, 40, 0, Prop.Casks, Prop.Crates) }),
        },
        ["traps"] = new[]
        {
            Styled("ловчий двор", WallStyle.Plain, RoofForm.HalfHip, annexes: CanopyOn(Flank.Right, 40, 44, holds: new[] { Prop.Hides, Prop.Hides })),
            Styled("сторожка ловчих", WallStyle.Frame, RoofForm.Steep, Mat.Thatch, dd: -8, annexes: YardOn(Flank.Left, 48, 48, holds: new[] { Prop.Kennel, Prop.Hides })),
        },
        ["barracks"] = new[]
        {
            Styled("казарма", WallStyle.Plain, RoofForm.Hip, annexes: YardOn(Flank.Left, 48, 56, holds: new[] { Prop.Rack, Prop.Rack, Prop.Target })),
            Styled("казарма с караульней", WallStyle.StoneBase, RoofForm.Gable, dd: -16, annexes: new[] { PorchOn(), TowerOn(Flank.Left, 23, 44, false, atEnd: true) }),
            Styled("казарма покоем", WallStyle.Rough, RoofForm.HalfHip, Mat.Slate, dd: -24, annexes: new[] { WingOn(Flank.Front, 44, 28, 26), CourtOn(Flank.Right, 44, 0, Prop.Rack, Prop.Target) }),
        },
        ["knights"] = new[]
        {
            Styled("рыцарский двор", WallStyle.StoneBase, RoofForm.HalfHip, crown: RidgeTop.Spire, annexes: CourtOn(Flank.Right, 64, 56, Prop.Rack, Prop.Stall)),
            Styled("рыцарский дом с башней", WallStyle.Plain, RoofForm.Hip, Mat.Tile, annexes: new[] { PorchOn(Mat.Tile), TowerOn(Flank.Left, 23, 44, true, Mat.Tile, atEnd: true) }),
        },
        ["range"] = new[]
        {
            Styled("стрельбище", WallStyle.Plain, RoofForm.Gable, annexes: new[] { ShedOn(Flank.Right, 24, holds: new[] { Prop.Rack }), YardOn(Flank.Left, 80, 40, holds: new[] { Prop.Target, Prop.Target }) }),
            Styled("стрелковый двор", WallStyle.Frame, RoofForm.Hip, dd: -16, annexes: YardOn(Flank.Left, 88, 48, holds: new[] { Prop.Target, Prop.Target })),
        },
        ["lab"] = new[]
        {
            Styled("башня алхимиков", WallStyle.Plain, RoofForm.Hip, crown: RidgeTop.Louvre, annexes: TowerOn(Flank.Right, 25, 72, true, Mat.Slate)),
            Styled("лаборатория", WallStyle.StoneBase, RoofForm.HalfHip, Mat.Slate, dd: 16, annexes: new[] { TowerOn(Flank.Left, 21, 44, false, Mat.Slate, atEnd: true), ShedOn(Flank.Back, 24, holds: new[] { Prop.Still, Prop.Casks }) }),
        },
        ["kennel"] = new[]
        {
            Styled("псарня", WallStyle.Plain, RoofForm.Steep, annexes: YardOn(Flank.Right, 40, 56, holds: new[] { Prop.Kennel, Prop.Kennel, Prop.Kennel })),
            Styled("псарня с вольерами", WallStyle.Frame, RoofForm.Gable, dd: -24, annexes: new[] { YardOn(Flank.Left, 36, 40, holds: new[] { Prop.Kennel }), YardOn(Flank.Right, 36, 40, holds: new[] { Prop.Kennel }) }),
        },
        ["stables"] = new[]
        {
            Styled("конюшня", WallStyle.Plain, RoofForm.Gable, crown: RidgeTop.Louvre, annexes: YardOn(Flank.Left, 56, 64, holds: new[] { Prop.Hay, Prop.Casks, Prop.Hay })),
            Styled("конюшня с сеновалом", WallStyle.StoneBase, RoofForm.HalfHip, dd: -24, annexes: new[] { ShedOn(Flank.Back, 28, holds: new[] { Prop.Hay, Prop.Hay }), WingOn(Flank.Right, 40, 37, 26, atEnd: false, holds: new[] { Prop.Hay }) }),
        },
        ["trolls"] = new[]
        {
            Styled("логово", WallStyle.Rough, RoofForm.Steep, dw: -5, annexes: YardOn(Flank.Right, 40, 56, holds: new[] { Prop.Blocks, Prop.Seat, Prop.Blocks })),
            Styled("нора под навесом", WallStyle.Rough, RoofForm.Hip, Mat.Slate, dd: -16, annexes: CanopyOn(Flank.Left, 36, 56, holds: new[] { Prop.Seat, Prop.Blocks })),
        },
        ["giants"] = new[]
        {
            Styled("чертог", WallStyle.Rough, RoofForm.Steep, annexes: new[] { PorchOn(), CourtOn(Flank.Right, 48, 56, Prop.Blocks, Prop.Seat) }),
            Styled("чертог со стоячими камнями", WallStyle.Plain, RoofForm.Hip, Mat.Slate, dh: -4, annexes: YardOn(Flank.Right, 56, 64, holds: new[] { Prop.Blocks, Prop.Blocks })),
        },
        ["workshop"] = new[]
        {
            Styled("амбар-мастерская", WallStyle.Plain, RoofForm.Steep, annexes: CanopyOn(Flank.Right, 44, 48, gabled: true, height: 24, holds: new[] { Prop.Workbench, Prop.Blocks })),
            Styled("мастерская с двором", WallStyle.Frame, RoofForm.Gable, dh: -6, annexes: YardOn(Flank.Left, 56, 64, holds: new[] { Prop.Blocks, Prop.Workbench, Prop.Logs })),
        },
        ["mine"] = new[]
        {
            Styled("рудокопня", WallStyle.Plain, RoofForm.Gable, annexes: ShedOn(Flank.Right, 24, holds: new[] { Prop.Ingots, Prop.Crates })),
            Styled("рудокопня с горном", WallStyle.StoneBase, RoofForm.Hip, annexes: CanopyOn(Flank.Left, 28, holds: new[] { Prop.Forge, Prop.Ingots })),
        },
    };

    /// <summary>The looks a kind of building is put up in.</summary>
    public static Look[] LooksOf(TownKind k) => k != null && _looks.TryGetValue(k.Key, out var l) ? l : Array.Empty<Look>();

    /// <summary>The look a building put up round a column has unless the player picks one: by where it stands, so that it is not always the first.</summary>
    public static int DefaultLook(TownKind k, int cx, int cz)
    {
        int n = LooksOf(k).Length;
        return n == 0 ? 0 : Math.Min(n - 1, (int)(Hash.H3(cx >> 6, 17, cz >> 6) * n));
    }

    /// <summary>A look of a kind by its number (wrapped), or null for a kind with none.</summary>
    public static Look LookOf(TownKind k, int variant)
    {
        var l = LooksOf(k);
        return l.Length == 0 ? null : l[((variant % l.Length) + l.Length) % l.Length];
    }

    // ------------------------------------------------------------ what is built on to a building

    /// <summary>
    /// The parts a look builds on to a building put up on a rectangle with its door here, each where it has room: into
    /// the plan, and what holds as an arch into <paramref name="arched"/>. What has no room is left out, and why goes
    /// into <paramref name="left"/> («навес: мешает дом»).
    /// </summary>
    void Annexes(Look look, byte covering, byte wallMat, (int x0, int z0, int x1, int z1) rect, Shapes.HouseDoor door, int shared, VoxelList plan, VoxelList arched, List<string> left)
    {
        if (look == null) return;
        var taken = TakenBesides(rect.x0, rect.z0, rect.x1, rect.z1);
        int floor = door.Y - 8, front = door.Side;
        int back = front ^ 1;
        // looking out of the door: its right is along the front in the direction (−nz, nx)
        int right = Shapes.SideOf(-door.NZ, door.NX), leftSide = right ^ 1;
        // what the building's own plan lays already (its eaves and its gables' overhang): no part lays it again
        var laid = new HashSet<long>();
        for (int i = 0; i < plan.Count; i++) laid.Add(Key(plan.X(i), plan.Y(i), plan.Z(i)));
        foreach (var a in look.Annexes)
        {
            int side = a.At switch { Flank.Front => front, Flank.Back => back, Flank.Right => right, _ => leftSide };
            string what = a.Kind switch { AnnexKind.Canopy => "навес", AnnexKind.Shed => "сарай", AnnexKind.Porch => "крыльцо", AnnexKind.Yard => "двор", AnnexKind.Wing => "крыло", _ => "башня" };
            if ((shared & 1 << side) != 0) { left.Add($"{what}: стена общая с соседом"); continue; }
            var part = new VoxelList();
            var partArched = new VoxelList();
            string why = Annex(a, side, a.Covering != 0 ? a.Covering : covering == 0 ? Mat.Wood : covering, wallMat, rect, door, floor, taken, part, partArched, laid);
            if (why != null) { left.Add($"{what}: {why}"); continue; }
            for (int i = 0; i < part.Count; i++) { plan.Add(part.X(i), part.Y(i), part.Z(i), part.M(i)); laid.Add(Key(part.X(i), part.Y(i), part.Z(i))); }
            for (int i = 0; i < partArched.Count; i++) arched.Add(partArched.X(i), partArched.Y(i), partArched.Z(i), partArched.M(i));
        }
    }

    static long Key(int x, int y, int z) => ((long)y << 40) | ((long)z << 20) | (uint)x;

    /// <summary>
    /// One part built on to a building by its side: its ground looked over first (in the valley, about level, nothing
    /// built, no other house, no water), then laid out. Why not, or null.
    /// </summary>
    string Annex(Annex a, int side, byte covering, byte wallMat, (int x0, int z0, int x1, int z1) rect, Shapes.HouseDoor door, int floor, Func<int, int, int, bool> taken, VoxelList L, VoxelList arched, HashSet<long> laid)
    {
        var (sx, sz) = Shapes.SideNormal(side);
        bool alongX = sz != 0;
        int wall = side switch { 0 => rect.z0, 1 => rect.z1, 2 => rect.x0, _ => rect.x1 };
        int p0 = alongX ? rect.x0 : rect.z0, p1 = alongX ? rect.x1 : rect.z1;
        // the porch over the door's middle; a tower or a wing over the wall's, or flush with its end; the rest along all
        // of it or as much as it asks
        int pm = a.Kind == AnnexKind.Porch ? (alongX ? door.X : door.Z) : a.AtEnd && a.Span > 0 ? p0 + a.Span / 2 : (p0 + p1) / 2;
        int pa = a.Span > 0 ? pm - a.Span / 2 : p0, pb = a.Span > 0 ? pa + a.Span - 1 : p1;
        // what stands by an end and is wider than it reaches out behind the building, not before its front: it would
        // stand in the street before the door, where the things of its trade and those keeping them are
        if (a.Span > p1 - p0 + 1 && door.Side != side && door.Side != (side ^ 1) && a.Kind != AnnexKind.Porch)
        {
            bool frontAtP0 = door.Side is 0 or 2;
            if (frontAtP0) { pa = p0; pb = p0 + a.Span - 1; } else { pb = p1; pa = p1 - a.Span + 1; }
        }
        // the building's first walls: how high they stand over its floor
        int high = door.Top - floor;
        int gap = a.Kind == AnnexKind.Tower ? Shapes.Eaves + 1 : 0;
        int depth = a.Depth;
        (int x, int z) At(int p, int t) => alongX ? (p, wall + sz * t) : (wall + sx * t, p);
        // the ground it takes, a little round it
        for (int t = 1; t <= gap + depth + 1; t++)
            for (int p = pa - 1; p <= pb + 1; p++)
            {
                var (x, z) = At(p, t);
                if (!World.InBounds(x, floor, z) || !World.OnLand(x, z) || Site.Forbidden(x, z)) return "здесь край долины";
                int g = World.SurfAt(x, z);
                if (World.Get(x, g - 1, z) is Mat.Water or Mat.Acid or Mat.Lava) return "там вода";
                // (the ground may rise a little into it, its posts and fence set on it; no more)
                if (g > floor + 6 || g < floor - 12) return "там неровно";
                if (taken(x, floor + 2, z) || BuildingAt(x, z) is { } other && !(other.X0 == rect.x0 && other.Z0 == rect.z0)) return "мешает дом";
                for (int y = g; y < floor + 30; y++)
                {
                    byte m = World.Get(x, y, z);
                    if (m != Mat.Air && !Brushes.Free(m) && !Mat.IsVeg[m]) return "мешает постройка";
                }
            }
        void Put(int p, int y, int t, byte m, bool arch = false)
        {
            var (x, z) = At(p, t);
            if (!World.InBounds(x, y, z) || Site.Forbidden(x, z) || !Brushes.Free(World.Get(x, y, z)) || laid.Contains(Key(x, y, z))) return;
            laid.Add(Key(x, y, z));
            L.Add(x, y, z, m);
            if (arch) arched.Add(x, y, z, m);
        }
        // a post from the ground up to a height over the floor
        void Post(int p, int t, int top, byte m = Mat.Wood)
        {
            var (x, z) = At(p, t);
            for (int y = Math.Min(World.SurfAt(x, z), floor); y <= floor + top; y++) Put(p, y, t, m);
        }
        switch (a.Kind)
        {
            case AnnexKind.Canopy or AnnexKind.Shed:
            {
                int h = a.Height;
                // posts a voxel in from its outer corners and every two and a half metres between, two thick
                var posts = new List<int> { pa, pb - 1 };
                for (int p = pa + 20; p < pb - 12; p += 20) posts.Add(p);
                foreach (int p in posts) for (int dp = 0; dp < 2; dp++) for (int dt = 0; dt < 2; dt++) Post(p + dp, depth - 1 + dt, h);
                // and against the wall, at its ends (the roof's edge rests on beams from them)
                foreach (int p in new[] { pa, pb - 1 }) for (int dp = 0; dp < 2; dp++) Post(p + dp, 1, h);
                // the beams: along the outer posts, and out from the wall at either end
                for (int p = pa; p <= pb; p++) for (int dt = 0; dt < 2; dt++) Put(p, floor + h, depth - 1 + dt, Mat.Wood, true);
                for (int t = 1; t <= depth; t++) { Put(pa, floor + h, t, Mat.Wood, true); Put(pb, floor + h, t, Mat.Wood, true); }
                if (a.Gabled)
                {
                    // two slopes falling to the ends, the ridge running out from the wall, the gable at the far end open
                    // but for its tie beam and king post
                    int e = Shapes.Eaves, cmin = pa - e, cmax = pb + e, half = (cmax - cmin) / 2, K = half / 2;
                    for (int t = 1; t <= depth + e; t++)
                        for (int p = cmin; p <= cmax; p++)
                        {
                            int k = Math.Min(Math.Min(p - cmin, cmax - p) / 2, K);
                            Put(p, floor + h + 1 + k, t, k == K || k == 0 ? Mat.Wood : covering, true);
                        }
                    for (int y = floor + h + 1; y < floor + h + 1 + K; y++) Put((pa + pb) / 2, y, depth, Mat.Wood, true);
                }
                else
                {
                    // one slope from under the building's eaves down past the outer posts
                    int e = 2, rise = Math.Max(1, high - 2 - h);
                    int pitch = Math.Max(2, (depth + e + rise - 1) / rise);
                    for (int t = 1; t <= depth + e; t++)
                        for (int p = pa - 1; p <= pb + 1; p++)
                            Put(p, floor + h + 1 + (depth + e - t) / pitch, t, t > depth || p < pa || p > pb ? Mat.Wood : covering, true);
                }
                if (a.Kind == AnnexKind.Shed)
                {
                    // the shed's far side and its end toward the door's side boarded up to the beams
                    for (int p = pa; p <= pb; p++) for (int y = floor; y < floor + h; y++) Put(p, y, depth + 1, Mat.Wood);
                    int endP = a.At == Flank.Left ? pa : pb;
                    for (int t = 1; t <= depth; t++) for (int y = floor; y < floor + h; y++) Put(endP, y, t, Mat.Wood);
                }
                Holds(a, pa + 3, pb - 3, 3, depth - 3, At, L);
                return null;
            }
            case AnnexKind.Porch:
            {
                // two posts out by the door, a beam across on them, a short slope from under the eaves over it
                int h = a.Height;
                foreach (int p in new[] { pa, pb - 1 }) for (int dp = 0; dp < 2; dp++) for (int dt = 0; dt < 2; dt++) Post(p + dp, depth - 1 + dt, h);
                for (int p = pa; p <= pb; p++) for (int dt = 0; dt < 2; dt++) Put(p, floor + h, depth - 1 + dt, Mat.Wood, true);
                for (int t = 1; t <= depth + 1; t++)
                    for (int p = pa - 1; p <= pb + 1; p++)
                        Put(p, floor + h + 1 + (depth + 1 - t) / 6, t, t > depth || p < pa || p > pb ? Mat.Wood : covering, true);
                return null;
            }
            case AnnexKind.Yard when a.Walled:
            {
                // a courtyard: a wall of stone two voxels thick on three sides, two metres high, capped
                // with logs, a gateway's gap in the middle of the far one
                int gm = (pa + pb) / 2;
                void Wall(int p, int t)
                {
                    if (t >= depth - 1 && Math.Abs(p - gm) <= 8) return;
                    var (x, z) = At(p, t);
                    for (int y = Math.Min(World.SurfAt(x, z), floor) - 1; y < floor + a.Height; y++)
                        Put(p, y, t, y == floor + a.Height - 1 ? Mat.Wood : Shapes.Lay(Mat.Stone, x, y, z));
                }
                for (int p = pa; p <= pb; p++) for (int t = depth - 1; t <= depth; t++) Wall(p, t);
                for (int t = 1; t < depth - 1; t++) for (int dp = 0; dp < 2; dp++) { Wall(pa + dp, t); Wall(pb - dp, t); }
                Holds(a, pa + 5, pb - 5, 4, depth - 6, At, L);
                return null;
            }
            case AnnexKind.Yard:
            {
                // a fence of posts and two rails on three sides, a gate's gap in the middle of the far one
                int gm = (pa + pb) / 2;
                void Fence(int p, int t, bool post)
                {
                    if (t == depth && Math.Abs(p - gm) <= 6) return;
                    if (post) Post(p, t, a.Height);
                    else { Put(p, floor + 4, t, Mat.Wood); Put(p, floor + a.Height - 1, t, Mat.Wood); }
                }
                for (int p = pa; p <= pb; p++) Fence(p, depth, (p - pa) % 6 == 0 || p == pb || Math.Abs(p - gm) == 7);
                for (int t = 1; t < depth; t++) { Fence(pa, t, (depth - t) % 6 == 0 || t == 1); Fence(pb, t, (depth - t) % 6 == 0 || t == 1); }
                Holds(a, pa + 4, pb - 4, 4, depth - 6, At, L);
                return null;
            }
            case AnnexKind.Tower:
            {
                // a tower a little out from the wall, joined to it low down; three voxels of masonry about a hollow,
                // slits for windows, a roof of the covering coming to a point, a finial on it
                int r = depth / 2, tc = gap + r + 1, h = a.Height;
                bool In(int dp, int dt, int rr) => a.Round ? dp * dp + dt * dt <= (rr + .5f) * (rr + .5f) : Math.Max(Math.Abs(dp), Math.Abs(dt)) <= rr;
                for (int dt = -r; dt <= r; dt++)
                    for (int dp = -r; dp <= r; dp++)
                    {
                        if (!In(dp, dt, r) || In(dp, dt, r - 3)) continue;
                        var (x, z) = At(pm + dp, tc + dt);
                        bool slit = (dp == 0 || dt == 0) && !In(dp, dt, r - 1);
                        for (int y = Math.Min(World.SurfAt(x, z), floor) - 1; y <= floor + h; y++)
                            Put(pm + dp, y, tc + dt, slit && (y - floor) % 12 is >= 8 and <= 10 && y - floor > 6 ? Mat.Window : Shapes.Lay(Mat.Stone, x, y, z));
                    }
                // the neck joining it to the building's wall, under the building's eaves
                for (int t = 1; t < tc - r + 1; t++)
                    for (int dp = -3; dp <= 3; dp++)
                    {
                        var (x, z) = At(pm + dp, t);
                        for (int y = Math.Min(World.SurfAt(x, z), floor) - 1; y < floor + Math.Min(h - 6, high - 2); y++) Put(pm + dp, y, t, Shapes.Lay(Mat.Stone, x, y, z));
                    }
                // the roof: eaves out a voxel, then a course in for every one up
                for (int k = 0; k <= r + 1; k++)
                    for (int dt = -(r + 1); dt <= r + 1; dt++)
                        for (int dp = -(r + 1); dp <= r + 1; dp++)
                        {
                            int rr = r + 1 - k;
                            if (!In(dp, dt, rr) || rr > 0 && In(dp, dt, rr - 1)) continue;
                            Put(pm + dp, floor + h + 1 + k, tc + dt, k == 0 ? Mat.Wood : covering, true);
                        }
                Put(pm, floor + h + r + 3, tc, Mat.Wood, true);
                Put(pm, floor + h + r + 4, tc, Mat.Bell, true);
                return null;
            }
            case AnnexKind.Wing:
            {
                // walls of the building's material half a metre thick on its two sides and its far end, up to its eaves;
                // a wide way in at the far end under a lintel of logs; a roof of two slopes of the covering, its ridge
                // running out square from the building (so that its plan is an L or a T), the far gable going up under
                // it; the near end is the building's own wall
                const int Tw = HouseWallOf;
                int h = a.Height, way = 10;
                byte Stone(int p, int y, int t) { var (x, z) = At(p, t); return Shapes.Lay(wallMat, x, y, z); }
                void Up(int p, int t)
                {
                    var (x, z) = At(p, t);
                    bool doorway = t > depth - Tw && Math.Abs(p - pm) <= way;
                    for (int y = Math.Min(World.SurfAt(x, z), floor) - 1; y < floor + h; y++)
                    {
                        if (doorway && y >= floor && y < floor + 20) continue;
                        Put(p, y, t, doorway || y == floor + h - 1 ? Mat.Wood : Stone(p, y, t), doorway && y >= floor + 20);
                    }
                }
                for (int t = 1; t <= depth; t++)
                    for (int p = pa; p <= pb; p++)
                        if (p - pa < Tw || pb - p < Tw || depth - t < Tw) Up(p, t);
                int e = Shapes.Eaves, cmin = pa - e, cmax = pb + e, K = (cmax - cmin) / 2 / 2;
                for (int t = 1; t <= depth + e; t++)
                    for (int p = cmin; p <= cmax; p++)
                    {
                        int k = Math.Min(Math.Min(p - cmin, cmax - p) / 2, K), y = floor + h + k;
                        // (not up through the building's own eaves over its wall)
                        if (t <= e && y >= floor + high) continue;
                        Put(p, y, t, k == K || k == 0 ? Mat.Wood : covering, true);
                        // the far gable under the slope
                        if (t > depth - Tw && t <= depth && p >= pa && p <= pb)
                            for (int yy = floor + h; yy < y; yy++) Put(p, yy, t, Stone(p, yy, t), true);
                    }
                Holds(a, pa + Tw + 2, pb - Tw - 2, Tw + 2, depth - Tw - 4, At, L);
                return null;
            }
        }
        return null;
    }

    /// <summary>How thick a wing's walls are: as a house's.</summary>
    const int HouseWallOf = Shapes.HouseWall;

    /// <summary>What stands under a canopy or in a yard, set out along it on the ground, each turned along the wall.</summary>
    void Holds(Annex a, int pFrom, int pTo, int tFrom, int tTo, Func<int, int, (int x, int z)> at, VoxelList L)
    {
        if (a.Holds.Length == 0 || pTo <= pFrom || tTo <= tFrom) return;
        int n = a.Holds.Length, slot = (pTo - pFrom) / n;
        for (int i = 0; i < n; i++)
        {
            var (w, d) = Size(a.Holds[i]);
            if (w > slot - 2 || d > tTo - tFrom) continue;
            int p = pFrom + i * slot + (slot - w) / 2, t = tFrom + (tTo - tFrom - d) / 2;
            var (ax, az) = at(p, t);
            var (bx, bz) = at(p + w - 1, t + d - 1);
            int x = Math.Min(ax, bx), z = Math.Min(az, bz), px = Math.Abs(bx - ax) + 1, pz = Math.Abs(bz - az) + 1;
            Draw(L, a.Holds[i], x, World.SurfAt(x + px / 2, z + pz / 2), z, px, pz, px != w, (xx, zz, y) => World.SurfAt(xx, zz));
        }
    }
}
