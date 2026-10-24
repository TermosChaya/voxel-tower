using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>The sculpting of a body: its cells, the shapes cut in them, and the features every face and hand has.</summary>
public static partial class Bodies
{
    /// <summary>
    /// What is sculpted, part by part: the body in cells of 3 cm (K to a half-voxel each way) and its dress finer still.
    /// The plain calls in whole half-voxels (<see cref="Box"/>, <see cref="Pair"/>, <see cref="Dot"/>, <see cref="Cut"/>,
    /// <see cref="Paint"/>, <see cref="Coat"/>, <see cref="Rim"/>, <see cref="Round"/>, <see cref="Flat"/> and
    /// <see cref="Disc"/>) fill whole blocks of cells, as they always filled half-voxels; the finer work — the F… calls and
    /// the dress — is done only when <see cref="Detail"/> is on, and round shapes are then cut in cells, not half-voxels.
    /// </summary>
    sealed class Sculpt
    {
        /// <summary>Cells to a half-voxel each way.</summary>
        public readonly int K;
        /// <summary>The finer work is done: off, only the body as it was sculpted in half-voxels is.</summary>
        public readonly bool Detail;
        readonly Cells _c;
        readonly List<Gear> _gear = new();

        public Sculpt()
        {
            K = _k > 0 ? _k : BodyPlan.K;
            Detail = _k > 0 ? _detail : true;
            _c = new Cells(K);
        }

        public void Box(BodyPart p, int x0, int y0, int z0, int x1, int y1, int z1, byte m, Tissue t = Tissue.Skin) => _c.Box((int)p, x0, y0, z0, x1, y1, z1, m, t);

        /// <summary>A box on the right side (x below zero) and its mirror on the left.</summary>
        public void Pair(BodyPart right, BodyPart left, int x0, int y0, int z0, int x1, int y1, int z1, byte m, Tissue t = Tissue.Skin)
        {
            Box(right, x0, y0, z0, x1, y1, z1, m, t);
            Box(left, -x1, y0, z0, -x0, y1, z1, m, t);
        }

        public void Dot(BodyPart p, int x, int y, int z, byte m, Tissue t = Tissue.Skin) => _c.Box((int)p, x, y, z, x + 1, y + 1, z + 1, m, t);

        /// <summary>Takes out what is in a box (and in its mirror, when <paramref name="both"/>).</summary>
        public void Cut(BodyPart p, int x0, int y0, int z0, int x1, int y1, int z1, bool both = false) => _c.Cut((int)p, x0, y0, z0, x1, y1, z1, both);

        /// <summary>Recolours what is already there in a box; its tissue stays unless another is given.</summary>
        public void Paint(BodyPart p, int x0, int y0, int z0, int x1, int y1, int z1, byte m, Tissue? t = null) => _c.Paint((int)p, x0, y0, z0, x1, y1, z1, m, t);

        bool Has(BodyPart p, int x, int y, int z) => _c.Has((int)p, x, y, z);

        /// <summary>Takes away all of a part: a man's legs, for one who sits a horse.</summary>
        public void Clear(BodyPart p) => _c.Clear((int)p);

        /// <summary>
        /// Recolours only what shows inside a box — what lies within a half-voxel of the air — as a hood or a helmet covers
        /// a head; what lies in <paramref name="but"/> (a face) is left as it is.
        /// </summary>
        public void Coat(BodyPart p, int x0, int y0, int z0, int x1, int y1, int z1, byte m, Tissue? t = null,
            (int x0, int y0, int z0, int x1, int y1, int z1)? but = null)
            => _c.Coat((int)p, x0, y0, z0, x1, y1, z1, K, m, t, but is { } e ? (e.x0, e.y0, e.z0, e.x1, e.y1, e.z1) : null);

        /// <summary>
        /// A disc of a half-voxel's thickness lying flat at row y (a hat's brim, the crown of a cap), round the middle of
        /// the figure, or round (cx, cz).
        /// </summary>
        public void Flat(BodyPart p, int y, float r, byte m, Tissue t = Tissue.Skin, float cz = 0, float cx = 0)
        {
            int n = (int)Math.Ceiling(r) + 1;
            if (!Detail)
            {
                for (int z = (int)cz - n; z <= cz + n; z++)
                    for (int x = (int)cx - n; x <= cx + n; x++)
                        if ((x + .5f - cx) * (x + .5f - cx) + (z + .5f - cz) * (z + .5f - cz) <= r * r) Dot(p, x, y, z, m, t);
                return;
            }
            // cut round in cells, not in half-voxels
            for (int z = _c.Lo(cz - r), ze = _c.Hi(cz + r); z < ze; z++)
                for (int x = _c.Lo(cx - r), xe = _c.Hi(cx + r); x < xe; x++)
                {
                    float dx = _c.Mid(x) - cx, dz = _c.Mid(z) - cz;
                    if (dx * dx + dz * dz > r * r) continue;
                    for (int yy = y * K; yy < (y + 1) * K; yy++) _c.Set((int)p, x, yy, z, m, t);
                }
        }

