using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using WhatIf.WorldGen;
using WhatIf.Planet;
using WhatIf.Planet.Surface;
using WhatIf.Planet.WorldGen;

namespace WhatIf.Planet.Experiment
{
    /// <summary>One 3 x 3 block of level-10 regions: the centre and the nine ids (row-major from the south-west, chart axes E / N of the centre).</summary>
    public sealed class BlockInfo
    {
        public string Name, Why; public RegionId Centre; public RegionId[] Regions;
        public string Describe() => $"{Name}: centre {Centre}; regions {string.Join(" ", Regions)}; {Why}";
    }

    /// <summary>
    /// RG-169 block search (kept by RG-185 for the anchor experiments, the region-first checks p1-p7 were removed): two deterministic 3 x 3 blocks of level-10 regions
    /// (seed 1): A = coast + a lake on one face, B = across a cube edge.
    /// </summary>
    public static class BlockChecks
    {
        public const ulong Seed = 1;
        const int RM = 1 << SectorId.RegionLevel, P = SectorId.PerRegionSide;
        const double R = CubeSphere.Radius;

        // ------------------------------------------------------------------ block selection (deterministic, seed 1)

        /// <summary>The 3 x 3 block around a region: the regions containing the chart points (dc, dr) x side length of the centre region (E, N axes of its chart).</summary>
        public static RegionId[] Block(RegionId centre)
        {
            var c = new RegionChart(centre); double wd = 0;
            for (int k = 0; k < 4; k++) { int m = (k + 1) & 3; wd += Math.Sqrt((c.CornerX[m] - c.CornerX[k]) * (c.CornerX[m] - c.CornerX[k]) + (c.CornerZ[m] - c.CornerZ[k]) * (c.CornerZ[m] - c.CornerZ[k])); }
            wd /= 4; var l = new RegionId[9]; int n = 0;
            for (int dr = -1; dr <= 1; dr++) for (int dc = -1; dc <= 1; dc++) l[n++] = CubeSphere.RegionOf(c.ToDir(dc * wd, dr * wd), SectorId.RegionLevel);
            return l;
        }

        /// <summary>Sector pairs (a in region a, neighbour in region b) along the border of the two regions.</summary>
        public static List<(SectorId A, SectorId B)> BorderPairs(RegionId a, RegionId b)
        {
            var res = new List<(SectorId, SectorId)>();
            for (int r = 0; r < P; r++)
                for (int c = 0; c < P; c++)
                {
                    if (r != 0 && r != P - 1 && c != 0 && c != P - 1) continue;
                    var id = new SectorId(a.Face, a.Row * P + r, a.Col * P + c);
                    for (int d = 0; d < 4; d++) { var nb = id.Neighbour(d); if (nb.Region.Equals(b)) res.Add((id, nb)); }
                }
            return res;
        }

        /// <summary>The 12 inner borders of a block (horizontal and vertical neighbours in the 3 x 3 grid); false when the nine regions are not distinct or a border is missing.</summary>
        public static bool Valid(RegionId[] b, out List<(int, int)> borders)
        {
            borders = new List<(int, int)>();
            if (b.Distinct().Count() != 9) return false;
            for (int r = 0; r < 3; r++) for (int c = 0; c < 3; c++)
                {
                    if (c < 2) borders.Add((r * 3 + c, r * 3 + c + 1));
                    if (r < 2) borders.Add((r * 3 + c, (r + 1) * 3 + c));
                }
            foreach (var (i, j) in borders) if (BorderPairs(b[i], b[j]).Count == 0) return false;
            return true;
        }

        static Vec3 RandomDir(ref ulong st)
        {
            while (true) { var v = new Vec3(SplitMix64.NextDouble(ref st) * 2 - 1, SplitMix64.NextDouble(ref st) * 2 - 1, SplitMix64.NextDouble(ref st) * 2 - 1); double l = v.Length; if (l > 0.05 && l <= 1) return v / l; }
        }

        /// <summary>Share of 9 x 9 lattice samples per kind over the 9 regions: [water (Ocean/Sea), land (Beach/Land), lake].</summary>
        static double[] KindShares(PlanetSurface s, RegionId[] regions)
        {
            double wat = 0, land = 0, lake = 0; int n = 0;
            foreach (var r in regions)
                for (int b = 0; b <= 8; b++) for (int a = 0; a <= 8; a++)
                    {
                        var k = s.Kind(Lattice.Direction(r.Face, r.Col * RegionChart.Span + a * (RegionChart.Span / 8), r.Row * RegionChart.Span + b * (RegionChart.Span / 8))); n++;
                        if (k == SurfaceKind.Ocean || k == SurfaceKind.Sea) wat++; else if (k == SurfaceKind.Lake) lake++; else land++;
                    }
            return new[] { wat / n, land / n, lake / n };
        }

