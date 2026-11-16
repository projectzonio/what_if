using System;
using System.Collections.Generic;
using System.Diagnostics;
using WhatIf.Planet;
using WhatIf.Planet.Surface;

namespace WhatIf.Planet.FarLayer.Experiment
{
    public sealed class Row
    {
        public string Id, Kind, Value, Threshold, Result, Note;
        public string Csv() => string.Join(",", new[] { Id, Kind, Q(Value), Q(Threshold), Result, Q(Note) });
        static string Q(string s) => s != null && (s.Contains(",") || s.Contains("\"")) ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        public static Row Proto(string id, string value, string thr, bool pass, string note) => new Row { Id = id, Kind = "protocol", Value = value, Threshold = thr, Result = pass ? "PASS" : "FAIL", Note = note };
        public static Row Report(string id, string value, string note) => new Row { Id = id, Kind = "report", Value = value, Threshold = "report", Result = "report", Note = note };
    }

    /// <summary>A moving camera: great circle through unit direction E with unit tangent T, s metres along it at frame k (60 frames/s).</summary>
    public sealed class CamPath
    {
        public readonly Vec3 E, T; public readonly double S0, Speed, Alt; public readonly bool AboveSea; public readonly PlanetSurface Surf;
        public CamPath(PlanetSurface s, Vec3 e, Vec3 t, double s0, double speed, double alt, bool aboveSea) { Surf = s; E = e; T = t; S0 = s0; Speed = speed; Alt = alt; AboveSea = aboveSea; }
        public const int Fps = 60;
        public FarCamera At(int frame)
        {
            double a = (S0 + Speed * frame / Fps) / CubeSphere.Radius, c = Math.Cos(a), sn = Math.Sin(a);
            Vec3 dir = E * c + T * sn, fwd = T * c - E * sn; double h = Surf.Height(dir);
            return new FarCamera { Pos = dir * (CubeSphere.Radius + (AboveSea ? Alt : h + Alt)), Ground = dir * (CubeSphere.Radius + h), Fwd = fwd };
        }
    }

    /// <summary>The six cameras of the ticket for one seed (points found with the RG-168 procedures, restricted to face interiors |a|,|b| &lt; 0.8 so the top-view images fit on one face).</summary>
    public sealed class Cameras
    {
        public readonly string[] Names = { "a_coast_2m", "b_lakeshore_2m", "c_land_300m", "d_land_10km" };
        public FarCamera[] Fixed = new FarCamera[4]; public Vec3[] Dirs = new Vec3[4];
        public CamPath Drive, Flight; public Vec3 DriveEdge;

        public static Vec3 RandomDir(ref ulong st)
        {
            while (true) { var v = new Vec3(SplitMix64.NextDouble(ref st) * 2 - 1, SplitMix64.NextDouble(ref st) * 2 - 1, SplitMix64.NextDouble(ref st) * 2 - 1); double l = v.Length; if (l > 0.05 && l <= 1) return v / l; }
        }
        static bool Interior(Vec3 d) { int f; double a, b; CubeSphere.ToFaceAB(d, out f, out a, out b); return Math.Abs(a) < 0.8 && Math.Abs(b) < 0.8; }
        static Vec3 FindLand(PlanetSurface s, ref ulong st) { while (true) { Vec3 d = RandomDir(ref st); if (Interior(d) && s.Sample(d).Kind == SurfaceKind.Land) return d; } }
        static Vec3 FindCoast(PlanetSurface s, ref ulong st)
        {
            while (true)
            {
                Vec3 d = FindLand(s, ref st), e = Vec3.Cross(new Vec3(0, 1, 0), d).Normalized(), n = Vec3.Cross(d, e); double ang = SplitMix64.NextDouble(ref st) * 2 * Math.PI;
                Vec3 t = e * Math.Cos(ang) + n * Math.Sin(ang), prev = d, cur = d; bool hit = false;
                for (int k = 1; k <= 400 && !hit; k++) { cur = (d * CubeSphere.Radius + t * (k * 3000.0)).Normalized(); if (s.Sample(cur).Kind <= SurfaceKind.Sea) hit = true; else prev = cur; }
                if (!hit) continue;
                for (int it = 0; it < 18; it++) { Vec3 m = ((prev + cur) * 0.5).Normalized(); if (s.Sample(m).Kind <= SurfaceKind.Sea) cur = m; else prev = m; }
                if (Interior(prev) && s.Sample(prev).Kind >= SurfaceKind.Beach && s.Sample(prev).Kind != SurfaceKind.Lake) return prev;
            }
        }
        static Vec3 FindLakeShore(PlanetSurface s, ref ulong st)
        {
            while (true)
            {
                Vec3 p = RandomDir(ref st) * CubeSphere.Radius; var l = s.LakeOfCell(PlanetSurface.CellOf(p.X), PlanetSurface.CellOf(p.Y), PlanetSurface.CellOf(p.Z)); if (l == null) continue;
                double th = SplitMix64.NextDouble(ref st) * 2 * Math.PI; Vec3 d = l.PointAt(th, l.Radius(th) + 8.0);
                if (Interior(d) && s.Sample(d).Kind == SurfaceKind.Land) return d;
            }
        }
        static FarCamera Fixed1(PlanetSurface s, Vec3 d, double above)
        {
            int f; double a, b; CubeSphere.ToFaceAB(d, out f, out a, out b); Vec3 east, north; CubeSphere.TangentAxes(f, d, out east, out north); double h = s.Height(d);
            return new FarCamera { Pos = d * (CubeSphere.Radius + h + above), Ground = d * (CubeSphere.Radius + h), Fwd = north };
        }

