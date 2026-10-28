using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace J35XE
{
    /// <summary>
    /// Flight hinges are held strong enough for a hard pull, including the elevons and the
    /// weapon pylons. A blast still cannot strip every surface at once. A real impact,
    /// the kind that comes from hitting the ground, breaks the joints so the airframe comes apart.
    /// </summary>
    internal static class Durability
    {
        internal const float BlastCap = 20f;
        internal const float ShockScale = 0.25f;
        const float MinPierceTolerance = 8f;
        const float BodyJoint = 500000f;
        const float SurfaceJoint = 900000f;
        const float CrashG = 35f;
        const float CrashImpulse = 5000f;

        static readonly FieldInfo Armor = AccessTools.Field(typeof(UnitPart), "armorProperties");
        static readonly FieldInfo Joints = AccessTools.Field(typeof(AeroPart), "joints");
        static readonly HashSet<int> Broken = new HashSet<int>();

        internal static void Apply(Aircraft ac)
        {
            AeroPart[] parts = ac.GetComponentsInChildren<AeroPart>(true);
            for (int i = 0; i < parts.Length; i++)
            {
                AeroPart p = parts[i];
                if (Armor?.GetValue(p) is ArmorProperties a && a.pierceTolerance < MinPierceTolerance)
                    a.pierceTolerance = MinPierceTolerance;
                Hold(p);
            }
        }

        internal static void Brace(Aircraft ac)
        {
            if (!PitchTune.IsOurs(ac))
                return;
            Joint[] joints = ac.GetComponentsInChildren<Joint>(true);
            for (int i = 0; i < joints.Length; i++)
            {
                Joint joint = joints[i];
                if (joint == null)
                    continue;
                joint.breakForce = float.PositiveInfinity;
                joint.breakTorque = float.PositiveInfinity;
            }
        }

        internal static void Unbrace(Aircraft ac)
        {
            if (ac == null)
                return;
            Apply(ac);
        }

        internal static void Hold(AeroPart p)
        {
            if (p == null || Joints?.GetValue(p) is not Array js)
                return;
            float joint = Loaded(p) ? SurfaceJoint : BodyJoint;
            foreach (object j in js)
            {
                if (j is not PartJoint pj)
                    continue;
                pj.breakForce = Mathf.Max(pj.breakForce, joint);
                pj.breakTorque = Mathf.Max(pj.breakTorque, joint);
                if (pj.joint != null)
                {
                    pj.joint.breakForce = pj.breakForce * 10f;
                    pj.joint.breakTorque = pj.breakTorque * 10f;
                }
            }
        }

        static bool Loaded(AeroPart p)
        {
            string n = p.gameObject.name;
            return n.StartsWith("elevon", StringComparison.Ordinal)
                || n == "rudder"
                || n.IndexOf("hardpoint", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("pylon", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("rail", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static void Crash(AeroPart hit, Collision collision)
        {
            if (hit == null || collision == null || hit.rb == null)
                return;
            Aircraft ac = hit.parentUnit as Aircraft;
            if (!PitchTune.IsOurs(ac))
                return;
            int id = ac.GetInstanceID();
            if (Broken.Contains(id))
                return;
            Rigidbody body = ac.rb != null ? ac.rb : hit.rb;
            float mass = Mathf.Max(body.mass, 1f);
            float dt = Time.fixedDeltaTime > 0f ? Time.fixedDeltaTime : 0.02f;
            float impulse = collision.impulse.magnitude;
            float g = impulse / dt / (mass * 9.81f);
            if (g < CrashG || impulse < CrashImpulse)
                return;
            Broken.Add(id);
            if (ac.simplePhysics)
            {
                try { ac.SetComplexPhysics(); } catch { }
            }
            AeroPart[] parts = ac.GetComponentsInChildren<AeroPart>(true);
            for (int i = 0; i < parts.Length; i++)
            {
                try { parts[i].BreakAllJoints(); } catch { }
            }
        }

        internal static bool Ours(UnitPart part)
        {
            return part != null && part.parentUnit is Aircraft ac && PitchTune.IsOurs(ac);
        }

        internal static bool Surface(UnitPart part)
        {
            if (part == null) return false;
            string n = part.gameObject.name;
            return n.StartsWith("elevon", StringComparison.Ordinal) || n == "rudder";
        }
    }

    [HarmonyPatch(typeof(UnitPart), nameof(UnitPart.ApplyDamage))]
    internal static class Patch_J35_BlastCap
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(UnitPart __instance, ref float netBlastDamage)
        {
            if (netBlastDamage > Durability.BlastCap && Durability.Ours(__instance))
                netBlastDamage = Durability.BlastCap;
        }
    }

    [HarmonyPatch(typeof(AeroPart), nameof(AeroPart.TakeShockwave))]
    internal static class Patch_J35_Shock
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(AeroPart __instance, ref float overpressure)
        {
            if (!Durability.Ours(__instance))
                return;
            if (Durability.Surface(__instance))
                overpressure = Mathf.Min(overpressure, 4f);
            else
                overpressure *= Durability.ShockScale;
        }
    }

    /// <summary>A hard impact breaks the airframe. A pull does not: that load stays inside the hinges.</summary>
    [HarmonyPatch(typeof(AeroPart), "OnCollisionEnter")]
    internal static class Patch_J35_Crash
    {
        static void Postfix(AeroPart __instance, Collision collision)
        {
            try { Durability.Crash(__instance, collision); }
            catch { }
        }
    }

    /// <summary>A broken neighbour used to set every hinge tied to it to zero force, so one hit dropped the whole set of elevons.</summary>
    [HarmonyPatch(typeof(AeroPart), "Part_OnTensorBreak")]
    internal static class Patch_J35_Tensor
    {
        private static bool Prefix(AeroPart __instance)
        {
            return __instance == null || !Durability.Ours(__instance) || !Durability.Surface(__instance);
        }
    }

    /// <summary>
    /// SGLOC and a hard pull shove a child body more than half a metre off its saved
    /// point for one physics step. That check used to drop every surface at once.
    /// A part that has actually been shot through still uses it.
    /// </summary>
    [HarmonyPatch(typeof(AeroPart), nameof(AeroPart.CheckAttachment))]
    internal static class Patch_J35_Attachment
    {
        static bool Prefix(AeroPart __instance)
        {
            if (!Durability.Ours(__instance))
                return true;
            if (Sgloc.Active)
                return false;
            return __instance.hitPoints <= 30f;
        }
    }

    /// <summary>Control-surface hinges stay on through a pull and through SGLOC. A crash still breaks them.</summary>
    [HarmonyPatch(typeof(AeroPart), nameof(AeroPart.SetHingeJoint))]
    internal static class Patch_J35_Hinge
    {
        static void Prefix(AeroPart __instance, ref float breakStrength)
        {
            if (Durability.Ours(__instance))
                breakStrength = float.PositiveInfinity;
        }

        static void Postfix(AeroPart __instance)
        {
            if (!Durability.Ours(__instance))
                return;
            HingeJoint[] hinges = __instance.GetComponents<HingeJoint>();
            for (int i = 0; i < hinges.Length; i++)
            {
                HingeJoint hinge = hinges[i];
                if (hinge == null)
                    continue;
                hinge.breakForce = float.PositiveInfinity;
                hinge.breakTorque = float.PositiveInfinity;
            }
        }
    }

    /// <summary>A round that starts inside the wing must not count as a hit on the jet that fired it.</summary>
    [HarmonyPatch(typeof(Unit), nameof(Unit.RegisterHit))]
    internal static class Patch_J35_OwnRound
    {
        static bool Prefix(Unit __instance, Unit hitUnit)
        {
            return hitUnit == null || !ReferenceEquals(hitUnit, __instance)
                || __instance is not Aircraft ac || !PitchTune.IsOurs(ac);
        }
    }

    [HarmonyPatch(typeof(BulletSim.Bullet), nameof(BulletSim.Bullet.TrajectoryTrace))]
    internal static class Patch_J35_MuzzleClear
    {
        static Vector3 _saved;
        static Transform _muzzle;
        static bool _nudged;

        static void Prefix(Transform muzzle, Unit owner)
        {
            _nudged = false;
            if (muzzle == null || owner is not Aircraft ac || !PitchTune.IsOurs(ac))
                return;
            Vector3 dir = muzzle.forward;
            if (dir.sqrMagnitude < 0.01f)
                dir = ac.transform.forward;
            Vector3 from = muzzle.position;
            Vector3 clear = from;
            for (int n = 0; n < 8; n++)
            {
                if (!Blocked(ac, clear, dir))
                    break;
                clear += dir * 0.4f + ac.transform.right * 0.05f;
            }
            if ((clear - from).sqrMagnitude < 0.0004f)
                return;
            _saved = from;
            _muzzle = muzzle;
            muzzle.position = clear;
            _nudged = true;
        }

        static void Postfix()
        {
            if (!_nudged || _muzzle == null)
                return;
            _muzzle.position = _saved;
            _nudged = false;
            _muzzle = null;
        }

        static bool Blocked(Aircraft ac, Vector3 p, Vector3 dir)
        {
            int mask = ~0;
            try { mask = ~(int)PhysicsLayers.ExclusionZonesMask; }
            catch { }
            RaycastHit[] hits = Physics.RaycastAll(p, dir, 2.5f, mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits.Length; i++)
            {
                Collider c = hits[i].collider;
                if (c != null && !c.isTrigger && c.transform.IsChildOf(ac.transform))
                    return true;
            }
            Collider[] near = Physics.OverlapSphere(p, 0.06f, mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < near.Length; i++)
            {
                Collider c = near[i];
                if (c != null && !c.isTrigger && c.transform.IsChildOf(ac.transform))
                    return true;
            }
            return false;
        }
    }
}
