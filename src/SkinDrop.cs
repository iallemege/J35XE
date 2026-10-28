using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using UnityEngine;

namespace J35XE
{
    /// <summary>
    /// PALA default hull. The game's faction id is Primeva, so a livery whose Faction
    /// field says PALA is rejected. Drop the 4096 hull sheet on
    /// BepInEx/plugins/J35XE/Skins/PALA.png and every Primeva J-35XE uses it.
    /// </summary>
    internal static class SkinDrop
    {
        static Texture2D _pala;
        static bool _loaded;

        internal static Texture2D Pala
        {
            get
            {
                if (!_loaded)
                    Load();
                return _pala;
            }
        }

        internal static bool IsPala(Aircraft ac)
        {
            try
            {
                string name = ac != null && ac.NetworkHQ != null && ac.NetworkHQ.faction != null
                    ? ac.NetworkHQ.faction.factionName : "";
                return string.Equals(name, "Primeva", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        static void Load()
        {
            _loaded = true;
            string dir = Path.Combine(Paths.PluginPath, "J35XE", "Skins");
            string path = Path.Combine(dir, "PALA.png");
            if (!File.Exists(path))
                path = Path.Combine(dir, "Primeva.png");
            if (!File.Exists(path))
                return;
            byte[] bytes;
            try { bytes = File.ReadAllBytes(path); }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("J-35XE PALA skin: " + ex.Message);
                return;
            }
            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, true, false);
            if (!ImageConversion.LoadImage(tex, bytes, false))
            {
                UnityEngine.Object.Destroy(tex);
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("J-35XE PALA skin is not a readable PNG: " + path);
                return;
            }
            tex.name = "J-35XE PALA";
            tex.wrapMode = TextureWrapMode.Clamp;
            _pala = tex;
            if (Plugin.Log != null)
                Plugin.Log.LogInfo("J-35XE PALA skin " + tex.width + "x" + tex.height + " from " + path);
        }
    }

    internal sealed class PalaSkin : MonoBehaviour
    {
        Aircraft _ac;
        bool _done;

        internal void Init(Aircraft ac)
        {
            _ac = ac;
            StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            float until = Time.time + 4f;
            while (Time.time < until && !SkinDrop.IsPala(_ac))
                yield return null;
            if (!SkinDrop.IsPala(_ac) || SkinDrop.Pala == null)
                yield break;
            yield return null;
            yield return null;
            yield return null;
            Apply();
        }

        void Apply()
        {
            if (_done || _ac == null || SkinDrop.Pala == null || SkinList.Has(_ac))
                return;
            Texture hull = HullTexture();
            if (hull == null)
                return;
            MeshRenderer[] rs = _ac.GetComponentsInChildren<MeshRenderer>(true);
            int n = 0;
            for (int i = 0; i < rs.Length; i++)
            {
                MeshRenderer r = rs[i];
                if (r == null)
                    continue;
                Material[] mats = r.materials;
                bool changed = false;
                for (int m = 0; m < mats.Length; m++)
                {
                    if (mats[m] == null || Albedo(mats[m]) != hull)
                        continue;
                    Paint(mats[m], SkinDrop.Pala);
                    changed = true;
                    n++;
                }
                if (changed)
                    r.materials = mats;
            }
            _done = n > 0;
        }

        Texture HullTexture()
        {
            var count = new Dictionary<Texture, int>();
            MeshRenderer[] rs = _ac.GetComponentsInChildren<MeshRenderer>(true);
            Texture best = null;
            int bestN = 0;
            for (int i = 0; i < rs.Length; i++)
            {
                MeshRenderer r = rs[i];
                if (r == null)
                    continue;
                Material[] mats = r.sharedMaterials;
                for (int m = 0; m < mats.Length; m++)
                {
                    Texture tex = Albedo(mats[m]);
                    if (tex == null)
                        continue;
                    int n = count.TryGetValue(tex, out int c) ? c + 1 : 1;
                    count[tex] = n;
                    if (n > bestN)
                    {
                        bestN = n;
                        best = tex;
                    }
                }
            }
            return bestN >= 2 ? best : null;
        }

        static Texture Albedo(Material mat)
        {
            if (mat == null)
                return null;
            if (mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") != null)
                return mat.GetTexture("_BaseMap");
            if (mat.HasProperty("_MainTex") && mat.GetTexture("_MainTex") != null)
                return mat.GetTexture("_MainTex");
            if (mat.HasProperty("_Basecolor") && mat.GetTexture("_Basecolor") != null)
                return mat.GetTexture("_Basecolor");
            return mat.mainTexture;
        }

        static void Paint(Material mat, Texture tex)
        {
            string[] slots = { "_BaseMap", "_MainTex", "_Basecolor", "_Livery" };
            for (int i = 0; i < slots.Length; i++)
            {
                if (mat.HasProperty(slots[i]))
                    mat.SetTexture(slots[i], tex);
            }
        }
    }
}
