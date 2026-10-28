using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace J35XE
{
    /// <summary>
    /// SGLOC on the J-35 only. H arms it below the speed of sound. [ and ] pick
    /// how fast the nose swings. Holding the stick yaws, pitches and rolls the
    /// jet; centering the stick drops the mode. The pilot stays conscious.
    /// </summary>
    [DefaultExecutionOrder(32040)]
    internal sealed class Sgloc : MonoBehaviour
    {
        const float MinSpeed = 40f;
        const float SpeedKeep = 0.96f;

        internal static bool Active { get; private set; }

        static bool _foreign;

        Aircraft _ac;
        Rigidbody _body;
        float _holdSpeed;
        float _savedDrag;
        bool _haveDrag;
        int _gear = 1;
        bool _deflected;
        float _centerSince;
        float _lineUntil;
        string _line = "";
        bool _savedLimiter;
        object _fbw;
        readonly float[] _fbwSaved = new float[6];
        readonly List<SavedSurface> _surfaces = new List<SavedSurface>();
        readonly List<SavedThrust> _thrusts = new List<SavedThrust>();
        float _calmUntil;
        GUIStyle _name, _bodyStyle;

        static readonly string[] FbwLimits =
        {
            "gLimitPositive", "alphaLimiter", "alphaLimiterStrength",
            "maxPitchAngularVel", "maxRollAngularVel", "postStallManeuverSpeed"
        };

        internal void Init(Aircraft ac)
        {
            _ac = ac;
            if (_foreign)
                return;
            Assembly[] all = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].GetType("SGLOC.Plugin") != null)
                {
                    _foreign = true;
                    break;
                }
            }
        }

        void Update()
        {
            if (_foreign || _ac == null || !Rig.Local(_ac))
                return;
            if (Input.GetKeyDown(KeyCode.LeftBracket))
                StepGear(-1);
            else if (Input.GetKeyDown(KeyCode.RightBracket))
                StepGear(1);
            if (!Input.GetKeyDown(KeyCode.H))
                return;
            if (Active)
            {
                End();
                Say("Released.");
                return;
            }
            Rigidbody body = _ac.GetComponent<Rigidbody>();
            if (body == null || OnGround())
                return;
            float speed = body.velocity.magnitude;
            if (speed >= Sound())
                return;
            Begin(body, speed);
            Say("Online.");
        }

        void FixedUpdate()
        {
            if (!Active || _ac == null || _body == null)
                return;
            Relax(true);
            HoldThrust();
            KeepSpeed();
            CalmPilot();
            if (StickHeld())
                Drive();
        }

        void LateUpdate()
        {
            if (!Active && Time.unscaledTime > _calmUntil)
                return;
            CalmVeil();
            if (!Active)
                return;
            CalmPilot();
            if (StickHeld())
            {
                _deflected = true;
                _centerSince = 0f;
                Drive();
                return;
            }
            if (!_deflected)
                return;
            if (_centerSince <= 0f)
                _centerSince = Time.unscaledTime;
            else if (Time.unscaledTime - _centerSince >= 0.12f)
            {
                End();
                Say("Released.");
            }
        }

        void OnGUI()
        {
            if (_foreign || _ac == null || !Rig.Local(_ac))
                return;
            if (Event.current == null || Event.current.type != EventType.Repaint)
                return;
            if (Time.unscaledTime > _lineUntil || string.IsNullOrEmpty(_line))
                return;
            if (_name == null)
            {
                _name = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                    fontSize = 18
                };
                _name.normal.textColor = Color.white;
                _bodyStyle = new GUIStyle(_name) { fontSize = 20, fontStyle = FontStyle.Normal };
            }
            float w = Mathf.Clamp(Screen.width * 0.7f, 420f, 980f);
            float x = (Screen.width - w) * 0.5f;
            float y = Mathf.Max(28f, Screen.height * 0.06f);
            GUI.Label(new Rect(x, y, w, 28f), "SGLOC System", _name);
            GUI.Label(new Rect(x, y + 30f, w, 32f), "<< " + _line + " >>", _bodyStyle);
        }

        void Begin(Rigidbody body, float speed)
        {
            Active = true;
            _body = body;
            _holdSpeed = Mathf.Max(speed, MinSpeed);
            Relax(true);
            BoostSurfaces(true);
            BoostThrust(true);
            _savedDrag = body.angularDrag;
            _haveDrag = true;
            body.angularDrag = 0f;
            _deflected = false;
            _centerSince = 0f;
            _calmUntil = Time.unscaledTime + 1.5f;
            Durability.Brace(_ac);
        }

        void End()
        {
            if (_ac != null)
            {
                Relax(false);
                BoostSurfaces(false);
                BoostThrust(false);
                Durability.Unbrace(_ac);
            }
            if (_haveDrag && _body != null)
                _body.angularDrag = _savedDrag;
            _haveDrag = false;
            Active = false;
            _body = null;
            _fbw = null;
            _calmUntil = Time.unscaledTime + 1.2f;
        }

        void StepGear(int dir)
        {
            int next = Mathf.Clamp(_gear + dir, 1, 4);
            if (next == _gear)
                return;
            _gear = next;
            Say("Gear " + _gear + ".");
        }

        void Say(string quote)
        {
            _line = quote ?? "";
            _lineUntil = Time.unscaledTime + 3.2f;
        }

        float TurnRate(float stick)
        {
            float seconds = _gear == 2 ? 2f : _gear == 3 ? 1f : _gear >= 4 ? 0.5f : 4f;
            return stick * (Mathf.PI * 2f / seconds);
        }

        bool StickHeld()
        {
            float p, r, y;
            Axes(out p, out r, out y);
            return Mathf.Abs(p) >= 0.08f || Mathf.Abs(r) >= 0.08f || Mathf.Abs(y) >= 0.08f;
        }

        void Drive()
        {
            float pitch, roll, yaw;
            Axes(out pitch, out roll, out yaw);
            Vector3 av = Vector3.zero;
            if (Mathf.Abs(pitch) >= 0.08f)
                av += _ac.transform.right * TurnRate(pitch);
            if (Mathf.Abs(roll) >= 0.08f)
                av += _ac.transform.forward * TurnRate(roll);
            if (Mathf.Abs(yaw) >= 0.08f)
                av += _ac.transform.up * TurnRate(yaw);
            if (av.sqrMagnitude <= 0f)
                return;
            _body.angularVelocity = av;
        }

        void Axes(out float pitch, out float roll, out float yaw)
        {
            pitch = roll = yaw = 0f;
            try
            {
                ControlInputs inputs = _ac.GetInputs();
                if (inputs == null)
                    return;
                pitch = inputs.pitch;
                roll = inputs.roll;
                yaw = inputs.yaw;
            }
            catch { }
        }

        void KeepSpeed()
        {
            Vector3 v = _body.velocity;
            float mag = v.magnitude;
            if (mag > _holdSpeed)
                _holdSpeed = mag;
            float floor = _holdSpeed * SpeedKeep;
            if (mag < 1f || mag >= floor)
                return;
            _body.velocity = v * (floor / mag);
        }

        void HoldThrust()
        {
            if (_thrusts.Count == 0)
                BoostThrust(true);
            float throttle = 0f;
            try
            {
                ControlInputs inputs = _ac.GetInputs();
                if (inputs != null)
                    throttle = Mathf.Clamp01(inputs.throttle);
            }
            catch { }
            float extra = 0f;
            for (int i = 0; i < _thrusts.Count; i++)
            {
                if (_thrusts[i].part == null)
                    continue;
                float now = GetFloat(_thrusts[i].part, "currentThrust");
                if (now > extra)
                    extra = now;
            }
            if (extra < 1f)
            {
                for (int i = 0; i < _thrusts.Count; i++)
                    extra += _thrusts[i].value * throttle;
            }
            if (extra >= 1f)
                _body.AddForce(_ac.transform.forward * extra * 4f, ForceMode.Force);
        }

        void BoostThrust(bool on)
        {
            if (!on)
            {
                for (int i = 0; i < _thrusts.Count; i++)
                    SetFloat(_thrusts[i].part, _thrusts[i].field, _thrusts[i].value);
                _thrusts.Clear();
                return;
            }
            if (_thrusts.Count > 0 || _ac == null)
                return;
            MonoBehaviour[] parts = _ac.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < parts.Length; i++)
            {
                NoteThrust(parts[i], "maxThrust");
                NoteThrust(parts[i], "staticThrust");
            }
        }

        void NoteThrust(Component part, string field)
        {
            if (part == null)
                return;
            FieldInfo info = part.GetType().GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (info == null || info.FieldType != typeof(float))
                return;
            float value = GetFloat(part, field);
            if (value < 1f)
                return;
            _thrusts.Add(new SavedThrust { part = part, field = field, value = value });
            SetFloat(part, field, value * 2f);
        }

        void BoostSurfaces(bool on)
        {
            if (!on)
            {
                for (int i = 0; i < _surfaces.Count; i++)
                {
                    SavedSurface s = _surfaces[i];
                    if (s.surface == null)
                        continue;
                    SetFloat(s.surface, "pitchRange", s.pitch);
                    SetFloat(s.surface, "rollRange", s.roll);
                    SetFloat(s.surface, "yawRange", s.yaw);
                    SetFloat(s.surface, "servoSpeed", s.servo);
                    try { s.surface.UpdateJobFields(); }
                    catch { }
                }
                _surfaces.Clear();
                return;
            }
            if (_surfaces.Count > 0 || _ac == null)
                return;
            ControlSurface[] all = _ac.GetComponentsInChildren<ControlSurface>(true);
            for (int i = 0; i < all.Length; i++)
            {
                ControlSurface surface = all[i];
                if (surface == null)
                    continue;
                SavedSurface s = new SavedSurface
                {
                    surface = surface,
                    pitch = GetFloat(surface, "pitchRange"),
                    roll = GetFloat(surface, "rollRange"),
                    yaw = GetFloat(surface, "yawRange"),
                    servo = GetFloat(surface, "servoSpeed")
                };
                _surfaces.Add(s);
                SetFloat(surface, "pitchRange", s.pitch * 2.5f);
                SetFloat(surface, "rollRange", s.roll * 2.5f);
                SetFloat(surface, "yawRange", s.yaw * 2.5f);
                SetFloat(surface, "servoSpeed", s.servo > 0.1f ? s.servo * 3f : 8f);
                try { surface.UpdateJobFields(); }
                catch { }
            }
        }

        void Relax(bool on)
        {
            ControlsFilter filter = null;
            try { filter = _ac.GetControlsFilter(); }
            catch { }
            if (filter == null)
                return;
            object fbw = null;
            FieldInfo wire = filter.GetType().GetField("flyByWire", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (wire != null)
                fbw = wire.GetValue(filter);
            if (fbw == null)
                return;
            _fbw = fbw;
            if (!on)
            {
                if (!_savedLimiter)
                    return;
                for (int i = 0; i < FbwLimits.Length; i++)
                    SetFloat(fbw, FbwLimits[i], _fbwSaved[i]);
                _savedLimiter = false;
                return;
            }
            if (!_savedLimiter)
            {
                for (int i = 0; i < FbwLimits.Length; i++)
                    _fbwSaved[i] = GetFloat(fbw, FbwLimits[i]);
                _savedLimiter = true;
            }
            SetFloat(fbw, "gLimitPositive", 40f);
            SetFloat(fbw, "alphaLimiter", 90f);
            SetFloat(fbw, "alphaLimiterStrength", 0f);
            SetFloat(fbw, "maxPitchAngularVel", 12f);
            SetFloat(fbw, "maxRollAngularVel", 12f);
            SetFloat(fbw, "postStallManeuverSpeed", 1000f);
        }

        void CalmPilot()
        {
            try { _ac.gForce = 1.2f; }
            catch { }
            Pilot[] pilots = null;
            try { pilots = _ac.pilots; }
            catch { }
            if (pilots == null)
                return;
            for (int i = 0; i < pilots.Length; i++)
            {
                if (pilots[i] == null)
                    continue;
                try { pilots[i].gForce = 1.2f; }
                catch { }
            }
        }

        void CalmVeil()
        {
            GLOC g = _ac.GetComponentInChildren<GLOC>(true);
            if (g == null)
                return;
            Set(g, "conscious", true);
            Set(g, "bloodPressure", 1f);
            Set(g, "stamina", 1f);
        }

        float Sound()
        {
            try
            {
                float v = LevelInfo.GetSpeedOfSound(_ac.transform.position.y);
                if (v > 50f)
                    return v;
            }
            catch { }
            return 340f;
        }

        bool OnGround()
        {
            RaycastHit hit;
            Vector3 from = _ac.transform.position + Vector3.up * 2f;
            if (!Physics.Raycast(from, Vector3.down, out hit, 18f))
                return false;
            if (hit.collider != null && hit.collider.transform.IsChildOf(_ac.transform))
                return false;
            return hit.distance < 6f;
        }

        static float GetFloat(object target, string name)
        {
            if (target == null)
                return 0f;
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null || field.FieldType != typeof(float))
                return 0f;
            try { return (float)field.GetValue(target); }
            catch { return 0f; }
        }

        static void SetFloat(object target, string name, float value)
        {
            if (target == null)
                return;
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null)
                return;
            try { field.SetValue(target, value); }
            catch { }
        }

        static void Set(object target, string name, object value)
        {
            if (target == null)
                return;
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null)
                return;
            try { field.SetValue(target, value); }
            catch { }
        }

        class SavedSurface
        {
            public ControlSurface surface;
            public float pitch, roll, yaw, servo;
        }

        class SavedThrust
        {
            public Component part;
            public string field;
            public float value;
        }
    }
}
