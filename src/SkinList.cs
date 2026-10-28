using System;
using System.Collections.Generic;
using HarmonyLib;
using NuclearOption.AddressableScripts;
using NuclearOption.AddressableScripts.ModFoldersImpl;
using Steamworks;
using UnityEngine;

namespace J35XE
{
    /// <summary>
    /// Imported liveries were invisible in the hangar list when Faction was PALA
    /// (the game id is Primeva) or the aircraft key was not exactly SaabJ35.
    /// The paint that did load only wrote _Livery. BIA leaves Trinity in _Basecolor,
    /// and that is the base the shader still shows.
    /// </summary>
    internal static class SkinList
    {
        struct Paint
        {
            public Texture Tex;
            public float Gloss;
        }

        static readonly Dictionary<int, Paint> _paint = new Dictionary<int, Paint>();

        internal static bool IsJ35(AircraftDefinition def)
        {
            if (def == null)
                return false;
            if (string.Equals(def.jsonKey, PitchTune.JsonKey, StringComparison.Ordinal))
                return true;
            return def.unitName != null && def.unitName.StartsWith("J-35XE", StringComparison.Ordinal);
        }
        internal static bool Has(Aircraft ac)
        {
            return ac != null && _paint.ContainsKey(ac.GetInstanceID());
        }

        internal static void Note(Aircraft ac, Texture tex, float gloss)
        {
            if (ac == null || tex == null)
                return;
            _paint[ac.GetInstanceID()] = new Paint { Tex = tex, Gloss = gloss };
            Apply(ac, tex, gloss);
        }

        internal static void Apply(Aircraft ac, Texture tex, float gloss)
        {
            if (ac == null || tex == null)
                return;
            Renderer[] rs = ac.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rs.Length; i++)
            {
                Renderer r = rs[i];
                if (r == null)
                    continue;
                string kind = r.GetType().Name;
                if (kind == "ParticleSystemRenderer" || kind == "TrailRenderer" || kind == "LineRenderer")
                    continue;
                Material[] shared = r.sharedMaterials;
                bool need = false;
                for (int m = 0; m < shared.Length; m++)
                {
                    Material mat = shared[m];
                    if (mat != null && TakesPaint(mat) && mat.HasProperty("_Basecolor") && mat.GetTexture("_Basecolor") != tex)
                        need = true;
                }
                if (!need)
                    continue;
                Material[] mats = r.materials;
                bool changed = false;
                for (int m = 0; m < mats.Length; m++)
                {
                    Material mat = mats[m];
                    if (mat == null || !TakesPaint(mat))
                        continue;
                    Set(mat, "_Basecolor", tex);
                    Set(mat, "_BasecolorDmg", tex);
                    Set(mat, "_BaseMap", tex);
                    Set(mat, "_MainTex", tex);
                    Set(mat, "_Livery", tex);
                    if (gloss > 0f && mat.HasProperty("_Glossiness"))
                        mat.SetFloat("_Glossiness", gloss);
                    changed = true;
                }
                if (changed)
                    r.materials = mats;
            }
        }

        internal static void Restore(Aircraft ac)
        {
            if (ac == null || !_paint.TryGetValue(ac.GetInstanceID(), out Paint p))
                return;
            Apply(ac, p.Tex, p.Gloss);
        }

        internal static bool Ours(LiveryMetaData data)
        {
            return Hit(data.Aircraft) || Hit(data.AircraftKey);
        }

        internal static bool FactionOk(string livery, string aircraft)
        {
            if (string.IsNullOrEmpty(livery) || livery == "None" || livery == "Neutral")
                return true;
            if (string.IsNullOrEmpty(aircraft) || aircraft == "None" || aircraft == "Neutral")
                return true;
            return Side(livery) == Side(aircraft);
        }

