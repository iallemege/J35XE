using HarmonyLib;
using UnityEngine;

namespace J35XE
{
    /// <summary>
    /// The cockpit camera's head sway is driven by the acceleration of Aircraft.CockpitRB() at the
    /// eye point. Once a hit puts the J-35 on complex physics that is the 700 kg nose part, which a
    /// blast shoves and its joint yanks back: the spike flings the eye out of the cockpit and the
    /// unbounded anti-slump integral holds it there. The J-35 reads the root body instead (the gauges,
    /// MFD, HUD and eye point all hang off the root), and the sway stays within a head's reach.
    /// </summary>
    internal static class CockpitCam
    {
        internal const float MaxOffset = 0.15f, MaxSpeed = 2.5f, MaxAntiSlump = 120f;

        internal static readonly AccessTools.FieldRef<CameraCockpitState, Aircraft> Ac =
            AccessTools.FieldRefAccess<CameraCockpitState, Aircraft>("aircraft");
        internal static readonly AccessTools.FieldRef<CameraCockpitState, Vector3> Pos =
            AccessTools.FieldRefAccess<CameraCockpitState, Vector3>("camRelativePos");
        internal static readonly AccessTools.FieldRef<CameraCockpitState, Vector3> Vel =
            AccessTools.FieldRefAccess<CameraCockpitState, Vector3>("camRelativeVel");
        internal static readonly AccessTools.FieldRef<CameraCockpitState, float> Slump =
            AccessTools.FieldRefAccess<CameraCockpitState, float>("antiSlump");
    }

    [HarmonyPatch(typeof(Aircraft), nameof(Aircraft.CockpitRB))]
    internal static class Patch_J35_CockpitRB
    {
        private static bool Prefix(Aircraft __instance, ref Rigidbody __result)
        {
            if (!PitchTune.IsOurs(__instance)) return true;
            Rigidbody rb = __instance.rb;
            if (rb == null) return true;
            __result = rb;
            return false;
        }
    }

    [HarmonyPatch(typeof(CameraCockpitState), "FixedUpdateState")]
    internal static class Patch_J35_CockpitSway
    {
        private static void Postfix(CameraCockpitState __instance)
        {
            Aircraft ac = CockpitCam.Ac(__instance);
            if (ac == null || !PitchTune.IsOurs(ac)) return;
            ref Vector3 pos = ref CockpitCam.Pos(__instance);
            ref Vector3 vel = ref CockpitCam.Vel(__instance);
            ref float slump = ref CockpitCam.Slump(__instance);
            pos = Vector3.ClampMagnitude(pos, CockpitCam.MaxOffset);
            vel = Vector3.ClampMagnitude(vel, CockpitCam.MaxSpeed);
            slump = Mathf.Clamp(slump, -CockpitCam.MaxAntiSlump, CockpitCam.MaxAntiSlump);
        }
    }
}
