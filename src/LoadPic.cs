using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using OritasySeries;
using UnityEngine;
using UnityEngine.UI;

namespace J35XE
{
    /// <summary>
    /// Mission loading screens. The pictures live in J35XE.ssbag (load/*.jpg, load/*.png).
    /// Each time the screen opens, one is chosen and scaled to cover the whole screen.
    /// </summary>
    static class LoadPic
    {
        static Sprite[] _arts = System.Array.Empty<Sprite>();
        static Sprite _last;
        static bool _tried;
        static FieldInfo _image;

        internal static Sprite Last => _last;

        internal static Sprite Pick()
        {
            if (!_tried) Load();
            if (_arts.Length == 0) return null;
            _last = _arts[Random.Range(0, _arts.Length)];
            return _last;
        }

        static void Load()
        {
            _tried = true;
            SsBag bag = SsBag.OpenBeside(Assembly.GetExecutingAssembly().Location, "J35XE.ssbag");
            var sprites = new List<Sprite>();
            if (bag.Ok)
            {
                foreach (string name in bag.Names)
                {
                    string e = Path.GetExtension(name);
                    if (name.IndexOf("load/", System.StringComparison.OrdinalIgnoreCase) != 0
                        && name.IndexOf("load\\", System.StringComparison.OrdinalIgnoreCase) != 0)
                        continue;
                    if (!e.Equals(".jpg", System.StringComparison.OrdinalIgnoreCase)
                        && !e.Equals(".jpeg", System.StringComparison.OrdinalIgnoreCase)
                        && !e.Equals(".png", System.StringComparison.OrdinalIgnoreCase))
                        continue;
                    Texture2D tex = bag.Tex(name);
                    if (tex == null) continue;
                    tex.name = "J-35XE " + Path.GetFileName(name);
                    Sprite art = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                    art.name = tex.name;
                    sprites.Add(art);
                }
            }
            _arts = sprites.ToArray();
            if (Plugin.Log != null)
                Plugin.Log.LogInfo("J-35XE load images " + _arts.Length + (bag.Ok ? "" : " (ssbag missing)"));
        }

        internal static void Fit(LoadingScreen screen, Sprite art)
        {
            if (art == null || screen == null) return;
            if (_image == null)
                _image = AccessTools.Field(typeof(LoadingScreen), "loadingImage");
            Image img = _image != null ? _image.GetValue(screen) as Image : null;
            if (img == null || img.sprite != art) return;
            img.type = Image.Type.Simple;
            img.preserveAspect = false;
            RectTransform rt = img.rectTransform;
            RectTransform parent = rt.parent as RectTransform;
            Rect area = parent != null ? parent.rect : new Rect(0f, 0f, Screen.width, Screen.height);
            float pw = Mathf.Max(1f, area.width);
            float ph = Mathf.Max(1f, area.height);
            float aspect = art.rect.width / Mathf.Max(1f, art.rect.height);
            float frame = pw / ph;
            float w, h;
            if (aspect > frame)
            {
                h = ph;
                w = ph * aspect;
            }
            else
            {
                w = pw;
                h = pw / aspect;
            }
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(w, h);
        }
    }

    [HarmonyPatch(typeof(LoadingScreen), nameof(LoadingScreen.ShowLoadingScreen))]
    static class H_LoadPic
    {
        static void Prefix(ref Sprite imageOverride)
        {
            if (imageOverride == null)
            {
                Sprite art = LoadPic.Pick();
                if (art != null) imageOverride = art;
            }
        }

        static void Postfix(LoadingScreen __instance) => LoadPic.Fit(__instance, LoadPic.Last);
    }
}
