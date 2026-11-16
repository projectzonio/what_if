using System;
using WhatIf.Planet;
using TerrainModel = WhatIf.WorldGen.TerrainModel;
using TerrainSpec = WhatIf.WorldGen.TerrainSpec;
using WorldSpec = WhatIf.WorldGen.WorldSpec;

namespace WhatIf.Planet.WorldGen
{
    /// <summary>One adapted sector: the 65 x 65 lattice vertices with their local plane point, planet base, raw WorldGen contribution, taper and position.</summary>
    public sealed class AdaptedSector
    {
        public const int Verts = QuadSector.Verts, Count = Verts * Verts;
        public readonly double[] X = new double[Count], Z = new double[Count];   // gnomonic local plane point of the vertex (m)
        public readonly double[] PlanetBase = new double[Count];                 // PlanetNoise.Height at dir * R (the same value QuadSector.Build uses)
        public readonly double[] Contribution = new double[Count];               // WorldGen height minus its base provider, before the taper (m)
        public readonly double[] Taper = new double[Count];                      // 0 on the quad edge, 1 from TaperM inwards
        public readonly double[] EdgeDistM = new double[Count];                  // distance to the nearest quad side in the local plane (m)
        public readonly byte[] Surface = new byte[Count];                        // RG-183 (AnchorSector only): 0 ground, 1 corridor road, 2 settlement street, 3 shoulder; cleared by the builder
        public readonly Vec3[] World = new Vec3[Count];                          // dir * (R + h), planet-fixed double
        public readonly float[] Lx = new float[Count], Ly = new float[Count], Lz = new float[Count];   // float mesh relative to the centre in the sector frame
        public static int Idx(int i, int j) => j * Verts + i;
    }

    /// <summary>
    /// RG-165 adapter: one deformed level-15 sector -> WorldGen near terrain on the planet. Per vertex: h = planet base (exact, global) + taper * (WorldGen
    /// height - its base provider), world = dir * (R + h). WorldGen sees the SectorPlanetInput grid as its low-frequency base (TerrainModel hook) and adds
    /// its detail octaves and road carving on top; that contribution is faded to 0 on the quad edge, so two neighbours (different frames, different local
    /// detail) meet exactly on the planet base (what-if-planet-content-roads.md section 2, rule 3).
    /// </summary>
    public static class SectorAdapter
    {
        /// <summary>Width of the border taper band (m, measured in the local plane from the nearest quad side). Smoothstep inside the band.</summary>
        public const double TaperM = 32.0;

        /// <summary>RG-175: vertical-curve stall exit for planet regions (outer rounds without improvement; NightRacer default 0 = off).</summary>
        public const int VcStallOuter = 300;

        /// <summary>WorldGen terrain spec of a sector: the existing "hilly" values (relief 60 m, wavelength 1400 m, 5 octaves) without mountain; under the
        /// planet base only octaves 2-4 (350 / 175 / 87.5 m) remain as detail.</summary>
        public static WorldSpec Spec(ulong regionSeed)
        {
            var s = new WorldSpec { Seed = (long)regionSeed, VcStallOuter = VcStallOuter };
            s.Terrain = new TerrainSpec { ReliefM = 60, BaseWavelengthM = 1400, Octaves = 5 };
            return s;
        }

        public static TerrainModel Model(SectorPlanetInput input) => new TerrainModel(Spec(input.RegionSeed), input.Base);

        /// <summary>Fills o with the adapted 65 x 65 vertex grid of input's sector. Single thread, no allocation besides one HeightSampler.</summary>
        public static void Build(SectorPlanetInput input, TerrainModel model, AdaptedSector o)
        {
            var sec = input.Sector; int face = sec.Id.Face, i0 = sec.Id.Col * QuadSector.Sub, j0 = sec.Id.Row * QuadSector.Sub;
            double r = CubeSphere.Radius;
            const int V = AdaptedSector.Verts;
            var u = new double[V]; var v = new double[V];
            for (int k = 0; k < V; k++) { u[k] = Lattice.Coord(i0 + k); v[k] = Lattice.Coord(j0 + k); }

            // the four quad sides as straight lines in the local plane (unit normal n, offset c: distance = |n.p - c|)
            var cx = new double[4]; var cz = new double[4];
            int[] ci = { 0, V - 1, V - 1, 0 }, cj = { 0, 0, V - 1, V - 1 };
            for (int k = 0; k < 4; k++) input.ToLocal(Lattice.Direction(face, i0 + ci[k], j0 + cj[k]), out cx[k], out cz[k]);
            var nx = new double[4]; var nz = new double[4]; var nc = new double[4];
            for (int k = 0; k < 4; k++)
            {
                int m = (k + 1) & 3; double ex = cx[m] - cx[k], ez = cz[m] - cz[k], l = Math.Sqrt(ex * ex + ez * ez);
                nx[k] = -ez / l; nz[k] = ex / l; nc[k] = nx[k] * cx[k] + nz[k] * cz[k];
            }

            var sampler = new PlanetNoise.HeightSampler(input.WorldSeed);
            for (int j = 0, n = 0; j < V; j++)
                for (int i = 0; i < V; i++, n++)
                {
                    int I = i0 + i, J = j0 + j;
                    Vec3 d = Lattice.OnEdge(I, J) ? Lattice.Direction(face, I, J) : Lattice.Compose(face, u[i], v[j]);   // exactly as QuadSector.Build
                    double pb = sampler.Sample(d * r);
                    double x, z; input.ToLocal(d, out x, out z);
                    double dist = double.MaxValue;
                    for (int k = 0; k < 4; k++) dist = Math.Min(dist, Math.Abs(nx[k] * x + nz[k] * z - nc[k]));
                    bool rim = i == 0 || j == 0 || i == V - 1 || j == V - 1;
                    double s = rim ? 0 : Math.Min(1.0, dist / TaperM), t = s * s * (3 - 2 * s);
                    double c = model.Height(x, z) - input.Base(x, z);
                    double h = t == 0 ? pb : pb + t * c;     // on the rim the vertex is the pure planet base, bit for bit
                    Vec3 w = d * (r + h), rel = w - sec.Centre;
                    o.X[n] = x; o.Z[n] = z; o.PlanetBase[n] = pb; o.Contribution[n] = c; o.Taper[n] = t; o.EdgeDistM[n] = rim ? 0 : dist; o.World[n] = w;
                    o.Lx[n] = (float)Vec3.Dot(rel, sec.East); o.Ly[n] = (float)Vec3.Dot(rel, sec.Up); o.Lz[n] = (float)Vec3.Dot(rel, sec.North);
                }
        }
    }
}
