using System;
using System.Collections.Generic;
using System.Text;
using WhatIf.Planet;

namespace WhatIf.Planet.Experiment
{
    /// <summary>Deformation report of the level-15 quad sectors (no threshold, report only; ADR draft section 7 prediction: ~306 m along the axes, ~256 m at the corners, area 1.41:1, one 120 deg corner at each cube vertex).</summary>
    public sealed class Deform
    {
        public string Name; public int Count;
        public double EdgeMin = 1e30, EdgeMax, AngMin = 1e30, AngMax, AreaMin = 1e30, AreaMax, SpMin = 1e30, SpMax, VertexAngMin = 1e30, VertexAngMax = 0;
        public string EdgeMinAt = "", EdgeMaxAt = "", AngMinAt = "", AngMaxAt = "", AreaMinAt = "", AreaMaxAt = "";

        /// <summary>vertexCorner = index 0..3 of the sector corner that is a cube vertex (for the corner sectors), else -1.</summary>
        public void Add(SectorId id, int vertexCorner = -1)
        {
            int face = id.Face, i0 = id.Col * 64, j0 = id.Row * 64; double R = CubeSphere.Radius;
            var u = new double[65]; var v = new double[65];
            for (int k = 0; k <= 64; k++) { u[k] = Lattice.Coord(i0 + k); v[k] = Lattice.Coord(j0 + k); }
            // corners in order (0,0) (64,0) (64,64) (0,64); sides sampled at all 65 vertices for the vertex spacing
            Vec3 c00 = Lattice.Compose(face, u[0], v[0]) * R, c10 = Lattice.Compose(face, u[64], v[0]) * R, c11 = Lattice.Compose(face, u[64], v[64]) * R, c01 = Lattice.Compose(face, u[0], v[64]) * R;
            Vec3[] c = { c00, c10, c11, c01 }; string at = id.ToString();
            for (int k = 0; k < 4; k++)
            {
                double len = (c[(k + 1) % 4] - c[k]).Length;
                if (len < EdgeMin) { EdgeMin = len; EdgeMinAt = at; }
                if (len > EdgeMax) { EdgeMax = len; EdgeMaxAt = at; }
                double ang = Vec3.Angle(c[(k + 1) % 4] - c[k], c[(k + 3) % 4] - c[k]) * 180 / Math.PI;
                if (ang < AngMin) { AngMin = ang; AngMinAt = at; }
                if (ang > AngMax) { AngMax = ang; AngMaxAt = at; }
                if (k == vertexCorner) { VertexAngMin = Math.Min(VertexAngMin, ang); VertexAngMax = Math.Max(VertexAngMax, ang); }
            }
            double area = 0.5 * Vec3.Cross(c10 - c00, c01 - c00).Length + 0.5 * Vec3.Cross(c10 - c11, c01 - c11).Length;
            if (area < AreaMin) { AreaMin = area; AreaMinAt = at; }
            if (area > AreaMax) { AreaMax = area; AreaMaxAt = at; }
            for (int side = 0; side < 4; side++)
            {
                Vec3 prev = default;
                for (int k = 0; k <= 64; k++)
                {
                    Vec3 p = side == 0 ? Lattice.Compose(face, u[k], v[0]) : side == 1 ? Lattice.Compose(face, u[64], v[k]) : side == 2 ? Lattice.Compose(face, u[k], v[64]) : Lattice.Compose(face, u[0], v[k]);
                    p = p * R; if (k > 0) { double d = (p - prev).Length; if (d < SpMin) SpMin = d; if (d > SpMax) SpMax = d; }
                    prev = p;
                }
            }
            Count++;
        }

        public string Row() => $"| {Name} | {Count} | {EdgeMin:F2} ({EdgeMinAt}) / {EdgeMax:F2} ({EdgeMaxAt}) | {AngMin:F3} ({AngMinAt}) / {AngMax:F3} ({AngMaxAt}) | {AreaMax / AreaMin:F4} ({AreaMin:F0} .. {AreaMax:F0} m2) | {SpMin:F3} / {SpMax:F3} |";

        public static string Report()
        {
            const int M = SectorId.PerFaceSide; var all = new List<Deform>();
            Deform Line(string name, Func<int, SectorId> f) { var d = new Deform { Name = name }; for (int k = 0; k < M; k++) d.Add(f(k)); all.Add(d); return d; }
            Line("face 0 diagonal (k, k)", k => new SectorId(0, k, k));
            Line("face 0 edge row (row 0)", k => new SectorId(0, 0, k));
            Line("face 0 middle row (row 2^14)", k => new SectorId(0, M / 2, k));
            Line("face 0 column 2^13 (quarter)", k => new SectorId(0, k, M / 4));
            var corner = new Deform { Name = "24 corner sectors (3 per cube vertex)" };
            for (int sx = -1; sx <= 1; sx += 2) for (int sy = -1; sy <= 1; sy += 2) for (int sz = -1; sz <= 1; sz += 2)
                    {
                        var cv = new Vec3(sx, sy, sz);
                        for (int f = 0; f < 6; f++)
                        {
                            if (Vec3.Dot(CubeSphere.Normal(f), cv) <= 0) continue;
                            bool hiU = Vec3.Dot(CubeSphere.AxisU(f), cv) > 0, hiV = Vec3.Dot(CubeSphere.AxisV(f), cv) > 0;
                            corner.Add(new SectorId(f, hiV ? M - 1 : 0, hiU ? M - 1 : 0), hiU ? (hiV ? 2 : 1) : (hiV ? 3 : 0));
                        }
                    }
            all.Add(corner);
            var sb = new StringBuilder();
            sb.AppendLine("Level-15 sectors, h = 0, sphere R = 6 378 000 m; quad corners joined by chords. Edge = chord of a sector side (m), angle = interior corner angle (deg), area = two flat triangles, spacing = distance of consecutive vertices along a sector side (m).\n");
            sb.AppendLine("| set | sectors | edge length min / max (m) | corner angle min / max (deg) | area ratio max : min | vertex spacing min / max (m) |\n|---|---|---|---|---|---|");
            foreach (var d in all) sb.AppendLine(d.Row());
            sb.AppendLine($"\nInterior angle at the cube vertex itself (24 corner sectors): {corner.VertexAngMin:F4} .. {corner.VertexAngMax:F4} deg (prediction 120).");
            return sb.ToString();
        }
    }
}
