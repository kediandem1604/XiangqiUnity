using UnityEngine;

/// <summary>
/// Dùng 4 con Xe (Rook) làm 4 góc: Bottom-Left, Bottom-Right, Top-Left, Top-Right (không cần đúng thứ tự; script sẽ tự nhận diện).
/// Tạo lưới theo bilinear, vẽ Gizmos các đường & giao điểm, và cung cấp API:
///   - GetWorldPoint(file, rank)
///   - TryWorldToGrid(worldPoint, out file, out rank)
/// Mặc định cho Xiangqi: files=9, ranks=10 (giao điểm lưới).
/// </summary>
[ExecuteAlways]
public class BoardFromRooks : MonoBehaviour
{
    [Header("Assign 4 rooks (Transforms)")]
    public Transform[] rooks = new Transform[4]; // thả 4 con xe vào đây
    [Tooltip("Khoá 4 góc ngay khi vào Play; về sau xe di chuyển sẽ không làm lưới thay đổi")]
    public bool lockCornersOnStart = true;

    [Header("Grid (Xiangqi = 9 x 10)")]
    public int files = 9;   // cột (theo chiều ngang)
    public int ranks = 10;  // hàng (theo chiều dọc)
    
    [Header("Gizmos")]
    public bool drawGrid = true;
    public Color lineColor = new Color(0f, 0.9f, 0.4f, 1f);
    public Color dotColor  = new Color(0.1f, 1f, 0.6f, 1f);
    public float dotRadius = 0.02f;
    public float surfaceOffset = 0.003f; // đẩy lưới nổi lên khỏi mặt bàn để tránh z-fighting
    [HideInInspector] public bool useEditorAA = true;      // dùng Handles.DrawAAPolyLine trong Editor
    [HideInInspector] public bool debugLogs = false;       // in log & label hỗ trợ debug góc
		private bool _initStarted;
		private bool _dotsReady; // Thêm flag để track dots đã sẵn sàng

	[Header("Dots (runtime)")]
	public Material dotMaterial; // material của cylinder dot
	[Range(0.02f, 5.5f)]
	public float dotScale = 2.15f; // đường kính XZ (m)
	[Range(0.01f, 0.1f)]
	public float dotHeight = 0.05f; // chiều cao (Y) của cylinder
	public string dotsRenderLayer = "Default"; // layer render cho dots

		private Transform _dotsRoot;
		private Transform[,] _dots; // [file, rank]
		private Vector3[,] _gridWorld; // cache world positions [file, rank]

    // 4 góc sau khi sắp xếp: BL, BR, TL, TR
    private Vector3 _bl, _br, _tl, _tr;
    private bool _cornersLocked;

		void OnEnable()
		{
            EnsureCorners();
            if (Application.isPlaying)
			{
				if (!_initStarted)
				{
					_initStarted = true;
					StartCoroutine(DelayedInitRuntime());
				}
			}
		}

		void Start()
		{
			// Một số phiên bản/thiết lập Editor gọi OnEnable trước khi isPlaying = true
			// Start luôn chạy khi đã vào Play → đảm bảo init
			if (Application.isPlaying && !_initStarted)
			{
				_initStarted = true;
				StartCoroutine(DelayedInitRuntime());
			}
		}

		System.Collections.IEnumerator DelayedInitRuntime()
		{
			// Đợi 1 frame để renderer.bounds/transform ổn định
			yield return null;
            EnsureCorners();
			BuildGridCache();
			EnsureDots();
			UpdateDotsPositions();
			ClearDots();
		}

		void OnValidate()
		{
            // Không tạo dot trong Editor để tránh cảnh báo
            if (!_cornersLocked) EnsureCorners();
            BuildGridCache();
            UpdateDotsPositions();
		}

        void Update() { }

