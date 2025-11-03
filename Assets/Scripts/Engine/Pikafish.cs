using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// Wrapper gọi native plugin Pikafish qua DllImport, hàng đợi callback an toàn thread.
/// </summary>
public class Pikafish : MonoBehaviour
{
    private static Pikafish _instance;
    public static bool EvalReady { get; private set; }
    
    // Ready flags để gate go calls
    private bool _nativeReady = false;
    private bool _callbacksReady = false;
    public bool IsReady => _nativeReady && _callbacksReady;
    
    public static Pikafish Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("Pikafish");
                _instance = go.AddComponent<Pikafish>();
                DontDestroyOnLoad(go);
            }
            return _instance;
        }
    }
    
    // Helper để check instance có tồn tại không mà không tạo mới
    public static bool HasInstance => _instance != null;

#if UNITY_IOS && !UNITY_EDITOR
    const string LIB = "__Internal";
#else
    const string LIB = "pikafish"; // pikafish.dll / libpikafish.so
#endif

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void InfoCb([MarshalAs(UnmanagedType.LPUTF8Str)] string line);
    
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void BestMoveCb(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string bestmove,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string ponder);

    [DllImport(LIB, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)] static extern int pika_init();
    [DllImport(LIB, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)] static extern void pika_quit();
    [DllImport(LIB, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)] static extern int pika_setoption([MarshalAs(UnmanagedType.LPStr)] string key, [MarshalAs(UnmanagedType.LPStr)] string value);
    [DllImport(LIB, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)] static extern int pika_position_fen([MarshalAs(UnmanagedType.LPStr)] string fen, [MarshalAs(UnmanagedType.LPStr)] string movesCsv);
    [DllImport(LIB, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)] static extern int pika_go_movetime(int ms);
    [DllImport(LIB, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)] static extern int pika_go_depth(int depth);
    [DllImport(LIB, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)] static extern int pika_go_infinite();
    [DllImport(LIB, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)] static extern int pika_stop();
    // Dùng delegate trực tiếp thay vì IntPtr (Marshaller sẽ tự convert)
    [DllImport(LIB, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)] static extern void pika_set_info_callback(InfoCb cb);
    [DllImport(LIB, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)] static extern void pika_set_bestmove_callback(BestMoveCb cb);
    [DllImport(LIB, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)] static extern void pika_set_callbacks(InfoCb infoCb, BestMoveCb bestmoveCb);
    [DllImport(LIB, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)] static extern void pika_emit_test_callbacks();
    [DllImport(LIB, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)] static extern int pika_get_callback_state(); // Returns: 0=none, 1=info only, 2=bestmove only, 3=both

    // Static delegates với GCHandle để giữ lifetime suốt quá trình chạy
    // Pattern: static field + GCHandle.Alloc để delegate không bị GC
    private static InfoCb _infoCb;
    private static BestMoveCb _bmCb;
    private static GCHandle _infoCbHandle;
    private static GCHandle _bmCbHandle;
    
    // Rebind callbacks sau domain reload của Unity
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    static void RebindCallbacksAfterReload()
    {
        if (_infoCb == null || _bmCb == null || _instance == null)
        {
            // Chỉ rebind nếu delegates và instance đã được khởi tạo (sau Awake())
            return;
        }
        
        try
        {
            // Re-pin delegates nếu cần
            if (!_infoCbHandle.IsAllocated) _infoCbHandle = GCHandle.Alloc(_infoCb, GCHandleType.Normal);
            if (!_bmCbHandle.IsAllocated) _bmCbHandle = GCHandle.Alloc(_bmCb, GCHandleType.Normal);
            
            // Rebind callbacks vào DLL (thử atomic function trước, fallback về separate)
            bool success = false;
            try
            {
                pika_set_callbacks(_infoCb, _bmCb);
                Debug.Log("[Pikafish] Callbacks rebound after domain reload (atomic)");
                success = true;
            }
            catch (System.EntryPointNotFoundException)
            {
                // Fallback cho DLL cũ
                pika_set_info_callback(_infoCb);
                pika_set_bestmove_callback(_bmCb);
                Debug.Log("[Pikafish] Callbacks rebound after domain reload (separate, old DLL)");
                success = true;
            }
            
            if (success && _instance != null)
            {
                _instance._callbacksReady = true;
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[Pikafish] Failed to rebind callbacks after domain reload: {ex.Message}");
        }
    }

    private readonly ConcurrentQueue<string> _infoQueue = new ConcurrentQueue<string>();
    private readonly ConcurrentQueue<(string best, string ponder)> _bmQueue = new ConcurrentQueue<(string, string)>();

    public event Action<string> OnInfo;
    public event Action<string, string> OnBestMove;

    void Awake()
    {
        Debug.Log("[Pikafish] Awake() started");
        
        // Kill duplicates - singleton pattern bulletproof
        if (_instance != null && _instance != this)
        {
            Debug.LogWarning("[Pikafish] Duplicate instance detected, destroying...");
            Destroy(gameObject);
            return;
        }
        
        _instance = this;
        DontDestroyOnLoad(gameObject);
        
        // Đợi một frame để đảm bảo scene đã sẵn sàng
        StartCoroutine(DelayedInit());
    }

    System.Collections.IEnumerator DelayedInit()
    {
        // Đợi frame đầu để Unity hoàn tất initialization
        yield return null;
        
        Debug.Log("[Pikafish] Starting delayed init...");
        
        // Initialize native engine
        int init = 0;
        try 
        { 
            Debug.Log("[Pikafish] Calling pika_init()...");
            init = pika_init();
            _nativeReady = (init == 1);
            Debug.Log($"[Pikafish] pika_init() returned: {init}, _nativeReady={_nativeReady}");
        }
        catch (System.DllNotFoundException ex) 
        { 
            Debug.LogError($"[Pikafish] DLL not found: {ex.Message}\n{ex.StackTrace}");
            yield break;
        }
        catch (System.EntryPointNotFoundException ex) 
        { 
            Debug.LogError($"[Pikafish] EntryPoint not found: {ex.Message}\n{ex.StackTrace}");
            yield break;
        }
        catch (System.AccessViolationException ex)
        {
            Debug.LogError($"[Pikafish] Access Violation (crash): {ex.Message}\n{ex.StackTrace}");
            yield break;
        }
        catch (System.Exception ex) 
        { 
            Debug.LogError($"[Pikafish] Init error: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            yield break;
        }

        if (!_nativeReady)
        {
            Debug.LogError("[Pikafish] Native engine init failed, cannot proceed");
            yield break;
        }

        // Pin delegate và set callbacks
        _callbacksReady = SetCallbacks();
        
        if (_callbacksReady)
        {
            Debug.Log($"[Pikafish] Initialization complete! IsReady={IsReady}");
        }
        else
        {
            Debug.LogError("[Pikafish] Callback setup failed!");
        }

        Debug.Log($"[Pikafish] Awake completed, _nativeReady={_nativeReady}, _callbacksReady={_callbacksReady}, IsReady={IsReady}");
    }

    // Helper method để set callbacks (dùng lại trong DelayedInit và RebindCallbacksAfterReload)
    private bool SetCallbacks()
    {
        try
        {
            Debug.Log("[Pikafish] Setting up callbacks...");
            
            // Free handles cũ nếu có
            if (_infoCbHandle.IsAllocated) _infoCbHandle.Free();
            if (_bmCbHandle.IsAllocated) _bmCbHandle.Free();

            // Tạo static delegates và pin ngay lập tức để lifetime = suốt quá trình chạy
            _infoCb = HandleInfo;
            _bmCb = HandleBestMove;
            
            // Pin delegates để không bị GC thu hồi (critical cho P/Invoke callbacks)
            _infoCbHandle = GCHandle.Alloc(_infoCb, GCHandleType.Normal);
            _bmCbHandle = GCHandle.Alloc(_bmCb, GCHandleType.Normal);

            Debug.Log("[Pikafish] Delegates pinned, setting callbacks...");
            
            // Gán callback vào DLL (thử dùng pika_set_callbacks trước, fallback về 2 functions riêng nếu không tồn tại)
            try 
            { 
                pika_set_callbacks(_infoCb, _bmCb);
                var fpInfo = Marshal.GetFunctionPointerForDelegate(_infoCb);
                var fpBm = Marshal.GetFunctionPointerForDelegate(_bmCb);
                Debug.Log($"[Pikafish] Callbacks set (atomic), Info ptr={fpInfo}, BestMove ptr={fpBm}");
            }
            catch (System.EntryPointNotFoundException)
            {
                // Fallback: DLL cũ chưa có pika_set_callbacks, dùng 2 functions riêng
                Debug.Log("[Pikafish] pika_set_callbacks not found, using separate functions (old DLL?)");
                try
                {
                    pika_set_info_callback(_infoCb);
                    pika_set_bestmove_callback(_bmCb);
                    var fpInfo = Marshal.GetFunctionPointerForDelegate(_infoCb);
                    var fpBm = Marshal.GetFunctionPointerForDelegate(_bmCb);
                    Debug.Log($"[Pikafish] Callbacks set (separate), Info ptr={fpInfo}, BestMove ptr={fpBm}");
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[Pikafish] Set callbacks (fallback) failed: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                    return false;
                }
            }
            catch (System.Exception ex) 
            { 
                Debug.LogError($"[Pikafish] Set callbacks failed: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
            
            // **HARD ASSERT**: Verify native side thấy được callbacks TRƯỚC khi start search
            
            // Check callback state từ native
            try
            {
                int state = pika_get_callback_state();
                string stateStr = state switch
                {
                    0 => "none (only no-ops)",
                    1 => "info only",
                    2 => "bestmove only",
                    3 => "both set ✓",
                    -1 => "error checking state",
                    _ => $"unknown ({state})"
                };
                Debug.Log($"[Pikafish] Callback state from native: {stateStr}");
                
                if (state != 3)
                {
                    Debug.LogError($"[Pikafish] Callbacks not fully set in native! State={state}, expected=3");
                    return false;
                }
            }
            catch (EntryPointNotFoundException)
            {
                Debug.LogWarning("[Pikafish] pika_get_callback_state not found (old DLL?), skipping state check");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[Pikafish] Failed to check callback state: {ex.GetType().Name}: {ex.Message}");
            }
            
            // Gọi test function để confirm callbacks đã được set đúng trong native
            try
            {
                pika_emit_test_callbacks();
                Debug.Log("[Pikafish] ✓ Test callbacks emitted - native side confirmed callbacks are set!");
            }
            catch (EntryPointNotFoundException)
            {
                Debug.LogWarning("[Pikafish] pika_emit_test_callbacks not found (old DLL?), skipping test");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Pikafish] Test callbacks failed: {ex.GetType().Name}: {ex.Message}");
                return false; // Nếu test fail, không ready
            }
            
            return true;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[Pikafish] Callback setup error: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            return false;
        }
    }

    void OnDestroy()
    {
        // Không tạo instance mới trong OnDestroy!
        if (_instance != this) return;
        
        try 
        { 
            // Xóa callbacks ở DLL trước khi dừng engine để tránh thread nền gọi vào delegate đã thu dọn
            try { pika_set_info_callback(null); } catch {}
            try { pika_set_bestmove_callback(null); } catch {}
        } 
        catch {}
        
        try { pika_stop(); } catch {}
        try { pika_quit(); } catch {}

        // Free pinned delegates (sau khi đã xóa callbacks trong DLL)
        if (_infoCbHandle.IsAllocated) _infoCbHandle.Free();
        if (_bmCbHandle.IsAllocated) _bmCbHandle.Free();
        
        _nativeReady = false;
        _callbacksReady = false;
    }

    // Các API public đơn giản
    public void SetOption(string key, string value)
    {
        try
        {
            if (string.IsNullOrEmpty(key))
            {
                Debug.LogWarning("[Pikafish] SetOption called with null/empty key");
                return;
            }
            
            if (string.Equals(key, "EvalFile", System.StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var exists = System.IO.File.Exists(value);
                    long size = exists ? new System.IO.FileInfo(value).Length : 0;
                    Debug.Log($"[Pikafish] SetOption EvalFile='{value}' exists={exists} size={size} bytes (persistent={UnityEngine.Application.persistentDataPath})");
                    EvalReady = exists && size > 0;
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[Pikafish] EvalFile check failed: {ex.Message}");
                }
            }
            
            pika_setoption(key ?? "", value ?? "");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[Pikafish] SetOption failed: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        }
    }
    public void SetPositionFEN(string fen, string movesCsv = null)
    {
        try
        {
            bool isStartPos = string.Equals(fen, "startpos", System.StringComparison.OrdinalIgnoreCase);
            string cmd = string.IsNullOrEmpty(movesCsv)
                ? (isStartPos ? "position startpos" : $"position fen {fen}")
                : (isStartPos ? $"position startpos moves {movesCsv}" : $"position fen {fen} moves {movesCsv}");
            Debug.Log($"[Pikafish] {cmd}");
            pika_position_fen(fen ?? "startpos", movesCsv);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[Pikafish] SetPositionFEN failed: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        }
    }
    
    public void GoMoveTimeMs(int ms) 
    { 
        if (!IsReady)
        {
            Debug.LogWarning($"[Pikafish] Engine not ready yet! _nativeReady={_nativeReady}, _callbacksReady={_callbacksReady}. Skipping go movetime {ms}ms");
            return;
        }
        
        try 
        { 
            Debug.Log($"[Pikafish] go movetime {ms}ms"); 
            int ret = pika_go_movetime(ms);
            if (ret == 0)
            {
                Debug.LogWarning($"[Pikafish] pika_go_movetime returned 0 - engine busy or callbacks not set");
            }
            else if (ret < 0)
            {
                Debug.LogError($"[Pikafish] pika_go_movetime failed with error code {ret}");
            }
            else if (ret != 1)
            {
                Debug.LogWarning($"[Pikafish] pika_go_movetime returned unexpected value {ret}");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[Pikafish] GoMoveTimeMs failed: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        }
    }
    
    public void GoDepth(int depth) 
    { 
        if (!IsReady)
        {
            Debug.LogWarning($"[Pikafish] Engine not ready yet! _nativeReady={_nativeReady}, _callbacksReady={_callbacksReady}. Skipping go depth {depth}");
            return;
        }
        
        try 
        { 
            Debug.Log($"[Pikafish] go depth {depth}"); 
            int ret =  (depth);
            if (ret == 0)
            {
                Debug.LogWarning($"[Pikafish] pika_go_depth returned 0 - engine busy or callbacks not set");
            }
            else if (ret < 0)
            {
                Debug.LogError($"[Pikafish] pika_go_depth failed with error code {ret}");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[Pikafish] GoDepth failed: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        }
    }
    
    public void GoInfinite() 
    { 
        if (!IsReady)
        {
            Debug.LogWarning($"[Pikafish] Engine not ready yet! _nativeReady={_nativeReady}, _callbacksReady={_callbacksReady}. Skipping go infinite");
            return;
        }
        
        try 
        { 
            Debug.Log("[Pikafish] go infinite"); 
            int ret = pika_go_infinite();
            if (ret == 0)
            {
                Debug.LogWarning($"[Pikafish] pika_go_infinite returned 0 - engine busy or callbacks not set");
            }
            else if (ret < 0)
            {
                Debug.LogError($"[Pikafish] pika_go_infinite failed with error code {ret}");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[Pikafish] GoInfinite failed: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        }
    }
    
    public void Stop() 
    { 
        try 
        { 
            Debug.Log("[Pikafish] stop"); 
            pika_stop(); 
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[Pikafish] Stop failed: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        }
    }

    // Callback native đẩy về hàng đợi
    // LƯU Ý: Callback được gọi từ native thread, không được touch Unity API
    [AOT.MonoPInvokeCallback(typeof(InfoCb))]
    static void HandleInfo(string line)
    {
        try
        {
            if (_instance != null && _instance._infoQueue != null)
            {
                _instance._infoQueue.Enqueue(line ?? "");
            }
        }
        catch (System.Exception ex)
        {
            // Không thể dùng Debug.Log trong callback từ native thread
            // Chỉ log vào console nếu có thể
            System.Console.Error.WriteLine($"[Pikafish.HandleInfo] Error: {ex.Message}");
        }
    }

    [AOT.MonoPInvokeCallback(typeof(BestMoveCb))]
    static void HandleBestMove(string best, string ponder)
    {
        try
        {
            if (_instance != null && _instance._bmQueue != null)
            {
                _instance._bmQueue.Enqueue((best ?? "", ponder ?? ""));
            }
        }
        catch (System.Exception ex)
        {
            // Không thể dùng Debug.Log trong callback từ native thread
            System.Console.Error.WriteLine($"[Pikafish.HandleBestMove] Error: {ex.Message}");
        }
    }

    void Update()
    {
        while (_infoQueue.TryDequeue(out var line)) OnInfo?.Invoke(line);
        while (_bmQueue.TryDequeue(out var bm)) OnBestMove?.Invoke(bm.best, bm.ponder);
    }
}


