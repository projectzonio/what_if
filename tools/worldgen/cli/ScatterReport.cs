using System.Diagnostics;
using System.Globalization;
using System.Text;
using WhatIf.WorldGen;

// RG-151: --scatter-dump <file> [--window x0,z0,size,step]. Writes the CSV and scatter-hash.txt (same folder), prints checks (a)-(d). The step is ignored (the grid is the 256 m sector).
static class ScatterReport
{
    static string F(double v, string f = "0.###") { return v.ToString(f, CultureInfo.InvariantCulture); }

    /// <summary>Returns 0 when checks (a)-(c) pass with no excluded instance, 1 otherwise. Check (d) is reported, never failed.</summary>
    public static int Run(GenResult res, WorldSpec spec, string csvPath, double[] w, double maskOffset)
    {
        double x0 = w[0], z0 = w[1], size = w[2];
        var sw = Stopwatch.StartNew();
        var model = new ScatterModel(spec, res) { MaskOffset = maskOffset };
        double ctorMs = sw.Elapsed.TotalMilliseconds;
        int sx0 = ScatterModel.SectorOf(x0), sx1 = ScatterModel.SectorOf(x0 + size - 1e-9), sz0 = ScatterModel.SectorOf(z0), sz1 = ScatterModel.SectorOf(z0 + size - 1e-9);
        var rows = new List<KeyValuePair<string, List<Instance>>>();
        var ms = new List<double>();
        var msTag = new List<string>();
        var audit = new ScatterAudit();
        var csv = new StringBuilder("kind,variant,x,y,z,yaw,scale,sector_x,sector_z\n");
        var perVillage = new int[res.Settlements.Count];
        int total = 0;
        for (int sz = sz0; sz <= sz1; sz++)
            for (int sx = sx0; sx <= sx1; sx++)
            {
                var t = Stopwatch.StartNew();
                var inst = model.Sector(sx, sz);
                ms.Add(t.Elapsed.TotalMilliseconds); msTag.Add(sx + "," + sz + " (" + inst.Count + " inst)");
                rows.Add(new KeyValuePair<string, List<Instance>>("S" + sx + "," + sz, inst));
                model.AuditSector(audit, sx, sz, inst);
                foreach (var it in inst)
                {
                    total++;
                    csv.Append(ScatterKind.Name(it.Kind)).Append(',').Append(it.Variant).Append(',').Append(F(it.X, "0.00")).Append(',').Append(F(it.Y, "0.00")).Append(',').Append(F(it.Z, "0.00")).Append(',')
                       .Append(F(it.Yaw, "0.000")).Append(',').Append(F(it.Scale, "0.000")).Append(',').Append(sx).Append(',').Append(sz).Append('\n');
                    if (it.Kind == ScatterKind.House)
                        for (int s = 0; s < res.Settlements.Count; s++)
                            if (!res.Settlements[s].IsCity && V2.Dist(new V2(it.X, it.Z), res.Settlements[s].C) <= res.Settlements[s].Radius * 1.5) { perVillage[s]++; break; }
                }
            }
        string dir = Path.GetDirectoryName(Path.GetFullPath(csvPath)) ?? ".";
        Directory.CreateDirectory(dir);
        File.WriteAllText(csvPath, csv.ToString(), new UTF8Encoding(false));
        string hash = ScatterModel.Hash(rows);
        File.WriteAllText(Path.Combine(dir, "scatter-hash.txt"), hash + "\n", new UTF8Encoding(false));
        // second pass = the cached lots are warm and a fresh model must give the same hash (determinism shown in the log; the unit tests assert it too)
        string hash2 = new ScatterModel(spec, res) { MaskOffset = maskOffset }.Hash(x0, z0, size);
        var sorted = ms.OrderBy(v => v).ToList();
        Console.WriteLine($"scatter: window {F(x0)},{F(z0)} size {F(size)} -> sectors {audit.Sectors}, instances {total} (trees {audit.Trees}, bushes {audit.Bushes}, houses {audit.Houses}, street trees {audit.StreetTrees}), csv {csvPath}");
        Console.WriteLine($"scatter hash {hash} (second run from a fresh model: {(hash2 == hash ? "identical" : "DIFFERENT " + hash2)}), mask offset {F(maskOffset)}");
        for (int s = 0; s < res.Settlements.Count; s++)
            if (!res.Settlements[s].IsCity) Console.WriteLine($"  houses near {res.Settlements[s].Name} ({F(res.Settlements[s].C.X, "0")},{F(res.Settlements[s].C.Z, "0")}): {perVillage[s]}");
        int fm = 0, fn = 0;
        for (double z = z0; z < z0 + size; z += 16) for (double x = x0; x < x0 + size; x += 16) { fn++; if (model.Mask(x, z) + maskOffset > ScatterModel.MaskThreshold) fm++; }
        Console.WriteLine($"forest mask: {F(100.0 * fm / fn, "0.0")} % of the window above the threshold {F(ScatterModel.MaskThreshold)} (16 m grid)");
        Console.WriteLine($"check (a) road clearance: {audit.ViolationsA} violations over {audit.Instances} instances, smallest margin {F(audit.MinMarginA, "0.00")} m");
        Console.WriteLine($"check (b) span/hole/height band/slope: {audit.ViolationsB} violations");
        Console.WriteLine($"check (c) caps: max trees {audit.MaxTreesInSector} (cap 1500), max buildings {audit.MaxBuildingsInSector} (cap 150): {audit.ViolationsC} violations");
        Console.WriteLine($"check (d) placement time per sector over {sorted.Count} sectors: min {F(sorted[0], "0.000")} ms, median {F(sorted[sorted.Count / 2], "0.000")} ms, max {F(sorted[sorted.Count - 1], "0.000")} ms, p95 {F(sorted[(int)(sorted.Count * 0.95)], "0.000")} ms (model build {F(ctorMs, "0.0")} ms)");
        if (sorted[sorted.Count / 2] > 1.0 || sorted[sorted.Count - 1] > 1.0) Console.WriteLine("  NOTE: over 1 ms per sector somewhere; proposal: split the stage over two pumps (trees+bushes / lots+street trees), design section 5 (not built here)");
        var order = Enumerable.Range(0, ms.Count).OrderByDescending(i => ms[i]).Take(4);
        Console.WriteLine("  slowest sectors (cold, in window order, the first sector also pays JIT): " + string.Join("; ", order.Select(i => msTag[i] + " " + F(ms[i], "0.00") + " ms")));
        // warm pass: same model, village lots cached, JIT done
        var warm = new List<double>();
        for (int sz = sz0; sz <= sz1; sz++)
            for (int sx = sx0; sx <= sx1; sx++) { var t = Stopwatch.StartNew(); model.Sector(sx, sz); warm.Add(t.Elapsed.TotalMilliseconds); }
        warm.Sort();
        Console.WriteLine($"check (d) warm repeat (lots cached): min {F(warm[0], "0.000")} ms, median {F(warm[warm.Count / 2], "0.000")} ms, max {F(warm[warm.Count - 1], "0.000")} ms");
        Console.WriteLine($"excluded instances: {audit.Excluded}");
        foreach (var s in audit.Samples) Console.WriteLine("  FAIL " + s);
        return audit.Pass && hash == hash2 ? 0 : 1;
    }
}