        /// <summary>A disc of a half-voxel's thickness standing in the x–y plane at depth z, round (cx, cy), coloured by where in it a cell is.</summary>
        public void Disc(BodyPart p, int z, float cx, float cy, float r, Func<float, float, float, byte> m, Tissue t)
        {
            int n = (int)Math.Ceiling(r) + 1;
            if (!Detail)
            {
                for (int y = (int)cy - n; y <= cy + n; y++)
                    for (int x = (int)cx - n; x <= cx + n; x++)
                    {
                        float d = MathF.Sqrt((x + .5f - cx) * (x + .5f - cx) + (y + .5f - cy) * (y + .5f - cy));
                        if (d <= r) Dot(p, x, y, z, m(x + .5f, y + .5f, d), t);
                    }
                return;
            }
            for (int y = _c.Lo(cy - r), ye = _c.Hi(cy + r); y < ye; y++)
                for (int x = _c.Lo(cx - r), xe = _c.Hi(cx + r); x < xe; x++)
                {
                    float d = MathF.Sqrt((_c.Mid(x) - cx) * (_c.Mid(x) - cx) + (_c.Mid(y) - cy) * (_c.Mid(y) - cy));
                    if (d > r) continue;
                    for (int zz = z * K; zz < (z + 1) * K; zz++) _c.Set((int)p, x, y, zz, m(_c.Mid(x), _c.Mid(y), d), t);
                }
        }

        /// <summary>A ring one half-voxel wide round a box's outline at row y, standing out of it by one: a rim, a band.</summary>
        public void Rim(BodyPart p, int y, int x0, int z0, int x1, int z1, byte m, Tissue t = Tissue.Skin)
        {
            for (int z = z0 - 1; z <= z1; z++)
                for (int x = x0 - 1; x <= x1; x++)
                {
                    bool inside = x >= x0 && x < x1 && z >= z0 && z < z1, corner = (x == x0 - 1 || x == x1) && (z == z0 - 1 || z == z1);
                    if (!inside && !corner) Dot(p, x, y, z, m, t);
                }
        }

        /// <summary>Cuts the four upright edges off a box, and the rim of its top: a head, rounded.</summary>
        public void Round(BodyPart p, int x0, int y0, int z0, int x1, int y1, int z1, bool top = true)
        {
            for (int y = y0; y < y1; y++)
                foreach (int x in new[] { x0, x1 - 1 })
                    foreach (int z in new[] { z0, z1 - 1 })
                        Cut(p, x, y, z, x + 1, y + 1, z + 1);
            if (!top) return;
            for (int z = z0; z < z1; z++)
                for (int x = x0; x < x1; x++)
                    if (x == x0 || x == x1 - 1 || z == z0 || z == z1 - 1) Cut(p, x, y1 - 1, z, x + 1, y1, z + 1);
        }

        // ---------------------------------------------------------------- the finer work, in fractions of a half-voxel

        public void FBox(BodyPart p, float x0, float y0, float z0, float x1, float y1, float z1, byte m, Tissue t = Tissue.Skin)
        {
            if (Detail) Finer(() => _c.Box((int)p, x0, y0, z0, x1, y1, z1, m, t));
        }

        /// <summary>A box on the right side and its mirror on the left (the same part, or the other arm or leg).</summary>
        public void FPair(BodyPart right, BodyPart left, float x0, float y0, float z0, float x1, float y1, float z1, byte m, Tissue t = Tissue.Skin)
        {
            FBox(right, x0, y0, z0, x1, y1, z1, m, t);
            FBox(left, -x1, y0, z0, -x0, y1, z1, m, t);
        }

        public void FCut(BodyPart p, float x0, float y0, float z0, float x1, float y1, float z1, bool both = false)
        {
            if (Detail) _c.Cut((int)p, x0, y0, z0, x1, y1, z1, both);
        }

        public void FPaint(BodyPart p, float x0, float y0, float z0, float x1, float y1, float z1, byte m, Tissue? t = null)
        {
            if (Detail) Finer(() => _c.Paint((int)p, x0, y0, z0, x1, y1, z1, m, t));
        }

        public void FPaintPair(BodyPart right, BodyPart left, float x0, float y0, float z0, float x1, float y1, float z1, byte m, Tissue? t = null)
        {
            FPaint(right, x0, y0, z0, x1, y1, z1, m, t);
            FPaint(left, -x1, y0, z0, -x0, y1, z1, m, t);
        }

        /// <summary>Recolours only the cells open to the air in a box (paint on the skin, a pattern on cloth), one cell deep.</summary>
        public void FSkin(BodyPart p, float x0, float y0, float z0, float x1, float y1, float z1, byte m, Tissue? t = null)
        {
            if (Detail) Finer(() => _c.Coat((int)p, x0, y0, z0, x1, y1, z1, 1, m, t, null));
        }

        public void FBall(BodyPart p, float cx, float cy, float cz, float rx, float ry, float rz, byte m, Tissue t = Tissue.Skin, bool paint = false)
        {
            if (Detail) Finer(() => _c.Ball((int)p, cx, cy, cz, rx, ry, rz, m, t, paint));
        }

        public void FRod(BodyPart p, float ax, float ay, float az, float bx, float by, float bz, float r, byte m, Tissue t = Tissue.Skin)
        {
            if (Detail) Finer(() => _c.Rod((int)p, ax, ay, az, bx, by, bz, r, m, t));
        }

        public void FLine(BodyPart p, float ax, float ay, float az, float bx, float by, float bz, byte m, Tissue t = Tissue.Skin, bool paint = false)
        {
            if (Detail) Finer(() => _c.Line((int)p, ax, ay, az, bx, by, bz, m, t, paint));
        }

