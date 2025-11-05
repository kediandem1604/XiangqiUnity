using UnityEngine;

/// <summary>
/// Kiểm tra nước đi hợp lệ cho Xiangqi
/// </summary>
public static class MoveValidator
{
    /// <summary>
    /// Kiểm tra nước đi có hợp lệ không
    /// </summary>
    public static bool IsValidMove(PieceController piece, int targetFile, int targetRank, BoardController board)
    {
        if (piece == null) return false;
        if (!board.IsValidPosition(targetFile, targetRank)) return false;

        int sourceFile = piece.file;
        int sourceRank = piece.rank;

        // Không cho phép đứng yên
        if (sourceFile == targetFile && sourceRank == targetRank) return false;

        // Không cho phép ăn đồng đội
        PieceController targetPiece = board.GetPieceAt(targetFile, targetRank);
        if (targetPiece != null && targetPiece.isRed == piece.isRed)
            return false;

        // Kiểm tra theo loại quân
        return piece.pieceType switch
        {
            PieceController.PieceType.King => IsValidKingMove(piece, sourceFile, sourceRank, targetFile, targetRank, board),
            PieceController.PieceType.Advisor => IsValidAdvisorMove(piece, sourceFile, sourceRank, targetFile, targetRank, board),
            PieceController.PieceType.Elephant => IsValidElephantMove(piece, sourceFile, sourceRank, targetFile, targetRank, board),
            PieceController.PieceType.Rook => IsValidRookMove(sourceFile, sourceRank, targetFile, targetRank, board),
            PieceController.PieceType.Horse => IsValidHorseMove(sourceFile, sourceRank, targetFile, targetRank, board),
            PieceController.PieceType.Cannon => IsValidCannonMove(sourceFile, sourceRank, targetFile, targetRank, board),
            PieceController.PieceType.Pawn => IsValidPawnMove(piece, sourceFile, sourceRank, targetFile, targetRank, board),
            _ => false
        };
    }

    // ==================== KING (Tướng/Soái) ====================
    static bool IsValidKingMove(PieceController piece, int fromFile, int fromRank, int toFile, int toRank, BoardController board)
    {
        int fileAbs = Mathf.Abs(toFile - fromFile);
        int rankAbs = Mathf.Abs(toRank - fromRank);
        if (!((fileAbs == 1 && rankAbs == 0) || (fileAbs == 0 && rankAbs == 1))) return false;
        if (piece.isRed)
            // Đỏ ở DƯỚI: cung dưới (ranks 0..2)
            return toRank >= 0 && toRank <= 2 && toFile >= 3 && toFile <= 5;
        else
            // Đen ở TRÊN: cung trên (ranks 7..9)
            return toRank >= 7 && toRank <= 9 && toFile >= 3 && toFile <= 5;
    }

    // ==================== ADVISOR (Sĩ) ====================
    static bool IsValidAdvisorMove(PieceController piece, int fromFile, int fromRank, int toFile, int toRank, BoardController board)
    {
        // Diagonal 1 and must stay in palace (sync flutter)
        int fileAbs = Mathf.Abs(toFile - fromFile);
        int rankAbs = Mathf.Abs(toRank - fromRank);
        if (fileAbs != 1 || rankAbs != 1) return false;
        if (piece.isRed)
            return toRank >= 0 && toRank <= 2 && toFile >= 3 && toFile <= 5;
        else
            return toRank >= 7 && toRank <= 9 && toFile >= 3 && toFile <= 5;
    }

    // ==================== ELEPHANT (Tượng) ====================
    static bool IsValidElephantMove(PieceController piece, int fromFile, int fromRank, int toFile, int toRank, BoardController board)
    {
        // Elephant moves diagonally 2 and cannot cross river (sync flutter)
        int fileAbs = Mathf.Abs(toFile - fromFile);
        int rankAbs = Mathf.Abs(toRank - fromRank);
        if (fileAbs != 2 || rankAbs != 2) return false;
        int eyeFile = fromFile + ((toFile - fromFile) / 2);
        int eyeRank = fromRank + ((toRank - fromRank) / 2);
        if (board.GetPieceAt(eyeFile, eyeRank) != null) return false;
        bool isRed = piece.isRed;
        // Đỏ (dưới) không được qua sông (lên nửa trên: rank >= 5)
        if (isRed && toRank >= 5) return false;
        // Đen (trên) không được qua sông (xuống nửa dưới: rank <= 4)
        if (!isRed && toRank <= 4) return false;
        return true;
    }

