using System;
using System.Collections.Generic;
using System.Globalization;

namespace WhatIf.WorldGen
{
    public static class RoadClass
    {
        public const string Motorway = "motorway", Primary = "primary", Secondary = "secondary", Local = "local", Track = "track", Link = "link";
        public static readonly string[] All = { Motorway, Primary, Secondary, Local, Track, Link };
        /// <summary>Plan 2.4 design speeds, m/s (130, 100, 80, 60, 40, ramps 60 km/h).</summary>
        public static double DesignSpeedMs(string c)
        {
            switch (c)
            {
                case Motorway: return 130 / 3.6;
                case Primary: return 100 / 3.6;
                case Secondary: return 80 / 3.6;
                case Local: return 60 / 3.6;
                case Track: return 40 / 3.6;
                default: return 60 / 3.6;
            }
        }
        public static int Lanes(string c) { return c == Motorway ? 4 : c == Link || c == Track ? 1 : 2; }
        public static double Width(string c)
        {
            switch (c)
            {
                case Motorway: return 23.0;
                case Primary: return 7.0;
                case Secondary: return 6.5;
                case Local: return 6.0;
                case Track: return 3.0;
                default: return 5.5;
            }
        }
        public static int Rank(string c) { return Array.IndexOf(All, c); }
    }

    /// <summary>A fitted distribution: p05/p50/p95 plus n and the provisional flag (n &lt; 30) of world-rules.json.</summary>
    public sealed class Dist3
    {
        public double P05 = double.NaN, P50 = double.NaN, P95 = double.NaN;
        public int N;
        public bool Provisional = true;
        public bool Present { get { return !double.IsNaN(P05) && !double.IsNaN(P50); } }

        public Dist3() { }
        public Dist3(double p05, double p50, double p95, int n = 1000)
        {
            P05 = p05; P50 = p50; P95 = p95; N = n; Provisional = n < 30;
        }

        /// <summary>Log-normal through p05/p50 (lower tail) and p50/p95 (upper tail), clamped to [p05/2, 2*p95].</summary>
        public double Sample(Rng r)
        {
            double z = r.Normal();
            double p95 = double.IsNaN(P95) ? P50 * (P50 / P05) : P95;
            double lo = Math.Log(Math.Max(P50, 1e-3) / Math.Max(P05, 1e-3)) / 1.645;
            double hi = Math.Log(Math.Max(p95, 1e-3) / Math.Max(P50, 1e-3)) / 1.645;
            double x = P50 * Math.Exp(z * (z < 0 ? lo : hi));
            return Math.Min(Math.Max(x, P05 * 0.5), p95 * 2.0);
        }

        public static Dist3 FromJson(object o)
        {
            var d = new Dist3();
            if (!(o is JObj)) return d;
            d.P05 = Json.Num(o, double.NaN, "p05");
            d.P50 = Json.Num(o, double.NaN, "p50");
            d.P95 = Json.Num(o, double.NaN, "p95");
            d.N = (int)Json.Num(o, 0, "n");
            d.Provisional = !(Json.Path(o, "provisional") is bool b) || b;
            return d;
        }
    }

    public sealed class ClassRules
    {
        public string Name;
        public double MinRadiusM = double.NaN;
        public double MaxGradePct = double.NaN;
        public bool RadiusProvisional;
        public Dist3 CurveRadius = new Dist3();
        public Dist3 StraightRun = new Dist3(), CurveRun = new Dist3();
        public Dist3 Spacing = new Dist3(), LegAngle = new Dist3();
        public Dist3 BridgeLength = new Dist3(), TunnelLength = new Dist3();
    }

    /// <summary>Fitted per-class rules (RG-109 world-rules.json) plus the design constants of the RG-109 threshold decision.
    /// A provisional fitted rule (n &lt; 30) is used only if the spec allows it by name, e.g. "motorway.junctions.spacing_m".</summary>
    public sealed class WorldRules
    {
        public readonly Dictionary<string, ClassRules> Classes = new Dictionary<string, ClassRules>();
        public readonly HashSet<string> AllowProvisional = new HashSet<string>();
        public string Sha256 = "";

        // Design values (RG-109 review, "threshold decision"; not data).
        public double LegAngleMinDeg = 70.0;
        public double TunnelMinM = 150.0;
        public double BridgeMinM = 60.0;
        public double ClearanceM = 5.0;
        public double ArcSeconds = 3.0;
        public double EndStraightM = 30.0;
        public double CurvatureJumpMax = 0.002;
        /// <summary>Spacing floor when the fitted p05 is missing or provisional and not allowed.</summary>
        public double DesignSpacingM = 30.0;
        public double StepM = 10.0;

