using System;
using System.Collections.Generic;
using System.IO;

namespace VoxelTower.Core;

/// <summary>What a template plans where it is set: the voxels to lay, those to stand as an arch, a doorway to cut, or why not.</summary>
public sealed class TemplatePlan
{
    public VoxelList Voxels;
    public VoxelList Arched;
    public Shapes.HouseDoor? Door;
    public string Why;
}

/// <summary>
/// A ready template beside the brushes (the author, 25.10.2026: «добавить шаблоны готовые»): a structure planned whole
/// where it is set and built by the workers like any marking. Its name, what it is, whether it is drawn from one end to
/// the other or set at a point, the size the height keys change, the settlement's rank it opens at, and its plan.
/// </summary>
public sealed class Template
{
    public string Key = "", Name = "", About = "";
    /// <summary>Drawn from one end to the other (a wall, a palisade) rather than set at a point.</summary>
    public bool Line;
    /// <summary>Its height, voxels, as the height keys set it, and how far they go.</summary>
    public int Size, SizeMin, SizeMax;
    /// <summary>The settlement's rank it opens at (0: from the start).</summary>
    public int Opens;
    /// <summary>The plan: the world, the site, where it is set (and, drawn, where it ends), its turn (0–3) and height.</summary>
    public Func<VoxelWorld, Site, int, int, int, int, int, int, TemplatePlan> Plan;
}

/// <summary>
/// The templates, and the settlement's rank. A template is set with the «Шаблоны» tool: its ghost at the cursor, R turns
/// it, the height keys raise or lower it, a click puts it up — a stretch of wall or a palisade is drawn with the mouse
/// held from one end to the other, and a point template with Shift held is set again every few metres along the drag.
/// Towers have battlements and a stair up to their top, the wall its merlons on the side away from the citadel and a
/// stair down from its middle on the other; the gatehouse's gate is cut and hung once its wall stands, as a house's
/// door is. The settlement's rank rises with what is built — houses, the town's buildings by their level, the things
/// set out about the place, the masonry laid — from a farmstead to a town; each rank pays gold and some open a template.
/// </summary>
public sealed partial class Sim
{
    // ------------------------------------------------------------ the templates

    /// <summary>The direction a turn faces: 0 south (+z), 1 east (+x), 2 north, 3 west.</summary>
    static (int x, int z) Facing4(int rot) => (rot & 3) switch { 0 => (0, 1), 1 => (1, 0), 2 => (0, -1), _ => (-1, 0) };

    public static readonly Template[] Templates =
    {
        new() { Key = "roundtower", Name = "Круглая башня", Size = 32, SizeMin = 24, SizeMax = 40,
            About = "Сплошная каменная башня 5 м в поперечнике, зубцы по краю, лестница на верх. Высота — клавишами высоты, R поворачивает лестницу.",
            Plan = (w, s, x, z, _, _, rot, h) => RoundTower(w, s, x, z, rot, h) },
        new() { Key = "squaretower", Name = "Квадратная башня", Size = 32, SizeMin = 24, SizeMax = 40, Opens = 1,
            About = "Сплошная каменная башня 4 × 4 м, зубцы и лестница на верх.",
            Plan = (w, s, x, z, _, _, rot, h) => SquareTower(w, s, x, z, rot, h) },
        new() { Key = "wall", Name = "Участок стены", Line = true, Size = 24, SizeMin = 16, SizeMax = 40,
            About = "Каменная стена в 1 м толщиной от одного конца до другого: зубцы с внешней стороны, ход по верху, лестница с внутренней стороны. Тяните мышью.",
            Plan = (w, s, ax, az, bx, bz, _, h) => WallSection(w, s, ax, az, bx, bz, h) },
        new() { Key = "palisade", Name = "Частокол", Line = true, Size = 20, SizeMin = 16, SizeMax = 28,
            About = "Частокол из заострённых брёвен. Строится быстро, но горит. Тяните мышью.",
            Plan = (w, s, ax, az, bx, bz, _, h) => Palisade(w, s, ax, az, bx, bz, h) },
        new() { Key = "gatehouse", Name = "Надвратная башня", Opens = 2,
            About = "Стена 7 м с воротами посередине и двумя башнями по бокам. Ворота прорубят и навесят, когда стена встанет. R поворачивает.",
            Plan = (w, s, x, z, _, _, rot, _) => Gatehouse(w, s, x, z, rot) },
        new() { Key = "watchtower", Name = "Дозорная вышка", Opens = 1,
            About = "Деревянная вышка: площадка 3 × 3 м на высоте около 3,5 м, перила и приставная лестница. R поворачивает лестницу.",
            Plan = (w, s, x, z, _, _, rot, _) => Watchtower(w, s, x, z, rot) },
        new() { Key = "barn", Name = "Амбар", Opens = 1,
            About = "Бревенчатый амбар с двускатной крышей и дверью. Не жилой дом: лежанок в нём нет.",
            Plan = (w, s, x, z, _, _, rot, _) => Barn(w, s, x, z, rot) },
        new() { Key = "stairs", Name = "Лестница на стену",
            About = "Каменная лестница с верха стены вниз. Наведите на верх стены, R поворачивает, куда она спустится.",
            Plan = (w, s, x, z, _, _, rot, _) => WallStairs(w, s, x, z, rot) },
        new() { Key = "floor", Name = "Настил", Size = 24, SizeMin = 16, SizeMax = 40,
            About = "Дощатый пол внутри готовых стен на заданной высоте: второй ярус башни или дома. Щёлкните внутри, высота — клавишами высоты. Шире 4 м без опоры не лечь.",
            Plan = (w, s, x, z, _, _, _, h) => Floor(w, s, x, z, h) },
    };

