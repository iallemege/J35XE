using UnityEngine;

namespace J35XE
{
    /// <summary>IMGUI lines, brackets and shadowed text for the COFFIN marks and the OHMD cue.</summary>
    internal static class GuiDraw
    {
        static Texture2D _white;
        static GUIStyle _text, _small;

        static Texture2D White
        {
            get
            {
                if (_white == null)
                {
                    _white = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                    _white.SetPixel(0, 0, Color.white);
                    _white.Apply();
                }
                return _white;
            }
        }

        internal static void Rect(float x, float y, float w, float h, Color c)
        {
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(new Rect(x, y, w, h), White);
            GUI.color = prev;
        }

        internal static void Line(Vector2 a, Vector2 b, float width, Color c)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 0.5f)
                return;
            Matrix4x4 m = GUI.matrix;
            Color prev = GUI.color;
            GUI.color = c;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, a);
            GUI.DrawTexture(new Rect(a.x, a.y - width * 0.5f, len, width), White);
            GUI.matrix = m;
            GUI.color = prev;
        }

        /// <summary>Four corner brackets around a centre; half is the half-size, arm the bracket length.</summary>
        internal static void Brackets(Vector2 c, float half, float arm, float w, Color col)
        {
            float l = c.x - half, r = c.x + half, t = c.y - half, b = c.y + half;
            Rect(l, t, arm, w, col); Rect(l, t, w, arm, col);
            Rect(r - arm, t, arm, w, col); Rect(r - w, t, w, arm, col);
            Rect(l, b - w, arm, w, col); Rect(l, b - arm, w, arm, col);
            Rect(r - arm, b - w, arm, w, col); Rect(r - w, b - arm, w, arm, col);
        }

        internal static void Box(Vector2 c, float half, float w, Color col)
        {
            Rect(c.x - half, c.y - half, half * 2f, w, col);
            Rect(c.x - half, c.y + half - w, half * 2f, w, col);
            Rect(c.x - half, c.y - half, w, half * 2f, col);
            Rect(c.x + half - w, c.y - half, w, half * 2f, col);
        }

        internal static void Diamond(Vector2 c, float r, float w, Color col)
        {
            Vector2 n = c + new Vector2(0f, -r), e = c + new Vector2(r, 0f), s = c + new Vector2(0f, r), wv = c + new Vector2(-r, 0f);
            Line(n, e, w, col); Line(e, s, w, col); Line(s, wv, w, col); Line(wv, n, w, col);
        }

        internal static void Ring(Vector2 c, float r, float w, Color col)
        {
            const int seg = 20;
            Vector2 prev = c + new Vector2(r, 0f);
            for (int i = 1; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                Vector2 p = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                Line(prev, p, w, col);
                prev = p;
            }
        }

        internal static void Text(float cx, float y, string s, Color col, bool small = false)
        {
            if (string.IsNullOrEmpty(s))
                return;
            GUIStyle st = Style(small);
            Vector2 size = st.CalcSize(new GUIContent(s));
            Rect r = new Rect(cx - size.x * 0.5f, y, size.x, size.y);
            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, col.a * 0.85f);
            GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), s, st);
            GUI.color = col;
            GUI.Label(r, s, st);
            GUI.color = prev;
        }

        static GUIStyle Style(bool small)
        {
            if (_text == null)
            {
                _text = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperLeft };
                _text.normal.textColor = Color.white;
                _text.wordWrap = false;
                _small = new GUIStyle(_text) { fontSize = 11, fontStyle = FontStyle.Normal };
            }
            return small ? _small : _text;
        }
    }
}
