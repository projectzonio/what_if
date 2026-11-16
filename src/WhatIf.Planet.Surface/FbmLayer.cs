using System;
using WhatIf.Planet;

namespace WhatIf.Planet.Surface
{
    /// <summary>
    /// Double fBm over the planet-fixed position with a per-octave corner cache (same lattice arithmetic as WhatIf.Planet.PlanetNoise.Value: int64 cell from
    /// floor(x * 2^-k), double fraction, smoothstep trilinear; octave o uses seed + o * K1 and cell 2^(baseCellLog2 - o), amplitude 0.5^o, normalised).
    /// The arithmetic of Value/Interp/Corner is copied because PlanetNoise keeps them private (RG-168 must not touch src/WhatIf.Planet). The cache only skips
    /// re-hashing the 8 corners: the result is bit-identical with or without it. Not thread safe (one instance per thread, like HeightSampler).
    /// </summary>
    public sealed class FbmLayer
    {
        const ulong K1 = 0x9E3779B97F4A7C15UL, K2 = 0xC2B2AE3D27D4EB4FUL, K3 = 0x165667B19E3779F9UL;
        readonly ulong seed;
        readonly int octaves;
        readonly double[] inv;          // exact 2^-(baseCellLog2 - o)
        readonly double invNorm;        // 1 / sum of the amplitudes
        readonly long[] cx, cy, cz;
        readonly double[] c;

        public FbmLayer(ulong seed, int baseCellLog2, int octaves)
        {
            this.seed = seed; this.octaves = octaves;
            inv = new double[octaves]; cx = new long[octaves]; cy = new long[octaves]; cz = new long[octaves]; c = new double[octaves * 8];
            double norm = 0, amp = 1;
            for (int o = 0; o < octaves; o++)
            {
                int k = baseCellLog2 - o; if (k < 0 || k > 40) throw new ArgumentOutOfRangeException(nameof(baseCellLog2));
                inv[o] = Pow2(-k); cx[o] = long.MinValue; norm += amp; amp *= 0.5;
            }
            invNorm = 1.0 / norm;
        }

        static double Pow2(int e) { double v = 1.0; for (int i = 0; i < -e; i++) v *= 0.5; for (int i = 0; i < e; i++) v *= 2.0; return v; }   // exact

        static double Corner(ulong h) => (SplitMix64.Mix(h) >> 11) * (2.0 / 9007199254740992.0) - 1.0;

        /// <summary>fBm in about [-1, 1], double (not rounded to float).</summary>
        public double Sample(double x, double y, double z)
        {
            double sum = 0, amp = 1;
            for (int o = 0; o < octaves; o++)
            {
                double iv = inv[o];
                double gx = x * iv, gy = y * iv, gz = z * iv;
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
                double tx = gx - fx0, ty = gy - fy0, tz = gz - fz0;
                double u = tx * tx * (3 - 2 * tx), v = ty * ty * (3 - 2 * ty), w = tz * tz * (3 - 2 * tz);
                double x00 = c[b] + (c[b + 1] - c[b]) * u, x10 = c[b + 2] + (c[b + 3] - c[b + 2]) * u, x01 = c[b + 4] + (c[b + 5] - c[b + 4]) * u, x11 = c[b + 6] + (c[b + 7] - c[b + 6]) * u;
                double y0 = x00 + (x10 - x00) * v, y1 = x01 + (x11 - x01) * v;
                sum += amp * (y0 + (y1 - y0) * w);
                amp *= 0.5;
            }
            return sum * invNorm;
        }
    }

    /// <summary>
    /// Three fBm layers with the same base cell and octave count at once (the continent domain warp x / y / z): one floor / smoothstep / corner-cache test per octave for all three.
    /// Each output is bit-identical to an FbmLayer of the same seed, cell and octave count (the arithmetic of one field is the same; unit test). Not thread safe.
    /// </summary>
    public sealed class FbmLayer3
    {
        const ulong K1 = 0x9E3779B97F4A7C15UL, K2 = 0xC2B2AE3D27D4EB4FUL, K3 = 0x165667B19E3779F9UL;
        readonly ulong[] seed = new ulong[3];
        readonly int octaves;
        readonly double[] inv;
        readonly double invNorm;
        readonly long[] cx, cy, cz;
        readonly double[] c;   // [octave * 24 + field * 8 + corner]

