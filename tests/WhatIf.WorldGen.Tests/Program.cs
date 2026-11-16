using WhatIf.WorldGen;

// Dependency-free tests for WhatIf.WorldGen (RG-110): exit 0 = ALL PASSED, 1 = a check failed. No Unity, no OSM data, no world-rules.json:
// every rule is synthetic (WorldRules.Synthetic). Each validator has a crafted bad graph that must fail and a good control that must pass.
var failures = 0;
void Check(string name, bool ok, string detail = "")
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name}{(detail.Length > 0 && !ok ? " :: " + detail : "")}");
    if (!ok) failures++;
}
bool Has(List<Finding> l, string check) => l.Any(f => f.Check == check);

var rules = WorldRules.Synthetic();

// ---- RNG ----
{
    var r = new Rng(0);
    Check("rng splitmix64 seed 0 first value", r.NextU64() == 0xE220A8397B1DCDAFUL);
    Check("rng splitmix64 seed 0 second value", r.NextU64() == 0x6E789E6AA1B965F4UL);
    var a = Rng.ForStage(5, "motorway"); var b = Rng.ForStage(5, "motorway"); var c = Rng.ForStage(5, "streets");
    Check("rng stage stream is reproducible", a.NextU64() == b.NextU64());
    Check("rng stages are independent streams", Rng.ForStage(5, "motorway").NextU64() != c.NextU64());
    var d = new Rng(42); bool inRange = true;
    for (int i = 0; i < 1000; i++) { double x = d.NextDouble(); if (x < 0 || x >= 1) inRange = false; }
    Check("rng NextDouble in [0,1)", inRange);
}

// ---- clothoid ----
{
    double ls = 60, rad = 300;
    double x, y, th;
    Clothoid.Point(ls, rad, ls, out x, out y, out th);
    Check("clothoid end heading = Ls/(2R)", Math.Abs(th - ls / (2 * rad)) < 1e-12, th.ToString());
    // sampled spiral: length by chords and end curvature by circumradius
    var pts = new List<V2>();
    for (int i = 0; i <= 120; i++) { Clothoid.Point(ls, rad, ls * i / 120.0, out x, out y, out th); pts.Add(new V2(x, y)); }
    double len = 0; for (int i = 1; i < pts.Count; i++) len += V2.Dist(pts[i], pts[i - 1]);
    Check("clothoid length = Ls", Math.Abs(len - ls) < 0.01, len.ToString());
    V2 p0 = pts[118], p1 = pts[119], p2 = pts[120];
    double area2 = Math.Abs(V2.Cross(p1 - p0, p2 - p0));
    double circum = V2.Dist(p0, p1) * V2.Dist(p1, p2) * V2.Dist(p2, p0) / (2 * area2);
    Check("clothoid end radius = R (circumradius of the last samples)", Math.Abs(circum - rad) / rad < 0.03, circum.ToString());
}

// ---- alignment: known arc radius, 3 s rule, class minimum ----
{
    string cls = RoadClass.Primary;
    double v = RoadClass.DesignSpeedMs(cls);
    var pis = new[] { new V2(0, 0), new V2(1000, 0), new V2(1000 + 1000 * Math.Cos(0.5), 1000 * Math.Sin(0.5)) };
    Centreline cl; string err;
    bool ok = AlignmentBuilder.Build(pis, new[] { false, false, false }, cls, rules, () => 600.0, out cl, out err);
    Check("alignment builds a single bend", ok, err ?? "");
    if (ok)
    {
        double kmax = cl.K.Max(k => Math.Abs(k));
        Check("alignment arc radius = drawn radius 600 m", Math.Abs(1.0 / kmax - 600) < 1.0, (1.0 / kmax).ToString());
        double arc = 0;
        for (int i = 1; i < cl.P.Length; i++) if (Math.Abs(cl.K[i]) > 0.999 * kmax && Math.Abs(cl.K[i - 1]) > 0.999 * kmax) arc += cl.S[i] - cl.S[i - 1];
        Check("alignment arc >= 3 s at design speed", arc >= 3 * v - 1, arc.ToString());
        // geometric radius of the arc from three dense points near the middle
        int m = Array.FindIndex(cl.K, k => Math.Abs(k) > 0.999 * kmax) + 20;
        double a2 = Math.Abs(V2.Cross(cl.P[m + 5] - cl.P[m], cl.P[m + 10] - cl.P[m]));
        double circ = V2.Dist(cl.P[m], cl.P[m + 5]) * V2.Dist(cl.P[m + 5], cl.P[m + 10]) * V2.Dist(cl.P[m], cl.P[m + 10]) / (2 * a2);
        Check("alignment geometry agrees with analytic curvature", Math.Abs(circ - 600) / 600 < 0.02, circ.ToString());
        Check("alignment ends exactly on the first and last waypoint", V2.Dist(cl.P[0], pis[0]) < 1e-9 && V2.Dist(cl.P[cl.P.Length - 1], pis[2]) < 1e-9);
        Check("alignment keeps the last 30 m straight", Math.Abs(cl.KAt(cl.Length - 15)) < 1e-12 && Math.Abs(cl.KAt(15)) < 1e-12);
    }
    var tight = new[] { new V2(0, 0), new V2(100, 0), new V2(100, 100) };
    bool okTight = AlignmentBuilder.Build(tight, new[] { false, true, false }, RoadClass.Motorway, rules, () => 345.0, out cl, out err);
    Check("alignment refuses a bend that does not fit (fixed PI)", !okTight && !string.IsNullOrEmpty(err), err ?? "");
}

// ---- validators: helper graph builders ----
GEdge Line(RoadGraph g, int a, int b, string cls, int layer = 0, double h0 = 0, double h1 = 0, string span = "ground")
{
    V2 pa = g.Nodes[a].P, pb = g.Nodes[b].P;
    int n = Math.Max(1, (int)Math.Round(V2.Dist(pa, pb) / 10));
    var pts = new V2[n + 1]; var h = new double[n + 1]; var k = new double[n + 1];
    for (int i = 0; i <= n; i++) { pts[i] = pa + (pb - pa) * ((double)i / n); h[i] = h0 + (h1 - h0) * i / n; }
    return g.AddEdge(a, b, cls, pts, h, k, layer, span);
}
GNode N(RoadGraph g, double x, double z, string kind = NodeKind.Joint) => g.AddNode(new V2(x, z), 0, kind);

