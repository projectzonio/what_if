using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using WhatIf.Planet;
using WhatIf.Planet.FarLayer;
using WhatIf.Planet.Runtime;
using WhatIf.Planet.WorldGen;

namespace WhatIf.Planet.Runtime.Experiment
{
    public sealed class Row
    {
        public string Id, Kind, Value, Threshold, Result, Note;
        public static Row Proto(string id, string value, string thr, bool pass, string note) => new Row { Id = id, Kind = "protocol", Value = value, Threshold = thr, Result = pass ? "PASS" : "FAIL", Note = note };
        public static Row Report(string id, string value, string note) => new Row { Id = id, Kind = "report", Value = value, Threshold = "report", Result = "report", Note = note };
        static string San(string s) => (s ?? "").Replace("|", "/").Replace("\r", " ").Replace("\n", " ");
        public string Line() => string.Join("|", San(Id), San(Kind), San(Value), San(Threshold), San(Result), San(Note));
        public static Row Parse(string l) { var p = l.Split('|'); return new Row { Id = p[0], Kind = p[1], Value = p[2], Threshold = p[3], Result = p[4], Note = p.Length > 5 ? p[5] : "" }; }
    }

    /// <summary>A moving camera: great circle through unit direction E with unit tangent T, s0 + speed * t metres along it.</summary>
    public sealed class GreatCircle
    {
        public readonly Vec3 E, T; public readonly double S0, Speed;
        public GreatCircle(Vec3 e, Vec3 t, double s0, double speed) { E = e; T = t; S0 = s0; Speed = speed; }
        public Vec3 Dir(double tSec) { double a = (S0 + Speed * tSec) / CubeSphere.Radius; return (E * Math.Cos(a) + T * Math.Sin(a)).Normalized(); }
        public Vec3 DirAtS(double s) { double a = s / CubeSphere.Radius; return (E * Math.Cos(a) + T * Math.Sin(a)).Normalized(); }
        /// <summary>The drive: crosses the cube edge between +X and +Y at s = 0 perpendicularly; starts 18 km before it (inside face 0), ends 18 km after it.</summary>
        public static GreatCircle Drive(double speed)
        {
            Vec3 x = CubeSphere.FromFaceAB(0, 1.0, 0.31), t = (new Vec3(-1, 1, 0) - x * Vec3.Dot(new Vec3(-1, 1, 0), x)).Normalized();
            return new GreatCircle(x, t, -18000.0, speed);
        }
        /// <summary>RG-173: the same drive over land: crosses the same cube edge at face-0 row ~160 (face 2 column ~160), the block B regions of RG-169 (land with generated worlds); the RG-172 path (row 670) is open ocean on the surface base.</summary>
        public static GreatCircle DriveLand(double speed)
        {
            Vec3 x = CubeSphere.FromFaceAB(0, 1.0, -0.6875), t = (new Vec3(-1, 1, 0) - x * Vec3.Dot(new Vec3(-1, 1, 0), x)).Normalized();
            return new GreatCircle(x, t, -18000.0, speed);
        }
        public static GreatCircle Flight(double speed)
        {
            Vec3 e = CubeSphere.FromFaceAB(2, -0.3, 0.2), t = Vec3.Cross(e, new Vec3(0.2, 0.1, 1)).Normalized();
            return new GreatCircle(e, t, 0.0, speed);
        }
    }

    public static class Stats
    {
        public static double Pct(List<double> v, double q) { if (v.Count == 0) return double.NaN; var s = v.OrderBy(x => x).ToList(); return s[Math.Min(s.Count - 1, (int)Math.Floor(q * (s.Count - 1) + 0.5))]; }
        public static double Slope(List<double> t, List<double> y)
        {
            if (t.Count < 2) return double.NaN; double mt = t.Average(), my = y.Average(), num = 0, den = 0;
            for (int i = 0; i < t.Count; i++) { num += (t[i] - mt) * (y[i] - my); den += (t[i] - mt) * (t[i] - mt); }
            return den > 0 ? num / den : 0;
        }
        public static string F(double v, string f = "F3") => v.ToString(f, CultureInfo.InvariantCulture);
    }

