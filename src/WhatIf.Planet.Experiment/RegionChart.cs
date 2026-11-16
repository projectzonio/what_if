using System;
using WhatIf.Planet;

namespace WhatIf.Planet.Experiment
{
    /// <summary>
    /// Geometry of one level-10 region in its gnomonic chart (RegionPlane: x along E, z along N, metres; a great circle is a straight line, so the four sides are
    /// straight lines and the region is a convex quadrilateral). Kept from the removed region world type (RG-185) for the anchor experiments: block search, top views,
    /// cold-touch test. No world, no Generator, no noise.
    /// </summary>
    public sealed class RegionChart
    {
        /// <summary>Lattice intervals per region side (32 sectors x 64).</summary>
        public const int Span = SectorId.PerRegionSide * QuadSector.Sub;

        public readonly RegionId Id;
        public readonly Vec3 C, E, N;                      // region centre direction, east, north
        public readonly double[] CornerX = new double[4], CornerZ = new double[4];   // corners in the chart: (I0,J0), (I0+S,J0), (I0+S,J0+S), (I0,J0+S)
        readonly double[] nx = new double[4], nz = new double[4], nc = new double[4];  // the four sides: unit normal (inward) and offset

        public RegionChart(RegionId id)
        {
            if (id.Level != SectorId.RegionLevel) throw new ArgumentException("region level must be " + SectorId.RegionLevel);
            Id = id;
            var plane = new RegionPlane(id); C = plane.C; E = plane.E; N = plane.N;
            int i0 = id.Col * Span, j0 = id.Row * Span;
            int[] ci = { 0, Span, Span, 0 }, cj = { 0, 0, Span, Span };
            for (int k = 0; k < 4; k++) ToChart(Lattice.Direction(id.Face, i0 + ci[k], j0 + cj[k]), out CornerX[k], out CornerZ[k]);
            double area2 = 0; for (int k = 0; k < 4; k++) { int m = (k + 1) & 3; area2 += CornerX[k] * CornerZ[m] - CornerX[m] * CornerZ[k]; }
            double sgn = area2 > 0 ? 1 : -1;   // counter-clockwise polygon: the inward normal is the left normal
            for (int k = 0; k < 4; k++)
            {
                int m = (k + 1) & 3; double ex = CornerX[m] - CornerX[k], ez = CornerZ[m] - CornerZ[k], l = Math.Sqrt(ex * ex + ez * ez);
                nx[k] = -ez / l * sgn; nz[k] = ex / l * sgn; nc[k] = nx[k] * CornerX[k] + nz[k] * CornerZ[k];
            }
        }

        /// <summary>Gnomonic chart point of a unit direction.</summary>
        public void ToChart(Vec3 dir, out double x, out double z)
        {
            double k = CubeSphere.Radius / Vec3.Dot(dir, C);
            x = Vec3.Dot(dir, E) * k; z = Vec3.Dot(dir, N) * k;
        }

        /// <summary>Unit direction of a chart point (inverse gnomonic).</summary>
        public Vec3 ToDir(double x, double z) => (C * CubeSphere.Radius + E * x + N * z).Normalized();

        /// <summary>Chart distance to the nearest region side (positive inside the polygon).</summary>
        public double BorderDist(double x, double z)
        {
            double d = double.MaxValue;
            for (int k = 0; k < 4; k++) d = Math.Min(d, nx[k] * x + nz[k] * z - nc[k]);
            return d;
        }
    }
}
