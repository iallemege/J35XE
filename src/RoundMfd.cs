using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace J35XE
{
    /// <summary>
    /// The J-35's round radar-hood MFD, replacing BIA's frozen BIADrakenRoundMfd.
    /// Outer ring: thrust pointer. Next ring: twelve RWR cells (30 degrees each, nose at the top).
    /// Centre, by page ([ and ] cycle): FLT attitude ball, or the OTIS target-camera picture while a
    /// target is locked; SMS front-view outline with every hardpoint; DMG top-view outline coloured by
    /// each part's health.
    /// </summary>
    [DefaultExecutionOrder(32010)]
    internal sealed class RoundMfd : MonoBehaviour
    {
        const int N = 256;
        const float Hz = 30f;
        const float ROuter = 126.5f, RThrustIn = 116.5f, RRwrOut = 114.5f, RRwrIn = 98.5f, RCentre = 94f;
        const float ThrustSweep = 135f, PingLife = 3f;
        const int Pages = 3;

        static readonly Color32 Clear = new Color32(0, 0, 0, 0);
        static readonly Color32 Bg = new Color32(4, 14, 8, 255);
        static readonly Color32 Bezel = new Color32(10, 30, 16, 255);
        static readonly Color32 Green = new Color32(80, 240, 110, 255);
        static readonly Color32 Dim = new Color32(40, 120, 60, 255);
        static readonly Color32 Amber = new Color32(255, 176, 40, 255);
        static readonly Color32 Red = new Color32(255, 60, 36, 255);
        static readonly Color32 White = new Color32(235, 245, 235, 255);
        static readonly Color32 Sky = new Color32(30, 90, 150, 255);
        static readonly Color32 Earth = new Color32(96, 64, 24, 255);
        static readonly Color32 Plate = new Color32(0, 0, 0, 200);
        static readonly Color32 CellOff = new Color32(14, 42, 22, 255);

        static readonly FieldInfo CamField = AccessTools.Field(typeof(TargetCam), "cam");
        static readonly FieldInfo Condition = AccessTools.Field(typeof(Turbojet), "condition");
        static readonly string[] Titles = { "FLT", "SMS", "DMG" };

        struct Ping
        {
            public float Seen;
            public bool Track;
        }

        Aircraft _ac;
        Renderer _screen;
        Material _mat;
        Texture2D _tex;
        Pix _p;
        GameObject _otis;
        Material _otisMat;
        TargetCam _cam;
        Turbojet _jet;
        UnitPart[] _parts;
        Planform _front, _top;
        bool _built;
        int _page;
        float _nextDraw, _nextKick;
        readonly Dictionary<Unit, Ping> _pings = new Dictionary<Unit, Ping>();
        readonly int[] _cells = new int[12];
        readonly List<Unit> _gone = new List<Unit>();

        internal void Init(Aircraft ac)
        {
            _ac = ac;
            Transform t = Rig.Find(ac.transform, "mfd_round");
            _screen = t != null ? t.GetComponent<Renderer>() : null;
            if (_screen == null)
            {
                enabled = false;
                return;
            }
            Behaviour[] old = t.GetComponents<Behaviour>();
            for (int i = 0; i < old.Length; i++)
            {
                if (old[i] != null && old[i].GetType().Name == "BIADrakenRoundMfd")
                    old[i].enabled = false;
            }
            _p = new Pix(N);
            _jet = ac.GetComponentInChildren<Turbojet>(true);
            _cam = ac.GetComponentInChildren<TargetCam>(true);
            ac.onRadarWarning += OnRadarWarning;
            _tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            Shader sprite = Shader.Find("Sprites/Default");
            if (sprite == null && _screen.sharedMaterial != null)
                sprite = _screen.sharedMaterial.shader;
            _mat = new Material(sprite) { mainTexture = _tex, renderQueue = 3001 };
            _screen.material = _mat;
            BuildOtis(t);
        }

        void OnDestroy()
        {
            if (_ac != null)
                _ac.onRadarWarning -= OnRadarWarning;
            if (_tex != null) Destroy(_tex);
            if (_mat != null) Destroy(_mat);
            if (_otisMat != null) Destroy(_otisMat);
        }

        /// <summary>OTIS sits just behind the overlay; the overlay leaves its centre clear when locked.</summary>
        void BuildOtis(Transform screen)
        {
            MeshFilter mf = screen.GetComponent<MeshFilter>();
            if (mf == null)
                return;
            _otis = new GameObject("otis_round");
            _otis.layer = screen.gameObject.layer;
            _otis.transform.SetParent(screen, false);
            _otis.transform.localPosition = new Vector3(0f, 0f, 0.012f);
            _otis.transform.localScale = Vector3.one * (RCentre * 2f / N);
            _otis.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            MeshRenderer mr = _otis.AddComponent<MeshRenderer>();
            // URP/Unlit is not always loadable by name in the player; Sprites/Default always is.
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit == null)
                unlit = Shader.Find("Unlit/Texture");
            if (unlit == null)
                unlit = Shader.Find("Sprites/Default");
            if (unlit == null)
            {
                Destroy(_otis);
                _otis = null;
                return;
            }
            _otisMat = new Material(unlit) { renderQueue = 2990 };
            mr.sharedMaterial = _otisMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            _otis.SetActive(false);
        }

        void OnRadarWarning(Aircraft.OnRadarWarning w)
        {
            if (w.emitter == null)
                return;
            Ping old;
            bool track = w.isTarget || (_pings.TryGetValue(w.emitter, out old) && old.Track && Time.time - old.Seen < PingLife);
            _pings[w.emitter] = new Ping { Seen = Time.time, Track = track };
        }

        void Update()
        {
            if (_ac == null || _screen == null)
                return;
            if (!Rig.Local(_ac))
            {
                if (_otis != null && _otis.activeSelf) _otis.SetActive(false);
                return;
            }
            if (Input.GetKeyDown(KeyCode.RightBracket))
                _page = (_page + 1) % Pages;
            if (Input.GetKeyDown(KeyCode.LeftBracket))
                _page = (_page + Pages - 1) % Pages;
            if (!Rig.Cockpit())
                return;
            float now = Time.time;
            Unit locked = Rig.Locked(_ac);
            if (locked != null && _cam != null && now >= _nextKick)
            {
                _nextKick = now + 0.4f;
                try { _cam.SetTargetCam(); } catch { }
            }
            bool otis = _page == 0 && locked != null && BindOtis();
            if (_otis != null && _otis.activeSelf != otis)
                _otis.SetActive(otis);
            if (now < _nextDraw)
                return;
            _nextDraw = now + 1f / Hz;
            try { Draw(locked, otis); }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("J-35XE MFD: " + ex.Message);
                _nextDraw = now + 2f;
            }
        }

        bool BindOtis()
        {
            if (_otisMat == null || _cam == null || CamField == null)
                return false;
            Camera cam = CamField.GetValue(_cam) as Camera;
            RenderTexture rt = cam != null ? cam.targetTexture : null;
            if (rt == null || !cam.enabled)
                return false;
            float aspect = rt.width / (float)Mathf.Max(1, rt.height);
            Vector2 scale = aspect >= 1f ? new Vector2(1f / aspect, 1f) : new Vector2(1f, aspect);
            Vector2 offset = new Vector2((1f - scale.x) * 0.5f, (1f - scale.y) * 0.5f);
            foreach (string prop in new[] { "_BaseMap", "_MainTex" })
            {
                if (!_otisMat.HasProperty(prop))
                    continue;
                _otisMat.SetTexture(prop, rt);
                _otisMat.SetTextureScale(prop, scale);
                _otisMat.SetTextureOffset(prop, offset);
            }
            return true;
        }

        // ================================================================ drawing

        void Draw(Unit locked, bool otis)
        {
            Pix p = _p;
            p.ClipR2 = 0f;
            p.Fill(Clear);
            float c = p.C;
            DrawRings(c);
            p.ClipR2 = RCentre * RCentre;
            switch (_page)
            {
                case 0:
                    if (otis) DrawOtis(c, locked);
                    else DrawFlight(c);
                    break;
                case 1:
                    DrawStores(c);
                    break;
                default:
                    DrawDamage(c);
                    break;
            }
            string title = otis ? "OTIS" : Titles[_page];
            p.Label(c, Mathf.RoundToInt(c - RCentre + 8f), title + " " + (_page + 1) + "/" + Pages, Green, Plate);
            p.ClipR2 = 0f;
            p.Upload(_tex);
        }

        /// <summary>One pass over the annulus: thrust ring outside, RWR cells inside it.</summary>
        void DrawRings(float c)
        {
            float thrust = 0f, dry = 0.6f;
            if (_jet != null)
            {
                float max = Mathf.Max(1f, _jet.GetMaxThrust());
                thrust = Mathf.Clamp01(_jet.GetThrust() / max);
                dry = Mathf.Clamp01(_jet.maxThrust / max);
            }
            Cells();
            bool blink = Mathf.Repeat(Time.time * 4f, 1f) < 0.5f;
            float thrustDeg = -ThrustSweep + thrust * ThrustSweep * 2f;
            float dryDeg = -ThrustSweep + dry * ThrustSweep * 2f;
            Pix p = _p;
            int lo = Mathf.FloorToInt(c - ROuter), hi = Mathf.CeilToInt(c + ROuter);
            for (int y = lo; y <= hi; y++)
            {
                for (int x = lo; x <= hi; x++)
                {
                    float dx = x - c, dy = y - c;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    if (r > ROuter)
                        continue;
                    if (r < RRwrIn)
                    {
                        p.Set(x, y, Bg);
                        continue;
                    }
                    float ang = Mathf.Atan2(dx, -dy) * Mathf.Rad2Deg;
                    if (r >= RThrustIn)
                    {
                        Color32 col = Bezel;
                        if (Mathf.Abs(ang) <= ThrustSweep && ang <= thrustDeg)
                            col = ang > dryDeg ? Amber : Dim;
                        p.Set(x, y, col);
                    }
                    else if (r <= RRwrOut)
                    {
                        float a = ang < 0f ? ang + 360f : ang;
                        int cell = Mathf.FloorToInt((a + 15f) / 30f) % 12;
                        float within = Mathf.Repeat(a + 15f, 30f);
                        if (within < 1.5f || within > 28.5f)
                        {
                            p.Set(x, y, Bg);
                            continue;
                        }
                        int lvl = _cells[cell];
                        Color32 col = lvl == 0 ? CellOff : lvl == 1 ? Amber : lvl == 2 ? Red : (blink ? White : Red);
                        p.Set(x, y, col);
                    }
                    else
                        p.Set(x, y, Bg);
                }
            }
            // Thrust scale: a tick every 10 %, a long one at full dry power, and the pointer.
            for (int i = 0; i <= 10; i++)
                Spoke(c, -ThrustSweep + i * ThrustSweep * 0.2f, RThrustIn, RThrustIn + (i % 5 == 0 ? 6f : 3f), Green);
            Spoke(c, dryDeg, RThrustIn - 1f, ROuter, White);
            Spoke(c, thrustDeg, RThrustIn - 3f, ROuter + 0.5f, White);
            Spoke(c, thrustDeg + 0.8f, RThrustIn - 3f, ROuter + 0.5f, White);
            // Nose mark at the top of the RWR ring.
            p.Line(c - 3f, c - RRwrIn + 4f, c, c - RRwrIn - 1f, Green);
            p.Line(c + 3f, c - RRwrIn + 4f, c, c - RRwrIn - 1f, Green);
        }

        void Spoke(float c, float deg, float r0, float r1, Color32 col)
        {
            float a = deg * Mathf.Deg2Rad, s = Mathf.Sin(a), k = -Mathf.Cos(a);
            _p.Line(c + s * r0, c + k * r0, c + s * r1, c + k * r1, col);
        }

        /// <summary>Threat level per 30-degree cell: 1 search, 2 tracking us, 3 missile inbound.</summary>
        void Cells()
        {
            Array.Clear(_cells, 0, _cells.Length);
            float now = Time.time;
            _gone.Clear();
            foreach (KeyValuePair<Unit, Ping> kv in _pings)
            {
                Unit u = kv.Key;
                if (u == null || u.disabled || now - kv.Value.Seen > PingLife)
                {
                    _gone.Add(u);
                    continue;
                }
                Mark(u.transform.position, kv.Value.Track ? 2 : 1);
            }
            for (int i = 0; i < _gone.Count; i++)
                _pings.Remove(_gone[i]);
            MissileWarning mw = null;
            try { mw = _ac.GetMissileWarningSystem(); } catch { }
            if (mw != null && mw.knownMissiles != null)
            {
                for (int i = 0; i < mw.knownMissiles.Count; i++)
                {
                    Missile m = mw.knownMissiles[i];
                    if (m != null && !m.disabled)
                        Mark(m.transform.position, 3);
                }
            }
        }

        void Mark(Vector3 world, int level)
        {
            float b = Rig.Bearing(_ac.transform, world);
            if (b < 0f) b += 360f;
            int cell = Mathf.FloorToInt((b + 15f) / 30f) % 12;
            if (level > _cells[cell])
                _cells[cell] = level;
        }

        void DrawFlight(float c)
        {
            Transform t = _ac.transform;
            float pitch = Mathf.Asin(Mathf.Clamp(t.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            float roll = Mathf.Atan2(-t.right.y, t.up.y) * Mathf.Rad2Deg;
            const float k = RCentre / 40f;
            float rr = roll * Mathf.Deg2Rad;
            Vector2 n = new Vector2(-Mathf.Sin(rr), Mathf.Cos(rr));
            Vector2 tan = new Vector2(Mathf.Cos(rr), Mathf.Sin(rr));
            float h = -pitch * k;
            Pix p = _p;
            int lo = Mathf.FloorToInt(c - RCentre), hi = Mathf.CeilToInt(c + RCentre);
            for (int y = lo; y <= hi; y++)
            {
                for (int x = lo; x <= hi; x++)
                {
                    float dx = x - c, up = c - y;
                    if (dx * dx + up * up > RCentre * RCentre)
                        continue;
                    p.Set(x, y, dx * n.x + up * n.y > h ? Sky : Earth);
                }
            }
            for (int deg = -40; deg <= 40; deg += 10)
            {
                float along = (deg - pitch) * k;
                float half = deg == 0 ? 70f : (deg % 20 == 0 ? 22f : 14f);
                Vector2 mid = n * along;
                Vector2 a = mid - tan * half, b = mid + tan * half;
                Color32 col = deg == 0 ? White : Green;
                p.Line(c + a.x, c - a.y, c + b.x, c - b.y, col);
                if (deg != 0 && deg % 20 == 0)
                    p.TextC(c + b.x + 10f, Mathf.RoundToInt(c - b.y - 3f), Mathf.Abs(deg).ToString(), Green);
            }
            // Fixed aircraft symbol.
            p.Thick(c - 34f, c, c - 12f, c, Amber);
            p.Thick(c - 12f, c, c - 6f, c + 6f, Amber);
            p.Thick(c - 6f, c + 6f, c, c, Amber);
            p.Thick(c, c, c + 6f, c + 6f, Amber);
            p.Thick(c + 6f, c + 6f, c + 12f, c, Amber);
            p.Thick(c + 12f, c, c + 34f, c, Amber);

            float spd = _ac.rb != null ? _ac.rb.velocity.magnitude : 0f;
            float sos = 340f;
            try { sos = Mathf.Max(200f, LevelInfo.GetSpeedOfSound(t.position.y - Datum.originPosition.y)); } catch { }
            float alt = t.position.y - Datum.originPosition.y;
            float thr = 0f;
            try
            {
                ControlInputs ci = _ac.GetInputs();
                if (ci != null) thr = Mathf.Clamp01(ci.throttle);
            }
            catch { }
            float rev = ReverseNow();
            string lever = rev >= 0.02f
                ? "R " + Mathf.RoundToInt(rev * 100f)
                : "T " + Mathf.RoundToInt(thr * 100f);
            p.Label(c, Mathf.RoundToInt(c - 66f), "HDG " + Mathf.RoundToInt(Rig.Heading(t.forward)).ToString("000"), White, Plate);
            p.Label(c - 52f, Mathf.RoundToInt(c + 22f), Mathf.RoundToInt(spd * 3.6f) + "KM", White, Plate);
            p.Label(c + 52f, Mathf.RoundToInt(c + 22f), Mathf.RoundToInt(alt) + "M", White, Plate);
            p.Label(c, Mathf.RoundToInt(c + 58f), "G " + _ac.gForce.ToString("0.0") + "  M " + (spd / sos).ToString("0.00")
                + "  " + lever, White, Plate);
        }

        void DrawOtis(float c, Unit target)
        {
            Pix p = _p;
            int lo = Mathf.FloorToInt(c - RCentre), hi = Mathf.CeilToInt(c + RCentre);
            for (int y = lo; y <= hi; y++)
                for (int x = lo; x <= hi; x++)
                    p.Set(x, y, Clear);
            const float b = 26f, g = 9f;
            // Track gate brackets and a gapped cross.
            p.Thick(c - b, c - b, c - b + g, c - b, Green); p.Thick(c - b, c - b, c - b, c - b + g, Green);
            p.Thick(c + b, c - b, c + b - g, c - b, Green); p.Thick(c + b, c - b, c + b, c - b + g, Green);
            p.Thick(c - b, c + b, c - b + g, c + b, Green); p.Thick(c - b, c + b, c - b, c + b - g, Green);
            p.Thick(c + b, c + b, c + b - g, c + b, Green); p.Thick(c + b, c + b, c + b, c + b - g, Green);
            p.Line(c - 60f, c, c - 8f, c, Green); p.Line(c + 8f, c, c + 60f, c, Green);
            p.Line(c, c - 60f, c, c - 8f, Green); p.Line(c, c + 8f, c, c + 60f, Green);
            if (target == null)
                return;
            float km = Vector3.Distance(_ac.transform.position, target.transform.position) / 1000f;
            p.Label(c, Mathf.RoundToInt(c + 46f), Short(Name(target), 14), Amber, Plate);
            p.Label(c, Mathf.RoundToInt(c + 60f), km.ToString("0.0") + "KM  B" + Mathf.RoundToInt(Mathf.Repeat(Rig.Bearing(_ac.transform, target.transform.position), 360f)).ToString("000"), White, Plate);
        }

        void DrawStores(float c)
        {
            Pix p = _p;
            EnsureOutlines();
            if (_front != null)
            {
                int ox = Mathf.RoundToInt(c - _front.W * 0.5f), oy = Mathf.RoundToInt(c - 62f);
                Blit(_front, ox, oy, false);
                WeaponInfo sel = Selected();
                WeaponManager wm = _ac.weaponManager;
                if (wm != null && wm.hardpointSets != null)
                {
                    for (int s = 0; s < wm.hardpointSets.Length; s++)
                    {
                        HardpointSet set = wm.hardpointSets[s];
                        if (set == null || set.hardpoints == null)
                            continue;
                        for (int h = 0; h < set.hardpoints.Count; h++)
                        {
                            Hardpoint hp = set.hardpoints[h];
                            if (hp == null || hp.transform == null)
                                continue;
                            Vector2 g = _front.Grid(_ac.transform.InverseTransformPoint(hp.transform.position));
                            Weapon w = hp.transform.GetComponentInChildren<Weapon>(true);
                            bool loaded = w != null && w.info != null;
                            bool picked = loaded && sel != null && w.info == sel;
                            int x = ox + Mathf.RoundToInt(g.x) - 3, y = oy + Mathf.RoundToInt(g.y) + 2;
                            p.Box(x, y, 7, 7, picked ? White : loaded ? Amber : Dim, loaded);
                            if (picked)
                                p.Box(x - 2, y - 2, 11, 11, White, false);
                        }
                    }
                }
            }
            WeaponStation cur = _ac.weaponManager != null ? _ac.weaponManager.currentWeaponStation : null;
            int line = Mathf.RoundToInt(c - 4f);
            int shown = 0;
            for (int i = 0; i < _ac.weaponStations.Count && shown < 7; i++)
            {
                WeaponStation st = _ac.weaponStations[i];
                if (st == null || st.WeaponInfo == null)
                    continue;
                bool picked = st == cur;
                string name = Short(string.IsNullOrEmpty(st.WeaponInfo.shortName) ? st.WeaponInfo.weaponName : st.WeaponInfo.shortName, 12);
                string row = (picked ? ">" : " ") + name.PadRight(12) + " " + st.Ammo + "/" + st.FullAmmo;
                p.Text(Mathf.RoundToInt(c - 76f), line, row, picked ? White : st.Ammo > 0 ? Green : Dim);
                line += 10;
                shown++;
            }
            if (shown == 0)
                p.TextC(c, line, "NO STORES", Dim);
        }

        void DrawDamage(float c)
        {
            Pix p = _p;
            EnsureOutlines();
            if (_top == null)
            {
                p.TextC(c, Mathf.RoundToInt(c), "NO DATA", Dim);
                return;
            }
            Color32[] fill = new Color32[256];
            Color32[] edge = new Color32[256];
            bool blink = Mathf.Repeat(Time.time * 2f, 1f) < 0.5f;
            for (int i = 0; i < _parts.Length && i < 254; i++)
            {
                UnitPart up = _parts[i];
                Color32 e;
                if (up == null || up.IsDetached())
                    e = blink ? Red : new Color32(70, 70, 70, 255);
                else if (up.hitPoints >= 70f)
                    e = Green;
                else if (up.hitPoints >= 35f)
                    e = Amber;
                else
                    e = Red;
                edge[i + 1] = e;
                fill[i + 1] = new Color32((byte)(e.r * 0.35f), (byte)(e.g * 0.35f), (byte)(e.b * 0.35f), 255);
            }
            int ox = Mathf.RoundToInt(c - _top.W * 0.5f), oy = Mathf.RoundToInt(c - _top.H * 0.5f - 4f);
            for (int y = 0; y < _top.H; y++)
            {
                for (int x = 0; x < _top.W; x++)
                {
                    int i = y * _top.W + x;
                    byte v = _top.Mask[i];
                    if (v == 0)
                        continue;
                    p.Set(ox + x, oy + y, _top.Edge[i] ? edge[v] : fill[v]);
                }
            }
            float eng = 1f;
            if (_jet != null && Condition != null)
            {
                try { eng = (float)Condition.GetValue(_jet); } catch { }
            }
            float fuel = 0f;
            FuelTank[] tanks = _ac.GetComponentsInChildren<FuelTank>(true);
            for (int i = 0; i < tanks.Length; i++)
                fuel += tanks[i].fuelMass;
            p.Label(c - 40f, Mathf.RoundToInt(c + 78f), "ENG " + Mathf.RoundToInt(eng * 100f) + "%", eng > 0.7f ? Green : eng > 0.35f ? Amber : Red, Plate);
            p.Label(c + 40f, Mathf.RoundToInt(c + 78f), "FUEL " + Mathf.RoundToInt(fuel), Green, Plate);
        }

        void Blit(Planform pf, int ox, int oy, bool fill)
        {
            for (int y = 0; y < pf.H; y++)
            {
                for (int x = 0; x < pf.W; x++)
                {
                    int i = y * pf.W + x;
                    if (pf.Mask[i] == 0)
                        continue;
                    if (pf.Edge[i])
                        _p.Set(ox + x, oy + y, Green);
                    else if (fill)
                        _p.Set(ox + x, oy + y, Dim);
                }
            }
        }

        void EnsureOutlines()
        {
            if (_built)
                return;
            _built = true;
            _parts = Planform.Parts(_ac);
            _front = Planform.Build(_ac, _parts, false, 168f, 56f);
            _top = Planform.Build(_ac, _parts, true, 120f, 150f);
        }

        static Type _revType;
        static PropertyInfo _revNow;
        static bool _revLooked;

        /// <summary>Oritasy reverse fraction, 0 when that mod is not loaded.</summary>
        static float ReverseNow()
        {
            if (!_revLooked)
            {
                _revLooked = true;
                foreach (System.Reflection.Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name != "OritasySS")
                        continue;
                    _revType = asm.GetType("OritasySS.Rev");
                    _revNow = _revType != null ? _revType.GetProperty("Now") : null;
                    break;
                }
            }
            if (_revNow == null)
                return 0f;
            try { return (float)_revNow.GetValue(null, null); }
            catch { return 0f; }
        }

        WeaponInfo Selected()
        {
            WeaponStation cur = _ac.weaponManager != null ? _ac.weaponManager.currentWeaponStation : null;
            return cur != null ? cur.WeaponInfo : null;
        }

        internal static string Name(Unit u)
        {
            if (u == null || u.definition == null)
                return u != null ? u.name : "";
            return !string.IsNullOrEmpty(u.definition.code) ? u.definition.code : u.definition.unitName;
        }

        internal static string Short(string s, int max)
        {
            if (string.IsNullOrEmpty(s))
                return "";
            s = s.ToUpperInvariant();
            return s.Length <= max ? s : s.Substring(0, max);
        }
    }
}