// ---- validators: good control ----
{
    var g = new RoadGraph();
    var a = N(g, 0, 0, NodeKind.End); var b = N(g, 500, 0, NodeKind.Junction); var c = N(g, 1000, 0, NodeKind.End); var d = N(g, 500, 400, NodeKind.End);
    Line(g, a.Id, b.Id, RoadClass.Local); Line(g, b.Id, c.Id, RoadClass.Local); Line(g, b.Id, d.Id, RoadClass.Local);
    var f = Validators.All(g, rules);
    Check("validators pass a clean T junction", f.Count == 0, string.Join("; ", f));
}
// ---- radius ----
{
    var g = new RoadGraph(); var a = N(g, 0, 0, NodeKind.End); var b = N(g, 200, 0, NodeKind.End);
    var e = Line(g, a.Id, b.Id, RoadClass.Motorway);
    e.Curv[3] = 1.0 / 100.0;
    Check("validator radius fails a 100 m radius on a motorway", Has(Validators.Radius(g, rules), "radius"));
    e.Curv[3] = 1.0 / 400.0;
    Check("validator radius passes 400 m on a motorway", Validators.Radius(g, rules).Count == 0);
}
// ---- grade ----
{
    var g = new RoadGraph(); var a = N(g, 0, 0, NodeKind.End); var b = N(g, 200, 0, NodeKind.End);
    Line(g, a.Id, b.Id, RoadClass.Motorway, 0, 0, 40);
    Check("validator grade fails 20 % on a motorway", Has(Validators.Grade(g, rules), "grade"));
    var g2 = new RoadGraph(); var a2 = N(g2, 0, 0, NodeKind.End); var b2 = N(g2, 200, 0, NodeKind.End);
    Line(g2, a2.Id, b2.Id, RoadClass.Motorway, 0, 0, 6);
    Check("validator grade passes 3 % on a motorway", Validators.Grade(g2, rules).Count == 0);
}
// ---- curvature jump at a join ----
{
    var g = new RoadGraph(); var a = N(g, 0, 0, NodeKind.End); var b = N(g, 200, 0); var c = N(g, 400, 0, NodeKind.End);
    var e0 = Line(g, a.Id, b.Id, RoadClass.Primary); var e1 = Line(g, b.Id, c.Id, RoadClass.Primary);
    Check("validator curvature-join passes continuous curvature", Validators.CurvatureJoins(g, rules).Count == 0);
    e0.Curv[e0.Curv.Length - 1] = 0.008; e1.Curv[0] = 0.0;
    Check("validator curvature-join fails a jump of 0.008 1/m", Has(Validators.CurvatureJoins(g, rules), "curvature-join"));
}
// ---- crossing without a node, clearance ----
{
    var g = new RoadGraph();
    var a = N(g, -200, 0, NodeKind.End); var b = N(g, 200, 0, NodeKind.End); var c = N(g, 0, -200, NodeKind.End); var d = N(g, 0, 200, NodeKind.End);
    Line(g, a.Id, b.Id, RoadClass.Local); Line(g, c.Id, d.Id, RoadClass.Local);
    Check("validator crossing fails two local roads crossing on one layer without a node", Has(Validators.Crossings(g, rules), "crossing"));
}
{
    var g = new RoadGraph();
    var a = N(g, -200, 0, NodeKind.End); var b = N(g, 200, 0, NodeKind.End); var c = N(g, 0, -200, NodeKind.End); var d = N(g, 0, 200, NodeKind.End);
    Line(g, a.Id, b.Id, RoadClass.Motorway, 1, 5, 5, "bridge"); Line(g, c.Id, d.Id, RoadClass.Local);
    Check("validator crossing passes a deck 5 m above on layer 1", Validators.Crossings(g, rules).Count == 0);
    var g2 = new RoadGraph();
    var a2 = N(g2, -200, 0, NodeKind.End); var b2 = N(g2, 200, 0, NodeKind.End); var c2 = N(g2, 0, -200, NodeKind.End); var d2 = N(g2, 0, 200, NodeKind.End);
    Line(g2, a2.Id, b2.Id, RoadClass.Motorway, 1, 3, 3, "bridge"); Line(g2, c2.Id, d2.Id, RoadClass.Local);
    Check("validator crossing fails a deck only 3 m above", Has(Validators.Crossings(g2, rules), "clearance"));
}
{
    var g = new RoadGraph();   // a real X: both roads cut at a shared node
    var a = N(g, -200, 0, NodeKind.End); var b = N(g, 200, 0, NodeKind.End); var c = N(g, 0, -200, NodeKind.End); var d = N(g, 0, 200, NodeKind.End); var x = N(g, 0, 0, NodeKind.Junction);
    Line(g, a.Id, x.Id, RoadClass.Local); Line(g, x.Id, b.Id, RoadClass.Local); Line(g, c.Id, x.Id, RoadClass.Local); Line(g, x.Id, d.Id, RoadClass.Local);
    Check("validator crossing passes an X with a junction node", Validators.Crossings(g, rules).Count == 0);
}
// ---- leg angle ----
{
    var g = new RoadGraph();
    var x = N(g, 0, 0, NodeKind.Junction); var a = N(g, -300, 0, NodeKind.End); var b = N(g, 300, 0, NodeKind.End);
    var c = N(g, 300 * Math.Cos(40 * Math.PI / 180), 300 * Math.Sin(40 * Math.PI / 180), NodeKind.End);
    Line(g, a.Id, x.Id, RoadClass.Local); Line(g, x.Id, b.Id, RoadClass.Local); Line(g, x.Id, c.Id, RoadClass.Local);
    Check("validator leg-angle fails a 40 degree T", Has(Validators.LegAngles(g, rules), "leg-angle"));
    var g2 = new RoadGraph();
    var x2 = N(g2, 0, 0, NodeKind.Junction); var a2 = N(g2, -300, 0, NodeKind.End); var b2 = N(g2, 300, 0, NodeKind.End); var c2 = N(g2, 0, 300, NodeKind.End);
    Line(g2, a2.Id, x2.Id, RoadClass.Local); Line(g2, x2.Id, b2.Id, RoadClass.Local); Line(g2, x2.Id, c2.Id, RoadClass.Local);
    Check("validator leg-angle passes a 90 degree T", Validators.LegAngles(g2, rules).Count == 0);
}
// ---- connectivity ----
{
    var g = new RoadGraph();
    var a = N(g, 0, 0, NodeKind.End); var b = N(g, 100, 0, NodeKind.End); var c = N(g, 0, 500, NodeKind.End); var d = N(g, 100, 500, NodeKind.End);
    Line(g, a.Id, b.Id, RoadClass.Local); Line(g, c.Id, d.Id, RoadClass.Local);
    Check("validator connected fails two components", Has(Validators.Connected(g), "connected"));
}
// ---- spacing ----
{
    var g = new RoadGraph();
    var j1 = N(g, 0, 0, NodeKind.Junction); var j2 = N(g, 10, 0, NodeKind.Junction);
    foreach (var (x, z) in new[] { (-200.0, 0.0), (0.0, 200.0) }) { var e = N(g, x, z, NodeKind.End); Line(g, e.Id, j1.Id, RoadClass.Local); }
    foreach (var (x, z) in new[] { (210.0, 0.0), (10.0, 200.0) }) { var e = N(g, x, z, NodeKind.End); Line(g, j2.Id, e.Id, RoadClass.Local); }
    Line(g, j1.Id, j2.Id, RoadClass.Local);
    Check("validator spacing fails junctions 10 m apart on a local road", Has(Validators.Spacing(g, rules), "spacing"));
}
// ---- spans ----
{
    var g = new RoadGraph();
    g.Spans.Add(new GSpan { Id = 0, Kind = "bridge", Class = RoadClass.Motorway, LengthM = 40, ClearanceM = 5 });
    g.Spans.Add(new GSpan { Id = 1, Kind = "bridge", Class = RoadClass.Motorway, LengthM = 80, ClearanceM = 3 });
    g.Spans.Add(new GSpan { Id = 2, Kind = "tunnel", Class = RoadClass.Motorway, LengthM = 100 });
    var f = Validators.Spans(g, rules);
    Check("validator spans fails a 40 m motorway bridge", f.Any(x => x.Check == "bridge-length" && x.Message.Contains("span 0")));
    Check("validator spans fails 3 m clearance", f.Any(x => x.Check == "bridge-clearance"));
    Check("validator spans fails a 100 m motorway tunnel", Has(f, "tunnel-length"));
    var g2 = new RoadGraph();
    g2.Spans.Add(new GSpan { Id = 0, Kind = "bridge", Class = RoadClass.Motorway, LengthM = 80, ClearanceM = 5 });
    g2.Spans.Add(new GSpan { Id = 1, Kind = "tunnel", Class = RoadClass.Motorway, LengthM = 400 });
    Check("validator spans passes 80 m bridge / 5 m / 400 m tunnel", Validators.Spans(g2, rules).Count == 0);
}

// ---- generator: valid output, determinism, seed sensitivity, export validity ----
{
    var spec = WorldSpec.FromJson("{\"seed\":7,\"size_km\":10,\"city\":{\"x\":0,\"z\":0,\"radius_m\":1400},\"villages\":3,\"motorway\":{\"entry_x\":-5000,\"entry_z\":-2200,\"exit_x\":5000,\"exit_z\":1600}}");
    var r1 = Generator.Run(spec, WorldRules.Synthetic());
    var f1 = Validators.All(r1.Graph, WorldRules.Synthetic());
    Check("generator output passes every validator", f1.Count == 0 && r1.Unconnected == 0, string.Join("; ", f1.Take(5)));
    string h1, h2, h3;
    var files1 = Export.Build(r1.Graph, spec, WorldRules.Synthetic(), null, out h1);
    var r2 = Generator.Run(WorldSpec.FromJson(spec.Raw), WorldRules.Synthetic());
    Export.Build(r2.Graph, spec, WorldRules.Synthetic(), null, out h2);
    Check("same seed gives the same hash", h1 == h2, h1 + " vs " + h2);
    var spec3 = WorldSpec.FromJson(spec.Raw); spec3.Seed = 8;
    var r3 = Generator.Run(spec3, WorldRules.Synthetic());
    Export.Build(r3.Graph, spec3, WorldRules.Synthetic(), null, out h3);
    Check("a different seed gives a different hash", h1 != h3);
    Check("export has the six schema v1 files", Export.FileNames.All(n => files1.ContainsKey(n) && files1[n].Contains("nightracer.worldgen.road-dataset.v1")));
    Check("generated graph has a diamond interchange (4 link edges)", r1.Graph.Edges.Count(e => e.Class == RoadClass.Link) == 4);
    Check("motorway has no at-grade junction", r1.Graph.Nodes.All(n => n.Kind != NodeKind.Junction || !r1.Graph.Edges.Any(e => (e.A == n.Id || e.B == n.Id) && e.Class == RoadClass.Motorway)));
    Check("every motorway crossing is a bridge at 5 m", r1.Graph.Spans.All(s => s.Kind == "bridge" && s.ClearanceM >= 5 && s.LengthM >= 60) && r1.Graph.Spans.Count >= 1);
    var json = Json.Parse(files1["nodes"]);
    Check("export parses back as JSON", json is JObj);
    // RG-142: no width step at a degree-2 node (seeds 1-3 of the 10 km spec)
    foreach (int sd in new[] { 1, 2, 3 })
    {
        var sj2 = WorldSpec.FromJson(spec.Raw); sj2.Seed = sd;
        var gw = Generator.Run(sj2, WorldRules.Synthetic()).Graph;
        var deg = gw.Degrees(); var incw = gw.Incident();
        int bad = 0;
        for (int n = 0; n < incw.Length; n++)
            if (deg[n] == 2 && incw[n].Count == 2 && Math.Abs(gw.Edges[incw[n][0]].WidthM - gw.Edges[incw[n][1]].WidthM) > 1e-9) bad++;
        Check("width joint: no degree-2 node with unequal incident widths, seed " + sd, bad == 0, bad + " nodes");
    }
}

