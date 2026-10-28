using System.Collections.Generic;
using UnityEngine;

namespace J35XE
{
    /// <summary>
    /// JATO nozzle in the brake-chute housing (fairing under the rudder, above the exhaust).
    /// K toggles 250 kN; a toggle is refused for 5 s after the last one. No fuel or burn limit.
    /// The thrust line runs from the housing through the dry COM so it does not pitch the jet.
    /// </summary>
    internal sealed class Jato : MonoBehaviour
    {
        const float Thrust = 250000f;
        const float Cooldown = 5f;
        const KeyCode Key = KeyCode.K;

        // Aircraft frame, measured off fuse_outer: the housing's rear face is a 0.35 x 0.23 m
        // ellipse ending at z -6.674.
        static readonly Vector3 HousingFace = new Vector3(0f, 1.495f, -6.674f);
        static readonly Vector3 DryCom = new Vector3(0f, 0.955f, -1.443f);
        const float RadiusX = 0.176f, RadiusY = 0.115f, Length = 0.20f;
        // The main exhaust the afterburner flame mesh was sized for is ~0.8 m across.
        const float FlameScale = 0.36f;

        Aircraft _ac;
        Transform _nozzle;
        Renderer _throat;
        readonly List<Renderer> _flames = new List<Renderer>();
        AudioSource _audio;
        bool _on;
        float _nextToggle;

        internal void Init(Aircraft ac)
        {
            _ac = ac;
            Transform parent = Tail(ac);
            Transform root = ac.transform;
            GameObject go = new GameObject("jato_nozzle");
            go.layer = ac.gameObject.layer;
            go.transform.SetParent(parent, false);
            go.transform.position = root.TransformPoint(HousingFace);
            Vector3 line = root.TransformDirection((DryCom - HousingFace).normalized);
            go.transform.rotation = Quaternion.LookRotation(line, root.up);
            _nozzle = go.transform;
            BuildShell();
            CopyFlames();
            CopyAudio();
            Show(false);
        }