    /// <summary>Everything one real-time run records.</summary>
    public sealed class RunResult
    {
        public readonly List<double> FrameMs = new List<double>(), FrameMsNoFar = new List<double>();
        public readonly List<string> Csv = new List<string>();
        public int Frames, NotReady300AfterWarm, CoverageChecks, CoverageViolations, CoverageViolationsAfterWarm, CoverageChecksAfterWarm, NearVerticesChecked;
        public readonly List<(RegionId Id, double EnterS, double ReadyS)> Entries = new List<(RegionId, double, double)>();
        public readonly List<double> MemT = new List<double>(), MemMiB = new List<double>();
        public double WallS, ProcCpuS, MaxWait600; public int MaxWaiting600, MaxNotReady600;
        public long Gc0, Gc200, Gc300, GcEnd;
        public double SetupS;
        public int BadRandom, BadNear, ChecksWithViolation; public double FirstViolT = -1, LastViolT = -1; public int GcC0, GcC1, GcC2; public double GcPauseMs;
        public readonly List<string> SlowFrames = new List<string>(), ViolDetail = new List<string>();
    }

    public static class Scenarios
    {
        public const ulong Seed = 1;
        public const double R = CubeSphere.Radius;
        [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint ms);
        public static void HighResTimer() { try { timeBeginPeriod(1); } catch (Exception) { } }

        /// <summary>RG-173: the far layer's height function = the surface height the region base uses (PlanetSurface.Height of the unit direction); own PlanetSurface, called from the main thread only.</summary>
        public static Func<Vec3, double> HeightFunc() { var surf = new WhatIf.Planet.Surface.PlanetSurface(Seed); return d => surf.Height(d); }

        /// <summary>Number of region borders and cube edges crossed by a path sampled every 10 m.</summary>
        public static void CrossingCount(GreatCircle p, double seconds, out int regionChanges, out int faceChanges, out List<RegionId> regions)
        {
            regionChanges = 0; faceChanges = 0; regions = new List<RegionId>(); RegionId prev = default; bool have = false;
            for (double s = p.S0; s <= p.S0 + p.Speed * seconds; s += 10.0)
            {
                RegionId r = CubeSphere.RegionOf(p.DirAtS(s), SectorId.RegionLevel);
                if (have && !r.Equals(prev)) { regionChanges++; if (r.Face != prev.Face) faceChanges++; }
                if (!have || !r.Equals(prev)) regions.Add(r);
                prev = r; have = true;
            }
        }

        /// <summary>Share of surface kinds along the path, sampled every 100 m (Ocean/Sea/Lake = water, rest land) and the number of distinct level-10 regions that are not ocean at all.</summary>
        public static string PathKinds(GreatCircle p, double seconds)
        {
            var surf = new WhatIf.Planet.Surface.PlanetSurface(Seed); int water = 0, total = 0; var landRegions = new HashSet<RegionId>(); var regions = new HashSet<RegionId>();
            for (double s = p.S0; s <= p.S0 + p.Speed * seconds; s += 100.0)
            {
                var d = p.DirAtS(s); var k = surf.Kind(d); total++; var reg = CubeSphere.RegionOf(d, SectorId.RegionLevel); regions.Add(reg);
                if (k == WhatIf.Planet.Surface.SurfaceKind.Ocean || k == WhatIf.Planet.Surface.SurfaceKind.Sea || k == WhatIf.Planet.Surface.SurfaceKind.Lake) water++; else landRegions.Add(reg);
            }
            return $"water {100.0 * water / total:F0} % / land {100.0 * (total - water) / total:F0} % of {total} samples (100 m); {landRegions.Count} of {regions.Count} regions on the path have land";
        }

