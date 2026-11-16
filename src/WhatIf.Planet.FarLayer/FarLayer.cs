using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace WhatIf.Planet.FarLayer
{
    /// <summary>
    /// Far layer of the planet: CDLOD selection over the six cube-face quadtrees (Strugar), bounded LRU node cache, budgeted node builds.
    /// Split rule: node l splits when min(distance(camera), distance(ground point)) to its shell cap is below LodRange(l) (the ground-point term is the
    /// "distance to the near set": it grades the quadtree around the hole, so the neighbour-level rule holds there too), or when it contains a near sector.
    /// Single thread, not re-entrant. Height source: Func (PlanetSurface.Height).
    /// </summary>
    public sealed class FarLayer
    {
        public const double R = CubeSphere.Radius;
        public readonly FarParams P;
        readonly Func<Vec3, double> height;
        readonly Dictionary<long, FarNode> nodes = new Dictionary<long, FarNode>();
        readonly LinkedList<FarNode> lru = new LinkedList<FarNode>();
        int frame, built;
        // per-Select context
        bool horizonOn; FarCamera cam; Vec3 camDir, gndDir; double camR, gndR, cosHc, sinHc; NearSet near; FrameSelection sel; bool useFrustum, wantRequests;
        Vec3 right, up; double tv;
        public int NodeCount => nodes.Count; public int BuiltCount => built; public int Frame => frame;

        public FarLayer(Func<Vec3, double> heightFunc, FarParams p = null) { height = heightFunc; P = p ?? new FarParams(); }

        // ---------------- cache ----------------
        public FarNode Node(int face, int level, int row, int col)
        {
            long key = NodeKey.Pack(face, level, row, col); FarNode n;
            if (nodes.TryGetValue(key, out n)) { Touch(n); return n; }
            double lo = -5000, hi = 5000;
            if (level > 0) { FarNode p = Node(face, level - 1, row >> 1, col >> 1); lo = p.HMin; hi = p.HMax; }
            n = new FarNode(face, level, row, col, lo, hi); nodes[key] = n;
            if (level > P.PinLevel) n.Lru = lru.AddFirst(n);
            n.Stamp = frame; return n;
        }
        void Touch(FarNode n) { n.Stamp = frame; if (n.Lru != null && n.Lru != lru.First) { lru.Remove(n.Lru); lru.AddFirst(n.Lru); } }
        void Trim()
        {
            while (nodes.Count > P.MaxNodes && lru.Last != null && lru.Last.Value.Stamp < frame)
            {
                FarNode n = lru.Last.Value; lru.RemoveLast(); n.Lru = null; nodes.Remove(n.Key); if (n.Built) built--;
            }
        }

        /// <summary>Builds the vertex heights (289 Height calls) and tightens the bounds.</summary>
        public void Build(FarNode n)
        {
            if (n.Built) return;
            var h = new double[FarNode.Verts * FarNode.Verts]; var u = new double[FarNode.Verts]; var v = new double[FarNode.Verts];
            for (int k = 0; k < FarNode.Verts; k++) { u[k] = Lattice.Coord(n.I0 + k * n.Step); v[k] = Lattice.Coord(n.J0 + k * n.Step); }
            double lo = double.MaxValue, hi = double.MinValue; int q = 0;
            for (int j = 0; j < FarNode.Verts; j++)
                for (int i = 0; i < FarNode.Verts; i++, q++)
                {
                    int I = n.I0 + i * n.Step, J = n.J0 + j * n.Step;
                    Vec3 d = Lattice.OnEdge(I, J) ? Lattice.Direction(n.Face, I, J) : Lattice.Compose(n.Face, u[i], v[j]);
                    double e = height(d); h[q] = e; if (e < lo) lo = e; if (e > hi) hi = e;
                }
            n.H = h; n.SetRange(lo, hi); built++;
        }

        /// <summary>Prebuilds the 6 roots and levels 1..maxLevel (the start-up set; they stay pinned).</summary>
        public int Prebuild(int maxLevel)
        {
            int c = 0;
            for (int l = 0; l <= maxLevel; l++) { int n = 1 << l; for (int f = 0; f < 6; f++) for (int r = 0; r < n; r++) for (int cc = 0; cc < n; cc++) { var nd = Node(f, l, r, cc); if (!nd.Built) { Build(nd); c++; } } }
            return c;
        }

        /// <summary>Builds queued requests (coarse levels first, then nearest) until the budget is spent; returns the number built. budgetMs <= 0: build everything.</summary>
        public int BuildPending(FrameSelection s, double budgetMs)
        {
            s.Requests.Sort((a, b) => a.Node.Level != b.Node.Level ? a.Node.Level.CompareTo(b.Node.Level) : a.Dist.CompareTo(b.Dist));
            var sw = Stopwatch.StartNew(); int c = 0; double last = 0;
            foreach (var r in s.Requests)
            {
                if (r.Node.Built) continue;
                if (budgetMs > 0 && sw.Elapsed.TotalMilliseconds + last > budgetMs) break;
                double t0 = sw.Elapsed.TotalMilliseconds; Build(r.Node); last = sw.Elapsed.TotalMilliseconds - t0; c++;
            }
            return c;
        }

        // ---------------- selection ----------------
        /// <summary>CDLOD selection + hole masks. frustum: also drop nodes outside the 90 x 59 degree frustum (count only; request = false then). requests: queue unbuilt nodes in s.Requests.</summary>
        public void Select(in FarCamera camera, NearSet nearSet, FrameSelection result, bool frustum = false, bool requests = true)
        {
            frame++; cam = camera; near = nearSet; sel = result; useFrustum = frustum; wantRequests = requests; sel.Clear();
            camR = cam.Pos.Length; camDir = cam.Pos / camR; gndR = cam.Ground.Length; gndDir = cam.Ground / gndR;
            horizonOn = camR > R;       // below the sea-level sphere (ocean floor, test heights < 0) nothing is culled by the horizon
            cosHc = camR > R ? R / camR : 1.0; sinHc = Math.Sqrt(Math.Max(0, 1 - cosHc * cosHc));
            if (frustum) { right = Vec3.Cross(cam.Fwd, camDir).Normalized(); up = camDir; tv = 9.0 / 16.0; }
            for (int f = 0; f < 6; f++) if (!Visit(Node(f, 0, 0, 0))) sel.IncompleteRoots++;
            foreach (var s in sel.Nodes)
            {
                int masked = 0; for (int b = 0; b < 16; b++) if ((s.Mask >> b & 1) != 0) masked++;
                sel.MaskedQuads += masked * 16; sel.FarTriangles += 2 * (FarNode.Grid * FarNode.Grid - masked * 16);
            }
            sel.SkirtTriangles = near.SkirtQuads * 2;
            Trim();
        }

        /// <summary>Distance (m) of point p (unit direction pd, radius pr) to the node's shell cap; cosMin = cos of the angular distance to the cap (1 inside).</summary>
        static double ShellDistance(FarNode n, Vec3 pd, double pr, out double cosMin)
        {
            double c = Vec3.Dot(pd, n.CentreDir);
            if (c >= n.CosA) { cosMin = 1.0; return pr < n.Rlo ? n.Rlo - pr : (pr > n.Rhi ? pr - n.Rhi : 0.0); }
            double s = Math.Sqrt(Math.Max(0, 1 - c * c)); cosMin = c * n.CosA + s * n.SinA;
            double rs = Math.Min(n.Rhi, Math.Max(n.Rlo, pr * cosMin));
            return Math.Sqrt(Math.Max(0, pr * pr + rs * rs - 2 * pr * rs * cosMin));
        }

        bool Culled(FarNode n, double cosMinCam, double slackRad = 0.0)
        {
            // horizon: angular distance to the cap > horizon angle of the camera sphere + horizon angle of the node's highest point (sea level counts as 0)
            double cosLim = cosHc * n.CosHz - sinHc * n.SinHz;
            if (slackRad > 0) cosLim -= slackRad * Math.Sqrt(Math.Max(0, 1 - cosLim * cosLim));     // cos(a + s) ~ cos a - s sin a
            if (horizonOn && cosMinCam < cosLim - P.HorizonSlack) return true;
            if (!useFrustum) return false;
            Vec3 q = n.Origin - cam.Pos; double x = Vec3.Dot(q, right), y = Vec3.Dot(q, up), z = Vec3.Dot(q, cam.Fwd), rho = n.SphereR;
            const double th = 1.0; double nh = 1.0 / Math.Sqrt(1 + th * th), nv = 1.0 / Math.Sqrt(1 + tv * tv);
            return (x + z * th) * nh < -rho || (-x + z * th) * nh < -rho || (y + z * tv) * nv < -rho || (-y + z * tv) * nv < -rho;
        }

        // true = the node's area is covered by built nodes (or culled); false = some part has no built node
        // draw = false: prefetch only (the node is just beyond the horizon): requests are queued, nothing is drawn, the area needs no coverage
        bool Visit(FarNode n, bool draw = true)
        {
            sel.Visited++;
            double cm, dc = ShellDistance(n, camDir, camR, out cm);
            if (Culled(n, cm))
            {
                if (!draw || !wantRequests || Culled(n, cm, P.PrefetchRad)) return true;
                draw = false;
            }
            double cg, dg = ShellDistance(n, gndDir, gndR, out cg), d = Math.Min(dc, dg);
            bool forced = n.Level <= P.MaxLevel && near.Ancestors.Contains(n.Key);
            bool split = n.Level < P.MaxLevel && (d < P.LodRange(n.Level) || forced);
            if (!split)
            {
                if (n.Built) { ushort m = 0; if (draw && n.Level == 13) near.Masks.TryGetValue(n.Key, out m); if (draw) sel.Nodes.Add(new SelectedNode { Node = n, Mask = m }); return true; }
                Request(n, d); return !draw;
            }
            int mark = sel.Nodes.Count; bool all = true;
            for (int k = 0; k < 4; k++) all &= Visit(Node(n.Face, n.Level + 1, n.Row * 2 + (k >> 1), n.Col * 2 + (k & 1)), draw);
            if (!draw) { Request(n, d); return true; }      // prefetch: the whole chain down to the leaves gets built ahead
            if (all) return true;
            if (n.Built && !forced) { sel.Nodes.RemoveRange(mark, sel.Nodes.Count - mark); sel.Nodes.Add(new SelectedNode { Node = n }); sel.Fallbacks++; return true; }
            Request(n, d); return false;
        }

        void Request(FarNode n, double d)
        {
            if (!wantRequests || n.ReqStamp == frame || n.Built) return;
            n.ReqStamp = frame; sel.Requests.Add(new BuildRequest { Node = n, Dist = d });
        }
    }
}
