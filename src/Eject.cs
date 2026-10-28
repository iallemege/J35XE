using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace J35XE
{
    /// <summary>
    /// After the player leaves a J-35, the body under the chute is not drawn and the
    /// jet destroys itself once the seat is clear.
    /// </summary>
    internal static class Eject
    {
        static readonly FieldInfo Skin = AccessTools.Field(typeof(Pilot), "skinnedMeshRenderer");

        internal static void Begin(Aircraft ac)
        {
            if (!PitchTune.IsOurs(ac) || ac.Player == null)
                return;
            HideSeated(ac);
            if (ac.GetComponent<EjectBoom>() == null)
                ac.gameObject.AddComponent<EjectBoom>().Begin(ac);
        }

        internal static void HideDismounted(PilotDismounted pilot)
        {
            if (pilot == null)
                return;
            Aircraft ac = null;
            try
            {
                Unit parent = null;
                if (UnitRegistry.TryGetUnit(pilot.parentUnit, out parent))
                    ac = parent as Aircraft;
            }
            catch { }
            if (!PitchTune.IsOurs(ac))
                return;
            Renderer[] rs = pilot.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rs.Length; i++)
            {
                if (rs[i] == null)
                    continue;
                string n = rs[i].gameObject.name;
                if (n.IndexOf("chute", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("para", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("canopy", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                if (rs[i] is SkinnedMeshRenderer)
                    rs[i].enabled = false;
            }
        }

        static void HideSeated(Aircraft ac)
        {
            if (ac.pilots == null)
                return;
            for (int i = 0; i < ac.pilots.Length; i++)
            {
                Pilot p = ac.pilots[i];
                if (p == null)
                    continue;
                try { p.TogglePilotVisibility(false); } catch { }
                if (Skin != null && Skin.GetValue(p) is Renderer r)
                    r.enabled = false;
            }
        }

        internal static void Boom(Aircraft ac)
        {
            if (ac == null || ac.disabled)
                return;
            try
            {
                if (ac.simplePhysics)
                    ac.SetComplexPhysics();
            }
            catch { }
            AeroPart[] parts = ac.GetComponentsInChildren<AeroPart>(true);
            for (int i = 0; i < parts.Length; i++)
            {
                AeroPart p = parts[i];
                if (p == null)
                    continue;
                try { p.BreakAllJoints(); } catch { }
                try { p.TakeDamage(0f, 0f, 1f, 0f, 400f, default); } catch { }
            }
            Blast(ac.transform.position);
            try { ac.DisableUnit(); } catch { }
        }

        static void Blast(Vector3 at)
        {
            MissileDefinition bomb = ModestBomb();
            Spawner sp = null;
            try { sp = NetworkSceneSingleton<Spawner>.i; } catch { }
            if (bomb == null || sp == null)
                return;
            try
            {
                Missile m = sp.SpawnMissile(bomb, at + Vector3.up * 1.5f, Quaternion.LookRotation(Vector3.down), Vector3.zero, null, null);
                if (m == null)
                    return;
                m.Arm();
                m.Detonate(Vector3.up, true, true);
            }
            catch { }
        }

        static MissileDefinition ModestBomb()
        {
            MissileDefinition best = null;
            float bestY = float.MaxValue;
            FieldInfo info = AccessTools.Field(typeof(Missile), "info");
            FieldInfo yield = AccessTools.Field(typeof(Missile), "blastYield");
            try
            {
                if (Encyclopedia.i == null || Encyclopedia.i.missiles == null)
                    return null;
                foreach (MissileDefinition d in Encyclopedia.i.missiles)
                {
                    if (d == null || d.unitPrefab == null)
                        continue;
                    Missile prefab = d.unitPrefab.GetComponent<Missile>();
                    if (prefab == null || info == null)
                        continue;
                    WeaponInfo w = info.GetValue(prefab) as WeaponInfo;
                    if (w == null || w.nuclear || w.energy || (!w.bomb && !w.glideBomb))
                        continue;
                    float y = 0f;
                    try { if (yield != null) y = (float)yield.GetValue(prefab); } catch { }
                    if (y < 40f || y > 2500f)
                        continue;
                    if (y < bestY)
                    {
                        bestY = y;
                        best = d;
                    }
                }
            }
            catch { }
            return best;
        }
    }

    internal sealed class EjectBoom : MonoBehaviour
    {
        Aircraft _ac;
        float _at;

        internal void Begin(Aircraft ac)
        {
            _ac = ac;
            _at = Time.time + 0.7f;
        }

        void Update()
        {
            if (_ac != null)
            {
                PilotDismounted[] pilots = FindObjectsOfType<PilotDismounted>();
                for (int i = 0; i < pilots.Length; i++)
                    Eject.HideDismounted(pilots[i]);
            }
            if (_ac == null || Time.time < _at)
                return;
            Eject.Boom(_ac);
            Destroy(this);
        }
    }

    [HarmonyPatch(typeof(Aircraft), nameof(Aircraft.StartEjectionSequence))]
    internal static class Patch_J35_Eject
    {
        static void Postfix(Aircraft __instance)
        {
            try { Eject.Begin(__instance); }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("J-35XE eject: " + ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(PilotDismounted), "OnStartClient")]
    internal static class Patch_J35_Chute
    {
        static void Postfix(PilotDismounted __instance)
        {
            try { Eject.HideDismounted(__instance); }
            catch { }
        }
    }

    [HarmonyPatch(typeof(PilotDismounted), nameof(PilotDismounted.SetPilotState))]
    internal static class Patch_J35_ChuteState
    {
        static void Postfix(PilotDismounted __instance)
        {
            try { Eject.HideDismounted(__instance); }
            catch { }
        }
    }
}
