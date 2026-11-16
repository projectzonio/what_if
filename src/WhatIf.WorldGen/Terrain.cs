using System;
using System.Collections.Generic;

namespace WhatIf.WorldGen
{
    /// <summary>Spec block "terrain" (RG-118, W5a). All fields optional; relief_m = 0 means flat and the generator output is bit-identical to W3.1.</summary>
    public sealed class TerrainSpec
    {
        public double ReliefM;                       // hill relief (peak to peak, nominal); 0 = flat
        public double BaseWavelengthM = 1400;
        public int Octaves = 5;
        public bool HasMountain;
        public V2 MountainA, MountainB;              // centre line of the ridge band
        public double MountainWidthM = 2400;         // full width of the band (the profile reaches 0 at +-width/2)
        public double MountainReliefM;
        public double SettlementFlatMarginM = 300;   // flat basin radius = settlement radius + margin
        public double BasinTransitionM = 800;

        public bool Enabled { get { return ReliefM > 0 || (HasMountain && MountainReliefM > 0); } }

        public static TerrainSpec FromJson(object root)
        {
            var t = new TerrainSpec();
            if (!(Json.Path(root, "terrain") is JObj)) return t;
            t.ReliefM = Json.Num(root, 0, "terrain", "relief_m");
            t.BaseWavelengthM = Json.Num(root, 1400, "terrain", "base_wavelength_m");
            t.Octaves = (int)Json.Num(root, 5, "terrain", "octaves");
            t.SettlementFlatMarginM = Json.Num(root, 300, "terrain", "settlement_flat_margin_m");
            t.BasinTransitionM = Json.Num(root, 800, "terrain", "basin_transition_m");
            if (Json.Path(root, "terrain", "mountain") is JObj)
            {
                t.HasMountain = true;
                t.MountainA = new V2(Json.Num(root, 0, "terrain", "mountain", "x0"), Json.Num(root, 0, "terrain", "mountain", "z0"));
                t.MountainB = new V2(Json.Num(root, 0, "terrain", "mountain", "x1"), Json.Num(root, 0, "terrain", "mountain", "z1"));
                t.MountainWidthM = Json.Num(root, 2400, "terrain", "mountain", "width_m");
                t.MountainReliefM = Json.Num(root, 0, "terrain", "mountain", "relief_m");
            }
            return t;
        }
    }

    /// <summary>Samples of one terrain tile: (N+1)^2 heights and hole flags, row-major with z as the slow index (index = iz * (N+1) + ix).</summary>
    public sealed class TerrainTile
    {
        public int N;
        public float[] Heights;
        public bool[] Holes;
    }

    /// <summary>Natural height N(x,z) (pure function of seed and spec, double math, no clock) and the carved terrain T(x,z) the roads shape.
    /// With the spec relief 0 the model is disabled: Natural = Height = 0 and Hole = false.</summary>
    public sealed class TerrainModel
    {
        // ---- road influence (design values, metres) ----
        public const double ShoulderM = 1.5, SlopeRun = 1.5, HoleLeadM = 8.0, HoleMarginM = 1.0, MaxDiffM = 20.0;
        public const double IndexCellM = 64.0;   // hash cell of the carve index (RG-170: HeightBatch keys its candidate sets by the same cell range)

        sealed class Seg
        {
            public V2 A, B;
            public double YA, YB, Hw;
            public bool CapA, CapB;   // round cap at the segment ends of an edge (false at a portal: the road continues in a tunnel or on a bridge)
            public int Prev = -1, Next = -1;   // neighbouring segments of the same edge
        }

