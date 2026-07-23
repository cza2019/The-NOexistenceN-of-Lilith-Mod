using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace LilithMod;

internal interface IChatFocusController : IDisposable
{
    bool HasKeyboardFocus { get; }

    bool Acquire();

    void Release();
}

internal static class ChatFocusController
{
    public static IChatFocusController Create()
    {
        return OperatingSystem.IsWindows()
            ? new WindowsChatFocusController()
            : new NoopChatFocusController();
    }

    private sealed class NoopChatFocusController : IChatFocusController
    {
        public bool HasKeyboardFocus => true;

        public bool Acquire() => true;

        public void Release()
        {
        }

        public void Dispose()
        {
        }
    }
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsChatFocusController : IChatFocusController
{
    private const int GwlExStyle = -20;
    private const long WsExNoActivate = 0x08000000L;

    private IntPtr _window;
    private IntPtr _previousForeground;
    private bool _restoreNoActivate;

    public bool HasKeyboardFocus
    {
        get
        {
            IntPtr target = _window != IntPtr.Zero ? _window : FindUnityWindow();
            return target != IntPtr.Zero && GetForegroundWindow() == target;
        }
    }

    public bool Acquire()
    {
        IntPtr target = FindUnityWindow();
        if (target == IntPtr.Zero)
        {
            LilithModPlugin.PluginLog.LogWarning("Could not find the Unity desktop window for chat keyboard focus");
            return false;
        }

        if (_window == IntPtr.Zero)
        {
            _previousForeground = GetForegroundWindow();
        }
        _window = target;
        long style = GetWindowLongPtr(_window, GwlExStyle).ToInt64();
        bool noActivatePresent = (style & WsExNoActivate) != 0;
        _restoreNoActivate |= noActivatePresent;
        if (noActivatePresent)
        {
            SetWindowLongPtr(_window, GwlExStyle, new IntPtr(style & ~WsExNoActivate));
        }

        bool foreground = RequestForeground(_window);
        LilithModPlugin.PluginLog.LogInfo(
            $"Unity chat keyboard focus requested: hwnd=0x{_window.ToInt64():X}; " +
            $"noActivateCleared={_restoreNoActivate}; foreground={foreground}");
        return foreground;
    }

    private static bool RequestForeground(IntPtr target)
    {
        IntPtr foregroundWindow = GetForegroundWindow();
        uint targetThread = GetWindowThreadProcessId(target, out _);
        uint foregroundThread = foregroundWindow == IntPtr.Zero
            ? 0
            : GetWindowThreadProcessId(foregroundWindow, out _);
        bool attached = targetThread != 0
            && foregroundThread != 0
            && targetThread != foregroundThread
            && AttachThreadInput(targetThread, foregroundThread, true);
        try
        {
            BringWindowToTop(target);
            SetActiveWindow(target);
            SetFocus(target);
            SetForegroundWindow(target);
            return GetForegroundWindow() == target;
        }
        finally
        {
            if (attached)
            {
                AttachThreadInput(targetThread, foregroundThread, false);
            }
        }
    }

    public void Release()
    {
        if (_window == IntPtr.Zero)
        {
            return;
        }
        if (_restoreNoActivate)
        {
            long style = GetWindowLongPtr(_window, GwlExStyle).ToInt64();
            SetWindowLongPtr(_window, GwlExStyle, new IntPtr(style | WsExNoActivate));
        }
        if (_previousForeground != IntPtr.Zero
            && _previousForeground != _window
            && IsWindow(_previousForeground))
        {
            SetForegroundWindow(_previousForeground);
        }
        _window = IntPtr.Zero;
        _previousForeground = IntPtr.Zero;
        _restoreNoActivate = false;
    }

    public void Dispose() => Release();

    private static IntPtr FindUnityWindow()
    {
        uint processId = (uint)Environment.ProcessId;
        IntPtr best = IntPtr.Zero;
        int bestScore = -1;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out uint owner);
            if (owner != processId || !IsWindowVisible(window))
            {
                return true;
            }

            string className = WindowClass(window);
            string title = WindowTitle(window);
            int score = 0;
            if (className.Contains("UnityWndClass", StringComparison.OrdinalIgnoreCase))
            {
                score += 100;
            }
            if (title.Contains("NOexistenceN", StringComparison.OrdinalIgnoreCase)
                || title.Contains("Lilith", StringComparison.OrdinalIgnoreCase))
            {
                score += 30;
            }
            if (className.Contains("ConsoleWindowClass", StringComparison.OrdinalIgnoreCase)
                || title.Contains("BepInEx", StringComparison.OrdinalIgnoreCase))
            {
                score -= 200;
            }
            if (score > bestScore)
            {
                bestScore = score;
                best = window;
            }
            return true;
        }, IntPtr.Zero);
        return bestScore > 0 ? best : Process.GetCurrentProcess().MainWindowHandle;
    }

    private static string WindowClass(IntPtr window)
    {
        var builder = new StringBuilder(256);
        GetClassName(window, builder, builder.Capacity);
        return builder.ToString();
    }

    private static string WindowTitle(IntPtr window)
    {
        var builder = new StringBuilder(512);
        GetWindowText(window, builder, builder.Capacity);
        return builder.ToString();
    }

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint attach, uint attachTo, bool attachInput);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder className, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder title, int maxCount);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr window, int index, IntPtr value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong32(IntPtr window, int index, int value);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr SetActiveWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr window);

    private static IntPtr GetWindowLongPtr(IntPtr window, int index)
    {
        return IntPtr.Size == 8
            ? GetWindowLongPtr64(window, index)
            : new IntPtr(GetWindowLong32(window, index));
    }

    private static IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value)
    {
        return IntPtr.Size == 8
            ? SetWindowLongPtr64(window, index, value)
            : new IntPtr(SetWindowLong32(window, index, value.ToInt32()));
    }
}
