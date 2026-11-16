using System;
using WhatIf.Planet;

namespace WhatIf.Planet.Surface
{
    /// <summary>Batch sampler of one level-15 sector: 65 x 65 vertices at QuadSector.Direction(i, j) (canonical on cube edges), row-major (j, i). The hot path of the HW budget row.</summary>
    public static class SurfaceSector
    {
        public const int Verts = QuadSector.Verts;

        /// <summary>Fills height (float m), kind, forest (and biome when not null); arrays have Verts * Verts entries. Equal to PlanetSurface.Sample at the same direction (bits of the double cast to float).</summary>
        public static void Sample(PlanetSurface surface, SectorId id, float[] height, byte[] kind, float[] forest, byte[] biome = null)
        {
            int face = id.Face, i0 = id.Col * QuadSector.Sub, j0 = id.Row * QuadSector.Sub;
            var u = new double[Verts]; var v = new double[Verts];
            for (int k = 0; k < Verts; k++) { u[k] = Lattice.Coord(i0 + k); v[k] = Lattice.Coord(j0 + k); }
            int n = 0; SurfaceSample s;
            for (int j = 0; j < Verts; j++)
                for (int i = 0; i < Verts; i++, n++)
                {
                    int I = i0 + i, J = j0 + j;
                    Vec3 d = Lattice.OnEdge(I, J) ? Lattice.Direction(face, I, J) : Lattice.Compose(face, u[i], v[j]);
                    surface.Sample(d, out s);
                    height[n] = (float)s.HeightM; kind[n] = (byte)s.Kind; forest[n] = s.Forest;
                    if (biome != null) biome[n] = s.Biome;
                }
        }
    }
}
