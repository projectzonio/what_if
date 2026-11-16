using System;

namespace WhatIf.Planet
{
    /// <summary>Region address: face 0..5, level L (N = 2^L cells per face side), col along the face u axis, row along v.</summary>
    public readonly struct RegionId : IEquatable<RegionId>
    {
        public readonly int Face, Level, Row, Col;
        public RegionId(int face, int level, int row, int col) { Face = face; Level = level; Row = row; Col = col; }
        public bool Equals(RegionId o) => Face == o.Face && Level == o.Level && Row == o.Row && Col == o.Col;
        public override bool Equals(object obj) => obj is RegionId o && Equals(o);
        public override int GetHashCode() => (((Face * 31 + Level) * 1000003 + Row) * 1000003) + Col;
        public override string ToString() => $"F{Face}.L{Level}.r{Row}.c{Col}";
        /// <summary>Seed stage name for Rng.ForStage-style derivation: "region"+face+level+row+col (WI-ADR-001 §4).</summary>
        public string StageName => "region" + Face + Level + Row + Col;
    }

    /// <summary>
    /// Cube-sphere addressing with the equal-angle warp a = (4/pi) atan(u) (RG-155 §1). Faces (normal; u axis; v axis, u x v = normal):
    /// 0 +X (Y,Z), 1 -X (Z,Y), 2 +Y (Z,X), 3 -Y (X,Z), 4 +Z (X,Y), 5 -Z (Y,X). Face coordinates a,b in [-1,1].
    /// </summary>
    public static class CubeSphere
    {
        public const double Radius = 6378000.0;
        const double FourOverPi = 4.0 / Math.PI;

        static readonly Vec3[] Normals =
        {
            new Vec3(1, 0, 0), new Vec3(-1, 0, 0), new Vec3(0, 1, 0), new Vec3(0, -1, 0), new Vec3(0, 0, 1), new Vec3(0, 0, -1)
        };
        static readonly Vec3[] AxesU =
        {
            new Vec3(0, 1, 0), new Vec3(0, 0, 1), new Vec3(0, 0, 1), new Vec3(1, 0, 0), new Vec3(1, 0, 0), new Vec3(0, 1, 0)
        };
        static readonly Vec3[] AxesV =
        {
            new Vec3(0, 0, 1), new Vec3(0, 1, 0), new Vec3(1, 0, 0), new Vec3(0, 0, 1), new Vec3(0, 1, 0), new Vec3(1, 0, 0)
        };

        public static Vec3 Normal(int face) => Normals[face];
        public static Vec3 AxisU(int face) => AxesU[face];
        public static Vec3 AxisV(int face) => AxesV[face];

        public static double Warp(double u) => FourOverPi * Math.Atan(u);
        public static double Unwarp(double a) => Math.Tan(a * (Math.PI / 4.0));

        /// <summary>Unit direction (any length) -> face and equal-angle coordinates. Ties pick the lowest face index.</summary>
        public static void ToFaceAB(Vec3 d, out int face, out double a, out double b)
        {
            double ax = Math.Abs(d.X), ay = Math.Abs(d.Y), az = Math.Abs(d.Z);
            if (ax >= ay && ax >= az) face = d.X >= 0 ? 0 : 1;
            else if (ay >= az) face = d.Y >= 0 ? 2 : 3;
            else face = d.Z >= 0 ? 4 : 5;
            double w = Vec3.Dot(d, Normals[face]);
            a = Warp(Vec3.Dot(d, AxesU[face]) / w);
            b = Warp(Vec3.Dot(d, AxesV[face]) / w);
        }

        /// <summary>Face coordinates -> unit direction. a,b may exceed 1 slightly (plane extrapolation across the edge).</summary>
        public static Vec3 FromFaceAB(int face, double a, double b) =>
            (Normals[face] + AxesU[face] * Unwarp(a) + AxesV[face] * Unwarp(b)).Normalized();

