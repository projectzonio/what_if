using System;
using System.Collections.Generic;
using WhatIf.Planet;
using WhatIf.Planet.Surface;

namespace WhatIf.Planet.Anchors
{
    /// <summary>Settlement class; the index is also the network layer the settlement joins (0 secondary, 1 primary, 2 motorway).</summary>
    public enum SettlementClass : byte { Village = 0, Town = 1, City = 2 }

    /// <summary>One anchor settlement: a pure function of (world seed, cell id, class). Dir = unit direction of the centre, HeightM = surface height there (the entrance height of every corridor).</summary>
    public sealed class Settlement
    {
        public ulong Id; public RegionId Cell; public SettlementClass Class; public Vec3 Dir; public double HeightM, RadiusM; public bool Coast;
        public override string ToString() => $"{Class} {Cell}";
    }

    /// <summary>One network link between two settlements (A.Id &lt; B.Id), with the road class of the highest layer that contains the pair.</summary>
    public sealed class Link
    {
        public Settlement A, B; public int Layer; public double GcM;
        public string RoadClass => AnchorParams.LayerClass[Layer];
        public ulong Key => SplitMix64.Mix(A.Id * 0x9E3779B97F4A7C15UL ^ B.Id);
    }

    /// <summary>Design constants of the spike (chosen in RG-177, see the outbox for the reasons).</summary>
    public static class AnchorParams
    {
        /// <summary>Cell level per class (village 11 ~ 4.9 km, town 10 ~ 9.8 km = one region, city 9 ~ 19.6 km); at most one settlement of the class per cell.</summary>
        public static readonly int[] Level = { 11, 10, 9 };
        /// <summary>Acceptance probability of a cell with a valid site (x CoastBoost when the sea is within 3 km).</summary>
        public static readonly double[] Accept = { 0.6, 0.5, 0.5 };
        public const double CoastBoost = 1.5;
        public static readonly double[] RadiusM = { 450, 900, 1400 };
        /// <summary>Max height difference / distance from the centre to the 8-point ring at 0.6 radius.</summary>
        public static readonly double[] SlopeMax = { 0.12, 0.09, 0.07 };
        /// <summary>No settlement of this class closer than this to one of a higher class (m): village 2.5 km from a town/city, town 5 km from a city.</summary>
        public static readonly double[] ClearM = { 2500, 5000, 0 };
        /// <summary>Network layer radius (m): RNG among settlements of class >= layer within this distance. Locality: the links of a settlement follow from the settlements within LayerRadius[2] of it.</summary>
        public static readonly double[] LayerRadius = { 10000, 20000, 40000 };
        public static readonly string[] LayerClass = { WhatIf.WorldGen.RoadClass.Secondary, WhatIf.WorldGen.RoadClass.Primary, WhatIf.WorldGen.RoadClass.Motorway };
        public const int Attempts = 4;
        /// <summary>RG-178: blend band of the settlement terrain outside the settlement radius (m) and the largest reach of a corridor carve around the edge of its footprint (shoulder + 20 m x 1.5, plus margin).</summary>
        public const double BlendM = 200, ReachM = 1.5 + 30 + 11.5 + 20;
    }

    /// <summary>
    /// RG-177 A1 + A2: settlements and network links. Every value is a pure function of the seed and cell ids; the per-instance memo only saves work (order of
    /// queries never changes a result). One instance per thread (the PlanetSurface it samples is not thread safe).
    /// </summary>
    public sealed class SettlementField
    {
        public readonly ulong Seed;
        public readonly PlanetSurface Surface;
        readonly Dictionary<RegionId, Settlement> memo = new Dictionary<RegionId, Settlement>();
        const double R = CubeSphere.Radius;

        public SettlementField(ulong seed, PlanetSurface surface) { Seed = seed; Surface = surface; }
        public int CellsEvaluated => memo.Count;

        public static double Gc(Vec3 a, Vec3 b) => R * Vec3.Angle(a, b);
        public static ulong IdOf(RegionId c) => ((ulong)(uint)c.Level << 48) | ((ulong)(uint)c.Face << 44) | ((ulong)(uint)c.Row << 22) | (ulong)(uint)c.Col;

        double U01(ulong id, ulong salt) => (SplitMix64.Mix(Seed ^ SplitMix64.Mix(id + salt * 0xD1B54A32D192ED03UL)) >> 11) * (1.0 / 9007199254740992.0);

        /// <summary>Point at tangent offset (x east, z north, metres) from a direction.</summary>
        public static Vec3 Offset(Vec3 d, double x, double z)
        {
            CubeSphere.ToFaceAB(d, out int f, out _, out _);
            CubeSphere.TangentAxes(f, d, out var e, out var n);
            return (d * R + e * x + n * z).Normalized();
        }

        static bool Water(SurfaceKind k) => k == SurfaceKind.Ocean || k == SurfaceKind.Sea || k == SurfaceKind.Lake;

        /// <summary>The settlement of a cell (level = AnchorParams.Level of its class) or null.</summary>
        public Settlement Cell(RegionId cell)
        {
            if (memo.TryGetValue(cell, out var s)) return s;
            s = Evaluate(cell);
            memo[cell] = s;
            return s;
        }

