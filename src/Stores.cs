using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NuclearOption.SavedMission;
using UnityEngine;

namespace J35XE
{
    /// <summary>
    /// Default J-35 stations other than the gun carry MMR-S3. Flares start at 120.
    /// The radar jammer is part of the airframe, not a pylon store.
    /// </summary>
    internal static class Stores
    {
        const int FlareAmmo = 120;
        const int GunRounds = 1200;
        const string GunName = "30mm autocannon";

        static readonly FieldInfo SelectorAircraft = AccessTools.Field(typeof(LoadoutSelector), "aircraft");
        static readonly FieldInfo SelectorRows = AccessTools.Field(typeof(LoadoutSelector), "weaponSelectors");
        static readonly FieldInfo ManagerAircraft = AccessTools.Field(typeof(WeaponManager), "aircraft");
        static readonly FieldInfo FlarePrefab = AccessTools.Field(typeof(FlareEjector), "flarePrefab");
        static readonly FieldInfo FlareGroup = AccessTools.Field(typeof(FlareEjector), "ejectionGrouping");
        static readonly FieldInfo FlareInterval = AccessTools.Field(typeof(FlareEjector), "ejectionInterval");
        static readonly FieldInfo FlareSpeed = AccessTools.Field(typeof(FlareEjector), "ejectionVelocity");
        static readonly FieldInfo FlareSpread = AccessTools.Field(typeof(FlareEjector), "ejectionVelocityVariance");
        static readonly FieldInfo FlareSound = AccessTools.Field(typeof(FlareEjector), "ejectionSound");
        static readonly FieldInfo FlareVolume = AccessTools.Field(typeof(FlareEjector), "ejectionVolume");
        static readonly FieldInfo FlareMax = AccessTools.Field(typeof(FlareEjector), "maxAmmo");
        static readonly FieldInfo JamPower = AccessTools.Field(typeof(RadarJammer), "powerUsage");
        static readonly FieldInfo JamCap = AccessTools.Field(typeof(RadarJammer), "capacitance");
        static readonly FieldInfo JamStrength = AccessTools.Field(typeof(RadarJammer), "jammingIntensity");
        static readonly FieldInfo JamVolume = AccessTools.Field(typeof(RadarJammer), "dischargeVolume");
        static readonly FieldInfo JamSound = AccessTools.Field(typeof(RadarJammer), "dischargeSound");
        static readonly FieldInfo GunMag = AccessTools.Field(typeof(Gun), "magazineCapacity");
        static readonly FieldInfo GunMags = AccessTools.Field(typeof(Gun), "maxMagazines");
        static readonly FieldInfo GunLoaded = AccessTools.Field(typeof(Gun), "bulletsLoaded");
        static readonly FieldInfo GunStationField = AccessTools.Field(typeof(Weapon), "weaponStation");
        static readonly FieldInfo GunMount = AccessTools.Field(typeof(Weapon), "mount");
        static readonly FieldInfo GunMuzzles = AccessTools.Field(typeof(Gun), "muzzles");

        internal static void Arm(Aircraft ac)
        {
            if (!PitchTune.IsOurs(ac))
                return;
            Stamp(ac);
            Flares(ac);
            Jammer(ac);
            PlaceGun(ac);
            CockpitEdit.Bind(ac);
        }

        /// <summary>
        /// The 30 mm sits on the starboard wing root. Bullets come from the muzzle
        /// transforms, so those move onto the wing with the hardpoint.
        /// </summary>
        static void PlaceGun(Aircraft ac)
        {
            Transform wing = Rig.Find(ac.transform, "wing_R");
            if (wing == null)
                wing = Rig.Find(ac.transform, "wing_r");
            if (wing == null)
                return;
            Vector3 world = WingRootWorld(ac, wing);
            Transform hp = Rig.Find(ac.transform, "hardpoint_gun");
            Seat(hp, wing, world, ac);
            Gun[] guns = ac.GetComponentsInChildren<Gun>(true);
            for (int i = 0; i < guns.Length; i++)
            {
                Transform[] muzzles = GunMuzzles != null ? GunMuzzles.GetValue(guns[i]) as Transform[] : null;
                if (muzzles == null)
                    continue;
                for (int m = 0; m < muzzles.Length; m++)
                    Seat(muzzles[m], wing, world, ac);
            }
        }

