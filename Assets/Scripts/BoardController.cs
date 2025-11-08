using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Quản lý state bàn cờ và selection
/// </summary>
public partial class BoardController : MonoBehaviour
{
    public static BoardController Instance { get; private set; }
    
    private PieceController _selectedPiece;
    private readonly Dictionary<(int, int), PieceController> _pieces = new Dictionary<(int, int), PieceController>();
    [Header("Side to move (Black starts)")]
    public bool redToMove = false;  // Đen đi trước (false = đen, true = đỏ)
    
    // Track original Y position khi select
    private Dictionary<PieceController, Vector3> _originalPositions = new Dictionary<PieceController, Vector3>();
    
    void Awake()
    {
        Instance = this;
        
        // Ensure InputManager exists
        if (FindFirstObjectByType<InputManager>() == null)
        {
            gameObject.AddComponent<InputManager>();
        }
        
        // BoardGridManager được gắn trực tiếp trên Chessboard ngoài Editor,
        // không tự thêm/bind ở đây nữa.
        
        InitializePieces();

        // Phân tích ngay khi Play (dùng AnalyzeAfterMove từ EngineController)
        var eng = GetComponent<EngineController>();
        if (eng != null)
        {
            string fen = GenerateFen();
            eng.AnalyzeAfterMove(fen, null);
        }
    }
    
    void InitializePieces()
    {
        // Tìm tất cả quân cờ trong scene - Dùng FindObjectsByType thay vì FindObjectsOfType
        PieceController[] allPieces = FindObjectsByType<PieceController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var piece in allPieces)
        {
            // Đồng bộ lại (file,rank) theo vị trí thực trên bàn để tránh giá trị rác
            SyncPieceCoordinates(piece);
            (int, int) pos = (piece.file, piece.rank);
            _pieces.TryAdd(pos, piece);
        }
        
        Debug.Log($"Board initialized with {_pieces.Count} pieces");
    }

    void SyncPieceCoordinates(PieceController piece)
    {
        if (piece == null) return;
        var grid = GetComponent<BoardFromRooks>();
        if (grid == null) return;
        // lấy world từ renderer.bounds.center để chính xác
        Vector3 world = piece.transform.position;
        var rend = piece.GetComponent<Renderer>();
        if (rend == null) rend = piece.GetComponentInChildren<Renderer>();
        if (rend != null) world = rend.bounds.center;
        if (grid.TryWorldToGrid(world, out int f, out int r))
        {
            piece.file = f;
            piece.rank = r;
        }
    }
    
    public void OnPieceClicked(PieceController piece)
    {
        // Chỉ cho chọn quân đúng màu lượt đi
        if ((redToMove && !piece.isRed) || (!redToMove && piece.isRed)) return;
        // Nếu click vào quân đã chọn -> Deselect
        if (_selectedPiece == piece)
        {
            DeselectPiece(piece);
            return;
        }

        // Deselect quân cũ
        if (_selectedPiece != null)
        {
            DeselectPiece(_selectedPiece);
        }

        // Select quân mới
        SelectPiece(piece);
    }
    
    void SelectPiece(PieceController piece)
    {
        _selectedPiece = piece;
        
        // Lưu vị trí gốc
        _originalPositions[piece] = piece.transform.localPosition;
        
        // Highlight effect - Nhấc lên theo trục Y (không ảnh hưởng X,Z)
        Vector3 newPos = piece.transform.localPosition;
        newPos.y += 0.3f; // Nhấc lên 0.3 units
        piece.transform.localPosition = newPos;
        
        // Đồng bộ lại tọa độ (file,rank) từ world trước khi tính nước đi
        SyncPieceCoordinates(piece);

        Debug.Log($"✅ Selected: {piece.pieceType} {(piece.isRed ? "Red" : "Black")} at ({piece.file}, {piece.rank})");

        // Hiện chấm hợp lệ
        var boardFromRooks = GetComponent<BoardFromRooks>();
        if (boardFromRooks != null)
        {
            var moves = MoveValidator.GetValidMoves(piece, this);
            int moveCount = moves is ICollection<(int, int)> col ? col.Count : (moves?.Count() ?? 0);
            Debug.Log($"[Dots] ShowMoves called with {moveCount} moves for {piece.pieceType} at ({piece.file},{piece.rank})");
            boardFromRooks.ShowMoves(moves);
        }
    }
    
    void DeselectPiece(PieceController piece)
    {
        // Trả về vị trí gốc
        if (_originalPositions.TryGetValue(piece, out Vector3 originalPos))
        {
            piece.transform.localPosition = originalPos;
            _originalPositions.Remove(piece);
        }
        
        _selectedPiece = null;
        
        // Ẩn chấm
        var boardFromRooks = GetComponent<BoardFromRooks>();
        if (boardFromRooks != null)
        {
            boardFromRooks.ClearDots();
        }
    }
    
    public PieceController GetPieceAt(int file, int rank)
    {
        (int, int) pos = (file, rank);
        return _pieces.GetValueOrDefault(pos);
    }
    
    public bool IsValidPosition(int file, int rank)
    {
        return file is >= 0 and <= 8 && rank is >= 0 and <= 9;
    }
    
    public PieceController SelectedPiece => _selectedPiece;
    
    /// <summary>
    /// Thử di chuyển quân đã chọn đến vị trí (file, rank)
    /// Call method này khi click vào ô trống hoặc quân đối phương
    /// </summary>
    public bool TryMoveSelectedPiece(int targetFile, int targetRank)
    {
        if (_selectedPiece == null) return false;

        Debug.Log($"Moving {_selectedPiece.pieceType} from ({_selectedPiece.file}, {_selectedPiece.rank}) to ({targetFile}, {targetRank})");

        // Chỉ cho phép nếu hợp lệ
        if (!MoveValidator.IsValidMove(_selectedPiece, targetFile, targetRank, this))
        {
            Debug.Log("Move is not valid");
            return false;
        }

        // Kiểm tra có quân ở vị trí đích không
        PieceController targetPiece = GetPieceAt(targetFile, targetRank);

        // Nếu có quân đối phương → ăn quân
        if (targetPiece != null && targetPiece.isRed != _selectedPiece.isRed)
        {
            CapturePiece(targetPiece);
        }

        // Cập nhật position trong dictionary
        _pieces.Remove((_selectedPiece.file, _selectedPiece.rank));
        _selectedPiece.file = targetFile;
        _selectedPiece.rank = targetRank;
        _pieces[(targetFile, targetRank)] = _selectedPiece;

        // Di chuyển quân
        PieceMover.Instance.MovePiece(_selectedPiece, targetFile, targetRank, this);

        // Deselect & clear dots
        DeselectPiece(_selectedPiece);

        // Đổi lượt đi
        redToMove = !redToMove;

        // Gọi lại engine để phân tích board mới (dùng AnalyzeAfterMove từ EngineController)
        var eng = GetComponent<EngineController>();
        if (eng != null)
        {
            string fen = GenerateFen();
            eng.AnalyzeAfterMove(fen, null);
        }

        return true;
    }
    
    void CapturePiece(PieceController piece)
    {
        Debug.Log($"Captured {piece.pieceType} {(piece.isRed ? "Red" : "Black")} at ({piece.file}, {piece.rank})");
        
        _pieces.Remove((piece.file, piece.rank));
        Destroy(piece.gameObject);
    }

    // Dots chuyển sang BoardGridManager
}

