using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NuclearOption.Jobs;
using UnityEngine;

namespace J35XE
{
    /// <summary>
    /// A held stick commands 15 G. The limit rises to 20 G only once the jet is fast.
    /// Above that speed the pitch and roll commands are smoothed and the pitch damper
    /// is eased off, so the airframe does not buzz.
    /// </summary>
    internal static class PitchTune
    {
        internal const string JsonKey = "SaabJ35";

        const float GLimit = 15f;
        const float GLimitFast = 20f;
        const float GSlow = 200f;
        const float GFast = 450f;
        const float CornerSpeed = 175f;
        const float MaxPitchRate = 2.2f;
        const float MaxRollRate = 5.5f;
        const float MaxRollSpeed = 210f;
        const float PitchP = 12f;
        const float PitchD = 0.4f;
        const float YawTightness = 1f;
        const float RollTightness = 0.45f;
        const float ElevonServo = 46f;
        const float ElevonInnerServo = 36f;
        const float ElevonMidServo = 40f;
        const float RudderYaw = -25f;
        const float RudderServo = 70f;
        const float AlphaLimit = 32f;
        const float AlphaStrength = 0.02f;
        const float ElevonOuter = -16f;
        const float ElevonMid = -18f;
        const float ElevonInner = -20f;
        const float RollOuter = 30f;
        const float RollMid = 20f;
        const float RollInner = 10f;
        const float ElevonInnerArea = 7f;
        const float ElevonMidArea = 4.5f;
        const float ElevonOuterArea = 4f;
        const float AirframeGLimit = 20f;
        const float AirframeCorner = 170f;
        // Vanilla aero areas are several times the mesh. A Draken half-wing on the model is about
        // 25 m²; each wing is set at 80 m² so the jet lifts and pitches the way the stock fighters do.
        const float WingAreaEach = 80f;
        const float AfterburnerThrust = 75000f;

        static readonly FieldInfo FlyByWire = AccessTools.Field(typeof(ControlsFilter), "flyByWire");
        static readonly Type FbwType = AccessTools.Inner(typeof(ControlsFilter), "FlyByWire");
        static readonly FieldInfo FbwG = Field("gLimitPositive");
        static readonly FieldInfo FbwCorner = Field("cornerSpeed");
        static readonly FieldInfo FbwPitchRate = Field("maxPitchAngularVel");
        static readonly FieldInfo FbwRollRate = Field("maxRollAngularVel");
        static readonly FieldInfo FbwRollSpeed = Field("maxRollSpeed");
        static readonly FieldInfo FbwP = Field("pFactorFast");
        static readonly FieldInfo FbwD = Field("dFactorFast");
        static readonly FieldInfo FbwYaw = Field("yawTightness");
        static readonly FieldInfo FbwRoll = Field("rollTightness");
        static readonly FieldInfo FbwAlpha = Field("alphaLimiter");
        static readonly FieldInfo FbwAlphaStrength = Field("alphaLimiterStrength");
        static readonly FieldInfo PitchRange = AccessTools.Field(typeof(ControlSurface), "pitchRange");
        static readonly FieldInfo RollRange = AccessTools.Field(typeof(ControlSurface), "rollRange");
        static readonly FieldInfo YawRange = AccessTools.Field(typeof(ControlSurface), "yawRange");
        static readonly FieldInfo VisibleMesh = AccessTools.Field(typeof(ControlSurface), "visibleMesh");
        static readonly FieldInfo ServoSpeed = AccessTools.Field(typeof(ControlSurface), "servoSpeed");
        static readonly AccessTools.FieldRef<ControlSurface, PtrAllocation<ControlSurfaceFields>> SurfaceJob =
            AccessTools.FieldRefAccess<ControlSurface, PtrAllocation<ControlSurfaceFields>>("JobFields");
        static readonly FieldInfo Afterburners = AccessTools.Field(typeof(JetNozzle), "afterburners");
        static readonly Type AbType = AccessTools.Inner(typeof(JetNozzle), "Afterburner");
        static readonly FieldInfo AbThrust = AbType != null ? AccessTools.Field(AbType, "thrust") : null;

        static FieldInfo Field(string name)
        {
            return FbwType != null ? AccessTools.Field(FbwType, name) : null;
        }

        internal static bool IsOurs(Aircraft ac)
        {
            if (ac == null)
                return false;
            AircraftDefinition def = ac.definition as AircraftDefinition;
            if (def != null && string.Equals(def.jsonKey, JsonKey, StringComparison.Ordinal))
                return true;
            return ac.gameObject.name.StartsWith(JsonKey, StringComparison.Ordinal);
        }

