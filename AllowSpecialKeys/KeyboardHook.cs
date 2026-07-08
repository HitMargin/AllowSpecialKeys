using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace AllowSpecialKeys;

/// <summary>
/// Low-level keyboard hook (WH_KEYBOARD_LL) that intercepts keys
/// BEFORE Windows processes them, so we can block system-wide shortcuts
/// like Win key, Alt+Tab, Ctrl+Esc, etc.
///
/// Key insight: WH_KEYBOARD_LL runs in a global hook chain (LIFO).
/// Our hook gets called before Windows processes the key.
/// Returning a nonzero value (1) prevents Windows from ever seeing
/// the key — which also means Windows never updates the global async
/// key-state table for that key, so GetAsyncKeyState/GetKeyState will
/// NEVER report it as pressed anywhere else in the process (including
/// in Unity/the game itself). Because we are the ones swallowing the
/// key, we are also the only ones who ever get to see it — so we must
/// record its physical down/up state ourselves, here, and expose it
/// to the rest of the mod instead of relying on GetAsyncKeyState.
/// </summary>
public class KeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;

    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int WM_QUIT = 0x0012;

    // Virtual key codes we may block
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_LMENU = 0xA4;   // Left Alt
    private const int VK_RMENU = 0xA5;   // Right Alt
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_TAB = 0x09;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_RETURN = 0x0D;
    private const int VK_F4 = 0x73;

    private bool _running;
    private Thread _hookThread;
    private IntPtr _hookId = IntPtr.Zero;
    private LowLevelKeyboardProc _hookProc;
    private uint _threadId;

    // The set of VK codes to block
    private readonly HashSet<int> _blockedKeys = new();
    // Additional constraints: Alt+Tab detection uses state tracking
    private bool _altHeld;
    private bool _ctrlHeld;

    // Settings reference
    private Settings _settings;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    private static readonly IntPtr ModuleHandle;

    // ======================= Physical key-state tracking =======================
    // Updated directly inside the hook callback for EVERY key event we see,
    // regardless of whether we go on to block that event or not. This is the
    // only reliable source of "is this key physically down" once we start
    // swallowing WH_KEYBOARD_LL events for it, because GetAsyncKeyState will
    // never see a key we've blocked.
    private static readonly bool[] _keyPhysicallyDown = new bool[256];

    /// <summary>
    /// Returns whether the given virtual-key code is currently physically
    /// held down, as observed directly by this hook. Safe to call from any
    /// thread (including Unity's main thread) — reads are volatile.
    /// </summary>
    public static bool IsKeyPhysicallyDown(int vkCode)
    {
        if (vkCode < 0 || vkCode >= _keyPhysicallyDown.Length) return false;
        return Volatile.Read(ref _keyPhysicallyDown[vkCode]);
    }

    static KeyboardHook()
    {
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        ModuleHandle = GetModuleHandle(curModule.ModuleName);
    }

    public KeyboardHook()
    {
    }

    public void UpdateBlockedKeys(Settings settings)
    {
        _settings = settings;
        RebuildBlockedSet();
    }

    public void Start()
    {
        if (_running) return;
        _running = true;

        _hookThread = new Thread(HookThreadProc)
        {
            Name = "AllowSpecialKeys Hook",
            IsBackground = true
        };
        _hookThread.Start();
    }

    public void Stop()
    {
        _running = false;
        if (_hookId != IntPtr.Zero)
        {
            PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            _hookId = IntPtr.Zero;
        }
        _hookThread?.Join(2000);
        _hookThread = null;

        // Reset tracked state so no key gets stuck "down" after the hook stops.
        for (int i = 0; i < _keyPhysicallyDown.Length; i++)
            Volatile.Write(ref _keyPhysicallyDown[i], false);
    }

    public void Dispose()
    {
        Stop();
    }

    private void RebuildBlockedSet()
    {
        _blockedKeys.Clear();

        if (_settings == null) return;

        if (_settings.BlockWinKey)
        {
            _blockedKeys.Add(VK_LWIN);
            _blockedKeys.Add(VK_RWIN);
        }

        // Alt+Tab and Alt+F4 blocking is done via state tracking
        // Ctrl+Esc is tracked via state
    }

    private void HookThreadProc()
    {
        _threadId = GetCurrentThreadId();
        _hookProc = HookCallback;

        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, ModuleHandle, 0);

        if (_hookId == IntPtr.Zero)
            return;

        while (_running && GetMessage(out var msg, IntPtr.Zero, 0, 0) != 0)
        {
        }

        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            bool isDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
            bool isUp = msg == WM_KEYUP || msg == WM_SYSKEYUP;

            if (isDown || isUp)
            {
                var khs = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                int vkCode = (int)khs.vkCode;

                // Record physical state FIRST, before any decision to block —
                // this is the one and only place this key's state is ever
                // observable once we swallow it below.
                if (vkCode >= 0 && vkCode < _keyPhysicallyDown.Length)
                    Volatile.Write(ref _keyPhysicallyDown[vkCode], isDown);

                // Track modifier state
                if (vkCode == VK_LMENU || vkCode == VK_RMENU)
                    _altHeld = isDown;
                if (vkCode == VK_LCONTROL || vkCode == VK_RCONTROL)
                    _ctrlHeld = isDown;

                // Check if this key should be blocked
                if (ShouldBlock(vkCode, isDown))
                {
                    return (IntPtr)1; // Block! Windows never sees this key
                }
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private bool ShouldBlock(int vkCode, bool isDown)
    {
        if (_settings == null) return false;

        // ============ Block Win key ============
        if (_settings.BlockWinKey && (vkCode == VK_LWIN || vkCode == VK_RWIN))
            return true;

        // ============ Block Alt+Tab / Alt+F4 / Alt+Esc ============
        // NOTE: We do NOT block standalone Alt presses — that would prevent
        // Unity from seeing the key (it uses window messages, not Raw Input).
        // We only block Alt combinations that trigger system shortcuts.
        if (_settings.BlockAltTab && _altHeld)
        {
            // Block Tab while Alt held → prevents Alt+Tab
            if (vkCode == VK_TAB)
                return true;
            // Block F4 while Alt held → prevents Alt+F4
            if (vkCode == VK_F4)
                return true;
            // Block Escape while Alt held → prevents Alt+Esc
            if (vkCode == VK_ESCAPE)
                return true;
            // Block Enter while Alt held → prevents Alt+Enter (fullscreen toggle)
            if (_settings.BlockAltEnter && vkCode == VK_RETURN)
                return true;
            // Block Alt release to prevent system menu activation on Alt+<any>
            // but ONLY if other keys were pressed during Alt hold (handled above)
            // We don't block standalone Alt release here
        }

        // ============ Block Ctrl+Esc ============
        if (_settings.BlockCtrlEsc && _ctrlHeld && vkCode == VK_ESCAPE)
            return true;

        return false;
    }

    #region P/Invoke

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public KBDLLHOOKSTRUCTFlags flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [Flags]
    private enum KBDLLHOOKSTRUCTFlags : uint
    {
        LLKHF_EXTENDED = 0x01,
        LLKHF_INJECTED = 0x10,
        LLKHF_ALTDOWN = 0x20,
        LLKHF_UP = 0x80,
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

    #endregion
}