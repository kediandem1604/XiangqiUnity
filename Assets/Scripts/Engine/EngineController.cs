using System;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Điều phối gọi Pikafish và phát sự kiện bestmove → vẽ mũi tên.
/// </summary>
public class EngineController : MonoBehaviour
{
    [Header("Search Settings")]
    public int depth = 12;
    
    [Header("Coordinate Mapping")]
    [Tooltip("Nếu mũi tên lệch 1 đơn vị, thử adjust coordinate offset")]
    public int rankOffset = 0;  // Adjust nếu grid lệch với engine notation
    public int fileOffset = 0;  // Thường không cần
    
    // Tránh overlap các lần phân tích
    private bool _isAnalyzing = false;
    
    // Track move history để truyền moves khi dùng startpos (theo logic flutter_android)
    private System.Collections.Generic.List<string> _moveHistory = new System.Collections.Generic.List<string>();

    private BestMoveArrow _arrow;
    private BoardFromRooks _grid;

    void Awake()
    {
        _arrow = GetComponent<BestMoveArrow>();
        if (_arrow == null) _arrow = gameObject.AddComponent<BestMoveArrow>();
        _grid = GetComponent<BoardFromRooks>();

        // Đợi một frame để đảm bảo Pikafish.Instance đã khởi tạo xong
        StartCoroutine(DelayedSubscribe());
    }
    
    System.Collections.IEnumerator DelayedSubscribe()
    {
        // Đợi Pikafish sẵn sàng
        yield return null;
        
        if (Pikafish.Instance != null)
        {
            Pikafish.Instance.OnBestMove += OnBestMove;
            Pikafish.Instance.OnInfo += OnInfo;
        }
    }

    void OnDestroy()
    {
        // KHÔNG tạo Pikafish.Instance trong OnDestroy!
        // Chỉ unsubscribe nếu instance đã tồn tại (không tạo mới)
        if (Pikafish.HasInstance)
        {
            Pikafish.Instance.OnBestMove -= OnBestMove;
            Pikafish.Instance.OnInfo -= OnInfo;
        }
    }

    public void RequestBestMoveByFen(string fen, string movesCsv = null)
    {
        StopAllCoroutines();
        StartCoroutine(CoWaitEvalAndSearch(fen, movesCsv));
    }

