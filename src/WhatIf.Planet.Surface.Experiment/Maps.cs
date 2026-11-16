using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using WhatIf.Planet;
using WhatIf.Planet.Surface;

namespace WhatIf.Planet.Surface.Experiment
{
    /// <summary>Raster of surface samples (height, kind, forest, biome) with a pixel size in metres (nominal, for hillshade and lengths).</summary>
    public sealed class Raster
    {
        public readonly int W, H;
        public readonly float[] Height, Forest; public readonly byte[] Kind, Biome;
        public Raster(int w, int h) { W = w; H = h; Height = new float[w * h]; Forest = new float[w * h]; Kind = new byte[w * h]; Biome = new byte[w * h]; }
        public void Set(int n, in SurfaceSample s) { Height[n] = (float)s.HeightM; Kind[n] = (byte)s.Kind; Forest[n] = s.Forest; Biome[n] = s.Biome; }
    }

    public static class Maps
    {
        /// <summary>Equirectangular planet map (lon -pi..pi along x, lat +pi/2 at the top row), pole axis +Y. One PlanetSurface per thread; the result does not depend on the thread count.</summary>
        public static Raster Planet(ulong seed, int w, int h, bool parallel)
        {
            var r = new Raster(w, h);
            Action<int, PlanetSurface> row = (y, surf) =>
            {
                double lat = Math.PI * (0.5 - (y + 0.5) / h), cl = Math.Cos(lat), sl = Math.Sin(lat); SurfaceSample s;
                for (int x = 0; x < w; x++)
                {
                    double lon = 2 * Math.PI * ((x + 0.5) / w - 0.5);
                    surf.Sample(new Vec3(cl * Math.Cos(lon), sl, cl * Math.Sin(lon)), out s); r.Set(y * w + x, s);
                }
            };
            if (parallel) Parallel.For(0, h, () => new PlanetSurface(seed), (y, st, surf) => { row(y, surf); return surf; }, surf => { });
            else { var surf = new PlanetSurface(seed); for (int y = 0; y < h; y++) row(y, surf); }
            return r;
        }

        public static Raster CubeFace(PlanetSurface surf, int face, int n)
        {
            var r = new Raster(n, n); SurfaceSample s;
            for (int j = 0; j < n; j++) for (int i = 0; i < n; i++) { surf.Sample(CubeSphere.FromFaceAB(face, -1 + 2.0 * (i + 0.5) / n, -1 + 2.0 * (j + 0.5) / n), out s); r.Set(j * n + i, s); }
            return r;
        }

        /// <summary>Square crop in the tangent plane at centre: n x n pixels of step metres, east to the right, north up.</summary>
        public static Raster Tangent(PlanetSurface surf, Vec3 centre, int n, double step)
        {
            int face; double a, b; CubeSphere.ToFaceAB(centre, out face, out a, out b);
            Vec3 east, north; CubeSphere.TangentAxes(face, centre, out east, out north);
            var r = new Raster(n, n); SurfaceSample s; Vec3 c = centre * CubeSphere.Radius;
            for (int j = 0; j < n; j++) for (int i = 0; i < n; i++)
                {
                    double ex = (i + 0.5 - n * 0.5) * step, nz = (n * 0.5 - (j + 0.5)) * step;
                    surf.Sample((c + east * ex + north * nz).Normalized(), out s); r.Set(j * n + i, s);
                }
            return r;
        }

        // ---------- colours + PNG ----------
        public static byte[] Rgb(Raster r, double pixelM, double exag)
        {
            var px = new byte[r.W * r.H * 3];
            for (int y = 0; y < r.H; y++)
                for (int x = 0; x < r.W; x++)
                {
                    int n = y * r.W + x; double R, G, B; Colour((SurfaceKind)r.Kind[n], r.Biome[n], r.Forest[n], r.Height[n], out R, out G, out B);
                    int xl = Math.Max(x - 1, 0), xr = Math.Min(x + 1, r.W - 1), yu = Math.Max(y - 1, 0), yd = Math.Min(y + 1, r.H - 1);
                    double dx = (r.Height[y * r.W + xr] - r.Height[y * r.W + xl]) / ((xr - xl) * pixelM), dy = (r.Height[yd * r.W + x] - r.Height[yu * r.W + x]) / ((yd - yu) * pixelM);
                    double sh = (r.Kind[n] >= 2) ? 1.0 + Math.Max(-0.45, Math.Min(0.45, exag * (dx - dy) * 0.7071)) : 1.0;    // light from the north-west; water is not shaded
                    px[n * 3] = (byte)Math.Max(0, Math.Min(255, R * sh * 255)); px[n * 3 + 1] = (byte)Math.Max(0, Math.Min(255, G * sh * 255)); px[n * 3 + 2] = (byte)Math.Max(0, Math.Min(255, B * sh * 255));
                }
            return px;
        }

        static void Colour(SurfaceKind k, byte biome, float forest, float h, out double R, out double G, out double B)
        {
            switch (k)
            {
                case SurfaceKind.Ocean: { double t = Math.Max(0, Math.Min(1, (-h - 200) / 3800)); R = 0.10 - 0.08 * t; G = 0.27 - 0.20 * t; B = 0.55 - 0.30 * t; return; }
                case SurfaceKind.Sea: { double t = Math.Max(0, Math.Min(1, -h / 200)); R = 0.30 - 0.20 * t; G = 0.60 - 0.33 * t; B = 0.78 - 0.23 * t; return; }
                case SurfaceKind.Lake: R = 0.18; G = 0.45; B = 0.75; return;
                case SurfaceKind.Beach: R = 0.85; G = 0.80; B = 0.55; return;
            }
            switch (biome)
            {
                case Biome.Ice: R = 0.93; G = 0.95; B = 0.98; break;
                case Biome.Tundra: R = 0.62; G = 0.64; B = 0.56; break;
                case Biome.Desert: R = 0.80; G = 0.68; B = 0.42; break;
                case Biome.Grassland: R = 0.62; G = 0.72; B = 0.35; break;
                default: R = 0.45; G = 0.62; B = 0.30; break;
            }
            if (h > 1500) { double t = Math.Min(1, (h - 1500) / 800.0); R += (0.78 - R) * t; G += (0.76 - G) * t; B += (0.74 - B) * t; }
            double f = forest; R += (0.07 - R) * f * 0.8; G += (0.28 - G) * f * 0.8; B += (0.09 - B) * f * 0.8;
        }

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
