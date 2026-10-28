using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Mirage;
using NuclearOption.Networking;
using UnityEngine;

namespace J35XE
{
    /// <summary>
    /// The baas stamps every network identity on the J-35 with one hash and never registers
    /// the prefab. A second aircraft then collides with the first, a client cannot spawn,
    /// and the copy other players see has no faction, so it is neither friend nor enemy
    /// and its kill line stays on the machine that scored it.
    /// </summary>
    static class NetFix
    {
        static bool _prefab;
        static float _deckAt;
        static FieldInfo _list;
        static FieldInfo _hash;

        internal static void Begin(Harmony h)
        {
            MethodInfo reg = AccessTools.Method(typeof(NetworkManagerNuclearOption), "RegisterPrefabs");
            if (reg != null)
                h.Patch(reg, postfix: new HarmonyMethod(typeof(NetFix), nameof(AfterGameRegister)));
            MethodInfo can = AccessTools.Method(typeof(Hangar), "CanSpawnAircraft");
            if (can != null)
                h.Patch(can, postfix: new HarmonyMethod(typeof(NetFix), nameof(AllowHangar)));
            _list = AccessTools.Field(typeof(Hangar), "availableAircraft");
        }

        static void AfterGameRegister()
        {
            _prefab = false;
            Tick();
        }

        static void AllowHangar(ref bool __result, Hangar __instance, AircraftDefinition definition)
        {
            if (definition == null || __instance == null)
                return;
            if (!string.Equals(definition.jsonKey, PitchTune.JsonKey, StringComparison.Ordinal))
                return;
            Unit hull = null;
            try { hull = __instance.attachedUnit; }
            catch { }
            if (hull is Ship)
            {
                __result = Stern(__instance) && Open(__instance);
                return;
            }
            if (__result)
                return;
            try { if (__instance.Available) __result = true; }
            catch { }
        }

        internal static void Tick()
        {
            if (!_prefab)
                Register();
            if (Time.unscaledTime < _deckAt)
                return;
            _deckAt = Time.unscaledTime + 4f;
            Decks();
        }

        static void Register()
        {
            AircraftDefinition def = Find();
            if (def == null || def.unitPrefab == null)
                return;
            NetworkManagerNuclearOption nm = null;
            try { nm = NetworkManagerNuclearOption.i; }
            catch { nm = null; }
            if (nm == null)
                return;
            ClientObjectManager objects = null;
            try { objects = nm.ClientObjectManager; }
            catch { objects = null; }
            if (objects == null)
                return;
            Stamp(def.unitPrefab);
            NetworkIdentity root = def.unitPrefab.GetComponent<NetworkIdentity>();
            if (root == null)
                return;
            try { objects.RegisterPrefab(root); }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("J-35XE prefab register: " + ex.Message);
            }
            _prefab = true;
            if (Plugin.Log != null)
                Plugin.Log.LogInfo("J-35XE network prefab registered, hash " + Read(root));
        }

        static void Decks()
        {
            AircraftDefinition def = Find();
            if (def == null || _list == null)
                return;
            Hangar[] all = UnityEngine.Object.FindObjectsOfType<Hangar>();
            if (all == null)
                return;
            for (int i = 0; i < all.Length; i++)
            {
                Hangar hangar = all[i];
                if (hangar == null)
                    continue;
                Unit hull = null;
                try { hull = hangar.attachedUnit; }
                catch { }
                if (hull is not Ship)
                    continue;
                Listed(hangar, def, Stern(hangar));
            }
        }

        static void Listed(Hangar hangar, AircraftDefinition def, bool allow)
        {
            AircraftDefinition[] arr = null;
            try { arr = _list.GetValue(hangar) as AircraftDefinition[]; }
            catch { return; }
            bool has = false;
            var next = new List<AircraftDefinition>();
            if (arr != null)
            {
                for (int i = 0; i < arr.Length; i++)
                {
                    if (arr[i] == null)
                        continue;
                    if (arr[i] == def)
                    {
                        has = true;
                        if (!allow)
                            continue;
                    }
                    next.Add(arr[i]);
                }
            }
            if (allow && !has)
                next.Add(def);
            if (allow == has)
                return;
            _list.SetValue(hangar, next.ToArray());
        }

        static bool Open(Hangar hangar)
        {
            try { return hangar.Available; }
            catch { return false; }
        }

        static bool Stern(Hangar hangar)
        {
            Unit hull = null;
            try { hull = hangar.attachedUnit; }
            catch { }
            if (hull == null || hull is not Ship)
                return true;
            Transform mine = null;
            try { mine = hangar.GetSpawnTransform(); }
            catch { }
            if (mine == null)
                mine = hangar.transform;
            float z = hull.transform.InverseTransformPoint(mine.position).z;
            Hangar[] all = hull.GetComponentsInChildren<Hangar>(true);
            if (all == null)
                return true;
            for (int i = 0; i < all.Length; i++)
            {
                Hangar other = all[i];
                if (other == null || other == hangar)
                    continue;
                Unit owner = null;
                try { owner = other.attachedUnit; }
                catch { }
                if (owner != hull)
                    continue;
                Transform spawn = null;
                try { spawn = other.GetSpawnTransform(); }
                catch { }
                if (spawn == null)
                    spawn = other.transform;
                float oz = hull.transform.InverseTransformPoint(spawn.position).z;
                if (oz < z - 1f)
                    return false;
            }
            return true;
        }

        static AircraftDefinition Find()
        {
            try
            {
                if (Encyclopedia.Lookup != null
                    && Encyclopedia.Lookup.TryGetValue(PitchTune.JsonKey, out UnitDefinition u)
                    && u is AircraftDefinition ad)
                    return ad;
            }
            catch { }
            AircraftDefinition[] all = Resources.FindObjectsOfTypeAll<AircraftDefinition>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && string.Equals(all[i].jsonKey, PitchTune.JsonKey, StringComparison.Ordinal))
                    return all[i];
            }
            return null;
        }

        static void Stamp(GameObject prefab)
        {
            NetworkIdentity[] ids = prefab.GetComponentsInChildren<NetworkIdentity>(true);
            if (ids == null || ids.Length == 0)
                return;
            int root = Read(ids[0]);
            if (root == 0)
            {
                root = Stable(PitchTune.JsonKey);
                Write(ids[0], root);
            }
            var used = new HashSet<int> { root };
            for (int i = 1; i < ids.Length; i++)
            {
                int h = Read(ids[i]);
                if (h != 0 && used.Add(h))
                    continue;
                int n = root + i;
                while (!used.Add(n))
                    n++;
                Write(ids[i], n);
            }
        }

        static int Read(NetworkIdentity id)
        {
            if (id == null)
                return 0;
            try
            {
                if (_hash == null)
                    _hash = AccessTools.Field(typeof(NetworkIdentity), "_prefabHash");
                if (_hash != null)
                    return (int)_hash.GetValue(id);
            }
            catch { }
            try { return id.PrefabHash; }
            catch { return 0; }
        }

        static void Write(NetworkIdentity id, int hash)
        {
            if (id == null || hash == 0)
                return;
            try
            {
                if (_hash == null)
                    _hash = AccessTools.Field(typeof(NetworkIdentity), "_prefabHash");
                if (_hash != null)
                    _hash.SetValue(id, hash);
            }
            catch { }
            try { id.PrefabHash = hash; }
            catch { }
        }

        static int Stable(string name)
        {
            int n = 17;
            if (name != null)
            {
                for (int i = 0; i < name.Length; i++)
                    n = n * 31 + name[i];
            }
            return n == 0 ? 1 : n;
        }
    }
}