        Settlement Evaluate(RegionId cell)
        {
            int c = Array.IndexOf(AnchorParams.Level, cell.Level);
            if (c < 0) throw new ArgumentException("no settlement class at level " + cell.Level);
            ulong id = IdOf(cell); int n = 1 << cell.Level;
            for (int at = 0; at < AnchorParams.Attempts; at++)
            {
                double u = 0.1 + 0.8 * U01(id, (ulong)(2 * at + 1)), v = 0.1 + 0.8 * U01(id, (ulong)(2 * at + 2));
                var d = CubeSphere.FromFaceAB(cell.Face, -1.0 + 2.0 * (cell.Col + u) / n, -1.0 + 2.0 * (cell.Row + v) / n);
                if (!Site(d, c, out double h, out bool coast)) continue;
                if (U01(id, 99) >= AnchorParams.Accept[c] * (coast ? AnchorParams.CoastBoost : 1)) return null;   // one draw per cell: a valid site is accepted or the cell stays empty
                for (int hc = c + 1; hc <= 2; hc++)
                    if (Within(d, AnchorParams.ClearM[c], hc, hc).Count > 0) return null;
                return new Settlement { Id = id, Cell = cell, Class = (SettlementClass)c, Dir = d, HeightM = h + Ground.Detail(Seed, d), RadiusM = AnchorParams.RadiusM[c], Coast = coast };
            }
            return null;
        }

        /// <summary>Site test of class c at d: centre on Land (not Beach), the 8-point ring at 0.6 radius dry and within the slope limit; city: also an 8-point ring at 2.5 km dry. coast = Ocean/Sea on the 3 km ring.</summary>
        bool Site(Vec3 d, int c, out double h0, out bool coast)
        {
            coast = false;
            h0 = Surface.HeightKind(d, out var k0);
            if (k0 != SurfaceKind.Land) return false;
            double r = 0.6 * AnchorParams.RadiusM[c];
            for (int k = 0; k < 8; k++)
            {
                double a = k * Math.PI / 4;
                double h = Surface.HeightKind(Offset(d, r * Math.Cos(a), r * Math.Sin(a)), out var kk);
                if (Water(kk) || Math.Abs(h - h0) > AnchorParams.SlopeMax[c] * r) return false;
            }
            for (int k = 0; k < 8; k++)
            {
                double a = k * Math.PI / 4 + Math.PI / 8;
                if (c == 2 && Water(Surface.Kind(Offset(d, 2500 * Math.Cos(a), 2500 * Math.Sin(a))))) return false;
                var kc = Surface.Kind(Offset(d, 3000 * Math.Cos(a), 3000 * Math.Sin(a)));
                if (kc == SurfaceKind.Ocean || kc == SurfaceKind.Sea) coast = true;
            }
            return true;
        }

        /// <summary>Cells of a level that may hold a point within distM of p: breadth-first over cell neighbours from p's cell, keeping cells whose disc (centre, max corner angle) reaches the circle. Sorted by id.</summary>
        public static List<RegionId> CellsNear(Vec3 p, double distM, int level)
        {
            var start = CubeSphere.RegionOf(p, level);
            var seen = new HashSet<RegionId> { start }; var q = new Queue<RegionId>(); q.Enqueue(start); var res = new List<RegionId>();
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                var ctr = CubeSphere.RegionCentre(c); double rad = 0;
                for (int cu = 0; cu < 2; cu++) for (int cv = 0; cv < 2; cv++) rad = Math.Max(rad, Gc(ctr, CubeSphere.RegionCorner(c, cu, cv)));
                if (!c.Equals(start) && Gc(p, ctr) - rad > distM) continue;
                res.Add(c);
                for (int dir = 0; dir < 4; dir++) { var nb = CubeSphere.Neighbour(c, dir); if (seen.Add(nb)) q.Enqueue(nb); }
            }
            res.Sort((a, b) => IdOf(a).CompareTo(IdOf(b)));
            return res;
        }

        /// <summary>Settlements of class minClass..maxClass within distM of p (great circle to the centre), sorted by id.</summary>
        public List<Settlement> Within(Vec3 p, double distM, int minClass, int maxClass = 2)
        {
            var res = new List<Settlement>();
            for (int c = minClass; c <= maxClass; c++)
                foreach (var cell in CellsNear(p, distM, AnchorParams.Level[c]))
                {
                    var s = Cell(cell);
                    if (s != null && Gc(p, s.Dir) <= distM) res.Add(s);
                }
            res.Sort((a, b) => a.Id.CompareTo(b.Id));
            return res;
        }

        /// <summary>
        /// Links of a settlement: per layer k &lt;= class, the relative neighbourhood graph among settlements of class >= k within LayerRadius[k] (pair a-b iff no c with
        /// max(d(a,c), d(b,c)) &lt; d(a,b)); every witness lies within d(a,b) of a, so the test uses only the settlements within LayerRadius[k] of a and is symmetric.
        /// A pair in several layers gets the highest. Sorted by the partner id.
        /// </summary>
        public List<Link> LinksOf(Settlement a)
        {
            var best = new SortedDictionary<ulong, Link>();
            for (int k = 0; k <= (int)a.Class; k++)
            {
                var cand = Within(a.Dir, AnchorParams.LayerRadius[k], k);
                foreach (var b in cand)
                {
                    if (b.Id == a.Id) continue;
                    double dab = Gc(a.Dir, b.Dir); bool ok = true;
                    foreach (var c in cand)
                    {
                        if (c.Id == a.Id || c.Id == b.Id) continue;
                        if (Gc(a.Dir, c.Dir) < dab && Gc(b.Dir, c.Dir) < dab) { ok = false; break; }
                    }
                    if (!ok) continue;
                    var lo = a.Id < b.Id ? a : b; var hi = a.Id < b.Id ? b : a;
                    best[b.Id] = new Link { A = lo, B = hi, Layer = k, GcM = dab };   // higher k overwrites
                }
            }
            return new List<Link>(best.Values);
        }
    }
}