        public readonly TerrainSpec Spec;
        readonly bool _enabled;
        readonly byte[] _perm = new byte[512];
        readonly double _offX, _offZ, _offX2, _offZ2;
        readonly List<V2> _setC = new List<V2>();
        readonly List<double> _setR = new List<double>();
        readonly List<Seg> _segs = new List<Seg>();
        readonly SegIndex _segIdx = new SegIndex(IndexCellM);
        readonly List<Seg> _holeSegs = new List<Seg>();
        readonly SegIndex _holeIdx = new SegIndex(32);
        readonly Func<double, double, double> _base;   // RG-165: optional external base height (x, z) -> m; null = the map-local hills + mountain
        [ThreadStatic] static List<int> _scratch;

        public bool Enabled { get { return _enabled; } }

        public TerrainModel(WorldSpec spec)
        {
            Spec = spec.Terrain;
            _enabled = Spec.Enabled;
            var rng = Rng.ForStage(spec.Seed, "terrain");
            var p = new byte[256];
            for (int i = 0; i < 256; i++) p[i] = (byte)i;
            for (int i = 255; i > 0; i--) { int j = rng.Int(i + 1); byte t = p[i]; p[i] = p[j]; p[j] = t; }
            for (int i = 0; i < 512; i++) _perm[i] = p[i & 255];
            _offX = rng.Range(0, 1000); _offZ = rng.Range(0, 1000);
            _offX2 = rng.Range(0, 1000); _offZ2 = rng.Range(0, 1000);
        }

        /// <summary>RG-165 hook: baseHeight (x, z) -> metres replaces the low-frequency part of Natural (the hills octaves below DetailFirstOctave and the
        /// mountain band); the hills octaves from DetailFirstOctave up (same weights, noise and basin factor) stay as detail and the road carving stays on
        /// top. A model with a provider is enabled even when the spec relief is 0. null gives exactly the single-argument constructor.</summary>
        public TerrainModel(WorldSpec spec, Func<double, double, double> baseHeight) : this(spec)
        {
            _base = baseHeight;
            if (baseHeight != null) _enabled = true;
        }

        /// <summary>First hills octave kept as detail under an external base: wavelength BaseWavelengthM / 4 and below.</summary>
        public const int DetailFirstOctave = 2;

        internal void AddSettlement(V2 c, double radius) { _setC.Add(c); _setR.Add(radius); }

        // ---------------------------------------------------------------- natural height

        static double Fade(double t) { return t * t * t * (t * (t * 6 - 15) + 10); }
        static double Lerp(double a, double b, double t) { return a + (b - a) * t; }
        static double Smooth(double t) { t = t < 0 ? 0 : (t > 1 ? 1 : t); return t * t * (3 - 2 * t); }

        double Grad(int h, double x, double z)
        {
            switch (h & 7)
            {
                case 0: return x + z;
                case 1: return -x + z;
                case 2: return x - z;
                case 3: return -x - z;
                case 4: return x * 1.4142135623730951;
                case 5: return -x * 1.4142135623730951;
                case 6: return z * 1.4142135623730951;
                default: return -z * 1.4142135623730951;
            }
        }

        /// <summary>Gradient noise, about [-1, 1].</summary>
        double Noise(double x, double z)
        {
            double fx = Math.Floor(x), fz = Math.Floor(z);
            int xi = (int)(((long)fx) & 255), zi = (int)(((long)fz) & 255);
            double dx = x - fx, dz = z - fz;
            double u = Fade(dx), v = Fade(dz);
            int aa = _perm[_perm[xi] + zi], ab = _perm[_perm[xi] + zi + 1], ba = _perm[_perm[xi + 1] + zi], bb = _perm[_perm[xi + 1] + zi + 1];
            double x1 = Lerp(Grad(aa, dx, dz), Grad(ba, dx - 1, dz), u);
            double x2 = Lerp(Grad(ab, dx, dz - 1), Grad(bb, dx - 1, dz - 1), u);
            return Lerp(x1, x2, v) * 0.7;
        }

