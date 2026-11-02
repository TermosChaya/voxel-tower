using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

/// <summary>
/// The time the calling thread spends working, in milliseconds of the machine's fast cores at their full clock: what a
/// check of how long a step takes should count. Unlike the wall clock it does not grow while other programs have the
/// processor (the timed checks failed whenever the machine was busy), and unlike the process's time it leaves out what
/// the step's background tasks do meanwhile. Nor does it grow when a busy machine moves the thread to a slow core: an M1
/// has fast cores at 3.2 GHz and slow ones at 2 GHz that do half as much a cycle, and with other work about the thread
/// spent a share of its time on the slow ones, where the same step took up to three and a half times as long (the
/// bench's step of two hundred raiders: 3.5 ms alone, 4.4 to 7 ms with other benches and programs running). So on a Mac
/// the thread's cycles and instructions are counted too (Mach's thread_selfcounts): a stretch of work done on a fast
/// core counts its cycles at the fast cores' full clock (they run slower when many are busy), and one done on a slow
/// core counts its instructions at the rate the thread did that work on a fast one lately. Elsewhere, or without the
/// counters, the thread's own time (Mach's thread_info), or the process's.
/// </summary>
static class BenchClock
{
    [DllImport("libSystem.dylib")] static extern uint mach_thread_self();
    [DllImport("libSystem.dylib")] static extern int thread_info(uint thread, int flavor, int[] info, ref int count);
    [DllImport("libSystem.dylib")] static extern int thread_selfcounts(int type, ulong[] counts, nuint size);
    [DllImport("libSystem.dylib")] static extern int sysctlbyname(string name, byte[] value, ref nuint size, IntPtr newValue, nuint newSize);

    const int ThreadBasicInfo = 3, BasicInfoCount = 10;
    static readonly Process Me = Process.GetCurrentProcess();
    [ThreadStatic] static uint _port;
    [ThreadStatic] static int[] _info;
    [ThreadStatic] static ulong[] _counts;
    // the counts at the last reading, the fast-core time added up since the first, and the thread's work a cycle on a
    // fast core lately (instructions and cycles, halved now and then so that what it did long ago weighs less)
    [ThreadStatic] static bool _going;
    [ThreadStatic] static double _cpuAt, _fastMs, _fastIns, _fastCyc;
    [ThreadStatic] static ulong _insAt, _cycAt;
    static bool _noCounts;

    /// <summary>The fast cores' full clock, cycles a millisecond (measured by <see cref="Weighed"/>; 0 till then).</summary>
    public static double FastPerMs;

    /// <summary>The thread's working time so far, in fast-core milliseconds; only differences between two readings mean anything.</summary>
    public static double CpuMs()
    {
        double cpu = ThreadMs();
        if (FastPerMs <= 0 || !Counts(out ulong ins, out ulong cyc)) return cpu;
        if (!_going) { _going = true; _cpuAt = cpu; _insAt = ins; _cycAt = cyc; return _fastMs; }
        double dc = cpu - _cpuAt; ulong di = ins - _insAt, dy = cyc - _cycAt;
        _cpuAt = cpu; _insAt = ins; _cycAt = cyc;
        _fastMs += Fast(dc, di, dy);
        return _fastMs;
    }

    /// <summary>A stretch of work in fast-core milliseconds: its time, its instructions and its cycles.</summary>
    static double Fast(double cpu, ulong ins, ulong cyc)
    {
        if (cpu <= 0 || cyc == 0) return Math.Max(0, cpu);
        // done on a fast core (the slow ones of an M1 go no faster than 2.06 GHz, the fast run 3 to 3.2): its cycles at
        // the full clock, and what it did a cycle remembered
        if (cyc / cpu >= FastPerMs * .82)
        {
            if (_fastCyc > 4e9) { _fastIns *= .5; _fastCyc *= .5; }
            _fastIns += ins; _fastCyc += cyc;
            return cyc / FastPerMs;
        }
        // on a slow core: the same instructions as the thread did them on a fast one; none done there yet, its cycles at
        // the fast clock (still too much: a slow core does less a cycle)
        return _fastCyc > 0 ? ins * (_fastCyc / _fastIns) / FastPerMs : cyc / FastPerMs;
    }

