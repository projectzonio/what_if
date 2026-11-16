using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using WhatIf.Planet;

namespace WhatIf.Planet.Anchors
{
    /// <summary>
    /// The anchors of a set of level-10 regions: settlements whose centre lies in the set, the links with both ends in the set, their corridors and segment
    /// profiles. Build order is a parameter (reverse = the cells, links and corridors in the opposite order) so the determinism row can show it does not matter.
    /// </summary>
    public sealed class AnchorSet
    {
        public readonly List<Settlement> Settlements = new List<Settlement>();
        public readonly List<Link> Links = new List<Link>();
        public readonly List<Corridor> Corridors = new List<Corridor>();
        public readonly Dictionary<ulong, Segment[]> Segments = new Dictionary<ulong, Segment[]>();
        public double SettlementSeconds;

        /// <summary>Settlements whose level-10 region is in the set: the class cells covering the regions (level 11 children, the regions, level 9 parents).</summary>
        public static List<Settlement> InRegions(SettlementField f, ICollection<RegionId> regions, bool reverse)
        {
            var set = new HashSet<RegionId>(regions); var cells = new List<RegionId>();
            foreach (var r in regions)
            {
                for (int a = 0; a < 2; a++) for (int b = 0; b < 2; b++) cells.Add(new RegionId(r.Face, 11, 2 * r.Row + b, 2 * r.Col + a));
                cells.Add(r); cells.Add(new RegionId(r.Face, 9, r.Row >> 1, r.Col >> 1));
            }
            cells = cells.Distinct().OrderBy(SettlementField.IdOf).ToList();
            if (reverse) cells.Reverse();
            var res = new List<Settlement>();
            foreach (var c in cells) { var s = f.Cell(c); if (s != null && set.Contains(CubeSphere.RegionOf(s.Dir, SectorId.RegionLevel))) res.Add(s); }
            return res.OrderBy(s => s.Id).ToList();
        }

        /// <summary>Settlements, links (both ends in the set), corridors and all segment profiles of the regions with one SettlementField/CorridorBuilder (one thread).</summary>
        public static AnchorSet Build(SettlementField f, CorridorBuilder cb, ICollection<RegionId> regions, bool reverse)
        {
            var a = new AnchorSet(); var sw = System.Diagnostics.Stopwatch.StartNew();
            a.Settlements.AddRange(InRegions(f, regions, reverse));
            a.SettlementSeconds = sw.Elapsed.TotalSeconds;
            var ids = new HashSet<ulong>(a.Settlements.Select(s => s.Id)); var links = new SortedDictionary<ulong, Link>();
            foreach (var s in reverse ? Enumerable.Reverse(a.Settlements) : a.Settlements)
                foreach (var l in f.LinksOf(s)) if (ids.Contains(l.A.Id) && ids.Contains(l.B.Id)) links[l.Key] = l;
            a.Links.AddRange(links.Values);
            foreach (var l in reverse ? Enumerable.Reverse(a.Links) : a.Links) a.Add(cb.Route(l));
            a.Corridors.Sort((x, y) => x.Link.Key.CompareTo(y.Link.Key));
            return a;
        }

        /// <summary>Adds a corridor and profiles all its segments.</summary>
        public void Add(Corridor c)
        {
            Corridors.Add(c);
            if (!c.Routed) return;
            var segs = new Segment[c.Joints.Length - 1];
            for (int k = 0; k < segs.Length; k++) segs[k] = CorridorBuilder.Profile(c, k);
            Segments[c.Link.Key] = segs;
        }

        /// <summary>SHA-256 over every anchor value (settlement ids, classes, direction and height bits; links; corridor stations and segment profiles), in id order.</summary>
        public static string Hash(IEnumerable<Settlement> settlements, IEnumerable<Corridor> corridors, IDictionary<ulong, Segment[]> segments)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                void D(double v) => w.Write(BitConverter.DoubleToInt64Bits(v));
                foreach (var s in settlements.OrderBy(x => x.Id)) { w.Write(s.Id); w.Write((byte)s.Class); D(s.Dir.X); D(s.Dir.Y); D(s.Dir.Z); D(s.HeightM); }
                foreach (var c in corridors.OrderBy(x => x.Link.Key))
                {
                    w.Write(c.Link.A.Id); w.Write(c.Link.B.Id); w.Write(c.Link.Layer); w.Write(c.Routed);
                    if (!c.Routed) continue;
                    for (int i = 0; i < c.X.Length; i++) { D(c.X[i]); D(c.Z[i]); D(c.Nat[i]); }
                    foreach (var sg in segments[c.Link.Key]) foreach (var y in sg.Y) D(y);
                }
                w.Flush();
                using (var sha = SHA256.Create()) return string.Concat(sha.ComputeHash(ms.ToArray()).Select(b => b.ToString("x2")));
            }
        }

        public string Hash() => Hash(Settlements, Corridors, Segments);
    }
}
