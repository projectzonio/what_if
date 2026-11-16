using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using WhatIf.Planet;
using WhatIf.Planet.Anchors;
using WhatIf.Planet.Surface;
using static WhatIf.Planet.Anchors.Experiment.Program;
using WorldRules = WhatIf.WorldGen.WorldRules;

namespace WhatIf.Planet.Anchors.Experiment
{
    /// <summary>RG-184 k1 diagnostics: every segment that ends at the vertical-curve stall exit, with joint grades, cut / fill bounds and the curve radius at the stall, for the
    /// RG-182 joints (None) and the kept fix (Slide, candidate b). Replaces the hard-coded `seg` mode.</summary>
    public static class Stalls
    {
        /// <summary>Row k1 inputs per area: stall exits / stations beyond / slowest segment of the RG-182 joints (None) and of the kept fix (Slide), each remaining exit named.</summary>
        public sealed class Summary
        {
            public int Exits, Stations, ExitsBefore, StationsBefore, Moved; public double MaxMs, MaxMsBefore; public string Slowest = ""; public readonly List<string> Named = new List<string>();
        }

        /// <summary>stalls &lt;dir&gt; [link key hex] [segment]: without a link, every stall-exit segment of both areas; with one, that segment only (any segment, stalled or not).</summary>
        public static int Run(string dir, List<Area> areas, string linkHex, int segK)
        {
            var sb = new StringBuilder(); void W(string s) { Console.WriteLine(s); sb.AppendLine(s); }
            Collect(areas, W, linkHex, segK);
            File.WriteAllText(Path.Combine(dir, "stalls.txt"), sb.ToString());
            return 0;
        }

        public static Dictionary<string, Summary> Collect(List<Area> areas, Action<string> W, string linkHex = null, int segK = -1)
        {
            var res = new Dictionary<string, Summary>();
            foreach (var a in areas)
            {
                var sets = new Dictionary<CorridorBuilder.StallFix, AnchorSet>();
                foreach (var fx in new[] { CorridorBuilder.StallFix.None, CorridorBuilder.StallFix.Slide })
                {
                    var surf = new PlanetSurface(Seed); var cb = new CorridorBuilder(surf) { Fix = fx };
                    AnchorSet.Build(new SettlementField(Seed, surf), cb, a.Regions, false);   // warm-up (JIT, surface tables); timed build follows
                    sets[fx] = AnchorSet.Build(new SettlementField(Seed, surf), cb, a.Regions, false);
                }
                var none = sets[CorridorBuilder.StallFix.None]; var sum = new Summary(); res[a.Name] = sum;
                foreach (var kv in sets)
                {
                    var segs = kv.Value.Segments.Values.SelectMany(x => x).ToList();
                    int moved = kv.Value.Corridors.Where(c => c.Routed).Count(c => !c.Joints.SequenceEqual(none.Corridors.First(o => o.Link.Key == c.Link.Key).Joints));
                    W($"area {a.Name} fix {kv.Key}: {segs.Count} segments, stall exits {segs.Count(x => x.BeyondCurve > 0)} ({segs.Sum(x => x.BeyondCurve)} stations), max {segs.Max(x => x.Seconds) * 1000:F0} ms, P95 {Stats.Q(segs.Select(x => x.Seconds * 1000).ToList(), 0.95):F1} ms, corridors with moved joints {moved}");
                    if (kv.Key == CorridorBuilder.StallFix.None) { sum.ExitsBefore = segs.Count(x => x.BeyondCurve > 0); sum.StationsBefore = segs.Sum(x => x.BeyondCurve); sum.MaxMsBefore = segs.Max(x => x.Seconds) * 1000; }
                    else { sum.Exits = segs.Count(x => x.BeyondCurve > 0); sum.Stations = segs.Sum(x => x.BeyondCurve); sum.MaxMs = segs.Max(x => x.Seconds) * 1000; sum.Moved = moved; }
                }
                foreach (var kv in sets)
                    foreach (var c in kv.Value.Corridors.Where(c => c.Routed))
                        for (int k = 0; k + 1 < c.Joints.Length; k++)
                        {
                            var g = kv.Value.Segments[c.Link.Key][k];
                            bool pick = linkHex != null ? c.Link.Key.ToString("X16") == linkHex.ToUpperInvariant() && k == segK : g.BeyondCurve > 0;
                            if (pick) { string cause = Describe(W, a.Name, kv.Key, c, k, g, none.Corridors.First(o => o.Link.Key == c.Link.Key)); if (kv.Key == CorridorBuilder.StallFix.Slide && g.BeyondCurve > 0) sum.Named.Add($"{c.Link.RoadClass} {c.Link.A}-{c.Link.B} seg {k}: {g.BeyondCurve} st., {g.Seconds * 1000:F0} ms, {cause}"); }
                        }
                var sl = sets[CorridorBuilder.StallFix.Slide];
                var w = sl.Corridors.Where(c => c.Routed).SelectMany(c => sl.Segments[c.Link.Key].Select(g => (c, g))).OrderByDescending(t => t.g.Seconds).First();
                sum.Slowest = $"{w.c.Link.RoadClass} {w.c.Link.A}-{w.c.Link.B} seg {w.g.Index}, {w.g.LengthM:F0} m, {w.g.Seconds * 1000:F0} ms, {w.g.BeyondCurve} stations beyond";
            }
            return res;
        }