        public ClassRules Of(string c)
        {
            ClassRules r;
            if (!Classes.TryGetValue(c, out r)) throw new InvalidOperationException("no rules for class " + c);
            return r;
        }

        public bool Usable(string cls, string key, Dist3 d)
        {
            return d.Present && (!d.Provisional || AllowProvisional.Contains(cls + "." + key));
        }

        /// <summary>Minimum vertical curve radius (RG-123 design value, orchestrator decision; not fitted data): crest / sag, metres.</summary>
        public static double VRadiusMin(string c, bool crest)
        {
            switch (c)
            {
                case RoadClass.Motorway: return crest ? 6000 : 3000;
                case RoadClass.Link: return crest ? 1000 : 600;
                case RoadClass.Primary: return crest ? 2500 : 1500;
                case RoadClass.Secondary: return crest ? 1200 : 800;
                case RoadClass.Local: return crest ? 400 : 300;
                default: return crest ? 200 : 150;
            }
        }

        /// <summary>Junction spacing floor for the class: fitted p05 when usable, else the design constant.</summary>
        public double SpacingFloor(string c)
        {
            var r = Of(c);
            return Usable(c, "junctions.spacing_m", r.Spacing) ? Math.Max(r.Spacing.P05, DesignSpacingM * 0.5) : DesignSpacingM;
        }

        public double MinFork(string c)
        {
            var r = Of(c);
            return Usable(c, "junctions.leg_angle_deg", r.LegAngle) ? Math.Min(LegAngleMinDeg, r.LegAngle.P05) : 15.0;
        }

        /// <summary>Minimum bridge length for the class: 60 m on motorway and link, else the class p05 (usable) or 10 m.</summary>
        public double BridgeMin(string c)
        {
            if (c == RoadClass.Motorway || c == RoadClass.Link) return BridgeMinM;
            var r = Of(c);
            return r.BridgeLength.Present ? r.BridgeLength.P05 : 10.0;
        }

        /// <summary>Minimum tunnel length for the class (RG-118): motorway 150 m, else the fitted p05 or 60 m, never below 30 m.</summary>
        public double TunnelMin(string c)
        {
            if (c == RoadClass.Motorway) return TunnelMinM;
            var r = Of(c);
            return Math.Max(30.0, r.TunnelLength.Present ? r.TunnelLength.P05 : 60.0);
        }

        public static WorldRules FromJson(string text, string sha256)
        {
            var root = Json.Parse(text);
            var w = new WorldRules { Sha256 = sha256 };
            foreach (var c in RoadClass.All)
            {
                var cr = new ClassRules { Name = c };
                cr.MinRadiusM = Json.Num(root, double.NaN, "classes", c, "radius", "min_radius_m");
                cr.RadiusProvisional = Json.Path(root, "classes", c, "radius", "curve_samples", "provisional") is bool b && b;
                cr.CurveRadius = Dist3.FromJson(Json.Path(root, "classes", c, "radius", "curve_samples"));
                cr.MaxGradePct = Json.Num(root, double.NaN, "classes", c, "grade", "max_grade_pct");
                cr.StraightRun = Dist3.FromJson(Json.Path(root, "classes", c, "segments", "straight_run_m"));
                cr.CurveRun = Dist3.FromJson(Json.Path(root, "classes", c, "segments", "curve_run_m"));
                cr.Spacing = Dist3.FromJson(Json.Path(root, "classes", c, "junctions", "spacing_m"));
                cr.LegAngle = Dist3.FromJson(Json.Path(root, "classes", c, "junctions", "leg_angle_deg"));
                cr.BridgeLength = Dist3.FromJson(Json.Path(root, "classes", c, "spans", "bridge", "length_m"));
                cr.TunnelLength = Dist3.FromJson(Json.Path(root, "classes", c, "spans", "tunnel", "length_m"));
                if (double.IsNaN(cr.MinRadiusM) || double.IsNaN(cr.MaxGradePct))
                    throw new FormatException("world-rules: class " + c + " lacks min_radius_m or max_grade_pct");
                w.Classes[c] = cr;
            }
            return w;
        }

