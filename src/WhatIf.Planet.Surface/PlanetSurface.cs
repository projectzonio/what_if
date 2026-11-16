using System;
using WhatIf.Planet;

namespace WhatIf.Planet.Surface
{
    public enum SurfaceKind : byte { Ocean = 0, Sea = 1, Beach = 2, Land = 3, Lake = 4 }

    /// <summary>Biome ids (byte). Water kinds are Water, Beach is Shore; the land ids come from temperature (latitude, elevation) and moisture.</summary>
    public static class Biome
    {
        public const byte Water = 0, Ice = 1, Tundra = 2, Boreal = 3, Temperate = 4, Grassland = 5, Desert = 6, Shore = 7, Tropical = 8;
    }

    public struct SurfaceSample
    {
        /// <summary>Metres above the sphere R; sea level is exactly 0. Lake: the bed height (below WaterLevelM).</summary>
        public double HeightM;
        public SurfaceKind Kind;
        /// <summary>Water surface height: 0 for Ocean/Sea, the lake level for Lake and for the lake bank, PlanetSurface.NoWater elsewhere.</summary>
        public double WaterLevelM;
        /// <summary>Tree density in [0, 1] (continuous in the direction); 0 on water, beach, above the tree line, on steep upland.</summary>
        public float Forest;
        public byte Biome;
    }

    /// <summary>Tunables of the surface (design constants, documented in what-if-planet-surface.md).</summary>
    public static class SurfaceParams
    {
        public const double WarpM = 1.0e6;                 // continent domain warp, metres
        public const double LandShareTarget = 0.32;        // sea level = this quantile of the continent field over 16384 fixed Fibonacci directions (per seed, deterministic)
        public const int CalibrationDirs = 16384;
        public const double CoastRoughness = 0.0008;       // field units; ~ +-0.5 km coast wobble at cells 16..4 km
        public const double CoastDetail = 0.004;          // RG-169: field units of the 4..1 km coast octaves, strong only within CoastBand of the sea threshold
        public const double CoastBandInner = 0.004, CoastBandOuter = 0.010;   // |x| below the inner value: full detail; above the outer one: none
        public const double ShelfDepthM = 200, AbyssDepthM = 4000, ShelfWidth = 0.04, SlopeWidth = 0.12;
        public const double LowlandM = 420, LowlandScale = 0.12;
        public const double MountainM = 2300, MountainStart = 0.22, MountainFull = 0.7;
        public const double DetailRampM = 120;            // continent elevation at which the 150 m detail is fully on
        public const double TreeLineM = 2200, TreeFadeM = 400;
        public const double LakeProb = 0.22;
    }

    /// <summary>
    /// Planet surface = pure deterministic function of (worldSeed, unit direction): height above the sphere (sea level 0), kind, lake level, forest, biome.
    /// Nothing is stored; seams are impossible because every layer is the 3D int64-lattice noise of the planet-fixed position (no face parameterisation).
    /// Pole axis for the climate = the planet's +Y. Not thread safe: the corner caches are per instance (one PlanetSurface per thread).
    /// </summary>
    public sealed class PlanetSurface
    {
        public const double NoWater = -9999.0;
        public const double R = CubeSphere.Radius;
        public readonly ulong Seed;
        /// <summary>Continent field value that maps to sea level for this seed.</summary>
        public readonly double SeaThreshold;
        readonly Terrain core;
        readonly LakeField lakes;

        public PlanetSurface(ulong seed)
        {
            Seed = seed; double thr = Calibrate(seed); core = new Terrain(seed, thr); lakes = new LakeField(seed, new Terrain(seed, thr));
            SeaThreshold = thr;
        }

        /// <summary>Surface kind at dir (no forest / biome): the cheap query for water masks. Exactly the kind of Sample(dir).</summary>
        public SurfaceKind Kind(Vec3 dir) { SurfaceSample s; Eval(dir, false, out s); return s.Kind; }

        /// <summary>Height and kind in one evaluation (no forest / biome).</summary>
        public double HeightKind(Vec3 dir, out SurfaceKind kind) { SurfaceSample s; Eval(dir, false, out s); kind = s.Kind; return s.HeightM; }

        /// <summary>Height above the sphere (m). Exactly the bits of Sample(dir).HeightM.</summary>
        public double Height(Vec3 dir) { SurfaceSample s; Eval(dir, false, out s); return s.HeightM; }

        public SurfaceSample Sample(Vec3 dir) { SurfaceSample s; Eval(dir, true, out s); return s; }