    System.Collections.IEnumerator CoWaitEvalAndSearch(string fen, string movesCsv)
    {
        // Đợi Pikafish instance sẵn sàng
        while (Pikafish.Instance == null)
            yield return null;
        
        // Đợi NNUE sẵn sàng trước khi gửi lệnh (không timeout để đảm bảo chất lượng nước đầu)
        while (!Pikafish.EvalReady)
            yield return null;

        // Đợi engine hoàn toàn sẵn sàng (native init + callbacks set)
        float t0 = Time.realtimeSinceStartup;
        while (!Pikafish.Instance.IsReady && Time.realtimeSinceStartup - t0 < 5f)
        {
            yield return null;
        }
        
        if (!Pikafish.Instance.IsReady)
        {
            Debug.LogError("[EngineController] Engine not ready after timeout, aborting search");
            yield break;
        }

        // Kiểm tra nếu từ startpos → dùng startpos (theo logic flutter_android)
        // Nếu không → dùng FEN
        bool isFromStartpos = IsFromStartpos(fen);
        if (isFromStartpos)
        {
            // Từ startpos → dùng startpos (theo logic flutter_android)
            Debug.Log($"[EngineController] position startpos");
            Pikafish.Instance.SetPositionFEN("startpos", movesCsv);
        }
        else
        {
            // Không từ startpos → dùng FEN (theo logic flutter_android)
            // KHÔNG force side to move - để BoardController quyết định side to move
            // Chỉ đảm bảo FEN có side to move hợp lệ
            string actualFen = fen;
            if (!actualFen.Contains(" w ") && !actualFen.Contains(" b "))
            {
                // FEN thiếu side to move, thêm 'w' (đỏ đi trước) mặc định
                if (actualFen.EndsWith(" -"))
                    actualFen = actualFen.Replace(" -", " w -");
                else
                    actualFen += " w - - 0 1";
            }
            
            // Debug: log side to move để kiểm tra
            string sideToMove = actualFen.Contains(" w ") ? "w (red)" : (actualFen.Contains(" b ") ? "b (black)" : "unknown");
            Debug.Log($"[EngineController] position fen, side to move: {sideToMove}, FEN: {actualFen}");
            Pikafish.Instance.SetPositionFEN(actualFen, movesCsv);
        }
        yield return null;
        Pikafish.Instance.GoDepth(depth);
    }
    private void OnBestMove(string bm, string ponder)
    {
        // Parse bestmove từ raw string (theo logic flutter_android)
        // Engine có thể trả về "bestmove xxxx" hoặc chỉ "xxxx"
        string bestMove = ParseBestMoveFromRaw(bm);
        if (string.IsNullOrEmpty(bestMove))
        {
            Debug.LogWarning($"[EngineController] Invalid or empty bestmove: {bm}");
            return;
        }
        
        // Validate và normalize bestmove (theo logic flutter_android)
        bestMove = ValidateAndNormalizeBestMove(bestMove);
        if (string.IsNullOrEmpty(bestMove))
        {
            Debug.LogWarning($"[EngineController] Bestmove failed validation: {bm}");
            return;
        }
        
        Debug.Log($"[EngineController] bestmove={bestMove} ponder={ponder}");
        
        // Parse và hiển thị mũi tên (không chặn nếu validation thất bại để tránh bỏ lỡ bestmove hợp lệ từ engine)
        if (!TryParseMoveToGrid(bestMove, out int f1, out int r1, out int f2, out int r2))
        {
            Debug.LogWarning($"[EngineController] Cannot parse bestmove to grid: {bestMove}");
            return;
        }
        // Không ép có quân tại from để không chặn hiển thị mũi tên
        
        // Apply coordinate offsets nếu cần (để fix lệch 1 đơn vị)
        f1 += fileOffset;
        r1 += rankOffset;
        f2 += fileOffset;
        r2 += rankOffset;
        
        // Clamp lại trong bounds (Unity grid: 0-8 files, 0-9 ranks)
        f1 = Mathf.Clamp(f1, 0, 8);
        r1 = Mathf.Clamp(r1, 0, 9);
        f2 = Mathf.Clamp(f2, 0, 8);
        r2 = Mathf.Clamp(r2, 0, 9);
        
        // Debug: log parsed coordinates
        Debug.Log($"[EngineController] Parsed move (UCI→Unity): {bestMove} -> ({f1},{r1}) -> ({f2},{r2}) [offset: file={fileOffset}, rank={rankOffset}]");
        
        _arrow.ShowArrow(f1, r1, f2, r2, _grid);
    }
    
    /// <summary>
    /// Validate bestmove theo Xiangqi rules (như flutter_android dùng XiangqiRules.isValidMove).
    /// Kiểm tra cơ bản: có quân cờ ở vị trí from và move có hợp lệ không.
    /// </summary>
    private bool IsValidBestMove(string bestMove)
    {
        if (string.IsNullOrWhiteSpace(bestMove) || bestMove.Length != 4)
            return false;
        
        // Parse tọa độ
        if (!TryParseMoveToGrid(bestMove, out int f1, out int r1, out int f2, out int r2))
            return false;
        
        // Kiểm tra có quân cờ ở vị trí from
        if (!ValidatePieceAtPosition(f1, r1))
            return false;
        
        // Kiểm tra move có hợp lệ theo Xiangqi rules (dùng MoveValidator)
        var boardController = GetComponent<BoardController>();
        if (boardController == null) return false;
        
        var piece = boardController.GetPieceAt(f1, r1);
        if (piece == null) return false;
        
        // Validate move bằng MoveValidator
        return MoveValidator.IsValidMove(piece, f2, r2, boardController);
    }
    
    /// <summary>
    /// Validate có quân cờ ở vị trí (file, rank) không.
    /// </summary>
    private bool ValidatePieceAtPosition(int file, int rank)
    {
        var boardController = GetComponent<BoardController>();
        if (boardController == null) return false;
        
        var piece = boardController.GetPieceAt(file, rank);
        return piece != null;
    }
    
