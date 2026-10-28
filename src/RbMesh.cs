using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using OritasySeries;
using UnityEngine;
using UnityEngine.Rendering;

namespace J35XE
{
    /// <summary>Rb 24 and Rb 28 body meshes, carried in J35XE.ssbag. Not the stock AAM shape.</summary>
    internal static class RbMesh
    {
        const float Len24 = 2.91f, Len28 = 2.18f;

        static SsBag _bag;
        static bool _opened;
        static Mesh _m24, _m28;
        static Texture2D _t24, _t28;
        static Sprite _card;
        static bool _miss24, _miss28;

        internal static void Apply(GameObject root, bool falcon)
        {
            if (root == null || root.transform.Find("J35_RbMark") != null)
                return;
            Mesh mesh = MeshOf(falcon);
            if (mesh == null)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("J-35XE Rb mesh missing: " + (falcon ? "Rb_28" : "Rb_24"));
                return;
            }
            float top = DonorTop(root);
            Shader shader = null;
            MeshRenderer[] old = root.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < old.Length; i++)
            {
                MeshRenderer mr = old[i];
                if (mr == null || Keep(mr.gameObject.name) || mr.sharedMaterial == null)
                    continue;
                if (mr.sharedMaterial.renderQueue >= (int)RenderQueue.Transparent)
                    continue;
                shader = mr.sharedMaterial.shader;
                break;
            }
            Texture2D tex = TexOf(falcon);
            Material mat = new Material(shader != null ? shader : Shader.Find("Standard"));
            mat.name = falcon ? "Rb28" : "Rb24";
            if (tex != null)
            {
                if (mat.HasProperty("_BaseMap"))
                    mat.SetTexture("_BaseMap", tex);
                if (mat.HasProperty("_MainTex"))
                    mat.SetTexture("_MainTex", tex);
            }
            GameObject mark = new GameObject("J35_RbMark");
            mark.transform.SetParent(root.transform, false);
            GameObject vis = new GameObject("J35_RbVis");
            vis.transform.SetParent(root.transform, false);
            vis.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            vis.transform.localPosition = Vector3.zero;
            float len = falcon ? Len28 : Len24;
            float src = mesh.bounds.size.z;
            if (mesh.bounds.size.x > src) src = mesh.bounds.size.x;
            if (mesh.bounds.size.y > src) src = mesh.bounds.size.y;
            Vector3 lossy = root.transform.lossyScale;
            float parent = Mathf.Max(Mathf.Abs(lossy.x), Mathf.Max(Mathf.Abs(lossy.y), Mathf.Abs(lossy.z)));
            if (parent < 0.0001f) parent = 1f;
            float scale = src > 0.01f ? len / (src * parent) : 1f;
            if (scale > 4f) scale = 4f;
            if (scale < 0.05f) scale = 0.05f;
            vis.transform.localScale = new Vector3(scale, scale, scale);
            MeshFilter filter = vis.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer = vis.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            float body = mesh.bounds.extents.y;
            if (mesh.bounds.extents.x < body) body = mesh.bounds.extents.x;
            if (body < 0.02f) body = 0.06f;
            float seat = body * scale;
            if (top > 0.02f) seat = top;
            bool flight = root.GetComponent<Missile>() != null;
            vis.transform.localPosition = new Vector3(0f, flight ? body * scale : seat, 0f);
            for (int i = 0; i < old.Length; i++)
            {
                MeshRenderer mr = old[i];
                if (mr == null || Keep(mr.gameObject.name))
                    continue;
                mr.enabled = false;
            }
        }

        internal static void OnRack(GameObject rack, string key)
        {
            if (rack == null)
                return;
            bool falcon = key == "J35_Rb_28";
            if (key != "J35_Rb_24" && !falcon)
                return;
            Weapon[] weapons = rack.GetComponentsInChildren<Weapon>(true);
            if (weapons == null || weapons.Length == 0)
            {
                Apply(rack, falcon);
                return;
            }
            for (int i = 0; i < weapons.Length; i++)
            {
                Weapon w = weapons[i];
                if (w == null || w is Gun)
                    continue;
                Apply(w.gameObject, falcon);
            }
        }

        static float DonorTop(GameObject root)
        {
            float top = 0f;
            bool any = false;
            MeshRenderer[] rs = root.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < rs.Length; i++)
            {
                MeshRenderer mr = rs[i];
                if (mr == null || Keep(mr.gameObject.name))
                    continue;
                Vector3 local = root.transform.InverseTransformPoint(mr.bounds.max);
                if (!any || local.y > top)
                {
                    top = local.y;
                    any = true;
                }
            }
            return any ? top : 0f;
        }

        static bool Keep(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            if (name.IndexOf("pylon", System.StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("rail", System.StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("rack", System.StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("launcher", System.StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("flame", System.StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("exhaust", System.StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("smoke", System.StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("trail", System.StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("fx", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return false;
        }

        static Mesh MeshOf(bool falcon)
        {
            if (falcon)
            {
                if (_m28 == null && !_miss28)
                    _m28 = Load("Rb_28.obj", out _miss28);
                return _m28;
            }
            if (_m24 == null && !_miss24)
                _m24 = Load("Rb_24.obj", out _miss24);
            return _m24;
        }

        static Texture2D TexOf(bool falcon)
        {
            Open();
            if (_bag == null || !_bag.Ok)
                return null;
            if (falcon)
            {
                if (_t28 == null)
                    _t28 = _bag.Tex("Rb_28.png");
                return _t28;
            }
            if (_t24 == null)
                _t24 = _bag.Tex("Rb_24.png");
            return _t24;
        }

        internal static Sprite Card()
        {
            Open();
            if (_card != null || _bag == null || !_bag.Ok)
                return _card;
            _card = SpriteOf(_bag.Tex("placeholder.png"), "Rb_placeholder");
            return _card;
        }

        static Sprite SpriteOf(Texture2D tex, string name)
        {
            if (tex == null)
                return null;
            Sprite sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = name;
            return sprite;
        }

        static Mesh Load(string name, out bool missing)
        {
            missing = false;
            Open();
            if (_bag == null || !_bag.Ok)
            {
                missing = true;
                return null;
            }
            string src = _bag.Text(name);
            if (string.IsNullOrEmpty(src))
            {
                missing = true;
                return null;
            }
            Mesh mesh = Parse(src, name);
            if (mesh == null)
            {
                missing = true;
                return null;
            }
            Seat(mesh);
            return mesh;
        }

        static void Open()
        {
            if (_opened)
                return;
            _opened = true;
            _bag = SsBag.OpenBeside(Assembly.GetExecutingAssembly().Location, "J35XE.ssbag");
        }

        static Mesh Parse(string src, string name)
        {
            var vp = new List<Vector3>(512);
            var vt = new List<Vector2>(512);
            var verts = new List<Vector3>(1024);
            var uvs = new List<Vector2>(1024);
            var tris = new List<int>(2048);
            var map = new Dictionary<long, int>(1024);
            string[] lines = src.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string s = lines[i].Trim();
                if (s.Length < 3 || s[0] == '#')
                    continue;
                if (s[0] == 'v' && s[1] == ' ')
                {
                    string[] p = s.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length >= 4)
                        vp.Add(new Vector3(Num(p[1]), Num(p[2]), Num(p[3])));
                }
                else if (s[0] == 'v' && s[1] == 't')
                {
                    string[] p = s.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length >= 3)
                        vt.Add(new Vector2(Num(p[1]), Num(p[2])));
                }
                else if (s[0] == 'f' && s[1] == ' ')
                {
                    string[] p = s.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                    int a = Corner(p, 1, vp, vt, verts, uvs, map);
                    int b = Corner(p, 2, vp, vt, verts, uvs, map);
                    for (int k = 3; k < p.Length; k++)
                    {
                        int c = Corner(p, k, vp, vt, verts, uvs, map);
                        if (a >= 0 && b >= 0 && c >= 0)
                        {
                            tris.Add(a);
                            tris.Add(b);
                            tris.Add(c);
                        }
                        b = c;
                    }
                }
            }
            if (verts.Count == 0 || tris.Count == 0)
                return null;
            Mesh mesh = new Mesh { name = name };
            if (verts.Count > 65000)
                mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static int Corner(string[] p, int i, List<Vector3> vp, List<Vector2> vt, List<Vector3> verts, List<Vector2> uvs, Dictionary<long, int> map)
        {
            if (i >= p.Length)
                return -1;
            string t = p[i];
            int sl = t.IndexOf('/');
            string vs = sl < 0 ? t : t.Substring(0, sl);
            string ts = "";
            if (sl >= 0 && sl + 1 < t.Length)
            {
                int sl2 = t.IndexOf('/', sl + 1);
                ts = sl2 < 0 ? t.Substring(sl + 1) : t.Substring(sl + 1, sl2 - sl - 1);
            }
            if (!int.TryParse(vs, out int vi) || vi <= 0 || vi > vp.Count)
                return -1;
            int ti = 0;
            int.TryParse(ts, out ti);
            long key = ((long)vi << 32) | (uint)ti;
            int at;
            if (map.TryGetValue(key, out at))
                return at;
            at = verts.Count;
            verts.Add(vp[vi - 1]);
            uvs.Add(ti > 0 && ti <= vt.Count ? vt[ti - 1] : Vector2.zero);
            map[key] = at;
            return at;
        }

        static void Seat(Mesh mesh)
        {
            Vector3[] v = mesh.vertices;
            if (v == null || v.Length == 0)
                return;
            mesh.RecalculateBounds();
            Vector3 c = mesh.bounds.center;
            for (int i = 0; i < v.Length; i++)
                v[i] -= c;
            mesh.vertices = v;
            mesh.RecalculateBounds();
            float r = mesh.bounds.extents.x;
            if (mesh.bounds.extents.y < r)
                r = mesh.bounds.extents.y;
            if (r < 0.02f)
                r = 0.06f;
            for (int i = 0; i < v.Length; i++)
                v[i].y -= r;
            mesh.vertices = v;
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
        }

        static float Num(string s)
        {
            float v;
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0f;
        }
    }

    [HarmonyPatch(typeof(Hardpoint), "SpawnMount")]
    static class H_RbRack
    {
        static void Postfix(WeaponMount weaponMount, GameObject __result)
        {
            if (weaponMount == null)
                return;
            DrakenArms.Hold(__result, weaponMount);
            RbMesh.OnRack(__result, weaponMount.jsonKey ?? "");
        }
    }
}