        /// <summary>A cell is there (fine coordinates).</summary>
        public bool FHas(BodyPart p, int x, int y, int z) => _c.Has((int)p, x, y, z);
        public bool FGet(BodyPart p, int x, int y, int z, out byte m, out Tissue t) => _c.Get((int)p, x, y, z, out m, out t);
        public void FSet(BodyPart p, int x, int y, int z, byte m, Tissue t = Tissue.Skin) { if (Detail) Finer(() => _c.Set((int)p, x, y, z, m, t)); }
        /// <summary>Recolours a cell that is there (fine coordinates), keeping what it is.</summary>
        public void FPut(BodyPart p, int x, int y, int z, byte m) { if (Detail && _c.Get((int)p, x, y, z, out _, out var t)) Finer(() => _c.Set((int)p, x, y, z, m, t)); }

        /// <summary>Does a piece of the finer work, its cells marked as such (<see cref="Cells.Marking"/>).</summary>
        void Finer(Action work)
        {
            _c.Marking = true;
            try { work(); }
            finally { _c.Marking = false; }
        }
        /// <summary>Takes a cell away (fine coordinates).</summary>
        public void FDel(BodyPart p, int x, int y, int z) { if (Detail) _c.Remove((int)p, x, y, z); }
        /// <summary>What a cell is made of (fine coordinates), 0 when it is not there.</summary>
        public byte FMat(BodyPart p, int x, int y, int z) => _c.Get((int)p, x, y, z, out var m, out _) ? m : (byte)0;
        /// <summary>
        /// Recolours the cells open to the air in a box (fine coordinates, the far corner excluded) for which a pattern
        /// gives a colour (0 leaves the cell as it is): lines of stitching, rows of mail, warpaint.
        /// </summary>
        public void FPattern(BodyPart p, int x0, int y0, int z0, int x1, int y1, int z1, Func<int, int, int, byte> m)
        {
            if (!Detail || !_c.Clip((int)p, ref x0, ref x1, ref y0, ref y1, ref z0, ref z1)) return;
            var put = new List<(int, int, int, byte)>();
            for (int y = y0; y < y1; y++)
                for (int z = z0; z < z1; z++)
                    for (int x = x0; x < x1; x++)
                    {
                        if (!_c.Has((int)p, x, y, z) || !_c.Shows((int)p, x, y, z, 1)) continue;
                        byte c = m(x, y, z);
                        if (c != 0) put.Add((x, y, z, c));
                    }
            foreach (var (x, y, z, c) in put) FPut(p, x, y, z, c);
        }
        /// <summary>The cell a coordinate in half-voxels falls in.</summary>
        public int FAt(float v) => (int)MathF.Floor(v * K);
        public float FMid(int c) => _c.Mid(c);

        /// <summary>
        /// Rounds the upright edges of a box (and the rim of its top) in cells: whatever lies beyond a quarter-circle of
        /// radius r (half-voxels) at each corner goes.
        /// </summary>
        public void FRound(BodyPart p, float x0, float y0, float z0, float x1, float y1, float z1, float r, bool top = true, float rTop = -1)
        {
            if (!Detail) return;
            if (rTop < 0) rTop = r;
            for (int y = _c.Lo(y0), ye = _c.Hi(y1); y < ye; y++)
                for (int z = _c.Lo(z0), ze = _c.Hi(z1); z < ze; z++)
                    for (int x = _c.Lo(x0), xe = _c.Hi(x1); x < xe; x++)
                    {
                        float mx = _c.Mid(x), my = _c.Mid(y), mz = _c.Mid(z);
                        float ex = MathF.Max(0, MathF.Max(x0 + r - mx, mx - (x1 - r))), ez = MathF.Max(0, MathF.Max(z0 + r - mz, mz - (z1 - r)));
                        bool gone = ex * ex + ez * ez > r * r;
                        if (top && !gone)
                        {
                            // the top's rim: a quarter-circle from each side up onto the top
                            float ey = MathF.Max(0, my - (y1 - rTop));
                            float ex2 = MathF.Max(0, MathF.Max(x0 + rTop - mx, mx - (x1 - rTop))), ez2 = MathF.Max(0, MathF.Max(z0 + rTop - mz, mz - (z1 - rTop)));
                            float side = MathF.Max(ex2, ez2);
                            gone = side * side + ey * ey > rTop * rTop;
                        }
                        if (gone) _c.Remove((int)p, x, y, z);
                    }
        }

        /// <summary>
        /// A piece of dress to sculpt, hung on a part (it is kept only with the finer work): in cells of 1.6 cm, or as big
        /// as the body's own (<paramref name="fine"/> off) for something the size of a troll's club.
        /// </summary>
        public Gear Hold(BodyPart on, DressKind kind, Tissue tissue = Tissue.Gear, bool fine = true)
        {
            var g = new Gear(fine ? BodyPlan.DressSub / BodyPlan.Sub : K) { On = on, Kind = kind, Tissue = tissue };
            if (Detail) _gear.Add(g);
            return g;
        }