        static void Wait(Stopwatch sw, double target)
        {
            while (true)
            {
                double rem = target - sw.Elapsed.TotalSeconds; if (rem <= 0) return;
                if (rem > 0.002) Thread.Sleep(1); else Thread.SpinWait(50);
            }
        }

        /// <summary>Settles the streamer at a pose before the timed part: pumps frames (60 Hz paced) until the 600 m near set is complete and the far layer has nothing left to build.</summary>
        public static double Settle(PlanetStreamer s, Vec3 dir, double alt, double timeoutS, bool needNear)
        {
            var sw = Stopwatch.StartNew(); double last = 0;
            while (sw.Elapsed.TotalSeconds < timeoutS)
            {
                double now = sw.Elapsed.TotalSeconds; s.Update(dir, alt, Math.Max(1e-3, now - last)); last = now;
                if ((!needNear || s.NearComplete(s.P.LoadRadiusM)) && s.Selection.Requests.Count == 0 && s.Info.FarBuilt == 0 && now > 1.0) break;
                Thread.Sleep(8);
            }
            return sw.Elapsed.TotalSeconds;
        }

        /// <summary>The timed real-time run: 60 Hz pacing, wall-clock motion, per-frame timing of Update only. checks = far coverage every 10th frame (outside the timed part).</summary>
        public static RunResult Run(PlanetStreamer s, Func<double, Vec3> dirAt, double alt, double seconds, double warmS, bool coverage, bool memSamples, Func<Vec3, double> groundH)
        {
            var r = new RunResult(); var proc = Process.GetCurrentProcess(); double cpu0 = proc.TotalProcessorTime.TotalSeconds;
            var sw = Stopwatch.StartNew(); double prev = 0; ulong rng = 12345; RegionId camRegion = default; bool haveRegion = false; double nextMem = 0; bool gc200 = false;
            double t0Streamer = s.NowS; long freq = Stopwatch.Frequency; int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2); var pause0 = GC.GetTotalPauseDuration();
            for (long k = 1; ; k++)
            {
                double target = k / 60.0; if (target > seconds) break; Wait(sw, target);
                double t = sw.Elapsed.TotalSeconds, dt = t - prev; prev = t; Vec3 dir = dirAt(t);
                long a = Stopwatch.GetTimestamp(); s.Update(dir, alt, dt); double ms = (Stopwatch.GetTimestamp() - a) * 1000.0 / freq;
                r.Frames++; r.FrameMs.Add(ms); if (s.Info.FarBuilt == 0) r.FrameMsNoFar.Add(ms);
                var fi = s.Info;
                if (ms >= 2.0 && r.SlowFrames.Count < 200) r.SlowFrames.Add($"t={t:F2}s {ms:F2} ms farBuilt={fi.FarBuilt} integrated={fi.Integrated} gc0/1/2={GC.CollectionCount(0) - g0}/{GC.CollectionCount(1) - g1}/{GC.CollectionCount(2) - g2}");
                if (haveRegion && !fi.CameraRegion.Equals(camRegion)) r.Entries.Add((fi.CameraRegion, s.NowS, s.RegionReadyAt(fi.CameraRegion)));
                camRegion = fi.CameraRegion; haveRegion = true;
                if (t > warmS && fi.NotReadyIn300 > 0) r.NotReady300AfterWarm++;
                r.MaxWaiting600 = Math.Max(r.MaxWaiting600, fi.WaitingForRegion600); r.MaxNotReady600 = Math.Max(r.MaxNotReady600, fi.NotReadyIn600);
                if (memSamples && t >= nextMem) { nextMem = t + 1.0; r.MemT.Add(t); r.MemMiB.Add(s.CacheBytes / 1048576.0); }
                if (memSamples && !gc200 && t >= seconds - 100.0) { gc200 = true; r.Gc200 = GC.GetTotalMemory(true); }
                if (k % 6 == 0) r.Csv.Add(string.Join(",", Stats.F(t, "F2"), Stats.F(ms, "F3"), fi.Wanted, fi.Ready, fi.NotReadyIn300, fi.NotReadyIn600, fi.WaitingForRegion600, fi.Queue, fi.NearCount, fi.FarBuilt, fi.RegionsReady, Stats.F(s.CacheBytes / 1048576.0, "F1"), Stats.F(fi.SpeedMps, "F1")));
                if (coverage && k % 10 == 0)
                {
                    int bad = Coverage(s, dir, R + groundH(dir) + alt, ref rng, out int nearV, out int badNear, r.ViolDetail, t); r.BadNear += badNear; r.BadRandom += bad - badNear; if (bad > 0) { r.ChecksWithViolation++; if (r.FirstViolT < 0) r.FirstViolT = t; r.LastViolT = t; }
                    r.CoverageChecks++; r.CoverageViolations += bad; r.NearVerticesChecked += nearV;
                    if (t > warmS) { r.CoverageChecksAfterWarm++; r.CoverageViolationsAfterWarm += bad; }
                }
            }
            r.GcC0 = GC.CollectionCount(0) - g0; r.GcC1 = GC.CollectionCount(1) - g1; r.GcC2 = GC.CollectionCount(2) - g2; r.GcPauseMs = (GC.GetTotalPauseDuration() - pause0).TotalMilliseconds;
            r.WallS = sw.Elapsed.TotalSeconds; r.ProcCpuS = proc.TotalProcessorTime.TotalSeconds - cpu0;
            return r;
        }

