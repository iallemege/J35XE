using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace J35XE
{
    /// <summary>
    /// Wing vapor for a hard pull, the same sheet the other jets get from VaporEffect.
    /// The J-35 has no emitters of its own, so this copies a stock particle and hangs
    /// points along both wings. It comes in a little earlier than the stock curve.
    /// </summary>
    internal sealed class WingVapor : MonoBehaviour
    {
        static readonly FieldInfo AircraftField = AccessToolsField(typeof(VaporEffect), "aircraft");
        static readonly FieldInfo EmittersField = AccessToolsField(typeof(VaporEffect), "emitters");
        static readonly FieldInfo ContrailField = AccessToolsField(typeof(VaporEmitter), "contrail");

        static VaporEmitter _shared;
        static float _hunt;

        Aircraft _ac;
        float _next;
        bool _done;

        internal void Init(Aircraft ac)
        {
            _ac = ac;
        }

        void Update()
        {
            if (_done || _ac == null || Time.unscaledTime < _next)
                return;
            _next = Time.unscaledTime + 6f;
            if (_ac.GetComponentInChildren<VaporEffect>(true) != null)
            {
                _done = true;
                return;
            }
            _done = Build();
        }

        bool Build()
        {
            VaporEmitter donor = Donor();
            if (donor == null || donor.particles == null)
                return false;
            Transform left = Rig.Find(_ac.transform, "wing_L");
            Transform right = Rig.Find(_ac.transform, "wing_R");
            if (left == null && right == null)
                return false;
            List<Transform> points = new List<Transform>();
            Points(left, points);
            Points(right, points);
            if (points.Count == 0)
                return false;

            GameObject host = new GameObject("j35_vapor");
            host.transform.SetParent(_ac.transform, false);
            host.SetActive(false);
            GameObject bits = Instantiate(donor.particles.gameObject, host.transform, false);
            bits.name = "j35_vapor_bits";
            ParticleSystem ps = bits.GetComponent<ParticleSystem>();
            if (ps == null)
            {
                Destroy(host);
                return false;
            }
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;

            VaporEmitter wing = new VaporEmitter();
            wing.particles = ps;
            wing.emitTransforms = points.ToArray();
            wing.emitFrequency = donor.emitFrequency > 0.1f ? donor.emitFrequency : 40f;
            wing.minSpeed = Mathf.Max(8f, donor.minSpeed * 0.7f);
            wing.opacity = Mathf.Clamp(donor.opacity * 1.25f, 0.5f, 1.45f);
            wing.alphaThresholdVelocity = Easier(donor.alphaThresholdVelocity);

            VaporEffect effect = host.AddComponent<VaporEffect>();
            if (AircraftField != null)
                AircraftField.SetValue(effect, _ac);
            if (EmittersField != null)
                EmittersField.SetValue(effect, new VaporEmitter[] { wing });
            host.SetActive(true);
            return true;
        }

        static VaporEmitter Donor()
        {
            if (_shared != null && _shared.particles != null)
                return _shared;
            if (Time.unscaledTime < _hunt)
                return null;
            _hunt = Time.unscaledTime + 8f;
            VaporEffect[] all = Resources.FindObjectsOfTypeAll<VaporEffect>();
            if (all == null)
                return null;
            for (int i = 0; i < all.Length; i++)
            {
                VaporEffect effect = all[i];
                if (effect == null || EmittersField == null)
                    continue;
                Aircraft ac = null;
                try { ac = effect.GetComponentInParent<Aircraft>(); }
                catch { }
                if (PitchTune.IsOurs(ac))
                    continue;
                VaporEmitter[] emitters = EmittersField.GetValue(effect) as VaporEmitter[];
                if (emitters == null)
                    continue;
                for (int e = 0; e < emitters.Length; e++)
                {
                    VaporEmitter item = emitters[e];
                    if (item == null || item.particles == null)
                        continue;
                    bool contrail = false;
                    if (ContrailField != null)
                    {
                        try { contrail = (bool)ContrailField.GetValue(item); }
                        catch { }
                    }
                    if (!contrail)
                    {
                        _shared = item;
                        return item;
                    }
                }
            }
            return null;
        }

        static void Points(Transform wing, List<Transform> into)
        {
            if (wing == null)
                return;
            Renderer renderer = wing.GetComponentInChildren<Renderer>();
            if (renderer == null)
                return;
            Mesh mesh = null;
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter != null)
                mesh = filter.sharedMesh;
            Bounds box = mesh != null ? mesh.bounds : renderer.localBounds;
            bool spanX = box.size.x >= box.size.z;
            for (int i = 0; i < 5; i++)
            {
                float t = 0.12f + 0.76f * i / 4f;
                Vector3 local = box.center;
                if (spanX)
                    local.x = Mathf.Lerp(box.min.x, box.max.x, t);
                else
                    local.z = Mathf.Lerp(box.min.z, box.max.z, t);
                local.y = box.max.y;
                GameObject point = new GameObject("vapor_pt");
                point.transform.SetParent(wing, false);
                point.transform.position = renderer.transform.TransformPoint(local);
                into.Add(point.transform);
            }
        }

        static AnimationCurve Easier(AnimationCurve src)
        {
            if (src == null || src.length == 0)
            {
                return new AnimationCurve(
                    new Keyframe(0f, 28f),
                    new Keyframe(80f, 14f),
                    new Keyframe(160f, 8f),
                    new Keyframe(320f, 5f));
            }
            Keyframe[] keys = src.keys;
            for (int i = 0; i < keys.Length; i++)
            {
                Keyframe key = keys[i];
                key.value *= 0.72f;
                if (key.value < 4f)
                    key.value = 4f;
                keys[i] = key;
            }
            return new AnimationCurve(keys);
        }

        static FieldInfo AccessToolsField(System.Type type, string name)
        {
            return type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }
    }
}
