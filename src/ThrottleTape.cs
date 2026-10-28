using System;
using System.Reflection;
using UnityEngine;

namespace J35XE
{
    /// <summary>
    /// Cockpit throttle tape. The vanilla arc is hidden with the rest of the HUD, so this
    /// draws the same reading in the round-MFD phosphor: a green column up from idle,
    /// amber at full afterburner, and a cyan column down from the idle line in reverse.
    /// </summary>
    [DefaultExecutionOrder(32035)]
    internal sealed class ThrottleTape : MonoBehaviour
    {
        const float BarW = 14f, FwdH = 176f, RevH = 72f;

        static readonly Color Green = new Color(0.31f, 0.94f, 0.43f, 0.96f);
        static readonly Color GreenDim = new Color(0.16f, 0.47f, 0.24f, 0.9f);
        static readonly Color Glow = new Color(0.31f, 0.94f, 0.43f, 0.22f);
        static readonly Color Amber = new Color(1f, 0.72f, 0.18f, 0.96f);
        static readonly Color AmberDim = new Color(1f, 0.62f, 0.12f, 0.45f);
        static readonly Color AmberGlow = new Color(1f, 0.72f, 0.18f, 0.22f);
        static readonly Color Cyan = new Color(0.4f, 0.88f, 1f, 0.96f);
        static readonly Color CyanDim = new Color(0.25f, 0.55f, 0.7f, 0.9f);
        static readonly Color CyanGlow = new Color(0.4f, 0.88f, 1f, 0.22f);
        static readonly Color Track = new Color(0.03f, 0.08f, 0.05f, 0.78f);
        static readonly Color Plate = new Color(0.01f, 0.025f, 0.015f, 0.62f);

        static Type _revType;
        static PropertyInfo _revNow;
        static bool _revLooked;

        Aircraft _ac;

        internal void Init(Aircraft ac)
        {
            _ac = ac;
        }

        void OnGUI()
        {
            if (Event.current == null || Event.current.type != EventType.Repaint)
                return;
            if (_ac == null || _ac.disabled || !Rig.Local(_ac) || !Rig.Cockpit())
                return;
            try { if (DynamicMap.mapMaximized) return; } catch { }

            float thr = 0f;
            try
            {
                ControlInputs inputs = _ac.GetInputs();
                if (inputs != null)
                    thr = Mathf.Clamp01(inputs.throttle);
            }
            catch { }
            float rev = Reverse();
            bool reversing = rev >= 0.02f;
            bool ab = !reversing && thr >= 0.995f;
            Draw(thr, rev, reversing, ab);
        }

        static void Draw(float thr, float rev, bool reversing, bool ab)
        {
            float x = 34f;
            float zero = Screen.height * 0.56f;
            float top = zero - FwdH;
            float revTop = zero + 8f;
            Color fill = ab ? Amber : reversing ? Cyan : Green;
            Color dim = ab ? AmberDim : reversing ? CyanDim : GreenDim;
            Color glow = ab ? AmberGlow : reversing ? CyanGlow : Glow;

            GuiDraw.Rect(x - 22f, top - 22f, 86f, FwdH + RevH + 62f, Plate);
            Frame(x - 22f, top - 22f, 86f, FwdH + RevH + 62f, GreenDim);

            GuiDraw.Text(x + BarW * 0.5f, top - 20f, "THR", GreenDim, true);

            GuiDraw.Rect(x, top, BarW, FwdH, Track);
            float abH = FwdH * 0.12f;
            GuiDraw.Rect(x, top, BarW, abH, AmberDim);
            if (!reversing && thr > 0.004f)
            {
                float h = FwdH * thr;
                GuiDraw.Rect(x - 3f, zero - h, BarW + 6f, h, glow);
                GuiDraw.Rect(x, zero - h, BarW, h, fill);
            }

            GuiDraw.Rect(x, revTop, BarW, RevH, Track);
            if (reversing)
            {
                float h = RevH * Mathf.Clamp01(rev);
                GuiDraw.Rect(x - 3f, revTop, BarW + 6f, h, glow);
                GuiDraw.Rect(x, revTop, BarW, h, Cyan);
            }

            GuiDraw.Rect(x - 4f, zero - 1f, BarW + 8f, 2f, Green);

            for (int i = 0; i <= 10; i++)
            {
                float p = i / 10f;
                float y = zero - FwdH * p;
                bool major = i % 5 == 0;
                GuiDraw.Rect(x + BarW + 3f, y, major ? 10f : 5f, 1f, i == 10 ? AmberDim : GreenDim);
                if (i == 10)
                    GuiDraw.Text(x + BarW + 28f, y - 7f, "AB", AmberDim, true);
                else if (major)
                    GuiDraw.Text(x + BarW + 28f, y - 7f, (i * 10).ToString(), GreenDim, true);
            }
            for (int i = 5; i <= 10; i += 5)
            {
                float y = revTop + RevH * (i / 10f);
                GuiDraw.Rect(x + BarW + 3f, y, 8f, 1f, CyanDim);
                GuiDraw.Text(x + BarW + 28f, y - 7f, "R" + (i * 10), CyanDim, true);
            }

            float mark = reversing ? revTop + RevH * Mathf.Clamp01(rev) : zero - FwdH * thr;
            Caret(x - 2f, mark, fill);

            string read = reversing
                ? "R " + Mathf.RoundToInt(rev * 100f)
                : ab ? "AB" : Mathf.RoundToInt(thr * 100f).ToString();
            GuiDraw.Text(x + BarW * 0.5f, revTop + RevH + 8f, read, fill);
            if (!reversing && !ab)
                GuiDraw.Text(x + BarW * 0.5f, revTop + RevH + 24f, "%", dim, true);
        }

        static void Frame(float x, float y, float w, float h, Color c)
        {
            GuiDraw.Rect(x, y, w, 1f, c);
            GuiDraw.Rect(x, y + h - 1f, w, 1f, c);
            GuiDraw.Rect(x, y, 1f, h, c);
            GuiDraw.Rect(x + w - 1f, y, 1f, h, c);
        }

        static void Caret(float x, float y, Color c)
        {
            GuiDraw.Line(new Vector2(x - 9f, y - 6f), new Vector2(x, y), 2f, c);
            GuiDraw.Line(new Vector2(x - 9f, y + 6f), new Vector2(x, y), 2f, c);
        }

        static float Reverse()
        {
            if (!_revLooked)
            {
                _revLooked = true;
                Assembly[] all = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i].GetName().Name != "OritasySS")
                        continue;
                    _revType = all[i].GetType("OritasySS.Rev");
                    _revNow = _revType != null ? _revType.GetProperty("Now") : null;
                    break;
                }
            }
            if (_revNow == null)
                return 0f;
            try { return (float)_revNow.GetValue(null, null); }
            catch { return 0f; }
        }
    }
}
