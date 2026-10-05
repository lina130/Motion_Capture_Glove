using UnityEngine;

/// <summary>
/// 纯圆三维穹顶受力膜 | 面板实时调半径 | 无受力完全隐藏 | 平滑渐变 | 已加显示阈值
/// </summary>
public class ForceGridVisualizer : MonoBehaviour
{
    [Header("圆形三维画布 (Squircle Dome)")]
    [Tooltip("穹顶半径，面板拖动实时生效，推荐0.01~0.015")]
    public float radius = 0.006f;
    public float domeHeight = 0.003f;
    [Range(20, 60)] public int resolution = 40;

    [Header("力学形变参数")]
    [Tooltip("显示阈值：合力低于此值时完全隐藏，过滤传感器零点漂移")]
    public float displayThreshold = 1.0f; // 🔥 新增：力值显示阈值
    public float maxDeformDepth = 0.015f;
    public float forceSpread = 0.00005f;
    public float maxForceThreshold = 10f;
    public float smoothSpeed = 16f;
    [Tooltip("显示/隐藏的渐变速度")]
    public float fadeSpeed = 20f;

    [Header("热力色彩")]
    public Gradient heatGradient;

    private Mesh _membraneMesh;
    private Vector3[] _baseVertices;
    private Vector3[] _dynamicVertices;
    private Color[] _vertexColors;
    private MeshFilter _mf;
    private MeshRenderer _mr;

    private Vector3 _targetForce;
    private Vector3 _smoothedForce;
    private float _currentAlpha; // 全局透明度控制，实现淡入淡出
    private float _lastRadius; // 缓存上一帧半径，实现实时更新

    void Awake()
    {
        _mf = gameObject.AddComponent<MeshFilter>();
        _mr = gameObject.AddComponent<MeshRenderer>();

        Shader transShader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
        if (transShader == null) transShader = Shader.Find("Particles/Standard Unlit");
        Material mat = new Material(transShader);
        mat.renderQueue = 3000;
        mat.SetInt("_ZWrite", 0);
        _mr.material = mat;
        _mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _mr.receiveShadows = false;

        InitializeHeatGradient();
        GenerateSquircleDome();
        _lastRadius = radius;
        _currentAlpha = 0f; // 初始完全隐藏
    }

    void InitializeHeatGradient()
    {
        if (heatGradient != null && heatGradient.colorKeys.Length > 0) return;
        heatGradient = new Gradient();
        GradientColorKey[] colors = {
            new GradientColorKey(new Color(0f, 0.4f, 1f), 0.0f),
            new GradientColorKey(Color.cyan, 0.25f),
            new GradientColorKey(Color.green, 0.5f),
            new GradientColorKey(Color.yellow, 0.75f),
            new GradientColorKey(Color.red, 1.0f)
        };
        GradientAlphaKey[] alphas = {
            new GradientAlphaKey(1.0f, 0.0f),
            new GradientAlphaKey(1.0f, 1.0f)
        };
        heatGradient.SetKeys(colors, alphas);
    }

