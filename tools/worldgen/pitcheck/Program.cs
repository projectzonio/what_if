// pitcheck - headless mesh-closed check of the worldgen ground around a node (no Unity process).
// Origin: RG-120 worker harness (found the InTunnel wedge bug). RG-127 made it a tool: arguments instead of positions, exit codes, batch mode.
// RG-130: a ground/pit edge is split at every mesh vertex lying on it (within 1 cm), at any fraction (was: whole metres only, so a wall column at a
// fractional position read as open). --drop-one removes one pit wall triangle to prove the check still reports open.
// Exit: 0 = every checked place closed, 1 = open edges found (listed), 2 = input error.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NightRacer.Streaming;
using NightRacer.StreamingRuntime;
using UnityEngine;
using WG = WhatIf.WorldGen;

static class Program
{
    struct Tri { public Vector3 A, B, C; public char Kind; }

    const string Usage =
        "pitcheck [--root <repo>] [--spec <spec.json>] [--rules <world-rules.json>] [--seed N] (--node <id> | --at x,z | --portals | --rails) [--range m] [--map] [--max-list N] [--drop-one]\n" +
        "  --node <id>   check the ground mesh around graph node <id>\n" +
        "  --at x,z      check around a world position (no node needed)\n" +
        "  --portals     batch: every tunnel portal node of the generated world (table node / closed / open edges)\n" +
        "  --rails       RG-136: list every bridge deck segment side without a barrier (reason, other edge, height difference) + totals; --at x,z --range m limits it; --rail-tol m = height gate (negative = off, pre-RG-136)\n" +
        "  --drop-one    self-check: remove the pit wall triangle nearest the target that no road/tube/bridge covers; the place must then report open\n" +
        "  --range m     half size of the checked square, default 30\n" +
        "  --map         also print an ASCII top view (north up; G digit = m above centre height, R road, T tube, B bridge, F/f pit floor, . nothing)\n" +
        "  defaults: --root = repo found from the current directory, --spec <root>/tools/worldgen/specs/terrain-10km.json, --rules <root>/artifacts/worldgen/world-rules.json, --seed 1\n" +
        "exit 0 closed / 1 open edges / 2 input error";

    static int Main(string[] args)
    {
        string root = null, specPath = null, rulesPath = null, at = null; long seed = 1; int node = -1; double range = 30; int maxList = 25;
        bool portals = false, map = false, rails = false; double railTol = double.NaN, edgesNear = 0;
        try
        {
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                Func<string> val = () => { if (i + 1 >= args.Length) throw new ArgumentException("missing value after " + a); return args[++i]; };
                switch (a)
                {
                    case "--root": root = val(); break;
                    case "--spec": specPath = val(); break;
                    case "--rules": rulesPath = val(); break;
                    case "--seed": seed = long.Parse(val(), CultureInfo.InvariantCulture); break;
                    case "--node": node = int.Parse(val(), CultureInfo.InvariantCulture); break;
                    case "--at": at = val(); break;
                    case "--range": range = double.Parse(val(), CultureInfo.InvariantCulture); break;
                    case "--max-list": maxList = int.Parse(val(), CultureInfo.InvariantCulture); break;
                    case "--portals": portals = true; break;
                    case "--map": map = true; break;
                    case "--edges-near": edgesNear = double.Parse(val(), CultureInfo.InvariantCulture); break;
                    case "--rails": rails = true; break;
                    case "--rail-tol": railTol = double.Parse(val(), CultureInfo.InvariantCulture); break;
                    case "--drop-one": dropOne = true; break;
                    case "-h": case "--help": Console.WriteLine(Usage); return 0;
                    default: throw new ArgumentException("unknown argument " + a);
                }
            }
            int modes = (node >= 0 ? 1 : 0) + (at != null && !rails ? 1 : 0) + (portals ? 1 : 0) + (rails ? 1 : 0);
            if (modes != 1) throw new ArgumentException("give exactly one of --node, --at, --portals, --rails");
        }
        catch (Exception e) { Console.Error.WriteLine("pitcheck: " + e.Message); Console.Error.WriteLine(Usage); return 2; }

