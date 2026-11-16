using System;
using System.Collections.Generic;
using WhatIf.Planet;

namespace WhatIf.Planet.Surface
{
    /// <summary>
    /// One lake of a 3D cell (2^13 m). Everything it touches (disc + bank + fill + fade, at most 1200 + 400 m) lies inside its own cell, so a sample needs only its own cell.
    /// Shape: centre direction, base radius Rho0 (250..1000 m) times a two-harmonic angular wobble (0.8..1.2) in the centre's tangent plane; bed depth 3..12 m.
    /// </summary>
    public sealed class Lake
    {
        public const double BankM = 100, FillM = 120, FadeEnd = 400, RimM = 120;   // bank ramp, fully filled band, end of the fill fade, rim ring distance outside the shore
        public readonly long Ix, Iy, Iz;
        public readonly Vec3 Centre, East, North;
        public readonly double Rho0, Level, Depth, W, Influence;
        readonly double p2, p3;

        internal Lake(long ix, long iy, long iz, Vec3 centre, double rho0, double level, double depth, double p2, double p3)
        {
            Ix = ix; Iy = iy; Iz = iz; Centre = centre; Rho0 = rho0; Level = level; Depth = depth; W = 0.5 * rho0; this.p2 = p2; this.p3 = p3;
            Influence = 1.2 * rho0 + FadeEnd;
            Vec3 e = Vec3.Cross(new Vec3(0, 1, 0), centre);
            if (e.Length < 1e-6) e = Vec3.Cross(new Vec3(1, 0, 0), centre);
            East = e.Normalized(); North = Vec3.Cross(centre, East);
        }

        /// <summary>Shore radius (m) at angle theta in the tangent plane.</summary>
        public double Radius(double theta) => Rho0 * (1.0 + 0.12 * Math.Sin(2 * theta + p2) + 0.08 * Math.Sin(3 * theta + p3));

        /// <summary>s = shore radius - distance of dir from the centre (m): > 0 inside the lake. False when dir is outside the influence disc.</summary>
        public bool Locate(Vec3 dir, out double s)
        {
            Vec3 v = dir - Centre;
            double a = Vec3.Dot(v, East) * CubeSphere.Radius, b = Vec3.Dot(v, North) * CubeSphere.Radius, d2 = a * a + b * b;
            if (d2 >= Influence * Influence) { s = 0; return false; }
            s = Radius(Math.Atan2(b, a)) - Math.Sqrt(d2);
            return true;
        }

        /// <summary>Direction at angle theta and tangent distance d from the centre (inverse of Locate: Locate(PointAt(t, d)) gives s = Radius(t) - d).</summary>
        public Vec3 PointAt(double theta, double d)
        {
            Vec3 c = Centre * CubeSphere.Radius + East * (d * Math.Cos(theta)) + North * (d * Math.Sin(theta));
            return c.Normalized();
        }
    }

    /// <summary>Lake lookup per 3D cell with a bounded cache (cleared when it exceeds MaxEntries; not thread safe) and a last-cell shortcut for grid walks.</summary>
    internal sealed class LakeField
    {
        public const double Cell = 8192.0, InvCell = 1.0 / 8192.0;
        const int MaxEntries = 4096, RimSamples = 64;
        const double MaxRimRelief = 30.0;
        const ulong K1 = 0x9E3779B97F4A7C15UL, K2 = 0xC2B2AE3D27D4EB4FUL, K3 = 0x165667B19E3779F9UL, Salt = 0x51ED270B1F2A9C37UL;
        readonly ulong seed;
        readonly PlanetSurface.Terrain rim;
        readonly Dictionary<(long, long, long), Lake> cache = new Dictionary<(long, long, long), Lake>();
        long lx = long.MinValue, ly, lz; Lake last;

        public LakeField(ulong seed, PlanetSurface.Terrain rimTerrain) { this.seed = seed; rim = rimTerrain; }
        public int Count => cache.Count;

        public Lake Lookup(Vec3 p)
        {
            long ix = (long)Math.Floor(p.X * InvCell), iy = (long)Math.Floor(p.Y * InvCell), iz = (long)Math.Floor(p.Z * InvCell);
            if (ix == lx && iy == ly && iz == lz) return last;
            last = Get(ix, iy, iz); lx = ix; ly = iy; lz = iz;
            return last;
        }

        public Lake Get(long ix, long iy, long iz)
        {
            var key = (ix, iy, iz);
            Lake l;
            if (cache.TryGetValue(key, out l)) return l;
            l = Build(ix, iy, iz);
            if (cache.Count >= MaxEntries) { cache.Clear(); }
            cache[key] = l;
            return l;
        }

        Lake Build(long ix, long iy, long iz)
        {
            ulong st = SplitMix64.Mix(seed ^ Salt ^ ((ulong)ix * K1) ^ ((ulong)iy * K2) ^ ((ulong)iz * K3));
            if (SplitMix64.NextDouble(ref st) >= SurfaceParams.LakeProb) return null;
            double cx = (ix + 0.5) * Cell, cy = (iy + 0.5) * Cell, cz = (iz + 0.5) * Cell;
            double jx = (SplitMix64.NextDouble(ref st) - 0.5) * 0.5 * Cell, jy = (SplitMix64.NextDouble(ref st) - 0.5) * 0.5 * Cell, jz = (SplitMix64.NextDouble(ref st) - 0.5) * 0.5 * Cell;
            Vec3 v = new Vec3(cx + jx, cy + jy, cz + jz), centre = v.Normalized(), pc = centre * CubeSphere.Radius;
            double lim = 0.5 * Cell - 1650.0;     // disc + bank + fill + fade (<= 1600 m) must stay inside the cell
            if (Math.Abs(pc.X - cx) > lim || Math.Abs(pc.Y - cy) > lim || Math.Abs(pc.Z - cz) > lim) return null;
            double u = SplitMix64.NextDouble(ref st), rho0 = 250.0 + 750.0 * u * u, depth = 3.0 + 9.0 * SplitMix64.NextDouble(ref st);
            double p2 = SplitMix64.NextDouble(ref st) * 2 * Math.PI, p3 = SplitMix64.NextDouble(ref st) * 2 * Math.PI;
            var lake = new Lake(ix, iy, iz, centre, rho0, 0, depth, p2, p3);
            double min = double.MaxValue, max = double.MinValue;
            for (int ring = 0; ring < 2; ring++)           // rim rings at 40 m and 120 m outside the shore
                for (int k = 0; k < RimSamples; k++)
                {
                    double th = 2 * Math.PI * k / RimSamples, x;
                    Vec3 d = lake.PointAt(th, lake.Radius(th) + (ring == 0 ? 40.0 : Lake.RimM));
                    double h = rim.Height0(d * CubeSphere.Radius, out x);
                    if (h < min) min = h; if (h > max) max = h;
                }
            if (min < 3.0) return null;            // the whole rim must stand >= 3 m above the sea: level >= 2.5 m
            if (max - min > MaxRimRelief) return null;   // lakes sit on flat ground: the bank ramp (100 m) then stays below ~45 degrees
            return new Lake(ix, iy, iz, centre, rho0, min - 0.5, depth, p2, p3);
        }
    }
}
