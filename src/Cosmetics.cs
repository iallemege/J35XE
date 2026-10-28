using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace J35XE
{
    /// <summary>
    /// Fixes on the frozen BIA 1.0.29 cockpit and skin for the J-35XE:
    /// BIADrakenGauges reads a Turbojet field "RPMRatio" that does not exist, so the RPM needle
    /// and the EGT needle it feeds never moved; drive both after BIA has written them.
    /// Door panels, elevons and the wing skin are single sheets drawn two-sided with the top-side
    /// normal, so from below they looked lit through. Give every such sheet a reversed back face
    /// with its own normal and draw front faces only.
    /// </summary>
    [DefaultExecutionOrder(32000)]
    internal sealed class Cosmetics : MonoBehaviour
    {
        const int KindRpm = 5, KindEgt = 6;
        const float NeedleLag = 6f;

        static readonly Dictionary<Mesh, Mesh> Doubled = new Dictionary<Mesh, Mesh>();

        Aircraft _ac;
        Turbojet _jet;
        JetNozzle _nozzle;
        FieldInfo _abList, _abAmount;
        Transform _rpm, _egt;
        float _rpmVal, _egtVal;

        internal void Init(Aircraft ac)
        {
            _ac = ac;
            _jet = ac.GetComponentInChildren<Turbojet>(true);
            _nozzle = ac.GetComponentInChildren<JetNozzle>(true);
            _abList = typeof(JetNozzle).GetField("afterburners", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            FindNeedles();
            StartCoroutine(BackFaces());
        }

        void FindNeedles()
        {
            Component[] all = _ac.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Component c = all[i];
                if (c == null || c.GetType().Name != "BIADrakenGauges")
                    continue;
                Transform[] needles = c.GetType().GetField("needles")?.GetValue(c) as Transform[];
                int[] kinds = c.GetType().GetField("kinds")?.GetValue(c) as int[];
                if (needles == null || kinds == null)
                    return;
                for (int n = 0; n < Mathf.Min(needles.Length, kinds.Length); n++)
                {
                    if (kinds[n] == KindRpm) _rpm = needles[n];
                    else if (kinds[n] == KindEgt) _egt = needles[n];
                }
                return;
            }
        }

        void LateUpdate()
        {
            if (_jet == null || (_rpm == null && _egt == null))
                return;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            float rpm = Mathf.Clamp01(_jet.GetRPMRatio());
            float egt = rpm < 0.05f ? 20f : 280f + 420f * rpm + 170f * Afterburner();
            _rpmVal = Mathf.Lerp(_rpmVal, rpm * 100f, 1f - Mathf.Exp(-NeedleLag * dt));
            _egtVal = Mathf.Lerp(_egtVal, egt, 1f - Mathf.Exp(-0.8f * dt));
            // Same dial scales as BIADrakenGauges.
            if (_rpm != null)
                _rpm.localRotation = Quaternion.Euler(0f, 0f, -(-150f + Mathf.Clamp(_rpmVal, 0f, 110f) / 110f * 300f));
            if (_egt != null)
                _egt.localRotation = Quaternion.Euler(0f, 0f, -(-135f + Mathf.Clamp(_egtVal, 0f, 900f) / 900f * 270f));
        }

        float Afterburner()
        {
            IList list = _nozzle != null && _abList != null ? _abList.GetValue(_nozzle) as IList : null;
            if (list == null || list.Count < 1 || list[0] == null)
                return 0f;
            if (_abAmount == null)
                _abAmount = list[0].GetType().GetField("afterburnerAmount");
            return _abAmount != null ? Convert.ToSingle(_abAmount.GetValue(list[0])) : 0f;
        }

        /// <summary>Runs after BIAAircraftSkin.Start, which already doubled the meshes it reskinned.</summary>
        IEnumerator BackFaces()
        {
            yield return null;
            yield return null;
            MeshRenderer[] rs = _ac.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < rs.Length; i++)
            {
                MeshRenderer r = rs[i];
                if (r == null || Skip(r.transform))
                    continue;
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null || mf.sharedMesh.name.EndsWith("_2s", StringComparison.Ordinal))
                    continue;
                Mesh two = TwoSided(mf.sharedMesh);
                if (two == null)
                    continue;
                mf.sharedMesh = two;
                Material[] mats = r.materials;
                for (int m = 0; m < mats.Length; m++)
                {
                    if (mats[m] != null && mats[m].HasProperty("_Cull"))
                        mats[m].SetFloat("_Cull", 2f);
                }
                r.materials = mats;
            }
            MachRings();
        }

        /// <summary>
        /// The Mach rings stand across the exhaust and are not billboarded like the diamonds, but their
        /// glow material culls back faces, so they only showed from in front. Each ring gets its own
        /// material with culling off (or a two-sided disc if the shader has no cull switch).
        /// </summary>
        void MachRings()
        {
            MeshRenderer[] rs = _ac.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < rs.Length; i++)
            {
                MeshRenderer r = rs[i];
                if (r == null || !r.name.StartsWith("ab_ring_", StringComparison.Ordinal))
                    continue;
                Material[] mats = r.materials;
                bool culled = false;
                for (int m = 0; m < mats.Length; m++)
                {
                    if (mats[m] != null && mats[m].HasProperty("_Cull"))
                        mats[m].SetFloat("_Cull", 0f);
                    else
                        culled = true;
                }
                r.materials = mats;
                if (!culled)
                    continue;
                MeshFilter mf = r.GetComponent<MeshFilter>();
                Mesh two = mf != null && mf.sharedMesh != null ? TwoSided(mf.sharedMesh) : null;
                if (two != null)
                    mf.sharedMesh = two;
            }
        }

        static bool Skip(Transform t)
        {
            string self = t.name;
            if (self == "fuse_inner" || self == "fuse_inner_rev" || self == "canopy_glass" || self == "glass"
                || self.EndsWith("_light", StringComparison.Ordinal))
                return true;
            for (Transform p = t; p != null; p = p.parent)
            {
                string n = p.name;
                if (n.StartsWith("oa_", StringComparison.Ordinal) || n.StartsWith("hud_", StringComparison.Ordinal)
                    || n.StartsWith("gauge_", StringComparison.Ordinal) || n.StartsWith("coffin", StringComparison.Ordinal)
                    || n.StartsWith("jato", StringComparison.Ordinal) || n.StartsWith("afterburner", StringComparison.Ordinal)
                    || n.StartsWith("ab_", StringComparison.Ordinal) || n.StartsWith("heat_haze", StringComparison.Ordinal)
                    || n.StartsWith("flame", StringComparison.Ordinal) || n.StartsWith("fx", StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        static Mesh TwoSided(Mesh src)
        {
            Mesh done;
            if (Doubled.TryGetValue(src, out done) && done != null)
                return done;
            if (!src.isReadable)
                return null;
            int n = src.vertexCount;
            Vector3[] v = src.vertices;
            Vector3[] nr = src.normals;
            Vector4[] tg = src.tangents;
            Vector2[] uv = src.uv;
            Vector2[] uv2 = src.uv2;
            Color[] col = src.colors;
            Mesh mesh = new Mesh();
            mesh.name = src.name + "_2s";
            mesh.indexFormat = n * 2 > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.vertices = Twice(v, x => x);
            if (nr != null && nr.Length == n)
                mesh.normals = Twice(nr, x => -x);
            if (tg != null && tg.Length == n)
                mesh.tangents = Twice(tg, x => new Vector4(x.x, x.y, x.z, -x.w));
            if (uv != null && uv.Length == n)
                mesh.uv = Twice(uv, x => x);
            if (uv2 != null && uv2.Length == n)
                mesh.uv2 = Twice(uv2, x => x);
            if (col != null && col.Length == n)
                mesh.colors = Twice(col, x => x);
            mesh.subMeshCount = src.subMeshCount;
            for (int s = 0; s < src.subMeshCount; s++)
            {
                int[] t = src.GetTriangles(s);
                int[] both = new int[t.Length * 2];
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    both[i] = t[i];
                    both[i + 1] = t[i + 1];
                    both[i + 2] = t[i + 2];
                    both[t.Length + i] = t[i] + n;
                    both[t.Length + i + 1] = t[i + 2] + n;
                    both[t.Length + i + 2] = t[i + 1] + n;
                }
                mesh.SetTriangles(both, s);
            }
            mesh.bounds = src.bounds;
            Doubled[src] = mesh;
            return mesh;
        }

        static T[] Twice<T>(T[] a, Func<T, T> back)
        {
            T[] r = new T[a.Length * 2];
            for (int i = 0; i < a.Length; i++)
            {
                r[i] = a[i];
                r[a.Length + i] = back(a[i]);
            }
            return r;
        }
    }
}