        if (root == null) root = FindRoot();
        if (root == null) { Console.Error.WriteLine("pitcheck: repo root not found (no AGENTS.md above the current directory); pass --root"); return 2; }
        if (specPath == null) specPath = Path.Combine(root, "tools/worldgen/specs/terrain-10km.json");
        if (rulesPath == null) rulesPath = Path.Combine(root, "artifacts/worldgen/world-rules.json");
        foreach (string p in new[] { specPath, rulesPath })
            if (!File.Exists(p)) { Console.Error.WriteLine("pitcheck: file not found: " + p); return 2; }

        WG.WorldRules rules = WG.WorldRules.FromJson(Encoding.UTF8.GetString(File.ReadAllBytes(rulesPath)), Sha(rulesPath));
        WG.WorldSpec spec = WG.WorldSpec.FromJson(File.ReadAllText(specPath));
        spec.Seed = seed;
        var res =WG.Generator.Run(spec, rules);
        Console.WriteLine("rules " + Sha(rulesPath).Substring(0, 8) + " seed " + spec.Seed + " nodes " + res.Graph.Nodes.Count);
        var world = new WorldGenMap(spec, res.Graph, res.Terrain);

        if (rails)
        {
            if (!double.IsNaN(railTol)) WorldGenMap.RailHeightTolM = railTol;
            double rx = 0, rz = 0; bool limit = at != null;
            if (limit)
            {
                string[] q = at.Split(',');
                if (q.Length != 2 || !double.TryParse(q[0], NumberStyles.Float, CultureInfo.InvariantCulture, out rx) || !double.TryParse(q[1], NumberStyles.Float, CultureInfo.InvariantCulture, out rz))
                { Console.Error.WriteLine("pitcheck: --at expects x,z"); return 2; }
            }
            int built, bedges;
            var all = world.RailReport(out built, out bedges);
            var cnt = new SortedDictionary<string, int>(); int shown = 0;
            Console.WriteLine("seed " + seed + " rail tol " + WorldGenMap.RailHeightTolM.ToString(CultureInfo.InvariantCulture) + " m: bridge edges " + bedges + ", barrier segments built " + built + ", skipped " + all.Count);
            Console.WriteLine("edge	k	side	x	z	deckY	reason	other	otherBridge	deck-other");
            foreach (var r in all)
            {
                string key = r.Reason + (r.Reason == "roadBelow" ? (r.OtherBridge ? ":bridge" : ":ground") : "");
                int c; cnt.TryGetValue(key, out c); cnt[key] = c + 1;
                if (limit && (Math.Abs(r.Pos.x - rx) > range || Math.Abs(r.Pos.z - rz) > range)) continue;
                if (shown++ < maxList)
                    Console.WriteLine(r.Edge + "	" + r.K + "	" + r.Side + "	" + r.Pos.x.ToString("F1", CultureInfo.InvariantCulture) + "	" + r.Pos.z.ToString("F1", CultureInfo.InvariantCulture) + "	" + r.Pos.y.ToString("F1", CultureInfo.InvariantCulture) + "	" + r.Reason + "	" + r.Other + "	" + r.OtherBridge + "	" + r.DeckMinusOther.ToString("F2", CultureInfo.InvariantCulture));
            }
            if (shown > maxList) Console.WriteLine("... " + (shown - maxList) + " more (raise --max-list)");
            foreach (var kv in cnt) Console.WriteLine("total " + kv.Key + " " + kv.Value);
            Console.WriteLine("total skipped " + all.Count + " (all segments, listing " + (limit ? "limited to " + range.ToString(CultureInfo.InvariantCulture) + " m around " + at : "unlimited") + ")");
            return 0;
        }

