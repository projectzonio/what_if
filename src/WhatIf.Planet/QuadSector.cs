using System;

namespace WhatIf.Planet
{
    /// <summary>
    /// Sector address (WI-ADR-001 row 3, revised in ADR draft section 7): a leaf cell of the per-face equal-angle quadtree at level 15
    /// (row, col in [0, 2^15)); its region is the level-10 ancestor (32 x 32 sectors). Deformed quads are accepted.
    /// </summary>
    public readonly struct SectorId : IEquatable<SectorId>
    {
        public const int Level = 15, RegionLevel = 10, PerRegionSide = 32, PerFaceSide = 1 << Level;
        public readonly int Face, Row, Col;
        public SectorId(int face, int row, int col) { Face = face; Row = row; Col = col; }
        public RegionId Region => new RegionId(Face, RegionLevel, Row >> 5, Col >> 5);
        /// <summary>The same cell as a RegionId at level 15 (for the neighbour table).</summary>
        public RegionId AsCell => new RegionId(Face, Level, Row, Col);
        /// <summary>4-neighbour: 0 = +col, 1 = -col, 2 = +row, 3 = -row; across a cube edge through the neighbour table.</summary>
        public SectorId Neighbour(int dir) { RegionId r = CubeSphere.Neighbour(AsCell, dir); return new SectorId(r.Face, r.Row, r.Col); }
        public static SectorId At(Vec3 d) { RegionId r = CubeSphere.RegionOf(d, Level); return new SectorId(r.Face, r.Row, r.Col); }
        public bool Equals(SectorId o) => Face == o.Face && Row == o.Row && Col == o.Col;
        public override bool Equals(object obj) => obj is SectorId o && Equals(o);
        public override int GetHashCode() => (Face * 1000003 + Row) * 1000003 + Col;
        public override string ToString() => $"F{Face}.r{Row}.c{Col}";
    }

    /// <summary>
    /// Face-wide integer vertex lattice: (face, I, J) with I, J in [0, 2^21] (64 intervals per level-15 sector). a = I / 2^20 - 1 is exact in double,
    /// u = tan(pi/4 a), direction = normalize(N + u U + v V). A lattice point on a cube edge or corner belongs to 2 or 3 faces; it is evaluated on the
    /// LOWEST face index of its equivalents (tan(pi/4) is not exactly 1 in double, so the faces would otherwise disagree by ~1e-16): any two sectors
    /// that share a vertex therefore get identical double bits.
    /// </summary>
    public static class Lattice
    {
        public const int N = 1 << 21;
        const double Half = 1048576.0;   // 2^20

        public static double Coord(int i) => Math.Tan((Math.PI / 4.0) * (i / Half - 1.0));
        public static bool OnEdge(int i, int j) => i == 0 || i == N || j == 0 || j == N;

        /// <summary>The one expression every vertex goes through, so equal (face, u, v) give equal bits.</summary>
        public static Vec3 Compose(int face, double u, double v) =>
            (CubeSphere.Normal(face) + CubeSphere.AxisU(face) * u + CubeSphere.AxisV(face) * v).Normalized();

        public static Vec3 Direction(int face, int i, int j)
        {
            int f, ci, cj; Canonical(face, i, j, out f, out ci, out cj);
            return Compose(f, Coord(ci), Coord(cj));
        }

        /// <summary>Lattice point -> its representation on the lowest face that contains it (itself when it is interior).</summary>
        public static void Canonical(int face, int i, int j, out int cf, out int ci, out int cj)
        {
            cf = face; ci = i; cj = j;
            if (!OnEdge(i, j)) return;
            var fs = new int[3]; var xs = new int[3]; var ys = new int[3]; int n = 0;
            fs[0] = face; xs[0] = i; ys[0] = j; n = 1;
            for (int k = 0; k < n; k++)
                for (int e = 0; e < 4; e++)
                {
                    if (!(e == 0 ? xs[k] == 0 : e == 1 ? xs[k] == N : e == 2 ? ys[k] == 0 : ys[k] == N)) continue;
                    int g, gi, gj; Hop(fs[k], xs[k], ys[k], e, out g, out gi, out gj);
                    bool seen = false; for (int m = 0; m < n; m++) if (fs[m] == g) seen = true;
                    if (!seen && n < 3) { fs[n] = g; xs[n] = gi; ys[n] = gj; n++; }
                }
            int best = 0; for (int m = 1; m < n; m++) if (fs[m] < fs[best]) best = m;
            cf = fs[best]; ci = xs[best]; cj = ys[best];
        }