        /// <summary>
        /// Fills the figure in: each half-voxel there where any of its cells is (or where a piece of dress fills it and the
        /// body does not), of what most of its cells show; then flesh under the skin, bones, the brain and the heart.
        /// </summary>
        public BodyPlan Into(BodyPlan f, int waist, int neck, int chestX)
        {
            var dressGrids = new PartGrid[_gear.Count];
            for (int d = 0; d < _gear.Count; d++)
            {
                var gear = _gear[d];
                dressGrids[d] = gear.C.Grid(0);
                if (dressGrids[d] == null) continue;
                f.Dress.Add(new Dress
                {
                    On = gear.On, Kind = gear.Kind, Grid = dressGrids[d], Sub = gear.C.R * BodyPlan.Sub,
                    Grip = (gear.Grip.X / H, gear.Grip.Y / H, gear.Grip.Z / H), Tip = (gear.Tip.X / H, gear.Tip.Y / H, gear.Tip.Z / H), Edge = gear.Edge,
                });
            }
            List<(byte m, byte t, int w)> mats = new(8), seen = new(8);
            for (int p = 0; p < BodyPlan.Parts; p++)
            {
                var fine = _c.Grid(p, out var finer);
                int x0 = int.MaxValue, y0 = int.MaxValue, z0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue, z1 = int.MinValue;
                void Span(PartGrid g, int r)
                {
                    x0 = Math.Min(x0, Down(g.X0, r)); y0 = Math.Min(y0, Down(g.Y0, r)); z0 = Math.Min(z0, Down(g.Z0, r));
                    x1 = Math.Max(x1, Down(g.X0 + g.SX - 1, r)); y1 = Math.Max(y1, Down(g.Y0 + g.SY - 1, r)); z1 = Math.Max(z1, Down(g.Z0 + g.SZ - 1, r));
                }
                if (fine != null) Span(fine, K);
                // the dress that hangs on this part, in the order it was sculpted (and so the index the plan keeps)
                var hung = new List<(int index, PartGrid grid, int r, (int, int, int) face)>();
                for (int d = 0, kept = 0; d < _gear.Count; d++)
                {
                    if (dressGrids[d] == null) continue;
                    var gear = _gear[d];
                    if (gear.On == (BodyPart)p) { hung.Add((kept, dressGrids[d], gear.C.R, gear.Kind == DressKind.Shield ? Facing(gear.Edge) : default)); Span(dressGrids[d], gear.C.R); }
                    kept++;
                }
                if (x0 == int.MaxValue) continue;
                var g = new PartGrid { X0 = x0, Y0 = y0, Z0 = z0, SX = x1 - x0 + 1, SY = y1 - y0 + 1, SZ = z1 - z0 + 1 };
                g.M = new byte[g.SX * g.SY * g.SZ];
                g.T = new byte[g.M.Length];
                var owner = new byte[g.M.Length];
                var solid = new bool[g.M.Length];
                var shown = new byte[g.M.Length];
                // cells a half-voxel's must be to be drawn at the middle distance: three of eight (one of one, unsculpted)
                int enough = Math.Max(1, (3 * K * K * K + 7) / 8);
                for (int y = y0; y <= y1; y++)
                    for (int z = z0; z <= z1; z++)
                        for (int x = x0; x <= x1; x++)
                        {
                            int i = g.Index(x, y, z);
                            // the body's own cells in it: what most of them show — a cell open to the air counts three times, one of
                            // the plain sculpting's four times one of the finer work's
                            mats.Clear();
                            int had = 0;
                            if (fine != null)
                                for (int dy = 0; dy < K; dy++)
                                    for (int dz = 0; dz < K; dz++)
                                        for (int dx = 0; dx < K; dx++)
                                        {
                                            int fx = x * K + dx, fy = y * K + dy, fz = z * K + dz;
                                            if (!fine.In(fx, fy, fz)) continue;
                                            int fi = fine.Index(fx, fy, fz);
                                            if (fine.M[fi] == 0) continue;
                                            int w = (fine.Mat(fx + 1, fy, fz) == 0 || fine.Mat(fx - 1, fy, fz) == 0 || fine.Mat(fx, fy + 1, fz) == 0
                                                || fine.Mat(fx, fy - 1, fz) == 0 || fine.Mat(fx, fy, fz + 1) == 0 || fine.Mat(fx, fy, fz - 1) == 0 ? 3 : 1) * (finer[fi] ? 1 : 4);
                                            Add(mats, fine.M[fi], fine.T[fi], w);
                                            had++;
                                        }
                            if (mats.Count > 0) { Most(mats, out g.M[i], out g.T[i]); g.Count++; solid[i] = had >= enough; continue; }
                            // else a piece of dress, where any of its cells is: a blade or a shaft however thin
                            foreach (var (index, dg, DR, face) in hung)
                            {
                                int n = DressCells(dg, DR, x, y, z, face, mats, seen);
                                if (n == 0) continue;
                                Most(mats, out g.M[i], out g.T[i]);
                                g.Count++;
                                owner[i] = (byte)(index + 1);
                                // a rivet or a stud alone in its half-voxel is not drawn as a block of one at the middle distance
                                solid[i] = n >= Math.Max(1, DR * DR * DR / 16);
                                Most(seen, out byte look, out _);
                                if (look != g.M[i]) shown[i] = look;
                                break;
                            }
                        }
                Inside(g, owner, solid, (BodyPart)p, f, waist, neck, chestX);
                // a half-voxel the body has too little of to be drawn (a knuckle of the fist) that a piece of dress fills
                // enough of is drawn with that piece — the haft going on into the hand, a crossguard whole before it —
                // though the blows find the knuckle there, as before (and the depths under the skin are already reckoned)
                for (int i = 0; i < g.M.Length && hung.Count > 0; i++)
                {
                    if (g.M[i] == 0 || owner[i] != 0 || solid[i]) continue;
                    int x = i % g.SX + g.X0, z = i / g.SX % g.SZ + g.Z0, y = i / (g.SX * g.SZ) + g.Y0;
                    foreach (var (index, dg, DR, face) in hung)
                    {
                        if (DressCells(dg, DR, x, y, z, face, mats, seen) < Math.Max(1, DR * DR * DR / 16)) continue;
                        owner[i] = (byte)(index + 1);
                        solid[i] = true;
                        Most(seen, out byte look, out _);
                        if (look != g.M[i]) shown[i] = look;
                        break;
                    }
                }
                f.Grid[p] = g;
                f.Fine[p] = fine;
                f.Owner[p] = owner;
                f.Solid[p] = solid;
                f.Shown[p] = Array.Exists(shown, v => v != 0) ? shown : null;
                f.Top = Math.Max(f.Top, (y1 + 2) / 2);
            }
            return f;
        }