        if (portals)
        {
            var list = TunnelPortals(res.Graph);
            Console.WriteLine("seed " + seed + ": " + list.Count + " tunnel portal nodes");
            Console.WriteLine("node\tclosed\topen_edges");
            int open = 0;
            foreach (int n in list)
            {
                WG.GNode g = res.Graph.Nodes[n];
                var leaks = Check(world, (float)g.P.X, (float)g.P.Z, range, null, false);
                if (leaks.Count > 0) open++;
                Console.WriteLine(n + "\t" + (leaks.Count == 0 ? "yes" : "NO") + "\t" + leaks.Count);
                for (int i = 0; i < leaks.Count && i < maxList; i++) Console.WriteLine("\t\t" + leaks[i]);
            }
            Console.WriteLine("portals " + list.Count + ", open " + open);
            return open == 0 ? 0 : 1;
        }

        float cx, cz, cy = 0f;
        if (node >= 0)
        {
            if (node >= res.Graph.Nodes.Count) { Console.Error.WriteLine("pitcheck: node " + node + " not in the graph (" + res.Graph.Nodes.Count + " nodes)"); return 2; }
            WG.GNode gn = res.Graph.Nodes[node];
            cx = (float)gn.P.X; cz = (float)gn.P.Z; cy = (float)gn.Y;
            Console.WriteLine("node " + node + " at " + cx.ToString("F1", CultureInfo.InvariantCulture) + ", " + cz.ToString("F1", CultureInfo.InvariantCulture) + " y " + cy.ToString("F1", CultureInfo.InvariantCulture) + " kind " + gn.Kind);
            foreach (WG.GEdge ge in res.Graph.Edges)   // RG-138: the legs of the node (id, span, width, length, which end)
                if (ge.A == node || ge.B == node) Console.WriteLine("  leg edge " + ge.Id + " span " + ge.Span + " width " + ge.WidthM.ToString("F1", CultureInfo.InvariantCulture) + " m length " + ge.LengthM.ToString("F1", CultureInfo.InvariantCulture) + " m, node at its " + (ge.A == node ? "start" : "end"));
        }
        else
        {
            string[] xz = at.Split(',');
            if (xz.Length != 2 || !float.TryParse(xz[0], NumberStyles.Float, CultureInfo.InvariantCulture, out cx) || !float.TryParse(xz[1], NumberStyles.Float, CultureInfo.InvariantCulture, out cz))
            { Console.Error.WriteLine("pitcheck: --at expects x,z"); return 2; }
            cy = world.GroundY(cx, cz);
            Console.WriteLine("at " + cx.ToString("F1", CultureInfo.InvariantCulture) + ", " + cz.ToString("F1", CultureInfo.InvariantCulture) + " ground y " + cy.ToString("F1", CultureInfo.InvariantCulture));
        }
        if (edgesNear > 0)   // RG-139: every road edge with a sample within --edges-near m of the target (id, span, layer, class, width, nearest sample, its height) + the spans it belongs to
        {
            foreach (WG.GEdge ge in res.Graph.Edges)
            {
                int bi = -1; double bd = edgesNear;
                for (int k = 0; k < ge.Pts.Length; k++) { double d = Math.Sqrt((ge.Pts[k].X - cx) * (ge.Pts[k].X - cx) + (ge.Pts[k].Z - cz) * (ge.Pts[k].Z - cz)); if (d <= bd) { bd = d; bi = k; } }
                if (bi < 0) continue;
                double hp = ge.H[bi]; double bp = double.MaxValue; int seg = -1; double uu = 0, lat = 0;   // H interpolated at the closest point of the polyline (not only at a sample)
                for (int k = 0; k + 1 < ge.Pts.Length; k++)
                {
                    double ax = ge.Pts[k].X, az = ge.Pts[k].Z, dx = ge.Pts[k + 1].X - ax, dz = ge.Pts[k + 1].Z - az, l2 = dx * dx + dz * dz;
                    double u = l2 > 0 ? Math.Max(0, Math.Min(1, ((cx - ax) * dx + (cz - az) * dz) / l2)) : 0;
                    double ex = ax + dx * u - cx, ez = az + dz * u - cz, dd = ex * ex + ez * ez;
                    if (dd < bp) { bp = dd; hp = ge.H[k] + (ge.H[k + 1] - ge.H[k]) * u; seg = k; uu = u; lat = l2 > 0 ? ((cx - ax) * -dz + (cz - az) * dx) / Math.Sqrt(l2) : 0; }
                }
                string sp = " seg " + seg + "+" + uu.ToString("F2", CultureInfo.InvariantCulture) + " lateral(left+) " + lat.ToString("F2", CultureInfo.InvariantCulture) + " m, pts[0..2] (" + ge.Pts[0].X.ToString("F1", CultureInfo.InvariantCulture) + "," + ge.Pts[0].Z.ToString("F1", CultureInfo.InvariantCulture) + ") (" + ge.Pts[1].X.ToString("F1", CultureInfo.InvariantCulture) + "," + ge.Pts[1].Z.ToString("F1", CultureInfo.InvariantCulture) + ") (" + ge.Pts[ge.Pts.Length - 1].X.ToString("F1", CultureInfo.InvariantCulture) + "," + ge.Pts[ge.Pts.Length - 1].Z.ToString("F1", CultureInfo.InvariantCulture) + ") Hproj " + hp.ToString("F2", CultureInfo.InvariantCulture) + " at " + Math.Sqrt(bp).ToString("F1", CultureInfo.InvariantCulture) + " m";
                foreach (WG.GSpan s in res.Graph.Spans) if (s.Edges.Contains(ge.Id)) sp += " span#" + s.Id + "(" + s.Kind + (s.Viaduct ? " viaduct" : "") + " clear " + s.ClearanceM.ToString("F2", CultureInfo.InvariantCulture) + " cover " + s.CoverDepthM.ToString("F1", CultureInfo.InvariantCulture) + ")";
                Console.WriteLine("  edge " + ge.Id + " " + ge.Span + " layer " + ge.Layer + " " + ge.Class + " w " + ge.WidthM.ToString("F1", CultureInfo.InvariantCulture) + " len " + ge.LengthM.ToString("F0", CultureInfo.InvariantCulture) + " nodes " + ge.A + "->" + ge.B
                    + ": nearest k " + bi + "/" + (ge.Pts.Length - 1) + " (" + ge.Pts[bi].X.ToString("F1", CultureInfo.InvariantCulture) + ", " + ge.Pts[bi].Z.ToString("F1", CultureInfo.InvariantCulture) + ") " + bd.ToString("F1", CultureInfo.InvariantCulture) + " m away, H " + ge.H[bi].ToString("F2", CultureInfo.InvariantCulture) + sp);
            }
        }
        var sb = map ? new StringBuilder() : null;
        List<string> found = Check(world, cx, cz, range, sb, true, cy);
        for (int i = 0; i < found.Count && i < maxList; i++) Console.WriteLine("OPEN edge " + found[i]);
        if (found.Count > maxList) Console.WriteLine("... " + (found.Count - maxList) + " more");
        Console.WriteLine("boundary edges not on a tile border within " + range.ToString(CultureInfo.InvariantCulture) + " m: " + found.Count + (found.Count == 0 ? "  -> CLOSED" : "  -> OPEN"));
        if (sb != null) { Console.WriteLine("map (north up, east right, 1 m cells, centre = target):"); Console.Write(sb.ToString()); }
        return found.Count == 0 ? 0 : 1;
    }

    static string Sha(string path)
    {
        using (var h = System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
    }

    static string FindRoot()
    {
        for (var d = new DirectoryInfo(Directory.GetCurrentDirectory()); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "AGENTS.md"))) return d.FullName;
        return null;
    }

    // a Portal node with a tunnel edge on at least one side
    static List<int> TunnelPortals(WG.RoadGraph g)
    {
        var set = new SortedSet<int>();
        foreach (WG.GEdge e in g.Edges)
        {
            if (e.Span != "tunnel") continue;
            if (g.Nodes[e.A].Kind == WG.NodeKind.Portal) set.Add(e.A);
            if (g.Nodes[e.B].Kind == WG.NodeKind.Portal) set.Add(e.B);
        }
        return new List<int>(set);
    }

    // Builds the 3x3 sectors around (px,pz): ground + pit, road, tunnel, bridge. Open = ground/pit edge used by one triangle only
    // (not on a sector border, where the neighbour tile closes it). Optionally fills an ASCII top view.
    static bool dropOne;

    static List<string> Check(WorldGenMap world, float px, float pz, double range, StringBuilder map, bool verbose, float cy = 0f)
    {
        SectorId c0 = world.Grid.CellOf(px, pz);
        var tris = new List<Tri>();
        // vertices of the regular tile grid + skirt; everything after belongs to the portal pit (RG-120). 256 m sector, TileStep 4 m, skirt = one ring of border verts
        int n1 = (int)(256f / WorldGenMap.TileStep) + 1; int pitFirst = n1 * n1 + 4 * n1;
        for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                var id = new SectorId(c0.X + dx, c0.Z + dz);
                WorldGenMap.GroundData gd = world.BuildGround(id);
                if (verbose) Console.WriteLine("tile " + id + " tris " + gd.Tris + " pit tris " + gd.PitTris + " pit wall quads " + gd.PitWalls + " verts " + gd.Pos.Length);
                for (int i = 0; i < gd.Idx.Length; i += 3)
                {
                    bool pit = gd.Idx[i] >= pitFirst && gd.Idx[i + 1] >= pitFirst && gd.Idx[i + 2] >= pitFirst;
                    Vector3 nn = Vector3.Cross(gd.Pos[gd.Idx[i + 1]] - gd.Pos[gd.Idx[i]], gd.Pos[gd.Idx[i + 2]] - gd.Pos[gd.Idx[i]]).normalized;
                    tris.Add(new Tri { A = gd.Pos[gd.Idx[i]], B = gd.Pos[gd.Idx[i + 1]], C = gd.Pos[gd.Idx[i + 2]], Kind = pit ? (Math.Abs(nn.y) > 0.5f ? 'F' : 'W') : 'G' });
                }
                WorldGenMap.Content ct = world.BuildSector(id);
                AddBuf(tris, ct.RoadPos, ct.RoadIdx, 'R');
                AddBuf(tris, ct.Tun.Pos, ct.Tun.Idx, 'T');
                AddBuf(tris, ct.Brg.Pos, ct.Brg.Idx, 'B');
            }
        if (dropOne)
        {
            int best = -1; double bd = double.MaxValue;
            for (int i = 0; i < tris.Count; i++)
            {
                Tri t = tris[i];
                if (t.Kind != 'W' || Vector3.Cross(t.B - t.A, t.C - t.A).magnitude < 0.1f) continue;   // area > 0.05 m2: a wall quad with bot = top is degenerate
                Vector3 m = (t.A + t.B + t.C) / 3f;
                double d = (m.x - px) * (m.x - px) + (m.z - pz) * (m.z - pz);
                if (d < bd && !Covered(tris, m.x, m.z, m.y)) { bd = d; best = i; }
            }
            if (best < 0) Console.WriteLine("drop-one: no uncovered pit wall triangle in the 3x3 sectors");
            else
            {
                Vector3 m = (tris[best].A + tris[best].B + tris[best].C) / 3f;
                Console.WriteLine("drop-one: removed pit wall triangle at (" + m.x.ToString("F1", CultureInfo.InvariantCulture) + "," + m.y.ToString("F1", CultureInfo.InvariantCulture) + "," + m.z.ToString("F1", CultureInfo.InvariantCulture) + ")");
                tris.RemoveAt(best);
            }
        }
        // count each edge, split at every ground/pit vertex lying on it within 1 cm (RG-130): tile edges meet the wall columns at any fraction
        // (T-junction, watertight but not welded). Vertices are bucketed by 1 m plan cell.
        var edges = new Dictionary<string, int>();
        Func<Vector3, string> key = v => Math.Round(v.x * 100) + "," + Math.Round(v.y * 100) + "," + Math.Round(v.z * 100);
        var cells = new Dictionary<long, List<Vector3>>(); var seen = new HashSet<string>();
        Func<int, int, long> cellKey = (ix, iz) => ((long)ix << 32) ^ (uint)iz;
        foreach (Tri t in tris)
        {
            if (t.Kind != 'G' && t.Kind != 'F' && t.Kind != 'W') continue;
            foreach (Vector3 v in new[] { t.A, t.B, t.C })
            {
                if (!seen.Add(key(v))) continue;
                long ck = cellKey((int)Math.Floor(v.x), (int)Math.Floor(v.z));
                List<Vector3> l; if (!cells.TryGetValue(ck, out l)) cells[ck] = l = new List<Vector3>();
                l.Add(v);
            }
        }
        var ts = new List<float>();
        foreach (Tri t in tris)
        {
            if (t.Kind != 'G' && t.Kind != 'F' && t.Kind != 'W') continue;
            Vector3[] p = { t.A, t.B, t.C };
            for (int e = 0; e < 3; e++)
            {
                Vector3 p0 = p[e], p1 = p[(e + 1) % 3], d = p1 - p0;
                float l2 = d.sqrMagnitude;
                ts.Clear(); ts.Add(0f); ts.Add(1f);
                if (l2 > 1e-8f)
                    for (int ix = (int)Math.Floor(Math.Min(p0.x, p1.x) - 0.01f); ix <= (int)Math.Floor(Math.Max(p0.x, p1.x) + 0.01f); ix++)
                        for (int iz = (int)Math.Floor(Math.Min(p0.z, p1.z) - 0.01f); iz <= (int)Math.Floor(Math.Max(p0.z, p1.z) + 0.01f); iz++)
                        {
                            List<Vector3> l; if (!cells.TryGetValue(cellKey(ix, iz), out l)) continue;
                            foreach (Vector3 v in l)
                            {
                                float u = Vector3.Dot(v - p0, d) / l2;
                                if (u <= 1e-4f || u >= 1f - 1e-4f) continue;
                                if ((p0 + d * u - v).sqrMagnitude <= 1e-4f) ts.Add(u);
                            }
                        }
                ts.Sort();
                for (int q = 0; q + 1 < ts.Count; q++)
                {
                    string a = key(Vector3.Lerp(p0, p1, ts[q])), b = key(Vector3.Lerp(p0, p1, ts[q + 1]));
                    if (a == b) continue;
                    string k = string.CompareOrdinal(a, b) < 0 ? a + "|" + b : b + "|" + a;
                    int n; edges.TryGetValue(k, out n); edges[k] = n + 1;
                }
            }
        }
        var leaks = new List<string>(); int covered = 0;
        foreach (KeyValuePair<string, int> kv in edges)
        {
            if (kv.Value != 1) continue;
            string[] ab = kv.Key.Split('|'); string[] a = ab[0].Split(','), b = ab[1].Split(',');
            double ax = double.Parse(a[0], CultureInfo.InvariantCulture) / 100, az = double.Parse(a[2], CultureInfo.InvariantCulture) / 100;
            double bx = double.Parse(b[0], CultureInfo.InvariantCulture) / 100, bz = double.Parse(b[2], CultureInfo.InvariantCulture) / 100;
            if (Math.Abs(ax - px) > range || Math.Abs(az - pz) > range) continue;
            bool border = OnBorder(ax, bx) || OnBorder(az, bz);
            if (border) continue;
            if (Covered(tris, (ax + bx) / 2, (az + bz) / 2, (double.Parse(a[1], CultureInfo.InvariantCulture) + double.Parse(b[1], CultureInfo.InvariantCulture)) / 200)) { covered++; continue; }
            leaks.Add("(" + ax.ToString("F1", CultureInfo.InvariantCulture) + "," + (double.Parse(a[1], CultureInfo.InvariantCulture) / 100).ToString("F1", CultureInfo.InvariantCulture) + "," + az.ToString("F1", CultureInfo.InvariantCulture) + ") -> ("
                + bx.ToString("F1", CultureInfo.InvariantCulture) + "," + (double.Parse(b[1], CultureInfo.InvariantCulture) / 100).ToString("F1", CultureInfo.InvariantCulture) + "," + bz.ToString("F1", CultureInfo.InvariantCulture) + ")");
        }
        leaks.Sort(StringComparer.Ordinal);
        if (verbose) Console.WriteLine("ground edges used once but sealed by a road/tube/bridge surface within 0.3 m (not counted): " + covered);
        if (map != null) DrawMap(tris, px, pz, cy, (int)range, map);
        return leaks;
    }

    // a once-used ground/pit edge that lies against a road, tube or bridge surface (same height +-1.5 m) is closed by that surface
    static bool Covered(List<Tri> tris, double mx, double mz, double my)
    {
        float[,] off = { { 0, 0 }, { 0.3f, 0 }, { -0.3f, 0 }, { 0, 0.3f }, { 0, -0.3f } };
        foreach (Tri t in tris)
        {
            if (t.Kind == 'G' || t.Kind == 'F' || t.Kind == 'W') continue;
            for (int k = 0; k < 5; k++)
            {
                float y;
                if (RayDown(t, (float)mx + off[k, 0], (float)mz + off[k, 1], out y) && Math.Abs(y - my) < 1.5) return true;
            }
        }
        return false;
    }

    static bool OnBorder(double u, double v)
    {
        return (Math.Abs(u % 256) < 0.02 || Math.Abs(u % 256) > 255.98) && (Math.Abs(v % 256) < 0.02 || Math.Abs(v % 256) > 255.98);
    }

    static void DrawMap(List<Tri> tris, float px, float pz, float cy, int range, StringBuilder sb)
    {
        for (int zi = range; zi >= -range; zi--)
        {
            for (int xi = -range; xi <= range; xi++)
            {
                float x = px + xi + 0.5f, z = pz + zi + 0.5f;
                float best = float.MinValue; char kind = '.';
                foreach (Tri t in tris)
                {
                    float y;
                    if (!RayDown(t, x, z, out y)) continue;
                    if (y > best) { best = y; kind = t.Kind; }
                }
                if (kind == '.') sb.Append('.');
                else if (kind == 'G') { int h = (int)Math.Round(best - cy); sb.Append(h < 0 ? '-' : h > 9 ? '+' : (char)('0' + h)); }
                else if (kind == 'F') sb.Append(best - cy < 0 ? 'f' : 'F');
                else sb.Append(kind);
            }
            sb.AppendLine();
        }
    }

    static void AddBuf(List<Tri> l, List<Vector3> pos, List<int> idx, char kind)
    {
        for (int i = 0; i < idx.Count; i += 3) l.Add(new Tri { A = pos[idx[i]], B = pos[idx[i + 1]], C = pos[idx[i + 2]], Kind = kind });
    }

    // y of the triangle at (x, z) when it contains that point seen from above
    static bool RayDown(Tri t, float x, float z, out float y)
    {
        y = 0f;
        float d = (t.B.z - t.C.z) * (t.A.x - t.C.x) + (t.C.x - t.B.x) * (t.A.z - t.C.z);
        if (Math.Abs(d) < 1e-9f) return false;
        float l1 = ((t.B.z - t.C.z) * (x - t.C.x) + (t.C.x - t.B.x) * (z - t.C.z)) / d;
        float l2 = ((t.C.z - t.A.z) * (x - t.C.x) + (t.A.x - t.C.x) * (z - t.C.z)) / d;
        float l3 = 1f - l1 - l2;
        if (l1 < 0f || l2 < 0f || l3 < 0f) return false;
        y = l1 * t.A.y + l2 * t.B.y + l3 * t.C.y;
        return true;
    }
}
