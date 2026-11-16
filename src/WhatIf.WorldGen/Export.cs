using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace WhatIf.WorldGen
{
    /// <summary>Exports the graph as nightracer.worldgen.road-dataset.v1 (RG-107 schema): provenance, nodes, edges, profiles, junctions, spans.
    /// The canonical export is the six files joined in a fixed order; hash.txt is its SHA-256. No clock, no paths, numbers rounded (1 mm, 1e-6 1/m).</summary>
    public static class Export
    {
        public const string Schema = "nightracer.worldgen.road-dataset.v1";
        public static readonly string[] FileNames = { "provenance", "nodes", "edges", "profiles", "junctions", "spans" };

        static double R3(double v) { return Math.Round(v, 3) + 0.0; }
        static double R6(double v) { return Math.Round(v, 6) + 0.0; }

        static JObj Doc(List<JObj> items) { return new JObj().Add("schema", Schema).Add("items", items); }

        /// <summary>sourceProv: the provenance.json of the dataset the rules were fitted from (carries OSM/DEM licence, credit, sources); may be null.</summary>
        public static Dictionary<string, string> Build(RoadGraph g, WorldSpec spec, WorldRules rules, JObj sourceProv, out string hash)
        {
            var files = new Dictionary<string, string>();
            var deg = g.Degrees();
            var inc = g.Incident();

            var nodes = new List<JObj>();
            foreach (var n in g.Nodes)
                nodes.Add(new JObj().Add("id", n.Id).Add("osm_id", null).Add("pos", new List<double> { R3(n.P.X), R3(n.Y), R3(n.P.Z) })
                    .Add("degree", deg[n.Id]).Add("kind", n.Kind).Add("layer", n.Layer).Add("boundary", n.Boundary).Add("roundabout_radius_m", null));

            var edges = new List<JObj>(); var profs = new List<JObj>();
            foreach (var e in g.Edges)
            {
                var cl = new List<double>(e.Pts.Length * 3);
                var curv = new List<double>(); var grade = new List<double>();
                for (int i = 0; i < e.Pts.Length; i++)
                {
                    cl.Add(R3(e.Pts[i].X)); cl.Add(R3(e.H[i])); cl.Add(R3(e.Pts[i].Z));
                    curv.Add(R6(e.Curv[i]));
                    int a = Math.Max(0, i - 1), b = Math.Min(e.Pts.Length - 1, i + 1);
                    double ds = V2.Dist(e.Pts[b], e.Pts[a]);
                    grade.Add(ds < 1e-9 ? 0 : R3((e.H[b] - e.H[a]) / ds * 100.0));
                }
                edges.Add(new JObj().Add("id", e.Id).Add("a", e.A).Add("b", e.B).Add("osm_way", null).Add("class", e.Class).Add("highway", e.Class)
                    .Add("lanes", e.Lanes).Add("width_m", e.WidthM).Add("surface", null).Add("oneway", e.Oneway).Add("maxspeed", e.MaxSpeedKmh)
                    .Add("roundabout", false).Add("layer", e.Layer).Add("span", e.Span).Add("length_m", R3(e.LengthM)).Add("step_m", R3(e.StepM))
                    .Add("centreline", cl).Add("bank_deg", null));
                var pr = new JObj().Add("edge", e.Id).Add("curvature", curv).Add("curv_valid", new List<int> { 0, e.Pts.Length - 1 }).Add("grade_pct", grade).Add("step_m", R3(e.StepM));
                if (spec.Terrain.Enabled)   // RG-123, additive: smallest vertical radius over the interior stations (null = no curved station); flat datasets stay byte-identical
                {
                    double vc, vs; Validators.VRadii(e, out vc, out vs);
                    pr.Add("vradius_crest_min_m", vc == double.MaxValue ? null : (object)R3(vc)).Add("vradius_sag_min_m", vs == double.MaxValue ? null : (object)R3(vs));
                }
                profs.Add(pr);
            }

            var juncs = new List<JObj>();
            foreach (var n in g.Nodes)
            {
                if (inc[n.Id].Count < 3) continue;
                var legs = new List<KeyValuePair<double, GEdge>>();
                foreach (int ei in inc[n.Id])
                {
                    var e = g.Edges[ei];
                    double hd = RoadGraph.LegHeading(e, n.Id) * 180 / Math.PI;
                    double brg = (90 - hd) % 360; if (brg < 0) brg += 360;
                    legs.Add(new KeyValuePair<double, GEdge>(R3(brg), e));
                }
                legs.Sort((p, q) => p.Key != q.Key ? p.Key.CompareTo(q.Key) : p.Value.Id.CompareTo(q.Value.Id));
                var br = new List<double>(); var le = new List<int>(); var lc = new List<string>();
                foreach (var kv in legs) { br.Add(kv.Key); le.Add(kv.Value.Id); lc.Add(kv.Value.Class); }
                juncs.Add(new JObj().Add("node", n.Id).Add("legs", legs.Count).Add("bearings", br).Add("leg_edges", le).Add("leg_classes", lc)
                    .Add("min_leg_angle_deg", R3(g.MinLegAngleDeg(n.Id, inc[n.Id]))).Add("roundabout_radius_m", null).Add("interchange_kind", n.InterchangeKind));
            }

            var spans = new List<JObj>();
            foreach (var s in g.Spans)
                spans.Add(new JObj().Add("id", s.Id).Add("kind", s.Kind).Add("class", s.Class).Add("edges", s.Edges).Add("start_node", s.StartNode).Add("end_node", s.EndNode)
                    .Add("length_m", R3(s.LengthM)).Add("cover_depth_m", s.Kind == "tunnel" ? (object)R3(s.CoverDepthM) : null)
                    .Add("clearance_m", s.Kind == "bridge" ? (object)R3(s.ClearanceM) : null)
                    .Add("portal_heights_m", new List<double> { R3(g.Nodes[s.StartNode].Y), R3(g.Nodes[s.EndNode].Y) }).Add("layer", s.Layer));

            files["provenance"] = Json.Write(Provenance(spec, rules, sourceProv));
            files["nodes"] = Json.Write(Doc(nodes));
            files["edges"] = Json.Write(Doc(edges));
            files["profiles"] = Json.Write(Doc(profs));
            files["junctions"] = Json.Write(Doc(juncs));
            files["spans"] = Json.Write(Doc(spans));

            var canon = new StringBuilder();
            foreach (var name in FileNames) canon.Append(name).Append('\n').Append(files[name]).Append('\n');
            using (var sha = SHA256.Create())
                hash = Hex(sha.ComputeHash(new UTF8Encoding(false).GetBytes(canon.ToString())));
            return files;
        }

        static JObj Provenance(WorldSpec spec, WorldRules rules, JObj src)
        {
            object osm = src != null ? src.Get("osm") : null, dem = src != null ? src.Get("dem") : null, sources = src != null ? src.Get("sources") : null;
            if (osm == null) osm = new JObj().Add("licence", "rules fitted from OpenStreetMap (ODbL 1.0); source provenance was not supplied");
            if (dem == null) dem = new JObj().Add("credit", "rules fitted with the Copernicus DEM; source provenance was not supplied");
            object specObj = Json.Parse(spec.Raw.Length > 0 ? spec.Raw : "{}");
            return new JObj()
                .Add("schema", Schema)
                .Add("adr", "ADR-030/031/032 (accepted 2026-10-29)")
                // The script literal keeps its race_game value (NightRacer.WorldGen): it is part of the hashed dataset, renaming it changes every golden hash (verified 2026-11-16).
                .Add("extractor", new JObj().Add("script", "src/NightRacer.WorldGen + tools/worldgen/cli").Add("version", "rg110-1"))
                .Add("date", "not recorded (deterministic export: same seed, spec and rules give the same bytes)")
                .Add("purpose", "generated road network (W3): no real road shape; rules learned from open data (ADR-032)")
                .Add("generator", new JObj().Add("seed", spec.Seed).Add("spec", specObj).Add("rules_sha256", rules.Sha256))
                .Add("window", new JObj().Add("name", "generated").Add("size_km", spec.SizeKm).Add("frame", "x east, y up, z north, metres, origin at the world centre"))
                .Add("parameters", spec.Terrain.Enabled
                    ? new JObj().Add("step_m", rules.StepM).Add("flat_terrain", false).Add("tunnels", "from the road vertical profile on the procedural terrain (RG-118, W5a)")
                    : new JObj().Add("step_m", rules.StepM).Add("flat_terrain", true).Add("tunnels", "none (no terrain until W5)"))
                .Add("osm", osm).Add("dem", dem).Add("sources", sources ?? new List<object>());
        }

        static string Hex(byte[] b)
        {
            var sb = new StringBuilder();
            foreach (byte x in b) sb.Append(x.ToString("x2"));
            return sb.ToString();
        }
    }
}
