using UnityEngine;

/// <summary>
/// Component gắn vào mỗi Dot cylinder để lưu tọa độ và cho phép click
/// </summary>
public class DotController : MonoBehaviour
{
    public int file;
    public int rank;
    
    public void SetPosition(int f, int r)
    {
        file = f;
        rank = r;
    }
}