        public Cameras(PlanetSurface s, ulong seed)
        {
            ulong st = seed * 7 + 3;
            Dirs[0] = FindCoast(s, ref st); Dirs[1] = FindLakeShore(s, ref st); Dirs[2] = FindLand(s, ref st); Dirs[3] = Dirs[2];
            double[] above = { 2.0, 2.0, 300.0, 10000.0 }; for (int i = 0; i < 4; i++) Fixed[i] = Fixed1(s, Dirs[i], above[i]);
            // drive: a great circle through a cube edge point (face + u edge), 1.8 km of land on each side
            bool found = false;
            for (int f = 0; f < 6 && !found; f++)
                for (int k = 0; k < 40 && !found; k++)
                    for (int axis = 0; axis < 2 && !found; axis++)
                    {
                        double b = -0.6 + 1.2 * k / 39.0; Vec3 e = axis == 0 ? CubeSphere.FromFaceAB(f, 1.0, b) : CubeSphere.FromFaceAB(f, b, 1.0), ax = axis == 0 ? CubeSphere.AxisU(f) : CubeSphere.AxisV(f);
                        Vec3 t = (ax - e * Vec3.Dot(ax, e)).Normalized(); bool ok = true;
                        for (int m = -18; m <= 18 && ok; m++) { double a = m * 100.0 / CubeSphere.Radius; var o = s.Sample((e * Math.Cos(a) + t * Math.Sin(a)).Normalized()); ok = o.Kind == SurfaceKind.Land && o.HeightM > 5.0; }
                        if (ok) { Drive = new CamPath(s, e, t, -1800.0, 30.0, 2.0, false); DriveEdge = e; found = true; }
                    }
            if (!found) throw new InvalidOperationException("no land cube-edge crossing found for seed " + seed);
            Vec3 fe = FindLand(s, ref st), fe2 = Vec3.Cross(new Vec3(0, 1, 0), fe).Normalized(), fn = Vec3.Cross(fe, fe2); double ang2 = SplitMix64.NextDouble(ref st) * 2 * Math.PI;
            Flight = new CamPath(s, fe, fe2 * Math.Cos(ang2) + fn * Math.Sin(ang2), 0.0, 250.0, 5000.0, true);
        }
    }

    /// <summary>One far layer + near set + the per-frame step used by every row.</summary>
    public sealed class Sim
    {
        public readonly PlanetSurface Surf; public readonly FarLayer Layer; public readonly NearSet Near = new NearSet(); public readonly FrameSelection Sel = new FrameSelection(), SelF = new FrameSelection();
        public double PrebuildMs; public int PrebuildNodes;
        public Sim(ulong seed, FarParams p = null)
        {
            Surf = new PlanetSurface(seed); Layer = new FarLayer(Surf.Height, p ?? new FarParams());
            var sw = Stopwatch.StartNew(); PrebuildNodes = Layer.Prebuild(3); PrebuildMs = sw.Elapsed.TotalMilliseconds;
        }
        public MorphContext Ctx(in FarCamera c) => new MorphContext { Cam = c.Pos, Ground = c.Ground, P = Layer.P };

        /// <summary>Start-up at a pose: select + build (unbudgeted) until nothing is missing. Returns nodes built.</summary>
        public int Settle(in FarCamera cam)
        {
            Near.Update(cam.Ground.Normalized()); int built = 0;
            for (int i = 0; i < 40; i++) { Layer.Select(cam, Near, Sel); if (Sel.Requests.Count == 0) break; built += Layer.BuildPending(Sel, 0); }
            return built;
        }

        public struct Stat { public double UpdMs, SelMs, FrustumMs, BuildMs; public int Nodes, Tris, SkirtTris, FNodes, FTris, Built, Incomplete, Parentless, Fallbacks; }

        /// <summary>One frame: near set update, all-around selection (timed), frustum selection (count only), budgeted builds.</summary>
        public Stat Frame(in FarCamera cam, double budgetMs)
        {
            var s = new Stat(); long t0 = Stopwatch.GetTimestamp();
            Near.Update(cam.Ground.Normalized()); long t1 = Stopwatch.GetTimestamp();
            Layer.Select(cam, Near, Sel); long t2 = Stopwatch.GetTimestamp();
            Layer.Select(cam, Near, SelF, true, false); long t3 = Stopwatch.GetTimestamp();
            s.UpdMs = Ms(t1 - t0); s.SelMs = Ms(t2 - t1); s.FrustumMs = Ms(t3 - t2);
            s.Nodes = Sel.Nodes.Count; s.Tris = Sel.Triangles; s.SkirtTris = Sel.SkirtTriangles; s.FNodes = SelF.Nodes.Count; s.FTris = SelF.Triangles; s.Incomplete = Sel.IncompleteRoots; s.Fallbacks = Sel.Fallbacks;
            foreach (var r in Sel.Requests) { var n = r.Node; if (n.Level > 0 && !Layer.Node(n.Face, n.Level - 1, n.Row >> 1, n.Col >> 1).Built) s.Parentless++; }
            if (Sel.Requests.Count > 0) { long b0 = Stopwatch.GetTimestamp(); s.Built = Layer.BuildPending(Sel, budgetMs); s.BuildMs = Ms(Stopwatch.GetTimestamp() - b0); }
            return s;
        }
        static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
    }
}