        internal static void Apply(Aircraft ac)
        {
            if (!IsOurs(ac))
                return;
            ControlsFilter filter = ac.GetComponent<ControlsFilter>();
            object fbw = filter != null && FlyByWire != null ? FlyByWire.GetValue(filter) : null;
            if (fbw != null)
            {
                Set(FbwG, fbw, GLimit);
                Set(FbwCorner, fbw, CornerSpeed);
                Set(FbwPitchRate, fbw, MaxPitchRate);
                Set(FbwRollRate, fbw, MaxRollRate);
                Set(FbwRollSpeed, fbw, MaxRollSpeed);
                Set(FbwP, fbw, PitchP);
                Set(FbwD, fbw, PitchD);
                Set(FbwYaw, fbw, YawTightness);
                Set(FbwRoll, fbw, RollTightness);
                Set(FbwAlpha, fbw, AlphaLimit);
                Set(FbwAlphaStrength, fbw, AlphaStrength);
            }
            AircraftDefinition def = ac.definition as AircraftDefinition;
            if (def != null && def.aircraftParameters != null)
            {
                def.aircraftParameters.aircraftGLimit = AirframeGLimit;
                def.aircraftParameters.cornerSpeed = AirframeCorner;
            }
            AeroPart[] parts = ac.GetComponentsInChildren<AeroPart>(true);
            for (int i = 0; i < parts.Length; i++)
            {
                string n = parts[i].gameObject.name;
                if (n == "wing_L" || n == "wing_R")
                    parts[i].SetWingArea(WingAreaEach);
                else if (n.StartsWith("elevon_", StringComparison.Ordinal))
                {
                    float area = n.EndsWith("_in", StringComparison.Ordinal) ? ElevonInnerArea
                        : n.EndsWith("_mid", StringComparison.Ordinal) ? ElevonMidArea : ElevonOuterArea;
                    parts[i].SetWingArea(area);
                }
            }
            JetNozzle[] nozzles = ac.GetComponentsInChildren<JetNozzle>(true);
            for (int i = 0; i < nozzles.Length && Afterburners != null && AbThrust != null; i++)
            {
                Array abs = Afterburners.GetValue(nozzles[i]) as Array;
                if (abs == null)
                    continue;
                foreach (object ab in abs)
                {
                    if (ab != null)
                        AbThrust.SetValue(ab, AfterburnerThrust);
                }
            }
            if (PitchRange == null)
                return;
            ControlSurface[] surfaces = ac.GetComponentsInChildren<ControlSurface>(true);
            for (int i = 0; i < surfaces.Length; i++)
                Shape(surfaces[i]);
            Ost.Apply(ac);
        }