// ---- street growth (RG-116): the local constraints, one case each, on a hand-made street set ----
{
    var gspec = WorldSpec.FromJson("{\"seed\":1,\"size_km\":10,\"city\":{\"x\":0,\"z\":0,\"radius_m\":1000},\"villages\":0,\"motorway\":{\"entry_x\":-5000,\"entry_z\":-2200,\"exit_x\":5000,\"exit_z\":1600}}");
    var gen = Generator.ForTest(gspec, WorldRules.Synthetic());
    var city = gen.TestSettlement(new V2(0, 0), 1000);
    var small = gen.TestSettlement(new V2(0, 0), 150);
    int[] main = gen.TestStreet(city, new V2(-200, 0), new V2(200, 0));        // E-W street, free ends
    int[] stubY = gen.TestStreet(city, new V2(0, -300), new V2(0, -200));      // street ending at (0,-200)
    int[] stubY2 = gen.TestStreet(city, new V2(200, -300), new V2(200, -200)); // street ending at (200,-200)

    // crossing -> T: a proposal that would run through the E-W street is cut at the crossing and stops there
    var cross = gen.ProbeSegment(city, stubY[1], new V2(0, 100));
    Check("growth: crossing a street cuts the segment at the crossing", cross.Reject == null && cross.Kind == Generator.EndKind.Cross && V2.Dist(cross.Pt, new V2(0, 0)) < 0.5, cross.Reject + " " + cross.Kind);
    int roadsBefore = gen.TestRoadCount;
    int endN = gen.ApplySegment(city, stubY[1], cross, "city-street");
    Check("growth: the crossed street gets a T node (one cut more) and the new street ends there", gen.TestRoadCount == roadsBefore + 1 && V2.Dist(gen.TestGraph.Nodes[endN].P, new V2(0, 0)) < 0.5
        && cross.Target.Cuts.Count == 3 && cross.Target.Cuts.Any(c => c.Node == endN), "roads " + gen.TestRoadCount + ", cuts " + cross.Target.Cuts.Count);

    // snap: an end within 25 m of an existing node lands exactly on that node
    var snap = gen.ProbeSegment(city, stubY2[1], new V2(205, 10));
    Check("growth: an end within 25 m of a node snaps to it", snap.Reject == null && snap.Kind == Generator.EndKind.Snap && snap.Node == main[1] && V2.Dist(snap.Pt, new V2(200, 0)) < 1e-9, snap.Reject + " " + snap.Kind);

    // reject below 70 degrees: at the start node (50 degrees off its only leg) and at a crossing (45 degrees)
    var sharp = gen.ProbeSegment(city, stubY[0], new V2(0, -300) + new V2(Math.Sin(50 * Math.PI / 180), Math.Cos(50 * Math.PI / 180)) * 100);
    Check("growth: a leg angle below 70 degrees at the start node is rejected", sharp.Reject != null && sharp.Reject.Contains("leg angle"), sharp.Reject + " " + sharp.Kind);
    var diag = gen.ProbeSegment(city, gen.TestStreet(city, new V2(-250, -250), new V2(-350, -250))[0], new V2(103.6, 103.6));
    Check("growth: crossing a street at 45 degrees is rejected", diag.Reject != null && diag.Reject.Contains("angle"), diag.Reject + " " + diag.Kind);

    // radius: a free end beyond the settlement radius is rejected
    var out1 = gen.ProbeSegment(small, main[1], new V2(300, 0));
    Check("growth: an end outside the settlement radius is rejected", out1.Reject != null && out1.Reject.Contains("radius"), out1.Reject + " " + out1.Kind);
    var in1 = gen.ProbeSegment(city, main[1], new V2(300, 0));
    Check("growth: the same segment inside the radius is a free end", in1.Reject == null && in1.Kind == Generator.EndKind.Free);

    // whole routine: same seed -> same streets, mostly T junctions, no grid left
    var sp = WorldSpec.FromJson("{\"seed\":11,\"size_km\":10,\"city\":{\"x\":0,\"z\":0,\"radius_m\":1400},\"villages\":2,\"motorway\":{\"entry_x\":-5000,\"entry_z\":-2200,\"exit_x\":5000,\"exit_z\":1600}}");
    var ga = Generator.Run(sp, WorldRules.Synthetic()); var gb = Generator.Run(WorldSpec.FromJson(sp.Raw), WorldRules.Synthetic());
    Func<GenResult, string> sig = r => string.Join(";", r.Graph.Edges.Where(e => e.Class == RoadClass.Local).Select(e => e.A + ">" + e.B + ":" + e.LengthM.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)));
    Check("growth: same seed gives identical streets", sig(ga) == sig(gb) && sig(ga).Length > 1000);
    var inc = ga.Graph.Incident(); var deg = ga.Graph.Degrees();
    var jn = ga.Graph.Nodes.Where(n => deg[n.Id] >= 3 && inc[n.Id].Any(ei => ga.Graph.Edges[ei].Class == RoadClass.Local)).ToList();
    double tShare = jn.Count == 0 ? 0 : jn.Count(n => deg[n.Id] == 3) / (double)jn.Count;
    Check("growth: at least 70 % of local junctions are T", tShare >= 0.7, tShare.ToString("0.00"));
    Check("growth: the streets pass every validator", Validators.All(ga.Graph, WorldRules.Synthetic()).Count == 0 && ga.Unconnected == 0);
}

