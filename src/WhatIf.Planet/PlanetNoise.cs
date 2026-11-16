using System;

namespace WhatIf.Planet
{
    /// <summary>
    /// CPU double authority noise (WI-ADR-001 row 4): 3D value noise over the planet-fixed double position in metres. The lattice cell is an int64 from
    /// floor(x / cell) (cell sizes are powers of two, so the division is exact), the fraction is computed in double, only the final value is rounded to float.
    /// A 3D lattice needs no face/pole parameterisation, so there is no seam to match. Same double input = same output bits on any origin.
    /// </summary>
    public static class PlanetNoise
    {
        const ulong K1 = 0x9E3779B97F4A7C15UL, K2 = 0xC2B2AE3D27D4EB4FUL, K3 = 0x165667B19E3779F9UL;
        static readonly double[] InvCell = MakeInv();   // exact 2^-k (powers of two), k = 0..40
        static double[] MakeInv() { var t = new double[41]; double v = 1; for (int i = 0; i < t.Length; i++, v *= 0.5) t[i] = v; return t; }

        /// <summary>One octave, value in [-1,1] (double). cell = 2^cellLog2 metres.</summary>
        public static double Value(ulong seed, double x, double y, double z, int cellLog2)
        {
            double inv = InvCell[cellLog2];
            double gx = x * inv, gy = y * inv, gz = z * inv;
            double fx0 = Math.Floor(gx), fy0 = Math.Floor(gy), fz0 = Math.Floor(gz);
            long ix = (long)fx0, iy = (long)fy0, iz = (long)fz0;
            double tx = gx - fx0, ty = gy - fy0, tz = gz - fz0;
            ulong hx0 = (ulong)ix * K1, hx1 = (ulong)(ix + 1) * K1, hy0 = (ulong)iy * K2, hy1 = (ulong)(iy + 1) * K2, hz0 = (ulong)iz * K3, hz1 = (ulong)(iz + 1) * K3;
            ulong s = seed;
            double c000 = Corner(s ^ hx0 ^ hy0 ^ hz0), c100 = Corner(s ^ hx1 ^ hy0 ^ hz0), c010 = Corner(s ^ hx0 ^ hy1 ^ hz0), c110 = Corner(s ^ hx1 ^ hy1 ^ hz0);
            double c001 = Corner(s ^ hx0 ^ hy0 ^ hz1), c101 = Corner(s ^ hx1 ^ hy0 ^ hz1), c011 = Corner(s ^ hx0 ^ hy1 ^ hz1), c111 = Corner(s ^ hx1 ^ hy1 ^ hz1);
            return Interp(c000, c100, c010, c110, c001, c101, c011, c111, tx, ty, tz);
        }

        static double Interp(double c000, double c100, double c010, double c110, double c001, double c101, double c011, double c111, double tx, double ty, double tz)
        {
            double u = tx * tx * (3 - 2 * tx), v = ty * ty * (3 - 2 * ty), w = tz * tz * (3 - 2 * tz);
            double x00 = c000 + (c100 - c000) * u, x10 = c010 + (c110 - c010) * u, x01 = c001 + (c101 - c001) * u, x11 = c011 + (c111 - c011) * u;
            double y0 = x00 + (x10 - x00) * v, y1 = x01 + (x11 - x01) * v;
            return y0 + (y1 - y0) * w;
        }

        /// <summary>
        /// Height sampler with a per-octave corner cache for callers that walk a grid (a sector build): consecutive 4 m vertices almost always stay in the
        /// same lattice cell, so the 8 corner hashes are reused. Returns exactly the bits of PlanetNoise.Height (same arithmetic, same order). Not thread safe.
        /// </summary>
        public sealed class HeightSampler
        {
            readonly ulong seed;
            readonly long[] cx = new long[HeightOctaves], cy = new long[HeightOctaves], cz = new long[HeightOctaves];
            readonly double[] c = new double[HeightOctaves * 8];
            public HeightSampler(ulong seed) { this.seed = seed; for (int o = 0; o < HeightOctaves; o++) cx[o] = long.MinValue; }

            public float Sample(Vec3 p)
            {
                double sum = 0, amp = 1, norm = 0;
                for (int o = 0; o < HeightOctaves; o++)
                {
                    double inv = InvCell[HeightBaseCellLog2 - o];
                    double gx = p.X * inv, gy = p.Y * inv, gz = p.Z * inv;
                    double fx0 = Math.Floor(gx), fy0 = Math.Floor(gy), fz0 = Math.Floor(gz);
                    long ix = (long)fx0, iy = (long)fy0, iz = (long)fz0;
                    int b = o * 8;
                    if (ix != cx[o] || iy != cy[o] || iz != cz[o])
                    {
                        cx[o] = ix; cy[o] = iy; cz[o] = iz;
                        ulong s = seed + (ulong)o * K1;
                        ulong hx0 = (ulong)ix * K1, hx1 = (ulong)(ix + 1) * K1, hy0 = (ulong)iy * K2, hy1 = (ulong)(iy + 1) * K2, hz0 = (ulong)iz * K3, hz1 = (ulong)(iz + 1) * K3;
                        c[b] = Corner(s ^ hx0 ^ hy0 ^ hz0); c[b + 1] = Corner(s ^ hx1 ^ hy0 ^ hz0); c[b + 2] = Corner(s ^ hx0 ^ hy1 ^ hz0); c[b + 3] = Corner(s ^ hx1 ^ hy1 ^ hz0);
                        c[b + 4] = Corner(s ^ hx0 ^ hy0 ^ hz1); c[b + 5] = Corner(s ^ hx1 ^ hy0 ^ hz1); c[b + 6] = Corner(s ^ hx0 ^ hy1 ^ hz1); c[b + 7] = Corner(s ^ hx1 ^ hy1 ^ hz1);
                    }
                    sum += amp * Interp(c[b], c[b + 1], c[b + 2], c[b + 3], c[b + 4], c[b + 5], c[b + 6], c[b + 7], gx - fx0, gy - fy0, gz - fz0);
                    norm += amp; amp *= 0.5;
                }
                return HeightAmplitude * (float)(sum / norm);
            }
        }

        static double Corner(ulong h) => (SplitMix64.Mix(h) >> 11) * (2.0 / 9007199254740992.0) - 1.0;

        /// <summary>Fractal sum, normalised to about [-1,1]; the result is rounded to float once, at the end. Octave o uses cell 2^(baseCellLog2-o).</summary>
        public static float Fbm(ulong seed, double x, double y, double z, int baseCellLog2, int octaves)
        {
            double sum = 0, amp = 1, norm = 0;
            for (int o = 0; o < octaves; o++)
            {
                sum += amp * Value(seed + (ulong)o * K1, x, y, z, baseCellLog2 - o);
                norm += amp; amp *= 0.5;
            }
            return (float)(sum / norm);
        }

        public const float HeightAmplitude = 150f;
        public const int HeightBaseCellLog2 = 12, HeightOctaves = 4;

        /// <summary>Terrain height above the sphere (metres) at a world position on the sphere: 150 m * fbm, cells 4096..512 m.</summary>
        public static float Height(ulong seed, Vec3 p) => HeightAmplitude * Fbm(seed, p.X, p.Y, p.Z, HeightBaseCellLog2, HeightOctaves);

        /// <summary>The same point rebuilt from a sector origin and a local offset (what a sector-local caller would do).</summary>
        public static float HeightFromOrigin(ulong seed, Vec3 origin, Vec3 local) => Height(seed, origin + local);
    }
}
