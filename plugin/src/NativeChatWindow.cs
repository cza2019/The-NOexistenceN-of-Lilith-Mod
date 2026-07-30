using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace LilithMod;

internal sealed class NativeChatWindow : IDisposable
{
    private const int CwUseDefault = unchecked((int)0x80000000);
    private const int WsOverlappedWindow = 0x00CF0000;
    private const int WsChild = 0x40000000;
    private const int WsVisible = 0x10000000;
    private const int WsBorder = 0x00800000;
    private const int WsExClientEdge = 0x00000200;
    private const int WsVScroll = 0x00200000;
    private const int WsTabStop = 0x00010000;
    private const int WsExAppWindow = 0x00040000;
    private const int EsMultiline = 0x0004;
    private const int EsReadOnly = 0x0800;
    private const int EsAutovscroll = 0x0040;
    private const int EsWantReturn = 0x1000;
    private const int EsLeft = 0x0000;
    private const int SwHide = 0;
    private const int SwShownormal = 1;
    private const uint WmCommand = 0x0111;
    private const uint WmKeyDown = 0x0100;
    private const uint WmChar = 0x0102;
    private const uint WmClose = 0x0010;
    private const uint WmDestroy = 0x0002;
    private const uint WmSetfont = 0x0030;
    private const uint WmQuit = 0x0012;
    private const uint WmAppShow = 0x8001;
    private const uint WmAppHide = 0x8002;
    private const uint WmAppRenderHistory = 0x8003;
    private const uint WmAppClearHistory = 0x8004;
    private const uint WmAppScrollHistory = 0x8005;
    private const uint WmVscroll = 0x0115;
    private const uint EmSetSel = 0x00B1;
    private const uint EmLineScroll = 0x00B6;
    private const uint EmScrollCaret = 0x00B7;
    private const int SbBottom = 7;
    private const int ButtonId = 1001;
    private const int ConfigButtonId = 1003;
    private const int ResetMemoryButtonId = 1004;
    private const int ToggleHistoryButtonId = 1005;
    private const int EditId = 1002;
    private const int ResetSessionMenuId = 2001;
    private const int ForgetPlayerMenuId = 2002;
    private const int GwlpWndProc = -4;
    private const int VkReturn = 0x0D;

    private readonly Action<string> _onSubmit;
    private readonly Action _onConfigure;
    private readonly Action _onResetSession;
    private readonly Action _onForgetPlayer;
    private readonly object _sync = new();
    private readonly AutoResetEvent _ready = new(false);
    private readonly ConcurrentQueue<string> _pendingHistory = new();
    private readonly StringBuilder _historyText = new();
    private readonly WndProc _wndProc;
    private readonly WndProc _editWndProc;
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _window;
    private IntPtr _history;
    private IntPtr _edit;
    private IntPtr _sendButton;
    private IntPtr _configButton;
    private IntPtr _resetMemoryButton;
    private IntPtr _toggleHistoryButton;
    private IntPtr _originalEditWndProc;
    private bool _historyVisible;
    private bool _disposed;

    public NativeChatWindow(
        Action<string> onSubmit,
        Action onConfigure,
        Action onResetSession,
        Action onForgetPlayer)
    {
        _onSubmit = onSubmit;
        _onConfigure = onConfigure;
        _onResetSession = onResetSession;
        _onForgetPlayer = onForgetPlayer;
        _wndProc = WindowProc;
        _editWndProc = EditWindowProc;
    }

    public void Show()
    {
        LilithModPlugin.PluginLog.LogInfo("Native chat Show requested");
        lock (_sync)
        {
            if (_disposed) return;
            if (_thread is null)
            {
                _thread = new Thread(WindowThread)
                {
                    IsBackground = true,
                    Name = "LilithChatWindow"
                };
                _thread.Start();
            }
        }
        _ready.WaitOne(3000);
        if (_window != IntPtr.Zero)
        {
            LilithModPlugin.PluginLog.LogInfo($"Native chat window HWND: 0x{_window.ToInt64():X}");
            PostMessage(_window, WmAppShow, IntPtr.Zero, IntPtr.Zero);
        }
        else
        {
            LilithModPlugin.PluginLog.LogWarning("Native chat window handle was not created");
        }
    }