    public static Template TemplateOf(string key) => Array.Find(Templates, t => t.Key == key);

    /// <summary>Whether all of a footprint is in the valley (not over the hexagon's edge, nor where nothing is built).</summary>
    static bool Inside(VoxelWorld w, Site s, int x0, int z0, int x1, int z1)
    {
        for (int z = z0; z <= z1; z += Math.Max(1, (z1 - z0) / 6))
            for (int x = x0; x <= x1; x += Math.Max(1, (x1 - x0) / 6))
                if (!w.OnLand(x, z) || x < 2 || z < 2 || x >= w.SX - 2 || z >= w.SZ - 2) return false;
        return w.OnLand(x1, z1) && w.OnLand(x0, z1) && w.OnLand(x1, z0);
    }

    static TemplatePlan No(string why) => new() { Why = why };
    const string Edge = "здесь край долины";

    /// <summary>Columns of solid stone from the ground up to a top, where they are free.</summary>
    static void Column(VoxelWorld w, VoxelList L, int x, int z, int y0, int y1, byte m)
    {
        for (int y = Math.Max(1, y0); y < y1 && y < w.SY; y++)
            if (Brushes.Free(w.Get(x, y, z))) L.Add(x, y, z, Shapes.Lay(m, x, y, z));
    }

    /// <summary>A merlon or a gap: every other stretch of so many voxels along the battlements stands up.</summary>
    static bool Merlon(double along, int each = 6) => (int)Math.Floor(along / each) % 2 == 0;

    static TemplatePlan RoundTower(VoxelWorld w, Site s, int cx, int cz, int rot, int h)
    {
        const int R = 20;
        if (!Inside(w, s, cx - R - 50, cz - R - 50, cx + R + 50, cz + R + 50)) return No(Edge);
        int top = Shapes.LevelTop(w, cx, cz, h);
        var L = Shapes.Circle(w, s, cx, cz, R, new Stroke { Mat = Mat.Stone, Width = 8, Height = h, Level = true }, top);
        var (dx, dz) = Facing4(rot);
        // the battlements round the rim, open where the stair comes up
        for (int z = cz - R; z <= cz + R; z++)
            for (int x = cx - R; x <= cx + R; x++)
            {
                double d = Brushes.Dist(x - cx, z - cz);
                if (d > R || d <= R - 3) continue;
                double a = Math.Atan2(z - cz, x - cx), along = (a + Math.PI) * R;
                if (!Merlon(along)) continue;
                double toStair = (x - cx) * dx + (z - cz) * dz;
                if (toStair > R - 10 && Math.Abs((x - cx) * dz - (z - cz) * dx) < 9) continue;
                Column(w, L, x, z, top, top + 5, Mat.Stone);
            }
        var stair = Brushes.Stairs(w, s, cx + dx * R, cz + dz * R, top, cx + dx * (R + 80), cz + dz * (R + 80), Mat.Stone);
        Add(L, stair);
        return new TemplatePlan { Voxels = L };
    }