    /// <summary>
    /// Parse bestmove từ raw string (theo logic flutter_android).
    /// Engine có thể trả về "bestmove xxxx ponder yyyy" hoặc chỉ "xxxx".
    /// </summary>
    private string ParseBestMoveFromRaw(string rawBestMove)
    {
        if (string.IsNullOrWhiteSpace(rawBestMove))
            return string.Empty;
        
        string line = rawBestMove.Trim();
        
        // Nếu có "bestmove" prefix, parse như flutter_android
        if (line.StartsWith("bestmove", System.StringComparison.OrdinalIgnoreCase))
        {
            string[] parts = line.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
            int idx = Array.IndexOf(parts, "bestmove");
            if (idx >= 0 && idx + 1 < parts.Length)
            {
                string move = parts[idx + 1];
                // Loại bỏ "(none)" hoặc null
                if (move == "(none)" || move == "null" || string.IsNullOrEmpty(move))
                    return string.Empty;
                return move;
            }
            return string.Empty;
        }
        
        // Nếu không có "bestmove" prefix, coi như đã là move string
        return line;
    }
    
    /// <summary>
    /// Validate và normalize bestmove từ engine (theo logic flutter_android).
    /// - Loại bỏ "(none)" hoặc moves không hợp lệ
    /// - Validate Xiangqi notation (4 chars, a-i 0-9)
    /// </summary>
    private string ValidateAndNormalizeBestMove(string rawBestMove)
    {
        if (string.IsNullOrWhiteSpace(rawBestMove))
            return string.Empty;
        
        // Trim và lowercase
        string move = rawBestMove.Trim().ToLowerInvariant();
        
        // Loại bỏ "(none)" hoặc moves không hợp lệ
        if (move == "(none)" || move == "none" || move.Length != 4)
            return string.Empty;
        
        // Validate Xiangqi notation: files a-i (0-8), ranks 0-9
        if (!IsValidXiangqiMove(move))
            return string.Empty;
        
        return move;
    }
    
    /// <summary>
    /// Validate move có phải Xiangqi notation hợp lệ không (theo logic flutter_android).
    /// Files: a-i (0-8), Ranks: 0-9
    /// </summary>
    private bool IsValidXiangqiMove(string move)
    {
        if (move == null || move.Length != 4)
            return false;
        
        // Parse files: a-i (0-8)
        int fromFile = move[0] - 'a';
        int toFile = move[2] - 'a';
        
        // Parse ranks: 0-9
        if (!int.TryParse(move[1].ToString(), out int fromRank) || 
            !int.TryParse(move[3].ToString(), out int toRank))
            return false;
        
        // Validate Xiangqi board bounds
        return fromFile >= 0 && fromFile <= 8 &&
               fromRank >= 0 && fromRank <= 9 &&
               toFile >= 0 && toFile <= 8 &&
               toRank >= 0 && toRank <= 9;
    }

    /// <summary>
    /// Gọi phân tích cho trạng thái bàn cờ mới sau mỗi lần di chuyển quân.
    /// Trình tự an toàn: stop -> set position -> go depth.
    /// </summary>
    public void AnalyzeAfterMove(string fenOrStartpos, string movesCsv = null, string lastMoveUci = null)
    {
        // Track move history (theo logic flutter_android)
        // Nếu có lastMoveUci, thêm vào history
        if (!string.IsNullOrWhiteSpace(lastMoveUci))
        {
            _moveHistory.Add(lastMoveUci);
        }
        
        // Giữ nguyên "startpos" nếu là string literal, không convert ngay
        // Logic kiểm tra startpos sẽ làm trong CoAnalyzeAfterMove
        StopAllCoroutines();
        StartCoroutine(CoAnalyzeAfterMove(fenOrStartpos, movesCsv));
    }
    
    /// <summary>
    /// Reset move history (khi bắt đầu ván mới - theo logic flutter_android).
    /// </summary>
    public void ResetMoveHistory()
    {
        _moveHistory.Clear();
    }
    
    /// <summary>
    /// Kiểm tra xem có phải là bàn cờ khởi đầu không (theo logic flutter_android).
    /// Flutter: _isFromStartpos() => state.setupFen == null
    /// Unity: kiểm tra bằng cách so sánh board position với STARTPOS_FEN.
    /// </summary>
    private bool IsFromStartpos(string fen)
    {
        if (string.IsNullOrWhiteSpace(fen))
            return false;
        
        // Nếu là string literal "startpos"
        if (fen.Equals("startpos", System.StringComparison.OrdinalIgnoreCase))
            return true;
        
        // So sánh board position (phần đầu trước dấu cách) với STARTPOS_FEN
        // Flutter dùng: _isFromStartpos() => state.setupFen == null
        // Unity: so sánh board position để xác định
        string[] fenParts = fen.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
        if (fenParts.Length == 0)
            return false;
        
        string boardPart = fenParts[0];
        string[] startPosParts = STARTPOS_FEN.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
        if (startPosParts.Length == 0)
            return false;
        
        string startPosBoard = startPosParts[0];
        
        // So sánh board position (case-insensitive)
        return boardPart.Equals(startPosBoard, System.StringComparison.OrdinalIgnoreCase);
    }