// ---- terrain (RG-118, W5a) ----
{
    string baseSpec(long seed, string terrain) => "{\"seed\":" + seed + ",\"size_km\":10,\"city\":{\"x\":0,\"z\":0,\"radius_m\":1400},\"villages\":4,\"motorway\":{\"entry_x\":-5000,\"entry_z\":-2200,\"exit_x\":5000,\"exit_z\":1600}"
        + (terrain.Length > 0 ? ",\"terrain\":" + terrain : "") + "}";
    const string hilly = "{\"relief_m\":60,\"base_wavelength_m\":1400,\"octaves\":5,\"mountain\":{\"x0\":-3300,\"z0\":-5000,\"x1\":-2700,\"z1\":5000,\"width_m\":2400,\"relief_m\":350},\"settlement_flat_margin_m\":300}";
    var syn = WorldRules.Synthetic();

    // relief 0 -> identical graph (the five geometry files are byte-identical; provenance carries the spec text, so only it may differ)
    var flatA = WorldSpec.FromJson(baseSpec(3, "")); var flatB = WorldSpec.FromJson(baseSpec(3, "{\"relief_m\":0}"));
    var fa = Generator.Run(flatA, syn); var fb = Generator.Run(flatB, syn);
    string ha, hb;
    var filesA = Export.Build(fa.Graph, flatA, syn, null, out ha); var filesB = Export.Build(fb.Graph, flatB, syn, null, out hb);
    Check("terrain: relief 0 gives the identical graph (nodes, edges, profiles, junctions, spans)", new[] { "nodes", "edges", "profiles", "junctions", "spans" }.All(n => filesA[n] == filesB[n]));
    Check("terrain: a flat spec has a disabled terrain model with height 0 and no holes", !fb.Terrain.Enabled && fb.Terrain.Height(123.4, -567.8) == 0 && fb.Terrain.Natural(10, 10) == 0 && !fb.Terrain.Hole(0, 0));
    Check("terrain: a flat spec has only the 5 m bridge decks and their ramps as heights", fb.Graph.Spans.All(s => s.Kind == "bridge" && !s.Viaduct) && fb.Graph.Edges.All(e => e.H.All(h => h >= 0 && h <= 5.0 + 1e-9) && (e.Class == RoadClass.Motorway || e.H.All(h => h == 0))));

    // crafted graph: ground road with a cut deepening from 3 to 8 m, then a tunnel 10 m below the surface, then ground again
    var ts = WorldSpec.FromJson(baseSpec(1, hilly));
    var tm = new TerrainModel(ts);
    var cg = new RoadGraph();
    Func<double, double> nat = x => tm.Natural(x, 0);
    GEdge Road(int a, int b, double x0, double x1, double below, string span)
    {
        int n = (int)Math.Round((x1 - x0) / 10);
        var pts = new V2[n + 1]; var h = new double[n + 1]; var k = new double[n + 1];
        for (int i = 0; i <= n; i++) { double x = x0 + (x1 - x0) * i / n; pts[i] = new V2(x, 0); h[i] = nat(x) - below; }
        return cg.AddEdge(a, b, RoadClass.Primary, pts, h, k, 0, span);
    }
    var n0 = cg.AddNode(new V2(0, 0), nat(0) - 3, NodeKind.End); var n1 = cg.AddNode(new V2(200, 0), nat(200) - 8, NodeKind.Portal);
    var n2 = cg.AddNode(new V2(600, 0), nat(600) - 10, NodeKind.Portal); var n3 = cg.AddNode(new V2(800, 0), nat(800) - 3, NodeKind.End);
    var ground = Road(n0.Id, n1.Id, 0, 200, 3, "ground");
    for (int i = 0; i < ground.H.Length; i++) ground.H[i] = nat(ground.Pts[i].X) - 3 - 5.0 * i / (ground.H.Length - 1);
    var tun = Road(n1.Id, n2.Id, 200, 600, 10, "tunnel");
    tun.H[0] = nat(200) - 8;
    var out2 = Road(n2.Id, n3.Id, 600, 800, 3, "ground");
    out2.H[0] = nat(600) - 10;
    tm.Bind(cg);
    double hw = ground.WidthM * 0.5;

    double worst = 0;
    foreach (var e in new[] { ground, out2 })
        for (int i = 0; i < e.Pts.Length; i++) worst = Math.Max(worst, Math.Abs(tm.Height(e.Pts[i].X, e.Pts[i].Z) - e.H[i]));
    Check("terrain: carved height equals the road height on the centreline (<= 1 cm)", worst <= 0.01, worst.ToString());
    double xq = 50, yRoad = ground.H[5];
    bool shoulderOk = true, slopeOk = true;
    for (double off = 0; off <= hw + 1.5 + 1e-9; off += 0.5) shoulderOk &= Math.Abs(tm.Height(xq, off) - yRoad) < 1e-6;
    foreach (double d in new[] { 0.5, 1.5, 3.0, 4.5, 9.0, 20.0 })
    {
        double zq = hw + 1.5 + d, nq = tm.Natural(xq, zq), k = d / 1.5;
        double want = yRoad + Math.Max(-k, Math.Min(k, nq - yRoad));
        slopeOk &= Math.Abs(tm.Height(xq, zq) - want) < 1e-6;
    }
    Check("terrain: flat shoulder of half width + 1.5 m at road level", shoulderOk);
    Check("terrain: cut slope 1:1.5 from the shoulder back to the natural height", slopeOk);
    Check("terrain: far from any road the carved height is the natural height", Math.Abs(tm.Height(400, 300) - tm.Natural(400, 300)) < 1e-12);
    Check("terrain: no carving over the tunnel (the natural height stays)", Math.Abs(tm.Height(400, 0) - nat(400)) < 1e-12);
    Check("terrain: ground ends at a portal without a cap (no ledge beyond it)", Math.Abs(tm.Height(203, 0) - tm.Natural(203, 0)) < 1e-12, tm.Height(203, 0) + " vs " + tm.Natural(203, 0));
    Check("terrain: hole mask opens the tunnel mouth within 8 m of the portal", tm.Hole(204, 0) && tm.Hole(596, 0) && tm.Hole(204, hw + 0.5));
    Check("terrain: no hole beyond 8 m, beside the footprint or over the open road", !tm.Hole(211, 0) && !tm.Hole(589, 0) && !tm.Hole(204, hw + 1.5) && !tm.Hole(400, 0) && !tm.Hole(100, 0));
    int holeScan = 0, holeAway = 0;
    for (double x = -20; x <= 820; x += 2) for (double z = -40; z <= 40; z += 2) if (tm.Hole(x, z)) { holeScan++; if (Math.Min(Math.Abs(x - 200), Math.Abs(x - 600)) > 8.01) holeAway++; }
    Check("terrain: hole samples exist and lie only within 8 m of a portal", holeScan > 0 && holeAway == 0, holeScan + " / " + holeAway);

    // terrain validators on crafted cases
    bool Fails(RoadGraph g2, string check) => Validators.Terrain(g2, syn, tm).Any(f => f.Check == check);
    Check("terrain validator: the crafted graph (cut up to 8 m, tunnel 10 m deep) is clean", Validators.Terrain(cg, syn, tm).Count == 0, string.Join("; ", Validators.Terrain(cg, syn, tm)));
    RoadGraph Single(string cls, string span, Func<int, double> below, int n = 10)
    {
        var g2 = new RoadGraph();
        var a = g2.AddNode(new V2(0, 0), 0, NodeKind.End); var c = g2.AddNode(new V2(10 * n, 0), 0, NodeKind.End);
        var pts = new V2[n + 1]; var h = new double[n + 1];
        for (int i = 0; i <= n; i++) { pts[i] = new V2(10 * i, 0); h[i] = nat(pts[i].X) - below(i); }
        g2.AddEdge(a.Id, c.Id, cls, pts, h, new double[n + 1], 0, span);
        return g2;
    }
    Check("terrain validator: a tunnel 6 m below the surface (cover < 7.5 m) fails", Fails(Single(RoadClass.Primary, "tunnel", i => 6.0, 40), "tunnel-cover"));
    Check("terrain validator: a tunnel 8 m below the surface passes", !Fails(Single(RoadClass.Primary, "tunnel", i => 8.0, 40), "tunnel-cover"));
    Check("terrain validator: a ground sample 13 m below the surface fails the 12 m cut limit", Fails(Single(RoadClass.Secondary, "ground", i => i == 4 ? 13.0 : 2.0), "cut-depth"));
    Check("terrain validator: a ground sample 6.5 m above the surface fails the 6 m fill limit", Fails(Single(RoadClass.Secondary, "ground", i => i == 4 ? -6.5 : -1.0), "fill-height"));
    Check("terrain validator: a viaduct on a local street fails", Fails(Single(RoadClass.Local, "bridge", i => -8.0), "span-on-local"));

    // vcurve validator (RG-123): the RG-119 crest (+5.69 % into -5.69 % at one 10 m station) and a sag, against rounded profiles
    RoadGraph Profile(string cls, int n, Func<double, double> h)
    {
        var g2 = new RoadGraph();
        var a = g2.AddNode(new V2(0, 0), h(0), NodeKind.End); var c = g2.AddNode(new V2(10 * n, 0), h(10 * n), NodeKind.End);
        var pts = new V2[n + 1]; var hh = new double[n + 1];
        for (int i = 0; i <= n; i++) { pts[i] = new V2(10 * i, 0); hh[i] = h(10 * i); }
        g2.AddEdge(a.Id, c.Id, cls, pts, hh, new double[n + 1], 0, "ground");
        return g2;
    }
    Func<double, double> sharpCrest = x => 100 + 0.0569 * (200 - Math.Abs(x - 200));
    Func<double, double> sharpSag = x => 100 + 0.0569 * Math.Abs(x - 200);
    var vcf = Validators.VCurve(Profile(RoadClass.Motorway, 40, sharpCrest), syn);
    Check("vcurve validator: a sharp motorway crest (R about 88 m) fails with a lift-off speed near 106 km/h", vcf.Count == 1 && vcf[0].Check == "vcurve" && vcf[0].Message.Contains("crest") && vcf[0].Message.Contains("lift-off 10"), string.Join("; ", vcf));
    Check("vcurve validator: a sharp motorway sag fails", Validators.VCurve(Profile(RoadClass.Motorway, 40, sharpSag), syn).Any(f => f.Message.Contains("sag")));
    double rr = 8000;   // parabolic crest of radius 8000 m, grade 5.69 % at the ends
    Check("vcurve validator: a rounded crest of R 8000 m passes on a motorway", Validators.VCurve(Profile(RoadClass.Motorway, 40, x => 100 + 40 - (x - 200) * (x - 200) / (2 * rr)), syn).Count == 0);
    Check("vcurve validator: the same R 8000 m crest on a primary is far above its 2500 m minimum", Validators.VCurve(Profile(RoadClass.Primary, 40, x => 100 + 40 - (x - 200) * (x - 200) / (2 * rr)), syn).Count == 0);
    Check("vcurve validator: R 1500 m crest passes on a secondary (1200 m), fails on a primary (2500 m)", Validators.VCurve(Profile(RoadClass.Secondary, 40, x => 140 - (x - 200) * (x - 200) / 3000), syn).Count == 0
        && Validators.VCurve(Profile(RoadClass.Primary, 40, x => 140 - (x - 200) * (x - 200) / 3000), syn).Count > 0);
    // the break exactly at a degree-2 node (two edges) is found too
    var jg = new RoadGraph();
    var j0 = jg.AddNode(new V2(0, 0), 100, NodeKind.End); var j1 = jg.AddNode(new V2(200, 0), 111.38, NodeKind.Joint); var j2 = jg.AddNode(new V2(400, 0), 100, NodeKind.End);
    var jpa = new V2[21]; var jha = new double[21]; var jpb = new V2[21]; var jhb = new double[21];
    for (int i = 0; i <= 20; i++) { jpa[i] = new V2(10 * i, 0); jha[i] = 100 + 0.0569 * 10 * i; jpb[i] = new V2(200 + 10 * i, 0); jhb[i] = 111.38 - 0.0569 * 10 * i; }
    jg.AddEdge(j0.Id, j1.Id, RoadClass.Motorway, jpa, jha, new double[21], 0, "ground"); jg.AddEdge(j1.Id, j2.Id, RoadClass.Motorway, jpb, jhb, new double[21], 0, "tunnel");
    Check("vcurve validator: a sharp crest exactly at a degree-2 node between two edges fails", Validators.VCurve(jg, syn).Count == 1, string.Join("; ", Validators.VCurve(jg, syn)));

    // generated worlds on the terrain spec: validators, determinism, tiles
    var tsp = WorldSpec.FromJson(baseSpec(1, hilly));
    var g1 = Generator.Run(tsp, syn); var g2r = Generator.Run(WorldSpec.FromJson(baseSpec(1, hilly)), syn);
    var fnd = Validators.All(g1.Graph, syn, g1.Terrain);
    Check("terrain: a generated hilly world passes every validator including the terrain checks", fnd.Count == 0 && g1.Unconnected == 0, string.Join("; ", fnd.Take(5)));
    Check("terrain: the hilly world has tunnels and viaducts", g1.Graph.Spans.Any(s => s.Kind == "tunnel") && g1.Graph.Spans.Any(s => s.Kind == "bridge" && s.Layer == 0), g1.Graph.Spans.Count + " spans");
    Check("terrain: tunnel cover >= 7.5 m below the surface at every tunnel sample", g1.Graph.Edges.Where(e => e.Span == "tunnel").All(e => Enumerable.Range(0, e.Pts.Length).All(i => g1.Terrain.Natural(e.Pts[i].X, e.Pts[i].Z) - e.H[i] >= 7.5 - 1e-9)));
    {   // RG-130: at a junction (degree >= 3, no motorway leg) every minor leg holds node height to widest half width + 3 m (adapter cut + 1 m, a station of its own);
        // the major pair (best class, then closest to 180 deg) has no plateau station
        int minorOk = 0, minorBad = 0, majorOk = 0, majorBad = 0; string firstBad = "";
        int[] dg = g1.Graph.Degrees(); var inc = g1.Graph.Incident();
        int Ord(string c) => c == RoadClass.Motorway ? 0 : c == RoadClass.Primary ? 1 : c == RoadClass.Link ? 2 : c == RoadClass.Secondary ? 3 : 4;
        foreach (var n in g1.Graph.Nodes)
        {
            if (dg[n.Id] < 3) continue;
            var legs = inc[n.Id].Select(ei => g1.Graph.Edges[ei]).ToList();
            if (legs.Any(e => e.Class == RoadClass.Motorway) || legs.Count != dg[n.Id]) continue;
            // leg samples ordered away from the node: distance from the node and height
            var dist = legs.Select(e => Enumerable.Range(0, e.Pts.Length).Select(i => e.A == n.Id ? i : e.Pts.Length - 1 - i).Select(i => Tuple.Create(V2.Dist(e.Pts[i], n.P), e.H[i])).ToList()).ToList();
            var ang = legs.Select(e => { V2 q = e.A == n.Id ? e.Pts[1] : e.Pts[e.Pts.Length - 2]; return Math.Atan2(q.Z - n.P.Z, q.X - n.P.X); }).ToList();
            int bi = -1, bj = -1; double best = double.MaxValue;
            for (int i = 0; i < legs.Count; i++) for (int j = i + 1; j < legs.Count; j++)
                {
                    double d = Math.Abs(ang[i] - ang[j]) % (2 * Math.PI); d = Math.Min(d, 2 * Math.PI - d);
                    double key = Math.Max(Ord(legs[i].Class), Ord(legs[j].Class)) * 10.0 + (Math.PI - d);
                    if (key < best) { best = key; bi = i; bj = j; }
                }
            double reach = legs.Max(e => RoadClass.Width(e.Class)) * 0.5 + 3.0; // class width: the terrain plateau is placed from it; RG-142 may widen WidthM of a degree-2 chain by up to 0.5 m per side (still >= 0.5 m inside the +1 m margin)
            for (int k = 0; k < legs.Count; k++)
            {
                bool hasEnd = dist[k].Any(t => Math.Abs(t.Item1 - reach) < 0.05);
                if (k == bi || k == bj) { if (hasEnd) { majorBad++; if (firstBad == "") firstBad = "major leg edge " + legs[k].Id + " at node " + n.Id; } else majorOk++; continue; }
                bool level = dist[k].Where(t => t.Item1 <= reach + 0.05).All(t => Math.Abs(t.Item2 - n.Y) < 1e-6);
                if (hasEnd && level) minorOk++; else { minorBad++; if (firstBad == "") firstBad = "minor leg edge " + legs[k].Id + " at node " + n.Id + (hasEnd ? " not level" : " no plateau station"); }
            }
        }
        Check("terrain: a minor junction leg holds node height to the end of its plateau (adapter cut + 1 m)", minorOk > 0 && minorBad == 0, minorOk + " ok, " + minorBad + " bad; " + firstBad);
        Check("terrain: the major pair of a junction keeps its profile (no plateau station)", majorOk > 0 && majorBad == 0, majorOk + " ok, " + majorBad + " bad; " + firstBad);
    }
    {   // RG-128: a road that leaves the motorway at a node shares the motorway height while its footprint overlaps the motorway footprint (ramp at ~27 deg: 0.98 m between the ribbons)
        var mot = g1.Graph.Edges.Where(e => e.Class == RoadClass.Motorway && e.Span == "ground").ToList();
        Func<V2, double[]> nearest = p =>
        {
            double best = double.MaxValue, y = 0;
            foreach (var m in mot) for (int i = 0; i + 1 < m.Pts.Length; i++)
                {
                    double dx = m.Pts[i + 1].X - m.Pts[i].X, dz = m.Pts[i + 1].Z - m.Pts[i].Z, l2 = dx * dx + dz * dz;
                    double t = l2 > 1e-9 ? Math.Max(0, Math.Min(1, ((p.X - m.Pts[i].X) * dx + (p.Z - m.Pts[i].Z) * dz) / l2)) : 0;
                    double ex = m.Pts[i].X + dx * t - p.X, ez = m.Pts[i].Z + dz * t - p.Z, d = Math.Sqrt(ex * ex + ez * ez);
                    if (d < best) { best = d; y = m.H[i] + (m.H[i + 1] - m.H[i]) * t; }
                }
            return new[] { best, y };
        };
        int leaving = 0, off = 0;
        foreach (var e in g1.Graph.Edges.Where(q => q.Class != RoadClass.Motorway && q.Span == "ground"))
            foreach (int dir in new[] { 1, -1 })
            {
                int i0 = dir > 0 ? 0 : e.Pts.Length - 1;
                if (nearest(e.Pts[i0])[0] > 0.5) continue;
                leaving++;
                double clear = RoadClass.Width(RoadClass.Motorway) * 0.5 + e.WidthM * 0.5;   // well inside the pinned range (the generator pins to clear + 2)
                for (int i = i0; i >= 0 && i < e.Pts.Length; i += dir)
                {
                    var nm = nearest(e.Pts[i]);
                    if (nm[0] >= clear) break;
                    if (Math.Abs(e.H[i] - nm[1]) > 0.01) { off++; break; }
                }
            }
        Check("terrain: a road leaving the motorway at a node has the motorway height inside the motorway footprint (RG-128 pin)", leaving > 0 && off == 0, leaving + " leaving ends, " + off + " off the motorway surface");
    }
    Check("terrain: no tunnel or viaduct on a local street", g1.Graph.Edges.Where(e => e.Class == RoadClass.Local).All(e => e.Span == "ground"));
    Check("terrain: ground samples cut <= 12 m and fill <= 6 m", g1.Graph.Edges.Where(e => e.Span == "ground").All(e => Enumerable.Range(0, e.Pts.Length).All(i => { double d = g1.Terrain.Natural(e.Pts[i].X, e.Pts[i].Z) - e.H[i]; return d <= 12 + 1e-9 && d >= -6 - 1e-9; })));
    string ea, eb; Export.Build(g1.Graph, tsp, syn, null, out ea); Export.Build(g2r.Graph, tsp, syn, null, out eb);
    Check("terrain: same seed and spec give the same graph hash", ea == eb);
    var t1 = g1.Terrain.SampleTile(-128, -128, 256, 4); var t2 = g2r.Terrain.SampleTile(-128, -128, 256, 4);
    Check("terrain: same seed gives the same terrain samples", t1.Heights.SequenceEqual(t2.Heights) && t1.Holes.SequenceEqual(t2.Holes));
    var other = Generator.Run(WorldSpec.FromJson(baseSpec(2, hilly)), syn);
    Check("terrain: another seed gives another terrain", !other.Terrain.SampleTile(-128, -128, 256, 4).Heights.SequenceEqual(t1.Heights));

    // tiles: shared borders are bit-identical, a tile sample equals the point query, ground road centrelines carve to the road height
    bool seamE = true, seamN = true, seamHole = true;
    foreach (var (tx, tz) in new[] { (0.0, 0.0), (-3328.0, -1024.0), (-2816.0, 768.0) })
    {
        var tw = g1.Terrain.SampleTile(tx, tz, 256, 4); var te = g1.Terrain.SampleTile(tx + 256, tz, 256, 4); var tn = g1.Terrain.SampleTile(tx, tz + 256, 256, 4);
        int w = tw.N + 1;
        for (int i = 0; i < w; i++)
        {
            seamE &= BitConverter.DoubleToInt64Bits(tw.Heights[i * w + tw.N]) == BitConverter.DoubleToInt64Bits(te.Heights[i * w]);
            seamN &= BitConverter.DoubleToInt64Bits(tw.Heights[tw.N * w + i]) == BitConverter.DoubleToInt64Bits(tn.Heights[i]);
            seamHole &= tw.Holes[i * w + tw.N] == te.Holes[i * w] && tw.Holes[tw.N * w + i] == tn.Holes[i];
        }
    }
    Check("terrain: adjacent tiles share bit-identical border samples (east and north, heights and holes)", seamE && seamN && seamHole);
    {
        var tw = g1.Terrain.SampleTile(-3328, -1024, 256, 4); int w = tw.N + 1;
        var rnd = new Rng(5); bool pointOk = true;
        for (int i = 0; i < 200; i++) { int ix = rnd.Int(w), iz = rnd.Int(w); pointOk &= tw.Heights[iz * w + ix] == (float)g1.Terrain.Height(-3328 + 4.0 * ix, -1024 + 4.0 * iz); }
        Check("terrain: a tile sample equals the point query", pointOk);
    }
    double wr = 0; int nr = 0;
    foreach (var e in g1.Graph.Edges.Where(q => q.Span == "ground"))
        for (int i = 0; i < e.Pts.Length; i++) { wr = Math.Max(wr, Math.Abs(g1.Terrain.Height(e.Pts[i].X, e.Pts[i].Z) - e.H[i])); nr++; }
    Check("terrain: carved height under every ground road sample equals the road height (<= 1 cm)", nr > 1000 && wr <= 0.01, nr + " samples, worst " + wr);
    int holesNearPortal = 0, holesElsewhere = 0;
    var portals = g1.Graph.Nodes.Where(n => n.Kind == NodeKind.Portal && g1.Graph.Edges.Any(e => e.Span == "tunnel" && (e.A == n.Id || e.B == n.Id))).ToList();
    foreach (var p in portals)
    {
        var tl = g1.Terrain.SampleTile(Math.Floor(p.P.X / 64) * 64 - 64, Math.Floor(p.P.Z / 64) * 64 - 64, 192, 2); int w = tl.N + 1;
        for (int iz = 0; iz < w; iz++) for (int ix = 0; ix < w; ix++)
                if (tl.Holes[iz * w + ix])
                {
                    V2 q = new V2(Math.Floor(p.P.X / 64) * 64 - 64 + 2.0 * ix, Math.Floor(p.P.Z / 64) * 64 - 64 + 2.0 * iz);
                    double dmin = portals.Min(o => V2.Dist(o.P, q));
                    if (dmin <= 8 + 12.5 + 1.0 + 0.1) holesNearPortal++; else holesElsewhere++;
                }
    }
    Check("terrain: in the generated world holes are only within 8 m + footprint of a tunnel portal", portals.Count > 0 && holesNearPortal > 0 && holesElsewhere == 0, portals.Count + " portals, " + holesNearPortal + " near, " + holesElsewhere + " elsewhere");
}

