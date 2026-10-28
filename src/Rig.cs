using System;
using UnityEngine;

namespace J35XE
{
    /// <summary>Puts the J-35 cockpit pieces on a freshly woken aircraft.</summary>
    internal static class Rig
    {
        internal static void Attach(Aircraft ac)
        {
            if (!PitchTune.IsOurs(ac))
                return;
            Add<RoundMfd>(ac, c => c.Init(ac));
            Add<CoffinMarks>(ac, c => c.Init(ac));
            Add<Ohmd>(ac, c => c.Init(ac));
            Add<ThrottleTape>(ac, c => c.Init(ac));
            Add<HudHide>(ac, c => c.Init(ac));
            Add<CockpitPanel>(ac, c => c.Init(ac));
            Add<Sgloc>(ac, c => c.Init(ac));
            Add<WingVapor>(ac, c => c.Init(ac));
        }

        static void Add<T>(Aircraft ac, Action<T> init) where T : Component
        {
            try
            {
                if (ac.GetComponent<T>() == null)
                    init(ac.gameObject.AddComponent<T>());
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("J-35XE " + typeof(T).Name + ": " + ex.Message);
            }
        }

        internal static bool Local(Aircraft ac)
        {
            if (ac == null)
                return false;
            try { return GameManager.GetLocalAircraft(out Aircraft local) && local == ac; }
            catch { return false; }
        }

        internal static bool Cockpit()
        {
            try { return CameraStateManager.cameraMode == CameraMode.cockpit; }
            catch { return false; }
        }

        internal static Transform Find(Transform t, string name)
        {
            if (t == null)
                return null;
            if (t.name == name)
                return t;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform f = Find(t.GetChild(i), name);
                if (f != null)
                    return f;
            }
            return null;
        }

        internal static Component FindByType(Aircraft ac, string typeName)
        {
            Component[] all = ac.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].GetType().Name == typeName)
                    return all[i];
            }
            return null;
        }

        internal static Unit Locked(Aircraft ac)
        {
            try
            {
                var list = ac.weaponManager != null ? ac.weaponManager.GetTargetList() : null;
                if (list == null)
                    return null;
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i] != null && !list[i].disabled)
                        return list[i];
                }
            }
            catch { }
            return null;
        }

        /// <summary>Signed bearing of a world point from the nose, degrees clockwise.</summary>
        internal static float Bearing(Transform ac, Vector3 world)
        {
            Vector3 d = world - ac.position;
            Vector3 f = Vector3.ProjectOnPlane(ac.forward, Vector3.up);
            d.y = 0f;
            if (d.sqrMagnitude < 1f || f.sqrMagnitude < 1e-4f)
                return 0f;
            return Vector3.SignedAngle(f, d, Vector3.up);
        }

        internal static float Heading(Vector3 forward)
        {
            float h = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            return h < 0f ? h + 360f : h;
        }
    }
}