    // Lấy điểm thế giới tại (file, rank) - DÙNG VỊ TRÍ DOT ĐÃ CACHE
    // Dùng để di chuyển quân cờ - CHỈ LẤY X, Z từ dot (không lấy Y offset)
    public Vector3 GetWorldPoint(int file, int rank)
    {
        ClampGrid(ref file, ref rank);
        
        // ƯU TIÊN: Lấy từ vị trí dot đã cache (đã đặt chính xác trên bàn)
        if (_gridWorld != null && _gridWorld.GetLength(0) == files && _gridWorld.GetLength(1) == ranks)
        {
            Vector3 dotPos = _gridWorld[file, rank];
            // Trả X, Z của dot (không lấy Y offset)
            Vector3 result = new Vector3(dotPos.x, 0f, dotPos.z);
            
            if (debugLogs)
            {
                Debug.Log($"[GetWorldPoint] Grid ({file},{rank}) → DotWorld ({dotPos.x:F3}, {dotPos.y:F3}, {dotPos.z:F3}) → Result ({result.x:F3}, {result.y:F3}, {result.z:F3})");
            }
            
            return result;
        }
        
        // Fallback: tính từ bilinear (chỉ dùng khi chưa có cache)
        float u = files == 1 ? 0f : (float)file / (files - 1);
        float v = ranks == 1 ? 0f : (float)rank / (ranks - 1);
        Vector3 pos = Bilinear(u, v);
        Vector3 result2 = new Vector3(pos.x, 0f, pos.z);
        
        if (debugLogs)
        {
            Debug.Log($"[GetWorldPoint] Fallback: Grid ({file},{rank}) → World ({result2.x:F3}, {result2.y:F3}, {result2.z:F3})");
        }
        
        return result2;
    }

    // Chuyển từ world → (file, rank) gần nhất. Trả về true nếu nằm trong tứ giác.
    public bool TryWorldToGrid(Vector3 worldPoint, out int file, out int rank)
    {
        file = rank = 0;
        if (!EnsureCorners()) return false;

        // Chia tứ giác thành 2 tam giác: T1: BL-BR-TR (uv (0,0)-(1,0)-(1,1))
        //                              T2: BL-TR-TL (uv (0,0)-(1,1)-(0,1))
        Vector3 a1 = _bl, b1 = _br, c1 = _tr;
        if (PointInTri(worldPoint, a1, b1, c1, out _, out var w2, out var w3))
        {
            // uv = w2*(1,0) + w3*(1,1)  => (u, v) = (w2 + w3, w3)
            float u = w2 + w3;
            float v = w3;
            file = Mathf.RoundToInt(u * (files - 1));
            rank = Mathf.RoundToInt(v * (ranks - 1));
            ClampGrid(ref file, ref rank);
            if (debugLogs)
            {
                Debug.Log($"[TryWorldToGrid] Hit tri1 BL-BR-TR → (u,v)=({u:F3},{v:F3}) → cell=({file},{rank}) world=({worldPoint.x:F3},{worldPoint.y:F3},{worldPoint.z:F3})");
            }
            return true;
        }

        // Tam giác thứ 2
        Vector3 a2 = _bl, b2 = _tr, c2 = _tl;
        if (PointInTri(worldPoint, a2, b2, c2, out _, out w2, out w3))
        {
            // uv = w2*(1,1) + w3*(0,1) => (u, v) = (w2, w2 + w3)
            float u = w2;
            float v = w2 + w3;
            file = Mathf.RoundToInt(u * (files - 1));
            rank = Mathf.RoundToInt(v * (ranks - 1));
            ClampGrid(ref file, ref rank);
            if (debugLogs)
            {
                Debug.Log($"[TryWorldToGrid] Hit tri2 BL-TR-TL → (u,v)=({u:F3},{v:F3}) → cell=({file},{rank}) world=({worldPoint.x:F3},{worldPoint.y:F3},{worldPoint.z:F3})");
            }
            return true;
        }

        return false;
    }

    private void ClampGrid(ref int f, ref int r)
    {
        f = Mathf.Clamp(f, 0, Mathf.Max(0, files - 1));
        r = Mathf.Clamp(r, 0, Mathf.Max(0, ranks - 1));
    }

