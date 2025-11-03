using System.IO;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Khởi tạo Pikafish: copy NNUE từ StreamingAssets → persistentDataPath và set option EvalFile.
/// Gắn script này vào một GameObject trong scene (ví dụ ChessBoard).
/// </summary>
public class PikafishBootstrap : MonoBehaviour
{
    [Header("NNUE")]
    public string streamingRelative = "nn/default.nnue"; // trong StreamingAssets
    public string persistentRelative = "nn/default.nnue"; // trong persistentDataPath

    IEnumerator Start()
    {
        string src = Path.Combine(Application.streamingAssetsPath, streamingRelative);
        string dst = Path.Combine(Application.persistentDataPath, persistentRelative);
        string dstDir = Path.GetDirectoryName(dst);
        if (!Directory.Exists(dstDir)) Directory.CreateDirectory(dstDir);

        // Copy nếu chưa có hoặc kích thước khác
        bool needCopy = true;
        if (File.Exists(dst))
        {
            try
            {
                long existing = new FileInfo(dst).Length;
                // Không có cách rẻ để biết size src trên Android trước khi tải; cứ overwrite=false nếu đã có
                needCopy = existing == 0;
            }
            catch { needCopy = true; }
        }

        if (needCopy)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (UnityWebRequest req = UnityWebRequest.Get(src))
            {
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                {
                    File.WriteAllBytes(dst, req.downloadHandler.data);
                }
                else
                {
                    Debug.LogWarning($"[PikafishBootstrap] Cannot read NNUE from {src}: {req.error}");
                }
            }
#else
            try { File.Copy(src, dst, true); }
            catch (System.Exception ex) { Debug.LogWarning($"[PikafishBootstrap] Copy NNUE failed: {ex.Message}"); }
#endif
        }

        // Đợi Pikafish instance sẵn sàng
        float timeout = Time.realtimeSinceStartup + 5f;
        while (Pikafish.Instance == null && Time.realtimeSinceStartup < timeout)
            yield return null;

        if (Pikafish.Instance == null)
        {
            Debug.LogError("[PikafishBootstrap] Pikafish.Instance is null! Cannot set EvalFile.");
            yield break;
        }

        // Đợi một frame nữa để đảm bảo Awake() đã chạy xong
        yield return null;

        // Đặt option EvalFile
        bool fileExists = File.Exists(dst);
        Debug.Log($"[PikafishBootstrap] NNUE src={src} dst={dst} exists={fileExists}");
        
        if (fileExists)
        {
            Pikafish.Instance.SetOption("EvalFile", dst);
            Debug.Log($"[PikafishBootstrap] Set EvalFile to: {dst}");
        }
        else
        {
            Debug.LogError($"[PikafishBootstrap] NNUE file not found at: {dst}");
        }
        
        yield break;
    }
}