        public void Sample(Vec3 dir, out SurfaceSample s) => Eval(dir, true, out s);

        /// <summary>Lake of the 3D cell (ix, iy, iz) of size 2^13 m, or null (for tools and tests).</summary>
        public Lake LakeOfCell(long ix, long iy, long iz) => lakes.Get(ix, iy, iz);
        public static long CellOf(double metres) => (long)Math.Floor(metres * LakeField.InvCell);
        public int LakeCacheCount => lakes.Count;

        /// <summary>Continent elevation coordinate (0 = coast, > 0 land) at dir; for tools.</summary>
        public double ContinentX(Vec3 dir) { double x; core.Height0(dir * R, out x); return x; }

        static double Calibrate(ulong seed)
        {
            var t = new Terrain(seed, 0.0); int n = SurfaceParams.CalibrationDirs; var xs = new double[n];
            double golden = Math.PI * (3.0 - Math.Sqrt(5.0));
            for (int i = 0; i < n; i++)
            {
                double y = 1.0 - 2.0 * (i + 0.5) / n, r = Math.Sqrt(1.0 - y * y), ph = i * golden;
                xs[i] = t.RawX(new Vec3(r * Math.Cos(ph), y, r * Math.Sin(ph)) * R);
            }
            Array.Sort(xs);
            return xs[(int)((1.0 - SurfaceParams.LandShareTarget) * n)];
        }

        /// <summary>Lake-free terrain height (the natural relief the lake carves into); for tools.</summary>
        public double Height0(Vec3 dir) { double x; return core.Height0(dir * R, out x); }

        void Eval(Vec3 dir, bool full, out SurfaceSample o)
        {
            Vec3 p = dir * R;
            o = default(SurfaceSample);
            double h, water = NoWater, lakeU = double.PositiveInfinity;
            double x;
            Lake lake = lakes.Lookup(p);
            double s = 0;
            bool near = false;
            if (lake != null) near = lake.Locate(dir, out s);
            if (near && s > 0)   // inside the lake: bed profile, independent of the natural terrain
            {
                o.HeightM = lake.Level - lake.Depth * Sm(0, 1, s / lake.W);
                o.Kind = SurfaceKind.Lake; o.WaterLevelM = lake.Level; o.Forest = 0; o.Biome = Biome.Water;
                return;
            }
            double h0 = core.Height0(p, out x);
            h = h0;
            if (near)            // outside, inside the influence disc: bank ramp + fill so the lake has no spill and joins the natural terrain continuously
            {
                double u = -s, L = lake.Level;
                double wf = u <= Lake.FillM ? 1.0 : 1.0 - Sm(0, 1, (u - Lake.FillM) / (Lake.FadeEnd - Lake.FillM));
                double a = h0 + wf * Math.Max(0.0, L - h0);
                h = L + (a - L) * (u >= Lake.BankM ? 1.0 : Sm(0, 1, u / Lake.BankM));
                water = L; lakeU = u;
            }
            o.HeightM = h; o.WaterLevelM = h < 0 ? 0.0 : water;
            SurfaceKind kind = h < -SurfaceParams.ShelfDepthM ? SurfaceKind.Ocean : h < 0 ? SurfaceKind.Sea : h < 2.0 ? SurfaceKind.Beach : SurfaceKind.Land;
            o.Kind = kind;
            if (kind == SurfaceKind.Land && full) core.Climate(p, dir.Y, h, x, lakeU, out o.Forest, out o.Biome);
            else o.Biome = kind == SurfaceKind.Beach ? Biome.Shore : Biome.Water;
        }

        internal static double Sm(double e0, double e1, double v)
        {
            double t = (v - e0) / (e1 - e0); if (t <= 0) return 0; if (t >= 1) return 1; return t * t * (3 - 2 * t);
        }

        /// <summary>The lake-free layers (continents, relief, climate). Own corner caches; LakeField owns a second instance for the rim sampling.</summary>
        internal sealed class Terrain
        {
            const ulong SaltA = 0xA1B2C3D4E5F60718UL;
            readonly FbmLayer3 warp;
            readonly FbmLayer cont, coast, coast2, ridge, moist, patch;
            readonly PlanetNoise.HeightSampler detail;
            readonly double thr;

            static ulong Salt(ulong seed, ulong k) => unchecked(seed ^ (SaltA * k));