        public static int CellsPerSide(int level) => 1 << level;

        public static RegionId RegionOf(Vec3 d, int level)
        {
            int face; double a, b; ToFaceAB(d, out face, out a, out b);
            int n = 1 << level;
            int col = Clamp((int)Math.Floor((a + 1.0) * 0.5 * n), n), row = Clamp((int)Math.Floor((b + 1.0) * 0.5 * n), n);
            return new RegionId(face, level, row, col);
        }
        static int Clamp(int i, int n) => i < 0 ? 0 : (i >= n ? n - 1 : i);

        /// <summary>Centre direction of a region (centre of the cell in a,b space).</summary>
        public static Vec3 RegionCentre(RegionId r)
        {
            int n = 1 << r.Level;
            return FromFaceAB(r.Face, -1.0 + (2 * r.Col + 1.0) / n, -1.0 + (2 * r.Row + 1.0) / n);
        }

        /// <summary>Corner direction of a region: cu, cv in {0,1} select the low/high a and b side.</summary>
        public static Vec3 RegionCorner(RegionId r, int cu, int cv)
        {
            int n = 1 << r.Level;
            return FromFaceAB(r.Face, -1.0 + 2.0 * (r.Col + cu) / n, -1.0 + 2.0 * (r.Row + cv) / n);
        }

        /// <summary>
        /// 4-neighbour across a cell side: dir 0 = +col (+u), 1 = -col, 2 = +row (+v), 3 = -row. Across a cube edge the target face, the
        /// 90-degree-multiple rotation and the index mirroring are derived from the face bases (no hand-typed table); the equal-angle warp is the
        /// same function on both faces, so the along-edge index maps by +-1 only.
        /// </summary>
        public static RegionId Neighbour(RegionId r, int dir)
        {
            int n = 1 << r.Level;
            int dc = dir == 0 ? 1 : (dir == 1 ? -1 : 0), dr = dir == 2 ? 1 : (dir == 3 ? -1 : 0);
            int col = r.Col + dc, row = r.Row + dr;
            if (col >= 0 && col < n && row >= 0 && row < n) return new RegionId(r.Face, r.Level, row, col);

            Vec3 w = dir == 0 ? AxesU[r.Face] : dir == 1 ? -AxesU[r.Face] : dir == 2 ? AxesV[r.Face] : -AxesV[r.Face];
            int g = FaceOfNormal(w);
            Vec3 alongF = dir < 2 ? AxesV[r.Face] : AxesU[r.Face];
            int idx = dir < 2 ? r.Row : r.Col;
            Vec3 back = Normals[r.Face];                        // tangent direction on face g that points back to the old face (= old normal)
            bool crossIsU = Math.Abs(Vec3.Dot(AxesU[g], back)) > 0.5;
            double sg = crossIsU ? Vec3.Dot(AxesU[g], back) : Vec3.Dot(AxesV[g], back);
            Vec3 alongG = crossIsU ? AxesV[g] : AxesU[g];
            int idxG = Vec3.Dot(alongF, alongG) > 0 ? idx : n - 1 - idx;
            int cross = sg > 0 ? n - 1 : 0;
            return crossIsU ? new RegionId(g, r.Level, idxG, cross) : new RegionId(g, r.Level, cross, idxG);
        }

        public static int FaceOfNormal(Vec3 w)
        {
            for (int f = 0; f < 6; f++) if (Vec3.Dot(Normals[f], w) > 0.5) return f;
            throw new InvalidOperationException("no face for " + w);
        }

        /// <summary>Tangent axes at a direction on a face: east = face u axis projected into the tangent plane, north = up x east (east x north = up).</summary>
        public static void TangentAxes(int face, Vec3 up, out Vec3 east, out Vec3 north)
        {
            Vec3 u = AxesU[face];
            east = (u - up * Vec3.Dot(u, up)).Normalized();
            north = Vec3.Cross(up, east);
        }
    }
}
