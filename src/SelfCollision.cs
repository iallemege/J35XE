using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace J35XE
{
    /// <summary>
    /// The tail part's convex collider wraps the whole fin and rudder, and the wing hulls
    /// sit over the elevon roots. Jointed parts never collide, but once a surface is shot
    /// off it is inside its neighbour's hull, and the depenetration push tore every other
    /// joint apart. Parts of one J-35 ignore each other for good.
    /// </summary>
    internal static class SelfCollision
    {
        internal static void Ignore(Aircraft ac)
        {
            if (!PitchTune.IsOurs(ac))
                return;
            Collider[] all = ac.GetComponentsInChildren<Collider>(true);
            List<Collider> solid = new List<Collider>(all.Length);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && !all[i].isTrigger)
                    solid.Add(all[i]);
            }
            for (int a = 0; a < solid.Count; a++)
            {
                for (int b = a + 1; b < solid.Count; b++)
                    Physics.IgnoreCollision(solid[a], solid[b], true);
            }
        }
    }

    [HarmonyPatch(typeof(Aircraft), nameof(Aircraft.SetComplexPhysics))]
    internal static class Patch_J35_ComplexPhysics
    {
        [HarmonyPostfix]
        private static void Postfix(Aircraft __instance)
        {
            try { SelfCollision.Ignore(__instance); }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("J-35XE self collision: " + ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(AeroPart), nameof(AeroPart.Detach))]
    internal static class Patch_J35_Detach
    {
        [HarmonyPrefix]
        private static void Prefix(AeroPart __instance)
        {
            try
            {
                Aircraft ac = __instance != null ? __instance.GetComponentInParent<Aircraft>() : null;
                SelfCollision.Ignore(ac);
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("J-35XE detach: " + ex.Message);
            }
        }
    }
}
