using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// The mountains of a mountain valley, as massifs (since 02.11.2026; before, its rocky knolls rose from a single noise as
/// smooth cones, and the highest of them stood cut flat against the top of the world: «горы … обрезанные сверху и
/// идеально конусные»). A valley has a few ranges, each a chain of summits of different heights, and a lone summit or
/// two besides. A summit's flanks run out in spurs, sharp along their crests, with broad hollows between them and
/// gullies down the hollows, deepest halfway down; where two summits of a range meet, the ground between them sinks to
/// a col. Over that the rock has lumps and lesser ridges of its own, more the higher up, and it lies in layers: ledges
/// and the cliffs between them, the layers dipping a little across the valley. Nothing stands within a few metres of
/// the top of the world: the summits are made low enough to keep under it, and whatever would still go over is bent
/// down under it. MapGen.GenerateHex lays it over the lowland, faded off the roads, the ramps, the plateau, the water
/// and the land's edge, and covers it: rock where it is steep, grass on the gentle ground below the trees, moss and dry
/// grass above them, snow on the heights, scree in fans under the cliffs.
/// </summary>
public static partial class MapGen
{
    sealed class Massifs
    {
        /// <summary>A summit: where it stands, how high over the lowland, how far out its foot reaches (out along its
        /// spurs a third again), how many spurs and gullies it has, and where its spurs and gullies are read from; how
        /// steep its flanks fall, and the way its steepest face looks and how much nearer its foot is on that side.</summary>
        public sealed class Summit
        {
            public double X, Z, H, R, Lobes, Creases, Ox, Oz, Gx, Gz, Bend, Steep, Face, Lean;
        }

        public readonly List<Summit> Summits = new();
        readonly ValueNoise _spur, _crease, _rough;
        readonly double _tiltX, _tiltZ, _step;
        readonly int _salt;
        /// <summary>The highest any ground stands, a few metres under the top of the world; the snow line; the tree line.</summary>
        public readonly int Top, SnowLine, TreeLine;
        /// <summary>The furthest a summit's spurs reach, as a share of its foot's reach.</summary>
        const double Spurs = 1.3;

        public Massifs(uint seed, int worldTop)
        {
            _spur = new ValueNoise(seed * 71 + 131); _crease = new ValueNoise(seed * 73 + 137); _rough = new ValueNoise(seed * 79 + 139);
            var r = new Mulberry32(seed * 83 + 149);
            double dip = r.Next() * Math.Tau, lean = .03 + r.Next() * .05;
            _tiltX = Math.Cos(dip) * lean; _tiltZ = Math.Sin(dip) * lean;
            _step = 10 + r.Next() * 3;
            _salt = (int)(r.Next() * 9973);
            Top = worldTop - 14;
            SnowLine = Top - 30;
            TreeLine = SnowLine - 24;
        }

        /// <summary>A summit at a spot, so high over the lowland and reaching so far, its spurs and gullies its own.</summary>
        public void Add(double x, double z, double h, double reach, Mulberry32 r)
        {
            Summits.Add(new Summit
            {
                X = x, Z = z, H = h, R = reach,
                // (read off a ring of the noise round the summit: the wider the ring, the more spurs and gullies)
                Lobes = .62 + r.Next() * .38, Creases = 1.9 + r.Next() * 1.2,
                Ox = 20 + r.Next() * 200, Oz = 20 + r.Next() * 200, Gx = 20 + r.Next() * 200, Gz = 20 + r.Next() * 200,
                Bend = (r.Next() - .5) * .7, Steep = 1.2 + r.Next() * .55, Face = r.Next() * Math.Tau, Lean = .1 + r.Next() * .24,
            });
        }

        /// <summary>Whether a summit reaching so far would stand too near those there are (but the one it follows on from).</summary>
        public bool Crowded(double x, double z, double reach, Summit after = null)
        {
            foreach (var s in Summits)
                if (s != after && Brushes.Dist(x - s.X, z - s.Z) < (s.R + reach) * .55) return true;
            return false;
        }

