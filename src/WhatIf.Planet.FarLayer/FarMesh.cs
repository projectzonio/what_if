using System;

namespace WhatIf.Planet.FarLayer
{
    /// <summary>Camera points that drive the morph factor: a vertex morphs by the smaller of its distances to the camera and to the ground point (near set).</summary>
    public struct MorphContext { public Vec3 Cam, Ground; public FarParams P; }

    /// <summary>
    /// CPU reference of the node mesh (the vertex shader does the same later). Triangles: every quad (a, b) is split on the diagonal (a, b)-(a + 1, b + 1);
    /// that diagonal passes through the odd-odd vertex, so at morph 1 an odd vertex lies exactly on its parent's edge or diagonal.
    /// </summary>
    public static class FarMesh
    {
        /// <summary>Morph factor of a vertex at planet position p for a node of the given level: 0 up to 70 % of the range, 1 at the range (= the parent level's LOD range).</summary>
        public static double MorphFactor(in MorphContext c, int level, Vec3 p)
        {
            if (level == 0) return 0.0;
            double d = Math.Min((p - c.Cam).Length, (p - c.Ground).Length), end = c.P.LodRange(level - 1), start = end * (1.0 - c.P.MorphFraction);
            return d <= start ? 0.0 : (d >= end ? 1.0 : (d - start) / (end - start));
        }

        /// <summary>The position vertex (i, j) moves to at morph 1: even-even stays; odd-even / even-odd = midpoint of the two even neighbours on the grid line; odd-odd = midpoint of the diagonal (i-1, j-1)-(i+1, j+1).</summary>
        public static Vec3 MorphTarget(FarNode n, int i, int j)
        {
            bool oi = (i & 1) != 0, oj = (j & 1) != 0;
            if (!oi && !oj) return n.Position(i, j);
            if (oi && !oj) return (n.Position(i - 1, j) + n.Position(i + 1, j)) * 0.5;
            if (!oi) return (n.Position(i, j - 1) + n.Position(i, j + 1)) * 0.5;
            return (n.Position(i - 1, j - 1) + n.Position(i + 1, j + 1)) * 0.5;
        }

        /// <summary>Position at an explicit morph factor (0 = own height bits, 1 = the parent mesh).</summary>
        public static Vec3 Morphed(FarNode n, int i, int j, double m)
        {
            Vec3 p = n.Position(i, j); if (m <= 0.0 || ((i | j) & 1) == 0) return p;
            return p + (MorphTarget(n, i, j) - p) * m;
        }

        /// <summary>Position with the morph factor derived from the camera and ground distances.</summary>
        public static Vec3 Morphed(FarNode n, int i, int j, in MorphContext c)
        {
            Vec3 p = n.Position(i, j); if (((i | j) & 1) == 0) return p;
            double m = MorphFactor(c, n.Level, p); return m <= 0.0 ? p : p + (MorphTarget(n, i, j) - p) * m;
        }

        /// <summary>Upload buffers for the viewer: 17 x 17 float xyz relative to n.Origin (double) and the float delta to the morph target (the shader does lerp(pos, pos + delta, morph)). Both arrays have 867 floats.</summary>
        public static void FillBuffers(FarNode n, float[] pos, float[] delta)
        {
            int q = 0;
            for (int j = 0; j < FarNode.Verts; j++)
                for (int i = 0; i < FarNode.Verts; i++, q += 3)
                {
                    Vec3 p = n.Position(i, j) - n.Origin, t = MorphTarget(n, i, j) - n.Origin;
                    pos[q] = (float)p.X; pos[q + 1] = (float)p.Y; pos[q + 2] = (float)p.Z;
                    delta[q] = (float)(t.X - p.X); delta[q + 1] = (float)(t.Y - p.Y); delta[q + 2] = (float)(t.Z - p.Z);
                }
        }

        /// <summary>
        /// Radius (m from the planet centre) of the far mesh along the unit direction d: the ray hits the plane of the triangle that contains d (lattice position on the node's face),
        /// vertices morphed with c. False when d is not inside the node (more than 1e-6 of a quad outside).
        /// </summary>
        public static bool RadiusAt(FarNode n, Vec3 d, in MorphContext c, out double radius)
        {
            radius = 0; double w = Vec3.Dot(d, CubeSphere.Normal(n.Face)); if (w <= 0) return false;
            double a = CubeSphere.Warp(Vec3.Dot(d, CubeSphere.AxisU(n.Face)) / w), b = CubeSphere.Warp(Vec3.Dot(d, CubeSphere.AxisV(n.Face)) / w);
            double qx = ((a + 1.0) * 1048576.0 - n.I0) / n.Step, qy = ((b + 1.0) * 1048576.0 - n.J0) / n.Step;
            if (qx < -1e-6 || qy < -1e-6 || qx > FarNode.Grid + 1e-6 || qy > FarNode.Grid + 1e-6) return false;
            int ix = Math.Min(FarNode.Grid - 1, Math.Max(0, (int)Math.Floor(qx))), iy = Math.Min(FarNode.Grid - 1, Math.Max(0, (int)Math.Floor(qy)));
            double fx = qx - ix, fy = qy - iy;
            Vec3 p0 = Morphed(n, ix, iy, c), p1, p2;
            if (fx >= fy) { p1 = Morphed(n, ix + 1, iy, c); p2 = Morphed(n, ix + 1, iy + 1, c); } else { p1 = Morphed(n, ix + 1, iy + 1, c); p2 = Morphed(n, ix, iy + 1, c); }
            Vec3 nrm = Vec3.Cross(p1 - p0, p2 - p0); double den = Vec3.Dot(nrm, d); if (Math.Abs(den) < 1e-12) return false;
            radius = Vec3.Dot(nrm, p0) / den; return true;
        }

        /// <summary>Skirt depth rule: measured gap x 1.25 rounded up to 0.5 m.</summary>
        public static double SkirtDepth(double maxGapM) => Math.Ceiling(maxGapM * 1.25 * 2.0) / 2.0;
    }
}