            public Terrain(ulong seed, double seaThreshold)
            {
                thr = seaThreshold;
                warp = new FbmLayer3(Salt(seed, 1), Salt(seed, 2), Salt(seed, 3), 20, 1);
                cont = new FbmLayer(Salt(seed, 4), 21, 4);
                coast = new FbmLayer(Salt(seed, 5), 14, 3);
                coast2 = new FbmLayer(Salt(seed, 9), 12, 3);
                ridge = new FbmLayer(Salt(seed, 6), 17, 3);
                moist = new FbmLayer(Salt(seed, 7), 17, 2);
                patch = new FbmLayer(Salt(seed, 8), 14, 2);
                detail = new PlanetNoise.HeightSampler(seed);
            }

            /// <summary>Continent field before the sea threshold and without the RG-169 coast detail (the sea level is calibrated on this).</summary>
            public double RawX(Vec3 p)
            {
                double a, b, c; warp.Sample(p.X, p.Y, p.Z, out a, out b, out c);
                double qx = p.X + SurfaceParams.WarpM * a, qy = p.Y + SurfaceParams.WarpM * b, qz = p.Z + SurfaceParams.WarpM * c;
                return cont.Sample(qx, qy, qz) + SurfaceParams.CoastRoughness * coast.Sample(p.X, p.Y, p.Z);
            }

            /// <summary>x = continent elevation coordinate (0 = coast). Height 0 above the sphere.</summary>
            public double Height0(Vec3 p, out double x)
            {
                x = RawX(p) - thr;
                double ax = x < 0 ? -x : x;
                if (ax < SurfaceParams.CoastBandOuter)   // RG-169 coast structure: 4..1 km wobble, only near the sea threshold (the window is 1 inside the inner band, 0 outside the outer one)
                    x += SurfaceParams.CoastDetail * coast2.Sample(p.X, p.Y, p.Z) * (1.0 - Sm(SurfaceParams.CoastBandInner, SurfaceParams.CoastBandOuter, ax));
                if (x <= 0)
                {
                    double u = -x;
                    if (u < SurfaceParams.ShelfWidth) return -SurfaceParams.ShelfDepthM * Sm(0, 1, u / SurfaceParams.ShelfWidth);
                    return -SurfaceParams.ShelfDepthM - (SurfaceParams.AbyssDepthM - SurfaceParams.ShelfDepthM) * Sm(0, 1, (u - SurfaceParams.ShelfWidth) / SurfaceParams.SlopeWidth);
                }
                double b = SurfaceParams.LowlandM * (1.0 - Math.Exp(-x / SurfaceParams.LowlandScale));
                double h = b;
                double mw = Sm(SurfaceParams.MountainStart, SurfaceParams.MountainFull, x);
                if (mw > 0)
                {
                    double r = 1.0 - Math.Min(1.0, Math.Abs(ridge.Sample(p.X, p.Y, p.Z)) * 3.0);
                    h += SurfaceParams.MountainM * mw * r;
                }
                double df = Sm(0, SurfaceParams.DetailRampM, b);
                if (df > 0) h += df * detail.Sample(p);
                return h;
            }

            /// <summary>Forest density and biome of a land point: temperature from latitude (pole = +Y) and elevation, moisture fBm, continuous fades.</summary>
            public void Climate(Vec3 p, double sinLat, double h, double x, double lakeU, out float forest, out byte biome)
            {
                double t = 30.0 - 55.0 * sinLat * sinLat - 6.5 * (h > 0 ? h : 0) * 0.001;
                double m = 0.56 + 1.1 * moist.Sample(p.X, p.Y, p.Z);
                double dz = (Math.Abs(sinLat) - 0.4) * (1.0 / 0.15);
                m -= 0.25 * Math.Exp(-dz * dz);
                if (m < 0) m = 0; else if (m > 1) m = 1;
                double f = Sm(-2, 5, t) * Sm(0.28, 0.5, m);
                if (f > 0)
                {
                    f *= 0.4 + 0.6 * Sm(-0.3, 0.2, patch.Sample(p.X, p.Y, p.Z));
                    f *= Sm(2, 40, h) * (1.0 - Sm(SurfaceParams.TreeLineM - SurfaceParams.TreeFadeM, SurfaceParams.TreeLineM, h)) * (1.0 - Sm(0.35, 0.6, x));
                    if (lakeU < double.PositiveInfinity) f *= Sm(0, 200, lakeU);
                }
                forest = (float)f;
                biome = t < -10 ? Biome.Ice : t < 0 ? Biome.Tundra : t < 8 ? Biome.Boreal : m < 0.25 ? Biome.Desert : m < 0.45 ? Biome.Grassland : t > 22 ? Biome.Tropical : Biome.Temperate;
            }
        }
    }
}