    private bool EnsureCorners()
    {
        if (_cornersLocked) return true; // dùng góc đã khoá
        // Kiểm tra đủ 4 transform
        if (rooks == null || rooks.Length != 4) return false;
        for (int i = 0; i < 4; i++)
            if (rooks[i] == null) return false;

        // Dùng tọa độ GLOBAL (world XZ) của renderer.bounds.center để phân loại 4 góc
        Vector3[] rookWorld = new Vector3[4];
        Vector2[] lp = new Vector2[4];
        for (int i = 0; i < 4; i++)
        {
            Vector3 p;
            var rend = rooks[i].GetComponent<Renderer>();
            if (rend == null) rend = rooks[i].GetComponentInChildren<Renderer>();
            if (rend != null)
                p = rend.bounds.center; // world center theo mesh thực
            else
                p = rooks[i].position;   // fallback nếu không có renderer

            rookWorld[i] = p;
            lp[i] = new Vector2(p.x, p.z); // world X,Z
        }

        // Tách nhóm bottom/top theo trục Z local
        float minZ = Mathf.Min(lp[0].y, lp[1].y, lp[2].y, lp[3].y);
        float maxZ = Mathf.Max(lp[0].y, lp[1].y, lp[2].y, lp[3].y);
        float midZ = (minZ + maxZ) * 0.5f;

        int bottomL = -1, bottomR = -1, topL = -1, topR = -1;
        float bMinX = float.PositiveInfinity, bMaxX = float.NegativeInfinity;
        float tMinX = float.PositiveInfinity, tMaxX = float.NegativeInfinity;

        for (int i = 0; i < 4; i++)
        {
            bool isBottom = lp[i].y <= midZ;
            if (isBottom)
            {
                if (lp[i].x < bMinX) { bMinX = lp[i].x; bottomL = i; }
                if (lp[i].x > bMaxX) { bMaxX = lp[i].x; bottomR = i; }
            }
            else
            {
                if (lp[i].x < tMinX) { tMinX = lp[i].x; topL = i; }
                if (lp[i].x > tMaxX) { tMaxX = lp[i].x; topR = i; }
            }
        }

        // Phòng khi phân nhóm nhầm do lệch nhẹ, fallback: sắp theo Z rồi X
        if (bottomL < 0 || bottomR < 0 || topL < 0 || topR < 0)
        {
            int[] idx = { 0, 1, 2, 3 };
            System.Array.Sort(idx, (a, b) =>
            {
                int cz = lp[a].y.CompareTo(lp[b].y);
                if (cz != 0) return cz;
                return lp[a].x.CompareTo(lp[b].x);
            });
            bottomL = idx[0]; bottomR = idx[1]; topL = idx[2]; topR = idx[3];
        }

        // Gán lại theo WORLD để vẽ đúng trong Gizmos
        _bl = rookWorld[bottomL];
        _br = rookWorld[bottomR];
        _tl = rookWorld[topL];
        _tr = rookWorld[topR];

        // Chỉ log khi debugLogs = true và đang ở Play mode hoặc khi thật sự cần debug
        if (debugLogs && Application.isPlaying)
        {
            Debug.Log($"[BoardFromRooks] Rooks world XZ (renderer centers): 0=({rookWorld[0].x:F3},{rookWorld[0].z:F3}) 1=({rookWorld[1].x:F3},{rookWorld[1].z:F3}) 2=({rookWorld[2].x:F3},{rookWorld[2].z:F3}) 3=({rookWorld[3].x:F3},{rookWorld[3].z:F3})");
            Debug.Log($"[BoardFromRooks] Corners: BL={_bl} BR={_br} TL={_tl} TR={_tr}");
        }

        // Nếu yêu cầu khoá ở Start: khi lần đầu init trong runtime, khoá lại
        if (Application.isPlaying && lockCornersOnStart)
            _cornersLocked = true;
        return true;
    }

    // Bilinear interpolation trên tứ giác BL,BR,TL,TR
    private Vector3 Bilinear(float u, float v)
    {
        EnsureCorners();
        // P(u,v) = (1-u)(1-v)BL + u(1-v)BR + (1-u)v TL + uv TR
        return (1 - u) * (1 - v) * _bl
             + u * (1 - v) * _br
             + (1 - u) * v * _tl
             + u * v * _tr;
    }

    private Vector3 BoardNormal()
    {
        // Dùng tam giác BL-BR-TL để tính normal
        Vector3 n = Vector3.Cross(_br - _bl, _tl - _bl).normalized;
        if (n == Vector3.zero) n = Vector3.up;
        return n;
    }

    private Vector3 P(float u, float v)
    {
			// Đặt tâm cylinder cao hơn bề mặt một nửa dotHeight để không bị chìm
			return Bilinear(u, v) + BoardNormal() * (surfaceOffset + dotHeight * 0.5f);
    }

    // Vị trí dùng cho vẽ lưới Gizmos (không phụ thuộc dotHeight)
    private Vector3 PGrid(float u, float v)
    {
        return Bilinear(u, v) + BoardNormal() * surfaceOffset;
    }

		private void BuildGridCache()
		{
			if (!EnsureCorners()) return;
			if (_gridWorld == null || _gridWorld.GetLength(0) != files || _gridWorld.GetLength(1) != ranks)
			{
				_gridWorld = new Vector3[files, ranks];
			}
			for (int r = 0; r < ranks; r++)
			{
				float v = ranks == 1 ? 0f : (float)r / (ranks - 1);
				for (int f = 0; f < files; f++)
				{
					float u = files == 1 ? 0f : (float)f / (files - 1);
					Vector3 bilinearPos = Bilinear(u, v);
					// CHỈ cache X, Z (không cache Y để tránh offset làm quân đi xuống)
					_gridWorld[f, r] = new Vector3(bilinearPos.x, 0f, bilinearPos.z);
				}
			}
		}