        static int Down(int v, int r) => v >= 0 ? v / r : (v - r + 1) / r;

        /// <summary>The way a shield's face looks, along the axis nearest it (one step of a cell).</summary>
        static (int, int, int) Facing((float X, float Y, float Z) e)
        {
            float ax = MathF.Abs(e.X), ay = MathF.Abs(e.Y), az = MathF.Abs(e.Z);
            if (ax >= ay && ax >= az) return (Math.Sign(e.X), 0, 0);
            return ay >= az ? (0, Math.Sign(e.Y), 0) : (0, 0, Math.Sign(e.Z));
        }

        /// <summary>
        /// The cells of a piece of dress (<paramref name="r"/> to a half-voxel each way) in the half-voxel (x, y, z): how
        /// many there are; what they are, each counted once (<paramref name="mats"/>, what the blows find); and what they
        /// show (<paramref name="seen"/>) — a cell open to the air three times one inside, and on a shield one on the face
        /// it turns to the foe (<paramref name="face"/>) three times that again, so that it shows its paint and not the
        /// planks the paint is on.
        /// </summary>
        static int DressCells(PartGrid dg, int r, int x, int y, int z, (int x, int y, int z) face, List<(byte m, byte t, int w)> mats, List<(byte m, byte t, int w)> seen)
        {
            mats.Clear(); seen.Clear();
            int n = 0;
            bool shield = face != default;
            for (int dy = 0; dy < r; dy++)
                for (int dz = 0; dz < r; dz++)
                    for (int dx = 0; dx < r; dx++)
                    {
                        int fx = x * r + dx, fy = y * r + dy, fz = z * r + dz;
                        if (!dg.In(fx, fy, fz)) continue;
                        int fi = dg.Index(fx, fy, fz);
                        if (dg.M[fi] == 0) continue;
                        Add(mats, dg.M[fi], dg.T[fi], 1);
                        bool front = shield && dg.Mat(fx + face.x, fy + face.y, fz + face.z) == 0;
                        bool open = front || dg.Mat(fx + 1, fy, fz) == 0 || dg.Mat(fx - 1, fy, fz) == 0 || dg.Mat(fx, fy + 1, fz) == 0
                            || dg.Mat(fx, fy - 1, fz) == 0 || dg.Mat(fx, fy, fz + 1) == 0 || dg.Mat(fx, fy, fz - 1) == 0;
                        Add(seen, dg.M[fi], dg.T[fi], front ? 9 : open ? 3 : 1);
                        n++;
                    }
            return n;
        }

        static void Add(List<(byte m, byte t, int w)> mats, byte m, byte t, int w)
        {
            for (int k = 0; k < mats.Count; k++)
                if (mats[k].m == m) { mats[k] = (m, mats[k].t, mats[k].w + w); return; }
            mats.Add((m, t, w));
        }

        static void Most(List<(byte m, byte t, int w)> mats, out byte m, out byte t)
        {
            var best = mats[0];
            foreach (var e in mats) if (e.w > best.w) best = e;
            m = best.m; t = best.t;
        }

        /// <summary>
        /// How deep each voxel lies under the surface (1 for the surface), counting worn things as cover — but not a
        /// half-voxel too thinly filled to be drawn at the middle distance, which leaves what is under it showing.
        /// </summary>
        static int[] Depth(PartGrid g, bool[] solid)
        {
            var d = new int[g.M.Length];
            var q = new Queue<int>();
            for (int y = 0; y < g.SY; y++)
                for (int z = 0; z < g.SZ; z++)
                    for (int x = 0; x < g.SX; x++)
                    {
                        int i = (y * g.SZ + z) * g.SX + x;
                        if (g.M[i] == 0) continue;
                        bool edge = x == 0 || y == 0 || z == 0 || x == g.SX - 1 || y == g.SY - 1 || z == g.SZ - 1 || !solid[i]
                            || !solid[i - 1] || !solid[i + 1] || !solid[i - g.SX] || !solid[i + g.SX] || !solid[i - g.SX * g.SZ] || !solid[i + g.SX * g.SZ];
                        if (edge) { d[i] = 1; q.Enqueue(i); }
                    }
            while (q.Count > 0)
            {
                int i = q.Dequeue(), x = i % g.SX, z = i / g.SX % g.SZ, y = i / (g.SX * g.SZ);
                foreach (var (dx, dy, dz) in Around)
                {
                    int nx = x + dx, ny = y + dy, nz = z + dz;
                    if (nx < 0 || ny < 0 || nz < 0 || nx >= g.SX || ny >= g.SY || nz >= g.SZ) continue;
                    int n = (ny * g.SZ + nz) * g.SX + nx;
                    if (g.M[n] == 0 || d[n] != 0) continue;
                    d[n] = d[i] + 1;
                    q.Enqueue(n);
                }
            }
            return d;
        }

        static readonly (int, int, int)[] Around = { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) };

