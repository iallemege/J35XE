using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace J35XE
{
    /// <summary>
    /// The J-35's own outline for the MFD: every airframe triangle is projected flat (top view on
    /// X/Z, front view on X/Y) and rasterised into a small grid that remembers which UnitPart owns
    /// each pixel. Edges are pixels next to empty space or to another part.
    /// </summary>
    internal sealed class Planform
    {
        public int W, H;
        public byte[] Mask;
        public bool[] Edge;
        public float Scale, MinU, MaxV;
        public bool Top;

        static readonly FieldInfo DamageMat = AccessTools.Field(typeof(UnitPart), "damageMaterial");

        /// <summary>Grid position of an aircraft-local point.</summary>
        public Vector2 Grid(Vector3 local)
        {
            float v = Top ? local.z : local.y;
            return new Vector2((local.x - MinU) * Scale, (MaxV - v) * Scale);
        }

        public static UnitPart[] Parts(Aircraft ac) => ac.GetComponentsInChildren<UnitPart>(true);

        public static Planform Build(Aircraft ac, UnitPart[] parts, bool top, float maxW, float maxH)
        {
            Transform root = ac.transform;
            Dictionary<Renderer, int> owner = Owners(parts);
            List<Vector2[]> tris = new List<Vector2[]>();
            List<byte> ids = new List<byte>();
            float minU = float.MaxValue, maxU = float.MinValue, minV = float.MaxValue, maxV = float.MinValue;
            MeshRenderer[] rs = ac.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < rs.Length; i++)
            {
                MeshRenderer r = rs[i];
                if (r == null || !r.enabled || Skip(r.transform, root))
                    continue;
                MeshFilter mf = r.GetComponent<MeshFilter>();
                Mesh mesh = mf != null ? mf.sharedMesh : null;
                if (mesh == null || !mesh.isReadable)
                    continue;
                int id;
                if (!owner.TryGetValue(r, out id))
                {
                    UnitPart up = r.GetComponentInParent<UnitPart>();
                    id = up != null ? Array.IndexOf(parts, up) : 0;
                    if (id < 0) id = 0;
                }
                Matrix4x4 m = root.worldToLocalMatrix * r.transform.localToWorldMatrix;
                Vector3[] v = mesh.vertices;
                Vector2[] flat = new Vector2[v.Length];
                for (int k = 0; k < v.Length; k++)
                {
                    Vector3 p = m.MultiplyPoint3x4(v[k]);
                    float u = p.x, w = top ? p.z : p.y;
                    flat[k] = new Vector2(u, w);
                    if (u < minU) minU = u;
                    if (u > maxU) maxU = u;
                    if (w < minV) minV = w;
                    if (w > maxV) maxV = w;
                }
                int[] t = mesh.triangles;
                for (int k = 0; k + 2 < t.Length; k += 3)
                {
                    tris.Add(new[] { flat[t[k]], flat[t[k + 1]], flat[t[k + 2]] });
                    ids.Add((byte)Mathf.Min(id + 1, 254));
                }
            }
            if (tris.Count == 0)
                return null;

            Planform pf = new Planform { Top = top };
            pf.Scale = Mathf.Min(maxW / Mathf.Max(0.1f, maxU - minU), maxH / Mathf.Max(0.1f, maxV - minV));
            pf.MinU = minU;
            pf.MaxV = maxV;
            pf.W = Mathf.CeilToInt((maxU - minU) * pf.Scale) + 1;
            pf.H = Mathf.CeilToInt((maxV - minV) * pf.Scale) + 1;
            pf.Mask = new byte[pf.W * pf.H];
            for (int i = 0; i < tris.Count; i++)
                pf.Fill(tris[i], ids[i]);
            pf.Edges();
            return pf;
        }

        static Dictionary<Renderer, int> Owners(UnitPart[] parts)
        {
            Dictionary<Renderer, int> map = new Dictionary<Renderer, int>();
            if (DamageMat == null)
                return map;
            for (int i = 0; i < parts.Length; i++)
            {
                DamageMaterial dm = DamageMat.GetValue(parts[i]) as DamageMaterial;
                if (dm == null || dm.renderers == null)
                    continue;
                for (int k = 0; k < dm.renderers.Count; k++)
                {
                    if (dm.renderers[k] != null)
                        map[dm.renderers[k]] = i;
                }
            }
            return map;
        }

        static bool Skip(Transform t, Transform root)
        {
            string self = t.name;
            if (self == "fuse_inner" || self == "fuse_inner_rev" || self == "canopy_glass" || self == "glass")
                return true;
            for (Transform p = t; p != null && p != root; p = p.parent)
            {
                string n = p.name;
                if (n.StartsWith("gear_", StringComparison.Ordinal) || n.StartsWith("wheel_", StringComparison.Ordinal)
                    || n.StartsWith("hardpoint", StringComparison.Ordinal) || n.StartsWith("oa_", StringComparison.Ordinal)
                    || n.StartsWith("gauge_", StringComparison.Ordinal) || n.StartsWith("hud_", StringComparison.Ordinal)
                    || n.StartsWith("coffin", StringComparison.Ordinal) || n.StartsWith("jato", StringComparison.Ordinal)
                    || n.StartsWith("afterburner", StringComparison.Ordinal) || n.StartsWith("ab_", StringComparison.Ordinal)
                    || n.StartsWith("heat_haze", StringComparison.Ordinal) || n.StartsWith("flame", StringComparison.Ordinal)
                    || n.StartsWith("fx", StringComparison.Ordinal) || n == "cockpitView")
                    return true;
            }
            return false;
        }

        void Fill(Vector2[] tri, byte id)
        {
            Vector2 a = Grid2(tri[0]), b = Grid2(tri[1]), c = Grid2(tri[2]);
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))));
            int x1 = Mathf.Min(W - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))));
            int y1 = Mathf.Min(H - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));
            float area = Cross(a, b, c);
            if (Mathf.Abs(area) < 1e-6f)
                return;
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                    float w0 = Cross(b, c, p), w1 = Cross(c, a, p), w2 = Cross(a, b, p);
                    bool inside = area > 0f ? (w0 >= 0f && w1 >= 0f && w2 >= 0f) : (w0 <= 0f && w1 <= 0f && w2 <= 0f);
                    if (inside)
                        Mask[y * W + x] = id;
                }
            }
        }

        Vector2 Grid2(Vector2 uv) => new Vector2((uv.x - MinU) * Scale, (MaxV - uv.y) * Scale);

        static float Cross(Vector2 a, Vector2 b, Vector2 p) => (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);

        void Edges()
        {
            Edge = new bool[Mask.Length];
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    byte v = Mask[y * W + x];
                    if (v == 0)
                        continue;
                    Edge[y * W + x] = Diff(x - 1, y, v) || Diff(x + 1, y, v) || Diff(x, y - 1, v) || Diff(x, y + 1, v);
                }
            }
        }

        bool Diff(int x, int y, byte v)
        {
            if (x < 0 || y < 0 || x >= W || y >= H)
                return true;
            return Mask[y * W + x] != v;
        }
    }
}