        /// <summary>Small synthetic rules for unit tests (no real data; mirrors the order of magnitude of the fitted values).</summary>
        public static WorldRules Synthetic()
        {
            var w = new WorldRules { Sha256 = "synthetic" };
            void Add(string c, double minR, double grade, double r50, double s05, double s50, double s95, double c05, double c50, double c95, double sp)
            {
                w.Classes[c] = new ClassRules
                {
                    Name = c,
                    MinRadiusM = minR,
                    MaxGradePct = grade,
                    CurveRadius = new Dist3(minR, r50, 1000),
                    StraightRun = new Dist3(s05, s50, s95),
                    CurveRun = new Dist3(c05, c50, c95),
                    Spacing = new Dist3(sp, sp * 4, sp * 20),
                    LegAngle = new Dist3(10, 60, 90),
                    BridgeLength = new Dist3(c == RoadClass.Motorway || c == RoadClass.Link ? 20 : 8, 40, 200),
                    TunnelLength = new Dist3(20, 80, 500, 10),
                };
            }
            Add(RoadClass.Motorway, 345, 6.3, 714, 20, 70, 560, 10, 20, 118, 40);
            Add(RoadClass.Primary, 121, 10.8, 439, 10, 85, 423, 10, 65, 199, 32);
            Add(RoadClass.Secondary, 70, 12.9, 280, 10, 51, 249, 20, 66, 179, 34);
            Add(RoadClass.Local, 36, 16.5, 214, 10, 58, 175, 20, 69, 163, 32);
            Add(RoadClass.Track, 44, 21.3, 211, 10, 31, 173, 20, 60, 197, 38);
            Add(RoadClass.Link, 41, 9.2, 153, 10, 47, 182, 30, 88, 232, 24);
            return w;
        }
    }

    /// <summary>RG-182: a road that enters the settlement from outside. Point = where it crosses the settlement edge, Dir = unit direction pointing into the settlement,
    /// Y = pinned height of the first station. The generator runs a straight road from Point along Dir until it joins the street graph.</summary>
    public sealed class Entrance
    {
        public V2 Point, Dir; public double Y; public string Class = RoadClass.Secondary;
    }

    public sealed class WorldSpec
    {
        public long Seed = 1;
        public double SizeKm = 10;
        public V2 City = new V2(0, 0);
        public double CityRadiusM = 1400;
        public int Villages = 4;
        public double VillageMinKm = 3, VillageMaxKm = 8;
        public V2 MotorwayEntry = new V2(-5000, -2200), MotorwayExit = new V2(5000, 1600);
        public List<string> AllowProvisional = new List<string>();
        public TerrainSpec Terrain = new TerrainSpec();
        public double VCurveScale = 1.0;   // spec terrain.vcurve_scale (RG-123)
        public bool Motorway = true;       // RG-176: false = village-only world (no motorway, no interchange; _set[0] is a village of radius CityRadiusM at City); true = NightRacer behaviour
        public int VcStallOuter = 0;       // RG-175: vertical-curve stall exit, outer rounds without improvement of the infeasible count (0 = off = NightRacer behaviour; set by the planet SectorAdapter)
        public bool? CentreIsCity = null;  // RG-182: class of _set[0] decoupled from Motorway (null = IsCity follows Motorway = NightRacer behaviour); true = city street budget and arterial seeds
        public List<Entrance> Entrances = new List<Entrance>();   // RG-182: pinned corridor entrances of _set[0] (empty = NightRacer behaviour)
        public string Raw = "";

        public double Half { get { return SizeKm * 500.0; } }

        public static WorldSpec FromJson(string text)
        {
            var root = Json.Parse(text);
            var s = new WorldSpec { Raw = text };
            s.Seed = (long)Json.Num(root, 1, "seed");
            s.SizeKm = Json.Num(root, 10, "size_km");
            s.City = new V2(Json.Num(root, 0, "city", "x"), Json.Num(root, 0, "city", "z"));
            s.CityRadiusM = Json.Num(root, 1400, "city", "radius_m");
            s.Villages = (int)Json.Num(root, 4, "villages");
            s.VillageMinKm = Json.Num(root, 3, "village_spacing_km", "min");
            s.VillageMaxKm = Json.Num(root, 8, "village_spacing_km", "max");
            s.MotorwayEntry = new V2(Json.Num(root, -5000, "motorway", "entry_x"), Json.Num(root, -2200, "motorway", "entry_z"));
            s.MotorwayExit = new V2(Json.Num(root, 5000, "motorway", "exit_x"), Json.Num(root, 1600, "motorway", "exit_z"));
            if (Json.Path(root, "allow_provisional") is List<object> l)
                foreach (var x in l) if (x is string str) s.AllowProvisional.Add(str);
            s.Terrain = TerrainSpec.FromJson(root);
            s.VCurveScale = Json.Num(root, 1.0, "terrain", "vcurve_scale");
            return s;
        }

        public string Describe()
        {
            return string.Format(CultureInfo.InvariantCulture, "seed {0}, {1} km, city ({2},{3}) r {4} m, {5} villages", Seed, SizeKm, City.X, City.Z, CityRadiusM, Villages);
        }
    }
}