        /// <summary>Called before the surface copies its ranges into the flight job, and again after.</summary>
        internal static void Shape(ControlSurface surface)
        {
            if (surface == null || PitchRange == null)
                return;
            Aircraft ac = surface.GetComponentInParent<Aircraft>();
            if (!IsOurs(ac))
                return;
            string n = surface.gameObject.name;
            float pitch = 0f, roll = 0f, yaw = 0f, servo = 0f;
            bool elevon = n.StartsWith("elevon_", StringComparison.OrdinalIgnoreCase);
            bool rudder = string.Equals(n, "rudder", StringComparison.OrdinalIgnoreCase);
            if (!elevon && !rudder)
                return;
            if (rudder)
            {
                yaw = RudderYaw;
                servo = RudderServo;
                SeatRudder(surface, ac);
            }
            else
            {
                bool left = n.IndexOf("_L_", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.EndsWith("_L", StringComparison.OrdinalIgnoreCase);
                bool outer = n.EndsWith("_out", StringComparison.OrdinalIgnoreCase);
                bool mid = n.EndsWith("_mid", StringComparison.OrdinalIgnoreCase);
                pitch = outer ? ElevonOuter : mid ? ElevonMid : ElevonInner;
                float mag = outer ? RollOuter : mid ? RollMid : RollInner;
                roll = left ? -mag : mag;
                servo = outer ? ElevonServo : mid ? ElevonMidServo : ElevonInnerServo;
            }
            PitchRange.SetValue(surface, pitch);
            RollRange?.SetValue(surface, roll);
            YawRange?.SetValue(surface, yaw);
            ServoSpeed?.SetValue(surface, servo);
            try
            {
                ref PtrAllocation<ControlSurfaceFields> job = ref SurfaceJob(surface);
                if (!job.IsCreated)
                    return;
                ref ControlSurfaceFields fields = ref job.Ref();
                fields.pitchRange = pitch;
                fields.rollRange = roll;
                fields.yawRange = yaw;
                fields.servoSpeed = servo;
            }
            catch { }
        }

        /// <summary>
        /// The flight job swings visibleMesh around its local X. The rudder only yaws when that axis is vertical.
        /// </summary>
        static void SeatRudder(ControlSurface surface, Aircraft ac)
        {
            if (VisibleMesh == null || ac == null)
                return;
            GameObject go = VisibleMesh.GetValue(surface) as GameObject;
            if (go == null)
                return;
            Transform hinge = go.transform;
            Vector3 axis = ac.transform.up;
            if (Mathf.Abs(Vector3.Dot(hinge.right, axis)) > 0.92f)
                return;
            Transform park = hinge.parent != null ? hinge.parent : ac.transform;
            int n = hinge.childCount;
            Transform[] kids = new Transform[n];
            for (int i = 0; i < n; i++)
                kids[i] = hinge.GetChild(i);
            for (int i = 0; i < n; i++)
                kids[i].SetParent(park, true);
            Vector3 x = axis.normalized;
            Vector3 z = Vector3.ProjectOnPlane(ac.transform.forward, x);
            if (z.sqrMagnitude < 1e-6f)
                z = Vector3.ProjectOnPlane(Vector3.forward, x);
            if (z.sqrMagnitude < 1e-6f)
                z = Vector3.forward;
            Vector3 y = Vector3.Cross(z.normalized, x);
            if (y.sqrMagnitude < 1e-6f)
                y = ac.transform.forward;
            hinge.rotation = Quaternion.LookRotation(z.normalized, y.normalized);
            for (int i = 0; i < n; i++)
                kids[i].SetParent(hinge, true);
        }

        internal static void ScaleG(ControlsFilter filter)
        {
            Aircraft ac = AircraftOf(filter);
            if (!IsOurs(ac) || Sgloc.Active)
                return;
            object fbw = WireOf(filter);
            if (fbw == null)
                return;
            float g = Mathf.Lerp(GLimit, GLimitFast, Mathf.InverseLerp(GSlow, GFast, ac.speed));
            Set(FbwG, fbw, g);
            float damp = Mathf.Lerp(PitchD, 0.18f, Mathf.InverseLerp(220f, 480f, ac.speed));
            Set(FbwD, fbw, damp);
        }

        internal static Aircraft AircraftOf(ControlsFilter filter)
        {
            return AircraftField != null ? AircraftField.GetValue(filter) as Aircraft : null;
        }

        internal static object WireOf(ControlsFilter filter)
        {
            return filter != null && FlyByWire != null ? FlyByWire.GetValue(filter) : null;
        }

        static void Set(FieldInfo f, object target, float value)
        {
            if (f != null)
                f.SetValue(target, value);
        }

        static readonly FieldInfo AircraftField = AccessTools.Field(typeof(ControlsFilter), "aircraft");
    }

    [HarmonyPatch(typeof(ControlSurface), "Awake")]
    internal static class Patch_J35_Surface
    {
        [HarmonyPrefix]
        private static void Prefix(ControlSurface __instance)
        {
            try { PitchTune.Shape(__instance); }
            catch { }
        }
    }