		public Vector3 GetGridWorldPoint(int file, int rank)
		{
			ClampGrid(ref file, ref rank);
			if (_gridWorld == null || _gridWorld.GetLength(0) != files || _gridWorld.GetLength(1) != ranks)
				BuildGridCache();
			return _gridWorld[file, rank];
		}

		public Vector3 GetLocalPoint(int file, int rank)
		{
			// Trả về toạ độ LOCAL của bàn cờ cho ô (file,rank)
			Vector3 world = GetWorldPoint(file, rank);
			return transform.InverseTransformPoint(world);
		}

    // Kiểm tra điểm có trong tam giác 3D (dùng barycentric). Trả weights (w1,w2,w3).
    private bool PointInTri(Vector3 p, Vector3 a, Vector3 b, Vector3 c,
                            out float w1, out float w2, out float w3)
    {
        // Dựa theo kỹ thuật barycentric trong không gian 3D phẳng
        Vector3 v0 = b - a;
        Vector3 v1 = c - a;
        Vector3 v2 = p - a;

        float d00 = Vector3.Dot(v0, v0);
        float d01 = Vector3.Dot(v0, v1);
        float d11 = Vector3.Dot(v1, v1);
        float d20 = Vector3.Dot(v2, v0);
        float d21 = Vector3.Dot(v2, v1);
        float denom = d00 * d11 - d01 * d01;

        // Trường hợp suy biến (điểm thẳng hàng…)
        if (Mathf.Abs(denom) < 1e-8f)
        {
            w1 = w2 = w3 = 0f;
            return false;
        }

        float v = (d11 * d20 - d01 * d21) / denom;
        float w = (d00 * d21 - d01 * d20) / denom;
        float u = 1.0f - v - w;

        w1 = u; w2 = v; w3 = w;

        // Cho phép chút sai số do số học dấu chấm động
        const float eps = 1e-3f;
        return (u >= -eps && v >= -eps && w >= -eps && u <= 1 + eps && v <= 1 + eps && w <= 1 + eps);
    }

    private void OnDrawGizmos()
    {
        if (!drawGrid) return;
        if (!EnsureCorners()) return;

			// Đồng bộ vị trí gizmos; dots chỉ update khi Play

        // Vẽ lưới (không phụ thuộc dotHeight)
#if UNITY_EDITOR
        if (useEditorAA)
        {
            UnityEditor.Handles.color = lineColor;
            for (int f = 0; f < files; f++)
            {
                float u = files == 1 ? 0f : (float)f / (files - 1);
                UnityEditor.Handles.DrawAAPolyLine(2f, PGrid(u, 0f), PGrid(u, 1f));
            }
            for (int r = 0; r < ranks; r++)
            {
                float v = ranks == 1 ? 0f : (float)r / (ranks - 1);
                UnityEditor.Handles.DrawAAPolyLine(2f, PGrid(0f, v), PGrid(1f, v));
            }
        }
        else
#endif
        {
            Gizmos.color = lineColor;
            for (int f = 0; f < files; f++)
            {
                float u = files == 1 ? 0f : (float)f / (files - 1);
                Gizmos.DrawLine(PGrid(u, 0f), PGrid(u, 1f));
            }
            for (int r = 0; r < ranks; r++)
            {
                float v = ranks == 1 ? 0f : (float)r / (ranks - 1);
                Gizmos.DrawLine(PGrid(0f, v), PGrid(1f, v));
            }
        }

        // Vẽ chấm tại giao điểm (để dễ debug)
        Gizmos.color = dotColor;
        for (int r = 0; r < ranks; r++)
        {
            float v = ranks == 1 ? 0f : (float)r / (ranks - 1);
            for (int f = 0; f < files; f++)
            {
                float u = files == 1 ? 0f : (float)f / (files - 1);
                Vector3 p = PGrid(u, v);
#if UNITY_EDITOR
                UnityEditor.Handles.color = dotColor;
                UnityEditor.Handles.SphereHandleCap(0, p, Quaternion.identity, dotRadius, EventType.Repaint);
#else
                Gizmos.DrawSphere(p, dotRadius);
#endif
            }
        }

        // Vẽ khung 4 góc
        Gizmos.color = Color.yellow;
        Vector3 nrm = BoardNormal() * surfaceOffset;
        Gizmos.DrawLine(_bl + nrm, _br + nrm);
        Gizmos.DrawLine(_br + nrm, _tr + nrm);
        Gizmos.DrawLine(_tr + nrm, _tl + nrm);
        Gizmos.DrawLine(_tl + nrm, _bl + nrm);

#if UNITY_EDITOR
        if (debugLogs)
        {
            UnityEditor.Handles.color = Color.white;
            UnityEditor.Handles.Label(_bl + nrm, "BL");
            UnityEditor.Handles.Label(_br + nrm, "BR");
            UnityEditor.Handles.Label(_tl + nrm, "TL");
            UnityEditor.Handles.Label(_tr + nrm, "TR");
        }
#endif
    }