        double Fbm(double x, double z, int octaves)
        {
            double sum = 0, amp = 1, norm = 0, f = 1;
            for (int o = 0; o < octaves; o++)
            {
                sum += amp * Noise(x * f + _offX + o * 17.0, z * f + _offZ + o * 31.0);
                norm += amp; amp *= 0.5; f *= 2.0;
            }
            return norm > 0 ? sum / norm : 0;
        }

        // the octaves [first, octaves) of Fbm with Fbm's own weights and normalisation (all octaves), so Fbm = low part + this part
        double FbmFrom(double x, double z, int first, int octaves)
        {
            double sum = 0, amp = 1, norm = 0, f = 1;
            for (int o = 0; o < octaves; o++)
            {
                if (o >= first) sum += amp * Noise(x * f + _offX + o * 17.0, z * f + _offZ + o * 31.0);
                norm += amp; amp *= 0.5; f *= 2.0;
            }
            return norm > 0 ? sum / norm : 0;
        }

        /// <summary>1 far from every settlement, 0 inside radius + margin, smooth in between.</summary>
        double BasinFactor(double x, double z)
        {
            double f = 1;
            for (int i = 0; i < _setC.Count; i++)
            {
                double d = Math.Sqrt((x - _setC[i].X) * (x - _setC[i].X) + (z - _setC[i].Z) * (z - _setC[i].Z));
                double u = (d - (_setR[i] + Spec.SettlementFlatMarginM)) / Math.Max(1.0, Spec.BasinTransitionM);
                f = Math.Min(f, Smooth(u));
            }
            return f;
        }

        /// <summary>True inside the flat basin of a settlement (radius + margin): roads there follow the natural surface closely, no tunnel or viaduct.</summary>
        public bool InBasin(double x, double z)
        {
            for (int i = 0; i < _setC.Count; i++)
            {
                double dx = x - _setC[i].X, dz = z - _setC[i].Z, r = _setR[i] + Spec.SettlementFlatMarginM;
                if (dx * dx + dz * dz < r * r) return true;
            }
            return false;
        }

        double Mountain(double x, double z)
        {
            V2 a = Spec.MountainA, ab = Spec.MountainB - a;
            double l2 = V2.Dot(ab, ab);
            double t = l2 < 1e-9 ? 0 : Math.Max(0, Math.Min(1, ((x - a.X) * ab.X + (z - a.Z) * ab.Z) / l2));
            double px = a.X + ab.X * t - x, pz = a.Z + ab.Z * t - z;
            double d = Math.Sqrt(px * px + pz * pz);
            double u = d / Math.Max(1.0, Spec.MountainWidthM * 0.5);
            if (u >= 1) return 0;
            double q = 1 - u * u;
            double rough = 0.75 + 0.25 * Math.Max(-1, Math.Min(1, 1.6 * Noise(x / 2200.0 + _offX2, z / 2200.0 + _offZ2)));
            return Spec.MountainReliefM * q * q * q * rough;
        }

        /// <summary>Natural terrain height in metres; 0 when the spec is flat.</summary>
        public double Natural(double x, double z) { return NaturalB(x, z, double.NaN); }

        /// <summary>RG-169: Natural with the base value already known (bv = the hook's value at (x, z); NaN = ask the hook). Bit-identical to Natural when bv is the hook's value.</summary>
        double NaturalB(double x, double z, double bv)
        {
            if (!_enabled) return 0;
            double b = BasinFactor(x, z);
            if (_base != null)
                return (double.IsNaN(bv) ? _base(x, z) : bv) + Spec.ReliefM * 0.5 * (0.12 + 0.88 * b) * FbmFrom(x / Math.Max(10.0, Spec.BaseWavelengthM), z / Math.Max(10.0, Spec.BaseWavelengthM), DetailFirstOctave, Math.Max(1, Spec.Octaves)) * 1.6;
            double hills = Spec.ReliefM * 0.5 * (0.12 + 0.88 * b) * Fbm(x / Math.Max(10.0, Spec.BaseWavelengthM), z / Math.Max(10.0, Spec.BaseWavelengthM), Math.Max(1, Spec.Octaves)) * 1.6;
            double m = Spec.HasMountain ? Mountain(x, z) * b : 0;
            return hills + m;
        }

