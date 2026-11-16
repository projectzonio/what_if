using WhatIf.Planet;
using WhatIf.Planet.Surface;

namespace WhatIf.Planet.Anchors
{
    /// <summary>
    /// RG-177: the ground the anchors route and profile on = planet surface + a global detail layer of the same size as the WorldGen detail a region world adds
    /// on the planet (RG-169 spec ReliefM 60: amplitude 60 * 0.5 * 1.6 = 48 m on the hills octaves 2..4 of a 1400 m wavelength, i.e. 350 / 175 / 87 m, weights
    /// 0.25 / 0.125 / 0.0625 over the 5-octave norm 1.9375, about +-11 m). Here the octaves are 3D value noise at cells 256 / 128 / 64 m on the sphere: a pure
    /// function of (seed, direction), no region, no basin factor. Without it B would be measured on smoother ground than A (row n4).
    /// </summary>
    public static class Ground
    {
        public const double DetailM = 48, Norm = 1.9375;
        static readonly int[] CellLog2 = { 8, 7, 6 };
        static readonly double[] Weight = { 0.25, 0.125, 0.0625 };
        const ulong Salt = 0xA4C40177UL, K = 0x9E3779B97F4A7C15UL;

        public static double Detail(ulong seed, Vec3 dir)
        {
            var p = dir * CubeSphere.Radius; double s = 0;
            for (int o = 0; o < 3; o++) s += Weight[o] * PlanetNoise.Value(seed ^ Salt + (ulong)o * K, p.X, p.Y, p.Z, CellLog2[o]);
            return DetailM * s / Norm;
        }

        public static double Height(PlanetSurface surf, Vec3 dir, out SurfaceKind kind) => surf.HeightKind(dir, out kind) + Detail(surf.Seed, dir);
    }
}
