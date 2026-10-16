using System;

namespace VoxelTower.Core;

/// <summary>Mulberry32, bit for bit the generator of the browser prototype, so the same seeds give the same map.</summary>
public sealed class Mulberry32
{
    uint _a;
    public Mulberry32(uint seed) { _a = seed; }

    public double Next()
    {
        unchecked
        {
            _a += 0x6D2B79F5;
            uint t = _a;
            t = (t ^ (t >> 15)) * (t | 1);
            t = (t + (t ^ (t >> 7)) * (t | 61)) ^ t;
            return (t ^ (t >> 14)) / 4294967296.0;
        }
    }
}

public static class Hash
{
    /// <summary>Stable 0..1 hash of three integers (prototype's hash3).</summary>
    public static double H3(int x, int y, int z)
    {
        unchecked
        {
            uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(z * 83492791);
            h = (h ^ (h >> 13)) * 1274126177u;
            return (h ^ (h >> 16)) / 4294967296.0;
        }
    }
}

/// <summary>Smooth value noise on a 256×256 lattice.</summary>
public sealed class ValueNoise
{
    readonly float[] _p = new float[65536];

    public ValueNoise(uint seed)
    {
        var r = new Mulberry32(seed);
        for (int i = 0; i < _p.Length; i++) _p[i] = (float)r.Next();
    }

    public double Sample(double x, double z)
    {
        int xi = (int)Math.Floor(x), zi = (int)Math.Floor(z);
        double xf = x - xi, zf = z - zi, u = xf * xf * (3 - 2 * xf), v = zf * zf * (3 - 2 * zf);
        double a = _p[((xi & 255) << 8) | (zi & 255)], b = _p[(((xi + 1) & 255) << 8) | (zi & 255)];
        double c = _p[((xi & 255) << 8) | ((zi + 1) & 255)], d = _p[(((xi + 1) & 255) << 8) | ((zi + 1) & 255)];
        return Lerp(Lerp(a, b, u), Lerp(c, d, u), v);
    }

    static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