    static TemplatePlan SquareTower(VoxelWorld w, Site s, int cx, int cz, int rot, int h)
    {
        const int S = 16;
        if (!Inside(w, s, cx - S - 50, cz - S - 50, cx + S + 50, cz + S + 50)) return No(Edge);
        int top = Shapes.LevelTop(w, cx, cz, h);
        var L = Shapes.Box(w, s, cx - S, cz - S, cx + S - 1, cz + S - 1, new Stroke { Mat = Mat.Stone, Width = 8, Height = h, Level = true }, top);
        // the corners whole: a joint raked out of both faces there would leave the stones over it on nothing
        foreach (var (x, z) in new[] { (cx - S, cz - S), (cx + S - 1, cz - S), (cx - S, cz + S - 1), (cx + S - 1, cz + S - 1) })
            Column(w, L, x, z, w.SurfAt(x, z), top, Mat.Stone);
        var (dx, dz) = Facing4(rot);
        for (int z = cz - S; z < cz + S; z++)
            for (int x = cx - S; x < cx + S; x++)
            {
                int ex = Math.Min(x - (cx - S), cx + S - 1 - x), ez = Math.Min(z - (cz - S), cz + S - 1 - z);
                if (Math.Min(ex, ez) > 2) continue;
                double along = ex <= 2 ? z - (cz - S) : x - (cx - S);
                if (!Merlon(along)) continue;
                // open where the stair comes up, at the middle of its side
                int fx = x - cx, fz = z - cz;
                if (fx * dx + fz * dz > S - 4 && Math.Abs(fx * dz - fz * dx) < 8) continue;
                Column(w, L, x, z, top, top + 5, Mat.Stone);
            }
        Add(L, Brushes.Stairs(w, s, cx + dx * S - (dx < 0 ? 1 : 0), cz + dz * S - (dz < 0 ? 1 : 0), top, cx + dx * (S + 80), cz + dz * (S + 80), Mat.Stone));
        return new TemplatePlan { Voxels = L };
    }

