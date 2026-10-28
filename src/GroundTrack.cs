using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace J35XE
{
    /// <summary>
    /// Below the FBW minSpeed the nose wheel only follows the stick, and with ~12% of the
    /// weight on it a crosswind on the big fin walks the jet off the taxi line. With no
    /// yaw input on the ground, hold the heading the pilot last left it on.
    /// </summary>
    internal static class GroundTrack
    {
        const float MaxSpeed = 50f;
        const float StickDeadband = 0.05f;
        const float HeadingGain = 0.04f;
        const float RateGain = 1.5f;
        const float Authority = 0.4f;

        static readonly FieldInfo FilterAircraft = AccessTools.Field(typeof(ControlsFilter), "aircraft");
        static readonly Dictionary<int, LandingGear[]> Gears = new Dictionary<int, LandingGear[]>();
        static readonly Dictionary<int, float> Hold = new Dictionary<int, float>();

        internal static void Apply(ControlsFilter filter, ControlInputs inputs, Vector3 rawInputs, Rigidbody rb)
        {
            Aircraft ac = FilterAircraft != null ? FilterAircraft.GetValue(filter) as Aircraft : null;
            if (inputs == null || rb == null || !PitchTune.IsOurs(ac))
                return;
            int id = ac.GetInstanceID();
            float heading = Heading(ac.transform.forward);
            if (!OnGround(ac, id) || ac.speed < 1f || ac.speed > MaxSpeed || Mathf.Abs(rawInputs.y) > StickDeadband)
            {
                Hold[id] = heading;
                return;
            }
            float target;
            if (!Hold.TryGetValue(id, out target))
            {
                Hold[id] = heading;
                return;
            }
            float error = Mathf.DeltaAngle(heading, target);
            float yawRate = ac.transform.InverseTransformDirection(rb.angularVelocity).y;
            float cmd = Mathf.Clamp(error * HeadingGain - yawRate * RateGain, -Authority, Authority);
            inputs.yaw = Mathf.Clamp(inputs.yaw + cmd, -1f, 1f);
        }

        static float Heading(Vector3 forward)
        {
            return Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        }

        static bool OnGround(Aircraft ac, int id)
        {
            LandingGear[] gears;
            if (!Gears.TryGetValue(id, out gears) || gears == null)
            {
                gears = ac.GetComponentsInChildren<LandingGear>(true);
                Gears[id] = gears;
            }
            for (int i = 0; i < gears.Length; i++)
            {
                if (gears[i] != null && gears[i].WeightOnWheel(0.05f))
                    return true;
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(ControlsFilter), nameof(ControlsFilter.Filter))]
    internal static class Patch_J35_GroundTrack
    {
        [HarmonyPostfix]
        private static void Postfix(ControlsFilter __instance, ControlInputs inputs, Vector3 rawInputs, Rigidbody rb)
        {
            try { GroundTrack.Apply(__instance, inputs, rawInputs, rb); }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("J-35XE ground track: " + ex.Message);
            }
        }
    }
}
