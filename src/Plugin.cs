using BepInEx;
using BepInEx.Logging;
using System;
using System.Reflection;
using HarmonyLib;

namespace J35XE
{
    internal static class PluginInfo
    {
        public const string GUID = "com.ial.j35xe";
        public const string Name = "J-35XE";
        public const string Version = "2.6.62";
    }

    /// <summary>
    /// J-35XE (jsonKey SaabJ35) runtime code. BIA.Runtime is frozen at 1.0.29;
    /// anything J-35 specific from now on lives here.
    /// </summary>
    [BepInPlugin(PluginInfo.GUID, PluginInfo.Name, PluginInfo.Version)]
    [BepInDependency("bia.runtime", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        private static Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            _harmony = new Harmony(PluginInfo.GUID);
            _harmony.PatchAll();
            CoffinMarks.PatchBia(_harmony);
            SkinList.PatchBia(_harmony);
            Ost.Begin();
            NetFix.Begin(_harmony);
            Log.LogInfo(PluginInfo.Name + " " + PluginInfo.Version + " loaded.");
        }

        static bool _ash;

        static bool PatchAsh(Harmony harmony)
        {
            try
            {
                Type pack = null;
                Assembly[] all = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i].GetName().Name != "AshWeaponPack")
                        continue;
                    pack = all[i].GetType("AshWeaponPack.Pack");
                    break;
                }
                if (pack == null)
                    return false;
                MethodInfo filter = AccessTools.Method(pack, "FilterAvailable");
                if (filter != null)
                    harmony.Patch(filter, postfix: new HarmonyMethod(typeof(DrakenArms), nameof(DrakenArms.AfterFilter)));
                return true;
            }
            catch (Exception ex)
            {
                if (Log != null)
                    Log.LogWarning("J-35XE ash exclusive: " + ex.Message);
                return true;
            }
        }

        private void Update()
        {
            if (!_ash)
                _ash = PatchAsh(_harmony);
            Ost.Tick();
            NetFix.Tick();
            DrakenArms.Tick();
            AiFly.Tick();
        }
    }
}
