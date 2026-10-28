using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace J35XE
{
    /// <summary>
    /// No vanilla HUD in the J-35 cockpit: the reflector sight, the round MFD and the COFFIN marks
    /// replace it. The top-right weapon panel, the bottom-left map and the countermeasure
    /// readout stay on. FlightHud, CombatHUD
    /// and the HMD keep running, so target selection still works. Everything comes back outside the
    /// cockpit view and while the M map is open.
    /// </summary>
    [DefaultExecutionOrder(32040)]
    internal sealed class HudHide : MonoBehaviour
    {
        const float Rescan = 1f;
        static readonly FieldInfo HudCanvas = AccessTools.Field(typeof(FlightHud), "canvas");
        static readonly FieldInfo CmBack = AccessTools.Field(typeof(CombatHUD), "countermeasureBackground");
        static readonly FieldInfo CmImage = AccessTools.Field(typeof(CombatHUD), "countermeasureImage");
        static readonly FieldInfo CmName = AccessTools.Field(typeof(CombatHUD), "countermeasureName");
        static readonly FieldInfo CmAmmo = AccessTools.Field(typeof(CombatHUD), "countermeasureAmmo");

        Aircraft _ac;
        readonly List<Graphic> _all = new List<Graphic>();
        readonly HashSet<Graphic> _known = new HashSet<Graphic>();
        readonly HashSet<Graphic> _ours = new HashSet<Graphic>();
        float _next;

        internal void Init(Aircraft ac)
        {
            _ac = ac;
        }

        void LateUpdate()
        {
            bool want = _ac != null && !_ac.disabled && Rig.Local(_ac) && Rig.Cockpit() && !DynamicMap.mapMaximized;
            if (!want)
            {
                Restore();
                return;
            }
            if (Time.unscaledTime >= _next)
            {
                _next = Time.unscaledTime + Rescan;
                Scan();
            }
            for (int i = 0; i < _all.Count; i++)
            {
                Graphic g = _all[i];
                if (g != null && g.enabled && !Keep(g))
                {
                    g.enabled = false;
                    _ours.Add(g);
                }
            }
        }

        void OnDisable() => Restore();

        void OnDestroy() => Restore();

        void Scan()
        {
            FlightHud hud = SceneSingleton<FlightHud>.i;
            Canvas canvas = hud != null && HudCanvas != null ? HudCanvas.GetValue(hud) as Canvas : null;
            if (canvas != null)
                Collect(canvas.gameObject);
            CombatHUD combat = SceneSingleton<CombatHUD>.i;
            if (combat != null)
                Collect(combat.gameObject);
            HeadMountedDisplay hmd = SceneSingleton<HeadMountedDisplay>.i;
            if (hmd != null)
                Collect(hmd.gameObject);
        }

        void Collect(GameObject root)
        {
            Graphic[] gs = root.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < gs.Length; i++)
            {
                if (gs[i] != null && !Keep(gs[i]) && _known.Add(gs[i]))
                    _all.Add(gs[i]);
            }
        }

        static bool Keep(Graphic g)
        {
            Transform t = g.transform;
            if (t.GetComponentInParent<WeaponStatus>(true) != null)
                return true;
            if (t.GetComponentInParent<WeaponSelector>(true) != null)
                return true;
            if (t.GetComponentInParent<DynamicMap>(true) != null)
                return true;
            return Countermeasure(g);
        }

        static bool Countermeasure(Graphic g)
        {
            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            if (hud == null || g == null)
                return false;
            Transform t = g.transform;
            if (Under(CmBack, hud, t))
                return true;
            if (Same(CmImage, hud, g))
                return true;
            if (Same(CmName, hud, g))
                return true;
            if (Same(CmAmmo, hud, g))
                return true;
            return false;
        }

        static bool Under(FieldInfo field, CombatHUD hud, Transform t)
        {
            if (field == null)
                return false;
            GameObject go = field.GetValue(hud) as GameObject;
            if (go == null)
                return false;
            return t == go.transform || t.IsChildOf(go.transform);
        }

        static bool Same(FieldInfo field, CombatHUD hud, Graphic g)
        {
            if (field == null)
                return false;
            UnityEngine.Object o = field.GetValue(hud) as UnityEngine.Object;
            if (o == null)
                return false;
            if (ReferenceEquals(o, g))
                return true;
            Component c = o as Component;
            return c != null && (g.transform == c.transform || g.transform.IsChildOf(c.transform));
        }

        void Restore()
        {
            if (_all.Count == 0)
                return;
            foreach (Graphic g in _ours)
            {
                if (g != null)
                    g.enabled = true;
            }
            _ours.Clear();
            _all.Clear();
            _known.Clear();
        }
    }
}
