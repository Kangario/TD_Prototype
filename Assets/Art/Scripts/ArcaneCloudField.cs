// Поле мультяшных облачков-тумана (шейдер P1/ArcaneCloud). Меш строится в памяти (в сцену не сохраняется):
// на облачко 4 вершины с точкой, размером, вариантом и фазой - билборд, полёт по ветру и жизненный цикл
// считает вершинный шейдер. Видно и в редакторе без Play, один draw call.
// Форма области: Ring - дуга-кольцо вокруг центра (облака по краю карты), Box - прямоугольник (дымка над полем).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
[AddComponentMenu("P1 Art/Arcane Cloud Field")]
public class ArcaneCloudField : MonoBehaviour
{
    public enum Area { Ring, Box }

    [Header("Где")]
    public Area area = Area.Ring;
    [Tooltip("Ring: радиусы кольца от центра объекта, м")]
    public float innerRadius = 46f, outerRadius = 72f;
    [Tooltip("Ring: дуга в градусах (0 = +X, 90 = +Z)")]
    public float arcFrom = -220f, arcTo = 40f;
    [Tooltip("Box: размер по X и Z, м")]
    public Vector2 boxSize = new Vector2(70f, 50f);
    [Tooltip("Высота над объектом, м (мин, макс)")]
    public Vector2 height = new Vector2(3f, 9f);

    [Header("Облачка")]
    [Range(1, 500)] public int count = 70;
    [Tooltip("Ширина облачка, м (мин, макс)")]
    public Vector2 size = new Vector2(10f, 18f);
    [Tooltip("Высота квада относительно ширины")]
    [Range(0.3f, 1.5f)] public float flatness = 0.85f;
    public Color tint = Color.white;
    [Range(0f, 1f)] public float opacityMin = 0.85f;
    [Tooltip("Разброс скорости жизненного цикла (множитель к _CycleSpeed материала)")]
    public Vector2 cycleSpeed = new Vector2(0.7f, 1.3f);
    public int seed = 7;

    struct Puff
    {
        public Vector3 pos;
        public float size, variant, phase, speed, flip, flat;
        public Color color;
    }

    Mesh _mesh;
    readonly List<Puff> _puffs = new List<Puff>();
    Vector3 _sortedFwd;
    bool _dirty = true;

    void OnEnable()
    {
        _dirty = true;
        Rebuild();
    }

    void OnValidate() => _dirty = true;

    void OnDisable()
    {
        if (_mesh != null)
        {
            if (Application.isPlaying) Destroy(_mesh);
            else DestroyImmediate(_mesh);
            _mesh = null;
        }
    }

    void LateUpdate()
    {
        if (_dirty) Rebuild();
        var cam = Camera.main;
        if (cam != null && Vector3.Angle(cam.transform.forward, _sortedFwd) > 3f)
            Sort(cam.transform.forward);
    }

    public void Rebuild()
    {
        _dirty = false;
        Generate();
        if (_mesh == null)
        {
            _mesh = new Mesh { name = "ArcaneClouds (generated)", hideFlags = HideFlags.DontSave };
            _mesh.MarkDynamic();
        }
        _mesh.Clear();

        int n = _puffs.Count;
        var verts = new Vector3[n * 4];
        var uv0 = new List<Vector4>(n * 4);
        var uv1 = new List<Vector4>(n * 4);
        var cols = new Color[n * 4];
        var corners = new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) };
        for (int i = 0; i < n; i++)
        {
            var p = _puffs[i];
            for (int c = 0; c < 4; c++)
            {
                int k = i * 4 + c;
                verts[k] = p.pos;
                uv0.Add(new Vector4(corners[c].x, corners[c].y, p.size, p.variant));
                uv1.Add(new Vector4(p.phase, p.speed, p.flip, p.flat));
                cols[k] = p.color;
            }
        }
        _mesh.vertices = verts;
        _mesh.SetUVs(0, uv0);
        _mesh.SetUVs(1, uv1);
        _mesh.colors = cols;

        // границы с запасом: билборды, дрейф и перерождение уводят облачка от их точек
        var b = new Bounds(transform.InverseTransformPoint(transform.position), Vector3.zero);
        foreach (var p in _puffs) b.Encapsulate(p.pos);
        b.Expand(new Vector3(size.y * 2f + 60f, size.y * 2f + 10f, size.y * 2f + 60f));
        _mesh.bounds = b;

        var cam = Camera.main;
        Sort(cam != null ? cam.transform.forward : new Vector3(0f, -0.75f, -0.66f));

        GetComponent<MeshFilter>().sharedMesh = _mesh;
        var r = GetComponent<MeshRenderer>();
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
    }

    void Generate()
    {
        _puffs.Clear();
        var rng = new System.Random(seed);
        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        for (int i = 0; i < count; i++)
        {
            Vector3 pos;
            if (area == Area.Ring)
            {
                float ang = R(arcFrom, arcTo) * Mathf.Deg2Rad;
                // ближе к внешнему краю гуще: корень даёт равномерную плотность по площади
                float rad = Mathf.Sqrt(R(innerRadius * innerRadius, outerRadius * outerRadius));
                pos = new Vector3(Mathf.Cos(ang) * rad, 0f, Mathf.Sin(ang) * rad);
            }
            else pos = new Vector3(R(-0.5f, 0.5f) * boxSize.x, 0f, R(-0.5f, 0.5f) * boxSize.y);
            pos.y = R(height.x, height.y);

            float s = R(size.x, size.y);
            var c = tint;
            c.a = tint.a * R(opacityMin, 1f);
            _puffs.Add(new Puff
            {
                pos = pos,
                size = s,
                variant = rng.Next(4),
                phase = (float)rng.NextDouble(),
                speed = R(cycleSpeed.x, cycleSpeed.y),
                flip = rng.Next(2) == 0 ? -1f : 1f,
                flat = flatness * R(0.85f, 1.1f),
                color = c
            });
        }
    }

    /// Порядок отрисовки: дальние облачка раньше. Для TD-камеры направление меняется редко.
    void Sort(Vector3 fwd)
    {
        if (_mesh == null || _puffs.Count == 0) return;
        _sortedFwd = fwd;
        var order = new int[_puffs.Count];
        var depth = new float[_puffs.Count];
        for (int i = 0; i < order.Length; i++)
        {
            order[i] = i;
            depth[i] = Vector3.Dot(transform.TransformPoint(_puffs[i].pos), fwd);
        }
        System.Array.Sort(depth, order);
        System.Array.Reverse(order);
        var tris = new int[order.Length * 6];
        for (int i = 0; i < order.Length; i++)
        {
            int v = order[i] * 4, t = i * 6;
            tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 2;
            tris[t + 3] = v; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
        }
        _mesh.SetTriangles(tris, 0, false);
    }
}