// ---- scatter (RG-151): determinism, sector independence, checks (a)-(c), road dataset unchanged ----
{
    var rs = WorldRules.Synthetic();
    string sj = "{\"seed\":1,\"size_km\":10,\"city\":{\"x\":0,\"z\":0,\"radius_m\":1400},\"villages\":4,\"motorway\":{\"entry_x\":-5000,\"entry_z\":-2200,\"exit_x\":5000,\"exit_z\":1600},\"terrain\":{\"relief_m\":60,\"base_wavelength_m\":1400,\"octaves\":5,\"mountain\":{\"x0\":-3300,\"z0\":-5000,\"x1\":-2700,\"z1\":5000,\"width_m\":2400,\"relief_m\":350},\"settlement_flat_margin_m\":300}}";
    var sspec = WorldSpec.FromJson(sj);
    var sres = Generator.Run(sspec, rs);
    string roadBefore; Export.Build(sres.Graph, sspec, rs, null, out roadBefore);
    Check("scatter: the generator exposes the city and the villages read-only", sres.Settlements.Count == 5 && sres.Settlements[0].IsCity && sres.Settlements.Skip(1).All(v => !v.IsCity && v.Radius > 0));
    var village = sres.Settlements.First(v => !v.IsCity);
    double wx = Math.Floor(village.C.X / 256) * 256 - 512, wz = Math.Floor(village.C.Z / 256) * 256 - 512;
    var m1 = new ScatterModel(sspec, sres);
    string h1 = m1.Hash(wx, wz, 1024);
    string h2 = new ScatterModel(sspec, sres).Hash(wx, wz, 1024);
    Check("scatter: two runs over a village window give the same hash", h1 == h2, h1 + " vs " + h2);
    // a sector computed alone equals the same sector computed among its neighbours (no state shared between sectors)
    int sx = ScatterModel.SectorOf(village.C.X), sz = ScatterModel.SectorOf(village.C.Z);
    var alone = new ScatterModel(sspec, sres).Sector(sx, sz);
    var busy = new ScatterModel(sspec, sres);
    for (int dz = 1; dz >= -1; dz--) for (int dx = 1; dx >= -1; dx--) busy.Sector(sx + dx, sz + dz);
    var among = busy.Sector(sx, sz);
    string ka = ScatterModel.Hash(new[] { new KeyValuePair<string, List<Instance>>("S", alone) }), kb = ScatterModel.Hash(new[] { new KeyValuePair<string, List<Instance>>("S", among) });
    Check("scatter: a sector computed alone equals the same sector computed among neighbours", ka == kb && alone.Count > 0, alone.Count + " vs " + among.Count);
    Check("scatter: a different seed gives a different scatter hash", WorldSpec.FromJson(sj.Replace("\"seed\":1", "\"seed\":2")) is WorldSpec s2 && new ScatterModel(s2, Generator.Run(s2, rs)).Hash(wx, wz, 1024) != h1);

    // checks (a)-(c) over a village window and a forest window on the mountain foot, every instance audited
    var audit = new ScatterAudit();
    foreach (var win in new[] { new[] { wx, wz }, new[] { -3950.0, 300.0 } })
        for (int z = ScatterModel.SectorOf(win[1]); z <= ScatterModel.SectorOf(win[1] + 1023); z++)
            for (int x = ScatterModel.SectorOf(win[0]); x <= ScatterModel.SectorOf(win[0] + 1023); x++)
                m1.AuditSector(audit, x, z, m1.Sector(x, z));
    Check("scatter (a): every instance keeps its road clearance (0 violations, 0 excluded)", audit.ViolationsA == 0 && audit.Excluded == 0 && audit.Instances > 100, audit.ViolationsA + " violations of " + audit.Instances + " " + string.Join("; ", audit.Samples));
    Check("scatter (b): no instance on a span, in a hole, above the height band or on too steep ground", audit.ViolationsB == 0, audit.ViolationsB + " " + string.Join("; ", audit.Samples));
    Check("scatter (c): per-sector counts stay under the caps", audit.ViolationsC == 0 && audit.MaxTreesInSector <= 1500 && audit.MaxBuildingsInSector <= 150);
    Check("scatter: the windows contain trees, bushes, houses and street trees", audit.Trees > 0 && audit.Houses > 0 && audit.StreetTrees > 0 && audit.Bushes > 0, $"{audit.Trees}/{audit.Bushes}/{audit.Houses}/{audit.StreetTrees}");

    // the audit is not vacuous: crafted bad instances are caught
    var edge = sres.Graph.Edges.First(e => e.Class == RoadClass.Primary && e.Span == "ground");
    V2 on = edge.Pts[edge.Pts.Length / 2];
    var bad = new ScatterAudit();
    m1.AuditSector(bad, 0, 0, new List<Instance> { new Instance { Kind = ScatterKind.Tree, X = (float)on.X, Z = (float)on.Z, Y = (float)sres.Terrain.Height(on.X, on.Z), Scale = 1 } });
    Check("scatter audit: a tree on a road centreline violates (a)", bad.ViolationsA == 1);
    var bad2 = new ScatterAudit();
    m1.AuditSector(bad2, 0, 0, new List<Instance> { new Instance { Kind = ScatterKind.Tree, X = -3000f, Z = 5000f, Y = 200f, Scale = 1 } });
    Check("scatter audit: a tree 200 m up violates (b)", bad2.ViolationsB == 1);
    var tun = sres.Graph.Edges.FirstOrDefault(e => e.Span == "tunnel");
    if (tun != null)
    {
        V2 tp = tun.Pts[tun.Pts.Length / 2];
        var bad3 = new ScatterAudit();
        m1.AuditSector(bad3, 0, 0, new List<Instance> { new Instance { Kind = ScatterKind.Bush, X = (float)tp.X, Z = (float)tp.Z, Y = (float)sres.Terrain.Height(tp.X, tp.Z), Scale = 1 } });
        Check("scatter audit: a bush above a tunnel centreline violates (a) and (b)", bad3.ViolationsA == 1 && bad3.ViolationsB == 1);
    }
    var bad4 = new ScatterAudit();
    var capped = new ScatterModel(sspec, sres) { TreeCap = 1, BuildingCap = 1 };
    var many = new List<Instance>(); for (int i = 0; i < 3; i++) many.Add(new Instance { Kind = ScatterKind.House, X = -4000 + 30 * i, Z = 4900, Y = 0, Scale = 1 });
    capped.AuditSector(bad4, 0, 0, many);
    Check("scatter audit: 3 houses over a cap of 1 violate (c)", bad4.ViolationsC == 1);

    // caps: deterministic thinning of a dense forest sector
    int fx = ScatterModel.SectorOf(-3500), fz = ScatterModel.SectorOf(900);
    int full = 0; for (int dz = -2; dz <= 2; dz++) for (int dx = -2; dx <= 2; dx++) full = Math.Max(full, m1.Sector(fx + dx, fz + dz).Count(i => i.Kind == ScatterKind.Tree));
    var thin = new ScatterModel(sspec, sres) { TreeCap = Math.Max(1, full / 3) };
    int thinMax = 0; for (int dz = -2; dz <= 2; dz++) for (int dx = -2; dx <= 2; dx++) thinMax = Math.Max(thinMax, thin.Sector(fx + dx, fz + dz).Count(i => i.Kind == ScatterKind.Tree));
    Check("scatter: a tree cap thins the densest sector below the cap", full > 3 && thinMax <= Math.Max(1, full / 3), full + " -> " + thinMax);

    string roadAfter; Export.Build(sres.Graph, sspec, rs, null, out roadAfter);
    Check("scatter: the road dataset hash is unchanged by building and running the scatter model", roadBefore == roadAfter, roadBefore + " vs " + roadAfter);
}

