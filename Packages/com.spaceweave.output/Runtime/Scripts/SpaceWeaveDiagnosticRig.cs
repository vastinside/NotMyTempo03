using UnityEngine;

namespace SpaceWeave.Output
{
[DisallowMultipleComponent]
public sealed class SpaceWeaveDiagnosticRig : MonoBehaviour
{
    public bool buildOnAwake = true;
    public bool playAudioTestTone;
    public float radius = 8f;
    public Color horizonColour = Color.yellow;
    Transform _movingBar;

    void Awake()
    {
        if (buildOnAwake) Build();
    }

    public void Build()
    {
        if (transform.Find("GeneratedDiagnostics") != null) return;
        Transform root = new GameObject("GeneratedDiagnostics").transform;
        root.SetParent(transform, false);

        AddLabel(root, "FRONT +Z", Vector3.forward, Color.cyan);
        AddLabel(root, "BACK -Z", Vector3.back, Color.magenta);
        AddLabel(root, "RIGHT +X", Vector3.right, Color.red);
        AddLabel(root, "LEFT -X", Vector3.left, Color.green);
        AddLabel(root, "TOP +Y", Vector3.up, Color.blue);
        AddLabel(root, "BOTTOM -Y", Vector3.down, Color.yellow);
        AddHorizon(root);
        AddLatitudeRings(root);
        AddMovingBars(root);
        if (playAudioTestTone) AddAudioTone(root);
    }

    void AddLabel(Transform root, string text, Vector3 direction, Color colour)
    {
        GameObject go = new GameObject(text);
        go.transform.SetParent(root, false);
        go.transform.localPosition = direction.normalized * radius;
        Vector3 up = Mathf.Abs(Vector3.Dot(direction.normalized, Vector3.up)) > 0.95f
            ? Vector3.forward : Vector3.up;
        go.transform.rotation = Quaternion.LookRotation(direction, up);
        TextMesh mesh = go.AddComponent<TextMesh>();
        mesh.text = text + "\n→";
        mesh.anchor = TextAnchor.MiddleCenter;
        mesh.alignment = TextAlignment.Center;
        mesh.fontSize = 96;
        mesh.characterSize = 0.045f;
        mesh.color = colour;
    }

    void AddHorizon(Transform root)
    {
        const int segments = 128;
        LineRenderer line = NewLine(root, "HORIZON 0deg", horizonColour, 0.035f, segments + 1);
        for (int i = 0; i <= segments; ++i)
        {
            float a = i * Mathf.PI * 2f / segments;
            line.SetPosition(i, new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * radius);
        }
    }

    void AddLatitudeRings(Transform root)
    {
        foreach (float latitude in new[] { -60f, -30f, 30f, 60f })
        {
            const int segments = 96;
            float pitch = latitude * Mathf.Deg2Rad;
            float y = Mathf.Sin(pitch) * radius;
            float r = Mathf.Cos(pitch) * radius;
            LineRenderer line = NewLine(root, $"LAT {latitude:+0;-0}", new Color(0.25f, 0.55f, 0.8f),
                0.018f, segments + 1);
            for (int i = 0; i <= segments; ++i)
            {
                float a = i * Mathf.PI * 2f / segments;
                line.SetPosition(i, new Vector3(Mathf.Sin(a) * r, y, Mathf.Cos(a) * r));
            }
        }
    }

    static LineRenderer NewLine(
        Transform root, string name, Color colour, float width, int count)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(root, false);
        LineRenderer line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.positionCount = count;
        line.startWidth = line.endWidth = width;
        line.startColor = line.endColor = colour;
        line.material = new Material(Shader.Find("Sprites/Default"));
        return line;
    }

    void AddMovingBars(Transform root)
    {
        GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bar.name = "MOVING FRAME-PACING BAR";
        bar.transform.SetParent(root, false);
        bar.transform.localScale = new Vector3(0.08f, 3f, 0.02f);
        bar.transform.localPosition = new Vector3(0f, 0f, radius - 0.2f);
        bar.GetComponent<Renderer>().material.color = Color.white;
        Destroy(bar.GetComponent<Collider>());
        _movingBar = bar.transform;
    }

    void AddAudioTone(Transform root)
    {
        const int sampleRate = 48000;
        AudioClip clip = AudioClip.Create("SpaceWeave 1kHz Test Tone", sampleRate, 1, sampleRate, false);
        float[] samples = new float[sampleRate];
        for (int i = 0; i < samples.Length; ++i)
            samples[i] = Mathf.Sin(i * 2f * Mathf.PI * 1000f / sampleRate) * 0.12f;
        clip.SetData(samples, 0);
        AudioSource source = root.gameObject.AddComponent<AudioSource>();
        source.clip = clip;
        source.loop = true;
        source.spatialBlend = 0f;
        source.Play();
    }

    void Update()
    {
        if (_movingBar == null) return;
        Vector3 p = _movingBar.localPosition;
        p.x = Mathf.PingPong(Time.unscaledTime * 2f, 6f) - 3f;
        _movingBar.localPosition = p;
    }
}
}
