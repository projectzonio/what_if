using System;
using System.Collections.Generic;

namespace WhatIf.Planet.FarLayer
{
    /// <summary>Packed (face, level, row, col) -> long. level <= 15 so row/col fit 16 bits.</summary>
    public static class NodeKey
    {
        public static long Pack(int face, int level, int row, int col) => ((long)face << 40) | ((long)level << 32) | ((long)row << 16) | (long)col;
    }

    /// <summary>
    /// One CDLOD node: a cell (face, level, row, col) of the per-face equal-angle quadtree with a 17 x 17 vertex grid (16 x 16 quads) on the lattice
    /// (spacing 2^(21 - level - 4) lattice steps, an integer for level <= 17). Vertex (i, j) = lattice (I0 + i * Step, J0 + j * Step) -> direction through
    /// Lattice.Direction (canonical on cube edges), so shared edge vertices of neighbouring nodes (also across faces) have identical bits.
    /// Bounds: the shell cap (cone of half angle alpha around CentreDir, radii Rlo..Rhi). Height range = own after the build, else the parent's (+ Margin).
    /// </summary>
    public sealed class FarNode
    {
        public const int Grid = 16, Verts = 17, Margin = 40;
        public readonly long Key; public readonly int Face, Level, Row, Col, Step, I0, J0;
        public readonly Vec3 CentreDir;
        public readonly double CosA, SinA;                 // cap half angle (corners, padded)
        public double HMin, HMax;                          // metres, vertex range (own once built)
        public double Rlo, Rhi;                            // shell radii incl. Margin
        public double CosHz, SinHz;                        // horizon term: acos(R / max(Rhi, R))
        public Vec3 Origin; public double SphereR;         // bounding sphere (frustum test, float-mesh origin)
        public double[] H;                                 // 17 x 17 heights (m), row-major (j, i); null until built
        internal int Stamp, ReqStamp; internal LinkedListNode<FarNode> Lru;
        public bool Built => H != null;

        public FarNode(int face, int level, int row, int col, double hmin, double hmax)
        {
            Face = face; Level = level; Row = row; Col = col; Key = NodeKey.Pack(face, level, row, col);
            int span = 1 << (Lattice_N_Log2 - level); Step = span / Grid; I0 = col * span; J0 = row * span;
            CentreDir = Lattice.Compose(face, Lattice.Coord(I0 + span / 2), Lattice.Coord(J0 + span / 2));
            double a = 0;
            for (int k = 0; k < 4; k++) a = Math.Max(a, Vec3.Angle(CentreDir, Lattice.Direction(face, I0 + (k & 1) * span, J0 + (k >> 1) * span)));
            a = a * (1 + 1e-9) + 1e-9; CosA = Math.Cos(a); SinA = Math.Sin(a);
            SetRange(hmin, hmax);
        }
        const int Lattice_N_Log2 = 21;

        public void SetRange(double hmin, double hmax)
        {
            HMin = hmin; HMax = hmax; double R = CubeSphere.Radius;
            Rlo = R + hmin - Margin; Rhi = R + hmax + Margin;
            CosHz = R / Math.Max(Rhi, R); SinHz = Math.Sqrt(Math.Max(0, 1 - CosHz * CosHz));
            double rm = 0.5 * (Rlo + Rhi); Origin = CentreDir * rm;
            SphereR = Math.Max(Math.Sqrt(Rlo * Rlo + rm * rm - 2 * Rlo * rm * CosA), Math.Sqrt(Rhi * Rhi + rm * rm - 2 * Rhi * rm * CosA));
        }

        /// <summary>Unit direction of vertex (i, j) 0..16.</summary>
        public Vec3 Dir(int i, int j)
        {
            int I = I0 + i * Step, J = J0 + j * Step;
            return Lattice.OnEdge(I, J) ? Lattice.Direction(Face, I, J) : Lattice.Compose(Face, Lattice.Coord(I), Lattice.Coord(J));
        }
        /// <summary>Planet-fixed position (double m) of vertex (i, j) before morphing.</summary>
        public Vec3 Position(int i, int j) => Dir(i, j) * (CubeSphere.Radius + H[j * Verts + i]);

        public override string ToString() => $"F{Face}.L{Level}.r{Row}.c{Col}";
    }

    /// <summary>Selected node + its hole mask (level-13 nodes: bit (qy / 4) * 4 + qx / 4 set = that sector's 4 x 4 quads are not drawn).</summary>
    public struct SelectedNode { public FarNode Node; public ushort Mask; }

    public struct BuildRequest { public FarNode Node; public double Dist; }

    /// <summary>Camera in the planet-fixed frame (double metres). Ground = the ground point under it (near set centre); Fwd = unit horizontal view direction (frustum only).</summary>
    public struct FarCamera { public Vec3 Pos, Ground, Fwd; }

    /// <summary>Result of one Select call; lists are reused between frames.</summary>
    public sealed class FrameSelection
    {
        public readonly List<SelectedNode> Nodes = new List<SelectedNode>();
        public readonly List<BuildRequest> Requests = new List<BuildRequest>();
        public int Visited, FarTriangles, SkirtTriangles, Fallbacks, IncompleteRoots, MaskedQuads;
        public int Triangles => FarTriangles + SkirtTriangles;
        public void Clear() { Nodes.Clear(); Requests.Clear(); Visited = FarTriangles = SkirtTriangles = Fallbacks = IncompleteRoots = MaskedQuads = 0; }
    }

    public sealed class FarParams
    {
        public double Range13 = 1950.0;        // LOD range of the finest level (m); range[l] = Range13 * 2^(13 - l)
        public double MorphFractionSetting = 0.0;   // 0 = automatic (the largest value that keeps T-junctions closed, see MorphFraction); else a fixed fraction of the node's range
        /// <summary>
        /// Morph zone = last fraction of a node's range. A fine edge vertex next to a coarser leaf sits at distance up to LodRange(l) + diagonal of the level-l node from the camera;
        /// the coarse node's own morph zone must not have started there (else its odd edge vertices have already moved off the fine node's even vertices: a crack).
        /// Start of the coarse zone = (1 - f) * 2 * LodRange(l), so f <= 0.5 - 0.725 * nodeSide / LodRange(l) (diagonal 1.45 x side; level-13 side 1250 m at the face centre).
        /// </summary>
        public double MorphFraction => MorphFractionSetting > 0 ? MorphFractionSetting : Math.Max(0.02, Math.Min(0.3, 0.5 - 0.725 * 1250.0 / Range13 - 0.01));
        public int MaxLevel = 13;
        public int PinLevel = 3;               // levels 0..PinLevel are prebuilt and never evicted
        public int MaxNodes = 12000;           // cache bound (info + built nodes)
        public double HorizonSlack = 1e-9;
        public double PrefetchRad = 0.01;     // nodes up to this angle (~13 km) beyond the horizon are only requested (built ahead), not drawn
        public double LodRange(int level) => Range13 * (double)(1L << (MaxLevel - level));
    }
}
