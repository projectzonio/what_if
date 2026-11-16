using System;
using System.Collections.Generic;

namespace WhatIf.Planet.FarLayer
{
    /// <summary>
    /// The near layer as the far layer sees it: level-15 sectors whose centre is within LoadRadius (600 m) of the ground point (the streaming rule today).
    /// Derived per Update: level-13 hole masks (4 x 4 sectors per node), the keys of all ancestors (these nodes must split), the exposed sector sides (skirt).
    /// BFS over SectorId.Neighbour, so it crosses cube edges.
    /// </summary>
    public sealed class NearSet
    {
        public const double LoadRadiusM = 600.0;
        public readonly HashSet<SectorId> Sectors = new HashSet<SectorId>();
        public readonly Dictionary<long, ushort> Masks = new Dictionary<long, ushort>();     // level-13 node key -> mask
        public readonly HashSet<long> Ancestors = new HashSet<long>();                       // keys of every ancestor level 0..13
        public readonly List<KeyValuePair<SectorId, int>> Exposed = new List<KeyValuePair<SectorId, int>>();   // (sector, side 0..3) with a non-near neighbour
        readonly HashSet<SectorId> visited = new HashSet<SectorId>(); readonly Queue<SectorId> queue = new Queue<SectorId>();
        public Vec3 GroundDir; public double RadiusM = LoadRadiusM;

        public static Vec3 CentreDir(SectorId s) => Lattice.Compose(s.Face, Lattice.Coord(s.Col * QuadSector.Sub + QuadSector.Sub / 2), Lattice.Coord(s.Row * QuadSector.Sub + QuadSector.Sub / 2));
        public bool Contains(SectorId s) => Sectors.Contains(s);
        public int Count => Sectors.Count;
        /// <summary>Skirt quads: 4 far quad edges per exposed sector side.</summary>
        public int SkirtQuads => Exposed.Count * 4;

        public void Update(Vec3 groundDir)
        {
            GroundDir = groundDir; Sectors.Clear(); Masks.Clear(); Ancestors.Clear(); Exposed.Clear(); visited.Clear(); queue.Clear();
            double visitR = RadiusM + 700.0;                 // a sector centre is at most ~230 m from its corner; the BFS front keeps one more sector ring
            SectorId s0 = SectorId.At(groundDir); visited.Add(s0); queue.Enqueue(s0);
            while (queue.Count > 0)
            {
                SectorId s = queue.Dequeue();
                double d = Vec3.Angle(CentreDir(s), groundDir) * CubeSphere.Radius;
                if (d > visitR) continue;
                if (d <= RadiusM) Sectors.Add(s);
                for (int k = 0; k < 4; k++) { SectorId n = s.Neighbour(k); if (visited.Add(n)) queue.Enqueue(n); }
            }
            foreach (SectorId s in Sectors)
            {
                int bit = ((s.Row & 3) << 2) | (s.Col & 3), shift = SectorId.Level - 13; long k13 = NodeKey.Pack(s.Face, 13, s.Row >> shift, s.Col >> shift);
                ushort m; Masks.TryGetValue(k13, out m); Masks[k13] = (ushort)(m | (1 << bit));
                for (int l = 0; l <= 13; l++) Ancestors.Add(NodeKey.Pack(s.Face, l, s.Row >> (SectorId.Level - l), s.Col >> (SectorId.Level - l)));
                for (int k = 0; k < 4; k++) if (!Sectors.Contains(s.Neighbour(k))) Exposed.Add(new KeyValuePair<SectorId, int>(s, k));
            }
        }
    }
}
