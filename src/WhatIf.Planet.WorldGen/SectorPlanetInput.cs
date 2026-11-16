using System;
using WhatIf.Planet;

namespace WhatIf.Planet.WorldGen
{
    /// <summary>
    /// SectorPlanetInput v1 (what-if-planet-content-roads.md section 2, subset): what the planet layer hands one deformed level-15 sector so the planar
    /// WorldGen terrain model can run in the sector's local tangent plane. Fields: sector id, frame (from QuadSector), base height grid, world + region seed.
    /// Left out in v1 (later tickets): baseSlopeMax, detail per biome, coastDistM, biome/climate, settlements, corridors, portals.
    ///
    /// Local plane: gnomonic projection onto the tangent plane at the sector centre (x along East, z along North, metres). The constant-u / constant-v lattice
    /// lines of a cube face are great circles, so the quad's footprint in this plane is a quadrilateral with straight sides.
    ///
    /// Base grid: 16 m spacing, square, centred on the sector centre, interior half extent HalfM = 16 * ceil(max |corner x|, |corner z| / 16) plus a
    /// 1-sample apron for the Catmull-Rom (C1) interpolation. The RG-160 record assumed a flat 256 m sector (17 x 17 interior, 19 x 19 with apron); a
    /// deformed quad (216-306 m sides) does not fit into 256 m, so v1 keeps the 16 m spacing and sizes the grid per sector (Side x Side, Side >= 19).
    /// Every sample is PlanetNoise.Height at the global position of its local point (direction of Centre + East x + North z, times R): no sector-local seed.
    /// </summary>
    public sealed class SectorPlanetInput
    {
        public const int Version = 1;
        public const double SpacingM = 16.0;

        public readonly SectorId Id;
        public readonly QuadSector Sector;            // frame: Centre (double3, planet-fixed m), Up, East, North
        public readonly ulong WorldSeed, RegionSeed;
        public readonly double HalfM;                 // interior half extent of the grid (m)
        public readonly int Side;                     // samples per side incl. the apron
        public readonly float[] BaseHeightM;          // Side * Side, row-major, z slow: sample (ix, iz) at x = (ix - 1) * 16 - HalfM, z = (iz - 1) * 16 - HalfM

        SectorPlanetInput(SectorId id, ulong worldSeed)
        {
            Id = id; Sector = new QuadSector(id); WorldSeed = worldSeed; RegionSeed = RegionSeedOf(id, worldSeed);
            double ext = 0; int i0 = id.Col * QuadSector.Sub, j0 = id.Row * QuadSector.Sub;
            foreach (var (di, dj) in new[] { (0, 0), (QuadSector.Sub, 0), (QuadSector.Sub, QuadSector.Sub), (0, QuadSector.Sub) })
            {
                double x, z; ToLocal(Lattice.Direction(id.Face, i0 + di, j0 + dj), out x, out z);
                ext = Math.Max(ext, Math.Max(Math.Abs(x), Math.Abs(z)));
            }
            HalfM = SpacingM * Math.Ceiling(ext / SpacingM);
            Side = (int)Math.Round(2 * HalfM / SpacingM) + 3;
            BaseHeightM = new float[Side * Side];
        }

        /// <summary>Builds the record: (Side)^2 PlanetNoise samples at global positions.</summary>
        public static SectorPlanetInput Build(SectorId id, ulong worldSeed)
        {
            var s = new SectorPlanetInput(id, worldSeed);
            var sampler = new PlanetNoise.HeightSampler(worldSeed);
            for (int iz = 0, n = 0; iz < s.Side; iz++)
                for (int ix = 0; ix < s.Side; ix++, n++)
                    s.BaseHeightM[n] = sampler.Sample(s.LocalDirection(s.GridX(ix), s.GridX(iz)) * CubeSphere.Radius);
            return s;
        }

        /// <summary>Local coordinate of grid column / row k (the same for x and z).</summary>
        public double GridX(int k) => (k - 1) * SpacingM - HalfM;

        /// <summary>Region seed: the level-10 region of the sector mixed with the world seed (WorldGen's spec.Seed for this sector).</summary>
        public static ulong RegionSeedOf(SectorId id, ulong worldSeed) => RegionSeedOf(id.Region, worldSeed);

        /// <summary>Region seed of a level-10 region (RG-167; the same value v1 uses for every sector of the region).</summary>
        public static ulong RegionSeedOf(RegionId r, ulong worldSeed)
        {
            ulong key = ((ulong)(uint)r.Face << 42) | ((ulong)(uint)r.Row << 21) | (uint)r.Col;
            return SplitMix64.Mix(worldSeed ^ SplitMix64.Mix(key + 0x9E3779B97F4A7C15UL));
        }

        /// <summary>Unit direction of the local plane point (x, z) (inverse gnomonic).</summary>
        public Vec3 LocalDirection(double x, double z) => (Sector.Centre + Sector.East * x + Sector.North * z).Normalized();

        /// <summary>Gnomonic projection of a unit direction onto the sector's tangent plane.</summary>
        public void ToLocal(Vec3 dir, out double x, out double z)
        {
            double k = CubeSphere.Radius / Vec3.Dot(dir, Sector.Up);
            x = Vec3.Dot(dir, Sector.East) * k; z = Vec3.Dot(dir, Sector.North) * k;
        }

        /// <summary>Base height (m above the sphere) at local (x, z): Catmull-Rom over the grid (C1). This is the WorldGen-side base provider.</summary>
        public double Base(double x, double z)
        {
            double gx = (x + HalfM) / SpacingM + 1, gz = (z + HalfM) / SpacingM + 1;
            int ix = Math.Max(1, Math.Min(Side - 3, (int)Math.Floor(gx))), iz = Math.Max(1, Math.Min(Side - 3, (int)Math.Floor(gz)));
            double tx = gx - ix, tz = gz - iz;
            double r0 = Row(iz - 1, ix, tx), r1 = Row(iz, ix, tx), r2 = Row(iz + 1, ix, tx), r3 = Row(iz + 2, ix, tx);
            return CatmullRom(r0, r1, r2, r3, tz);
        }

        double Row(int iz, int ix, double t)
        {
            int b = iz * Side + ix;
            return CatmullRom(BaseHeightM[b - 1], BaseHeightM[b], BaseHeightM[b + 1], BaseHeightM[b + 2], t);
        }

        static double CatmullRom(double p0, double p1, double p2, double p3, double t) =>
            p1 + 0.5 * t * (p2 - p0 + t * (2 * p0 - 5 * p1 + 4 * p2 - p3 + t * (3 * (p1 - p2) + p3 - p0)));
    }
}
