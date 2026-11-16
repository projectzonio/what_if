using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using WhatIf.WorldGen;

// RG-110 CLI. Exit 0 = generated and every validator passed, 1 = a validator failed, 2 = usage / input error.
// --seed overrides the seed of the spec; --source-provenance carries the OSM/DEM licence and sources of the rules into provenance.json.
string? spec = null, rules = null, outDir = null, srcProv = null, dump = null, scatterDump = null; double maskOffset = 0; long? seed = null; bool stats = false, tileBench = false; double[]? window = null;
for (int i = 0; i < args.Length; i++)
{
    string a = args[i];
    string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException("missing value after " + a);
    try
    {
        switch (a)
        {
            case "--spec": spec = Next(); break;
            case "--rules": rules = Next(); break;
            case "--out": outDir = Next(); break;
            case "--seed": seed = long.Parse(Next()); break;
            case "--source-provenance": srcProv = Next(); break;
            case "--terrain-stats": stats = true; break;
            case "--tile-bench": tileBench = true; break;
            case "--terrain-dump": dump = Next(); break;
            case "--scatter-dump": scatterDump = Next(); break;
            case "--mask-offset": maskOffset = double.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
            case "--window": window = Next().Split(',').Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray(); break;
            default: Console.Error.WriteLine("unknown argument " + a); return 2;
        }
    }
    catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 2; }
}
if (spec == null || rules == null || outDir == null)
{
    Console.Error.WriteLine("usage: --spec <spec.json> --rules <world-rules.json> --out <dir> [--seed N] [--source-provenance <provenance.json>] [--terrain-stats] [--tile-bench] [--terrain-dump <file> [--window x0,z0,size,step]] [--scatter-dump <file.csv> [--window x0,z0,size,step] [--mask-offset v]]");
    return 2;
}
try
{
    var rulesText = File.ReadAllText(rules, Encoding.UTF8);
    string sha;
    using (var h = SHA256.Create()) sha = string.Concat(h.ComputeHash(File.ReadAllBytes(rules)).Select(b => b.ToString("x2")));
    var wr = WorldRules.FromJson(rulesText, sha);
    var ws = WorldSpec.FromJson(File.ReadAllText(spec, Encoding.UTF8));
    if (seed.HasValue) ws.Seed = seed.Value;
    var res = Generator.Run(ws, wr);
    foreach (var l in res.Log) Console.WriteLine("  " + l);
    var findings = Validators.All(res.Graph, wr, res.Terrain);
    JObj? prov = srcProv != null ? Json.Parse(File.ReadAllText(srcProv, Encoding.UTF8)) as JObj : null;
    var files = Export.Build(res.Graph, ws, wr, prov, out string hash);
    Directory.CreateDirectory(outDir);
    foreach (var kv in files) File.WriteAllText(Path.Combine(outDir, kv.Key + ".json"), kv.Value, new UTF8Encoding(false));
    File.WriteAllText(Path.Combine(outDir, "hash.txt"), hash + "\n", new UTF8Encoding(false));
    Console.WriteLine($"seed {ws.Seed}: {res.Graph.Nodes.Count} nodes, {res.Graph.Edges.Count} edges, {res.Graph.Spans.Count} spans, generation {res.Seconds:0.00} s, hash {hash}");
    if (stats)
    {
        var st = Report.Stats(res, ws);
        if (tileBench) st.Add("tile_bench", Report.TileBench(res, ws));
        File.WriteAllText(Path.Combine(outDir, "terrain-stats.json"), Json.Write(st, true) + "\n", new UTF8Encoding(false));
    }
    if (dump != null)
    {
        var w = window ?? new[] { -ws.Half, -ws.Half, ws.SizeKm * 1000, 8.0 };
        Report.Dump(res, dump, w[0], w[1], w[2], w[3]);
    }
    int scatterRc = 0;
    if (scatterDump != null)
    {
        var w = window ?? new[] { -ws.Half, -ws.Half, ws.SizeKm * 1000, 8.0 };
        scatterRc = ScatterReport.Run(res, ws, scatterDump, w, maskOffset);
    }
    Console.WriteLine($"validators: {findings.Count} finding(s)");
    foreach (var f in findings.Take(40)) Console.WriteLine("  FAIL " + f);
    return findings.Count == 0 && res.Unconnected == 0 && scatterRc == 0 ? 0 : 1;
}
catch (Exception ex) when (ex is IOException || ex is FormatException || ex is InvalidOperationException || ex is ArgumentException)
{
    Console.Error.WriteLine("error: " + ex.Message);
    return 2;
}