    void GenerateSquircleDome()
    {
        _membraneMesh = new Mesh();
        _membraneMesh.MarkDynamic();
        _mf.mesh = _membraneMesh;

        int vertCount = resolution * resolution;
        _baseVertices = new Vector3[vertCount];
        _dynamicVertices = new Vector3[vertCount];
        _vertexColors = new Color[vertCount];

        int[] triangles = new int[(resolution - 1) * (resolution - 1) * 6];
        int triIndex = 0;

        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                int i = z * resolution + x;

                float u = (x / (float)(resolution - 1)) * 2f - 1f;
                float v = (z / (float)(resolution - 1)) * 2f - 1f;

                float px = u * Mathf.Sqrt(1f - v * v / 2f) * radius;
                float pz = v * Mathf.Sqrt(1f - u * u / 2f) * radius;

                float distFromCenter = Mathf.Sqrt(px * px + pz * pz);
                float normalizedDist = Mathf.Clamp01(distFromCenter / radius);

                float py = domeHeight * (Mathf.Cos(normalizedDist * Mathf.PI) + 1f) / 2f;

                _baseVertices[i] = new Vector3(px, py, pz);
                _dynamicVertices[i] = _baseVertices[i];
                _vertexColors[i] = heatGradient.Evaluate(0f);
                _vertexColors[i].a = 0f; // 初始完全透明

                if (x < resolution - 1 && z < resolution - 1)
                {
                    triangles[triIndex] = i;
                    triangles[triIndex + 1] = i + resolution;
                    triangles[triIndex + 2] = i + 1;
                    triangles[triIndex + 3] = i + 1;
                    triangles[triIndex + 4] = i + resolution;
                    triangles[triIndex + 5] = i + resolution + 1;
                    triIndex += 6;
                }
            }
        }

        _membraneMesh.vertices = _baseVertices;
        _membraneMesh.triangles = triangles;
        _membraneMesh.colors = _vertexColors;
        _membraneMesh.RecalculateNormals();
    }

    // 运行时实时更新半径
    void UpdateRadius()
    {
        if (Mathf.Abs(radius - _lastRadius) < 0.0001f) return;

        _lastRadius = radius;
        for (int i = 0; i < _baseVertices.Length; i++)
        {
            float u = (i % resolution) / (float)(resolution - 1) * 2f - 1f;
            float v = (i / resolution) / (float)(resolution - 1) * 2f - 1f;

            float px = u * Mathf.Sqrt(1f - v * v / 2f) * radius;
            float pz = v * Mathf.Sqrt(1f - u * u / 2f) * radius;

            float distFromCenter = Mathf.Sqrt(px * px + pz * pz);
            float normalizedDist = Mathf.Clamp01(distFromCenter / radius);
            float py = domeHeight * (Mathf.Cos(normalizedDist * Mathf.PI) + 1f) / 2f;

            _baseVertices[i] = new Vector3(px, py, pz);
        }
    }

    public void UpdateForce(Vector3 force)
    {
        _targetForce = (force.magnitude < 0.001f) ? Vector3.zero : force;
    }

    void Update()
    {
        // 运行时实时更新半径
        UpdateRadius();

        _smoothedForce = Vector3.Lerp(_smoothedForce, _targetForce, Time.deltaTime * smoothSpeed);
        float magnitude = _smoothedForce.magnitude;

        // 🔥 核心：力值低于阈值时完全隐藏
        float targetAlpha = magnitude > displayThreshold ? 1f : 0f;
        _currentAlpha = Mathf.Lerp(_currentAlpha, targetAlpha, Time.deltaTime * fadeSpeed);

        if (_currentAlpha < 0.001f)
        {
            ResetMembrane();
            return;
        }

        Vector3 localForceDir = transform.InverseTransformDirection(_smoothedForce.normalized);
        // 🔥 优化：热力映射从阈值开始计算，避免阈值附近颜色过浅
        float forceStrength = Mathf.Clamp01((magnitude - displayThreshold) / (maxForceThreshold - displayThreshold));

        for (int i = 0; i < _baseVertices.Length; i++)
        {
            float sqrDist = (_baseVertices[i].x * _baseVertices[i].x) + (_baseVertices[i].z * _baseVertices[i].z);
            float influence = Mathf.Exp(-sqrDist / forceSpread);

            _dynamicVertices[i] = _baseVertices[i] + localForceDir * (forceStrength * influence * maxDeformDepth);

            float localStress = forceStrength * influence;
            _vertexColors[i] = heatGradient.Evaluate(Mathf.Clamp01(localStress));

            // 边缘透明 + 全局淡入淡出
            float normalizedDist = Mathf.Clamp01(Mathf.Sqrt(sqrDist) / radius);
            _vertexColors[i].a = (1f - normalizedDist) * _currentAlpha;
        }

        _membraneMesh.vertices = _dynamicVertices;
        _membraneMesh.colors = _vertexColors;
        _membraneMesh.RecalculateNormals();
    }

    void ResetMembrane()
    {
        System.Array.Copy(_baseVertices, _dynamicVertices, _baseVertices.Length);
        for (int i = 0; i < _vertexColors.Length; i++)
        {
            _vertexColors[i] = heatGradient.Evaluate(0f);
            _vertexColors[i].a = 0f; // 完全透明
        }
        _membraneMesh.vertices = _dynamicVertices;
        _membraneMesh.colors = _vertexColors;
        _membraneMesh.RecalculateNormals();
    }
}