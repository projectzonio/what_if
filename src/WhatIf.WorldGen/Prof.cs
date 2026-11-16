using System;
using System.Diagnostics;

namespace WhatIf.WorldGen
{
    /// <summary>
    /// RG-173: optional profiling hook. Hook (stage, milliseconds, bytes allocated by the calling thread) is null by default: a Scope then costs two field reads and
    /// never changes any output. Used by the What If region-build profile (tools/whatif-runtime); the hook must be thread safe when regions build in parallel.
    /// </summary>
    public static class Prof
    {
        public static Action<string, double, long> Hook;

        public struct Scope : IDisposable
        {
            readonly string name; readonly long t0, b0; readonly Action<string, double, long> hook;
            public Scope(string name)
            {
                hook = Hook; this.name = name;
                if (hook == null) { t0 = 0; b0 = 0; return; }
                b0 = GC.GetAllocatedBytesForCurrentThread(); t0 = Stopwatch.GetTimestamp();
            }
            public void Dispose()
            {
                if (hook == null) return;
                long t1 = Stopwatch.GetTimestamp(), b1 = GC.GetAllocatedBytesForCurrentThread();
                hook(name, (t1 - t0) * 1000.0 / Stopwatch.Frequency, b1 - b0);
            }
        }
    }
}
