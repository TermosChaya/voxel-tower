using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

/// <summary>
/// The time the calling thread spends working, in milliseconds: what a check of how long a step takes should count,
/// for unlike the wall clock it does not grow while other programs have the processor (the timed checks failed whenever
/// the machine was busy), and unlike the process's time it leaves out what the step's background tasks do meanwhile.
/// On macOS the thread's own time (Mach's thread_info); elsewhere the process's.
/// </summary>
static class BenchClock
{
    [DllImport("libSystem.dylib")] static extern uint mach_thread_self();
    [DllImport("libSystem.dylib")] static extern int thread_info(uint thread, int flavor, int[] info, ref int count);

    const int ThreadBasicInfo = 3, BasicInfoCount = 10;
    static readonly Process Me = Process.GetCurrentProcess();
    [ThreadStatic] static uint _port;
    [ThreadStatic] static int[] _info;

    public static double CpuMs()
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

    /// <summary>What the clock counts of 50 ms of work and of 50 ms asleep. (Here and not in the bench's own long
    /// method: a loop in that one has the whole of it compiled again, optimised, in the middle of the counting.)</summary>
    public static (double work, double asleep) Weighed()
    {
        double c0 = CpuMs();
        var spin = Stopwatch.StartNew();
        while (spin.ElapsedMilliseconds < 50) { }
        double work = CpuMs() - c0;
        c0 = CpuMs();
        System.Threading.Thread.Sleep(50);
        return (work, CpuMs() - c0);
    }
}