        // The same lattice point on the face across edge e (0 = -u, 1 = +u, 2 = -v, 3 = +v). Integers only: the warp is the same function on both
        // faces, so the coordinate along the edge maps by identity or mirror (N - x), the coordinate across becomes 0 or N.
        static void Hop(int f, int i, int j, int e, out int g, out int gi, out int gj)
        {
            Vec3 w = e == 0 ? -CubeSphere.AxisU(f) : e == 1 ? CubeSphere.AxisU(f) : e == 2 ? -CubeSphere.AxisV(f) : CubeSphere.AxisV(f);
            g = CubeSphere.FaceOfNormal(w);
            Vec3 alongF = e < 2 ? CubeSphere.AxisV(f) : CubeSphere.AxisU(f);
            int idx = e < 2 ? j : i;
            Vec3 back = CubeSphere.Normal(f);
            bool crossIsU = Math.Abs(Vec3.Dot(CubeSphere.AxisU(g), back)) > 0.5;
            double sg = crossIsU ? Vec3.Dot(CubeSphere.AxisU(g), back) : Vec3.Dot(CubeSphere.AxisV(g), back);
            Vec3 alongG = crossIsU ? CubeSphere.AxisV(g) : CubeSphere.AxisU(g);
            int idxG = Vec3.Dot(alongF, alongG) > 0 ? idx : N - idx;
            int cross = sg > 0 ? N : 0;
            if (crossIsU) { gi = cross; gj = idxG; } else { gi = idxG; gj = cross; }
        }
    }

    /// <summary>
    /// One level-15 sector: its own tangent frame (origin = centre direction * R, up = radial, east = face u axis projected, north = up x east) and the
    /// 65 x 65 vertex grid taken from the face lattice. Mesh data is float, relative to the sector centre in that frame. No neighbour is needed to load it.
    /// </summary>
    public sealed class QuadSector
    {
        public const int Verts = 65, Sub = 64;
        public readonly SectorId Id;
        public readonly Vec3 Up, Centre, East, North;
        readonly int i0, j0;

        public QuadSector(SectorId id)
        {
            Id = id; i0 = id.Col * Sub; j0 = id.Row * Sub;
            Up = Lattice.Compose(id.Face, Lattice.Coord(i0 + Sub / 2), Lattice.Coord(j0 + Sub / 2));
            Centre = Up * CubeSphere.Radius;
            CubeSphere.TangentAxes(id.Face, Up, out East, out North);
        }

        /// <summary>Unit direction of vertex (i, j), 0..64, canonical on cube edges.</summary>
        public Vec3 Direction(int i, int j) => Lattice.Direction(Id.Face, i0 + i, j0 + j);

        /// <summary>World position (planet-fixed double metres) of a float local mesh point of this sector.</summary>
        public Vec3 LocalToWorld(float x, float y, float z) => Centre + East * (double)x + Up * (double)y + North * (double)z;

        /// <summary>
        /// Builds the vertex grid: world = dir * (R + h) with h = PlanetNoise at dir * R (or 0 when terrain is false), then local float position relative to
        /// the centre in this sector's frame. lx/ly/lz have Verts*Verts entries, row-major (j, i). world (optional) receives the double positions.
        /// </summary>
        public void Build(ulong seed, bool terrain, float[] lx, float[] ly, float[] lz, Vec3[] world = null)
        {
            double r = CubeSphere.Radius; int face = Id.Face;
            var u = new double[Verts]; var v = new double[Verts];
            for (int k = 0; k < Verts; k++) { u[k] = Lattice.Coord(i0 + k); v[k] = Lattice.Coord(j0 + k); }   // separable: 65 + 65, not per vertex
            var sampler = terrain ? new PlanetNoise.HeightSampler(seed) : null;
            int n = 0;
            for (int j = 0; j < Verts; j++)
                for (int i = 0; i < Verts; i++, n++)
                {
                    int I = i0 + i, J = j0 + j;
                    Vec3 d = Lattice.OnEdge(I, J) ? Lattice.Direction(face, I, J) : Lattice.Compose(face, u[i], v[j]);
                    double h = terrain ? sampler.Sample(d * r) : 0.0;
                    Vec3 w = d * (r + h), rel = w - Centre;
                    lx[n] = (float)Vec3.Dot(rel, East); ly[n] = (float)Vec3.Dot(rel, Up); lz[n] = (float)Vec3.Dot(rel, North);
                    if (world != null) world[n] = w;
                }
        }
    }
}
