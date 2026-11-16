using System;
using System.Collections.Generic;
using System.Linq;
using WhatIf.Planet;
using WhatIf.Planet.Anchors;
using WhatIf.Planet.Experiment;
using WhatIf.Planet.Surface;
using WhatIf.Planet.WorldGen;

namespace WhatIf.Planet.Anchors.Experiment
{
    /// <summary>Top view of one area: B (anchors) on the surface background, the centre region's chart, north up, 600 x 600 px at 120 m each.</summary>
    public static class Draw
    {
        const int N = 600, Gap = 12; const double Step = 120;

        public static void Image(string path, Area a, Net nb, AnchorSet set, PlanetSurface surf)
        {
            int W = N; var img = new byte[W * N * 3]; var ch = new RegionChart(a.Centre);
            for (int i = 0; i < img.Length; i++) img[i] = 255;
            for (int py = 0; py < N; py++)
                for (int px = 0; px < N; px++)
                {
                    var sm = surf.Sample(ch.ToDir((px - N / 2.0) * Step, (N / 2.0 - py) * Step)); byte r, g, b;
                    switch (sm.Kind)
                    {
                        case SurfaceKind.Ocean: r = 10; g = 30; b = 90; break;
                        case SurfaceKind.Sea: r = 30; g = 80; b = 150; break;
                        case SurfaceKind.Lake: r = 70; g = 160; b = 210; break;
                        case SurfaceKind.Beach: r = 225; g = 210; b = 150; break;
                        default: double f = sm.Forest; r = (byte)(165 - 120 * f); g = (byte)(175 - 60 * f); b = (byte)(95 - 50 * f); break;
                    }
                    { int o = (py * W + px) * 3; img[o] = r; img[o + 1] = g; img[o + 2] = b; }
                }
            bool Px(Vec3 d, int side, out int x, out int y)
            {
                ch.ToChart(d, out double cx, out double cz);
                x = (int)Math.Round(cx / Step + N / 2.0); y = (int)Math.Round(N / 2.0 - cz / Step);
                bool ok = x >= 0 && y >= 0 && x < N && y < N; x += side * (N + Gap); return ok;
            }
            void Dot(int x, int y, byte r, byte g, byte b, int t) { for (int dy = 0; dy < t; dy++) for (int dx = 0; dx < t; dx++) { int xx = x + dx - t / 2, yy = y + dy - t / 2; if (xx < 0 || yy < 0 || xx >= W || yy >= N) continue; int o = (yy * W + xx) * 3; img[o] = r; img[o + 1] = g; img[o + 2] = b; } }
            void Line(Vec3 p, Vec3 q, int side, byte r, byte g, byte b, int t, int every = 1)
            {
                bool okp = Px(p, side, out int x0, out int y0), okq = Px(q, side, out int x1, out int y1);
                if (!okp && !okq) return;
                int m = Math.Max(1, Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0)));
                for (int k = 0; k <= m; k += every)
                {
                    int x = x0 + (x1 - x0) * k / m, y = y0 + (y1 - y0) * k / m, lx = x - side * (N + Gap);
                    if (lx >= 0 && lx < N && y >= 0 && y < N) Dot(x, y, r, g, b, t);
                }
            }
            void Road(List<Vec3> line, string cls, int side)
            {
                byte r = 255, g = 255, b = 255; int t = 1;
                if (cls == WhatIf.WorldGen.RoadClass.Motorway) { r = 220; g = 30; b = 30; t = 3; }
                else if (cls == WhatIf.WorldGen.RoadClass.Primary || cls == WhatIf.WorldGen.RoadClass.Link) { r = 255; g = 140; b = 0; t = 2; }
                else if (cls == WhatIf.WorldGen.RoadClass.Secondary) { r = 255; g = 230; b = 60; t = 2; }
                for (int i = 0; i + 1 < line.Count; i += Math.Max(1, line.Count / 400)) Line(line[i], line[Math.Min(line.Count - 1, i + Math.Max(1, line.Count / 400))], side, r, g, b, t);
            }
            foreach (var id in a.Regions)
            {
                var w = new RegionChart(id);
                for (int k = 0; k < 4; k++) { int m = (k + 1) & 3; Line(w.ToDir(w.CornerX[k], w.CornerZ[k]), w.ToDir(w.CornerX[m], w.CornerZ[m]), 0, 20, 20, 20, 1); }
            }
            foreach (var (line, cls) in nb.Draw) Road(line, cls, 0);
            foreach (var c in set.Corridors.Where(x => !x.Routed)) Line(c.Link.A.Dir, c.Link.B.Dir, 0, 120, 120, 120, 2, 6);
            foreach (var s in set.Settlements) if (Px(s.Dir, 0, out int x, out int y)) Dot(x, y, 230, 0, 230, s.Class == SettlementClass.City ? 9 : s.Class == SettlementClass.Town ? 7 : 5);
            Png.Write(path, W, N, img);
        }
    }
}
