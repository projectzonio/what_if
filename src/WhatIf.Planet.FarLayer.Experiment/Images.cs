using System;
using System.IO;
using System.IO.Compression;
using WhatIf.Planet;

namespace WhatIf.Planet.FarLayer.Experiment
{
    /// <summary>Selection images: node level colours, hole black, skirt edges red, node borders dark. PNG writer copied from the RG-168 experiment (not referenced).</summary>
    public static class Images
    {
        static void Colour(int level, out byte r, out byte g, out byte b)
        {
            double h = (level * 47 % 360) / 60.0, v = 0.55 + 0.4 * (level % 3) / 2.0; int i = (int)h; double f = h - i, p = 0.15 * v, q = v * (1 - 0.85 * f), t = v * (1 - 0.85 * (1 - f)), R, G, B;
            switch (i % 6) { case 0: R = v; G = t; B = p; break; case 1: R = q; G = v; B = p; break; case 2: R = p; G = v; B = t; break; case 3: R = p; G = q; B = v; break; case 4: R = t; G = p; B = v; break; default: R = v; G = p; B = q; break; }
            r = (byte)(R * 255); g = (byte)(G * 255); b = (byte)(B * 255);
        }

        /// <summary>Which selected node covers (face, a, b)? Returns its level, -1 when none; masked = the pixel lies in a hole quad.</summary>
        static int Cover(CoverageIndex idx, int face, double a, double b, out bool masked, out int row13, out int col13)
        {
            masked = false; row13 = col13 = 0;
            for (int l = 0; l <= 13; l++)
            {
                int n = 1 << l, col = Math.Min(n - 1, Math.Max(0, (int)Math.Floor((a + 1.0) * 0.5 * n))), row = Math.Min(n - 1, Math.Max(0, (int)Math.Floor((b + 1.0) * 0.5 * n))); ushort m;
                if (!idx.TryGet(NodeKey.Pack(face, l, row, col), out m)) continue;
                if (l == 13) { int ns = 1 << 15, sc = Math.Min(ns - 1, (int)Math.Floor((a + 1.0) * 0.5 * ns)), sr = Math.Min(ns - 1, (int)Math.Floor((b + 1.0) * 0.5 * ns)); masked = ((m >> (((sr & 3) << 2) | (sc & 3))) & 1) != 0; }
                row13 = row; col13 = col; return l;
            }
            return -1;
        }

        /// <summary>Top view on the camera's face: px x px pixels, +-halfWidthM around the camera (lattice scale 4.78 m per step at the face centre).</summary>
        public static void TopView(string path, CoverageIndex idx, NearSet near, FarCamera cam, double halfWidthM, int px)
        {
            Vec3 cd = cam.Pos.Normalized(); int face; double a0, b0; CubeSphere.ToFaceAB(cd, out face, out a0, out b0);
            double stepsPerPx = 2 * halfWidthM / (px * 4.78), aPerPx = stepsPerPx / 1048576.0; var rgb = new byte[px * px * 3];
            for (int y = 0; y < px; y++)
                for (int x = 0; x < px; x++)
                {
                    double a = a0 + (x - px / 2) * aPerPx, b = b0 - (y - px / 2) * aPerPx; byte r = 40, g = 40, bl = 40;
                    if (a > -1 && a < 1 && b > -1 && b < 1)
                    {
                        bool masked; int r13, c13; int l = Cover(idx, face, a, b, out masked, out r13, out c13);
                        if (l >= 0)
                        {
                            Colour(l, out r, out g, out bl);
                            double span = 2.0 / (1 << l); double fa = ((a + 1.0) % span) / span, fb = ((b + 1.0) % span) / span, eps = aPerPx / span * 1.2;
                            if (fa < eps || fa > 1 - eps || fb < eps || fb > 1 - eps) { r = (byte)(r / 3); g = (byte)(g / 3); bl = (byte)(bl / 3); }
                            if (masked) { r = g = bl = 0; }
                        }
                        // skirt: a pixel row/column on the border of a near sector whose neighbour is not near
                        int ns = 1 << 15; double sa = (a + 1.0) * 0.5 * ns, sb = (b + 1.0) * 0.5 * ns; int sc = (int)Math.Floor(sa), sr = (int)Math.Floor(sb); var s = new SectorId(face, sr, sc);
                        if (near.Contains(s))
                        {
                            double e = aPerPx * 0.5 * ns * 1.5; double fx = sa - sc, fy = sb - sr;
                            if ((fx < e && !near.Contains(new SectorId(face, sr, sc - 1))) || (fx > 1 - e && !near.Contains(new SectorId(face, sr, sc + 1))) || (fy < e && !near.Contains(new SectorId(face, sr - 1, sc))) || (fy > 1 - e && !near.Contains(new SectorId(face, sr + 1, sc)))) { r = 255; g = 30; bl = 30; }
                        }
                    }
                    int o = (y * px + x) * 3; rgb[o] = r; rgb[o + 1] = g; rgb[o + 2] = bl;
                }
            WritePng(path, px, px, rgb);
        }