        // ---------------------------------------------------------------- carved terrain

        /// <summary>Registers the ground edges (road y is the authority) and the tunnel portal footprints. Called once, after the graph is final.</summary>
        internal void Bind(RoadGraph g)
        {
            if (!_enabled) return;
            var tunnelEnds = new Dictionary<int, int>();
            foreach (var e in g.Edges)
            {
                if (e.Span == "tunnel")
                {
                    int c;
                    tunnelEnds.TryGetValue(e.A, out c); tunnelEnds[e.A] = c + 1;
                    tunnelEnds.TryGetValue(e.B, out c); tunnelEnds[e.B] = c + 1;
                }
            }
            foreach (var e in g.Edges)
            {
                double hw = e.WidthM * 0.5;
                if (e.Span == "ground")
                {
                    bool capA = g.Nodes[e.A].Kind != NodeKind.Portal, capB = g.Nodes[e.B].Kind != NodeKind.Portal;
                    int first = _segs.Count, count = e.Pts.Length - 1;
                    for (int i = 0; i < count; i++)
                    {
                        var s = new Seg { A = e.Pts[i], B = e.Pts[i + 1], YA = e.H[i], YB = e.H[i + 1], Hw = hw, CapA = i == 0 ? capA : true, CapB = i + 1 == count ? capB : true };
                        s.Prev = i > 0 ? first + i - 1 : -1; s.Next = i + 1 < count ? first + i + 1 : -1;
                        _segIdx.Add(s.A, s.B, _segs.Count);
                        _segs.Add(s);
                    }
                }
                else if (e.Span == "tunnel")
                {
                    // the first / last HoleLeadM metres of a tunnel (measured from the portal node) are open
                    if (tunnelEnds[e.A] == 1) AddHole(e, true, hw + HoleMarginM);
                    if (tunnelEnds[e.B] == 1) AddHole(e, false, hw + HoleMarginM);
                }
            }
        }

        void AddHole(GEdge e, bool fromA, double r)
        {
            double left = HoleLeadM;
            int n = e.Pts.Length;
            for (int k = 0; k + 1 < n && left > 1e-9; k++)
            {
                V2 p = fromA ? e.Pts[k] : e.Pts[n - 1 - k], q = fromA ? e.Pts[k + 1] : e.Pts[n - 2 - k];
                double len = V2.Dist(p, q);
                if (len < 1e-9) continue;
                double use = Math.Min(len, left);
                V2 q2 = p + (q - p) * (use / len);
                var s = new Seg { A = p, B = q2, Hw = r };
                _holeIdx.Add(p, q2, _holeSegs.Count);
                _holeSegs.Add(s);
                left -= use;
            }
        }

        static double Clamp(double v, double lo, double hi) { return v < lo ? lo : (v > hi ? hi : v); }

        static double Param(Seg s, double x, double z)
        {
            double abx = s.B.X - s.A.X, abz = s.B.Z - s.A.Z, l2 = abx * abx + abz * abz;
            return l2 < 1e-12 ? 0 : ((x - s.A.X) * abx + (z - s.A.Z) * abz) / l2;
        }

