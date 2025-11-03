using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public class PieceController : MonoBehaviour
{
    public enum PieceType { King, Advisor, Elephant, Rook, Horse, Cannon, Pawn }

    [Header("Piece Info")]
    public PieceType pieceType = PieceType.King;
    public bool isRed = true;
    public int file;
    public int rank;
    [Header("Y Lock")]
    public float baseY; // Y gốc cố định để khóa cao độ khi di chuyển

    void Awake()
    {
        baseY = transform.position.y;
    }

    void OnEnable()
    {
        UpdatePosition();
    }

    /// <summary>
    /// Cập nhật file và rank từ world position - KHÔNG set lại transform để tránh "nhảy lệch"
    /// </summary>
    public void UpdatePosition()
    {
        if (BoardController.Instance == null) return;
        
        // Lấy world position
        Vector3 worldPos = transform.position;
        var renderer = GetComponent<Renderer>();
        if (renderer == null) renderer = GetComponentInChildren<Renderer>();
        if (renderer != null) worldPos = renderer.bounds.center;
        
        // Convert world → (file, rank) dùng BoardFromRooks
        var boardFromRooks = BoardController.Instance.GetComponent<BoardFromRooks>();
        if (boardFromRooks != null && boardFromRooks.TryWorldToGrid(worldPos, out int f, out int r))
        {
            file = f;
            rank = r;
        }
        // KHÔNG set transform.localPosition/position ở đây để tránh snap!
    }
}
