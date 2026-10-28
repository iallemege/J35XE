using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NuclearOption.SavedMission;
using UnityEngine;

namespace J35XE
{
    /// <summary>
    /// Rb 24 and Rb 28 are part of the J-35. They are cloned from the stock
    /// infrared missile, then given the Ash pack numbers, LOAL, and a relock.
    /// Ash Weapon Pack is not required.
    /// </summary>
    internal static class DrakenArms
    {
        const float Ring = 0.45f;
        const string Desc24 = "Rb 24 is Sweden's AIM-9B/J-class Sidewinder, locally designated for Viggen and Draken. 2072 Rb 24s share the AIM-9J digital actuator kit on Swedish rails.";
        const string Desc28 = "Rb 28 is Sweden's AIM-4 Falcon IR missile for Draken interceptors. In 2072 Rb 28s get a cooled seeker and a digital fuze.";
        static float _next;
        static bool _seeded;
        static WeaponMount _rb24, _rb28;
        static WeaponInfo _info24, _info28;
        static int _pending;
        static float _pendingUntil;
        static bool _loggedDonor;

        internal static void Tick()
        {
            if (_seeded)
                return;
            if (Time.unscaledTime < _next)
                return;
            _next = Time.unscaledTime + 4f;
            Ensure();
            if (_rb24 != null || _rb28 != null)
                _seeded = true;
        }

        internal static void ClaimOne(WeaponManager wm)
        {
            Ensure();
            if (_rb24 == null && _rb28 == null)
                return;
            Claim(wm);
        }

        internal static void Prepare(WeaponManager wm, HardpointSet hs)
        {
            Ensure();
            if (wm == null || hs == null || hs.weaponOptions == null)
                return;
            if (!PitchTune.IsOurs(AircraftOf(wm)))
                return;
            if (Gun(hs) || UtilityOnly(hs))
                return;
            Note(hs);
            OnlyRb(hs.weaponOptions);
        }

