using System.Runtime.InteropServices;
using KeyRemapper.Models;

namespace KeyRemapper.Services;

/// <summary>
/// 全局低级键盘钩子（WH_KEYBOARD_LL）：拦截物理按键，按映射表用 SendInput 注入替换按键。
/// 注入的事件带 LLKHF_INJECTED 标记，会被本钩子直接放行，因此不会形成死循环；
/// 同时也意味着只有物理键盘的输入会被映射。
/// </summary>
public sealed class KeyboardHookService : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const uint WM_QUIT = 0x0012;
    private const uint LLKHF_INJECTED_MASK = 0x10 | 0x02; // LLKHF_INJECTED | LLKHF_LOWER_IL_INJECTED
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint INPUT_KEYBOARD = 1;

    private readonly LowLevelKeyboardProc _proc;
    private readonly object _gate = new();
    private Dictionary<int, int> _mappings = new();
    private readonly Dictionary<int, int> _held = new(); // 物理键 -> 已注入的按下键（保证按下/抬起配对）
    private IntPtr _hook;
    private Thread? _thread;
    private uint _threadId;
    private volatile bool _running;
    private volatile bool _paused;

    public KeyboardHookService() => _proc = HookProc;

    public bool IsRunning { get; private set; }

    public void SetMappings(IEnumerable<KeyMapping> mappings)
    {
        lock (_gate)
        {
            var table = new Dictionary<int, int>();
            foreach (var m in mappings)
            {
                int from = KeyNames.Normalize(m.FromVk);
                int to = KeyNames.Normalize(m.ToVk);
                if (from != 0 && to != 0 && from != to) table[from] = to;
            }
            _mappings = table;
        }
    }

    /// <summary>暂停映射（例如弹窗捕获按键时），已注入但未抬起的键会被补上抬起事件。</summary>
    public void SetPaused(bool paused)
    {
        lock (_gate)
        {
            _paused = paused;
            if (paused) ReleaseHeld();
        }
    }

    public void Start()
    {
        if (IsRunning) return;
        lock (_gate) ReleaseHeld();

        _running = true;
        _thread = new Thread(HookThread) { IsBackground = true, Name = "KeyRemapper-Hook" };
        _thread.Start();

        // 等钩子线程完成安装，UI 才能得到准确的运行状态
        for (int i = 0; i < 300 && _hook == IntPtr.Zero && _running; i++) Thread.Sleep(10);
        IsRunning = _hook != IntPtr.Zero;
    }

    public void Stop()
    {
        if (!IsRunning && _thread == null) return;
        _running = false;
        if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread?.Join(3000);
        _thread = null;
        _threadId = 0;
        lock (_gate) ReleaseHeld();
        IsRunning = false;
    }

    public void Dispose() => Stop();

    // 调用方需持有 _gate。补发已注入键的抬起事件，避免目标键卡在按下状态。
    private void ReleaseHeld()
    {
        foreach (var target in _held.Values) SendKey(target, down: false);
        _held.Clear();
    }

    private void HookThread()
    {
        try
        {
            _threadId = GetCurrentThreadId();
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero)
            {
                _running = false;
                return;
            }

            while (_running && GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }

            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
        catch
        {
            // 后台线程兜底，不让异常带崩整个进程
        }
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            if (msg is WM_KEYDOWN or WM_KEYUP or WM_SYSKEYDOWN or WM_SYSKEYUP)
            {
                var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                if ((info.flags & LLKHF_INJECTED_MASK) == 0 && !_paused)
                {
                    bool down = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
                    int physical = KeyNames.Normalize(unchecked((int)info.vkCode));

                    lock (_gate)
                    {
                        if (down)
                        {
                            // 已按住（自动重复）：继续重复注入目标键
                            if (_held.TryGetValue(physical, out int heldTarget))
                            {
                                SendKey(heldTarget, down: true);
                                return (IntPtr)1;
                            }
                            if (_mappings.TryGetValue(physical, out int target))
                            {
                                _held[physical] = target;
                                SendKey(target, down: true);
                                return (IntPtr)1;
                            }
                        }
                        else if (_held.Remove(physical, out int target))
                        {
                            SendKey(target, down: false);
                            return (IntPtr)1;
                        }
                    }
                }
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private static void SendKey(int vk, bool down)
    {
        uint flags = (down ? 0u : KEYEVENTF_KEYUP) | (IsExtended(vk) ? KEYEVENTF_EXTENDEDKEY : 0u);
        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new INPUTUNION
            {
                ki = new KEYBDINPUT { wVk = unchecked((ushort)vk), dwFlags = flags },
            },
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    private static bool IsExtended(int vk) =>
        vk is >= 0x21 and <= 0x28 or 0x2D or 0x2E; // 翻页/方向/Insert/Delete 等扩展键

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION u;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
}
