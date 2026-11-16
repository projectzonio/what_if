// RG-128 roadfeel: what a tyre feels. Walks every road edge at 1 m stations on several lateral lines, takes the highest collider surface (road mesh + ground tile) near the design height,
// and reports (a) where something stands above the design surface, (b) sharp grade changes ("lips": the driven surface jumps between two stations 1 m apart). Same classes as the player, no Unity.
// Exit 0: no lip >= --lip-limit (default 0.20 m); 1: lips found; 2: input error. Edge 0 is not counted (see README).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NightRacer.Streaming;
using NightRacer.StreamingRuntime;
using UnityEngine;
using WG = WhatIf.WorldGen;

static class Program
{
    struct Tri { public Vector3 A, B, C; public bool Road; }
    static readonly CultureInfo CI = CultureInfo.InvariantCulture;
    const float G = 4f;
    static readonly Dictionary<long, List<Tri>> grid = new Dictionary<long, List<Tri>>();
    static readonly HashSet<SectorId> loaded = new HashSet<SectorId>();
    static WorldGenMap world;

    static int Main(string[] args)
    {
        string root = FindRoot(), spec = Path.Combine(root, "tools/worldgen/specs/terrain-10km.json"), csv = null;
        long seed = 1; float over = 0.02f, lipLimit = 0.20f; var lips = new List<Lip>(); bool dumpEdges = false; var only = new List<int>();
        for (int i = 0; i < args.Length; i++)
            switch (args[i])
            {
                case "--seed": seed = long.Parse(args[++i], CI); break;
                case "--spec": spec = args[++i]; break;
                case "--over": over = float.Parse(args[++i], CI); break;
                case "--lip-limit": lipLimit = float.Parse(args[++i], CI); break;
                case "--csv": csv = args[++i]; break;
                case "--edge": only.Add(int.Parse(args[++i], CI)); break;
                case "--dump-edges": dumpEdges = true; break;
                default: Console.Error.WriteLine("unknown " + args[i]); return 2;
            }
        WG.WorldRules rules = WG.WorldRules.FromJson(Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(root, "artifacts/worldgen/world-rules.json"))), "x");
        WG.WorldSpec sp = WG.WorldSpec.FromJson(File.ReadAllText(spec)); sp.Seed = seed;
        var res = WG.Generator.Run(sp, rules);
        world = new WorldGenMap(sp, res.Graph, res.Terrain);
        var g = res.Graph; int[] deg = g.Degrees();
        Console.WriteLine("seed " + seed + " nodes " + g.Nodes.Count + " edges " + g.Edges.Count);

        if (dumpEdges)
        {
            foreach (int ei in only)
            {
                var e = g.Edges[ei];
                Console.WriteLine("edge " + ei + " " + e.A + "->" + e.B + " " + e.Class + " " + e.Span + " w " + e.WidthM.ToString("F1", CI) + " len " + e.LengthM.ToString("F0", CI) + " degA " + deg[e.A] + " degB " + deg[e.B] + " A " + Pt(g.Nodes[e.A].P) + " B " + Pt(g.Nodes[e.B].P));
            }
        }

        var rows = new List<string>();
        var csvw = csv != null ? new StreamWriter(csv) : null;
        if (csvw != null) csvw.WriteLine("edge,s,lat,x,z,design,top,road");
        var runs = new List<Run>();
        IEnumerable<WG.GEdge> edges = only.Count > 0 ? only.Select(q => g.Edges[q]) : g.Edges;
        foreach (var e in edges)
        {
            if (e.Span == "tunnel") continue;
            var s = new double[e.Pts.Length];
            for (int i = 1; i < s.Length; i++) s[i] = s[i - 1] + WG.V2.Dist(e.Pts[i], e.Pts[i - 1]);
            double len = s[s.Length - 1];
            float half = (float)e.WidthM * 0.5f;
            foreach (float frac in new[] { 0f, -0.5f, 0.5f, -0.9f, 0.9f })
            {
                Run cur = null; float prevDy = float.NaN;
                for (double d = 0; d <= len; d += 1.0)
                {
                    int i = 0; while (i < s.Length - 2 && s[i + 1] < d) i++;
                    double t = s[i + 1] > s[i] ? (d - s[i]) / (s[i + 1] - s[i]) : 0;
                    double dx = e.Pts[i + 1].X - e.Pts[i].X, dz = e.Pts[i + 1].Z - e.Pts[i].Z, l = Math.Sqrt(dx * dx + dz * dz);
                    float x = (float)(e.Pts[i].X + dx * t - dz / l * frac * half), z = (float)(e.Pts[i].Z + dz * t + dx / l * frac * half);
                    float design = (float)(e.H[i] + (e.H[i + 1] - e.H[i]) * t) + WorldGenMap.Lift;
                    Load(x, z);
                    bool road; float top = Top(x, z, design, out road);
                    if (csvw != null) csvw.WriteLine(e.Id + "," + d.ToString("F0", CI) + "," + frac.ToString("F1", CI) + "," + x.ToString("F2", CI) + "," + z.ToString("F2", CI) + "," + design.ToString("F3", CI) + "," + top.ToString("F3", CI) + "," + (road ? 1 : 0));
                    float dy = top - design;
                    if (top != float.MinValue)
                    {
                        if (!float.IsNaN(prevDy) && Math.Abs(dy - prevDy) >= 0.05f) lips.Add(new Lip { Edge = e.Id, Frac = frac, S = (float)d, Jump = dy - prevDy, X = x, Z = z });
                        prevDy = dy;
                    }
                    else prevDy = float.NaN;
                    if (dy > over && top != float.MinValue)
                    {
                        if (cur == null) { cur = new Run { Edge = e.Id, Frac = frac, S0 = d, X = x, Z = z, Road = road }; runs.Add(cur); }
                        cur.S1 = d; if (dy > cur.Max) { cur.Max = dy; cur.X = x; cur.Z = z; cur.Road = road; }
                    }
                    else cur = null;
                }
            }
        }
        if (csvw != null) csvw.Dispose();
        var grouped = runs.GroupBy(r => r.Edge).OrderByDescending(q => q.Max(r => r.Max)).ToList();
        Console.WriteLine("edges with surface above design > " + over + " m: " + grouped.Count + " of " + g.Edges.Count + "; runs " + runs.Count);
        foreach (var q in grouped.Take(60))
        {
            var e = g.Edges[q.Key]; var w = q.OrderByDescending(r => r.Max).First();
            Console.WriteLine("  edge " + q.Key + " " + e.A + "(d" + deg[e.A] + ")->" + e.B + "(d" + deg[e.B] + ") " + e.Class + "/" + e.Span + " len " + e.LengthM.ToString("F0", CI) + ": max +" + w.Max.ToString("F2", CI) + " m at s " + w.S0.ToString("F0", CI) + ".." + w.S1.ToString("F0", CI) + " lat " + w.Frac.ToString("F1", CI) + " (" + w.X.ToString("F1", CI) + "," + w.Z.ToString("F1", CI) + ") " + (w.Road ? "road mesh" : "GROUND") + "; runs " + q.Count());
        }
        var counted = lips.Where(l => l.Edge != 0).ToList();
        foreach (float th in new[] { 0.05f, 0.10f, 0.20f, 0.50f })
            Console.WriteLine("lips >= " + th.ToString("F2", CI) + " m per 1 m of travel: " + counted.Count(l => Math.Abs(l.Jump) >= th) + " (edge 0 not counted: " + lips.Count(l => l.Edge == 0 && Math.Abs(l.Jump) >= th) + ")");
        var bad = counted.Where(l => Math.Abs(l.Jump) >= lipLimit).OrderByDescending(l => Math.Abs(l.Jump)).ToList();
        foreach (var l in bad.GroupBy(q => q.Edge).Select(q => q.First()).Take(20))
            Console.WriteLine("  LIP edge " + l.Edge + " lat " + l.Frac.ToString("F1", CI) + " s " + l.S.ToString("F0", CI) + " " + l.Jump.ToString("+0.00;-0.00", CI) + " m at " + l.X.ToString("F1", CI) + "," + l.Z.ToString("F1", CI));
        Console.WriteLine(bad.Count == 0 ? "roadfeel: OK, no lip >= " + lipLimit.ToString("F2", CI) + " m" : "roadfeel: " + bad.Count + " lip(s) >= " + lipLimit.ToString("F2", CI) + " m");
        return bad.Count == 0 ? 0 : 1;
    }

    sealed class Lip { public int Edge; public float Frac, S, Jump, X, Z; }
    sealed class Run { public int Edge; public float Frac; public double S0, S1; public float Max, X, Z; public bool Road; }

    static string Pt(WG.V2 p) { return p.X.ToString("F0", CI) + "," + p.Z.ToString("F0", CI); }

    static void Load(float x, float z)
    {
        SectorId c = world.Grid.CellOf(x, z);
        for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                var id = new SectorId(c.X + dx, c.Z + dz);
                if (!loaded.Add(id)) continue;
                if (world.TerrainOn)
                {
                    var gd = world.BuildGround(id);
                    for (int i = 0; i < gd.Idx.Length; i += 3) Add(new Tri { A = gd.Pos[gd.Idx[i]], B = gd.Pos[gd.Idx[i + 1]], C = gd.Pos[gd.Idx[i + 2]], Road = false });
                }
                var ct = world.BuildSector(id);
                for (int i = 0; i < ct.RoadIdx.Count; i += 3) Add(new Tri { A = ct.RoadPos[ct.RoadIdx[i]], B = ct.RoadPos[ct.RoadIdx[i + 1]], C = ct.RoadPos[ct.RoadIdx[i + 2]], Road = true });
            }
    }

    static void Add(Tri t)
    {
        if (Math.Abs(Vector3.Cross(t.B - t.A, t.C - t.A).y) < 1e-6f) return;
        float mnx = Math.Min(t.A.x, Math.Min(t.B.x, t.C.x)), mxx = Math.Max(t.A.x, Math.Max(t.B.x, t.C.x)), mnz = Math.Min(t.A.z, Math.Min(t.B.z, t.C.z)), mxz = Math.Max(t.A.z, Math.Max(t.B.z, t.C.z));
        for (int gz = (int)Math.Floor(mnz / G); gz <= (int)Math.Floor(mxz / G); gz++)
            for (int gx = (int)Math.Floor(mnx / G); gx <= (int)Math.Floor(mxx / G); gx++)
            {
                long k = ((long)gx << 32) ^ (uint)gz;
                if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<Tri>();
                l.Add(t);
            }
    }

    // highest surface in design - 1 m .. design + 2 m
    static float Top(float x, float z, float design, out bool road)
    {
        float y = float.MinValue; road = false;
        long k = ((long)(int)Math.Floor(x / G) << 32) ^ (uint)(int)Math.Floor(z / G);
        if (!grid.TryGetValue(k, out var l)) return y;
        foreach (Tri t in l)
        {
            float ty;
            if (!Down(t, x, z, out ty) || ty > design + 2f || ty < design - 1f || ty <= y) continue;
            y = ty; road = t.Road;
        }
        return y;
    }

    static bool Down(Tri t, float x, float z, out float y)
    {
        y = 0f;
        float d = (t.B.z - t.C.z) * (t.A.x - t.C.x) + (t.C.x - t.B.x) * (t.A.z - t.C.z);
        if (Math.Abs(d) < 1e-9f) return false;
        float l1 = ((t.B.z - t.C.z) * (x - t.C.x) + (t.C.x - t.B.x) * (z - t.C.z)) / d;
        float l2 = ((t.C.z - t.A.z) * (x - t.C.x) + (t.A.x - t.C.x) * (z - t.C.z)) / d;
        float l3 = 1f - l1 - l2;
        const float e = -1e-5f;
        if (l1 < e || l2 < e || l3 < e) return false;
        y = l1 * t.A.y + l2 * t.B.y + l3 * t.C.y;
        return true;
    }

    static string FindRoot()
    {
        for (var d = new DirectoryInfo(Directory.GetCurrentDirectory()); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "AGENTS.md"))) return d.FullName;
        return null;
    }
}