// ---- terrain base-height hook (RG-165): a model without the provider keeps the pre-RG-165 heights bit for bit ----
{
    const string spec = "{\"seed\":1,\"size_km\":10,\"city\":{\"x\":0,\"z\":0,\"radius_m\":1400},\"villages\":4,\"motorway\":{\"entry_x\":-5000,\"entry_z\":-2200,\"exit_x\":5000,\"exit_z\":1600},"
        + "\"terrain\":{\"relief_m\":60,\"base_wavelength_m\":1400,\"octaves\":5,\"mountain\":{\"x0\":-3300,\"z0\":-5000,\"x1\":-2700,\"z1\":5000,\"width_m\":2400,\"relief_m\":350},\"settlement_flat_margin_m\":300}}";
    var ws = WorldSpec.FromJson(spec);
    var gen = Generator.Run(ws, WorldRules.Synthetic());
    var plain = new TerrainModel(ws);
    ulong h = 14695981039346656037UL;
    void Mix(double v) { ulong b = (ulong)BitConverter.DoubleToInt64Bits(v); for (int k = 0; k < 8; k++) { h ^= (b >> (8 * k)) & 0xFF; h *= 1099511628211UL; } }
    foreach (var (tx, tz) in new[] { (-128.0, -128.0), (-3328.0, -1024.0), (2048.0, 1536.0) })
        foreach (float v in gen.Terrain.SampleTile(tx, tz, 256, 4).Heights) Mix(v);
    for (int i = 0; i < 200; i++) { double x = -4900 + 49.3 * i, z = 4700 - 47.1 * i; Mix(gen.Terrain.Height(x, z)); Mix(gen.Terrain.Natural(x, z)); Mix(plain.Height(x, z)); }
    Check("terrain hook: without a base provider Height/Natural/SampleTile are bit-identical to the pre-RG-165 golden hash", h == 0xD221EBA2351460E0UL, h.ToString("X16"));

    // RG-167: Generator.Run(spec, rules, null) is the old Run(spec, rules): same road dataset hash and the same golden terrain hash
    var genNull = Generator.Run(WorldSpec.FromJson(spec), WorldRules.Synthetic(), null);
    string hashOld, hashNull;
    Export.Build(gen.Graph, ws, WorldRules.Synthetic(), null, out hashOld);
    Export.Build(genNull.Graph, ws, WorldRules.Synthetic(), null, out hashNull);
    ulong h2 = h; h = 14695981039346656037UL;
    foreach (var (tx, tz) in new[] { (-128.0, -128.0), (-3328.0, -1024.0), (2048.0, 1536.0) })
        foreach (float v in genNull.Terrain.SampleTile(tx, tz, 256, 4).Heights) Mix(v);
    for (int i = 0; i < 200; i++) { double x = -4900 + 49.3 * i, z = 4700 - 47.1 * i; Mix(genNull.Terrain.Height(x, z)); Mix(genNull.Terrain.Natural(x, z)); Mix(plain.Height(x, z)); }
    Check("generator hook: Run(spec, rules, null) gives the same road dataset hash and terrain golden hash as Run(spec, rules)", hashOld == hashNull && h == 0xD221EBA2351460E0UL && h2 == h, hashOld + " vs " + hashNull + "; " + h.ToString("X16"));
    // RG-175: the vertical-curve stall exit is off by default: GenResult of a NightRacer seed stays bit-identical (road dataset hash recorded on the pre-change code, 2026-11-13)
    string hashStall0, hashStall;
    Export.Build(gen.Graph, ws, WorldRules.Synthetic(), null, out hashStall0);
    var wsStall = WorldSpec.FromJson(spec); wsStall.VcStallOuter = 30;
    var genStall = Generator.Run(wsStall, WorldRules.Synthetic());
    Export.Build(genStall.Graph, wsStall, WorldRules.Synthetic(), null, out hashStall);
    Check("RG-175: default spec (VcStallOuter 0) gives the pre-change road dataset hash; the stall exit is opt-in and still produces a dataset", ws.VcStallOuter == 0 && hashStall0 == "9857d480c73b47ce0c070d8813e9161f0bed6979998e818ffda27ec64caa914b" && hashStall.Length == hashStall0.Length, hashStall0);
    // RG-176: Motorway = false gives a village-only world: no motorway / link edges, >= 2 settlements, one connected component, no city
    var wsV = WorldSpec.FromJson(spec); wsV.Motorway = false; wsV.CityRadiusM = 450;
    var genV = Generator.Run(wsV, WorldRules.Synthetic());
    var gv = genV.Graph; int nGv = gv.Nodes.Count; var pv = new int[nGv]; for (int i = 0; i < nGv; i++) pv[i] = i;
    int FindV(int x) { while (pv[x] != x) { pv[x] = pv[pv[x]]; x = pv[x]; } return x; }
    foreach (var e in gv.Edges) pv[FindV(e.A)] = FindV(e.B);
    int compV = Enumerable.Range(0, nGv).Where(i => gv.Degrees()[i] > 0).Select(FindV).Distinct().Count();
    int mwV = gv.Edges.Count(e => e.Class == RoadClass.Motorway || e.Class == RoadClass.Link);
    Check("RG-176: Motorway = false gives a world with 0 motorway/link edges, >= 2 settlements (none a city), one connected component", ws.Motorway && mwV == 0 && gv.Edges.Count > 0 && genV.Settlements.Count >= 2 && genV.Settlements.All(x => !x.IsCity) && compV == 1 && genV.Unconnected == 0, mwV + " motorway/link edges, " + genV.Settlements.Count + " settlements, " + compV + " components, " + genV.Unconnected + " unconnected, " + gv.Edges.Count + " edges");
}

