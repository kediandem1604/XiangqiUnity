using UnityEngine;

/// <summary>
/// Vẽ mũi tên bestmove trên bàn cờ bằng LineRenderer + cone nhỏ ở đầu.
/// </summary>
[RequireComponent(typeof(BoardFromRooks))]
public class BestMoveArrow : MonoBehaviour
{
    public Material lineMaterial;
    public Color color = new Color(0.2f, 1f, 0.2f, 0.9f);
    public float width = 0.03f;
    public float yLift = 0.02f;

    private LineRenderer _lr;
    private Transform _head;

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
                    lineMaterial.color = color;
                    lineMaterial.renderQueue = 3000;
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
            _lr.startWidth = _lr.endWidth = width;
            _lr.startColor = _lr.endColor = color;
            _lr.useWorldSpace = true;
        }
        if (_head == null)
        {
            // Unity không có PrimitiveType.Cone, dùng Cube làm đầu mũi tên tối giản
            var h = GameObject.CreatePrimitive(PrimitiveType.Cube);
            h.name = "BestMoveArrow_Head";
            h.transform.SetParent(transform, false);
            Object.Destroy(h.GetComponent<Collider>());
            var mr = h.GetComponent<MeshRenderer>();
            if (mr != null && lineMaterial != null) mr.sharedMaterial = lineMaterial;
            _head = h.transform;
            _head.localScale = new Vector3(width * 6f, width * 6f, width * 12f); // khối chữ nhật mảnh
        }
    }

    public void Clear()
    {
        if (_lr != null) _lr.enabled = false;
        if (_head != null) _head.gameObject.SetActive(false);
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
        _lr.startWidth = _lr.endWidth = width;
        _lr.positionCount = 2;
        _lr.SetPosition(0, a);
        _lr.SetPosition(1, b);

        // đặt đầu mũi tên
        _head.gameObject.SetActive(true);
        _head.position = b;
        Vector3 dir = (b - a).normalized;
        if (dir != Vector3.zero)
            _head.rotation = Quaternion.LookRotation(dir, Vector3.up);

        Debug.Log($"[BestMoveArrow] Arrow {fromFile},{fromRank} -> {toFile},{toRank} | a={a} b={b} mat={(lineMaterial?lineMaterial.name:"null")}");
    }
}


