using UnityEngine;

namespace J35XE
{
    /// <summary>
    /// Heading cue in the cockpit. The tape and the yellow nose caret stay up after takeoff.
    /// The taxi / rollout / landing line is only drawn with the gear down and low.
    /// </summary>
    [DefaultExecutionOrder(32030)]
    internal sealed class Ohmd : MonoBehaviour
    {
        const float MaxAgl = 800f, TapeSpan = 60f;
        static readonly Color Col = new Color(0.35f, 1f, 0.45f, 0.95f);
        static readonly Color Nose = new Color(1f, 0.85f, 0.25f, 0.95f);

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
            Camera cam = Camera.main;
            if (cam == null)
                return;
            Transform t = _ac.transform;
            Vector3 eye = cam.transform.position;
            float w = Screen.width, h = Screen.height;
            Vector2 mid = new Vector2(w * 0.5f, h * 0.5f);
            float acHdg = Rig.Heading(t.forward);
            Vector3 vel = _ac.rb != null ? _ac.rb.velocity : Vector3.zero;
            bool moving = vel.magnitude > 15f;
            float trk = moving ? Rig.Heading(vel) : acHdg;

            DrawTape(w, Rig.Heading(cam.transform.forward), acHdg, moving ? trk : -1f);

            if (_ac.gearDeployed && _ac.radarAlt <= MaxAgl)
            {
                string phase = _ac.radarAlt < 1.5f ? (_ac.speed < 40f ? "TAXI" : "ROLL") : vel.y >= 0f ? "TKOF" : "LDG";
                GuiDraw.Text(w * 0.5f, 96f, phase + "  HDG " + Mathf.RoundToInt(acHdg).ToString("000")
                    + (moving ? "  TRK " + Mathf.RoundToInt(trk).ToString("000") : ""), Col, true);
            }

            Vector2 nose;
            if (Project(cam, eye + t.forward * 2000f, h, out nose) && Inside(nose, w, h, 40f))
            {
                Waterline(nose);
                GuiDraw.Text(nose.x, nose.y + 14f, Mathf.RoundToInt(acHdg).ToString("000"), Nose, true);
            }
            else
                EdgeArrow(cam, eye + t.forward * 2000f, mid, w, h, "NOSE " + Mathf.RoundToInt(acHdg).ToString("000"));

            Vector2 fpm;
            if (moving && Project(cam, eye + vel.normalized * 2000f, h, out fpm) && Inside(fpm, w, h, 30f))
            {
                GuiDraw.Ring(fpm, 7f, 2f, Col);
                GuiDraw.Line(fpm + new Vector2(-19f, 0f), fpm + new Vector2(-7f, 0f), 2f, Col);
                GuiDraw.Line(fpm + new Vector2(7f, 0f), fpm + new Vector2(19f, 0f), 2f, Col);
                GuiDraw.Line(fpm + new Vector2(0f, -7f), fpm + new Vector2(0f, -14f), 2f, Col);
            }
        }

        /// <summary>Heading scale centred on where the head points; yellow caret is the nose, ring the track.</summary>
        static void DrawTape(float w, float look, float nose, float track)
        {
            float cx = w * 0.5f, y = 70f, px = w * 0.35f / TapeSpan;
            GuiDraw.Line(new Vector2(cx - TapeSpan * px, y), new Vector2(cx + TapeSpan * px, y), 1.5f, Col);
            int first = Mathf.CeilToInt((look - TapeSpan) / 10f) * 10;
            for (int d = first; d <= look + TapeSpan; d += 10)
            {
                float x = cx + Mathf.DeltaAngle(look, d) * px;
                bool major = d % 30 == 0;
                GuiDraw.Line(new Vector2(x, y), new Vector2(x, y - (major ? 9f : 5f)), 1.5f, Col);
                if (major)
                {
                    int lbl = Mathf.RoundToInt(Mathf.Repeat(d, 360f)) / 10;
                    GuiDraw.Text(x, y - 26f, lbl.ToString("00"), Col, true);
                }
            }
            GuiDraw.Line(new Vector2(cx, y + 2f), new Vector2(cx, y + 9f), 2f, Col);
            float dn = Mathf.DeltaAngle(look, nose);
            float nx = cx + Mathf.Clamp(dn, -TapeSpan, TapeSpan) * px;
            GuiDraw.Line(new Vector2(nx - 7f, y + 12f), new Vector2(nx, y + 3f), 2.5f, Nose);
            GuiDraw.Line(new Vector2(nx + 7f, y + 12f), new Vector2(nx, y + 3f), 2.5f, Nose);
            if (track >= 0f)
            {
                float tx = cx + Mathf.Clamp(Mathf.DeltaAngle(look, track), -TapeSpan, TapeSpan) * px;
                GuiDraw.Ring(new Vector2(tx, y + 8f), 4f, 1.5f, Col);
            }
        }

        static void Waterline(Vector2 c)
        {
            Vector2[] pts =
            {
                new Vector2(-30f, 0f), new Vector2(-14f, 0f), new Vector2(-7f, 8f), new Vector2(0f, 0f),
                new Vector2(7f, 8f), new Vector2(14f, 0f), new Vector2(30f, 0f),
            };
            for (int i = 0; i + 1 < pts.Length; i++)
                GuiDraw.Line(c + pts[i], c + pts[i + 1], 2.5f, Nose);
        }

        static void EdgeArrow(Camera cam, Vector3 world, Vector2 mid, float w, float h, string label)
        {
            Vector3 sp = cam.WorldToScreenPoint(world);
            Vector2 dir = new Vector2(sp.x, h - sp.y) - mid;
            if (sp.z < 0f)
                dir = -dir;
            if (dir.sqrMagnitude < 1f)
                dir = Vector2.up;
            dir.Normalize();
            float rx = w * 0.42f, ry = h * 0.40f;
            float k = 1f / Mathf.Sqrt(dir.x * dir.x / (rx * rx) + dir.y * dir.y / (ry * ry));
            Vector2 tip = mid + dir * k;
            Vector2 side = new Vector2(-dir.y, dir.x);
            Vector2 back = tip - dir * 22f;
            GuiDraw.Line(tip, back + side * 11f, 3f, Nose);
            GuiDraw.Line(tip, back - side * 11f, 3f, Nose);
            GuiDraw.Line(back + side * 11f, back - side * 11f, 3f, Nose);
            Vector2 lbl = tip - dir * 44f;
            GuiDraw.Text(lbl.x, lbl.y - 8f, label, Nose, true);
        }

        static bool Project(Camera cam, Vector3 world, float h, out Vector2 p)
        {
            Vector3 sp = cam.WorldToScreenPoint(world);
            p = new Vector2(sp.x, h - sp.y);
            return sp.z > 0.1f;
        }

        static bool Inside(Vector2 p, float w, float h, float pad) => p.x > pad && p.x < w - pad && p.y > pad && p.y < h - pad;
    }
}
