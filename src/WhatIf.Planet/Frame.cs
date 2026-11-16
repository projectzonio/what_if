using System;

namespace WhatIf.Planet
{
    /// <summary>
    /// Vehicle frame in the planet-fixed frame (WI-ADR-001 row 1): double position + orthonormal (Right, Up, Forward) with Right = Forward x Up,
    /// carried by parallel transport along geodesic chords: rotate the whole frame about up0 x up1 by the angle between the two ups.
    /// No latitude/longitude and no cube-face branch: this works identically over a pole, a face edge and a cube corner.
    /// </summary>
    public struct Frame
    {
        public Vec3 Position;
        public Vec3 Right, Up, Forward;

        public static Frame At(Vec3 position, Vec3 forwardHint)
        {
            Vec3 up = position.Normalized();
            Vec3 f = (forwardHint - up * Vec3.Dot(forwardHint, up)).Normalized();
            return new Frame { Position = position, Up = up, Forward = f, Right = Vec3.Cross(f, up) };
        }

        /// <summary>Moves to newPosition and parallel-transports the frame along the great-circle chord. Returns the rotation angle (rad).</summary>
        public double Transport(Vec3 newPosition)
        {
            Vec3 u1 = newPosition.Normalized();
            Vec3 axis = Vec3.Cross(Up, u1);
            double s = axis.Length, c = Vec3.Dot(Up, u1);
            double theta = Math.Atan2(s, c);
            Position = newPosition;
            if (s < 1e-300) return 0;
            Vec3 k = axis / s;
            Up = Rotate(Up, k, s, c); Forward = Rotate(Forward, k, s, c); Right = Rotate(Right, k, s, c);
            return theta;
        }

        // Rodrigues with sin/cos already known: v cos + (k x v) sin + k (k.v)(1-cos). s,c come from atan2-consistent values (s^2 + c^2 = 1 after normalising).
        static Vec3 Rotate(Vec3 v, Vec3 k, double s, double c)
        {
            double n = Math.Sqrt(s * s + c * c); s /= n; c /= n;
            return v * c + Vec3.Cross(k, v) * s + k * (Vec3.Dot(k, v) * (s * s / (1.0 + c)));   // 1-c without cancellation
        }

        /// <summary>Largest deviation from an orthonormal right-handed basis whose Up is the radial of Position.</summary>
        public double Error()
        {
            double e = Math.Abs(Right.Length - 1);
            e = Math.Max(e, Math.Abs(Up.Length - 1)); e = Math.Max(e, Math.Abs(Forward.Length - 1));
            e = Math.Max(e, Math.Abs(Vec3.Dot(Right, Up))); e = Math.Max(e, Math.Abs(Vec3.Dot(Right, Forward))); e = Math.Max(e, Math.Abs(Vec3.Dot(Up, Forward)));
            e = Math.Max(e, (Vec3.Cross(Forward, Up) - Right).Length);
            e = Math.Max(e, (Up - Position.Normalized()).Length);
            return e;
        }

        /// <summary>Rotation angle (rad) that maps frame a onto frame b: atan2 of the antisymmetric part of B A^T (accurate for tiny angles).</summary>
        public static double RotationAngle(Frame a, Frame b)
        {
            // M = sum over axes of b_i a_i^T; vector part = 0.5 * sum (a_i x b_i), trace = sum a_i.b_i
            Vec3 v = (Vec3.Cross(a.Right, b.Right) + Vec3.Cross(a.Up, b.Up) + Vec3.Cross(a.Forward, b.Forward)) * 0.5;
            double tr = Vec3.Dot(a.Right, b.Right) + Vec3.Dot(a.Up, b.Up) + Vec3.Dot(a.Forward, b.Forward);
            return Math.Atan2(v.Length, (tr - 1.0) * 0.5);
        }
    }
}