        /// <summary>
        /// How high the summits raise a column over the lowland: each summit's flank as far out as its spur reaches along
        /// that way (the further out the nearer it is to a spur's crest), steepest near the top; the gullies taken out of it;
        /// the summits met smoothly where they overlap, so that between two of a range the ground sinks to a col.
        /// </summary>
        public double Rise(double x, double z, out double share, out double steep)
        {
            double best = 0, most = 0;
            share = steep = 0;
            foreach (var s in Summits)
            {
                double dx = x - s.X, dz = z - s.Z, reach = s.R * Spurs;
                if (Math.Abs(dx) >= reach || Math.Abs(dz) >= reach) continue;
                double d2 = dx * dx + dz * dz;
                if (d2 >= reach * reach) continue;
                double d = Math.Sqrt(d2);
                // which way from the summit, bent a little the further down, so the spurs and the gullies wind; nearer its
                // foot on the side of its steep face
                double way = Math.Atan2(dz, dx), a = way + s.Bend * d / s.R, c = Math.Cos(a), sn = Math.Sin(a);
                double crest = 1 - Math.Abs(_spur.Sample(c * s.Lobes + s.Ox, sn * s.Lobes + s.Oz) * 2 - 1);
                double far = s.R * (.56 + .74 * crest * crest * crest) * (1 - s.Lean * Math.Cos(way - s.Face)), t = d / far;
                if (t >= 1) continue;
                double up = s.H * Math.Pow(1 - t, s.Steep);
                double gully = 1 - Math.Abs(_crease.Sample(c * s.Creases + s.Gx, sn * s.Creases + s.Gz) * 2 - 1), g2 = gully * gully;
                up -= s.H * .13 * g2 * g2 * g2 * gully * Math.Sin(Math.PI * t);
                if (up <= 0) continue;
                // (how far up the summit that stands highest here this is)
                if (up > most) { most = up; share = up / s.H; steep = s.H * s.Steep * Math.Pow(1 - t, s.Steep - 1) / far; }
                best = best <= 0 ? up : SoftMax(best, up);
            }
            return best;
        }

        /// <summary>
        /// The ground's height over a mountain's rise: the rock's lumps and lesser ridges, more of them the higher up; the
        /// layers of the rock as ledges and cliffs, dipping a little across the valley; and under the top, with room to spare.
        /// The last stretch up to a summit (by <paramref name="share"/> of its height, <see cref="Rise"/>) keeps to its point:
        /// no ledge nor lump flattens it.
        /// </summary>
        public double Shape(double x, double z, double low, double up, double share, double steep)
        {
            double point = 1 - Site.Smooth01((share - .72) / .2);
            double k = Math.Min(1, up / 45) * (.3 + .7 * point);
            double r1 = 1 - Math.Abs(_rough.Sample(x * .013, z * .013) * 2 - 1), r2 = 1 - Math.Abs(_rough.Sample(x * .031 + 71, z * .031 + 37) * 2 - 1);
            double h = low + up + ((r1 * r1 - .45) * 20 + (r2 - .5) * 8) * k;
            // (the layers thicker here and thinner there, and no ledges on the gentle skirts)
            double step = _step * (.8 + .4 * _rough.Sample(x * .004 + 5, z * .004 + 11));
            double tilt = x * _tiltX + z * _tiltZ, f = (h + tilt) / step, fl = Math.Floor(f);
            double stepped = (fl + Site.Smooth01((f - fl - .6) / .4)) * step - tilt;
            double band = .85 * Site.Smooth01((up - 10) / 30) * Site.Smooth01((steep - .45) / .5) * (.5 + .5 * _rough.Sample(x * .006 + 13, z * .006 + 29)) * point;
            h += (stepped - h) * band;
            // (what would stand over the last few metres under the top is drawn down to half its height there, so a summit
            // keeps its point under the top rather than standing cut flat against it)
            double knee = Top - 14;
            if (h > knee) h = Math.Min(Top, knee + (h - knee) * .5);
            return h;
        }

        /// <summary>The rock of a mountain at a voxel: in layers, pale and dark, two to a ledge, dipping as the ledges do.</summary>
        public byte Rock(int x, int y, int z)
        {
            int layer = (int)Math.Floor((y + x * _tiltX + z * _tiltZ) * 2 / _step);
            return Hash.H3(layer, _salt, 911) < .38 ? Mat.RockPale : Mat.Rock;
        }

        /// <summary>A column's rock from one height up to another, in its layers.</summary>
        public void FillRock(VoxelWorld w, int x, int z, int y0, int y1)
        {
            for (int y = y0; y < y1;)
            {
                byte m = Rock(x, y, z);
                int e = y + 1;
                while (e < y1 && Rock(x, e, z) == m) e++;
                w.FillColumnRaw(x, z, y, e, m);
                y = e;
            }
        }

        /// <summary>The higher of two summits' rises, rounded where they meet (the less, the lower the two are: none at their feet).</summary>
        static double SoftMax(double a, double b)
        {
            double k = Math.Min(14, Math.Min(a, b) * .6);
            if (k < 1e-3) return Math.Max(a, b);
            double m = Math.Max(k - Math.Abs(a - b), 0) / k;
            return Math.Max(a, b) + m * m * k * .25;
        }
    }
}