        static bool Hit(string s)
        {
            if (string.IsNullOrEmpty(s))
                return false;
            if (s.IndexOf("J-35", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (s.IndexOf("J35", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return string.Equals(s, "SaabJ35", StringComparison.OrdinalIgnoreCase);
        }

        static string Side(string s)
        {
            if (string.Equals(s, "PALA", StringComparison.OrdinalIgnoreCase)
                || string.Equals(s, "Primeva", StringComparison.OrdinalIgnoreCase))
                return "primeva";
            if (string.Equals(s, "BDF", StringComparison.OrdinalIgnoreCase)
                || string.Equals(s, "Boscali", StringComparison.OrdinalIgnoreCase))
                return "boscali";
            return s.ToLowerInvariant();
        }

        static bool TakesPaint(Material mat)
        {
            string shader = mat.shader != null ? mat.shader.name : "";
            if (shader.IndexOf("canopy", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            if (shader.IndexOf("AircraftSkin", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return mat.HasProperty("_Livery") || mat.HasProperty("_Basecolor");
        }

        static void Set(Material mat, string slot, Texture tex)
        {
            if (mat.HasProperty(slot))
                mat.SetTexture(slot, tex);
        }

        internal static void PatchBia(Harmony harmony)
        {
            System.Type t = AccessTools.TypeByName("BIA.BIAAircraftSkin");
            if (t == null)
                return;
            System.Reflection.MethodInfo apply = AccessTools.Method(t, "Apply");
            if (apply == null)
                return;
            harmony.Patch(apply, postfix: new HarmonyMethod(typeof(SkinList), nameof(AfterBia)));
        }

        static void AfterBia(Component __instance)
        {
            if (__instance == null)
                return;
            Aircraft ac = __instance.GetComponent<Aircraft>();
            if (ac == null)
                ac = __instance.GetComponentInParent<Aircraft>();
            Restore(ac);
        }
    }

    [HarmonyPatch(typeof(UnitPart), nameof(UnitPart.SetLivery))]
    internal static class Patch_J35_LiveryPaint
    {
        static void Postfix(UnitPart __instance, LiveryData livery)
        {
            if (livery == null || livery.Texture == null || __instance == null)
                return;
            Aircraft ac = __instance.parentUnit as Aircraft;
            if (ac == null)
                ac = __instance.GetComponentInParent<Aircraft>();
            if (!PitchTune.IsOurs(ac))
                return;
            SkinList.Note(ac, livery.Texture, livery.Glossiness);
        }
    }

    [HarmonyPatch(typeof(LiveryMetaData), nameof(LiveryMetaData.CheckAircraft))]
    internal static class Patch_J35_LiveryMatch
    {
        static void Postfix(LiveryMetaData __instance, AircraftDefinition aircraft, ref bool __result)
        {
            if (__result || !SkinList.IsJ35(aircraft))
                return;
            if (SkinList.Ours(__instance))
                __result = true;
        }
    }

    [HarmonyPatch(typeof(Skins), "CheckMetaData")]
    internal static class Patch_J35_LiveryFaction
    {
        static void Postfix(LiveryMetaData data, AircraftDefinition aircraft, string faction, ref bool __result)
        {
            if (__result || !SkinList.IsJ35(aircraft) || !SkinList.Ours(data))
                return;
            if (SkinList.FactionOk(data.Faction, faction))
                __result = true;
        }
    }

    [HarmonyPatch(typeof(LoadoutSelector), nameof(LoadoutSelector.GetLiveryOptions))]
    internal static class Patch_J35_LiveryList
    {
        static void Prefix(AircraftDefinition aircraft)
        {
            if (SkinList.IsJ35(aircraft))
                ModLoadCache.HasSkinMetaData = false;
        }

        static void Postfix(List<(LiveryKey key, string label)> resultsList, AircraftDefinition aircraft, string aircraftFaction)
        {
            if (resultsList == null || !SkinList.IsJ35(aircraft) || !ModLoadCache.HasSkinMetaData)
                return;
            for (int i = 0; i < ModLoadCache.SkinMetaData.Count; i++)
            {
                LiveryMetaData meta = ModLoadCache.SkinMetaData[i];
                if (!SkinList.Ours(meta) || !SkinList.FactionOk(meta.Faction, aircraftFaction))
                    continue;
                bool workshop = meta.Id != PublishedFileId_t.Invalid;
                LiveryKey key = new LiveryKey(meta, workshop);
                bool have = false;
                for (int j = 0; j < resultsList.Count; j++)
                {
                    LiveryKey old = resultsList[j].key;
                    if (old.Type == key.Type && old.Id == key.Id && old.AppDataName == key.AppDataName)
                    {
                        have = true;
                        break;
                    }
                }
                if (have)
                    continue;
                string suffix = workshop ? " (workshop)" : " (app data)";
                resultsList.Add((key, meta.DisplayName + suffix));
            }
        }
    }

/// <summary>
/// Keeps a chosen livery in the base slot. The game writes only _Livery, and BIA
/// puts Trinity back into _Basecolor afterwards, so the hull stays Trinity underneath.
/// </summary>
internal sealed class LiveryLatch : MonoBehaviour
{
    Aircraft _ac;
    Texture _stock;

    internal void Init(Aircraft ac)
    {
        _ac = ac;
        _stock = Hull(ac);
    }

    void LateUpdate()
    {
        if (_ac == null)
            return;
        if (SkinList.Has(_ac))
        {
            SkinList.Restore(_ac);
            return;
        }
        Texture custom = Foreign(ac: _ac, stock: _stock);
        if (custom != null)
            SkinList.Note(_ac, custom, 0.32f);
    }

    static Texture Hull(Aircraft ac)
    {
        Renderer[] rs = ac.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < rs.Length; i++)
        {
            Renderer r = rs[i];
            if (r == null)
                continue;
            Material[] mats = r.sharedMaterials;
            for (int m = 0; m < mats.Length; m++)
            {
                Material mat = mats[m];
                if (mat == null || !mat.HasProperty("_Basecolor"))
                    continue;
                Texture tex = mat.GetTexture("_Basecolor");
                if (tex != null)
                    return tex;
            }
        }
        return null;
    }

    static Texture Foreign(Aircraft ac, Texture stock)
    {
        Renderer[] rs = ac.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < rs.Length; i++)
        {
            Renderer r = rs[i];
            if (r == null)
                continue;
            Material[] mats = r.sharedMaterials;
            for (int m = 0; m < mats.Length; m++)
            {
                Material mat = mats[m];
                if (mat == null || !mat.HasProperty("_Livery"))
                    continue;
                Texture tex = mat.GetTexture("_Livery");
                if (tex != null && tex != stock)
                    return tex;
            }
        }
        return null;
    }
}
}