    /// <summary>The thread's own time, user and system, milliseconds, wherever it ran.</summary>
    public static double ThreadMs()
    {
        if (OperatingSystem.IsMacOS())
        {
            // (the thread's port taken once: each call of mach_thread_self would hold another right to it)
            if (_port == 0) { _port = mach_thread_self(); _info = new int[BasicInfoCount]; }
            int count = BasicInfoCount;
            if (thread_info(_port, ThreadBasicInfo, _info, ref count) == 0)
                // user and system time, each seconds and microseconds
                return (_info[0] + _info[2]) * 1000.0 + (_info[1] + _info[3]) / 1000.0;
        }
        Me.Refresh();
        return Me.TotalProcessorTime.TotalMilliseconds;
    }

    /// <summary>The thread's instructions and cycles so far, if the machine counts them for it.</summary>
    static bool Counts(out ulong ins, out ulong cyc)
    {
        ins = cyc = 0;
        if (_noCounts || !OperatingSystem.IsMacOS()) return false;
        _counts ??= new ulong[2];
        try
        {
            if (thread_selfcounts(1, _counts, 16) != 0) { _noCounts = true; return false; }
        }
        catch (Exception) { _noCounts = true; return false; }
        (ins, cyc) = (_counts[0], _counts[1]);
        return cyc > 0;
    }

    /// <summary>
    /// The fast cores' clock, and what the clock counts of 50 ms of the thread's own work (how long that took on the wall
    /// clock: never less) and of 50 ms asleep. The work is counted to 50 ms of the thread's own time rather than of the
    /// wall's, which on a busy machine gave the thread a tenth of that. (Here and not in the bench's own long method: a
    /// loop in that one has the whole of it compiled again, optimised, in the middle of the counting.)
    /// </summary>
    public static (double work, double wall, double asleep) Weighed()
    {
        // the clock: the chip's fast cores' with one core busy, as Apple gives it; for a chip not known, the fastest a cycle
        // count went over a few short stretches of spinning, each 5 ms of the thread's time (on a busy machine those
        // may all fall on slow cores: so they did with sixteen loops spinning, and it read 2.05 GHz)
        FastPerMs = 0;
        if (Counts(out _, out _))
        {
            FastPerMs = FastClock(Chip()) * 1e6;
            for (int i = 0; i < 24 && FastPerMs <= 0; i++)
            {
                double c0 = ThreadMs(); Counts(out _, out ulong y0);
                var cap = Stopwatch.StartNew();
                while (ThreadMs() - c0 < 5 && cap.ElapsedMilliseconds < 500) { }
                double dc = ThreadMs() - c0; Counts(out _, out ulong y1);
                if (dc > 1) FastPerMs = Math.Max(FastPerMs, (y1 - y0) / dc);
            }
        }
        double w0 = CpuMs();
        var wall = Stopwatch.StartNew();
        while (CpuMs() - w0 < 50 && wall.ElapsedMilliseconds < 10000) { }
        double work = CpuMs() - w0, walled = wall.Elapsed.TotalMilliseconds;
        w0 = CpuMs();
        System.Threading.Thread.Sleep(50);
        return (work, walled, CpuMs() - w0);
    }

    /// <summary>The machine's chip as the system names it («Apple M1 Pro»), or "" if it does not say.</summary>
    public static string Chip()
    {
        try
        {
            nuint size = 0;
            if (sysctlbyname("machdep.cpu.brand_string", null, ref size, IntPtr.Zero, 0) != 0 || size == 0) return "";
            var buf = new byte[(int)size];
            if (sysctlbyname("machdep.cpu.brand_string", buf, ref size, IntPtr.Zero, 0) != 0) return "";
            return System.Text.Encoding.ASCII.GetString(buf, 0, Math.Max(0, (int)size - 1)).Trim();
        }
        catch (Exception) { return ""; }
    }

    /// <summary>The top clock of an Apple chip's fast cores, GHz, or 0 for a chip not known.</summary>
    static double FastClock(string chip) => chip switch
    {
        "Apple M1" => 3.204,
        "Apple M1 Pro" or "Apple M1 Max" or "Apple M1 Ultra" => 3.228,
        "Apple M2" => 3.49,
        "Apple M2 Pro" or "Apple M2 Max" or "Apple M2 Ultra" => 3.504,
        "Apple M3" or "Apple M3 Pro" or "Apple M3 Max" or "Apple M3 Ultra" => 4.056,
        "Apple M4" => 4.4,
        "Apple M4 Pro" or "Apple M4 Max" => 4.512,
        _ => 0,
    };
}