        internal static WeaponMount For(HardpointSet set, WeaponManager wm)
        {
            Ensure();
            if (set == null || (_rb24 == null && _rb28 == null))
                return null;
            if (Gun(set))
                return null;
            string n = set.name ?? "";
            bool sidewinder = n.IndexOf("1_6", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("pylon1", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Outer", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!sidewinder && n.IndexOf("2_5", StringComparison.OrdinalIgnoreCase) < 0
                && n.IndexOf("pylon2", StringComparison.OrdinalIgnoreCase) < 0)
                sidewinder = Outer(set, wm);
            if (sidewinder && _rb24 != null)
                return _rb24;
            return _rb28 != null ? _rb28 : _rb24;
        }

        static void Claim(WeaponManager wm)
        {
            if (wm == null || wm.hardpointSets == null)
                return;
            Aircraft ac = AircraftOf(wm);
            bool ours = PitchTune.IsOurs(ac);
            for (int h = 0; h < wm.hardpointSets.Length; h++)
            {
                HardpointSet hs = wm.hardpointSets[h];
                if (hs == null || hs.weaponOptions == null)
                    continue;
                if (!ours)
                {
                    Strip(hs.weaponOptions);
                    continue;
                }
                if (Gun(hs) || UtilityOnly(hs))
                    continue;
                Note(hs);
                OnlyRb(hs.weaponOptions);
            }
            if (!ours || ac.definition is not AircraftDefinition def || def.aircraftParameters == null)
                return;
            AircraftParameters p = def.aircraftParameters;
            if (p.loadouts != null && p.loadouts.Count > 1)
                Write(p.loadouts[1], wm);
            if (ac.loadout != null)
                Write(ac.loadout, wm);
            try
            {
                AircraftCustomization saved;
                if (GameManager.aircraftCustomization.TryGetValue(def, out saved) && saved != null && saved.loadout != null)
                    Write(saved.loadout, wm);
            }
            catch { }
            if (p.StandardLoadouts == null)
                return;
            Loadout empty = p.loadouts != null && p.loadouts.Count > 0 ? p.loadouts[0] : null;
            for (int i = 0; i < p.StandardLoadouts.Length; i++)
            {
                StandardLoadout std = p.StandardLoadouts[i];
                if (std == null || std.loadout == null || std.loadout == empty)
                    continue;
                if (std.Name != null && std.Name.IndexOf("Empty", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                Write(std.loadout, wm);
            }
        }

        static bool UtilityOnly(HardpointSet set)
        {
            if (set == null || set.weaponOptions == null || set.weaponOptions.Count == 0)
                return false;
            bool any = false;
            for (int i = 0; i < set.weaponOptions.Count; i++)
            {
                WeaponMount m = set.weaponOptions[i];
                if (m == null)
                    continue;
                any = true;
                if (!Utility(m))
                    return false;
            }
            return any;
        }

        static bool Utility(WeaponMount m)
        {
            if (m == null)
                return false;
            if (m.countermeasure || m.GunAmmo)
                return true;
            if (m.info != null && (m.info.gun || m.info.jammer))
                return true;
            return false;
        }

        static void OnlyRb(List<WeaponMount> list)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                WeaponMount m = list[i];
                string k = m != null ? m.jsonKey ?? "" : "";
                if (k == "J35_Rb_24" || k == "J35_Rb_28")
                    continue;
                list.RemoveAt(i);
            }
            Add(list, _rb24);
            Add(list, _rb28);
        }

        static void Write(Loadout loadout, WeaponManager wm)
        {
            if (loadout.weapons == null)
                return;
            while (loadout.weapons.Count < wm.hardpointSets.Length)
                loadout.weapons.Add(null);
            for (int i = 0; i < wm.hardpointSets.Length && i < loadout.weapons.Count; i++)
            {
                HardpointSet set = wm.hardpointSets[i];
                WeaponMount cur = loadout.weapons[i];
                if (Keep(cur) || Gun(set))
                    continue;
                WeaponMount m = For(set, wm);
                if (m != null && m.prefab != null)
                    loadout.weapons[i] = m;
            }
        }

        static bool Keep(WeaponMount cur)
        {
            if (cur == null)
                return false;
            string k = cur.jsonKey ?? "";
            if (k == "J35_Rb_24" || k == "J35_Rb_28")
                return true;
            if (cur.countermeasure || cur.GunAmmo)
                return true;
            if (cur.info != null && (cur.info.gun || cur.info.troops || cur.info.cargo))
                return true;
            return false;
        }

        static bool Outer(HardpointSet set, WeaponManager wm)
        {
            float x = AbsX(set);
            float max = 0f;
            int rings = 0;
            if (wm != null && wm.hardpointSets != null)
            {
                for (int i = 0; i < wm.hardpointSets.Length; i++)
                {
                    HardpointSet hs = wm.hardpointSets[i];
                    if (hs == null || Gun(hs))
                        continue;
                    float a = AbsX(hs);
                    if (a > max)
                        max = a;
                    rings++;
                }
            }
            if (rings <= 2)
                return x >= max - Ring;
            return x >= max - Ring;
        }

        static float AbsX(HardpointSet set)
        {
            if (set == null || set.hardpoints == null || set.hardpoints.Count == 0 || set.hardpoints[0] == null)
                return 0f;
            return Mathf.Abs(set.hardpoints[0].transform.localPosition.x);
        }

        static bool Gun(HardpointSet set)
        {
            return set != null && !string.IsNullOrEmpty(set.name)
                && set.name.IndexOf("Gun", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static void Add(List<WeaponMount> list, WeaponMount m)
        {
            if (m != null && !list.Contains(m))
                list.Add(m);
        }

        static void Strip(List<WeaponMount> list)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (Exclusive(list[i]))
                    list.RemoveAt(i);
            }
        }

        static bool Exclusive(WeaponMount m)
        {
            if (m == null)
                return false;
            string k = m.jsonKey ?? "";
            if (k == "J35_Rb_24" || k == "J35_Rb_28")
                return false;
            string n = m.mountName ?? "";
            if (k.IndexOf("28C", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("28C", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            return k.IndexOf("Rb_24", StringComparison.OrdinalIgnoreCase) >= 0
                || k.IndexOf("Rb_28", StringComparison.OrdinalIgnoreCase) >= 0
                || n.StartsWith("Rb 24", StringComparison.OrdinalIgnoreCase)
                || n.StartsWith("Rb 28", StringComparison.OrdinalIgnoreCase);
        }

        static void Ensure()
        {
            if (_rb24 != null && _rb28 != null)
                return;
            WeaponMount donor = Donor();
            if (donor == null || donor.info == null)
            {
                if (!_loggedDonor && Time.unscaledTime > 8f)
                {
                    _loggedDonor = true;
                    if (Plugin.Log != null)
                        Plugin.Log.LogWarning("J-35XE Rb: no stock infrared missile to clone yet.");
                }
                return;
            }
            if (_rb24 == null)
                _rb24 = Make(donor, "J35_Rb_24", "Rb 24 Sidewinder", "Rb 24",
                    "Rb 24 is Sweden's AIM-9B/J-class Sidewinder, locally designated for Viggen and Draken. 2072 Rb 24s share the AIM-9J digital actuator kit on Swedish rails.",
                    8000f, 2f, 76f, 7f, 1f, 18f, 42.5f, 22.5f, 36.9f, false, out _info24);
            if (_rb28 == null)
                _rb28 = Make(donor, "J35_Rb_28", "Rb 28 Falcon", "Rb 28",
                    "Rb 28 is Sweden's AIM-4 Falcon IR missile for Draken interceptors. In 2072 Rb 28s get a cooled seeker and a digital fuze.",
                    16000f, 2.5f, 61f, 9.5f, 1.16f, 32f, 64.34f, 34.06f, 46f, true, out _info28);
        }

        static float _donorAfter;

        static WeaponMount Donor()
        {
            if (Time.unscaledTime < _donorAfter)
                return null;
            _donorAfter = Time.unscaledTime + 8f;
            WeaponMount[] all = Resources.FindObjectsOfTypeAll<WeaponMount>();
            WeaponMount named = null;
            WeaponMount any = null;
            if (all == null)
                return null;
            for (int i = 0; i < all.Length; i++)
            {
                WeaponMount m = all[i];
                if (!SingleIr(m))
                    continue;
                string k = m.jsonKey ?? "";
                if (k == "AAM2" || k == "AAM-2" || k.StartsWith("AAM2", StringComparison.OrdinalIgnoreCase))
                    return m;
                string name = (m.info.weaponName ?? "") + " " + (m.info.shortName ?? "") + " " + (m.mountName ?? "");
                if (name.IndexOf("AAM-2", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("AAM2", StringComparison.OrdinalIgnoreCase) >= 0)
                    named = m;
                else if (any == null)
                    any = m;
            }
            return named != null ? named : any;
        }

        static bool SingleIr(WeaponMount m)
        {
            if (m == null || m.info == null || !m.info.missile || m.info.gun || m.ammo != 1)
                return false;
            string k = m.jsonKey ?? "";
            if (k.StartsWith("J35_", StringComparison.Ordinal) || k.StartsWith("Ash_", StringComparison.Ordinal))
                return false;
            if (k.IndexOf("double", StringComparison.OrdinalIgnoreCase) >= 0
                || k.IndexOf("triple", StringComparison.OrdinalIgnoreCase) >= 0
                || k.IndexOf("internal", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            string blob = k + " " + (m.info.weaponName ?? "") + " " + (m.info.shortName ?? "") + " " + (m.mountName ?? "");
            if (blob.IndexOf("AAM-2", StringComparison.OrdinalIgnoreCase) >= 0
                || blob.IndexOf("AAM2", StringComparison.OrdinalIgnoreCase) >= 0
                || blob.IndexOf("IRM", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            GameObject body = m.info.weaponPrefab != null ? m.info.weaponPrefab : m.prefab;
            return body != null && body.GetComponentInChildren<IRSeeker>(true) != null;
        }

        static WeaponMount Make(WeaponMount donor, string key, string name, string shortName, string description, float rangeM, float mach, float mass, float burn, float thrustMul, float g, float blast, float pierce, float gimbal, bool falcon, out WeaponInfo info)
        {
            info = UnityEngine.Object.Instantiate(donor.info);
            info.name = "WeaponInfo_" + key;
            info.hideFlags = HideFlags.DontUnloadUnusedAsset;
            info.weaponName = name + " [OAS-M]";
            info.shortName = shortName + " [OAS-M]";
            info.description = description;
            info.massPerRound = mass;
            info.maxSpeed = mach * 340.3f;
            info.muzzleVelocity = mach * 340.3f;
            info.blastDamage = blast;
            info.pierceDamage = pierce;
            info.missile = true;
            info.overHorizon = true;
            info.boresight = false;
            try
            {
                TargetRequirements tr = info.targetRequirements;
                tr.maxRange = rangeM;
                tr.minRange = 0f;
                tr.lineOfSight = false;
                info.targetRequirements = tr;
            }
            catch { }
            try
            {
                RoleIdentity e = info.effectiveness;
                e.antiAir = 1f;
                info.effectiveness = e;
            }
            catch { }
            Sprite icon = RbMesh.Card();
            if (icon != null)
                info.weaponIcon = icon;
            WeaponMount clone = UnityEngine.Object.Instantiate(donor);
            clone.name = key + "_Mount";
            clone.jsonKey = key;
            clone.mountName = shortName + " [OAS-M]";
            clone.hideFlags = HideFlags.DontUnloadUnusedAsset;
            clone.prefab = donor.prefab != null ? donor.prefab : donor.info.weaponPrefab;
            if (info.weaponPrefab == null)
                info.weaponPrefab = donor.info.weaponPrefab;
            clone.ammo = 1;
            clone.mass = mass;
            clone.info = info;
            Register(clone);
            RbGuide.Remember(info, burn, thrustMul, g, blast, pierce, gimbal, rangeM);
            return clone;
        }

        internal static void Restore(WeaponMount mount)
        {
            if (mount == null)
                return;
            string k = mount.jsonKey ?? "";
            WeaponInfo info = k == "J35_Rb_28" ? _info28 : k == "J35_Rb_24" ? _info24 : null;
            if (info == null)
                return;
            Sprite icon = RbMesh.Card();
            if (icon != null)
                info.weaponIcon = icon;
            mount.info = info;
            mount.ammo = 1;
            mount.mass = info.massPerRound;
            mount.mountName = info.shortName;
            info.description = k == "J35_Rb_28" ? Desc28 : Desc24;
            info.weaponName = k == "J35_Rb_28" ? "Rb 28 Falcon [OAS-M]" : "Rb 24 Sidewinder [OAS-M]";
            info.shortName = mount.mountName;
            if (mount.prefab == null && info.weaponPrefab != null)
                mount.prefab = info.weaponPrefab;
        }

        internal static void Push(Weapon weapon, WeaponMount mount)
        {
            Restore(mount);
            if (weapon == null || weapon is Gun || mount == null || mount.info == null)
                return;
            string k = mount.jsonKey ?? "";
            if (k != "J35_Rb_24" && k != "J35_Rb_28")
                return;
            weapon.info = mount.info;
        }

        internal static void Hold(GameObject rack, WeaponMount mount)
        {
            Restore(mount);
            if (rack == null || mount == null)
                return;
            Weapon[] rails = rack.GetComponentsInChildren<Weapon>(true);
            for (int i = 0; i < rails.Length; i++)
                Push(rails[i], mount);
        }

        static void Register(WeaponMount clone)
        {
            if (clone == null || string.IsNullOrEmpty(clone.jsonKey))
                return;
            Encyclopedia enc = null;
            try { enc = Encyclopedia.i; }
            catch { }
            if (enc != null && enc.weaponMounts != null && !enc.weaponMounts.Contains(clone))
                enc.weaponMounts.Add(clone);
            try
            {
                if (Encyclopedia.WeaponLookup != null && !Encyclopedia.WeaponLookup.ContainsKey(clone.jsonKey))
                    Encyclopedia.WeaponLookup[clone.jsonKey] = clone;
            }
            catch { }
        }

        internal static void NoteFire(WeaponInfo info)
        {
            _pending = 0;
            if (info == null)
                return;
            if (info == _info24)
                _pending = 24;
            else if (info == _info28)
                _pending = 28;
            if (_pending != 0)
                _pendingUntil = Time.unscaledTime + 0.5f;
        }

        internal static void NoteSpawn(Missile missile)
        {
            int kind = _pending;
            _pending = 0;
            if (kind == 0 || missile == null || Time.unscaledTime > _pendingUntil)
                return;
            WeaponInfo info = kind == 24 ? _info24 : _info28;
            if (info == null)
                return;
            try
            {
                FieldInfo f = AccessTools.Field(typeof(Missile), "info");
                if (f != null)
                    f.SetValue(missile, info);
            }
            catch { }
            try { missile.NetworkunitName = info.weaponName; }
            catch { }
            RbMesh.Apply(missile.gameObject, kind == 28);
            if (missile.GetComponent<RbGuide>() == null)
                missile.gameObject.AddComponent<RbGuide>().Arm(missile, info);
        }

        static Aircraft AircraftOf(WeaponManager wm)
        {
            FieldInfo f = AccessTools.Field(typeof(WeaponManager), "aircraft");
            Aircraft ac = f != null ? f.GetValue(wm) as Aircraft : null;
            if (ac == null)
                ac = wm.GetComponent<Aircraft>();
            if (ac == null)
                ac = wm.GetComponentInParent<Aircraft>();
            return ac;
        }

        internal static void AfterFilter(HardpointSet hs, List<WeaponMount> list)
        {
            if (hs == null || list == null)
                return;
            Strip(list);
            if (!BelongsToJ35(hs))
                return;
            Ensure();
            Add(list, _rb24);
            Add(list, _rb28);
        }

        static readonly List<HardpointSet> Stations = new List<HardpointSet>();

        static void Note(HardpointSet hs)
        {
            if (hs == null)
                return;
            for (int i = 0; i < Stations.Count; i++)
            {
                if (ReferenceEquals(Stations[i], hs))
                    return;
            }
            Stations.Add(hs);
        }

        static bool BelongsToJ35(HardpointSet hs)
        {
            if (hs == null)
                return false;
            for (int i = 0; i < Stations.Count; i++)
            {
                if (ReferenceEquals(Stations[i], hs))
                    return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Lock-on after launch, and a real break followed by another lock, for the J-35's own Rb 24 and Rb 28.
    /// </summary>
    internal sealed class RbGuide : MonoBehaviour
    {
        struct Card
        {
            public float Burn, ThrustMul, G, Blast, Pierce, Gimbal, Range;
        }

        static readonly Dictionary<int, Card> Cards = new Dictionary<int, Card>();
        static readonly FieldInfo Motors = AccessTools.Field(typeof(Missile), "motors");
        static readonly FieldInfo GField = AccessTools.Field(typeof(Missile), "gLimit");
        static readonly FieldInfo TurnField = AccessTools.Field(typeof(Missile), "maxTurnRate");
        static readonly FieldInfo BlastField = AccessTools.Field(typeof(Missile), "blastYield");
        static readonly FieldInfo PierceField = AccessTools.Field(typeof(Missile), "pierceDamage");
        static readonly FieldInfo IrField = AccessTools.Field(typeof(IRSeeker), "IRTarget");
        static readonly List<Unit> Buf = new List<Unit>(64);

        Missile _missile;
        WeaponInfo _info;
        Card _card;
        Unit _target;
        float _relockAt;
        bool _tuned;

        internal static void Remember(WeaponInfo info, float burn, float thrustMul, float g, float blast, float pierce, float gimbal, float range)
        {
            if (info == null)
                return;
            Cards[info.GetInstanceID()] = new Card
            {
                Burn = burn,
                ThrustMul = thrustMul,
                G = g,
                Blast = blast,
                Pierce = pierce,
                Gimbal = gimbal,
                Range = range
            };
        }

        internal void Arm(Missile missile, WeaponInfo info)
        {
            _missile = missile;
            _info = info;
            Card card;
            if (info != null && Cards.TryGetValue(info.GetInstanceID(), out card))
                _card = card;
        }

        void FixedUpdate()
        {
            if (_missile == null || _missile.disabled)
                return;
            if (!_tuned)
                Tune();
            if (FlareHeld() || Time.time < _relockAt)
            {
                if (_target != null)
                    Break(FlareHeld());
                return;
            }
            if (Live(_target) && InCone(_target))
            {
                Guide(_target);
                return;
            }
            if (_target != null)
            {
                Break(false);
                return;
            }
            Unit next = Hunt();
            if (next != null)
                Guide(next);
        }

        void Tune()
        {
            _tuned = true;
            if (BlastField != null)
                BlastField.SetValue(_missile, _card.Blast);
            if (PierceField != null)
                PierceField.SetValue(_missile, _card.Pierce);
            if (GField != null)
                GField.SetValue(_missile, _card.G);
            if (TurnField != null)
                TurnField.SetValue(_missile, _card.G * 2.2f);
            Array motors = Motors != null ? Motors.GetValue(_missile) as Array : null;
            if (motors == null || motors.Length == 0)
                return;
            object motor = motors.GetValue(0);
            if (motor == null)
                return;
            FieldInfo thrust = motor.GetType().GetField("thrust");
            FieldInfo burn = motor.GetType().GetField("burnTime");
            FieldInfo top = motor.GetType().GetField("topSpeed");
            if (thrust != null)
            {
                float t = 0f;
                try { t = (float)thrust.GetValue(motor); }
                catch { }
                if (t < 100f)
                    t = 12000f;
                thrust.SetValue(motor, t * (_card.ThrustMul > 0.1f ? _card.ThrustMul : 1f));
            }
            if (burn != null && _card.Burn > 0.5f)
                burn.SetValue(motor, _card.Burn);
            if (top != null && _info != null)
                top.SetValue(motor, _info.maxSpeed);
        }

        void Guide(Unit unit)
        {
            _target = unit;
            try { _missile.SetTarget(unit); }
            catch { }
            try { _missile.SetAimpoint(unit.transform.position.ToGlobalPosition(), unit.rb != null ? unit.rb.velocity : Vector3.zero); }
            catch { }
            if (IrField == null)
                return;
            IRSeeker ir = _missile.GetComponent<IRSeeker>();
            if (ir == null)
                return;
            IRSource heat = null;
            try { heat = unit.GetIRSource(); }
            catch { }
            if (heat != null && !heat.flare)
                IrField.SetValue(ir, heat);
        }

        void Break(bool keepFlare)
        {
            _target = null;
            _relockAt = Time.time + 0.7f;
            try { _missile.SetTarget(null); }
            catch { }
            if (keepFlare || IrField == null)
                return;
            IRSeeker ir = _missile.GetComponent<IRSeeker>();
            if (ir != null)
                IrField.SetValue(ir, null);
        }

        bool FlareHeld()
        {
            if (IrField == null || _missile == null)
                return false;
            IRSeeker ir = _missile.GetComponent<IRSeeker>();
            if (ir == null)
                return false;
            IRSource src = IrField.GetValue(ir) as IRSource;
            return src != null && src.flare;
        }

        bool InCone(Unit unit)
        {
            if (unit == null || _missile == null)
                return false;
            Vector3 to = unit.transform.position - _missile.transform.position;
            if (to.sqrMagnitude < 4f)
                return true;
            Vector3 fwd = _missile.transform.forward;
            if (_missile.rb != null && _missile.rb.velocity.sqrMagnitude > 900f)
                fwd = _missile.rb.velocity.normalized;
            float lim = _card.Gimbal > 5f ? _card.Gimbal : 35f;
            return Vector3.Angle(fwd, to) <= lim;
        }

        Unit Hunt()
        {
            if (_missile == null)
                return null;
            GlobalPosition origin;
            try { origin = _missile.GlobalPosition(); }
            catch { origin = _missile.transform.position.ToGlobalPosition(); }
            float radius = _card.Range > 100f ? _card.Range : 8000f;
            Buf.Clear();
            try { BattlefieldGrid.GetUnitsInRangeNonAlloc(origin, radius, Buf); }
            catch { return null; }
            Unit best = null;
            float bestScore = float.PositiveInfinity;
            for (int i = 0; i < Buf.Count; i++)
            {
                Unit u = Buf[i];
                if (!Live(u) || !InCone(u))
                    continue;
                float d = (u.transform.position - _missile.transform.position).sqrMagnitude;
                if (d < bestScore)
                {
                    bestScore = d;
                    best = u;
                }
            }
            return best;
        }

        bool Live(Unit unit)
        {
            if (unit == null || unit == _missile || ( _missile != null && unit == _missile.owner))
                return false;
            try
            {
                if (unit.disabled)
                    return false;
            }
            catch { return false; }
            if (unit is Missile || unit is PilotDismounted)
                return false;
            FactionHQ ours = null;
            FactionHQ theirs = null;
            try { if (_missile.owner != null) ours = _missile.owner.NetworkHQ; }
            catch { }
            try { theirs = unit.NetworkHQ; }
            catch { }
            return ours != null && theirs != null && !object.ReferenceEquals(ours, theirs);
        }
    }

    [HarmonyPatch(typeof(WeaponMount), "Initialize")]
    internal static class Patch_J35_RbInit
    {
        static void Postfix(WeaponMount __instance)
        {
            DrakenArms.Restore(__instance);
        }
    }

    [HarmonyPatch(typeof(WeaponManager), "RegisterWeapon")]
    internal static class Patch_J35_RbRegister
    {
        static void Prefix(Weapon weapon, WeaponMount weaponMount)
        {
            DrakenArms.Push(weapon, weaponMount);
        }
    }

    [HarmonyPatch(typeof(WeaponStation), "RegisterWeapon")]
    internal static class Patch_J35_RbStation
    {
        static void Postfix(WeaponStation __instance, Weapon weapon, WeaponMount weaponMount)
        {
            DrakenArms.Push(weapon, weaponMount);
            if (__instance == null || weapon == null || weaponMount == null)
                return;
            string k = weaponMount.jsonKey ?? "";
            if (k != "J35_Rb_24" && k != "J35_Rb_28")
                return;
            if (weapon.info != null)
                __instance.WeaponInfo = weapon.info;
        }
    }

    [HarmonyPatch(typeof(MountedMissile), "Fire")]
    internal static class Patch_J35_RbFire
    {
        static void Prefix(MountedMissile __instance)
        {
            if (__instance != null)
                DrakenArms.NoteFire(__instance.info);
        }
    }

    [HarmonyPatch(typeof(Spawner), "SpawnMissile", new Type[] { typeof(MissileDefinition), typeof(Vector3), typeof(Quaternion), typeof(Vector3), typeof(Unit), typeof(Unit) })]
    internal static class Patch_J35_RbSpawnDef
    {
        static void Postfix(Missile __result)
        {
            DrakenArms.NoteSpawn(__result);
        }
    }

    [HarmonyPatch(typeof(Spawner), "SpawnMissile", new Type[] { typeof(GameObject), typeof(Vector3), typeof(Quaternion), typeof(Vector3), typeof(Unit), typeof(Unit) })]
    internal static class Patch_J35_RbSpawnGo
    {
        static void Postfix(Missile __result)
        {
            DrakenArms.NoteSpawn(__result);
        }
    }
}
