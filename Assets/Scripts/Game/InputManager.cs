using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Xử lý input từ camera xuống bàn cờ (raycast) - Dùng Input System mới
/// </summary>
public class InputManager : MonoBehaviour
{
    private Camera _mainCamera;
    private UnityEngine.InputSystem.Mouse _mouse;
    
    void Start()
    {
        // Tìm Main Camera nếu chưa có
        _mainCamera = Camera.main;
        if (_mainCamera == null)
        {
            _mainCamera = FindFirstObjectByType<Camera>();
        }
        
        if (_mainCamera == null)
        {
            Debug.LogError("[InputManager] No camera found!");
        }
        
        // Get mouse input
        _mouse = UnityEngine.InputSystem.Mouse.current;
    }
    
    void Update()
    {
        // Click chuột trái - Dùng Input System mới
        if (_mouse != null && _mouse.leftButton.wasPressedThisFrame)
        {
            HandleClick();
        }
    }
    
    void HandleClick()
    {
        if (_mainCamera == null) return;
        
        // Raycast từ camera xuống - Dùng Input System mới
        Ray ray = _mainCamera.ScreenPointToRay(_mouse.position.ReadValue());
        RaycastHit hit;
        
        // Raycast không dùng layerMask để bắt mọi thứ
        if (Physics.Raycast(ray, out hit, 1000f))
        {
            GameObject hitObject = hit.collider.gameObject;
            
            // Kiểm tra xem có phải quân cờ không
            PieceController piece = hitObject.GetComponent<PieceController>();
            if (piece == null)
            {
                piece = hitObject.GetComponentInParent<PieceController>();
            }
            
            if (piece != null)
            {
                // Click vào quân cờ
                Debug.Log($"[CLICK] Hit piece: {piece.gameObject.name} - {piece.pieceType} {(piece.isRed ? "Red" : "Black")} at ({piece.file},{piece.rank})");
                BoardController.Instance?.OnPieceClicked(piece);
                return;
            }
            
            // Kiểm tra xem có phải click vào dot (cylinder) không
            DotController dot = hitObject.GetComponent<DotController>();
            if (dot != null && BoardController.Instance?.SelectedPiece != null)
            {
                // Click vào dot hợp lệ - di chuyển quân đến vị trí dot
                Debug.Log($"[CLICK] Hit dot at ({dot.file}, {dot.rank})");
                BoardController.Instance.TryMoveSelectedPiece(dot.file, dot.rank);
                return;
            }
            
            // Click vào bàn cờ (empty square)
            // Convert hit point thành board coordinates
            Vector3 localPos = hit.point;
            if (BoardController.Instance != null)
            {
                localPos = BoardController.Instance.transform.InverseTransformPoint(hit.point);
            }
            
            int file = Mathf.RoundToInt(localPos.x + 4f);
            int rank = Mathf.RoundToInt(localPos.z + 4.5f);
            
            Debug.Log($"[CLICK] Hit board at ({file}, {rank})");
            
            // Thử di chuyển quân đã chọn
            if (BoardController.Instance?.SelectedPiece != null)
            {
                BoardController.Instance.TryMoveSelectedPiece(file, rank);
            }
        }
    }
}