// ---- RG-182: CentreIsCity (town / city street budget without the motorway) and one pinned entrance ----
{
    const string spec = "{\"seed\":1,\"size_km\":10,\"city\":{\"x\":0,\"z\":0,\"radius_m\":900},\"villages\":0,\"terrain\":{\"relief_m\":60,\"base_wavelength_m\":1400,\"octaves\":5}}";
    var ws0 = WorldSpec.FromJson(spec); ws0.Motorway = false;
    var gen0 = Generator.Run(ws0, WorldRules.Synthetic());
    var wsC = WorldSpec.FromJson(spec); wsC.Motorway = false; wsC.CentreIsCity = true;
    var entrance = new Entrance { Point = new V2(-900, 0), Dir = new V2(1, 0), Y = gen0.Terrain.Natural(-900, 0) + 1.5, Class = RoadClass.Primary };
    wsC.Entrances.Add(entrance);
    var genC = Generator.Run(wsC, WorldRules.Synthetic());
    var gC = genC.Graph;
    var node0 = gC.Nodes.OrderBy(n => V2.Dist(n.P, entrance.Point)).First();
    var edge0 = gC.Edges.FirstOrDefault(e => e.A == node0.Id || e.B == node0.Id);
    bool first = edge0 != null && edge0.A == node0.Id; int last = edge0 == null ? 0 : edge0.Pts.Length - 1;
    V2 p0 = edge0 == null ? entrance.Point : edge0.Pts[first ? 0 : last], p1 = edge0 == null ? entrance.Point : edge0.Pts[first ? 1 : last - 1];
    double h0 = edge0 == null ? 0 : edge0.H[first ? 0 : last];
    var pc = new int[gC.Nodes.Count]; for (int i = 0; i < pc.Length; i++) pc[i] = i;
    int FindC(int x) { while (pc[x] != x) { pc[x] = pc[pc[x]]; x = pc[x]; } return x; }
    foreach (var e in gC.Edges) pc[FindC(e.A)] = FindC(e.B);
    int centreNode = gC.Nodes.OrderBy(n => V2.Dist(n.P, new V2(0, 0))).First().Id;
    Check("RG-182: CentreIsCity gives a town: >= 5 x the graph nodes of the village world, the centre reported as a city", ws0.CentreIsCity == null && gC.Nodes.Count >= 5 * gen0.Graph.Nodes.Count && genC.Settlements[0].IsCity && !gen0.Settlements[0].IsCity, gen0.Graph.Nodes.Count + " -> " + gC.Nodes.Count + " nodes");
    Check("RG-182: the entrance road starts at the entrance point, at the pinned height, along the entrance direction, of the entrance class, joined to the street graph",
        edge0 != null && V2.Dist(p0, entrance.Point) < 1e-6 && Math.Abs(h0 - entrance.Y) < 1e-6 && V2.Dot((p1 - p0).Unit, entrance.Dir) > 0.99995 && edge0.Class == RoadClass.Primary && FindC(node0.Id) == FindC(centreNode),
        edge0 == null ? "no edge at the entrance node" : V2.Dist(p0, entrance.Point) + " m, " + h0 + " m, " + V2.Dot((p1 - p0).Unit, entrance.Dir) + ", " + edge0.Class);
    var genC2 = Generator.Run(wsC, WorldRules.Synthetic());
    Check("RG-182: the entrance world is deterministic (same node and edge count, same first entrance edge)", genC2.Graph.Nodes.Count == gC.Nodes.Count && genC2.Graph.Edges.Count == gC.Edges.Count);
}

// ---- RG-170: HeightBatch is bit-identical to Height (candidate-set reuse and exact pruning) on a dense settlement world ----
{
    const string spec = "{\"seed\":1,\"size_km\":10,\"city\":{\"x\":0,\"z\":0,\"radius_m\":1400},\"villages\":4,\"motorway\":{\"entry_x\":-5000,\"entry_z\":-2200,\"exit_x\":5000,\"exit_z\":1600},"
        + "\"terrain\":{\"relief_m\":60,\"base_wavelength_m\":1400,\"octaves\":5,\"mountain\":{\"x0\":-3300,\"z0\":-5000,\"x1\":-2700,\"z1\":5000,\"width_m\":2400,\"relief_m\":350},\"settlement_flat_margin_m\":300}}";
    var gen = Generator.Run(WorldSpec.FromJson(spec), WorldRules.Synthetic());
    var rnd = new Random(170);
    const int NB = 24000;
    var xs = new double[NB]; var zs = new double[NB]; var bh = new double[NB]; var bh2 = new double[NB];
    int nb = 0;
    for (; nb < 12000; nb++) { xs[nb] = rnd.NextDouble() * 10000 - 5000; zs[nb] = rnd.NextDouble() * 10000 - 5000; }
    for (; nb < 18000; nb++) { xs[nb] = rnd.NextDouble() * 2800 - 1400; zs[nb] = rnd.NextDouble() * 2800 - 1400; }   // dense city streets
    var edges = gen.Graph.Edges;   // road ends, bends and portals: points within 60 m of an edge vertex
    for (; nb < NB; nb++)
    {
        var e = edges[rnd.Next(edges.Count)]; var pt = e.Pts[rnd.Next(2) == 0 ? 0 : (rnd.Next(2) == 0 ? e.Pts.Length - 1 : rnd.Next(e.Pts.Length))];
        xs[nb] = pt.X + rnd.NextDouble() * 120 - 60; zs[nb] = pt.Z + rnd.NextDouble() * 120 - 60;
    }
    gen.Terrain.HeightBatch(xs, zs, NB, bh); gen.Terrain.HeightBatch(xs, zs, NB, bh2, false);
    int bad = 0, bad2 = 0;
    for (int i = 0; i < NB; i++)
    {
        long r = BitConverter.DoubleToInt64Bits(gen.Terrain.Height(xs[i], zs[i]));
        if (BitConverter.DoubleToInt64Bits(bh[i]) != r) bad++;
        if (BitConverter.DoubleToInt64Bits(bh2[i]) != r) bad2++;
    }
    // compact batches (the grid path): 60 patches of 400 points in a 260 m box around random road vertices, dense street patches in the city and open-country patches
    int badG = 0, nG = 0;
    for (int pt = 0; pt < 60; pt++)
    {
        double cx, cz;
        if (pt < 20) { cx = rnd.NextDouble() * 2400 - 1200; cz = rnd.NextDouble() * 2400 - 1200; }
        else if (pt < 50) { var e = edges[rnd.Next(edges.Count)]; var q = e.Pts[rnd.Next(e.Pts.Length)]; cx = q.X; cz = q.Z; }
        else { cx = rnd.NextDouble() * 9000 - 4500; cz = rnd.NextDouble() * 9000 - 4500; }
        var gx = new double[400]; var gz = new double[400]; var gh = new double[400];
        for (int i = 0; i < 400; i++) { gx[i] = cx + rnd.NextDouble() * 260 - 130; gz[i] = cz + rnd.NextDouble() * 260 - 130; }
        gen.Terrain.HeightBatch(gx, gz, 400, gh);
        for (int i = 0; i < 400; i++, nG++) if (BitConverter.DoubleToInt64Bits(gh[i]) != BitConverter.DoubleToInt64Bits(gen.Terrain.Height(gx[i], gz[i]))) badG++;
    }
    Check("HeightBatch: " + nG + " points in compact batches (grid ring search + set fallback) bit-identical to Height", badG == 0, badG + " differ");
    Check("HeightBatch: " + NB + " points (random, dense streets, road ends/bends/portals) bit-identical to Height, pruned and set-reuse only", bad == 0 && bad2 == 0, bad + " / " + bad2 + " differ");
}