    public void Hide()
    {
        if (_window != IntPtr.Zero)
        {
            PostMessage(_window, WmAppHide, IntPtr.Zero, IntPtr.Zero);
        }
    }

    public void AppendMessage(
        string speaker,
        string text,
        IReadOnlyList<ChatCitation>? citations = null)
    {
        string value = text.Trim();
        if (value.Length == 0)
        {
            return;
        }
        var entry = new StringBuilder($"{speaker}：{value}");
        if (citations is not null)
        {
            for (int index = 0; index < Math.Min(5, citations.Count); index++)
            {
                ChatCitation citation = citations[index];
                entry.Append("\r\n");
                entry.Append($"[{index + 1}] {citation.Title}\r\n{citation.Url}");
            }
        }
        _pendingHistory.Enqueue(entry.ToString());
        if (_window != IntPtr.Zero)
        {
            PostMessage(_window, WmAppRenderHistory, IntPtr.Zero, IntPtr.Zero);
        }
    }

    public void ClearHistory()
    {
        if (_window != IntPtr.Zero)
        {
            PostMessage(_window, WmAppClearHistory, IntPtr.Zero, IntPtr.Zero);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
        }
        if (_threadId != 0)
        {
            PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        }
        if (_thread is { IsAlive: true })
        {
            _thread.Join(1500);
        }
        _ready.Dispose();
    }

    private void WindowThread()
    {
        try
        {
            WindowThreadCore();
        }
        catch (Exception error)
        {
            LilithModPlugin.PluginLog.LogError($"Native chat window failed: {error}");
            _ready.Set();
        }
    }