        static Transform Tail(Aircraft ac)
        {
            AeroPart[] parts = ac.GetComponentsInChildren<AeroPart>(true);
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] != null && parts[i].gameObject.name == "tail")
                    return parts[i].transform;
            }
            return ac.transform;
        }

        void Update()
        {
            if (_ac == null || _nozzle == null || !Input.GetKeyDown(Key))
                return;
            Aircraft local;
            if (!GameManager.GetLocalAircraft(out local) || local != _ac)
                return;
            if (Time.time < _nextToggle)
                return;
            _on = !_on;
            _nextToggle = Time.time + Cooldown;
            Show(_on);
        }

        void FixedUpdate()
        {
            if (!_on || _ac == null || _nozzle == null || !_ac.LocalSim)
                return;
            Rigidbody rb = _nozzle.GetComponentInParent<Rigidbody>();
            if (rb != null)
                rb.AddForceAtPosition(_nozzle.forward * Thrust, _nozzle.position);
        }

        void Show(bool on)
        {
            for (int i = 0; i < _flames.Count; i++)
            {
                if (_flames[i] != null)
                    _flames[i].enabled = on;
            }
            if (_throat != null)
            {
                Material m = _throat.material;
                Color glow = on ? new Color(4f, 1.6f, 0.4f) : Color.black;
                if (m.HasProperty("_EmissionColor"))
                    m.SetColor("_EmissionColor", glow);
                if (on)
                    m.EnableKeyword("_EMISSION");
            }
            if (_audio != null)
            {
                if (on && !_audio.isPlaying)
                    _audio.Play();
                else if (!on && _audio.isPlaying)
                    _audio.Stop();
            }
        }

        void BuildShell()
        {
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null)
                lit = Shader.Find("Standard");
            Material metal = new Material(lit);
            metal.color = new Color(0.16f, 0.15f, 0.14f);
            if (metal.HasProperty("_Metallic"))
                metal.SetFloat("_Metallic", 0.85f);
            if (metal.HasProperty("_Smoothness"))
                metal.SetFloat("_Smoothness", 0.45f);
            Material throat = new Material(lit);
            throat.color = new Color(0.03f, 0.03f, 0.03f);

            AddMesh("jato_shell", Shell(), metal);
            _throat = AddMesh("jato_throat", Disc(-Length * 0.6f, 0.8f), throat);
        }

        Renderer AddMesh(string name, Mesh mesh, Material mat)
        {
            GameObject go = new GameObject(name);
            go.layer = _nozzle.gameObject.layer;
            go.transform.SetParent(_nozzle, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            return mr;
        }

        // Elliptic tube from the housing face (z 0) back to -Length, outer and inner walls,
        // each wound both ways so it reads from any angle.
        static Mesh Shell()
        {
            const int seg = 32;
            List<Vector3> v = new List<Vector3>();
            List<int> t = new List<int>();
            float[] scales = { 1f, 0.82f };
            for (int s = 0; s < scales.Length; s++)
            {
                int start = v.Count;
                for (int i = 0; i <= seg; i++)
                {
                    float a = i * Mathf.PI * 2f / seg;
                    float x = Mathf.Cos(a) * RadiusX * scales[s];
                    float y = Mathf.Sin(a) * RadiusY * scales[s];
                    v.Add(new Vector3(x, y, 0f));
                    v.Add(new Vector3(x, y, -Length));
                }
                for (int i = 0; i < seg; i++)
                {
                    int a0 = start + i * 2, a1 = a0 + 1, b0 = a0 + 2, b1 = a0 + 3;
                    Quad(t, a0, b0, b1, a1);
                }
            }
            // Rear lip joining the outer and inner walls.
            int lip = v.Count;
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                v.Add(new Vector3(Mathf.Cos(a) * RadiusX, Mathf.Sin(a) * RadiusY, -Length));
                v.Add(new Vector3(Mathf.Cos(a) * RadiusX * 0.82f, Mathf.Sin(a) * RadiusY * 0.82f, -Length));
            }
            for (int i = 0; i < seg; i++)
            {
                int a0 = lip + i * 2, a1 = a0 + 1, b0 = a0 + 2, b1 = a0 + 3;
                Quad(t, a0, b0, b1, a1);
            }
            Mesh m = new Mesh();
            m.name = "jato_shell";
            m.SetVertices(v);
            m.SetTriangles(t, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        static Mesh Disc(float z, float scale)
        {
            const int seg = 32;
            List<Vector3> v = new List<Vector3> { new Vector3(0f, 0f, z) };
            List<int> t = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                v.Add(new Vector3(Mathf.Cos(a) * RadiusX * scale, Mathf.Sin(a) * RadiusY * scale, z));
            }
            for (int i = 1; i <= seg; i++)
            {
                t.Add(0); t.Add(i); t.Add(i + 1);
                t.Add(0); t.Add(i + 1); t.Add(i);
            }
            Mesh m = new Mesh();
            m.name = "jato_throat";
            m.SetVertices(v);
            m.SetTriangles(t, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        static void Quad(List<int> t, int a, int b, int c, int d)
        {
            t.Add(a); t.Add(b); t.Add(c);
            t.Add(a); t.Add(c); t.Add(d);
            t.Add(a); t.Add(c); t.Add(b);
            t.Add(a); t.Add(d); t.Add(c);
        }

        void CopyFlames()
        {
            string[] names = { "afterburner_flame", "afterburner_nozzle_glow" };
            Transform[] all = _ac.GetComponentsInChildren<Transform>(true);
            for (int n = 0; n < names.Length; n++)
            {
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] == null || all[i].name != names[n])
                        continue;
                    Renderer src = all[i].GetComponent<Renderer>();
                    if (src == null)
                        break;
                    GameObject copy = Instantiate(all[i].gameObject, _nozzle, false);
                    copy.name = "jato_" + names[n];
                    copy.transform.localPosition = new Vector3(0f, 0f, -Length);
                    copy.transform.localRotation = all[i].localRotation;
                    Vector3 s = all[i].localScale;
                    copy.transform.localScale = new Vector3(s.x * FlameScale, s.y * FlameScale, s.z * FlameScale * 1.6f);
                    Renderer r = copy.GetComponent<Renderer>();
                    r.sharedMaterials = src.sharedMaterials;
                    _flames.Add(r);
                    break;
                }
            }
        }

        void CopyAudio()
        {
            AudioSource[] sources = _ac.GetComponentsInChildren<AudioSource>(true);
            for (int i = 0; i < sources.Length; i++)
            {
                AudioSource s = sources[i];
                if (s == null || s.clip == null || s.clip.name.IndexOf("fterburner") < 0)
                    continue;
                _audio = _nozzle.gameObject.AddComponent<AudioSource>();
                _audio.clip = s.clip;
                _audio.outputAudioMixerGroup = s.outputAudioMixerGroup;
                _audio.loop = true;
                _audio.playOnAwake = false;
                _audio.spatialBlend = 1f;
                _audio.minDistance = s.minDistance;
                _audio.maxDistance = s.maxDistance;
                _audio.dopplerLevel = s.dopplerLevel;
                _audio.volume = 1f;
                return;
            }
        }
    }
}
