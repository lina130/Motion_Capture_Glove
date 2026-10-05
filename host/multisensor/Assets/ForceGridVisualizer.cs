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
    public float displayThreshold = 1.0f;
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
    private float _currentAlpha;
    private float _lastRadius;

    void Awake()
    {
        // 🔥 修复点 1：安全获取 MeshFilter 和 MeshRenderer，防止在已有骨骼上重复添加导致返回 null
        _mf = GetComponent<MeshFilter>();
        if (_mf == null) _mf = gameObject.AddComponent<MeshFilter>();

        _mr = GetComponent<MeshRenderer>();
        if (_mr == null) _mr = gameObject.AddComponent<MeshRenderer>();

        if (_mr == null)
        {
            Debug.LogError($"[{gameObject.name}] 无法添加 MeshRenderer！请确保此脚本没有挂载到已有 SkinnedMeshRenderer 的骨骼上。");
            return;
        }

        // 🔥 修复点 2：增加 URP/HDRP 兼容的 Shader 降级寻找策略
        Shader transShader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
        if (transShader == null) transShader = Shader.Find("Particles/Standard Unlit");
        if (transShader == null) transShader = Shader.Find("Sprites/Default"); // 究极兜底

        if (transShader != null)
        {
            Material mat = new Material(transShader);
            mat.renderQueue = 3000;
            mat.SetInt("_ZWrite", 0);
            _mr.material = mat;
            _mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _mr.receiveShadows = false;
        }
        else
        {
            Debug.LogWarning("未找到合适的透明 Shader，压力网格可能显示异常。");
        }

        InitializeHeatGradient();
        GenerateSquircleDome();
        _lastRadius = radius;
        _currentAlpha = 0f;
    }

    void InitializeHeatGradient()
    {
        // 🔥 修复点 3：增加 colorKeys 非空判断，防止意外的序列化损坏
        if (heatGradient != null && heatGradient.colorKeys != null && heatGradient.colorKeys.Length > 0) return;

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
        if (_mf != null) _mf.mesh = _membraneMesh;

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
                _vertexColors[i].a = 0f;

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
        // 🔥 修复点 4：防止组件初始化失败时，Update 里的数组为空继续报错
        if (_membraneMesh == null || _baseVertices == null) return;

        UpdateRadius();

        _smoothedForce = Vector3.Lerp(_smoothedForce, _targetForce, Time.deltaTime * smoothSpeed);
        float magnitude = _smoothedForce.magnitude;

        float targetAlpha = magnitude > displayThreshold ? 1f : 0f;
        _currentAlpha = Mathf.Lerp(_currentAlpha, targetAlpha, Time.deltaTime * fadeSpeed);

        if (_currentAlpha < 0.001f)
        {
            ResetMembrane();
            return;
        }

        Vector3 localForceDir = transform.InverseTransformDirection(_smoothedForce.normalized);
        float forceStrength = Mathf.Clamp01((magnitude - displayThreshold) / (maxForceThreshold - displayThreshold));

        for (int i = 0; i < _baseVertices.Length; i++)
        {
            float sqrDist = (_baseVertices[i].x * _baseVertices[i].x) + (_baseVertices[i].z * _baseVertices[i].z);
            float influence = Mathf.Exp(-sqrDist / forceSpread);

            _dynamicVertices[i] = _baseVertices[i] + localForceDir * (forceStrength * influence * maxDeformDepth);

            float localStress = forceStrength * influence;
            _vertexColors[i] = heatGradient.Evaluate(Mathf.Clamp01(localStress));

            float normalizedDist = Mathf.Clamp01(Mathf.Sqrt(sqrDist) / radius);
            _vertexColors[i].a = (1f - normalizedDist) * _currentAlpha;
        }

        _membraneMesh.vertices = _dynamicVertices;
        _membraneMesh.colors = _vertexColors;
        _membraneMesh.RecalculateNormals();
    }

    void ResetMembrane()
    {
        if (_membraneMesh == null || _baseVertices == null) return;

        System.Array.Copy(_baseVertices, _dynamicVertices, _baseVertices.Length);
        for (int i = 0; i < _vertexColors.Length; i++)
        {
            _vertexColors[i] = heatGradient.Evaluate(0f);
            _vertexColors[i].a = 0f;
        }
        _membraneMesh.vertices = _dynamicVertices;
        _membraneMesh.colors = _vertexColors;
        _membraneMesh.RecalculateNormals();
    }
}