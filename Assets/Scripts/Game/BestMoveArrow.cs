using UnityEngine;

/// <summary>
/// Vẽ mũi tên bestmove trên bàn cờ bằng LineRenderer + cone nhỏ ở đầu.
/// Vẽ mũi tên bestmove trên bàn cờ bằng một LineRenderer 3D duy nhất.
/// </summary>
[RequireComponent(typeof(BoardFromRooks))]
public class BestMoveArrow : MonoBehaviour
{
    public Material lineMaterial;
    public Color color = new Color(0.2f, 1f, 0.2f, 0.9f);
    public float width = 0.50f; // thân mũi tên to hơn mặc định
    public float yLift = 0.06f;
    public Gradient colorGradient = new Gradient();
    
    [Tooltip("Billboard theo camera (View) giúp line luôn nhìn thấy rõ")]
    public bool billboardViewAlignment = true;
    [Range(0, 16)] public int cornerVertices = 8;
    [Range(0, 16)] public int capVertices = 8;
    public LineTextureMode textureMode = LineTextureMode.Stretch;
    
    [Tooltip("Đường cong độ dày: 0→đầu từ, 1→đầu đến. Mặc định dùng độ dày cố định.")]
    public AnimationCurve widthCurve = AnimationCurve.Constant(0f, 1f, 1f);
    [Tooltip("Dùng độ dày cố định toàn tuyến (bỏ thu nhọn đầu)")]
    public bool constantWidth = true;

    private LineRenderer _lr;
    private GameObject _headGo;
    private Mesh _headMesh;
    
    [Header("Arrow Head (Cone)")]
    public float headRadius = 0.80f;
    public float headLength = 1.20f;
    [Range(6, 64)] public int headSegments = 32;