        static string Describe(Action<string> W, string area, CorridorBuilder.StallFix fx, Corridor c, int k, Segment g, Corridor before)
        {
            string cls = c.Link.RoadClass; double kc = 1.0 / WorldRules.VRadiusMin(cls, true), ks = 1.0 / WorldRules.VRadiusMin(cls, false);
            int i0 = c.Joints[k], i1 = c.Joints[k + 1];
            W($"-- area {area} fix {fx} {cls} {c.Link.A} - {c.Link.B} key {c.Link.Key:X16} segment {k} of {c.Joints.Length - 1}: {g.LengthM:F0} m, stations {i0}..{i1} of {c.X.Length} (RG-182 joints {string.Join(",", before.Joints)}), trim {c.TrimStart}/{c.TrimEnd}");
            W($"   joints: H {c.PinH[k]:F2} / {c.PinH[k + 1]:F2} m, grade {c.PinG[k]:F4} / {c.PinG[k + 1]:F4} (class max {CorridorBuilder.MaxGrade(cls):F3}, profile cap {CorridorBuilder.MaxGrade(cls) * CorridorBuilder.GradeUse:F4}), ground {c.Nat[i0]:F2} / {c.Nat[i1]:F2} m; crest R min {1 / kc:F0} m, sag R min {1 / ks:F0} m");
            W($"   solve {g.Seconds * 1000:F0} ms, beyond {g.BeyondCurve}, infeasible {g.Infeasible}, max grade {g.MaxGrade:F4}");
            var y = g.Y;
            CorridorBuilder.ProfileInputs(c, k, out var s, out _, out var lo, out var hi, out _, out var pin);
            int shown = 0, atBound = 0, atCap = 0, nearJoint = 0; double cap = CorridorBuilder.MaxGrade(cls) * CorridorBuilder.GradeUse;
            for (int i = 1; i + 1 < y.Length; i++)
            {
                double a = s[i] - s[i - 1], b = s[i + 1] - s[i]; if (a < 1e-6 || b < 1e-6) continue;
                double kk = ((y[i + 1] - y[i]) / b - (y[i] - y[i - 1]) / a) / ((a + b) / 2);
                bool crest = kk < 0; double lim = crest ? kc : ks;
                if (Math.Abs(kk) <= lim * 1.02) continue;
                if (Math.Abs(y[i] - lo[i]) < 1e-6 || Math.Abs(y[i] - hi[i]) < 1e-6) atBound++;
                if (Math.Max(Math.Abs((y[i] - y[i - 1]) / a), Math.Abs((y[i + 1] - y[i]) / b)) >= 0.98 * cap) atCap++;
                if (Math.Min(s[i], s[s.Length - 1] - s[i]) <= 150) nearJoint++;
                if (shown++ >= 12) continue;
                string at = Math.Abs(y[i] - lo[i]) < 1e-6 ? "at cut bound" : Math.Abs(y[i] - hi[i]) < 1e-6 ? "at fill bound" : pin[i] ? "pinned" : "free";
                W($"   beyond: s {s[i]:F0} m (from end {s[s.Length - 1] - s[i]:F0} m), {(crest ? "crest" : "sag")} R {1 / Math.Abs(kk):F0} m (limit {1 / lim:F0}), y {y[i]:F2}, ground {c.Nat[i0 + i]:F2}, bounds {lo[i]:F2}..{hi[i]:F2} ({at}), grade in/out {(y[i] - y[i - 1]) / a:F4} / {(y[i + 1] - y[i]) / b:F4}");
            }
            if (shown > 12) W($"   ... {shown - 12} more stations beyond");
            string cause = $"{shown} stations beyond: {atCap} at a grade-cap break (bounded solve clips the rounded profile at {cap:F4}), {atBound} at a cut / fill bound, {nearJoint} within 150 m of a pinned joint";
            W("   cause: " + cause); return cause;
        }
    }
}