        double HeightWith(double x, double z, List<int> cand, double bv = double.NaN)
        {
            double n = NaturalB(x, z, bv);
            if (!_enabled) return n;
            double bestD = double.MaxValue, bestY = 0, bestEff = 0; int bestId = -1;
            for (int ci = 0; ci < cand.Count; ci++)
            {
                int id = cand[ci];
                var s = _segs[id];
                double t = Param(s, x, z);
                // beyond an end the segment counts only as a round cap: at an edge end when the road goes on there, and at a joint only inside the wedge of a bend
                // (the point lies behind the next segment's start), never behind a portal
                if (t < 0)
                {
                    if (s.Prev >= 0) { if (Param(_segs[s.Prev], x, z) <= 1) continue; }
                    else if (!s.CapA) continue;
                    t = 0;
                }
                else if (t > 1)
                {
                    if (s.Next >= 0) { if (Param(_segs[s.Next], x, z) >= 0) continue; }
                    else if (!s.CapB) continue;
                    t = 1;
                }
                double qx = s.A.X + (s.B.X - s.A.X) * t - x, qz = s.A.Z + (s.B.Z - s.A.Z) * t - z;
                double dist = Math.Sqrt(qx * qx + qz * qz);
                // the nearest centreline decides (a stem's own centreline always wins over the wider road it joins, so T = road y under every centreline)
                if (dist < bestD || (dist == bestD && id < bestId)) { bestD = dist; bestId = id; bestY = s.YA + (s.YB - s.YA) * t; bestEff = dist - s.Hw; }
            }
            if (bestId < 0) return n;
            double k = Math.Max(0, (bestEff - ShoulderM) / SlopeRun);
            return bestY + Clamp(n - bestY, -k, k);
        }

        static List<int> Scratch() { return _scratch ?? (_scratch = new List<int>()); }

        /// <summary>Carved height T(x,z): the road is the authority inside half width + 1.5 m, then cut / fill at 1:1.5 back to the natural height.</summary>
        public double Height(double x, double z)
        {
            if (!_enabled) return 0;
            var c = Scratch();
            double pad = MaxDiffM * SlopeRun + ShoulderM + 12.0;
            _segIdx.Query(new V2(x - pad, z - pad), new V2(x + pad, z + pad), c);
            return HeightWith(x, z, c);
        }

        // ---- RG-170 batch query: bit-identical to Height; candidate-set reuse per 64 m cell range + an 16 m ring search that stops once the next ring is farther than the best ----
        public const double FineCellM = 16.0;
        public const int GridMinSegments = 400;   // RG-170: segments near the batch from which the 16 m grid beats candidate-set reuse (measured on the planet sectors)

        sealed class CandSet
        {
            public List<int> Ids = new List<int>();
            public int Gx0, Gz0, Nx, Nz;       // fine grid (cells of FineCellM) covering the hash-cell range and every candidate box; Nx = 0: no grid, scan Ids
            public int[] Start, Items;          // CSR: the candidates whose box touches a fine cell
        }

        double[] _bx0, _bx1, _bz0, _bz1;   // per-segment bounding box (lazy; the segment list is final after construction)

        void EnsureBoxes()
        {
            int n = _segs.Count;
            if (_bx0 != null && _bx0.Length == n) return;
            _bx0 = new double[n]; _bx1 = new double[n]; _bz0 = new double[n]; _bz1 = new double[n];
            for (int i = 0; i < n; i++)
            {
                var s = _segs[i];
                _bx0[i] = Math.Min(s.A.X, s.B.X); _bx1[i] = Math.Max(s.A.X, s.B.X);
                _bz0[i] = Math.Min(s.A.Z, s.B.Z); _bz1[i] = Math.Max(s.A.Z, s.B.Z);
            }
        }