    System.Collections.IEnumerator CoAnalyzeAfterMove(string fenOrStartpos, string movesCsv)
    {
        // Gate: engine sẵn sàng
        float t0 = Time.realtimeSinceStartup;
        while (Pikafish.Instance == null)
            yield return null;
        // Đợi native ready
        while (!Pikafish.Instance.IsReady && Time.realtimeSinceStartup - t0 < 5f)
            yield return null;
        // Đợi NNUE sẵn sàng trước khi gửi position (đảm bảo log đúng thứ tự: NNUE → position)
        while (!Pikafish.EvalReady)
            yield return null;
        
        if (Pikafish.Instance == null || !Pikafish.Instance.IsReady)
        {
            Debug.LogWarning("[EngineController] AnalyzeAfterMove: engine not ready");
            yield break;
        }
        
        if (_isAnalyzing)
        {
            // Đợi lượt đang thực thi kết thúc để tránh overlap
            yield return null;
        }
        _isAnalyzing = true;
        
        // 2) Set position mới
        // Quy ước:
        // - Nước đầu (history==0): dùng FEN hiện tại, KHÔNG kèm moves
        // - Các nước sau  (history>0): dùng FEN + full history
        
        // Dừng search hiện tại khi đã có history để engine nhận position mới
        if (_moveHistory.Count > 0)
        {
            try { Pikafish.Instance.Stop(); } catch {}
            yield return null;
        }

        // Build history CSV (full) nếu đã có nước đi
        string historyCsv = _moveHistory.Count > 0 ? string.Join(" ", _moveHistory) : null;
        
        // Kiểm tra nếu từ startpos → dùng startpos (theo logic flutter_android)
        // Nếu không → dùng FEN và đảm bảo side to move đúng
        bool isFromStartpos = IsFromStartpos(fenOrStartpos);
        if (isFromStartpos)
        {
            // Từ startpos → dùng startpos (theo logic flutter_android)
            Debug.Log($"[EngineController] position startpos {(historyCsv==null ? "(no moves)" : "(with moves)")}");
            Pikafish.Instance.SetPositionFEN("startpos", historyCsv);
        }
        else
        {
            // Không từ startpos → dùng FEN và đảm bảo side to move đúng
            string actualFen = fenOrStartpos;
            if (!actualFen.Contains(" w ") && !actualFen.Contains(" b "))
            {
                // FEN thiếu side to move, thêm 'w' (đỏ đi trước) mặc định
                if (actualFen.EndsWith(" -"))
                    actualFen = actualFen.Replace(" -", " w -");
                else
                    actualFen += " w - - 0 1";
            }
            
            // Debug: log side to move để kiểm tra
            string sideToMove = actualFen.Contains(" w ") ? "w (red)" : (actualFen.Contains(" b ") ? "b (black)" : "unknown");
            Debug.Log($"[EngineController] position fen {(historyCsv==null ? "(no moves)" : "(with moves)")}, side to move: {sideToMove}, FEN: {actualFen}");
            Pikafish.Instance.SetPositionFEN(actualFen, historyCsv);
        }
        
        // Nhường 1 frame trước khi go
        yield return null;
        
        // 3) Go depth an toàn
        Debug.Log($"[EngineController] AnalyzeAfterMove: calling go depth {depth}");
        Pikafish.Instance.GoDepth(depth);
        _isAnalyzing = false;
    }

    private void OnInfo(string line)
    {
        // Log vài dòng quan trọng (nnue, eval file, ready, depth)
        if (line.Contains("nnue", System.StringComparison.OrdinalIgnoreCase) ||
            line.Contains("eval", System.StringComparison.OrdinalIgnoreCase) ||
            line.Contains("ready", System.StringComparison.OrdinalIgnoreCase) ||
            line.Contains("info "))
        {
            Debug.Log($"[EngineController][info] {line}");
        }
    }