		// ========== Dots (90 giao điểm) ==========
		public void EnsureDots()
		{
			if (!Application.isPlaying) return;
			if (_dotsRoot == null)
			{
				_dotsRoot = new GameObject("Dots").transform;
				_dotsRoot.SetParent(transform, false);
			}
			if (_dots == null || _dots.GetLength(0) != files || _dots.GetLength(1) != ranks)
			{
				for (int i = _dotsRoot.childCount - 1; i >= 0; i--) Destroy(_dotsRoot.GetChild(i).gameObject);
				_dots = new Transform[files, ranks];
				for (int r = 0; r < ranks; r++)
				{
					for (int f = 0; f < files; f++)
					{
					var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
					go.name = $"Dot_{f}_{r}";
					int layer = LayerMask.NameToLayer(dotsRenderLayer);
					go.layer = layer >= 0 ? layer : gameObject.layer;
					var tr = go.transform;
					tr.SetParent(_dotsRoot, false);
					tr.localScale = new Vector3(1f, 0.05f, 1f); // X=Z=1, Y=0.05 để cylinder dẹt
					var mr = go.GetComponent<MeshRenderer>();
					if (mr != null && dotMaterial != null) mr.sharedMaterial = dotMaterial;
					
					// Gắn DotController để lưu tọa độ và BẬT collider để có thể click
					var dotCtrl = go.AddComponent<DotController>();
					dotCtrl.SetPosition(f, r);
					var col = go.GetComponent<Collider>();
					if (col != null)
					{
						col.enabled = true; // BẬT collider để có thể raycast click vào
						col.isTrigger = true; // Đặt trigger để không làm vật lý ảnh hưởng
					}
					
					go.SetActive(false); // tạo ở trạng thái ẩn để tránh cảnh báo OnValidate/Awake
					_dots[f, r] = tr;
					}
				}
			}
		}

		// BindExistingDots/ApplyDotScale đã loại bỏ (không cần khi tự tạo runtime)

		public void UpdateDotsPositions()
		{
			if (_dots == null) return;
			if (!EnsureCorners()) return;
			for (int r = 0; r < ranks; r++)
			{
				float v = ranks == 1 ? 0f : (float)r / (ranks - 1);
				for (int f = 0; f < files; f++)
				{
					float u = files == 1 ? 0f : (float)f / (files - 1);
						var tr = _dots[f, r];
						if (tr == null) continue;
						// đặt theo LOCAL của bàn cờ để trùng hệ quy chiếu Inspector
						Vector3 worldP = (_gridWorld != null && _gridWorld.GetLength(0) == files && _gridWorld.GetLength(1) == ranks) ? _gridWorld[f, r] : P(u, v);
						tr.localPosition = transform.InverseTransformPoint(worldP);
				}
			}
		}

		public void ClearDots()
		{
			if (_dots == null) return;
			for (int r = 0; r < ranks; r++)
				for (int f = 0; f < files; f++)
					if (_dots[f, r] != null) _dots[f, r].gameObject.SetActive(false);
		}

        // ShowAllDots đã loại bỏ để tránh lộ nút thử nghiệm trên Inspector

		public void ShowMoves(System.Collections.Generic.IEnumerable<(int file, int rank)> moves)
		{
			EnsureDots();
			UpdateDotsPositions();
			ClearDots();
			if (moves == null) return;
			foreach (var m in moves)
			{
				int f = Mathf.Clamp(m.file, 0, files - 1);
				int r = Mathf.Clamp(m.rank, 0, ranks - 1);
				var tr = _dots[f, r];
				if (tr != null) tr.gameObject.SetActive(true);
			}
			if (debugLogs)
			{
				int cnt = 0;
				foreach (var _ in moves) cnt++;
				Debug.Log($"[BoardFromRooks] ShowMoves count={cnt}");
			}
		}

// không còn menu editor tái tạo dots
}