    private void WindowThreadCore()
    {
        _threadId = GetCurrentThreadId();
        string className = $"LilithModChat_{Environment.ProcessId}";
        IntPtr instance = GetModuleHandle(null);
        var windowClass = new WndClassEx
        {
            cbSize = (uint)Marshal.SizeOf<WndClassEx>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = instance,
            lpszClassName = className,
            hCursor = LoadCursor(IntPtr.Zero,  IDC_ARROW),
            hbrBackground = (IntPtr)(COLOR_WINDOW + 1)
        };
        if (RegisterClassEx(ref windowClass) == 0 && Marshal.GetLastWin32Error() != 1410)
        {
            throw new InvalidOperationException($"RegisterClassEx failed: {Marshal.GetLastWin32Error()}");
        }

        _window = CreateWindowEx(
            WsExAppWindow,
            className,
            "Lilith Chat",
            WsOverlappedWindow,
            CwUseDefault,
            CwUseDefault,
            600,
            150,
            IntPtr.Zero,
            IntPtr.Zero,
            instance,
            IntPtr.Zero);
        if (_window == IntPtr.Zero)
        {
            LilithModPlugin.PluginLog.LogWarning($"CreateWindowEx failed: {Marshal.GetLastWin32Error()}");
            _ready.Set();
            return;
        }

        _history = CreateWindowEx(
            WsExClientEdge,
            "EDIT",
            "",
            WsChild | WsVScroll | EsMultiline | EsAutovscroll | EsReadOnly | EsLeft,
            18,
            18,
            544,
            300,
            _window,
            IntPtr.Zero,
            instance,
            IntPtr.Zero);
        _edit = CreateWindowEx(
            WsExClientEdge,
            "EDIT",
            "",
            WsChild | WsVisible | WsVScroll | WsTabStop | EsMultiline | EsAutovscroll | EsWantReturn | EsLeft,
            18,
            18,
            350,
            70,
            _window,
            (IntPtr)EditId,
            instance,
            IntPtr.Zero);
        _originalEditWndProc = SetWindowLongPtr(
            _edit,
            GwlpWndProc,
            Marshal.GetFunctionPointerForDelegate(_editWndProc));
        if (_originalEditWndProc == IntPtr.Zero)
        {
            LilithModPlugin.PluginLog.LogWarning($"Could not subclass native chat edit control: {Marshal.GetLastWin32Error()}");
        }
        LilithModPlugin.PluginLog.LogInfo($"Native chat edit HWND: 0x{_edit.ToInt64():X}");
        _sendButton = CreateWindowEx(
            0,
            "BUTTON",
            ModLocalization.T("chat.send"),
            WsChild | WsVisible | WsTabStop,
            374,
            18,
            82,
            32,
            _window,
            (IntPtr)ButtonId,
            instance,
            IntPtr.Zero);
        _configButton = CreateWindowEx(
            0,
            "BUTTON",
            ModLocalization.T("menu.mod_settings"),
            WsChild | WsVisible | WsTabStop,
            464,
            18,
            98,
            32,
            _window,
            (IntPtr)ConfigButtonId,
            instance,
            IntPtr.Zero);
        _resetMemoryButton = CreateWindowEx(
            0,
            "BUTTON",
            ModLocalization.T("chat.forget_player"),
            WsChild | WsVisible | WsTabStop,
            464,
            56,
            98,
            32,
            _window,
            (IntPtr)ResetMemoryButtonId,
            instance,
            IntPtr.Zero);
        _toggleHistoryButton = CreateWindowEx(
            0,
            "BUTTON",
            ModLocalization.T("chat.show_history"),
            WsChild | WsVisible | WsTabStop,
            374,
            56,
            82,
            32,
            _window,
            (IntPtr)ToggleHistoryButtonId,
            instance,
            IntPtr.Zero);
        IntPtr font = GetStockObject(DefaultGuiFont);
        SendMessage(_history, WmSetfont, font, new IntPtr(1));
        SendMessage(_edit, WmSetfont, font, new IntPtr(1));
        SendMessage(_sendButton, WmSetfont, font, new IntPtr(1));
        SendMessage(_configButton, WmSetfont, font, new IntPtr(1));
        SendMessage(_resetMemoryButton, WmSetfont, font, new IntPtr(1));
        SendMessage(_toggleHistoryButton, WmSetfont, font, new IntPtr(1));
        ShowWindow(_window, SwShownormal);
        SetForegroundWindow(_window);
        SetActiveWindow(_window);
        SetFocus(_edit);
        RenderHistory();
        _ready.Set();

        var message = new Message();
        while (GetMessage(ref message, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }
        if (_window != IntPtr.Zero)
        {
            if (_edit != IntPtr.Zero && _originalEditWndProc != IntPtr.Zero)
            {
                SetWindowLongPtr(_edit, GwlpWndProc, _originalEditWndProc);
            }
            DestroyWindow(_window);
            _window = IntPtr.Zero;
        }
    }

    private IntPtr WindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WmAppShow)
        {
            ShowWindow(window, SwShownormal);
            SetForegroundWindow(window);
            SetActiveWindow(window);
            SetFocus(_edit);
            return IntPtr.Zero;
        }
        if (message == WmAppHide)
        {
            ShowWindow(window, SwHide);
            return IntPtr.Zero;
        }
        if (message == WmAppRenderHistory)
        {
            RenderHistory();
            return IntPtr.Zero;
        }
        if (message == WmAppClearHistory)
        {
            while (_pendingHistory.TryDequeue(out _)) { }
            _historyText.Clear();
            if (_history != IntPtr.Zero)
            {
                SetWindowText(_history, "");
            }
            return IntPtr.Zero;
        }
        if (message == WmAppScrollHistory)
        {
            ScrollHistoryToBottom();
            return IntPtr.Zero;
        }
        if (message == WmCommand && (wParam.ToInt64() & 0xffff) == ConfigButtonId)
        {
            _onConfigure();
            return IntPtr.Zero;
        }
        if (message == WmCommand && (wParam.ToInt64() & 0xffff) == ResetMemoryButtonId)
        {
            ShowMemoryMenu(window);
            return IntPtr.Zero;
        }
        if (message == WmCommand && (wParam.ToInt64() & 0xffff) == ToggleHistoryButtonId)
        {
            SetHistoryVisible(!_historyVisible);
            return IntPtr.Zero;
        }
        if (message == WmCommand && (wParam.ToInt64() & 0xffff) == ButtonId)
        {
            int length = GetWindowTextLength(_edit);
            var builder = new StringBuilder(length + 1);
            GetWindowText(_edit, builder, builder.Capacity);
            string text = builder.ToString().Trim();
            if (text.Length > 0)
            {
                SetWindowText(_edit, "");
        AppendMessage(ModLocalization.T("common.you"), text);
                _onSubmit(text);
            }
            return IntPtr.Zero;
        }
        if (message == WmClose)
        {
            ShowWindow(window, SwHide);
            return IntPtr.Zero;
        }
        if (message == WmDestroy)
        {
            PostQuitMessage(0);
            return IntPtr.Zero;
        }
        return DefWindowProc(window, message, wParam, lParam);
    }

    private IntPtr EditWindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        if ((message == WmKeyDown || message == WmChar)
            && wParam.ToInt32() == VkReturn
            && !IsImeComposing(window))
        {
            if (message == WmKeyDown && (lParam.ToInt64() & (1L << 30)) == 0)
            {
                PostMessage(_window, WmCommand, (IntPtr)ButtonId, _edit);
            }
            return IntPtr.Zero;
        }
        return _originalEditWndProc == IntPtr.Zero
            ? DefWindowProc(window, message, wParam, lParam)
            : CallWindowProc(_originalEditWndProc, window, message, wParam, lParam);
    }

    private static bool IsImeComposing(IntPtr window)
    {
        IntPtr inputContext = ImmGetContext(window);
        if (inputContext == IntPtr.Zero)
        {
            return false;
        }
        try
        {
            return ImmGetCompositionString(inputContext, GcsCompStr, IntPtr.Zero, 0) > 0;
        }
        finally
        {
            ImmReleaseContext(window, inputContext);
        }
    }

    private void ShowMemoryMenu(IntPtr owner)
    {
        IntPtr menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }
        try
        {
        AppendMenu(menu, MfString, ResetSessionMenuId, ModLocalization.T("chat.reset_session"));
        AppendMenu(menu, MfString, ForgetPlayerMenuId, ModLocalization.T("chat.forget_player"));
            GetWindowRect(_resetMemoryButton, out NativeRect buttonRect);
            int choice = TrackPopupMenu(
                menu,
                TpmReturnCmd | TpmRightButton,
                buttonRect.Left,
                buttonRect.Bottom,
                0,
                owner,
                IntPtr.Zero);
            if (choice == ResetSessionMenuId)
            {
                _onResetSession();
            }
            else if (choice == ForgetPlayerMenuId)
            {
                int confirmed = MessageBox(
                    owner,
                ModLocalization.T("chat.memory_warning"),
                ModLocalization.T("chat.memory_warning_title"),
                    MbYesNo | MbIconWarning | MbDefButton2);
                if (confirmed == IdYes)
                {
                    _onForgetPlayer();
                }
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private void SetHistoryVisible(bool visible)
    {
        _historyVisible = visible;
        ShowWindow(_history, visible ? SwShow : SwHide);
        int inputY = visible ? 330 : 18;
        MoveWindow(_edit, 18, inputY, 350, 70, true);
        MoveWindow(_sendButton, 374, inputY, 82, 32, true);
        MoveWindow(_configButton, 464, inputY, 98, 32, true);
        MoveWindow(_toggleHistoryButton, 374, inputY + 38, 82, 32, true);
        MoveWindow(_resetMemoryButton, 464, inputY + 38, 98, 32, true);
        SetWindowText(_toggleHistoryButton, visible
            ? ModLocalization.T("chat.hide_history_native")
            : ModLocalization.T("chat.show_history"));
        SetWindowPos(
            _window,
            IntPtr.Zero,
            0,
            0,
            600,
            visible ? 470 : 150,
            SwpNoMove | SwpNoZOrder | SwpNoActivate);
        if (visible)
        {
            ScrollHistoryToBottom();
            PostMessage(_window, WmAppScrollHistory, IntPtr.Zero, IntPtr.Zero);
        }
        SetFocus(_edit);
    }

    private void RenderHistory()
    {
        while (_pendingHistory.TryDequeue(out string? line))
        {
            if (_historyText.Length > 0)
            {
                _historyText.Append("\r\n\r\n");
            }
            _historyText.Append(line);
        }
        if (_historyText.Length > 16000)
        {
            _historyText.Remove(0, _historyText.Length - 12000);
        }
        if (_history != IntPtr.Zero)
        {
            SetWindowText(_history, _historyText.ToString());
            ScrollHistoryToBottom();
        }
    }

    private void ScrollHistoryToBottom()
    {
        if (_history == IntPtr.Zero)
        {
            return;
        }
        SendMessage(_history, EmSetSel, new IntPtr(-1), new IntPtr(-1));
        SendMessage(_history, EmScrollCaret, IntPtr.Zero, IntPtr.Zero);
        SendMessage(_history, EmLineScroll, IntPtr.Zero, new IntPtr(int.MaxValue));
        SendMessage(_history, WmVscroll, new IntPtr(SbBottom), IntPtr.Zero);
    }

    private delegate IntPtr WndProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int pointX;
        public int pointY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private const int ColorWindow = 5;
    private const int DefaultGuiFont = 17;
    private const int IdfArrow = 32512;
    private const int IdYes = 6;
    private const int SwShow = 5;
    private const uint MbYesNo = 0x00000004;
    private const uint MbIconWarning = 0x00000030;
    private const uint MbDefButton2 = 0x00000100;
    private const uint MfString = 0x00000000;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCmd = 0x0100;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint GcsCompStr = 0x0008;
    private static readonly IntPtr COLOR_WINDOW = (IntPtr)ColorWindow;
    private static readonly IntPtr IDC_ARROW = (IntPtr)IdfArrow;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WndClassEx windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr window, string text, string caption, uint type);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(
        IntPtr previousWindowProc,
        IntPtr window,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr newValue);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int GetMessage(ref Message message, IntPtr window, uint minFilter, uint maxFilter);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Message message);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref Message message);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern bool MoveWindow(
        IntPtr window,
        int x,
        int y,
        int width,
        int height,
        bool repaint);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr menu, uint flags, int itemId, string text);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(
        IntPtr menu,
        uint flags,
        int x,
        int y,
        int reserved,
        IntPtr window,
        IntPtr rect);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr SetActiveWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetWindowText(IntPtr window, string text);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadCursor(IntPtr instance, IntPtr cursor);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("gdi32.dll")]
    private static extern IntPtr GetStockObject(int objectId);

    [DllImport("imm32.dll")]
    private static extern IntPtr ImmGetContext(IntPtr window);

    [DllImport("imm32.dll")]
    private static extern bool ImmReleaseContext(IntPtr window, IntPtr inputContext);

    [DllImport("imm32.dll", EntryPoint = "ImmGetCompositionStringW")]
    private static extern int ImmGetCompositionString(
        IntPtr inputContext,
        uint index,
        IntPtr buffer,
        uint bufferLength);
}
