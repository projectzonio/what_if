using System;
using System.Security.Cryptography;
using WhatIf.Planet.WorldGen;

namespace WhatIf.Planet.Runtime
{
    /// <summary>SHA-256 over every field of an AdaptedSector (the bits, not a tolerance): the determinism row compares streamer output with a direct AnchorSector.Build.</summary>
    public static class SectorHash
    {
        public static string Of(AdaptedSector s)
        {
            using (var h = SHA256.Create())
            {
                Add(h, s.X); Add(h, s.Z); Add(h, s.PlanetBase); Add(h, s.Contribution); Add(h, s.Taper); Add(h, s.EdgeDistM);
                var w = new double[s.World.Length * 3]; for (int i = 0; i < s.World.Length; i++) { w[3 * i] = s.World[i].X; w[3 * i + 1] = s.World[i].Y; w[3 * i + 2] = s.World[i].Z; }
                Add(h, w); Add(h, s.Lx); Add(h, s.Ly); Add(h, s.Lz);
                h.TransformFinalBlock(new byte[0], 0, 0);
                return BitConverter.ToString(h.Hash).Replace("-", "");
            }
        }
        static void Add(HashAlgorithm h, Array a)
        {
            var b = new byte[Buffer.ByteLength(a)]; Buffer.BlockCopy(a, 0, b, 0, b.Length); h.TransformBlock(b, 0, b.Length, null, 0);
        }
    }
}
