using System;
using System.IO;
using BepInEx;
using UnityEngine;
using UnityEngine.Networking;

namespace J35XE
{
    /// <summary>
    /// Takeoff theme. plugins/J35XE/J-35XE.mp3, clip name J-35XE.
    /// The game plays it once when the local jet lifts off with the gear still down.
    /// </summary>
    internal static class Ost
    {
        const string ClipName = "J-35XE";

        static UnityWebRequest _req;
        static AudioClip _clip;

        internal static void Begin()
        {
            string path = Path.Combine(Paths.PluginPath, "J35XE", "J-35XE.mp3");
            if (!File.Exists(path))
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("J-35XE theme not found: " + path);
                return;
            }
            try
            {
                _req = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, AudioType.MPEG);
                if (_req.downloadHandler is DownloadHandlerAudioClip dh)
                    dh.streamAudio = false;
                _req.SendWebRequest();
            }
            catch (Exception ex)
            {
                _req = null;
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("J-35XE theme: " + ex.Message);
            }
        }

        internal static void Tick()
        {
            if (_req == null || !_req.isDone)
                return;
            UnityWebRequest req = _req;
            _req = null;
            try
            {
                if (req.result == UnityWebRequest.Result.Success)
                {
                    _clip = DownloadHandlerAudioClip.GetContent(req);
                    if (_clip != null)
                        _clip.name = ClipName;
                }
                else if (Plugin.Log != null)
                    Plugin.Log.LogWarning("J-35XE theme: " + req.error);
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("J-35XE theme: " + ex.Message);
            }
            finally
            {
                req.Dispose();
            }
            if (_clip == null)
                return;
            Aircraft[] all = UnityEngine.Object.FindObjectsOfType<Aircraft>();
            for (int i = 0; i < all.Length; i++)
                Apply(all[i]);
        }

        internal static void Apply(Aircraft ac)
        {
            if (_clip == null || !PitchTune.IsOurs(ac))
                return;
            AircraftDefinition def = ac.definition as AircraftDefinition;
            if (def != null && def.aircraftParameters != null)
                def.aircraftParameters.takeoffMusic = _clip;
        }
    }
}