        static void Inside(PartGrid g, byte[] owner, bool[] solid, BodyPart p, BodyPlan f, int waist, int neck, int chestX)
        {
            var depth = Depth(g, solid);
            int headY0 = f.HeadY0, headY1 = f.HeadY1, w = waist, n = neck, chestY = w + (n - w) * 2 / 5;
            int back = int.MaxValue;
            for (int i = 0; i < g.M.Length; i++)
            {
                // under the skin (or the mail) is the body; what is carried, and the dress, stay what they are
                if (g.T[i] is not ((byte)Tissue.Skin or (byte)Tissue.Armor) || depth[i] < 2 || owner[i] != 0) continue;
                int x = i % g.SX + g.X0, z = i / g.SX % g.SZ + g.Z0, y = i / (g.SX * g.SZ) + g.Y0;
                Tissue t;
                if (p == BodyPart.Body && y >= headY0 && y < headY1) t = depth[i] == 2 ? Tissue.Bone : Tissue.Brain;
                else if (p == BodyPart.Body && !f.Beast && y >= w && y < n)
                {
                    t = Tissue.Flesh;
                    // ribs round the chest every other row, the heart a little left of the middle
                    if (y >= chestY && (y & 1) == 0 && Math.Abs(x + .5) >= 1.5) t = Tissue.Bone;
                    if (y >= chestY + 2 && y < chestY + 5 && x >= 0 && x <= chestX / 2) t = Tissue.Heart;
                }
                else if (p == BodyPart.Body) t = Tissue.Flesh;
                else t = Tissue.Bone;
                g.T[i] = (byte)t;
                g.M[i] = t switch { Tissue.Bone => Mat.Bone, Tissue.Brain => Mat.Brain, Tissue.Heart => Mat.Heart, _ => Mat.Flesh };
                if (t == Tissue.Brain) f.Brain++;
                if (t == Tissue.Heart) f.Heart++;
                if (p == BodyPart.Body && y >= w && y < n) back = Math.Min(back, z);
            }
            // the spine down the back of the trunk (or along the top of a beast)
            if (p != BodyPart.Body || back == int.MaxValue) return;
            for (int i = 0; i < g.M.Length; i++)
            {
                int x = i % g.SX + g.X0, z = i / g.SX % g.SZ + g.Z0, y = i / (g.SX * g.SZ) + g.Y0;
                if (f.Beast || y < w || y >= n || z != back || x < -1 || x > 0 || g.T[i] is not ((byte)Tissue.Flesh or (byte)Tissue.Heart)) continue;
                if (g.T[i] == (byte)Tissue.Heart) f.Heart--;
                g.T[i] = (byte)Tissue.Bone;
                g.M[i] = Mat.Bone;
            }
        }
    }

    /// <summary>
    /// Legs in boots with the toes forward, a trunk with rounded shoulders, a belt and its buckle, a round head with eyes,
    /// brows, a nose, a mouth and ears, hair as it is worn, and two arms with hands. Everything mirrors across the middle.
    /// </summary>
    static Sculpt Humanoid(Build b, BodyPlan f)
    {
        var s = new Sculpt();
        int lz0 = -(b.LegD / 2), lz1 = b.LegD - b.LegD / 2;
        s.Pair(LegR, LegL, b.LegX0, 0, lz0, b.LegX1, b.Leg, lz1, b.Trousers);
        if (b.Boot > 0)
        {
            s.Pair(LegR, LegL, b.LegX0, 0, lz0, b.LegX1, b.Boot, lz1, b.Boots);
            s.Pair(LegR, LegL, b.LegX0, 0, lz1, b.LegX1, Math.Max(1, b.Boot - 1), lz1 + 1, b.Boots);
        }
        else s.Pair(LegR, LegL, b.LegX0, 0, lz1, b.LegX1, 1, lz1 + 1, b.Skin);

        // the trunk, its shoulders rounded, a belt with a buckle, the throat showing at the collar
        s.Box(Body, -b.Chest, b.Waist, b.Z0, b.Chest, b.Neck, b.Z1, b.Shirt);
        s.Cut(Body, -b.Chest, b.Neck - 1, b.Z0, -b.Chest + 1, b.Neck, b.Z1, both: true);
        s.Box(Body, -b.Chest, b.Waist, b.Z0, b.Chest, b.Waist + 1, b.Z1, b.Belt);
        s.Box(Body, -1, b.Waist, b.Z1, 1, b.Waist + 1, b.Z1 + 1, Mat.Brass, Tissue.Armor);
        s.Paint(Body, -1, b.Neck - 2, b.Z1 - 1, 1, b.Neck, b.Z1, b.Skin);

        // the head
        int hx = b.HeadW, hz0 = b.HeadZ, hz1 = b.HZ1, hy0 = b.Neck, hy1 = b.Crown, up = b.FaceUp, eye = hy0 + 3 + up;
        s.Box(Body, -hx, hy0, hz0, hx, hy1, hz1, b.Skin);
        s.Round(Body, -hx, hy0, hz0, hx, hy1, hz1);
        s.FRound(Body, -hx, hy0, hz0, hx, hy1, hz1, Math.Min(1.25f, hx * .3f), true, 1);
        s.Cut(Body, -hx, hy0, hz0, hx, hy0 + 1, hz0 + 2);
        s.Pair(Body, Body, -hx + 1, eye, b.FaceZ, -hx + 2, eye + 2, hz1, b.Eye);
        s.Pair(Body, Body, -hx + 1, eye + 2, b.FaceZ, -1, eye + 3, hz1, b.Brow != 0 ? b.Brow : b.Hair);
        s.Box(Body, -1, hy0 + 2 + up, hz1, 1, hy0 + 4 + up, hz1 + 1, b.Skin);
        s.Box(Body, -1, hy0 + 1 + up, b.FaceZ, 1, hy0 + 2 + up, hz1, b.Mouth);
        s.Pair(Body, Body, -hx - 1, eye, -1, -hx, eye + 2, 1, b.Skin);
        Features(s, b);
        Hair(s, b);

        // the arms: sleeves, a cuff, the hands; the shoulders rounded on top
        byte sleeve = b.Sleeve != 0 ? b.Sleeve : b.Shirt;
        s.Pair(Main, Off, b.ArmX0, b.HandY + b.Hand, b.ArmZ0, -b.Chest, b.Neck, b.ArmZ1, sleeve);
        s.Pair(Main, Off, b.ArmX0, b.HandY, b.ArmZ0, -b.Chest, b.HandY + b.Hand, b.ArmZ1, b.Skin);
        if (b.Cuff != 0) s.Pair(Main, Off, b.ArmX0, b.HandY + b.Hand, b.ArmZ0, -b.Chest, b.HandY + b.Hand + 1, b.ArmZ1, b.Cuff);
        s.Cut(Main, b.ArmX0, b.Neck - 1, b.ArmZ0, b.ArmX0 + 1, b.Neck, b.ArmZ1);
        s.Cut(Off, -b.ArmX0 - 1, b.Neck - 1, b.ArmZ0, -b.ArmX0, b.Neck, b.ArmZ1);
        Fists(s, b);
        Feet(s, b);

        float legX = (b.LegX0 + b.LegX1) / 2f, armX = (b.ArmX0 - b.Chest) / 2f, armZ = (b.ArmZ0 + b.ArmZ1) / 2f;
        Joint(f, Body, 0, b.Waist / H, 0);
        Joint(f, LegR, legX / H, b.Leg / H, 0);
        Joint(f, LegL, -legX / H, b.Leg / H, 0);
        Joint(f, Main, armX / H, (b.Neck - 1) / H, armZ / H);
        Joint(f, Off, -armX / H, (b.Neck - 1) / H, armZ / H);
        f.HeadY0 = hy0; f.HeadY1 = hy1;
        return s;
    }

    /// <summary>
    /// The face drawn finer than its half-voxels: eyes three cells high with a glint at the upper corner and the lid
    /// under the brow, the brow a single row, the nose a bridge down to its tip, the mouth a line with the lip under it,
    /// the ears rounded at their corners with the hollow of the ear in them.
    /// </summary>
    static void Features(Sculpt s, Build b)
    {
        if (!s.Detail) return;
        int hx = b.HeadW, up = b.FaceUp, E = (b.Neck + 3 + up) * 2, F = b.HZ1 * 2 - 1, ex = (-hx + 1) * 2, N = (b.Neck + 2 + up) * 2, er = (-hx - 1) * 2;
        foreach (int sg in new[] { 1, -1 })
        {
            int X(int x) => sg > 0 ? x : -1 - x;
            for (int x = ex; x < ex + 4; x++)
            {
                if (x < ex + 2) s.FPut(Body, X(x), E + 3, F, b.Skin);
                s.FPut(Body, X(x), E + 5, F, b.Skin);
            }
            if (b.Eye == Mat.Eye) s.FPut(Body, sg > 0 ? ex : -ex - 2, E + 2, F, Mat.EyeWhite);
            s.FPut(Body, X(ex - 1), E + 4, F, b.Brow != 0 ? b.Brow : b.Hair);
            // the ear: its corners rounded, its hollow
            s.FDel(Body, X(er), E + 3, -2); s.FDel(Body, X(er), E, 1);
            s.FPut(Body, X(er), E + 1, 0, Mat.Lips); s.FPut(Body, X(er), E + 2, -1, Mat.Lips);
        }
        // the nose: a bridge two cells across, its tip standing out a cell more
        for (int y = N; y < N + 4; y++)
            for (int x = -2; x < 2; x++)
                if (!(y < N + 2 && x is -1 or 0)) s.FDel(Body, x, y, F + 2);
        // the mouth: the upper lip skin again over the line of it, a lower lip under it
        if (b.Mouth != b.Skin)
        {
            for (int x = -2; x < 2; x++) s.FPut(Body, x, N - 1, F, b.Skin);
            if (b.Eye == Mat.Eye) { s.FPut(Body, -1, N - 3, F, Mat.Lips); s.FPut(Body, 0, N - 3, F, Mat.Lips); }
        }
    }

    /// <summary>
    /// The hands drawn finer: the edges of the fist rounded under it, the fingers parted by grooves under it and up its
    /// outer side, the thumb laid along before it.
    /// </summary>
    static void Fists(Sculpt s, Build b)
    {
        if (!s.Detail) return;
        int x0 = b.ArmX0 * 2, x1 = -b.Chest * 2, y0 = b.HandY * 2, z0 = b.ArmZ0 * 2, z1 = b.ArmZ1 * 2, fw = Math.Max(2, (z1 - z0) / 4);
        foreach (var (p, sg) in new[] { (Main, 1), (Off, -1) })
        {
            int X(int x) => sg > 0 ? x : -1 - x;
            for (int z = z0; z < z1; z++) { s.FDel(p, X(x0), y0, z); s.FDel(p, X(x1 - 1), y0, z); }
            for (int x = x0; x < x1; x++) { s.FDel(p, X(x), y0, z0); s.FDel(p, X(x), y0, z1 - 1); }
            for (int z = z0 + fw; z < z1 - 1; z += fw)
            {
                for (int x = x0 + 1; x < x1 - 1; x++) s.FDel(p, X(x), y0, z);
                for (int y = y0 + 1; y < y0 + 3; y++) s.FDel(p, X(x0), y, z);
            }
            int ty = y0 + b.Hand * 2 - 3;
            s.FSet(p, X(x1 - 1), ty, z1, b.Skin);
            s.FSet(p, X(x1 - 1), ty + 1, z1, b.Skin);
        }
    }

    /// <summary>Boots drawn finer: a dark sole, the toe's upper edge rounded, a cuff rolled over at the top.</summary>
    static void Feet(Sculpt s, Build b)
    {
        if (!s.Detail || b.Boot <= 0) return;
        int lz0 = -(b.LegD / 2) * 2, lz1 = (b.LegD - b.LegD / 2) * 2, x0 = b.LegX0 * 2, x1 = b.LegX1 * 2, top = b.Boot * 2 - 1, toe = Math.Max(1, b.Boot - 1) * 2 - 1;
        byte sole = b.Boots == Mat.Steel ? Mat.SteelDark : b.Boots == Mat.Iron ? Mat.Iron : Mat.LeatherDark;
        foreach (var (p, sg) in new[] { (LegR, 1), (LegL, -1) })
        {
            int X(int x) => sg > 0 ? x : -1 - x;
            for (int z = lz0; z < lz1 + 2; z++) for (int x = x0; x < x1; x++) s.FPut(p, X(x), 0, z, sole);
            for (int x = x0; x < x1; x++) { s.FDel(p, X(x), toe, lz1 + 1); s.FSet(p, X(x), top, lz1, b.Boots); s.FSet(p, X(x), top, lz0 - 1, b.Boots); }
            for (int z = lz0 - 1; z <= lz1; z++) { s.FSet(p, X(x0 - 1), top, z, b.Boots); s.FSet(p, X(x1), top, z, b.Boots); }
        }
    }

    /// <summary>The hair over the top and the back of the head, down the sides above the ears, a fringe over the brow.</summary>
    static void Hair(Sculpt s, Build b)
    {
        if (b.HairStyle == 3) return;
        int hx = b.HeadW, hz0 = b.HeadZ, hz1 = b.HZ1, hy0 = b.Neck, hy1 = b.Crown;
        s.Paint(Body, -hx, hy1 - 1, hz0, hx, hy1, hz1, b.Hair);
        s.Paint(Body, -hx, hy0 + (b.HairStyle == 1 ? 4 : 2), hz0, hx, hy1, hz0 + 1, b.Hair);
        if (b.HairStyle == 1) return;
        s.Paint(Body, -hx, hy0 + 4, hz0, hx, hy1, hz0 + 2, b.Hair);
        s.Paint(Body, -hx, hy0 + 5, hz0, -hx + 1, hy1, hz1 - 2, b.Hair);
        s.Paint(Body, hx - 1, hy0 + 5, hz0, hx, hy1, hz1 - 2, b.Hair);
        s.Paint(Body, -hx + 1, hy1 - 2, b.FaceZ, hx - 1, hy1 - 1, hz1, b.Hair);
        // long: down the back of the neck and over the shoulders
        if (b.HairStyle == 2) s.Box(Body, -hx + 1, hy0 - 3, hz0 - 1, hx - 1, hy1 - 1, hz0, b.Hair);
    }

    /// <summary>A beard over the chin and the jaw and hanging a little below, a moustache, the sideburns.</summary>
    static void Beard(Sculpt s, Build b, byte hair, bool full = true)
    {
        int hx = b.HeadW, hz1 = b.HZ1, hy0 = b.Neck;
        s.Paint(Body, -hx, hy0, 0, hx, hy0 + 3, hz1, hair);
        s.Paint(Body, -hx, hy0 + 3, 0, -hx + 1, hy0 + 5, hz1 - 1, hair);
        s.Paint(Body, hx - 1, hy0 + 3, 0, hx, hy0 + 5, hz1 - 1, hair);
        s.Box(Body, -2, hy0 - (full ? 2 : 1), b.FaceZ, 2, hy0 + 1, hz1, hair);
        s.Box(Body, -2, hy0 + 1, hz1, 2, hy0 + 2, hz1 + 1, hair);
        if (full) s.Box(Body, -1, hy0 - 3, b.FaceZ, 1, hy0 - 2, hz1, hair);
    }

    /// <summary>A hood over the head and down to the shoulders, the face showing; a point hangs down at the back.</summary>
    static void Hood(Sculpt s, Build b, byte cloth)
    {
        int hx = b.HeadW, hz0 = b.HeadZ, hz1 = b.HZ1, hy0 = b.Neck, hy1 = b.Crown;
        s.Box(Body, -hx + 1, hy1, hz0 + 1, hx - 1, hy1 + 1, hz1 - 2, cloth);
        s.Box(Body, -hx + 1, hy0 + 1, hz0 - 1, hx - 1, hy1, hz0, cloth);
        s.Coat(Body, -hx - 1, hy0, hz0 - 1, hx + 1, hy1 + 1, hz1 + 1, cloth, null, (-hx + 1, hy0, hz1 - 2, hx - 1, hy1 - 1, hz1 + 2));
        s.Paint(Body, -b.Chest, b.Neck - 2, b.Z0, b.Chest, b.Neck, b.Z1, cloth);
        s.Box(Body, -1, hy0 - 2, hz0 - 2, 1, hy0 + 3, hz0 - 1, cloth);
    }

    static void Joint(BodyPlan f, BodyPart p, float x, float y, float z) { f.JX[(int)p] = x; f.JY[(int)p] = y; f.JZ[(int)p] = z; }

    static BodyPlan Done(Sculpt s, BodyPlan f, Build b) => s.Into(f, b.Waist, b.Neck, b.Chest);
}