    /// <summary>
    /// Parse bestmove từ UCI notation sang Unity grid coordinates.
    /// UCI notation: files a-i, ranks 0-9.
    /// Unity grid: files 0-8, ranks 0-9.
    /// KHÔNG FLIP: dùng trực tiếp rank (ở đây 9 sẽ hiển thị trên cùng nếu Unity quy ước 9=top).
    /// </summary>
    static bool TryParseMoveToGrid(string s, out int f1, out int r1, out int f2, out int r2)
    {
        f1 = r1 = f2 = r2 = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        s = s.Trim().ToLowerInvariant();
        
        // Format 1: digits only, length 4: 0312 (0-based grid) - hiếm dùng
        if (Regex.IsMatch(s, "^[0-9]{4}$"))
        {
            f1 = s[0]-'0'; r1 = s[1]-'0'; f2 = s[2]-'0'; r2 = s[3]-'0';
            if (f1==f2 && r1==r2) return false;
            return InBoard(f1,r1) && InBoard(f2,r2);
        }
        
        // Format 2: algebraic notation a0a9 (theo chuẩn UCI/Xiangqi)
        // Dùng trực tiếp rank từ UCI → Unity
        if (Regex.IsMatch(s, "^[a-i][0-9][a-i][0-9]$"))
        {
            // Parse files: a-i (0-8) - từ trái qua phải
            f1 = s[0] - 'a';
            f2 = s[2] - 'a';
            
            // Parse UCI ranks: '0'-'9'
            int fromRankUci = int.Parse(s[1].ToString());
            int toRankUci = int.Parse(s[3].ToString());
            
            r1 = fromRankUci;
            r2 = toRankUci;
            
            if (f1==f2 && r1==r2) return false;
            return InBoard(f1,r1) && InBoard(f2,r2);
        }
        
        // Format 3: explicit rank 10 support: a10b10, a2b10, a10b3
        // rank 10 map về 9
        if (Regex.IsMatch(s, @"^[a-i](10|[1-9])[a-i](10|[1-9])$"))
        {
            // Parse files: a-i (0-8) - từ trái qua phải
            f1 = s[0]-'a';
            
            // Parse left rank: '10' hoặc '1'-'9'
            int leftRankUci = 0;
            if (s[1]=='1' && s.Length > 2 && s[2]=='0')
            {
                leftRankUci = 10;
                // File thứ 2 bắt đầu từ index 3
                f2 = s[3]-'a';
            }
            else
            {
                leftRankUci = int.Parse(s[1].ToString());
                f2 = s[2]-'a';
            }
            
            // Parse right rank
            int rightRankStart = (leftRankUci == 10) ? 4 : 3;
            int rightRankUci = 0;
            if (rightRankStart < s.Length && s[rightRankStart]=='1' && rightRankStart+1 < s.Length && s[rightRankStart+1]=='0')
                rightRankUci = 10;
            else if (rightRankStart < s.Length)
                rightRankUci = int.Parse(s[rightRankStart].ToString());
            
            // Map 10→9 và dùng trực tiếp
            int leftRankBoard = (leftRankUci == 10) ? 9 : leftRankUci;
            int rightRankBoard = (rightRankUci == 10) ? 9 : rightRankUci;
            r1 = leftRankBoard;
            r2 = rightRankBoard;
            
            if (f1==f2 && r1==r2) return false;
            return InBoard(f1,r1) && InBoard(f2,r2);
        }
        
        return false;
    }

    static bool InBoard(int f, int r) => f >= 0 && f <= 8 && r >= 0 && r <= 9;

    // Dùng để so sánh/start khi cần bên dưới đi trước (side-to-move = b)
    const string STARTPOS_FEN = "rnbakabnr/9/1c5c1/p1p1p1p1p/9/9/P1P1P1P1P/1C5C1/9/RNBAKABNR b - - 0 1";
    
    /// <summary>
    /// Helper chuẩn hoá FEN: luôn ép side to move = 'b' (đen đi trước).
    /// Dùng ở mọi nơi gọi SetPositionFEN để đảm bảo engine luôn phân tích cho đen.
    /// </summary>
    private static string ForceBlackToMove(string fen)
    {
        if (string.IsNullOrWhiteSpace(fen))
            return STARTPOS_FEN;
        
        string f = fen;
        
        // Force 'w' → 'b' (đen đi trước)
        if (f.Contains(" w "))
        {
            f = f.Replace(" w ", " b ");
        }
        else if (!f.Contains(" b "))
        {
            // Nếu thiếu hẳn trường side-to-move, bổ sung 'b'
            if (f.EndsWith(" -"))
                f = f.Replace(" -", " b -");
            else
                f += " b - - 0 1";
        }
        
        return f;
    }
}