        static void Seat(Transform t, Transform wing, Vector3 world, Aircraft ac)
        {
            if (t == null)
                return;
            t.SetParent(wing, true);
            t.position = world;
            t.rotation = ac.transform.rotation;
        }

        static Vector3 WingRootWorld(Aircraft ac, Transform wing)
        {
            Renderer[] rs = wing.GetComponentsInChildren<Renderer>(true);
            bool any = false;
            Bounds b = default;
            for (int i = 0; i < rs.Length; i++)
            {
                Renderer r = rs[i];
                if (r == null || r is ParticleSystemRenderer || r is TrailRenderer)
                    continue;
                if (!any)
                {
                    b = r.bounds;
                    any = true;
                }
                else
                    b.Encapsulate(r.bounds);
            }
            if (!any)
                return wing.position + ac.transform.right * 0.6f;
            bool right = b.center.x >= ac.transform.position.x;
            float x = right ? b.min.x + 0.55f : b.max.x - 0.55f;
            return new Vector3(x, b.center.y, b.max.z + 0.2f);
        }

        internal static void StampGun(Gun gun)
        {
            if (gun == null)
                return;
            Aircraft ac = gun.GetComponentInParent<Aircraft>();
            if (!PitchTune.IsOurs(ac))
                return;
            if (GunMag != null)
                GunMag.SetValue(gun, GunRounds);
            if (GunMags != null)
                GunMags.SetValue(gun, 0);
            if (GunLoaded != null)
                GunLoaded.SetValue(gun, GunRounds);
            gun.ammo = GunRounds;
            if (gun.info != null && gun.info.weaponName != GunName)
            {
                WeaponInfo copy = UnityEngine.Object.Instantiate(gun.info);
                copy.weaponName = GunName;
                copy.shortName = GunName;
                copy.hideFlags = HideFlags.DontUnloadUnusedAsset;
                gun.info = copy;
            }
            WeaponMount mount = GunMount != null ? GunMount.GetValue(gun) as WeaponMount : null;
            if (mount != null && mount.mountName != GunName)
            {
                WeaponMount local = UnityEngine.Object.Instantiate(mount);
                local.name = "J35_30mm";
                local.mountName = GunName;
                local.info = gun.info;
                local.hideFlags = HideFlags.DontUnloadUnusedAsset;
                GunMount.SetValue(gun, local);
            }
            else if (mount != null)
                mount.info = gun.info;
            if (GunStationField != null && GunStationField.GetValue(gun) is WeaponStation st)
            {
                st.WeaponInfo = gun.info;
                st.Ammo = GunRounds;
                st.FullAmmo = GunRounds;
            }
            NameMount(ac, gun);
        }