        /// <summary>Equirectangular overview of the node levels (lon -pi..pi, lat +pi/2 on top, pole axis +Y); not covered = dark grey; camera = white cross.</summary>
        public static void Overview(string path, CoverageIndex idx, FarCamera cam, int w, int h)
        {
            var rgb = new byte[w * h * 3]; Vec3 cd = cam.Pos.Normalized();
            for (int y = 0; y < h; y++)
            {
                double lat = Math.PI * (0.5 - (y + 0.5) / h), cl = Math.Cos(lat), sl = Math.Sin(lat);
                for (int x = 0; x < w; x++)
                {
                    double lon = 2 * Math.PI * ((x + 0.5) / w - 0.5); Vec3 d = new Vec3(cl * Math.Cos(lon), sl, cl * Math.Sin(lon)); int face; double a, b; CubeSphere.ToFaceAB(d, out face, out a, out b);
                    bool masked; int r13, c13; int l = Cover(idx, face, a, b, out masked, out r13, out c13); byte r = 40, g = 40, bl = 40; if (l >= 0) Colour(l, out r, out g, out bl);
                    int o = (y * w + x) * 3; rgb[o] = r; rgb[o + 1] = g; rgb[o + 2] = bl;
                }
            }
            double clat = Math.Asin(cd.Y), clon = Math.Atan2(cd.Z, cd.X); int cx = (int)((clon / (2 * Math.PI) + 0.5) * w), cy = (int)((0.5 - clat / Math.PI) * h);
            for (int k = -6; k <= 6; k++) { Put(rgb, w, h, cx + k, cy); Put(rgb, w, h, cx, cy + k); }
            WritePng(path, w, h, rgb);
        }
        static void Put(byte[] rgb, int w, int h, int x, int y) { if (x < 0 || y < 0 || x >= w || y >= h) return; int o = (y * w + x) * 3; rgb[o] = rgb[o + 1] = rgb[o + 2] = 255; }

        public static void WritePng(string path, int w, int h, byte[] rgb)
        {
            using (var fs = File.Create(path))
            {
                fs.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);
                var ihdr = new byte[13]; Be(ihdr, 0, w); Be(ihdr, 4, h); ihdr[8] = 8; ihdr[9] = 2;
                Chunk(fs, "IHDR", ihdr);
                var raw = new byte[(w * 3 + 1) * h];
                for (int y = 0; y < h; y++) Buffer.BlockCopy(rgb, y * w * 3, raw, y * (w * 3 + 1) + 1, w * 3);
                var ms = new MemoryStream();
                using (var z = new ZLibStream(ms, CompressionLevel.Optimal, true)) z.Write(raw, 0, raw.Length);
                Chunk(fs, "IDAT", ms.ToArray()); Chunk(fs, "IEND", new byte[0]);
            }
        }
        static void Be(byte[] b, int o, int v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }
        static void Chunk(Stream s, string type, byte[] data)
        {
            var len = new byte[4]; Be(len, 0, data.Length); s.Write(len, 0, 4);
            var td = new byte[4 + data.Length]; for (int i = 0; i < 4; i++) td[i] = (byte)type[i]; Buffer.BlockCopy(data, 0, td, 4, data.Length);
            s.Write(td, 0, td.Length);
            uint crc = 0xFFFFFFFF; foreach (byte b in td) { crc ^= b; for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1; }
            crc ^= 0xFFFFFFFF; var c = new byte[4]; Be(c, 0, (int)crc); s.Write(c, 0, 4);
        }
    }
}
