using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace J35XE
{
    internal sealed class CockpitPanel : MonoBehaviour
    {
        Aircraft _ac;

        internal void Init(Aircraft ac)
        {
            _ac = ac;
            CockpitEdit.Bind(ac);
        }

        void Update()
        {
            if (_ac == null || !Rig.Local(_ac))
                return;
            if (Input.GetKeyDown(KeyCode.F7))
                CockpitEdit.Toggle();
        }

        void OnGUI()
        {
            CockpitEdit.Draw(_ac);
        }
    }

    /// <summary>
    /// In-cockpit layout menu. F7 opens it. Move, turn, resize or hide the gauges,
    /// the round MFD and the reflector sight, then save a setup next to the plugin.
    /// </summary>
    internal static class CockpitEdit
    {
        const float Nudge = 0.001f;
        const float NudgeBig = 0.01f;
        const float Turn = 5f;
        const float TurnFine = 1f;
        const float ScaleStep = 1.05f;

        struct Pose
        {
            public Vector3 P, E, S;
            public bool On;
        }

        static readonly string[] Want =
        {
            "mfd_round", "hud_combiner", "hud_symbols",
            "gauge_adi", "gauge_alt", "gauge_ias", "gauge_mach", "gauge_vsi",
            "gauge_radalt", "gauge_hsi", "gauge_rpm", "gauge_egt", "gauge_fuel",
            "gauge_g", "gauge_clock"
        };

        static readonly Dictionary<string, Pose> Base = new Dictionary<string, Pose>();
        static readonly List<string> Names = new List<string>();
        static readonly Dictionary<string, Transform> Live = new Dictionary<string, Transform>();
        static bool _open, _captured;
        static int _pick;
        static string _saveName = "My cockpit";
        static string _status = "";
        static Rect _win = new Rect(24f, 80f, 360f, 520f);
        static Vector2 _scroll;

        internal static void Toggle()
        {
            _open = !_open;
        }

        internal static bool Open
        {
            get { return _open; }
        }

        internal static void Bind(Aircraft ac)
        {
            if (!PitchTune.IsOurs(ac))
                return;
            Capture(ac);
            string cur = ReadCurrent();
            if (!string.IsNullOrEmpty(cur))
                ApplyFile(ac, cur);
        }

        internal static void Draw(Aircraft ac)
        {
            if (!_open || ac == null || !PitchTune.IsOurs(ac) || !Rig.Local(ac))
                return;
            Refresh(ac);
            _win = GUI.Window(914351, _win, id => Window(ac, id), "J-35 cockpit");
        }

        static void Window(Aircraft ac, int id)
        {
            float step = (Event.current != null && Event.current.shift) ? NudgeBig : Nudge;
            float ang = (Event.current != null && Event.current.shift) ? TurnFine : Turn;
            _scroll = GUILayout.BeginScrollView(_scroll);
            if (Names.Count == 0)
                GUILayout.Label("No cockpit parts on this jet.");
            for (int i = 0; i < Names.Count; i++)
            {
                string n = Names[i];
                bool on = Live.ContainsKey(n) && Live[n] != null && Live[n].localScale.x > 0.01f;
                if (GUILayout.Toggle(i == _pick, (on ? "" : "(hidden) ") + n))
                    _pick = i;
            }
            GUILayout.EndScrollView();
            Transform t = Selected();
            GUILayout.Label(t == null ? "Nothing selected" : t.name + "   Shift = 1 cm / 1°");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("X-")) NudgeAxis(t, 0, -step);
            if (GUILayout.Button("X+")) NudgeAxis(t, 0, step);
            if (GUILayout.Button("Y-")) NudgeAxis(t, 1, -step);
            if (GUILayout.Button("Y+")) NudgeAxis(t, 1, step);
            if (GUILayout.Button("Z-")) NudgeAxis(t, 2, -step);
            if (GUILayout.Button("Z+")) NudgeAxis(t, 2, step);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Pitch-")) TurnAxis(t, 0, -ang);
            if (GUILayout.Button("Pitch+")) TurnAxis(t, 0, ang);
            if (GUILayout.Button("Yaw-")) TurnAxis(t, 1, -ang);
            if (GUILayout.Button("Yaw+")) TurnAxis(t, 1, ang);
            if (GUILayout.Button("Roll-")) TurnAxis(t, 2, -ang);
            if (GUILayout.Button("Roll+")) TurnAxis(t, 2, ang);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Smaller")) Scale(t, 1f / ScaleStep);
            if (GUILayout.Button("Larger")) Scale(t, ScaleStep);
            if (GUILayout.Button("Hide / show")) Hide(t);
            if (GUILayout.Button("Reset part")) Reset(t);
            GUILayout.EndHorizontal();
            GUILayout.Label("Setup name");
            _saveName = GUILayout.TextField(_saveName ?? "", 40);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save")) Save(ac);
            if (GUILayout.Button("Default")) { ApplyBase(ac); WriteCurrent(""); _status = "Default cockpit"; }
            GUILayout.EndHorizontal();
            string[] files = ListFiles();
            for (int i = 0; i < files.Length; i++)
            {
                string file = files[i];
                GUILayout.BeginHorizontal();
                GUILayout.Label(Path.GetFileNameWithoutExtension(file));
                if (GUILayout.Button("Use", GUILayout.Width(48f)))
                {
                    ApplyFile(ac, file);
                    WriteCurrent(file);
                    _status = "Using " + Path.GetFileNameWithoutExtension(file);
                }
                GUILayout.EndHorizontal();
            }
            if (!string.IsNullOrEmpty(_status))
                GUILayout.Label(_status);
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 22f));
        }

        static void Capture(Aircraft ac)
        {
            if (_captured)
                return;
            _captured = true;
            Names.Clear();
            for (int i = 0; i < Want.Length; i++)
            {
                Transform t = Rig.Find(ac.transform, Want[i]);
                if (t == null)
                    continue;
                Names.Add(Want[i]);
                Base[Want[i]] = new Pose { P = t.localPosition, E = t.localEulerAngles, S = t.localScale, On = true };
            }
        }

        static void Refresh(Aircraft ac)
        {
            Live.Clear();
            for (int i = 0; i < Names.Count; i++)
            {
                Transform t = Rig.Find(ac.transform, Names[i]);
                if (t != null)
                    Live[Names[i]] = t;
            }
        }

        static Transform Selected()
        {
            if (_pick < 0 || _pick >= Names.Count)
                return null;
            Transform t;
            return Live.TryGetValue(Names[_pick], out t) ? t : null;
        }

        static void NudgeAxis(Transform t, int axis, float d)
        {
            if (t == null) return;
            Vector3 p = t.localPosition;
            if (axis == 0) p.x += d;
            else if (axis == 1) p.y += d;
            else p.z += d;
            t.localPosition = p;
        }

        static void TurnAxis(Transform t, int axis, float d)
        {
            if (t == null) return;
            Vector3 e = t.localEulerAngles;
            if (axis == 0) e.x += d;
            else if (axis == 1) e.y += d;
            else e.z += d;
            t.localEulerAngles = e;
        }

        static void Scale(Transform t, float m)
        {
            if (t == null) return;
            Vector3 s = t.localScale * m;
            if (s.x < 0.05f) s = Vector3.one * 0.05f;
            if (s.x > 4f) s = t.localScale.normalized * 4f;
            t.localScale = s;
        }

        static void Hide(Transform t)
        {
            if (t == null) return;
            if (t.localScale.x < 0.01f)
            {
                Pose b;
                if (Base.TryGetValue(t.name, out b))
                    t.localScale = b.S;
                else
                    t.localScale = Vector3.one;
            }
            else
                t.localScale = Vector3.one * 0.0001f;
        }

        static void Reset(Transform t)
        {
            if (t == null) return;
            Pose b;
            if (!Base.TryGetValue(t.name, out b))
                return;
            t.localPosition = b.P;
            t.localEulerAngles = b.E;
            t.localScale = b.S;
        }

        static void ApplyBase(Aircraft ac)
        {
            Refresh(ac);
            for (int i = 0; i < Names.Count; i++)
            {
                Transform t;
                Pose b;
                if (Live.TryGetValue(Names[i], out t) && Base.TryGetValue(Names[i], out b))
                {
                    t.localPosition = b.P;
                    t.localEulerAngles = b.E;
                    t.localScale = b.S;
                }
            }
        }

        static void Save(Aircraft ac)
        {
            Refresh(ac);
            string name = string.IsNullOrEmpty(_saveName) ? "cockpit" : _saveName.Trim();
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            Directory.CreateDirectory(Dir());
            string path = Path.Combine(Dir(), name + ".layout");
            StringBuilder sb = new StringBuilder();
            sb.Append("J35COCKPIT 1\n");
            for (int i = 0; i < Names.Count; i++)
            {
                Transform t;
                if (!Live.TryGetValue(Names[i], out t) || t == null)
                    continue;
                Vector3 p = t.localPosition, e = t.localEulerAngles, s = t.localScale;
                sb.Append(Names[i]).Append(' ')
                    .Append(F(p.x)).Append(' ').Append(F(p.y)).Append(' ').Append(F(p.z)).Append(' ')
                    .Append(F(e.x)).Append(' ').Append(F(e.y)).Append(' ').Append(F(e.z)).Append(' ')
                    .Append(F(s.x)).Append(' ').Append(F(s.y)).Append(' ').Append(F(s.z)).Append('\n');
            }
            File.WriteAllText(path, sb.ToString());
            WriteCurrent(path);
            _status = "Saved " + name;
        }

        static void ApplyFile(Aircraft ac, string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return;
            Refresh(ac);
            string[] lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrEmpty(line) || line.StartsWith("J35", StringComparison.Ordinal))
                    continue;
                string[] w = line.Split(' ');
                if (w.Length < 10)
                    continue;
                Transform t;
                if (!Live.TryGetValue(w[0], out t) || t == null)
                    t = Rig.Find(ac.transform, w[0]);
                if (t == null)
                    continue;
                t.localPosition = new Vector3(P(w[1]), P(w[2]), P(w[3]));
                t.localEulerAngles = new Vector3(P(w[4]), P(w[5]), P(w[6]));
                t.localScale = new Vector3(P(w[7]), P(w[8]), P(w[9]));
            }
        }

        static string Dir()
        {
            string plugin = Path.GetDirectoryName(typeof(CockpitEdit).Assembly.Location);
            return Path.Combine(plugin ?? ".", "J35XE", "Cockpits");
        }

        static string CurrentPath()
        {
            return Path.Combine(Dir(), "current.txt");
        }

        static void WriteCurrent(string path)
        {
            try
            {
                Directory.CreateDirectory(Dir());
                File.WriteAllText(CurrentPath(), path ?? "");
            }
            catch { }
        }

        static string ReadCurrent()
        {
            try
            {
                string p = CurrentPath();
                if (!File.Exists(p))
                    return "";
                return File.ReadAllText(p).Trim();
            }
            catch { return ""; }
        }

        static string[] ListFiles()
        {
            try
            {
                if (!Directory.Exists(Dir()))
                    return new string[0];
                return Directory.GetFiles(Dir(), "*.layout");
            }
            catch { return new string[0]; }
        }

        static string F(float v)
        {
            return v.ToString("0.####", CultureInfo.InvariantCulture);
        }

        static float P(string s)
        {
            float v;
            if (!float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                return 0f;
            return v;
        }
    }
}
