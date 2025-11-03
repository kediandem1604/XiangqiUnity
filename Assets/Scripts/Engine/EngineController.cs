using System;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Điều phối gọi Pikafish và phát sự kiện bestmove → vẽ mũi tên.
/// </summary>
public class EngineController : MonoBehaviour
{
    [Header("Search Settings")]
    public int depth = 5;

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
        
        // Đợi NNUE sẵn sàng trước khi gửi lệnh
        float t0 = Time.realtimeSinceStartup;
        while (!Pikafish.EvalReady && Time.realtimeSinceStartup - t0 < 3f)
            yield return null;

        // Đợi engine hoàn toàn sẵn sàng (native init + callbacks set)
        t0 = Time.realtimeSinceStartup;
        while (!Pikafish.Instance.IsReady && Time.realtimeSinceStartup - t0 < 5f)
        {
            yield return null;
        }
        
        if (!Pikafish.Instance.IsReady)
        {
            Debug.LogError("[EngineController] Engine not ready after timeout, aborting search");
            yield break;
        }

        bool isStart = (fen == STARTPOS_FEN || fen == "startpos");
        if (isStart)
        {
            Debug.Log("[EngineController] position startpos");
            Pikafish.Instance.SetPositionFEN("startpos", movesCsv);
        }
        else
        {
            Debug.Log($"[EngineController] position fen {fen}");
            Pikafish.Instance.SetPositionFEN(fen, movesCsv);
        }
        yield return null;
        Pikafish.Instance.GoDepth(depth);
    }
    private void OnBestMove(string bm, string ponder)
    {
        Debug.Log($"[EngineController] bestmove={bm} ponder={ponder}");
        if (!TryParseMoveToGrid(bm, out int f1, out int r1, out int f2, out int r2))
        {
            Debug.LogWarning($"[EngineController] Cannot parse bestmove: {bm}");
            return;
        }
        _arrow.ShowArrow(f1, r1, f2, r2, _grid);
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

    // Hỗ trợ 2 format: a0a9 (a-i,0-9) hoặc 4 chữ số 0-9: f r f r
    static bool TryParseMoveToGrid(string s, out int f1, out int r1, out int f2, out int r2)
    {
        f1 = r1 = f2 = r2 = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        s = s.Trim();
        // digits only, length 4: 0312
        if (Regex.IsMatch(s, "^[0-9]{4}$"))
        {
            f1 = s[0] - '0'; r1 = s[1] - '0'; f2 = s[2] - '0'; r2 = s[3] - '0';
            return InBoard(f1, r1) && InBoard(f2, r2);
        }
        // algebraic a0a9
        if (Regex.IsMatch(s, "^[a-i][0-9][a-i][0-9]$"))
        {
            f1 = s[0] - 'a'; r1 = s[1] - '0'; f2 = s[2] - 'a'; r2 = s[3] - '0';
            return InBoard(f1, r1) && InBoard(f2, r2);
        }
        // UCI-liken with files 1..9 and ranks 1..10 (some variants)
        if (Regex.IsMatch(s, "^[a-i][1-9][a-i][1-9]0?$"))
        {
            int rA = (s[1] == '0' ? 10 : (s[1]-'0')); // handle 10 if present
            int rB = (s.Length == 5 ? 10 : (s[3]-'0'));
            f1 = s[0]-'a'; r1 = rA-1; f2 = s[2]-'a'; r2 = rB-1;
            return InBoard(f1, r1) && InBoard(f2, r2);
        }
        return false;
    }

    static bool InBoard(int f, int r) => f >= 0 && f <= 8 && r >= 0 && r <= 9;

    // Dùng để so sánh startpos chuẩn Xiangqi (chỉnh lại cho khớp với GenerateFen)
    const string STARTPOS_FEN = "rnbakabnr/9/1c5c1/p1p1p1p1p/9/9/P1P1P1P1P/1C5C1/9/RNBAKABNR w - - 0 1";
}


