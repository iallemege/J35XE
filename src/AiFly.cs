using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;

namespace J35XE
{
    /// <summary>
    /// Faction AI only launches types that side still has in airframe stock.
    /// J-35XE is kept stocked above the mission reserve so it joins that draw.
    /// A side that has banned the type is left alone.
    /// </summary>
    internal static class AiFly
    {
        const int Spare = 12;
        static readonly FieldInfo Reserve = AccessTools.Field(typeof(FactionHQ), "reserveAirframes");
        static readonly FieldInfo Extra = AccessTools.Field(typeof(FactionHQ), "extraReservesPerPlayer");
        static readonly HashSet<int> Granted = new HashSet<int>();
        static float _next;
        static AircraftDefinition _def;

        internal static void Tick()
        {
            if (Time.unscaledTime < _next)
                return;
            _next = Time.unscaledTime + 8f;
            if (!Host())
                return;
            AircraftDefinition def = Def();
            if (def == null)
                return;
            foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
            {
                if (hq == null)
                    continue;
                int id = hq.GetInstanceID();
                if (Granted.Contains(id))
                    continue;
                if (hq.restrictedAircraft != null && hq.restrictedAircraft.Contains(PitchTune.JsonKey))
                {
                    Granted.Add(id);
                    continue;
                }
                int have;
                try { have = hq.GetUnitSupply(def); }
                catch { continue; }
                int want = Hold(hq) + Spare;
                if (have < want)
                    hq.ModifyUnitSupply(def, want - have);
                Granted.Add(id);
            }
        }

        static int Hold(FactionHQ hq)
        {
            int reserve = 0;
            int extra = 0;
            int players = 0;
            try
            {
                if (Reserve != null)
                    reserve = (int)Reserve.GetValue(hq);
                if (Extra != null)
                    extra = (int)Extra.GetValue(hq);
                if (hq.factionPlayers != null)
                    players = hq.factionPlayers.Count;
            }
            catch { }
            if (reserve < 0)
                reserve = 0;
            if (extra < 0)
                extra = 0;
            return reserve + players * extra;
        }

        static AircraftDefinition Def()
        {
            if (_def != null)
                return _def;
            try
            {
                if (Encyclopedia.Lookup != null
                    && Encyclopedia.Lookup.TryGetValue(PitchTune.JsonKey, out UnitDefinition u)
                    && u is AircraftDefinition found)
                    _def = found;
            }
            catch { }
            if (_def != null)
                return _def;
            try
            {
                if (Encyclopedia.i == null || Encyclopedia.i.aircraft == null)
                    return null;
                for (int i = 0; i < Encyclopedia.i.aircraft.Count; i++)
                {
                    AircraftDefinition ad = Encyclopedia.i.aircraft[i];
                    if (ad != null && ad.jsonKey == PitchTune.JsonKey)
                    {
                        _def = ad;
                        return _def;
                    }
                }
            }
            catch { }
            return null;
        }

        static bool Host()
        {
            try
            {
                NetworkManagerNuclearOption net = NetworkManagerNuclearOption.i;
                return net != null && net.Server != null && net.Server.Active;
            }
            catch
            {
                return false;
            }
        }
    }
}