    void Ensure()
    {
        if (lineMaterial == null)
        {
            // 1) Ưu tiên dùng sẵn material người dùng tạo: Assets/Materials/Line_Arrow.mat
#if UNITY_EDITOR
            var mat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Line_Arrow.mat");
            if (mat != null) lineMaterial = mat;
#endif
            // 2) Thử Resources (nếu người dùng đặt trong Resources)
            if (lineMaterial == null)
            {
                lineMaterial = Resources.Load<Material>("Line_Arrow");
                if (lineMaterial == null) lineMaterial = Resources.Load<Material>("Materials/Line_Arrow");
            }
            // 3) Fallback: tự tạo Unlit material
            if (lineMaterial == null)
            {
                var sh = Shader.Find("Unlit/Color");
                if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
                if (sh != null)
                {
                    lineMaterial = new Material(sh);
                    // Set màu vật liệu theo màu đầu của gradient
                    var c = (colorGradient != null) ? colorGradient.Evaluate(0f) : new Color(0.2f, 1f, 0.2f, 1f);
                    if (lineMaterial.HasProperty("_Color")) lineMaterial.color = c;
                    lineMaterial.renderQueue = 3000;
                    // Double-sided nếu shader hỗ trợ để đầu nón không bị mất mặt
                    if (lineMaterial.HasProperty("_CullMode")) lineMaterial.SetFloat("_CullMode", 0f);
                    else if (lineMaterial.HasProperty("_Cull")) lineMaterial.SetFloat("_Cull", 0f);
                }
            }
        }
        if (_lr == null)
        {
            var go = new GameObject("BestMoveArrow_Line");
            go.transform.SetParent(transform, false);
            _lr = go.AddComponent<LineRenderer>();
            _lr.positionCount = 2;
            _lr.material = lineMaterial;
            _lr.widthMultiplier = width;
            _lr.widthCurve = constantWidth ? AnimationCurve.Constant(0f, 1f, 1f) : widthCurve;
            _lr.useWorldSpace = true;
            _lr.numCornerVertices = cornerVertices;
            _lr.numCapVertices = capVertices;
            _lr.textureMode = textureMode;
            _lr.alignment = billboardViewAlignment ? LineAlignment.View : LineAlignment.TransformZ;
            _lr.colorGradient = colorGradient;
        }
        if (_headGo == null)
        {
            _headGo = new GameObject("BestMoveArrow_Head");
            _headGo.transform.SetParent(transform, false);
            var mf = _headGo.AddComponent<MeshFilter>();
            var mr = _headGo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = lineMaterial;
            _headMesh = BuildConeMesh(headRadius, headLength, headSegments); // radius, height, segments
            mf.sharedMesh = _headMesh;
            _headGo.SetActive(false);
        }
        // Đảm bảo gradient có key mặc định nếu người dùng chưa gán
        if (colorGradient != null && (colorGradient.colorKeys == null || colorGradient.colorKeys.Length == 0))
        {
            colorGradient.colorKeys = new[]
            {
                new GradientColorKey(new Color(0.2f, 1f, 0.2f), 0f),
                new GradientColorKey(new Color(0.2f, 1f, 0.2f), 1f)
            };
            colorGradient.alphaKeys = new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 1f)
            };
        }
    }

    public void Clear()
    {
        if (_lr != null) _lr.enabled = false;
        if (_headGo != null) _headGo.SetActive(false);
    }

    public void ShowArrow(int fromFile, int fromRank, int toFile, int toRank, BoardFromRooks grid)
    {
        Ensure();
        if (grid == null) grid = GetComponent<BoardFromRooks>();
        if (grid == null) return;

        Vector3 a = grid.GetWorldPoint(fromFile, fromRank);
        Vector3 b = grid.GetWorldPoint(toFile, toRank);
        a.y += yLift; b.y += yLift;

        if (_lr == null)
        {
            Debug.LogWarning("[BestMoveArrow] LineRenderer is null after Ensure()");
            return;
        }

        _lr.enabled = true;
        _lr.material = lineMaterial; // đảm bảo gán lần nữa
        _lr.widthMultiplier = width;
        _lr.widthCurve = constantWidth ? AnimationCurve.Constant(0f, 1f, 1f) : widthCurve;
        _lr.numCornerVertices = cornerVertices;
        _lr.numCapVertices = capVertices;
        _lr.textureMode = LineTextureMode.Stretch;
        _lr.alignment = billboardViewAlignment ? LineAlignment.View : LineAlignment.TransformZ;
        _lr.colorGradient = colorGradient;

        // Direction và cắt bớt phần thân để dành chỗ cho đầu mũi tên rõ ràng
        Vector3 dir = (b - a);
        float len = dir.magnitude;
        if (len < 1e-4f)
        {
            _lr.enabled = false; if (_headGo) _headGo.SetActive(false); return;
        }
        dir /= len;
        
        // Cắt đúng bằng chiều dài đầu mũi tên để đáy nón khớp thân
        float headLen = headLength;
        float cut = Mathf.Min(headLen, len * 0.8f);
        Vector3 bShaft = b - dir * headLen;

        _lr.positionCount = 2;
        _lr.SetPosition(0, a);
        _lr.SetPosition(1, bShaft);

        // Đầu mũi tên (cone): tip ở b, hướng theo dir
        if (_headGo != null)
        {
            _headGo.SetActive(true);
            // Đặt sao cho tip trùng b: với mesh tip ở +Z, base ở 0 → tịnh tiến lùi theo -dir*headLength
            _headGo.transform.position = b - dir * headLength;
            if (dir != Vector3.zero)
                _headGo.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            _headGo.transform.localScale = new Vector3(headRadius, headRadius, headLength);
        }

        Debug.Log($"[BestMoveArrow] Arrow {fromFile},{fromRank} -> {toFile},{toRank} | a={a} b={b} mat={(lineMaterial?lineMaterial.name:"null")}");
    }

    // Tạo mesh hình nón: đáy tại z=0, tip tại z=height (trục +Z)
    Mesh BuildConeMesh(float radius, float height, int segments)
    {
        segments = Mathf.Max(3, segments);
        var mesh = new Mesh();
        int vertCount = segments + 2; // base center + ring + tip
        Vector3[] v = new Vector3[vertCount];
        
        // base center at z=0
        v[0] = new Vector3(0f, 0f, 0f);
        float angStep = Mathf.PI * 2f / segments;
        for (int i = 0; i < segments; i++)
        {
            float ang = i * angStep;
            float x = Mathf.Cos(ang) * radius;
            float y = Mathf.Sin(ang) * radius;
            v[1 + i] = new Vector3(x, y, 0f); // ring on base plane
        }
        
        // tip at z=height
        v[vertCount - 1] = new Vector3(0f, 0f, height);
        
        mesh.vertices = v;
        
        // side triangles
        int sideTriCount = segments * 3;
        int baseTriCount = segments * 3;
        int[] tris = new int[sideTriCount + baseTriCount];
        int t = 0;
        
        for (int i = 0; i < segments; i++)
        {
            int i0 = 1 + i;
            int i1 = 1 + ((i + 1) % segments);
            // side (i0, i1, tip)
            tris[t++] = i0; tris[t++] = i1; tris[t++] = vertCount - 1;
        }
        
        for (int i = 0; i < segments; i++)
        {
            int i0 = 1 + i;
            int i1 = 1 + ((i + 1) % segments);
            // base (center, i1, i0) clockwise to face outward +Z
            tris[t++] = 0; tris[t++] = i1; tris[t++] = i0;
        }
        
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        return mesh;
    }
}
