using UnityEngine;
using DG.Tweening;

/// <summary>
/// Quản lý di chuyển quân cờ với animation dùng DOTween
/// </summary>
public class PieceMover : MonoBehaviour
{
    private static PieceMover _instance;
    public static PieceMover Instance
    {
        get
        {
            if (_instance == null)
            {
                GameObject go = new GameObject("PieceMover");
                _instance = go.AddComponent<PieceMover>();
            }
            return _instance;
        }
    }

    /// <summary>
    /// Animate quân cờ bay tới đích (giữ nguyên Y hiện tại)
    /// </summary>
    void AnimatePieceMovement(PieceController piece, Vector3 targetPos)
    {
        if (piece == null) return;
        
        Vector3 startPos = piece.transform.position;
        float keepY = piece.baseY; // Khóa theo Y gốc của quân
        Vector3 targetFinal = new Vector3(targetPos.x, keepY, targetPos.z);
        
        // KILL tween cũ để tránh xung đột
        piece.transform.DOKill();
        // Chuẩn hóa Y tại điểm bắt đầu để tránh drift tích lũy
        piece.transform.position = new Vector3(startPos.x, keepY, startPos.z);
        
        // Tính độ cao nhảy tỉ lệ theo quãng đường để luôn thấy cung cong rõ ràng
        float horizontalDist = Vector3.Distance(new Vector3(startPos.x, 0f, startPos.z), new Vector3(targetFinal.x, 0f, targetFinal.z));
        float effectiveJumpHeight = Mathf.Max(minJumpHeight, horizontalDist * jumpHeightFactor);
        
        // Parabola: nhảy theo cung, bay lên rồi hạ xuống giữ đúng Y đích
        piece.transform.DOJump(targetFinal, effectiveJumpHeight, jumpCount, moveDuration)
            .SetEase(easeType)
            .OnComplete(() =>
            {
                piece.transform.position = targetFinal;
                piece.UpdatePosition();
            })
            .OnKill(() =>
            {
                // Đảm bảo cuối cùng Y chính xác tuyệt đối
                Vector3 p = piece.transform.position;
                piece.transform.position = new Vector3(targetFinal.x, keepY, targetFinal.z);
            });
    }

    /// <summary>
    /// Fallback: Convert (file,rank) → world nếu thiếu BoardFromRooks
    /// </summary>
    Vector3 BoardToWorldPosition(int file, int rank, Transform boardTransform)
    {
        float x = file - 4f; // Center x=0
        float z = rank - 4.5f; // Center z=0
        Vector3 localPos = new Vector3(x, 0f, z);
        return boardTransform.TransformPoint(localPos);
    }

    void Awake()
    {
        _instance = this;
    }

    void OnDestroy()
    {
        DOTween.KillAll();
    }
    
    [Header("Animation Settings")]
    public float moveDuration = 0.5f;
    [Header("Jump Curve")]
    public float jumpHeight = 0.3f; // legacy – nếu không dùng factor
    public float jumpHeightFactor = 0.25f; // chiều cao = factor * quãng đường
    public float minJumpHeight = 0.15f;
    public int jumpCount = 1;
    public Ease easeType = Ease.InOutCubic;
    
    /// <summary>
    /// Di chuyển quân cờ từ vị trí này sang vị trí khác với animation
    /// </summary>
    public void MovePiece(PieceController piece, int targetFile, int targetRank, BoardController board)
    {
        if (piece == null) return;
        
        var boardFromRooks = board.GetComponent<BoardFromRooks>();
        if (boardFromRooks == null)
        {
            Debug.LogError("[PieceMover] BoardFromRooks not found!");
            return;
        }
        
        // LẤY TỌA ĐỘ TARGET TRỰC TIẾP TỪ GRID (không dựa vào vị trí cũ)
        Vector3 targetWorld = boardFromRooks.GetWorldPoint(targetFile, targetRank);
        
        // Giữ nguyên Y hiện tại của quân cờ
        float keepY = piece.transform.position.y;
        Vector3 targetFinal = new Vector3(targetWorld.x, keepY, targetWorld.z);
        
        // Hiệu chỉnh theo offset giữa pivot và renderer.center để renderer.center trùng điểm lưới
        var rend = piece.GetComponent<Renderer>();
        if (rend == null) rend = piece.GetComponentInChildren<Renderer>();
        if (rend != null)
        {
            Vector3 off = rend.bounds.center - piece.transform.position; // world-space offset
            targetFinal -= new Vector3(off.x, 0f, off.z);
        }

        // Animate tới vị trí đích
        AnimatePieceMovement(piece, targetFinal);
    }
}