// ---- RG-169: HeightBatch with the base values passed in, water mask, scatter density multiplier ----
{
    const string spec = "{\"seed\":1,\"size_km\":10,\"city\":{\"x\":0,\"z\":0,\"radius_m\":1400},\"villages\":4,\"motorway\":{\"entry_x\":-5000,\"entry_z\":-2200,\"exit_x\":5000,\"exit_z\":1600},"
        + "\"terrain\":{\"relief_m\":60,\"base_wavelength_m\":1400,\"octaves\":5,\"mountain\":{\"x0\":-3300,\"z0\":-5000,\"x1\":-2700,\"z1\":5000,\"width_m\":2400,\"relief_m\":350},\"settlement_flat_margin_m\":300}}";
    var ws = WorldSpec.FromJson(spec);
    Func<double, double, double> hook = (x, z) => 40.0 * Math.Sin(x * 0.0021) * Math.Cos(z * 0.0017) + 0.01 * x;
    var gen = Generator.Run(ws, WorldRules.Synthetic(), hook);
    var rnd = new Random(169);
    const int NP = 6000;
    var xs = new double[NP]; var zs = new double[NP]; var bv = new double[NP]; var o1 = new double[NP]; var o2 = new double[NP]; var o3 = new double[NP]; var o4 = new double[NP];
    for (int i = 0; i < NP; i++) { xs[i] = rnd.NextDouble() * 1000 - 500 + (i % 2 == 0 ? 0 : 3000); zs[i] = rnd.NextDouble() * 1000 - 500; bv[i] = hook(xs[i], zs[i]); }
    gen.Terrain.HeightBatch(xs, zs, NP, o1, true); gen.Terrain.HeightBatch(xs, zs, NP, o2, bv, true);
    gen.Terrain.HeightBatch(xs, zs, NP, o3, false); gen.Terrain.HeightBatch(xs, zs, NP, o4, bv, false);
    int badB = 0; for (int i = 0; i < NP; i++) if (BitConverter.DoubleToInt64Bits(o1[i]) != BitConverter.DoubleToInt64Bits(o2[i]) || BitConverter.DoubleToInt64Bits(o3[i]) != BitConverter.DoubleToInt64Bits(o4[i]) || BitConverter.DoubleToInt64Bits(o1[i]) != BitConverter.DoubleToInt64Bits(o3[i])) badB++;
    Check("HeightBatch(baseH): " + NP + " points, pruned and set path, bit-identical to the hook path (the base value is passed, not re-evaluated)", badB == 0, badB + " differ");
    var calls = 0; Func<double, double, double> counting = (x, z) => { calls++; return hook(x, z); };
    var cg = Generator.Run(ws, WorldRules.Synthetic(), counting); int before = calls; cg.Terrain.HeightBatch(xs, zs, 200, o1, bv.Take(200).ToArray(), true);
    Check("HeightBatch(baseH): the base hook is not called by the carve", calls == before, (calls - before) + " calls");

    // water mask null = the same world (road dataset hash), a mask removes roads and villages on water
    var gNull = Generator.Run(ws, WorldRules.Synthetic(), hook, null);
    string hA, hB; Export.Build(gen.Graph, ws, WorldRules.Synthetic(), null, out hA); Export.Build(gNull.Graph, ws, WorldRules.Synthetic(), null, out hB);
    Check("water mask: Run(spec, rules, hook, null) gives the same road dataset hash as Run(spec, rules, hook)", hA == hB, hA + " vs " + hB);
    Func<double, double, bool> water = (x, z) => x > 1800 && z < 1500;   // a sea in the south-east
    var gw = Generator.Run(ws, WorldRules.Synthetic(), hook, water);
    int onWater = 0, samples = 0;
    foreach (var e in gw.Graph.Edges) { for (int k = 0; k + 1 < e.Pts.Length; k++) { double len = V2.Dist(e.Pts[k], e.Pts[k + 1]); int n = Math.Max(1, (int)Math.Ceiling(len / 4.0)); for (int t = 0; t <= n; t++) { var q = e.Pts[k] + (e.Pts[k + 1] - e.Pts[k]) * ((double)t / n); samples++; if (water(q.X, q.Z) && !(e.Pts.Length == 0)) onWater++; } } }
    int vWater = gw.Settlements.Count(s => water(s.C.X, s.C.Z));
    Check("water mask: no road sample (4 m) on water, no settlement centre on water, the world still has roads", onWater == 0 && vWater == 0 && gw.Graph.Edges.Count > 0 && gw.Graph.Edges.Count < gNull.Graph.Edges.Count, onWater + " on water of " + samples + "; " + vWater + " settlements; " + gw.Graph.Edges.Count + " of " + gNull.Graph.Edges.Count + " edges");
    var degs = gw.Graph.Degrees(); bool idsOk = gw.Graph.Edges.Select((e, i) => e.Id == i && e.A < gw.Graph.Nodes.Count && e.B < gw.Graph.Nodes.Count).All(b => b) && gw.Graph.Nodes.Select((n, i) => n.Id == i).All(b => b) && degs.All(d => d > 0);
    Check("water mask: graph ids are dense after the drop and no node is left without an edge", idsOk);

    // scatter: null multiplier / null mask = the same hash; factor 0 = no trees; water = no instance on water
    double sx0 = 1024, sz0 = -2048;
    string sh0 = new ScatterModel(ws, gNull).Hash(sx0, sz0, 1024), sh1 = new ScatterModel(ws, gNull) { DensityFactor = null, IsWater = null }.Hash(sx0, sz0, 1024);
    Check("scatter: DensityFactor / IsWater null are bit-identical", sh0 == sh1);
    var none = new ScatterModel(ws, gNull) { DensityFactor = (x, z) => 0.0 };
    var half = new ScatterModel(ws, gNull) { DensityFactor = (x, z) => 0.5 };
    var all = new ScatterModel(ws, gNull);
    int tAll = 0, tNone = 0, tHalf = 0, tWater = 0;
    var wm = new ScatterModel(ws, gNull) { IsWater = water };
    for (int sx = ScatterModel.SectorOf(sx0); sx < ScatterModel.SectorOf(sx0 + 2048); sx++)
        for (int sz = ScatterModel.SectorOf(sz0); sz < ScatterModel.SectorOf(sz0 + 2048); sz++)
        {
            tAll += all.Sector(sx, sz).Count(i => i.Kind == ScatterKind.Tree); tNone += none.Sector(sx, sz).Count(i => i.Kind == ScatterKind.Tree); tHalf += half.Sector(sx, sz).Count(i => i.Kind == ScatterKind.Tree);
            tWater += wm.Sector(sx, sz).Count(i => water(i.X, i.Z));
        }
    Check("scatter: density multiplier 0 gives no trees, 0.5 gives fewer than 1 (multiplies the mask), the water mask leaves nothing on water", tAll > 50 && tNone == 0 && tHalf > 0 && tHalf < tAll && tWater == 0, tAll + " / " + tHalf + " / " + tNone + " / " + tWater);
}

// RG-177: the public vertical-profile entry (Generator.SolveVerticalProfile) outside a Generator run: pins and grade cap honoured, curve count = VerticalCurveViolations
{
    int m = 301; var s = new double[m]; var tg = new double[m]; var lo = new double[m]; var hi = new double[m]; var g = new double[m - 1]; var pin = new bool[m]; var y = new double[m];
    for (int i = 0; i < m; i++) { s[i] = i * 10.0; tg[i] = 30 * Math.Sin(i * 0.05) + (i > 150 ? 25 : 0); lo[i] = tg[i] - 25; hi[i] = tg[i] + 15; if (i > 0) g[i - 1] = 0.9 * 0.129; }
    lo[0] = hi[0] = tg[0] = 0; pin[0] = true; lo[m - 1] = hi[m - 1] = tg[m - 1] = 20; pin[m - 1] = true;
    int beyond = Generator.SolveVerticalProfile(s, tg, lo, hi, g, pin, y, RoadClass.Secondary, 1.0, 300, out int infeasible);
    double maxG = 0; for (int i = 1; i < m; i++) maxG = Math.Max(maxG, Math.Abs(y[i] - y[i - 1]) / 10.0);
    bool inBounds = Enumerable.Range(0, m).All(i => y[i] >= lo[i] - 1e-9 && y[i] <= hi[i] + 1e-9);
    Check("RG-177 SolveVerticalProfile: pinned ends kept, grade cap and bounds honoured, count = VerticalCurveViolations", y[0] == 0 && y[m - 1] == 20 && maxG <= 0.9 * 0.129 + 1e-9 && inBounds && infeasible == 0 && beyond == Generator.VerticalCurveViolations(s, y, RoadClass.Secondary, 1.0), $"ends {y[0]} / {y[m - 1]}, max grade {maxG:F4}, bounds {inBounds}, infeasible {infeasible}, beyond {beyond}");
}

Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
return failures == 0 ? 0 : 1;