// ========== FEN & Engine Integration ==========
public partial class BoardController
{
    string GenerateFen()
    {
        // Xiangqi FEN yêu cầu quét từ TRÊN XUỐNG (bên Đen trước).
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int rr = 9; rr >= 0; rr--)
        {
            int empty = 0;
            for (int ff = 0; ff <= 8; ff++)
            {
                int r = rr;
                int f = ff;
                if (_pieces.TryGetValue((f, r), out var p) && p != null)
                {
                    if (empty > 0)
                    {
                        sb.Append(empty);
                        empty = 0;
                    }
                    sb.Append(GetFenChar(p));
                }
                else
                {
                    empty++;
                }
            }
            if (empty > 0) sb.Append(empty);
            if (rr != 0) sb.Append('/');
        }
        sb.Append(' ');
        sb.Append(redToMove ? 'w' : 'b');
        // Các trường còn lại để mặc định
        sb.Append(" - - 0 1");
        return sb.ToString();
    }

    char GetFenChar(PieceController p)
    {
        char c = 'p';
        switch (p.pieceType)
        {
            case PieceController.PieceType.King: c = 'k'; break;
            case PieceController.PieceType.Advisor: c = 'a'; break;
            case PieceController.PieceType.Elephant: c = 'b'; break;
            case PieceController.PieceType.Rook: c = 'r'; break;
            case PieceController.PieceType.Horse: c = 'n'; break;
            case PieceController.PieceType.Cannon: c = 'c'; break;
            case PieceController.PieceType.Pawn: c = 'p'; break;
        }
        // Đỏ in hoa, Đen thường (quy định mới: lowercase = đen, uppercase = đỏ)
        if (p.isRed) c = char.ToUpperInvariant(c);
        return c;
    }

    // Đã xóa AnalyzeCurrentPosition() - chỉ dùng AnalyzeAfterMove() từ EngineController
    // để tránh duplicate calls và đảm bảo side to move đúng
}