        /// <summary>Lakes (3D cells of 8192 m) whose centre lies in one of the regions.</summary>
        static int LakesIn(PlanetSurface s, RegionId[] regions)
        {
            double mnx = double.MaxValue, mny = double.MaxValue, mnz = double.MaxValue, mxx = double.MinValue, mxy = double.MinValue, mxz = double.MinValue;
            foreach (var r in regions)
                foreach (var (a, b) in new[] { (0, 0), (RegionChart.Span, 0), (0, RegionChart.Span), (RegionChart.Span, RegionChart.Span) })
                {
                    var p = Lattice.Direction(r.Face, r.Col * RegionChart.Span + a, r.Row * RegionChart.Span + b) * R;
                    mnx = Math.Min(mnx, p.X); mxx = Math.Max(mxx, p.X); mny = Math.Min(mny, p.Y); mxy = Math.Max(mxy, p.Y); mnz = Math.Min(mnz, p.Z); mxz = Math.Max(mxz, p.Z);
                }
            int n = 0; var set = new HashSet<RegionId>(regions);
            for (long ix = PlanetSurface.CellOf(mnx - 2000); ix <= PlanetSurface.CellOf(mxx + 2000); ix++)
                for (long iy = PlanetSurface.CellOf(mny - 2000); iy <= PlanetSurface.CellOf(mxy + 2000); iy++)
                    for (long iz = PlanetSurface.CellOf(mnz - 2000); iz <= PlanetSurface.CellOf(mxz + 2000); iz++)
                    {
                        var lake = s.LakeOfCell(ix, iy, iz); if (lake == null) continue;
                        if (set.Contains(CubeSphere.RegionOf(lake.Centre, SectorId.RegionLevel))) n++;
                    }
            return n;
        }

        /// <summary>Block A: a coast (>= 12 % sea) with land (>= 35 %) and at least one lake, on one face away from its edges. First hit of a seeded random walk over coastal land points.</summary>
        public static BlockInfo FindBlockA(PlanetSurface s)
        {
            ulong st = 0xA11CE5UL; int tried = 0;
            for (int i = 0; i < 4000000; i++)
            {
                var d = RandomDir(ref st); var h = s.HeightKind(d, out var k);
                if (k != SurfaceKind.Land || h > 40) continue;
                var r = CubeSphere.RegionOf(d, SectorId.RegionLevel);
                if (r.Row < 3 || r.Row > RM - 4 || r.Col < 3 || r.Col > RM - 4) continue;
                var b = Block(r); tried++;
                if (!Valid(b, out _) || b.Any(x => x.Face != r.Face)) continue;
                var sh = KindShares(s, b); if (sh[0] < 0.12 || sh[1] < 0.35) continue;
                int lakes = LakesIn(s, b); if (lakes < 1) continue;
                return new BlockInfo { Name = "A", Centre = r, Regions = b, Why = $"coast + lake, one face; 9 x 9 probe per region: {sh[0] * 100:F0} % sea, {sh[1] * 100:F0} % land, {lakes} lake(s) with the centre in the block; {tried} candidates examined" };
            }
            throw new InvalidOperationException("no block A found");
        }

        /// <summary>Block B: the centre region on a cube edge (the block spans two faces), at least seven of nine regions with >= 10 % land. First hit scanning faces 0..5, rows 64..960 step 32.</summary>
        public static BlockInfo FindBlockB(PlanetSurface s)
        {
            for (int f = 0; f < 6; f++)
                for (int row = 64; row < RM - 64; row += 32)
                {
                    var r = new RegionId(f, SectorId.RegionLevel, row, RM - 1);
                    var b = Block(r);
                    if (!Valid(b, out _) || b.Select(x => x.Face).Distinct().Count() != 2) continue;
                    int landish = 0;
                    foreach (var x in b) { var sh = KindShares(s, new[] { x }); if (sh[1] + sh[2] >= 0.10) landish++; }
                    if (landish < 7) continue;
                    var all = KindShares(s, b);
                    return new BlockInfo { Name = "B", Centre = r, Regions = b, Why = $"across the cube edge between faces {string.Join(" and ", b.Select(x => x.Face).Distinct())}; {landish} of 9 regions with >= 10 % land; {all[0] * 100:F0} % sea, {all[1] * 100:F0} % land in the 9 x 9 probe" };
                }
            throw new InvalidOperationException("no block B found");
        }
    }
}