        public FbmLayer3(ulong seedA, ulong seedB, ulong seedC, int baseCellLog2, int octaves)
        {
            seed[0] = seedA; seed[1] = seedB; seed[2] = seedC; this.octaves = octaves;
            inv = new double[octaves]; cx = new long[octaves]; cy = new long[octaves]; cz = new long[octaves]; c = new double[octaves * 24];
            double norm = 0, amp = 1;
            for (int o = 0; o < octaves; o++)
            {
                int k = baseCellLog2 - o; if (k < 0 || k > 40) throw new ArgumentOutOfRangeException(nameof(baseCellLog2));
                double v = 1.0; for (int i = 0; i < k; i++) v *= 0.5;
                inv[o] = v; cx[o] = long.MinValue; norm += amp; amp *= 0.5;
            }
            invNorm = 1.0 / norm;
        }

        static double Corner(ulong h) => (SplitMix64.Mix(h) >> 11) * (2.0 / 9007199254740992.0) - 1.0;

        public void Sample(double x, double y, double z, out double oa, out double ob, out double oc)
        {
            double s0 = 0, s1 = 0, s2 = 0, amp = 1;
            for (int o = 0; o < octaves; o++)
            {
                double iv = inv[o];
                double gx = x * iv, gy = y * iv, gz = z * iv;
                double fx0 = Math.Floor(gx), fy0 = Math.Floor(gy), fz0 = Math.Floor(gz);
                long ix = (long)fx0, iy = (long)fy0, iz = (long)fz0;
                int bo = o * 24;
                if (ix != cx[o] || iy != cy[o] || iz != cz[o])
                {
                    cx[o] = ix; cy[o] = iy; cz[o] = iz;
                    ulong hx0 = (ulong)ix * K1, hx1 = (ulong)(ix + 1) * K1, hy0 = (ulong)iy * K2, hy1 = (ulong)(iy + 1) * K2, hz0 = (ulong)iz * K3, hz1 = (ulong)(iz + 1) * K3;
                    for (int f = 0; f < 3; f++)
                    {
                        ulong s = seed[f] + (ulong)o * K1; int b = bo + f * 8;
                        c[b] = Corner(s ^ hx0 ^ hy0 ^ hz0); c[b + 1] = Corner(s ^ hx1 ^ hy0 ^ hz0); c[b + 2] = Corner(s ^ hx0 ^ hy1 ^ hz0); c[b + 3] = Corner(s ^ hx1 ^ hy1 ^ hz0);
                        c[b + 4] = Corner(s ^ hx0 ^ hy0 ^ hz1); c[b + 5] = Corner(s ^ hx1 ^ hy0 ^ hz1); c[b + 6] = Corner(s ^ hx0 ^ hy1 ^ hz1); c[b + 7] = Corner(s ^ hx1 ^ hy1 ^ hz1);
                    }
                }
                double tx = gx - fx0, ty = gy - fy0, tz = gz - fz0;
                double u = tx * tx * (3 - 2 * tx), v = ty * ty * (3 - 2 * ty), w = tz * tz * (3 - 2 * tz);
                s0 += amp * Interp(c, bo, u, v, w); s1 += amp * Interp(c, bo + 8, u, v, w); s2 += amp * Interp(c, bo + 16, u, v, w);
                amp *= 0.5;
            }
            oa = s0 * invNorm; ob = s1 * invNorm; oc = s2 * invNorm;
        }

        static double Interp(double[] c, int b, double u, double v, double w)
        {
            double x00 = c[b] + (c[b + 1] - c[b]) * u, x10 = c[b + 2] + (c[b + 3] - c[b + 2]) * u, x01 = c[b + 4] + (c[b + 5] - c[b + 4]) * u, x11 = c[b + 6] + (c[b + 7] - c[b + 6]) * u;
            double y0 = x00 + (x10 - x00) * v, y1 = x01 + (x11 - x01) * v;
            return y0 + (y1 - y0) * w;
        }
    }
}
