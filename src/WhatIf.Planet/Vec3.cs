using System;

namespace WhatIf.Planet
{
    /// <summary>Double 3D vector (metres or unit direction). Planet-fixed frame: x,y,z of the six cube faces, see CubeSphere.</summary>
    public readonly struct Vec3
    {
        public readonly double X, Y, Z;
        public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; }
        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator -(Vec3 a) => new Vec3(-a.X, -a.Y, -a.Z);
        public static Vec3 operator *(Vec3 a, double s) => new Vec3(a.X * s, a.Y * s, a.Z * s);
        public static Vec3 operator *(double s, Vec3 a) => new Vec3(a.X * s, a.Y * s, a.Z * s);
        public static Vec3 operator /(Vec3 a, double s) => new Vec3(a.X / s, a.Y / s, a.Z / s);
        public static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        public static Vec3 Cross(Vec3 a, Vec3 b) => new Vec3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
        public Vec3 Normalized() { double l = Length; return new Vec3(X / l, Y / l, Z / l); }
        /// <summary>Angle between two vectors, atan2 form (accurate for tiny angles).</summary>
        public static double Angle(Vec3 a, Vec3 b) => Math.Atan2(Cross(a, b).Length, Dot(a, b));
        public override string ToString() => $"({X:R}, {Y:R}, {Z:R})";
    }

    /// <summary>SplitMix64 (same stream as WhatIf.WorldGen.Rng). System.Random differs across runtimes.</summary>
    public static class SplitMix64
    {
        public static ulong Mix(ulong z)
        {
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
        /// <summary>Advances the state by the golden gamma and returns the next value.</summary>
        public static ulong Next(ref ulong state) { state += 0x9E3779B97F4A7C15UL; return Mix(state); }
        /// <summary>Uniform double in [0,1) from the top 53 bits.</summary>
        public static double NextDouble(ref ulong state) => (Next(ref state) >> 11) * (1.0 / 9007199254740992.0);
    }
}