    // ==================== ROOK (Xe) ====================
    static bool IsValidRookMove(int fromFile, int fromRank, int toFile, int toRank, BoardController board)
    {
        // Đi theo hàng dọc hoặc ngang
        if (fromFile != toFile && fromRank != toRank)
            return false;

        // Kiểm tra đường đi không bị chặn
        int fileStep = fromFile < toFile ? 1 : fromFile > toFile ? -1 : 0;
        int rankStep = fromRank < toRank ? 1 : fromRank > toRank ? -1 : 0;

        int currentFile = fromFile + fileStep;
        int currentRank = fromRank + rankStep;

        while (currentFile != toFile || currentRank != toRank)
        {
            if (board.GetPieceAt(currentFile, currentRank) != null)
                return false;

            currentFile += fileStep;
            currentRank += rankStep;
        }

        return true;
    }

    // ==================== HORSE (Mã) ====================
    static bool IsValidHorseMove(int fromFile, int fromRank, int toFile, int toRank, BoardController board)
    {
        int fileAbs = Mathf.Abs(toFile - fromFile);
        int rankAbs = Mathf.Abs(toRank - fromRank);
        bool isL = (fileAbs == 2 && rankAbs == 1) || (fileAbs == 1 && rankAbs == 2);
        if (!isL) return false;
        int legFile = fromFile;
        int legRank = fromRank;
        if (fileAbs == 2) legFile = fromFile + ((toFile - fromFile) / 2);
        else legRank = fromRank + ((toRank - fromRank) / 2);
        return board.GetPieceAt(legFile, legRank) == null;
    }

    // ==================== CANNON (Pháo) ====================
    static bool IsValidCannonMove(int fromFile, int fromRank, int toFile, int toRank, BoardController board)
    {
        // Đi theo hàng dọc hoặc ngang
        if (fromFile != toFile && fromRank != toRank)
            return false;

        // Đếm số quân trên đường đi
        int fileStep = fromFile < toFile ? 1 : fromFile > toFile ? -1 : 0;
        int rankStep = fromRank < toRank ? 1 : fromRank > toRank ? -1 : 0;

        int currentFile = fromFile + fileStep;
        int currentRank = fromRank + rankStep;
        int pieceCount = 0;

        while (currentFile != toFile || currentRank != toRank)
        {
            if (board.GetPieceAt(currentFile, currentRank) != null)
                pieceCount++;

            currentFile += fileStep;
            currentRank += rankStep;
        }

        PieceController targetPiece = board.GetPieceAt(toFile, toRank);

        // Nếu đến ô trống: phải không có quân chặn (pieceCount == 0)
        // Nếu đến ô có quân: phải có đúng 1 quân chặn làm "miếng đệm"
        if (targetPiece == null)
            return pieceCount == 0;
        else
            return pieceCount == 1;
    }

    // ==================== PAWN (Tốt) ====================
    static bool IsValidPawnMove(PieceController piece, int fromFile, int fromRank, int toFile, int toRank, BoardController board)
    {
        int fileDiff = toFile - fromFile;
        int rankDiff = toRank - fromRank;
        bool isRed = piece.isRed;
        // Đã qua sông? Đỏ khi rank >= 5, Đen khi rank <= 4
        bool crossedRiver = isRed ? (fromRank >= 5) : (fromRank <= 4);

        if (isRed)
        {
            // Đỏ tiến lên phía TRÊN: rank tăng +1
            if (rankDiff == 1 && fileDiff == 0) return true;
            if (crossedRiver && rankDiff == 0 && Mathf.Abs(fileDiff) == 1) return true; // đi ngang sau khi qua sông
            return false;
        }
        else
        {
            // Đen tiến xuống phía DƯỚI: rank giảm -1
            if (rankDiff == -1 && fileDiff == 0) return true;
            if (crossedRiver && rankDiff == 0 && Mathf.Abs(fileDiff) == 1) return true;
            return false;
        }
    }

    /// <summary>
    /// Lấy danh sách tất cả nước đi hợp lệ của một quân
    /// </summary>
    public static System.Collections.Generic.List<(int file, int rank)> GetValidMoves(PieceController piece, BoardController board)
    {
        var validMoves = new System.Collections.Generic.List<(int, int)>();

        if (piece == null) return validMoves;

        // Kiểm tra tất cả 90 vị trí trên bàn
        for (int file = 0; file <= 8; file++)
        {
            for (int rank = 0; rank <= 9; rank++)
            {
                if (IsValidMove(piece, file, rank, board))
                {
                    validMoves.Add((file, rank));
                }
            }
        }

        return validMoves;
    }
}