    static TemplatePlan WallSection(VoxelWorld w, Site s, int ax, int az, int bx, int bz, int h)
    {
        double len = Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
        if (len < 16) return No("протяните стену дальше");
        if (!Inside(w, s, Math.Min(ax, bx) - 12, Math.Min(az, bz) - 12, Math.Max(ax, bx) + 12, Math.Max(az, bz) + 12)) return No(Edge);
        int top = Shapes.LevelTop(w, ax, az, h);
        var L = Shapes.Line(w, s, ax, az, bx, bz, new Stroke { Mat = Mat.Stone, Width = 8, Height = h, Level = true }, top);
        double ux = (bx - ax) / len, uz = (bz - az) / len, nx = -uz, nz = ux;
        // the merlons on the side away from the citadel
        double mx = (ax + bx) / 2.0 - s.KeepX, mz = (az + bz) / 2.0 - s.KeepZ;
        if (nx * mx + nz * mz < 0) { nx = -nx; nz = -nz; }
        int x0 = Math.Min(ax, bx) - 6, x1 = Math.Max(ax, bx) + 6, z0 = Math.Min(az, bz) - 6, z1 = Math.Max(az, bz) + 6;
        for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                double px = x + .5 - ax - .5, pz = z + .5 - az - .5;
                double along = px * ux + pz * uz, side = px * nx + pz * nz;
                if (along < 0 || along > len || side <= 1.5 || side > 4.25) continue;
                if (!Merlon(along)) continue;
                Column(w, L, x, z, top, top + 5, Mat.Stone);
            }
        // a stair down from the walk at its middle, on the citadel's side
        int sx = (int)Math.Round((ax + bx) / 2.0 - nx * 4.5), sz = (int)Math.Round((az + bz) / 2.0 - nz * 4.5);
        if (h >= 16) Add(L, Brushes.Stairs(w, s, sx, sz, top, sx - (int)Math.Round(nx * 80), sz - (int)Math.Round(nz * 80), Mat.Stone));
        return new TemplatePlan { Voxels = L };
    }

    static TemplatePlan Palisade(VoxelWorld w, Site s, int ax, int az, int bx, int bz, int h)
    {
        double len = Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
        if (len < 8) return No("протяните частокол дальше");
        if (!Inside(w, s, Math.Min(ax, bx) - 4, Math.Min(az, bz) - 4, Math.Max(ax, bx) + 4, Math.Max(az, bz) + 4)) return No(Edge);
        var L = new VoxelList();
        double ux = (bx - ax) / len, uz = (bz - az) / len;
        // logs side by side, each standing on its own ground, their tops sharpened by turns
        for (int x = Math.Min(ax, bx) - 3; x <= Math.Max(ax, bx) + 3; x++)
            for (int z = Math.Min(az, bz) - 3; z <= Math.Max(az, bz) + 3; z++)
            {
                double px = x - ax, pz = z - az, along = px * ux + pz * uz, side = Math.Abs(px * -uz + pz * ux);
                if (along < 0 || along > len || side > 1.6) continue;
                int g = w.SurfAt(x, z), log = (int)Math.Floor(along / 3);
                Column(w, L, x, z, g, g + h + (log % 2 == 0 ? 3 : 0), Mat.Wood);
            }
        return L.Count > 0 ? new TemplatePlan { Voxels = L } : No("здесь не на что ставить");
    }

    static TemplatePlan Gatehouse(VoxelWorld w, Site s, int cx, int cz, int rot)
    {
        var (dx, dz) = Facing4(rot);
        int px = -dz, pz = dx;
        if (!Inside(w, s, cx - 60, cz - 60, cx + 60, cz + 60)) return No(Edge);
        int g = w.SurfAt(cx, cz), top = g + 32, towerTop = g + 44;
        var L = new VoxelList();
        // the curtain over the gate: seven metres across, a metre deep; the towers either side, two metres square
        for (int a = -44; a < 44; a++)
            for (int d = -8; d < 8; d++)
            {
                bool tower = Math.Abs(a + .5) > 28;
                if (!tower && (d < -4 || d >= 4)) continue;
                int x = cx + px * a + dx * d, z = cz + pz * a + dz * d;
                int y1 = tower ? towerTop : top;
                Column(w, L, x, z, w.SurfAt(x, z), y1, Mat.Stone);
                // battlements on the outer edge of the towers' tops and over the gate
                bool outer = tower ? (Math.Abs(a + .5) > 41 || d >= 5 || d < -5) : d >= 2;
                if (outer && Merlon(a + 44)) Column(w, L, x, z, y1, y1 + 5, Mat.Stone);
            }
        // a stair up to the curtain's walk on the inner side
        Add(L, Brushes.Stairs(w, s, cx - dx * 4 + px * 16, cz - dz * 4 + pz * 16, top, cx - dx * 84 + px * 16, cz - dz * 84 + pz * 16, Mat.Stone));
        var door = new Shapes.HouseDoor { X = cx + dx * 3, Y = g + 8, Z = cz + dz * 3, NX = dx, NZ = dz, Thick = 8, Top = 0 };
        return new TemplatePlan { Voxels = L, Door = door };
    }

    static TemplatePlan Watchtower(VoxelWorld w, Site s, int cx, int cz, int rot)
    {
        if (!Inside(w, s, cx - 20, cz - 20, cx + 20, cz + 20)) return No(Edge);
        var (dx, dz) = Facing4(rot);
        // set on the cells: the deck covers three of them each way, so that one stands on it as on a floor
        int bx = ((cx >> 3) - 1) << 3, bz = ((cz >> 3) - 1) << 3, g = 0;
        for (int x = bx; x < bx + 24; x += 4) for (int z = bz; z < bz + 24; z += 4) g = Math.Max(g, w.SurfAt(x, z));
        int deck = ((g + 31) >> 3) << 3;
        var L = new VoxelList();
        // posts at the corners and one in the middle under the deck, a metre over it at the corners for the rail
        foreach (var (ox, oz, up) in new[] { (0, 0, 8), (22, 0, 8), (0, 22, 8), (22, 22, 8), (11, 11, 0) })
            for (int x = bx + ox; x < bx + ox + 2; x++)
                for (int z = bz + oz; z < bz + oz + 2; z++) Column(w, L, x, z, w.SurfAt(x, z), deck + up, Mat.Wood);
        // the deck of planks a voxel thick (one stands in its cell, as on a thin floor), and the rail round it, open
        // over the ladder
        for (int x = bx; x < bx + 24; x++)
            for (int z = bz; z < bz + 24; z++)
            {
                Column(w, L, x, z, deck, deck + 1, Mat.Wood);
                bool rim = x == bx || x == bx + 23 || z == bz || z == bz + 23;
                int ex = x - bx - 12, ez = z - bz - 12;
                bool overLadder = ex * dx + ez * dz >= 10 && Math.Abs(ex * dz - ez * dx) <= 4;
                if (rim && !overLadder) Column(w, L, x, z, deck + 6, deck + 7, Mat.Wood);
            }
        // the ladder in the cell outside the middle of the side the turn faces: two rails and rungs every third voxel
        int lx = dx > 0 ? bx + 24 : dx < 0 ? bx - 1 : bx + 9, lz = dz > 0 ? bz + 24 : dz < 0 ? bz - 1 : bz + 9;
        for (int t = 0; t < 6; t++)
        {
            int x = lx + (dx == 0 ? t : 0), z = lz + (dz == 0 ? t : 0);
            for (int y = w.SurfAt(x, z); y < deck + 4; y++)
                if ((t == 0 || t == 5 || y % 3 == 0) && Brushes.Free(w.Get(x, y, z))) L.Add(x, y, z, Mat.Ladder);
        }
        return new TemplatePlan { Voxels = L };
    }

    static TemplatePlan Barn(VoxelWorld w, Site s, int cx, int cz, int rot)
    {
        int across = 18, along = 22;
        if ((rot & 1) == 1) (across, along) = (along, across);
        int x0 = cx - across, z0 = cz - along, x1 = cx + across, z1 = cz + along - 1;
        if (!Inside(w, s, x0 - 4, z0 - 4, x1 + 4, z1 + 4)) return No(Edge);
        var plan = Shapes.House(w, s, x0, z0, x1, z1, new Stroke { Mat = Mat.Wood, Width = 4, Height = 32, Level = true }, Shapes.LevelTop(w, cx, cz, 32), out var door, out string why, out var arched);
        if (plan == null) return No(why);
        return new TemplatePlan { Voxels = plan, Arched = arched, Door = door };
    }

    static TemplatePlan WallStairs(VoxelWorld w, Site s, int x, int z, int rot)
    {
        if (!Inside(w, s, x - 40, z - 40, x + 40, z + 40)) return No(Edge);
        var (dx, dz) = Facing4(rot);
        int top = w.SurfAt(x, z);
        // the ground where the stair would come down
        int low = w.SurfAt(x + dx * 24, z + dz * 24);
        if (top - low < 8) return No("наведите на верх стены");
        var L = Brushes.Stairs(w, s, x, z, top, x + dx * 120, z + dz * 120, Mat.Stone);
        return L != null ? new TemplatePlan { Voxels = L } : No("здесь лестнице негде спуститься");
    }

    /// <summary>
    /// A floor of planks inside walls that stand, so high over the ground where it was clicked: the room at that height
    /// filled from the click out to the walls round it. Not where it would run out of the walls (a gap, a doorway, no
    /// walls at all), nor where its middle would be further from any wall than planks bear.
    /// </summary>
    static TemplatePlan Floor(VoxelWorld w, Site s, int cx, int cz, int h)
    {
        if (!Inside(w, s, cx - 4, cz - 4, cx + 4, cz + 4)) return No(Edge);
        int y = w.SurfAt(cx, cz) + h;
        if (y + 2 >= w.SY || !Brushes.Free(w.Get(cx, y, cz))) return No("здесь не пусто");
        const int Most = 3000, Far = 80;
        var seen = new Dictionary<int, int>();
        var queue = new Queue<(int x, int z)>();
        var walls = new Queue<(int x, int z)>();
        seen[cz * w.SX + cx] = -1;
        queue.Enqueue((cx, cz));
        while (queue.Count > 0)
        {
            var (x, z) = queue.Dequeue();
            foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = x + dx, nz = z + dz, key = nz * w.SX + nx;
                if (seen.ContainsKey(key)) continue;
                if (Math.Abs(nx - cx) > Far || Math.Abs(nz - cz) > Far || !w.OnLand(nx, nz)) return No("стены вокруг не замкнуты");
                // (a joint raked out of the wall's face at that height gets its plank too: so the floor touches the stone)
                if (!Brushes.Free(w.Get(nx, y, nz))) { seen[key] = 0; walls.Enqueue((nx, nz)); continue; }
                seen[key] = -1;
                if (seen.Count > Most) return No("слишком просторно: настил кладут внутри стен");
                queue.Enqueue((nx, nz));
            }
        }
        // how far each plank is from the nearest wall, out from the walls inward
        int far = 0;
        while (walls.Count > 0)
        {
            var (x, z) = walls.Dequeue();
            int d = seen[z * w.SX + x];
            foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int key = (z + dz) * w.SX + x + dx;
                if (!seen.TryGetValue(key, out int v) || v != -1) continue;
                seen[key] = d + 1;
                far = Math.Max(far, d + 1);
                walls.Enqueue((x + dx, z + dz));
            }
        }
        if (far > 15) return No("настил шире 4 м: без опоры посередине не лечь");
        // planks a voxel thick: one stands on them as on a thin floor
        var L = new VoxelList();
        foreach (var (key, d) in seen)
            if (d > 0) L.Add(key % w.SX, y, key / w.SX, Mat.Wood);
        return L.Count > 0 ? new TemplatePlan { Voxels = L } : No("здесь нечего настилать");
    }

    static void Add(VoxelList L, VoxelList more)
    {
        if (more == null) return;
        for (int i = 0; i < more.Count; i++) L.Add(more.X(i), more.Y(i), more.Z(i), more.M(i));
    }

    /// <summary>Whether a template is open yet: by the settlement's rank (in the sandbox, all are).</summary>
    public bool TemplateOpen(Template t) => Sandbox || t.Opens <= Standing;

    /// <summary>A template's plan put up for the workers: marked, its arch held, its doorway to be cut once the wall stands. Null, or why not.</summary>
    public string PutTemplate(Template t, TemplatePlan plan)
    {
        if (!TemplateOpen(t)) return $"«{t.Name}» откроется на уровне «{StandingNames[t.Opens]}»";
        if (plan == null || plan.Voxels == null) return plan?.Why ?? "здесь нельзя";
        if (DesignateBuild(plan.Voxels) == 0) return "здесь нечего класть";
        Arch(plan.Arched);
        if (plan.Door is { } door) HouseDoors.Add((door, Time));
        return null;
    }

    // ------------------------------------------------------------ the settlement's rank

    public static readonly string[] StandingNames = { "Хутор", "Деревня", "Село", "Посад", "Городок", "Город" };
    public static readonly int[] StandingPoints = { 0, 60, 150, 300, 500, 800 };

    /// <summary>The rank the settlement has reached (kept in the save: each pays its reward once).</summary>
    public int Standing;
    /// <summary>Voxels the workers have laid in all, for the rank.</summary>
    public long BuiltVoxels;
    double _standingAt;
    /// <summary>The settlement rose a rank: the rank, the gold it paid, and the templates it opened.</summary>
    public event Action<int, int, Template[]> StandingRose;

    /// <summary>What the settlement is worth: houses, the town's buildings by their level, the things set out about the place, the masonry laid.</summary>
    public int SettlementPoints()
    {
        int p = 0;
        foreach (var z in Zones) if (z.Kind == Zone.Dwelling) p += 10;
        foreach (var b in Buildings) if (b.Ready) p += 15 * Math.Max(1, b.Level);
        foreach (var y in Yards) if (y.Ready) p += 2;
        p += (int)(BuiltVoxels / 2000);
        return p;
    }

    /// <summary>Every few seconds: a rank more when the points are there, with its gold and its line in the chronicle.</summary>
    void TickStanding()
    {
        if (Time < _standingAt || Sandbox) return;
        _standingAt = Time + 5;
        int points = SettlementPoints();
        while (Standing + 1 < StandingPoints.Length && points >= StandingPoints[Standing + 1])
        {
            Standing++;
            int gold = 40 * Standing;
            Stock.Gold += gold;
            var opened = Array.FindAll(Templates, t => t.Opens == Standing);
            string names = string.Join(", ", Array.ConvertAll(opened, t => $"«{t.Name}»"));
            Say($"Поселение выросло: теперь это {StandingNames[Standing].ToLowerInvariant()}. Награда: {gold} золота" + (opened.Length > 0 ? (opened.Length > 1 ? $", шаблоны {names}." : $", шаблон {names}.") : "."), 1);
            StandingRose?.Invoke(Standing, gold, opened);
        }
    }

    /// <summary>The rank's line for the town tab: «Деревня · 85 из 150 до «Село»».</summary>
    public string StandingLine()
    {
        int p = SettlementPoints();
        return Standing + 1 < StandingPoints.Length ? $"{StandingNames[Standing]} · {p} из {StandingPoints[Standing + 1]} до «{StandingNames[Standing + 1]}»" : $"{StandingNames[Standing]} · {p}";
    }

    void WriteStanding(BinaryWriter w) { w.Write(Standing); w.Write(BuiltVoxels); }
    void ReadStanding(BinaryReader r) { Standing = r.ReadInt32(); BuiltVoxels = r.ReadInt64(); }
}