        void BuildFine(CandSet cs, double rx0, double rz0, double rx1, double rz1)
        {
            var ids = cs.Ids; double minX = rx0, maxX = rx1, minZ = rz0, maxZ = rz1;
            for (int i = 0; i < ids.Count; i++) { int id = ids[i]; minX = Math.Min(minX, _bx0[id]); maxX = Math.Max(maxX, _bx1[id]); minZ = Math.Min(minZ, _bz0[id]); maxZ = Math.Max(maxZ, _bz1[id]); }
            cs.Gx0 = (int)Math.Floor(minX / FineCellM); cs.Gz0 = (int)Math.Floor(minZ / FineCellM);
            int nx = (int)Math.Floor(maxX / FineCellM) - cs.Gx0 + 1, nz = (int)Math.Floor(maxZ / FineCellM) - cs.Gz0 + 1;
            if ((long)nx * nz > 20000) { cs.Nx = 0; return; }
            cs.Nx = nx; cs.Nz = nz;
            var cnt = new int[nx * nz + 1];
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < ids.Count; i++)
                {
                    int id = ids[i];
                    int x0 = (int)Math.Floor(_bx0[id] / FineCellM) - cs.Gx0, x1 = (int)Math.Floor(_bx1[id] / FineCellM) - cs.Gx0;
                    int z0 = (int)Math.Floor(_bz0[id] / FineCellM) - cs.Gz0, z1 = (int)Math.Floor(_bz1[id] / FineCellM) - cs.Gz0;
                    for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++)
                        { if (pass == 0) cnt[z * nx + x + 1]++; else cs.Items[cnt[z * nx + x]++] = id; }
                }
                if (pass == 0)
                {
                    for (int c = 0; c < nx * nz; c++) cnt[c + 1] += cnt[c];
                    cs.Start = (int[])cnt.Clone(); cs.Items = new int[cnt[nx * nz]];
                }
            }
        }

        // visits one segment: the nearest centreline (ties: lowest id) wins; identical rules to HeightWith
        void Consider(int id, double x, double z, ref double bestD, ref double bestY, ref double bestEff, ref int bestId)
        {
            var s = _segs[id];
            double t = Param(s, x, z);
            if (t < 0)
            {
                if (s.Prev >= 0) { if (Param(_segs[s.Prev], x, z) <= 1) return; }
                else if (!s.CapA) return;
                t = 0;
            }
            else if (t > 1)
            {
                if (s.Next >= 0) { if (Param(_segs[s.Next], x, z) >= 0) return; }
                else if (!s.CapB) return;
                t = 1;
            }
            double qx = s.A.X + (s.B.X - s.A.X) * t - x, qz = s.A.Z + (s.B.Z - s.A.Z) * t - z;
            double dist = Math.Sqrt(qx * qx + qz * qz);
            if (dist < bestD || (dist == bestD && id < bestId)) { bestD = dist; bestId = id; bestY = s.YA + (s.YB - s.YA) * t; bestEff = dist - s.Hw; }
        }

        double HeightRings(double x, double z, CandSet cs, int[] stamp, ref int stampNo, double padM, ref int lastId, out bool ok, double bv = double.NaN)
        {
            double n = 0;
            double bestD = double.MaxValue, bestY = 0, bestEff = 0; int bestId = -1;
            ok = true;
            {
                stampNo++;
                // seed with the previous point's winner (neighbouring lattice points: a tight first bound; visiting order does not change the result)
                if (lastId >= 0) { stamp[lastId] = stampNo; Consider(lastId, x, z, ref bestD, ref bestY, ref bestEff, ref bestId); }
                int nx = cs.Nx, nz = cs.Nz;
                int cx = Math.Min(nx - 1, Math.Max(0, (int)Math.Floor(x / FineCellM) - cs.Gx0)), cz = Math.Min(nz - 1, Math.Max(0, (int)Math.Floor(z / FineCellM) - cs.Gz0));
                int maxK = Math.Max(Math.Max(cx, nx - 1 - cx), Math.Max(cz, nz - 1 - cz));
                for (int k = 0; k <= maxK; k++)
                {
                    // a segment not seen in rings < k lies entirely in rings >= k: its box, hence the segment, is at least (k - 1) cells away (1e-9 slack covers rounding)
                    if (bestId >= 0 && (k - 1) * FineCellM > bestD * (1 + 1e-9) + 1e-9) break;
                    if ((k - 1) * FineCellM > padM + 1e-9) break;   // nothing within the carve box any more: the caller falls back to the exact set path
                    int za = Math.Max(0, cz - k), zb = Math.Min(nz - 1, cz + k), xa = Math.Max(0, cx - k), xb = Math.Min(nx - 1, cx + k);
                    for (int gz = za; gz <= zb; gz++)
                    {
                        bool edgeRow = gz == cz - k || gz == cz + k;
                        for (int gx = xa; gx <= xb; gx++)
                        {
                            if (!edgeRow && gx != cx - k && gx != cx + k) { if (gx < cx + k) gx = Math.Max(gx, cx + k) - 1; continue; }
                            int cell = gz * nx + gx;
                            for (int q = cs.Start[cell]; q < cs.Start[cell + 1]; q++)
                            {
                                int id = cs.Items[q];
                                if (stamp[id] == stampNo) continue;
                                stamp[id] = stampNo;
                                Consider(id, x, z, ref bestD, ref bestY, ref bestEff, ref bestId);
                            }
                        }
                    }
                }
            }
            // the carve set of Height holds every segment within padM of the point: a winner inside padM is also the winner of that set; otherwise ask the set path
            if (bestId < 0 || bestD > padM * (1 - 1e-9)) { ok = false; return 0; }
            lastId = bestId;
            n = NaturalB(x, z, bv);
            double kk = Math.Max(0, (bestEff - ShoulderM) / SlopeRun);
            return bestY + Clamp(n - bestY, -kk, kk);
        }

        /// <summary>outH[i] = Height(xs[i], zs[i]) for i &lt; count, bit for bit. prune = true (compact batches, e.g. one sector): one 16 m grid of the segments near the batch, the
        /// nearest centreline is searched ring by ring until the next ring is farther than the best; a winner inside the carve pad is the winner of Height's own candidate set
        /// (exact); points without one use the set path. The set path (also prune = false, or a batch spread over more than 1200 m): points whose carve box covers the same
        /// hash-cell range share one candidate query. Either way the winner is the minimum of (distance, id), independent of the visiting order.</summary>
        public void HeightBatch(double[] xs, double[] zs, int count, double[] outH, bool prune = true) { HeightBatch(xs, zs, count, outH, null, prune); }

        /// <summary>RG-169 (RG-170 decision B): as HeightBatch, but baseH[i] is the already evaluated base hook value at (xs[i], zs[i]); the carve does not call the hook again.
        /// Output is bit-identical to the hook path when baseH[i] == hook(xs[i], zs[i]). baseH = null: the hook is called per point.</summary>
        public void HeightBatch(double[] xs, double[] zs, int count, double[] outH, double[] baseH, bool prune = true)
        {
            if (!_enabled) { for (int i = 0; i < count; i++) outH[i] = 0; return; }
            double pad = MaxDiffM * SlopeRun + ShoulderM + 12.0;
            CandSet grid = null; int[] stamp = null; int stampNo = 0, lastId = -1;
            if (prune && count > 0)
            {
                double mnx = double.MaxValue, mxx = double.MinValue, mnz = double.MaxValue, mxz = double.MinValue;
                for (int i = 0; i < count; i++) { mnx = Math.Min(mnx, xs[i]); mxx = Math.Max(mxx, xs[i]); mnz = Math.Min(mnz, zs[i]); mxz = Math.Max(mxz, zs[i]); }
                if (mxx - mnx <= 1200 && mxz - mnz <= 1200)
                {
                    EnsureBoxes();
                    grid = new CandSet();
                    _segIdx.Query(new V2(mnx - pad, mnz - pad), new V2(mxx + pad, mxz + pad), grid.Ids);
                    // a sparse neighbourhood (open country, a few roads) is cheaper on the set path; the grid pays off from a dense street net on
                    if (grid.Ids.Count >= GridMinSegments) BuildFine(grid, mnx - pad, mnz - pad, mxx + pad, mxz + pad); else grid.Nx = 0;
                    if (grid.Nx == 0) grid = null; else stamp = new int[_segs.Count];
                }
            }
            Dictionary<long, CandSet> sets = null;
            for (int i = 0; i < count; i++)
            {
                double x = xs[i], z = zs[i];
                if (grid != null) { bool ok; double hv = HeightRings(x, z, grid, stamp, ref stampNo, pad, ref lastId, out ok, baseH == null ? double.NaN : baseH[i]); if (ok) { outH[i] = hv; continue; } }
                if (sets == null) sets = new Dictionary<long, CandSet>();
                int x0 = (int)Math.Floor((x - pad) / IndexCellM), x1 = (int)Math.Floor((x + pad) / IndexCellM);
                int z0 = (int)Math.Floor((z - pad) / IndexCellM), z1 = (int)Math.Floor((z + pad) / IndexCellM);
                long key = (((long)x0 & 0xFFFFFF) << 40) ^ (((long)z0 & 0xFFFFFF) << 16) ^ ((long)(x1 - x0) << 8) ^ (long)(z1 - z0);
                CandSet c;
                if (!sets.TryGetValue(key, out c))
                {
                    c = new CandSet();
                    _segIdx.Query(new V2(x - pad, z - pad), new V2(x + pad, z + pad), c.Ids);
                    sets[key] = c;
                }
                outH[i] = HeightWith(x, z, c.Ids, baseH == null ? double.NaN : baseH[i]);
            }
        }

        /// <summary>True where the terrain is open for a tunnel mouth: the footprint (width + 1 m each side) within 8 m of a portal.</summary>
        public bool Hole(double x, double z)
        {
            if (!_enabled || _holeSegs.Count == 0) return false;
            var c = Scratch();
            _holeIdx.Query(new V2(x - 20, z - 20), new V2(x + 20, z + 20), c);
            return HoleWith(x, z, c);
        }

        bool HoleWith(double x, double z, List<int> cand)
        {
            for (int i = 0; i < cand.Count; i++)
            {
                var s = _holeSegs[cand[i]];
                double t = Param(s, x, z);
                if (t < 0 || t > 1) continue;     // an oriented strip along the first 8 m of the tunnel: no round ends
                double qx = s.A.X + (s.B.X - s.A.X) * t - x, qz = s.A.Z + (s.B.Z - s.A.Z) * t - z;
                if (qx * qx + qz * qz < s.Hw * s.Hw) return true;
            }
            return false;
        }

        /// <summary>Heights and hole flags on an (n+1)^2 grid, n = round(sizeM / stepM). Sample (ix, iz) lies at x0 + sizeM * ix / n, z0 + sizeM * iz / n, so the
        /// border samples of two adjacent tiles are the same point and the same value, bit for bit.</summary>
        public TerrainTile SampleTile(double x0, double z0, double sizeM, double stepM)
        {
            int n = Math.Max(1, (int)Math.Round(sizeM / stepM));
            int w = n + 1;
            var tile = new TerrainTile { N = n, Heights = new float[w * w], Holes = new bool[w * w] };
            if (!_enabled) return tile;
            var cand = new List<int>();
            double pad = MaxDiffM * SlopeRun + ShoulderM + 12.0;
            _segIdx.Query(new V2(x0 - pad, z0 - pad), new V2(x0 + sizeM + pad, z0 + sizeM + pad), cand);
            var hc = new List<int>();
            _holeIdx.Query(new V2(x0 - 20, z0 - 20), new V2(x0 + sizeM + 20, z0 + sizeM + 20), hc);
            for (int iz = 0; iz < w; iz++)
            {
                double z = z0 + sizeM * ((double)iz / n);
                for (int ix = 0; ix < w; ix++)
                {
                    double x = x0 + sizeM * ((double)ix / n);
                    tile.Heights[iz * w + ix] = (float)HeightWith(x, z, cand);
                    if (hc.Count > 0) tile.Holes[iz * w + ix] = HoleWith(x, z, hc);
                }
            }
            return tile;
        }
    }
}
