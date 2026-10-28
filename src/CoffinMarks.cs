using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace J35XE
{
    /// <summary>
    /// COFFIN target marks for the J-35, replacing BIA's every-unit labels. Only enemies the faction
    /// is tracking right now and that the selected weapon can engage are shown: aircraft for an
    /// anti-air store, vehicles / ships / buildings for an anti-surface store, both for a gun.
    /// Air marks are red brackets, ground marks orange diamonds; a solid frame and IN RNG once inside
    /// the weapon's maximum range; a yellow LOCK box on the targets in the weapon target list.
    /// </summary>
    [DefaultExecutionOrder(32020)]
    internal sealed class CoffinMarks : MonoBehaviour
    {
        const int MaxMarks = 16;
        const float Refresh = 0.25f, DefaultRange = 20000f, GunRange = 3000f, MaxShow = 80000f, FriendRange = 25000f, AirShow = 40000f, MissileShow = 30000f, ThreatShow = 45000f;

        static readonly Color AirCol = new Color(1f, 0.25f, 0.18f, 1f);
        static readonly Color GroundCol = new Color(1f, 0.6f, 0.15f, 1f);
        static readonly Color SamCol = new Color(0.78f, 0.28f, 1f, 1f);
        static readonly Color ShipCol = new Color(0.35f, 0.78f, 1f, 1f);
        static readonly Color MissileCol = new Color(1f, 0.45f, 0.9f, 1f);
        static readonly Color BombCol = new Color(1f, 0.55f, 0.15f, 1f);
        static readonly Color FriendCol = new Color(0.35f, 0.95f, 1f, 1f);
        static readonly Color LockCol = new Color(1f, 0.92f, 0.2f, 1f);
        static readonly Color RangeCol = new Color(0.35f, 1f, 0.45f, 1f);
        static readonly Color TextCol = new Color(0.9f, 0.95f, 0.9f, 1f);

        static Type _coffinType;
        static FieldInfo _fAircraft, _fInCockpit, _fBlank, _fUserOff, _fLiveMat;
        static MethodInfo _mLive;

        struct Target
        {
            public Unit U;
            public bool Air, Lock, InRange, Friend, Missile, Bomb, Threat, Ship;
            public float Dist, Spd, Hdg, Cls;
            public string Name, Kind;
        }

        Aircraft _ac;
        Component _coffin;
        float _next;
        readonly List<Target> _targets = new List<Target>();
        readonly object[] _args = new object[5];
        string _mode = "";

        /// <summary>Stop BIA drawing its own every-unit labels on a J-35 COFFIN.</summary>
        internal static void PatchBia(Harmony h)
        {
            _coffinType = AccessTools.TypeByName("BIA.BIACoffinCanopy");
            if (_coffinType == null)
                return;
            _fAircraft = AccessTools.Field(_coffinType, "_aircraft");
            _fInCockpit = AccessTools.Field(_coffinType, "_inCockpit");
            _fBlank = AccessTools.Field(_coffinType, "_blank");
            _fUserOff = AccessTools.Field(_coffinType, "_userOff");
            _fLiveMat = AccessTools.Field(_coffinType, "_liveMat");
            _mLive = AccessTools.Method(_coffinType, "LabelOnLiveTile");
            MethodInfo draw = AccessTools.Method(_coffinType, "DrawNearbyLabels");
            if (draw != null)
                h.Patch(draw, new HarmonyMethod(typeof(CoffinMarks), nameof(SkipBiaLabels)));
        }

        static bool SkipBiaLabels(object __instance)
        {
            Aircraft ac = _fAircraft != null ? _fAircraft.GetValue(__instance) as Aircraft : null;
            return !PitchTune.IsOurs(ac);
        }

        internal void Init(Aircraft ac)
        {
            _ac = ac;
        }

        /// <summary>
        /// BIA lays a 7 % pale-blue sheet over every live tile. By day it is invisible; at night,
        /// over a black world, it is a white film. COFFIN is meant to be a clear view: no tint.
        /// </summary>
        void Update()
        {
            if (_coffin == null && _coffinType != null && _ac != null)
                _coffin = _ac.GetComponentInChildren(_coffinType, true);
            Material live = _coffin != null && _fLiveMat != null ? _fLiveMat.GetValue(_coffin) as Material : null;
            if (live == null)
                return;
            foreach (string prop in new[] { "_BaseColor", "_Color" })
            {
                if (!live.HasProperty(prop))
                    continue;
                Color c = live.GetColor(prop);
                if (c.a > 0f)
                    live.SetColor(prop, new Color(c.r, c.g, c.b, 0f));
            }
        }

        bool CoffinUp()
        {
            if (_coffin == null && _coffinType != null)
                _coffin = _ac.GetComponentInChildren(_coffinType, true);
            if (_coffin == null || _fInCockpit == null)
                return false;
            return (bool)_fInCockpit.GetValue(_coffin) && !(bool)_fBlank.GetValue(_coffin) && !(bool)_fUserOff.GetValue(_coffin);
        }

        void OnGUI()
        {
            if (Event.current == null || Event.current.type != EventType.Repaint)
                return;
            if (_ac == null || _ac.disabled || !Rig.Local(_ac) || !Rig.Cockpit())
                return;
            try { if (DynamicMap.mapMaximized) return; } catch { }
            if (!CoffinUp())
            {
                BootLine();
                return;
            }
            Camera cam = Camera.main;
            if (cam == null || _mLive == null)
                return;
            try
            {
                if (Time.unscaledTime >= _next)
                {
                    _next = Time.unscaledTime + Refresh;
                    Collect();
                }
                float h = Screen.height;
                GuiDraw.Text(Screen.width * 0.5f, h - 132f, _mode, RangeCol, true);
                for (int i = 0; i < _targets.Count; i++)
                    Draw(cam, h, _targets[i]);
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("J-35XE COFFIN marks: " + ex.Message);
                _next = Time.unscaledTime + 2f;
            }
        }

        static GUIStyle _boot;

        void BootLine()
        {
            if (_boot == null)
            {
                _boot = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 22,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.UpperCenter
                };
                _boot.normal.textColor = Color.white;
            }
            const string line = "Press U to turn on the G2E";
            float w = Screen.width;
            Vector2 size = _boot.CalcSize(new GUIContent(line));
            Rect r = new Rect((w - size.x) * 0.5f, Screen.height * 0.72f, size.x, size.y);
            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.85f);
            GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), line, _boot);
            GUI.color = Color.white;
            GUI.Label(r, line, _boot);
            GUI.color = prev;
        }

        void Draw(Camera cam, float h, Target t)
        {
            if (t.U == null || t.U.disabled)
                return;
            _args[0] = cam;
            _args[1] = t.U.transform.position;
            _args[2] = h;
            _args[3] = 0f;
            _args[4] = 0f;
            if (!(bool)_mLive.Invoke(_coffin, _args))
                return;
            Vector2 c = new Vector2((float)_args[3], (float)_args[4]);
            float half = Mathf.Clamp(900f / Mathf.Sqrt(Mathf.Max(t.Dist, 1f)), 10f, 24f);
            Color col = t.Friend ? FriendCol
                : t.Bomb ? BombCol
                : t.Missile ? MissileCol
                : t.Ship ? ShipCol
                : t.Threat ? SamCol
                : t.Air ? AirCol : GroundCol;
            float w = t.InRange ? 3f : 1.5f;
            if (t.Air || t.Missile)
                GuiDraw.Brackets(c, half, half * 0.6f, w, col);
            else
                GuiDraw.Diamond(c, half * 1.15f, w, col);
            GuiDraw.Rect(c.x - 1.5f, c.y - 1.5f, 3f, 3f, col);
            if (t.Lock)
            {
                GuiDraw.Box(c, half + 6f, 2.5f, LockCol);
                GuiDraw.Line(c + new Vector2(-half - 14f, 0f), c + new Vector2(-half - 6f, 0f), 2f, LockCol);
                GuiDraw.Line(c + new Vector2(half + 6f, 0f), c + new Vector2(half + 14f, 0f), 2f, LockCol);
                GuiDraw.Line(c + new Vector2(0f, -half - 14f), c + new Vector2(0f, -half - 6f), 2f, LockCol);
                GuiDraw.Line(c + new Vector2(0f, half + 6f), c + new Vector2(0f, half + 14f), 2f, LockCol);
                GuiDraw.Text(c.x, c.y - half - 30f, "LOCK", LockCol);
            }
            float y = c.y + half + 8f;
            if (!t.Lock && t.Dist > 18000f)
                return;
            GuiDraw.Text(c.x, y, t.Kind + " " + t.Name, t.Lock ? LockCol : col, true);
            if (!t.Lock && t.Dist > 8000f)
                return;
            GuiDraw.Text(c.x, y + 14f, "SPD " + Mathf.RoundToInt(t.Spd) + "  HDG " + Mathf.RoundToInt(t.Hdg).ToString("000"), TextCol, true);
            string range = (t.Dist / 1000f).ToString(t.Dist < 10000f ? "0.0" : "0") + " KM";
            if (t.Air || t.Missile)
            {
                float dh = t.U.transform.position.y - _ac.transform.position.y;
                range += (dh >= 0f ? "  +" : "  -") + Mathf.RoundToInt(Mathf.Abs(dh)) + " M";
            }
            string cls = "CLS " + (t.Cls >= 0f ? "+" : "") + Mathf.RoundToInt(t.Cls);
            if (t.InRange)
                cls += "  IN RNG";
            GuiDraw.Text(c.x, y + 28f, range + "  " + cls, t.InRange ? RangeCol : TextCol, true);
        }

        void Collect()
        {
            _targets.Clear();
            FactionHQ hq = _ac.NetworkHQ;
            if (hq == null)
                return;
            WeaponStation st = _ac.weaponManager != null ? _ac.weaponManager.currentWeaponStation : null;
            WeaponInfo info = st != null ? st.WeaponInfo : null;
            bool air = true, ground = true;
            float range = DefaultRange;
            string weapon = "NO WPN";
            if (info != null)
            {
                bool any = info.effectiveness.antiAir > 0.01f || info.effectiveness.antiSurface > 0.01f || info.effectiveness.antiRadar > 0.01f;
                air = !any || info.effectiveness.antiAir > 0.01f || info.gun;
                ground = !any || info.effectiveness.antiSurface > 0.01f || info.effectiveness.antiRadar > 0.01f || info.gun;
                range = info.targetRequirements.maxRange > 1f ? info.targetRequirements.maxRange : info.gun ? GunRange : DefaultRange;
                weapon = RoundMfd.Short(string.IsNullOrEmpty(info.shortName) ? info.weaponName : info.shortName, 12);
            }
            float show = Mathf.Min(MaxShow, Mathf.Max(range * 1.6f, 6000f));
            List<Unit> locks = null;
            try { locks = _ac.weaponManager != null ? _ac.weaponManager.GetTargetList() : null; } catch { }
            Vector3 me = _ac.transform.position;
            List<Unit> all = UnitRegistry.allUnits;
            int foes = 0, friends = 0, missiles = 0, airShown = 0;
            for (int i = 0; all != null && i < all.Count; i++)
            {
                Unit u = all[i];
                if (u == null || u == _ac || u.disabled)
                    continue;
                float d = Vector3.Distance(me, u.transform.position);
                bool locked = locks != null && locks.Contains(u);
                if (u is Missile)
                {
                    if (!locked && d > MissileShow)
                        continue;
                    if (missiles >= 4 && !locked)
                        continue;
                    bool bomb = Bomb(u);
                    _targets.Add(Mark(u, d, locked, false, true, bomb, false, false, range, bomb ? "BOMB" : "MSL"));
                    missiles++;
                    continue;
                }
                bool isAir = u is Aircraft;
                bool ship = u is Ship;
                bool threat = !isAir && (ship || SamOrRadar(u));
                bool isGround = u is GroundVehicle || ship || u is Building;
                FactionHQ other = u.NetworkHQ;
                if (other == null)
                    continue;
                bool friend = other == hq;
                if (friend)
                {
                    if (!isAir)
                        continue;
                    if (!locked && d > FriendRange)
                        continue;
                }
                else if (threat)
                {
                    if (!locked && d > ThreatShow)
                        continue;
                    if (!locked && d > 15000f && !hq.IsTargetBeingTracked(u))
                        continue;
                }
                else if (isAir)
                {
                    if (!locked && d > Mathf.Max(show, AirShow))
                        continue;
                    if (airShown >= 8 && !locked)
                        continue;
                    airShown++;
                }
                else
                {
                    if (!ground || !isGround)
                        continue;
                    if (!locked && (d > show || !hq.IsTargetBeingTracked(u)))
                        continue;
                }
                _targets.Add(Mark(u, d, locked, friend, false, false, threat, ship, range, friend ? "FRND" : Kind(u)));
                if (friend) friends++;
                else foes++;
            }
            _targets.Sort((a, b) =>
            {
                if (a.Lock != b.Lock) return a.Lock ? -1 : 1;
                if (a.Missile != b.Missile) return a.Missile ? -1 : 1;
                if (a.Air != b.Air) return a.Air ? -1 : 1;
                if (a.Threat != b.Threat) return a.Threat ? -1 : 1;
                if (a.Friend != b.Friend) return a.Friend ? 1 : -1;
                return a.Dist.CompareTo(b.Dist);
            });
            if (_targets.Count > MaxMarks)
                _targets.RemoveRange(MaxMarks, _targets.Count - MaxMarks);
            string domain = air && ground ? "AIR+GND" : air ? "AIR" : "GND";
            _mode = "COFFIN  " + weapon + "  " + domain + "  " + foes + " TGT  " + friends + " FRND  " + missiles + " MSL  RNG " + Mathf.RoundToInt(range / 1000f) + " KM";
        }

        Target Mark(Unit u, float d, bool locked, bool friend, bool missile, bool bomb, bool threat, bool ship, float range, string kind)
        {
            Vector3 to = _ac.transform.position - u.transform.position;
            float cls = 0f;
            if (to.sqrMagnitude > 1f && u.rb != null)
                cls = Vector3.Dot(u.rb.velocity, to.normalized);
            Vector3 fwd = u.transform.forward;
            fwd.y = 0f;
            float hdg = fwd.sqrMagnitude > 0.0001f ? Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg : 0f;
            if (hdg < 0f)
                hdg += 360f;
            return new Target
            {
                U = u,
                Air = u is Aircraft,
                Missile = missile,
                Bomb = bomb,
                Threat = threat,
                Ship = ship,
                Lock = locked,
                Friend = friend,
                InRange = d <= range,
                Dist = d,
                Spd = u.speed,
                Hdg = hdg,
                Cls = cls,
                Name = RoundMfd.Short(RoundMfd.Name(u), 12),
                Kind = kind,
            };
        }

        static bool SamOrRadar(Unit u)
        {
            if (u != null && u.definition is VehicleDefinition vd)
            {
                switch (vd.vehicleType)
                {
                    case VehicleType.AAA:
                    case VehicleType.IR_SAM:
                    case VehicleType.R_SAM:
                    case VehicleType.RDR:
                        return true;
                }
            }
            string n = u != null && u.definition != null
                ? (u.definition.unitName ?? "") + " " + (u.definition.code ?? "")
                : u != null ? u.name : "";
            if (string.IsNullOrEmpty(n))
                return false;
            n = n.ToLowerInvariant();
            return n.IndexOf("sam", StringComparison.Ordinal) >= 0
                || n.IndexOf("radar", StringComparison.Ordinal) >= 0
                || n.IndexOf("aaa", StringComparison.Ordinal) >= 0;
        }

        static FieldInfo _info;

        static bool Bomb(Unit u)
        {
            if (_info == null)
                _info = AccessTools.Field(typeof(Missile), "info");
            WeaponInfo w = _info != null ? _info.GetValue(u) as WeaponInfo : null;
            return w != null && (w.bomb || w.glideBomb);
        }

        static string Kind(Unit u)
        {
            if (u is Aircraft)
                return "AIR";
            if (u is Ship)
                return "SHIP";
            if (u is Building)
                return "BLDG";
            if (u.definition is VehicleDefinition vd)
            {
                switch (vd.vehicleType)
                {
                    case VehicleType.MBT: return "TANK";
                    case VehicleType.AFV: return "AFV";
                    case VehicleType.ART: return "ARTY";
                    case VehicleType.AAA: return "AAA";
                    case VehicleType.IR_SAM:
                    case VehicleType.R_SAM: return "SAM";
                    case VehicleType.RDR: return "RDR";
                }
            }
            return "VEH";
        }
    }
}