        static double NextD(ref ulong st) => SplitMix64.NextDouble(ref st);

        /// <summary>f1-style coverage of the current selection: 3000 random visible directions + the vertices (every 8th) of every near sector, exactly one cover each.</summary>
        public static int Coverage(PlanetStreamer s, Vec3 dir, double camR, ref ulong st, out int nearVerts, out int nearBad, List<string> detail = null, double tNow = 0)
        {
            var idx = new CoverageIndex(s.Selection, s.Near); int bad = 0; nearVerts = 0; nearBad = 0;
            double thMax = camR > R ? Math.Acos(R / camR) * 0.999 : 0.003;   // below the sea-level sphere the far layer culls nothing by horizon

            Vec3 e1 = Vec3.Cross(dir, new Vec3(0.3, 0.9, 0.1)).Normalized(), e2 = Vec3.Cross(dir, e1);
            for (int i = 0; i < 3000; i++)
            {
                double ct = 1 - NextD(ref st) * (1 - Math.Cos(thMax)), phi = NextD(ref st) * 2 * Math.PI, sn = Math.Sqrt(1 - ct * ct);
                Vec3 q = (dir * ct + (e1 * Math.Cos(phi) + e2 * Math.Sin(phi)) * sn).Normalized(); int cn = idx.Count(q);
                if (cn != 1) { bad++; if (detail != null && detail.Count < 40) detail.Add($"t={tNow:F1}s count={cn} dist={Vec3.Angle(dir, q) * R:F0} m near={s.Near.Count} fallbacks={s.Selection.Fallbacks} requests={s.Selection.Requests.Count} incompleteRoots={s.Selection.IncompleteRoots} camRegionReady={s.Info.CameraRegionReady}"); }
            }
            foreach (var sec in s.Near.Sectors)
            {
                var q = new QuadSector(sec);
                for (int j = 0; j < 65; j += 8) for (int i = 0; i < 65; i += 8) { nearVerts++; if (idx.Count(q.Direction(i, j)) != 1) { bad++; nearBad++; } }
            }
            return bad;
        }

        public static string Csv(RunResult r, string name) => "t,frame_ms,wanted,ready,notready300,notready600,waiting_region600,queue,near,far_built,regions_ready,cache_mib,speed\n" + string.Join("\n", r.Csv) + "\n";
    }
}