        static void NameMount(Aircraft ac, Gun gun)
        {
            WeaponManager wm = ac.weaponManager;
            if (wm == null || wm.hardpointSets == null || gun.info == null)
                return;
            for (int i = 0; i < wm.hardpointSets.Length; i++)
            {
                HardpointSet set = wm.hardpointSets[i];
                if (set == null || set.hardpoints == null || !Holds(set, gun))
                    continue;
                WeaponMount src = set.weaponMount;
                if (src == null && ac.loadout != null && ac.loadout.weapons != null && i < ac.loadout.weapons.Count)
                    src = ac.loadout.weapons[i];
                if (src == null || src.mountName == GunName)
                    return;
                WeaponMount local = UnityEngine.Object.Instantiate(src);
                local.name = "J35_30mm";
                local.mountName = GunName;
                local.info = gun.info;
                local.hideFlags = HideFlags.DontUnloadUnusedAsset;
                set.weaponMount = local;
                if (ac.loadout != null && ac.loadout.weapons != null && i < ac.loadout.weapons.Count)
                    ac.loadout.weapons[i] = local;
                return;
            }
            for (int i = 0; i < wm.hardpointSets.Length; i++)
            {
                HardpointSet set = wm.hardpointSets[i];
                if (set == null || set.name == null || set.name.IndexOf("Gun", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (set.weaponMount == null || set.weaponMount.mountName == GunName)
                    return;
                WeaponMount local = UnityEngine.Object.Instantiate(set.weaponMount);
                local.name = "J35_30mm";
                local.mountName = GunName;
                local.info = gun.info;
                local.hideFlags = HideFlags.DontUnloadUnusedAsset;
                set.weaponMount = local;
                if (ac.loadout != null && ac.loadout.weapons != null && i < ac.loadout.weapons.Count)
                {
                    WeaponMount slot = ac.loadout.weapons[i];
                    if (slot != null && slot.info != null && slot.info.gun)
                        ac.loadout.weapons[i] = local;
                }
                return;
            }
        }

        static bool Holds(HardpointSet set, Gun gun)
        {
            for (int h = 0; h < set.hardpoints.Count; h++)
            {
                Hardpoint hp = set.hardpoints[h];
                if (hp == null || hp.BuiltInWeapons == null)
                    continue;
                for (int b = 0; b < hp.BuiltInWeapons.Length; b++)
                    if (hp.BuiltInWeapons[b] == gun)
                        return true;
            }
            return false;
        }

        internal static void Stamp(Aircraft ac)
        {
            if (!PitchTune.IsOurs(ac))
                return;
            WeaponManager wm = ac.weaponManager;
            AircraftDefinition def = ac.definition as AircraftDefinition;
            if (def == null || def.aircraftParameters == null || wm == null || wm.hardpointSets == null)
                return;
        }

        static void Fill(Loadout loadout, WeaponManager wm)
        {
            if (loadout.weapons == null)
                return;
            while (loadout.weapons.Count < wm.hardpointSets.Length)
                loadout.weapons.Add(null);
            for (int i = 0; i < wm.hardpointSets.Length && i < loadout.weapons.Count; i++)
            {
                WeaponMount cur = loadout.weapons[i];
                if (cur != null)
                    continue;
                HardpointSet set = wm.hardpointSets[i];
                if (GunStation(set, null))
                    continue;
                WeaponMount s3 = S3Option(set, 1) ?? S3Anywhere(1);
                if (s3 != null)
                    loadout.weapons[i] = s3;
            }
        }

        static bool GunStation(HardpointSet set, WeaponMount cur)
        {
            if (cur != null && ((cur.info != null && cur.info.gun) || cur.GunAmmo))
                return true;
            if (set == null)
                return false;
            if (!string.IsNullOrEmpty(set.name) && set.name.IndexOf("Gun", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (set.weaponOptions == null || set.weaponOptions.Count == 0)
                return false;
            bool any = false;
            for (int i = 0; i < set.weaponOptions.Count; i++)
            {
                WeaponMount o = set.weaponOptions[i];
                if (o == null)
                    continue;
                any = true;
                if (o.info == null || !o.info.gun)
                    return false;
            }
            return any;
        }

        static WeaponMount GunOption(HardpointSet set)
        {
            if (set == null || set.weaponOptions == null)
                return null;
            for (int i = 0; i < set.weaponOptions.Count; i++)
            {
                WeaponMount o = set.weaponOptions[i];
                if (o != null && o.info != null && o.info.gun)
                    return o;
            }
            return null;
        }

        static bool IsS3(WeaponMount m)
        {
            if (m == null)
                return false;
            string name = m.info != null && !string.IsNullOrEmpty(m.info.weaponName) ? m.info.weaponName : m.mountName;
            if (string.IsNullOrEmpty(name) || name.IndexOf("MMR-S3", StringComparison.OrdinalIgnoreCase) < 0)
                return false;
            if (m.mountName != null && m.mountName.IndexOf("backward", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            return true;
        }

        static WeaponMount S3Option(HardpointSet set, int ammo)
        {
            if (set == null || set.weaponOptions == null)
                return null;
            WeaponMount best = null;
            int bestDiff = int.MaxValue;
            for (int i = 0; i < set.weaponOptions.Count; i++)
            {
                WeaponMount o = set.weaponOptions[i];
                if (!IsS3(o))
                    continue;
                int diff = Math.Abs(o.ammo - ammo);
                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    best = o;
                }
            }
            return best;
        }

        static WeaponMount S3Anywhere(int ammo)
        {
            WeaponMount[] all = Resources.FindObjectsOfTypeAll<WeaponMount>();
            WeaponMount best = null;
            int bestDiff = int.MaxValue;
            for (int i = 0; i < all.Length; i++)
            {
                WeaponMount m = all[i];
                if (!IsS3(m))
                    continue;
                int diff = Math.Abs(m.ammo - ammo);
                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    best = m;
                }
            }
            return best;
        }

        static void Flares(Aircraft ac)
        {
            FlareEjector[] flares = ac.GetComponentsInChildren<FlareEjector>(true);
            for (int i = 0; i < flares.Length; i++)
                PrepFlare(flares[i]);
            if (ac.countermeasureManager != null)
                ac.countermeasureManager.UpdateHUD();
        }

        static void PrepFlare(FlareEjector flare)
        {
            if (flare == null)
                return;
            flare.ammo = FlareAmmo;
            if (FlareMax != null && Convert.ToInt32(FlareMax.GetValue(flare)) < FlareAmmo)
                FlareMax.SetValue(flare, FlareAmmo);
            if (FlareGroup != null && Convert.ToInt32(FlareGroup.GetValue(flare)) < 1)
                FlareGroup.SetValue(flare, 2);
            BorrowFlare(flare);
        }

        static void BorrowFlare(FlareEjector flare)
        {
            if (FlarePrefab == null)
                return;
            GameObject prefab = FlarePrefab.GetValue(flare) as GameObject;
            float speed = FlareSpeed != null ? Convert.ToSingle(FlareSpeed.GetValue(flare)) : 1f;
            if (prefab != null && speed > 1f)
                return;
            FlareEjector[] all = Resources.FindObjectsOfTypeAll<FlareEjector>();
            for (int i = 0; i < all.Length; i++)
            {
                FlareEjector donor = all[i];
                if (donor == null || donor == flare)
                    continue;
                GameObject donorPrefab = FlarePrefab.GetValue(donor) as GameObject;
                if (donorPrefab == null)
                    continue;
                if (prefab == null)
                    FlarePrefab.SetValue(flare, donorPrefab);
                CopyIfZero(FlareSpeed, flare, donor);
                CopyIfZero(FlareSpread, flare, donor);
                CopyIfZero(FlareInterval, flare, donor);
                if (FlareSound != null && FlareSound.GetValue(flare) == null)
                    FlareSound.SetValue(flare, FlareSound.GetValue(donor));
                CopyIfZero(FlareVolume, flare, donor);
                return;
            }
        }

        static void CopyIfZero(FieldInfo field, FlareEjector flare, FlareEjector donor)
        {
            if (field == null)
                return;
            float mine = Convert.ToSingle(field.GetValue(flare));
            if (mine > 0.01f)
                return;
            field.SetValue(flare, field.GetValue(donor));
        }

        static void Jammer(Aircraft ac)
        {
            RadarJammer jam = ac.GetComponent<RadarJammer>();
            if (jam == null)
                jam = ac.GetComponentInChildren<RadarJammer>(true);
            if (jam == null)
                jam = ac.gameObject.AddComponent<RadarJammer>();
            RadarJammer donor = DonorJammer(jam);
            if (donor != null)
            {
                CopyJam(JamPower, jam, donor, 50f);
                CopyJam(JamCap, jam, donor, 100f);
                CopyJam(JamStrength, jam, donor, 8f);
                CopyJam(JamVolume, jam, donor, 1f);
                if (JamSound != null && JamSound.GetValue(jam) == null)
                    JamSound.SetValue(jam, JamSound.GetValue(donor));
                if (jam.displayImage == null)
                    jam.displayImage = donor.displayImage;
            }
            else
            {
                SetAtLeast(JamPower, jam, 50f);
                SetAtLeast(JamCap, jam, 100f);
                SetAtLeast(JamStrength, jam, 8f);
                SetAtLeast(JamVolume, jam, 1f);
            }
            if (string.IsNullOrEmpty(jam.displayName))
                jam.displayName = "Radar ECM";
            jam.chargeable = true;
            jam.enabled = true;
            if (jam.aircraft == null)
            {
                jam.AttachToUnit(ac);
                PowerSupply supply = ac.GetPowerSupply();
                if (supply != null)
                    supply.ModifyCapacitance(100f);
            }
        }

        static RadarJammer DonorJammer(RadarJammer mine)
        {
            RadarJammer[] all = Resources.FindObjectsOfTypeAll<RadarJammer>();
            RadarJammer best = null;
            float bestStrength = 0f;
            for (int i = 0; i < all.Length; i++)
            {
                RadarJammer j = all[i];
                if (j == null || j == mine || JamStrength == null)
                    continue;
                float strength = Convert.ToSingle(JamStrength.GetValue(j));
                if (strength > bestStrength)
                {
                    bestStrength = strength;
                    best = j;
                }
            }
            return best;
        }

        static void CopyJam(FieldInfo field, RadarJammer jam, RadarJammer donor, float floor)
        {
            if (field == null)
                return;
            float mine = Convert.ToSingle(field.GetValue(jam));
            float theirs = Convert.ToSingle(field.GetValue(donor));
            if (mine < floor)
                field.SetValue(jam, theirs > floor ? theirs : floor);
        }

        static void SetAtLeast(FieldInfo field, RadarJammer jam, float floor)
        {
            if (field == null)
                return;
            if (Convert.ToSingle(field.GetValue(jam)) < floor)
                field.SetValue(jam, floor);
        }

        static void ShowRb(LoadoutSelector selector)
        {
            Aircraft ac = SelectorAircraft != null ? SelectorAircraft.GetValue(selector) as Aircraft : null;
            if (!PitchTune.IsOurs(ac) || ac.weaponManager == null || SelectorRows == null)
                return;
            List<WeaponSelector> rows = SelectorRows.GetValue(selector) as List<WeaponSelector>;
            HardpointSet[] sets = ac.weaponManager.hardpointSets;
            if (rows == null || sets == null)
                return;
            WeaponMount shown = null;
            int n = rows.Count < sets.Length ? rows.Count : sets.Length;
            for (int i = 0; i < n; i++)
            {
                if (rows[i] == null || Gun(sets[i]) || Utility(sets[i]))
                    continue;
                WeaponMount m = DrakenArms.For(sets[i], ac.weaponManager);
                if (m == null)
                    continue;
                rows[i].SetValue(m);
                if (shown == null)
                    shown = m;
            }
            if (shown != null)
                selector.ShowMountInfo(shown);
        }

        static bool Gun(HardpointSet set)
        {
            return set != null && set.name != null && set.name.IndexOf("Gun", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool Utility(HardpointSet set)
        {
            if (set == null || set.weaponOptions == null)
                return false;
            bool any = false;
            for (int i = 0; i < set.weaponOptions.Count; i++)
            {
                WeaponMount m = set.weaponOptions[i];
                if (m == null)
                    continue;
                any = true;
                if (m.countermeasure || m.GunAmmo || (m.info != null && (m.info.gun || m.info.jammer)))
                    continue;
                string k = m.jsonKey ?? "";
                if (k == "J35_Rb_24" || k == "J35_Rb_28")
                    return false;
                return false;
            }
            return any;
        }

        [HarmonyPatch(typeof(FlareEjector), "Awake")]
        static class Patch_FlareAmmo
        {
            static void Prefix(FlareEjector __instance)
            {
                try
                {
                    Aircraft ac = __instance.GetComponentInParent<Aircraft>();
                    if (!PitchTune.IsOurs(ac))
                        return;
                    __instance.ammo = FlareAmmo;
                    if (FlareGroup != null && Convert.ToInt32(FlareGroup.GetValue(__instance)) < 1)
                        FlareGroup.SetValue(__instance, 2);
                }
                catch { }
            }
        }

        [HarmonyPatch(typeof(LoadoutSelector), nameof(LoadoutSelector.AssignAircraft))]
        static class Patch_HangarOpen
        {
            static void Prefix(Aircraft aircraft)
            {
                try
                {
                    if (aircraft != null && aircraft.weaponManager != null)
                        DrakenArms.ClaimOne(aircraft.weaponManager);
                }
                catch { }
            }
        }

        [HarmonyPatch(typeof(WeaponSelector), "Initialize", new System.Type[] { typeof(Aircraft), typeof(HardpointSet), typeof(FactionHQ), typeof(Airbase) })]
        static class Patch_HangarRow
        {
            static void Prefix(Aircraft aircraft, HardpointSet hardpointSet)
            {
                try
                {
                    if (aircraft != null)
                        DrakenArms.Prepare(aircraft.weaponManager, hardpointSet);
                }
                catch { }
            }
        }

        [HarmonyPatch(typeof(LoadoutSelector), "LoadDefaults")]
        static class Patch_LoadDefaults
        {
            static void Prefix(LoadoutSelector __instance)
            {
                try
                {
                    Aircraft ac = SelectorAircraft != null ? SelectorAircraft.GetValue(__instance) as Aircraft : null;
                    if (ac != null)
                        DrakenArms.ClaimOne(ac.weaponManager);
                }
                catch { }
            }

            static void Postfix(LoadoutSelector __instance)
            {
                try { ShowRb(__instance); }
                catch { }
            }
        }

        [HarmonyPatch(typeof(WeaponManager), "InitializeWeaponManager")]
        static class Patch_InitWeapons
        {
            static void Prefix(WeaponManager __instance)
            {
                try
                {
                    Aircraft ac = ManagerAircraft != null ? ManagerAircraft.GetValue(__instance) as Aircraft : __instance.GetComponent<Aircraft>();
                    if (ac != null)
                        Stamp(ac);
                    DrakenArms.ClaimOne(__instance);
                }
                catch { }
            }
        }

        [HarmonyPatch(typeof(Aircraft), "OnStartClient")]
        static class Patch_SpawnLoadout
        {
            static void Prefix(Aircraft __instance)
            {
                try { Stamp(__instance); }
                catch { }
            }
        }

        [HarmonyPatch(typeof(Gun), "LoadAmmunition")]
        static class Patch_GunLoad
        {
            static void Postfix(Gun __instance)
            {
                try { StampGun(__instance); }
                catch { }
            }
        }

        [HarmonyPatch(typeof(WeaponManager), "SpawnWeapons")]
        static class Patch_GunSpawn
        {
            static void Postfix(WeaponManager __instance)
            {
                try
                {
                    Aircraft ac = ManagerAircraft != null ? ManagerAircraft.GetValue(__instance) as Aircraft : __instance.GetComponent<Aircraft>();
                    if (!PitchTune.IsOurs(ac))
                        return;
                    PlaceGun(ac);
                    Gun[] guns = ac.GetComponentsInChildren<Gun>(true);
                    for (int i = 0; i < guns.Length; i++)
                        StampGun(guns[i]);
                }
                catch { }
            }
        }
    }
}