    [HarmonyPatch(typeof(Aircraft), "Awake")]
    internal static class Patch_J35_PitchTune
    {
        [HarmonyPostfix]
        private static void Postfix(Aircraft __instance)
        {
            try { PitchTune.Apply(__instance); }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("J-35XE pitch tune: " + ex.Message);
            }
            try { Stores.Arm(__instance); }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("J-35XE stores: " + ex.Message);
            }
            try
            {
                if (PitchTune.IsOurs(__instance))
                    Durability.Apply(__instance);
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("J-35XE durability: " + ex.Message);
            }
            try
            {
                if (PitchTune.IsOurs(__instance) && __instance.GetComponent<Jato>() == null)
                    __instance.gameObject.AddComponent<Jato>().Init(__instance);
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("J-35XE JATO: " + ex.Message);
            }
            try
            {
                if (PitchTune.IsOurs(__instance) && __instance.GetComponent<Cosmetics>() == null)
                    __instance.gameObject.AddComponent<Cosmetics>().Init(__instance);
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("J-35XE cosmetics: " + ex.Message);
            }
            try
            {
                if (PitchTune.IsOurs(__instance))
                {
                    if (__instance.GetComponent<PalaSkin>() == null)
                        __instance.gameObject.AddComponent<PalaSkin>().Init(__instance);
                    if (__instance.GetComponent<LiveryLatch>() == null)
                        __instance.gameObject.AddComponent<LiveryLatch>().Init(__instance);
                }
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("J-35XE skin: " + ex.Message);
            }
            Rig.Attach(__instance);
        }
    }

    /// <summary>
    /// On the runway the fly-by-wire holds pitch at the current rate while the nose wheel
    /// is planted, so a pull never reaches the elevons and the jet will not rotate.
    /// While the gear is down and the speed is still low, a pull is passed through.
    /// </summary>
    [HarmonyPatch(typeof(ControlsFilter), "Filter")]
    internal static class Patch_J35_Rotate
    {
        [HarmonyPrefix]
        private static void Prefix(ControlsFilter __instance)
        {
            try { PitchTune.ScaleG(__instance); }
            catch { }
        }

        [HarmonyPostfix]
        private static void Postfix(ControlsFilter __instance, ControlInputs inputs, Vector3 rawInputs)
        {
            try
            {
                Aircraft ac = PitchTune.AircraftOf(__instance);
                if (!PitchTune.IsOurs(ac) || inputs == null)
                    return;
                SmoothYaw(ac, inputs);
                SteadyFast(ac, inputs);
                if (!ac.gearDeployed || ac.radarAlt > 18f || ac.speed > 100f)
                    return;
                if (rawInputs.x <= 0.05f)
                    return;
                inputs.pitch = Mathf.Max(inputs.pitch, rawInputs.x);
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("J-35XE rotate: " + ex.Message);
            }
        }

        static readonly Dictionary<int, float> Yaw = new Dictionary<int, float>();
        static readonly Dictionary<int, Vector2> Fast = new Dictionary<int, Vector2>();

        /// <summary>
        /// A half-roll makes the yaw rate flicker, and the damper was following every flicker
        /// with the rudder. A short smooth keeps a steady pedal and drops the left-right slap.
        /// </summary>
        static void SmoothYaw(Aircraft ac, ControlInputs inputs)
        {
            int id = ac.GetInstanceID();
            float prev;
            if (!Yaw.TryGetValue(id, out prev))
                prev = inputs.yaw;
            float dt = Time.fixedDeltaTime > 0f ? Time.fixedDeltaTime : 0.02f;
            float blend = 1f - Mathf.Exp(-dt / 0.08f);
            float yaw = Mathf.Lerp(prev, inputs.yaw, blend);
            Yaw[id] = yaw;
            inputs.yaw = yaw;
        }

        /// <summary>
        /// At high speed a one-frame twitch in the pitch or roll command is a large force.
        /// The command is averaged over a short window so the jet holds the pull without buzzing.
        /// </summary>
        static void SteadyFast(Aircraft ac, ControlInputs inputs)
        {
            int id = ac.GetInstanceID();
            Vector2 prev;
            if (!Fast.TryGetValue(id, out prev))
                prev = new Vector2(inputs.pitch, inputs.roll);
            float spd = ac.speed;
            float amount = Mathf.InverseLerp(190f, 460f, spd);
            float tau = Mathf.Lerp(0.08f, 0.16f, amount);
            float dt = Time.fixedDeltaTime > 0f ? Time.fixedDeltaTime : 0.02f;
            float blend = 1f - Mathf.Exp(-dt / tau);
            float pitch = Mathf.Lerp(prev.x, inputs.pitch, blend);
            float roll = Mathf.Lerp(prev.y, inputs.roll, Mathf.Lerp(blend, 1f, 0.35f));
            Fast[id] = new Vector2(pitch, roll);
            inputs.pitch = pitch;
            inputs.roll = roll;
        }
    }

    /// <summary>A G spike used to spend the pilot's hit points. Drones have no pilot to kill; the J-35 pilot is treated the same way. Blackout still starts at 15 G.</summary>
    [HarmonyPatch(typeof(Pilot), nameof(Pilot.TakeGForceDamage))]
    internal static class Patch_J35_PilotG
    {
        static bool Prefix(Pilot __instance)
        {
            return __instance == null || __instance.aircraft == null || !PitchTune.IsOurs(__instance.aircraft);
        }
    }

    /// <summary>Vision stays clear below 15 G. The stock model starts greying out near 7 G.</summary>
    [HarmonyPatch(typeof(GLOC), nameof(GLOC.SimulateGLOC))]
    internal static class Patch_J35_Gloc
    {
        const float BlackoutG = 15f;

        static void Prefix(GLOC __instance, ref float gForce)
        {
            Aircraft ac = __instance.GetComponentInParent<Aircraft>();
            if (!PitchTune.IsOurs(ac))
                return;
            if (Mathf.Abs(gForce) < BlackoutG)
                gForce = 0f;
        }
    }
}
