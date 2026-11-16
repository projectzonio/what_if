using System.Diagnostics;
using System.Globalization;
using WhatIf.WorldGen;

// RG-118: terrain summary (terrain-stats.json), tile benchmark and raw height dump for the preview renderer.
static class Report
{
    const double AtGradeM = 0.5;

    static double R3(double v) { return Math.Round(v, 3) + 0.0; }

    public static JObj Stats(GenResult res, WorldSpec spec)
    {
        var g = res.Graph; var t = res.Terrain;
        var classes = new JObj();
        foreach (var cls in RoadClass.All)
        {
            double km = 0, cut = 0, fill = 0, flat = 0, tunnel = 0, viaduct = 0, bridge = 0, maxCut = 0, maxFill = 0;
            foreach (var e in g.Edges.Where(q => q.Class == cls))
            {
                km += e.LengthM;
                if (e.Span == "tunnel") { tunnel += e.LengthM; continue; }
                if (e.Span == "bridge") { if (e.Layer == 1) bridge += e.LengthM; else viaduct += e.LengthM; continue; }
                for (int i = 0; i + 1 < e.Pts.Length; i++)
                {
                    double ds = V2.Dist(e.Pts[i], e.Pts[i + 1]);
                    double d0 = t.Natural(e.Pts[i].X, e.Pts[i].Z) - e.H[i], d1 = t.Natural(e.Pts[i + 1].X, e.Pts[i + 1].Z) - e.H[i + 1];
                    double d = 0.5 * (d0 + d1);
                    maxCut = Math.Max(maxCut, Math.Max(d0, d1)); maxFill = Math.Max(maxFill, Math.Max(-d0, -d1));
                    if (d > AtGradeM) cut += ds; else if (d < -AtGradeM) fill += ds; else flat += ds;
                }
            }
            if (km <= 0) continue;
            classes.Add(cls, new JObj().Add("km", R3(km / 1000)).Add("cut_km", R3(cut / 1000)).Add("fill_km", R3(fill / 1000)).Add("at_grade_km", R3(flat / 1000))
                .Add("tunnel_km", R3(tunnel / 1000)).Add("viaduct_km", R3(viaduct / 1000)).Add("crossing_bridge_km", R3(bridge / 1000))
                .Add("max_cut_m", R3(maxCut)).Add("max_fill_m", R3(maxFill)));
        }
        var spans = new List<JObj>();
        foreach (var s in g.Spans)
        {
            var n = g.Nodes[s.StartNode];
            spans.Add(new JObj().Add("id", s.Id).Add("kind", s.Kind).Add("class", s.Class).Add("layer", s.Layer).Add("length_m", R3(s.LengthM))
                .Add("cover_m", s.Kind == "tunnel" ? (object)R3(s.CoverDepthM) : null).Add("clearance_m", s.Kind == "bridge" ? (object)R3(s.ClearanceM) : null)
                .Add("x", R3(n.P.X)).Add("z", R3(n.P.Z)));
        }
        double nmin = double.MaxValue, nmax = double.MinValue;
        for (double x = -spec.Half; x <= spec.Half; x += 100)
            for (double z = -spec.Half; z <= spec.Half; z += 100) { double h = t.Natural(x, z); nmin = Math.Min(nmin, h); nmax = Math.Max(nmax, h); }
        return new JObj().Add("seed", spec.Seed).Add("terrain_enabled", t.Enabled).Add("generation_s", R3(res.Seconds))
            .Add("natural_min_m", R3(nmin)).Add("natural_max_m", R3(nmax)).Add("classes", classes).Add("spans", spans);
    }

    /// <summary>SampleTile 256 m at 4 m over every tile of the world, Release, one warm-up pass. Returns ms per tile (sorted).</summary>
    public static JObj TileBench(GenResult res, WorldSpec spec)
    {
        var t = res.Terrain;
        int nt = (int)Math.Round(spec.SizeKm * 1000 / 256.0);
        double x0 = -spec.Half;
        t.SampleTile(x0, x0, 256, 4);
        var ms = new List<double>();
        for (int iz = 0; iz < nt; iz++)
            for (int ix = 0; ix < nt; ix++)
            {
                var sw = Stopwatch.StartNew();
                t.SampleTile(x0 + ix * 256.0, x0 + iz * 256.0, 256, 4);
                ms.Add(sw.Elapsed.TotalMilliseconds);
            }
        ms.Sort();
        return new JObj().Add("tiles", ms.Count).Add("tile_m", 256).Add("step_m", 4).Add("median_ms", R3(ms[ms.Count / 2])).Add("p95_ms", R3(ms[(int)(ms.Count * 0.95)]))
            .Add("max_ms", R3(ms[ms.Count - 1]));
    }

    /// <summary>Raw dump for tools/worldgen/render_net.py: header line "x0 z0 size step n", then (n+1)^2 float32 heights (carved), then (n+1)^2 bytes hole flags.</summary>
    public static void Dump(GenResult res, string path, double x0, double z0, double size, double step)
    {
        var tile = res.Terrain.SampleTile(x0, z0, size, step);
        using var f = File.Create(path);
        var head = System.Text.Encoding.ASCII.GetBytes(string.Format(CultureInfo.InvariantCulture, "{0} {1} {2} {3} {4}\n", x0, z0, size, step, tile.N));
        f.Write(head, 0, head.Length);
        var buf = new byte[tile.Heights.Length * 4];
        Buffer.BlockCopy(tile.Heights, 0, buf, 0, buf.Length);
        f.Write(buf, 0, buf.Length);
        var hb = new byte[tile.Holes.Length];
        for (int i = 0; i < hb.Length; i++) hb[i] = tile.Holes[i] ? (byte)1 : (byte)0;
        f.Write(hb, 0, hb.Length);
    }
}
