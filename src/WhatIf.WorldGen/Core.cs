using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WhatIf.WorldGen
{
    /// <summary>SplitMix64. Never System.Random: its sequence differs across runtimes. One stream per stage, derived from seed + stage name.</summary>
    public sealed class Rng
    {
        ulong _s;
        public Rng(ulong seed) { _s = seed; }

        /// <summary>FNV-1a of the stage name mixed into the seed, so a change in one stage never reshuffles the others.</summary>
        public static Rng ForStage(long seed, string stage)
        {
            ulong h = 14695981039346656037UL;
            foreach (char c in stage) { h ^= c; h *= 1099511628211UL; }
            var m = new Rng((ulong)seed ^ h);
            m.NextU64();
            return m;
        }

        public ulong NextU64()
        {
            ulong z = (_s += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>Uniform in [0,1) with 53 bits.</summary>
        public double NextDouble() { return (NextU64() >> 11) * (1.0 / 9007199254740992.0); }
        public double Range(double a, double b) { return a + (b - a) * NextDouble(); }
        public int Int(int n) { return (int)(NextU64() % (ulong)n); }
        public bool Chance(double p) { return NextDouble() < p; }

        /// <summary>Standard normal (Box-Muller).</summary>
        public double Normal()
        {
            double u = Math.Max(NextDouble(), 1e-12), v = NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u)) * Math.Cos(2.0 * Math.PI * v);
        }
    }

    public struct V2
    {
        public double X, Z;
        public V2(double x, double z) { X = x; Z = z; }
        public static V2 operator +(V2 a, V2 b) { return new V2(a.X + b.X, a.Z + b.Z); }
        public static V2 operator -(V2 a, V2 b) { return new V2(a.X - b.X, a.Z - b.Z); }
        public static V2 operator *(V2 a, double s) { return new V2(a.X * s, a.Z * s); }
        public double Len { get { return Math.Sqrt(X * X + Z * Z); } }
        public static double Dot(V2 a, V2 b) { return a.X * b.X + a.Z * b.Z; }
        public static double Cross(V2 a, V2 b) { return a.X * b.Z - a.Z * b.X; }
        public static double Dist(V2 a, V2 b) { return (a - b).Len; }
        public V2 Unit { get { double l = Len; return l < 1e-12 ? new V2(1, 0) : new V2(X / l, Z / l); } }
        /// <summary>Rotated counter-clockwise (x east, z north) by a radians.</summary>
        public V2 Rot(double a) { double c = Math.Cos(a), s = Math.Sin(a); return new V2(X * c - Z * s, X * s + Z * c); }
        public V2 Left { get { return new V2(-Z, X); } }
        /// <summary>Heading angle, radians, atan2(z, x).</summary>
        public double Heading { get { return Math.Atan2(Z, X); } }
        public static V2 FromHeading(double h) { return new V2(Math.Cos(h), Math.Sin(h)); }
        public static double AngDiff(double a, double b)
        {
            double d = (a - b) % (2 * Math.PI);
            if (d > Math.PI) d -= 2 * Math.PI;
            if (d < -Math.PI) d += 2 * Math.PI;
            return d;
        }
    }

    /// <summary>Segment geometry helpers shared by generator and validators.</summary>
    public static class Geo
    {
        /// <summary>Proper intersection of segments ab and cd (touching ends and parallel overlaps are not crossings). t,u in (0,1), or in [0,1) with halfOpen so that a hit exactly on a shared vertex is counted once.</summary>
        public static bool SegCross(V2 a, V2 b, V2 c, V2 d, out double t, out double u, bool halfOpen = false)
        {
            t = u = 0;
            V2 r = b - a, s = d - c;
            double den = V2.Cross(r, s);
            // parallel or (numerically) collinear: a head-on join of two roads at a shared node must not read as a crossing (RG-116)
            if (Math.Abs(den) < 1e-12 || Math.Abs(den) < 1e-9 * r.Len * s.Len) return false;
            V2 ac = c - a;
            t = V2.Cross(ac, s) / den;
            u = V2.Cross(ac, r) / den;
            if (halfOpen) return t >= -1e-9 && t < 1 - 1e-9 && u >= -1e-9 && u < 1 - 1e-9;
            return t > 1e-9 && t < 1 - 1e-9 && u > 1e-9 && u < 1 - 1e-9;
        }

        public static double Round(double v, double q) { return Math.Round(v / q) * q; }
    }

    /// <summary>Minimal JSON tree (object = SortedDictionary-free ordered list, array = List, number = double, string, bool, null) + writer/parser.
    /// The writer is canonical: fixed key order as given, invariant culture, numbers rounded by the caller. Same input -> same bytes.</summary>
    public sealed class JObj
    {
        public readonly List<KeyValuePair<string, object>> Items = new List<KeyValuePair<string, object>>();
        public JObj Add(string k, object v) { Items.Add(new KeyValuePair<string, object>(k, v)); return this; }
        public object Get(string k) { foreach (var kv in Items) if (kv.Key == k) return kv.Value; return null; }
        public bool Has(string k) { return Get(k) != null; }
    }

    public static class Json
    {
        public static string Write(object v, bool indent = false)
        {
            var sb = new StringBuilder();
            WriteVal(sb, v, indent ? 0 : -1);
            return sb.ToString();
        }

        static void NL(StringBuilder sb, int ind) { if (ind >= 0) { sb.Append('\n'); sb.Append(' ', ind * 1); } }

        static void WriteVal(StringBuilder sb, object v, int ind)
        {
            switch (v)
            {
                case null: sb.Append("null"); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case string s: WriteStr(sb, s); break;
                case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); break;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); break;
                case double d:
                    if (double.IsNaN(d) || double.IsInfinity(d)) throw new InvalidOperationException("non-finite number in JSON output");
                    if (d == Math.Floor(d) && Math.Abs(d) < 1e15) sb.Append(((long)d).ToString(CultureInfo.InvariantCulture));
                    else sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case JObj o:
                    sb.Append('{');
                    for (int k = 0; k < o.Items.Count; k++)
                    {
                        if (k > 0) sb.Append(',');
                        if (ind >= 0) NL(sb, ind + 1);
                        WriteStr(sb, o.Items[k].Key); sb.Append(':');
                        WriteVal(sb, o.Items[k].Value, ind >= 0 ? ind + 1 : -1);
                    }
                    if (ind >= 0 && o.Items.Count > 0) NL(sb, ind);
                    sb.Append('}');
                    break;
                case System.Collections.IEnumerable e:
                    sb.Append('[');
                    bool first = true;
                    foreach (var x in e)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        if (ind >= 0 && x is JObj) NL(sb, ind + 1);
                        WriteVal(sb, x, ind >= 0 ? ind + 1 : -1);
                    }
                    if (ind >= 0 && !first && v is List<JObj>) NL(sb, ind);
                    sb.Append(']');
                    break;
                default: throw new InvalidOperationException("unsupported JSON value " + v.GetType().Name);
            }
        }

        static void WriteStr(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        /// <summary>Parses JSON into JObj / List&lt;object&gt; / double / string / bool / null. Throws FormatException on bad input.</summary>
        public static object Parse(string text)
        {
            int p = 0;
            object v = ParseVal(text, ref p);
            Skip(text, ref p);
            if (p != text.Length) throw new FormatException("trailing characters at " + p);
            return v;
        }

        static void Skip(string t, ref int p) { while (p < t.Length && char.IsWhiteSpace(t[p])) p++; }

        static object ParseVal(string t, ref int p)
        {
            Skip(t, ref p);
            if (p >= t.Length) throw new FormatException("unexpected end");
            char c = t[p];
            if (c == '{')
            {
                p++;
                var o = new JObj();
                Skip(t, ref p);
                if (t[p] == '}') { p++; return o; }
                while (true)
                {
                    Skip(t, ref p);
                    string k = ParseStr(t, ref p);
                    Skip(t, ref p);
                    if (t[p++] != ':') throw new FormatException("expected ':' at " + p);
                    o.Add(k, ParseVal(t, ref p));
                    Skip(t, ref p);
                    if (t[p] == ',') { p++; continue; }
                    if (t[p] == '}') { p++; return o; }
                    throw new FormatException("expected ',' or '}' at " + p);
                }
            }
            if (c == '[')
            {
                p++;
                var a = new List<object>();
                Skip(t, ref p);
                if (t[p] == ']') { p++; return a; }
                while (true)
                {
                    a.Add(ParseVal(t, ref p));
                    Skip(t, ref p);
                    if (t[p] == ',') { p++; continue; }
                    if (t[p] == ']') { p++; return a; }
                    throw new FormatException("expected ',' or ']' at " + p);
                }
            }
            if (c == '"') return ParseStr(t, ref p);
            if (string.CompareOrdinal(t, p, "true", 0, 4) == 0) { p += 4; return true; }
            if (string.CompareOrdinal(t, p, "false", 0, 5) == 0) { p += 5; return false; }
            if (string.CompareOrdinal(t, p, "null", 0, 4) == 0) { p += 4; return null; }
            int s = p;
            while (p < t.Length && "+-0123456789.eE".IndexOf(t[p]) >= 0) p++;
            if (s == p) throw new FormatException("unexpected '" + c + "' at " + p);
            return double.Parse(t.Substring(s, p - s), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        static string ParseStr(string t, ref int p)
        {
            if (t[p] != '"') throw new FormatException("expected string at " + p);
            p++;
            var sb = new StringBuilder();
            while (t[p] != '"')
            {
                char c = t[p++];
                if (c != '\\') { sb.Append(c); continue; }
                char e = t[p++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u': sb.Append((char)Convert.ToInt32(t.Substring(p, 4), 16)); p += 4; break;
                    default: sb.Append(e); break;
                }
            }
            p++;
            return sb.ToString();
        }

        // ---- lookup helpers for rules/spec readers ----
        public static object Path(object root, params string[] keys)
        {
            object cur = root;
            foreach (var k in keys)
            {
                var o = cur as JObj;
                if (o == null) return null;
                cur = o.Get(k);
            }
            return cur;
        }

        public static double Num(object root, double dflt, params string[] keys)
        {
            object v = Path(root, keys);
            return v is double d ? d : dflt;
        }
    }
}
