using System;
using System.Runtime.InteropServices;
using System.Text;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace LilithMod;

internal static class UserInterfacePatches
{
    [ThreadStatic]
    private static bool _rightClickChatRequest;
    [ThreadStatic]
    private static bool _companionDialogueScope;
    private static DialogueBubbleUI? _autonomousDialogueBubble;
    private static DialogueBubbleUI? _cachedDialogueBubble;
    private static float _nextDialogueBubbleLookup;
    private static string _autonomousDialogueLine = string.Empty;
    private static Il2CppSystem.Action<int>? _chatSelection;
    private static bool _rightButtonWasDown;
    private static bool _leftButtonWasDown;
    private static bool _f8WasDown;
    private static bool _mouseOverCharacter;
    private static string _companionDialogueText = string.Empty;
    private static float _companionDialogueUntil;
    private static float _lastNativeBubbleClickTime = -10f;
    private static string _lastDeliveredBubbleLine = string.Empty;
    private static float _lastDeliveredBubbleTime = -10f;
    private static string _recentVisibleBubbleLine = string.Empty;
    private static float _recentVisibleBubbleTime = -10f;
    private static float _recentBubbleMinX;
    private static float _recentBubbleMaxX;
    private static float _recentBubbleMinY;
    private static float _recentBubbleMaxY;
    private static bool _hasRecentBubbleBounds;
    private static string _latestNoteLine = string.Empty;
    private static int _lastMenuFrame = -1;
    private static bool _pollingLogged;

    private const int VkLbutton = 0x01;
    private const int VkRbutton = 0x02;
    private const int VkF8 = 0x77;
    private const int SmXvirtualscreen = 76;
    private const int SmYvirtualscreen = 77;
    private const int SmCxvirtualscreen = 78;
    private const int SmCyvirtualscreen = 79;

    internal static void SetCompanionDialogueScope(bool active)
    {
        _companionDialogueScope = active;
        if (active)
        {
            ClearAutonomousDialogue();
        }
    }

    internal static void MarkCompanionDialogue(string text, float durationSeconds)
    {
        _companionDialogueText = CleanDialogueText(text);
        _companionDialogueUntil = Time.unscaledTime + Math.Max(2f, durationSeconds) + 1f;
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    internal static bool PollChatHotkey()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }
        short keyState = GetAsyncKeyState(VkF8);
        bool isDown = (keyState & 0x8000) != 0;
        bool pressed = (keyState & 0x0001) != 0 || (isDown && !_f8WasDown);
        _f8WasDown = isDown;
        return pressed;
    }

    [HarmonyPatch(typeof(PassThroughMouseHook), nameof(PassThroughMouseHook.SetOverCharacter))]
    private static class CharacterHoverStatePatch
    {
        private static void Prefix(ref bool over)
        {
            over |= TrayIntegration.IsChatPointerOver();
        }

        private static void Postfix(bool over)
        {
            _mouseOverCharacter = over;
        }
    }

    internal static void PollCharacterRightClick()
    {
        if (!_pollingLogged)
        {
            _pollingLogged = true;
            LilithModPlugin.PluginLog.LogInfo("Right-click polling active");
        }

        short keyState = GetAsyncKeyState(VkRbutton);
        bool windowsDown = (keyState & 0x8000) != 0;
        bool unityDown = Input.GetMouseButton(1);
        bool isDown = windowsDown || unityDown;
        bool wasPressedSinceLastFrame = (keyState & 0x0001) != 0
            || Input.GetMouseButtonDown(1);
        bool wasReleased = Input.GetMouseButtonUp(1)
            || (_rightButtonWasDown && !isDown)
            || (!_rightButtonWasDown && !isDown && wasPressedSinceLastFrame);
        _rightButtonWasDown = isDown;
        if (!wasReleased || Time.frameCount == _lastMenuFrame)
        {
            return;
        }
        if (TrayIntegration.IsChatPointerOver())
        {
            return;
        }
        CharacterInteractionHandler handler = CharacterInteractionHandler.instance;
        if (handler is null)
        {
            LilithModPlugin.PluginLog.LogWarning("Character right-click released before interaction handler was ready");
            return;
        }

        bool hitCharacter = _mouseOverCharacter;
        if (!hitCharacter)
        {
            RaycastHit2D hit = handler.RaycastMouse();
            hitCharacter = handler.IsHitCharacter(hit);
        }

        LilithModPlugin.PluginLog.LogInfo(
            $"Character right-click released; overCharacter={_mouseOverCharacter}; hit={hitCharacter}");
        if (!hitCharacter)
        {
            return;
        }
        LilithModPlugin.PluginLog.LogInfo("Character right-click hit detected");
        ShowRightClickChatMenu(handler);
    }

    internal static void PollAutonomousDialogueClick()
    {
        short keyState = GetAsyncKeyState(VkLbutton);
        bool windowsDown = (keyState & 0x8000) != 0;
        bool unityDown = Input.GetMouseButton(0);
        bool isDown = windowsDown || unityDown;
        bool wasPressedSinceLastFrame = (keyState & 0x0001) != 0
            || Input.GetMouseButtonDown(0)
            || (!_leftButtonWasDown && isDown);
        _leftButtonWasDown = isDown;

        DialogueBubbleUI? bubble = GetCachedDialogueBubble();
        string visibleLine = string.Empty;
        string lineSource = "visible";
        bool hasVisibleBubble = bubble is not null
            && TryGetVisibleBubbleLine(bubble, false, out visibleLine, out lineSource);
        if (hasVisibleBubble)
        {
            CacheVisibleBubble(bubble!, visibleLine);
        }

        if (!wasPressedSinceLastFrame || TrayIntegration.IsChatPointerOver())
        {
            return;
        }

        // The game's primary label is normally sufficient. Some short-lived bubble
        // variants render through a child label instead; scan those only on an actual
        // click so the IL2CPP array allocation is not paid every frame.
        if (!hasVisibleBubble && bubble is not null)
        {
            hasVisibleBubble = TryGetVisibleBubbleLine(
                bubble,
                true,
                out visibleLine,
                out lineSource);
            if (hasVisibleBubble)
            {
                CacheVisibleBubble(bubble, visibleLine);
            }
        }
        if (Time.unscaledTime - _lastNativeBubbleClickTime <= 0.25f)
        {
            LilithModPlugin.PluginLog.LogInfo("Dialogue bubble click poll suppressed after native click");
            return;
        }

        Vector3 unityPoint = Input.mousePosition;
        bool hasWindowsPoint = TryGetWindowsCursorUnityPoint(out Vector3 windowsPoint);
        if (!hasVisibleBubble)
        {
            if (TryDeliverRecentBubbleClick(unityPoint, hasWindowsPoint, windowsPoint))
            {
                return;
            }

            if (bubble is null)
            {
                LilithModPlugin.PluginLog.LogInfo("Dialogue bubble click poll: no bubble instance or recent bubble");
                return;
            }
            float alpha = bubble._canvasGroup is null ? -1f : bubble._canvasGroup.alpha;
            bool rootActive = bubble._rootRect?.gameObject.activeInHierarchy ?? false;
            LilithModPlugin.PluginLog.LogInfo(
                $"Dialogue bubble click poll: bubble not visually active; " +
                $"isShowing={bubble._isShowing}; alpha={alpha:0.00}; rootActive={rootActive}");
            return;
        }

        try
        {
            bool unityHit = IsPointInsideBubble(bubble!, unityPoint);
            bool windowsHit = hasWindowsPoint && IsPointInsideBubble(bubble!, windowsPoint);
            bool hit = unityHit || windowsHit;
            bubble!.TryGetBubbleScreenBounds(
                out float minX,
                out float maxX,
                out float minY,
                out float maxY);
            string windowsPointText = hasWindowsPoint
                ? $"({windowsPoint.x:0},{windowsPoint.y:0})"
                : "unavailable";
            LilithModPlugin.PluginLog.LogInfo(
                $"Dialogue bubble click poll: hit={hit}; unityHit={unityHit}; windowsHit={windowsHit}; " +
                $"unity=({unityPoint.x:0},{unityPoint.y:0}); windows={windowsPointText}; " +
                $"bounds=({minX:0},{minY:0})-({maxX:0},{maxY:0})");
            if (hit)
            {
                DeliverBubbleLine(visibleLine, lineSource);
            }
        }
        catch (Exception error)
        {
            LilithModPlugin.PluginLog.LogWarning(
                $"Dialogue bubble hit test failed: {error.GetType().Name}: {error.Message}");
        }
    }

    private static void CacheVisibleBubble(DialogueBubbleUI bubble, string line)
    {
        _recentVisibleBubbleLine = line;
        _recentVisibleBubbleTime = Time.unscaledTime;
        _hasRecentBubbleBounds = bubble.TryGetBubbleScreenBounds(
            out _recentBubbleMinX,
            out _recentBubbleMaxX,
            out _recentBubbleMinY,
            out _recentBubbleMaxY);
    }

    private static bool TryDeliverRecentBubbleClick(
        Vector3 unityPoint,
        bool hasWindowsPoint,
        Vector3 windowsPoint)
    {
        const float recentBubbleGraceSeconds = 0.4f;
        if (!_hasRecentBubbleBounds
            || _recentVisibleBubbleLine.Length == 0
            || Time.unscaledTime - _recentVisibleBubbleTime > recentBubbleGraceSeconds)
        {
            return false;
        }

        bool unityHit = IsPointInsideCachedBubble(unityPoint);
        bool windowsHit = hasWindowsPoint && IsPointInsideCachedBubble(windowsPoint);
        LilithModPlugin.PluginLog.LogInfo(
            $"Dialogue bubble recent-frame fallback: unityHit={unityHit}; windowsHit={windowsHit}; " +
            $"age={Time.unscaledTime - _recentVisibleBubbleTime:0.000}s");
        if (!unityHit && !windowsHit)
        {
            return false;
        }

        DeliverBubbleLine(_recentVisibleBubbleLine, "recent-frame");
        return true;
    }

    private static bool IsPointInsideCachedBubble(Vector3 point)
    {
        const float padding = 12f;
        bool XInside(float x) => x >= _recentBubbleMinX - padding
            && x <= _recentBubbleMaxX + padding;
        bool YInside(float y) => y >= _recentBubbleMinY - padding
            && y <= _recentBubbleMaxY + padding;
        return XInside(point.x) && (YInside(point.y) || YInside(Screen.height - point.y));
    }

    private static bool TryGetWindowsCursorUnityPoint(out Vector3 point)
    {
        point = Vector3.zero;
        if (!OperatingSystem.IsWindows() || !GetCursorPos(out NativePoint cursor))
        {
            return false;
        }

        int virtualX = GetSystemMetrics(SmXvirtualscreen);
        int virtualY = GetSystemMetrics(SmYvirtualscreen);
        int virtualWidth = GetSystemMetrics(SmCxvirtualscreen);
        int virtualHeight = GetSystemMetrics(SmCyvirtualscreen);
        if (virtualWidth <= 0 || virtualHeight <= 0)
        {
            return false;
        }

        point = new Vector3(
            cursor.X - virtualX,
            virtualY + virtualHeight - cursor.Y,
            0f);
        return true;
    }

    private static bool TryGetVisibleBubbleLine(
        DialogueBubbleUI bubble,
        bool allowChildScan,
        out string line,
        out string source)
    {
        string visibleLine = CleanDialogueText(bubble._dialogueText?.text ?? string.Empty);
        if (visibleLine.Length == 0 && allowChildScan)
        {
            visibleLine = FindRenderedBubbleLine(bubble);
        }
        bool capturedForThisBubble = IsCapturedBubble(bubble)
            && _autonomousDialogueLine.Length > 0;
        line = capturedForThisBubble ? _autonomousDialogueLine : visibleLine;
        source = capturedForThisBubble ? "captured" : "visible";
        if (line.Length == 0)
        {
            return false;
        }

        RectTransform? root = bubble._rootRect;
        if (root is null || !root.gameObject.activeInHierarchy)
        {
            return false;
        }

        CanvasGroup? canvasGroup = bubble._canvasGroup;
        if (canvasGroup is not null && canvasGroup.alpha <= 0.01f)
        {
            return false;
        }

        return bubble._dialogueText is not null
            && bubble._dialogueText.gameObject.activeInHierarchy;
    }

    private static DialogueBubbleUI? GetCachedDialogueBubble()
    {
        if (_cachedDialogueBubble is not null)
        {
            try
            {
                _ = _cachedDialogueBubble.GetInstanceID();
                return _cachedDialogueBubble;
            }
            catch
            {
                _cachedDialogueBubble = null;
            }
        }

        if (Time.unscaledTime < _nextDialogueBubbleLookup)
        {
            return null;
        }
        _nextDialogueBubbleLookup = Time.unscaledTime + 1f;
        _cachedDialogueBubble = UnityEngine.Object.FindObjectOfType<DialogueBubbleUI>(true);
        return _cachedDialogueBubble;
    }

    private static void CacheDialogueBubble(DialogueBubbleUI bubble)
    {
        _cachedDialogueBubble = bubble;
        _nextDialogueBubbleLookup = Time.unscaledTime + 1f;
    }

    private static bool IsPointInsideBubble(DialogueBubbleUI bubble, Vector3 point)
    {
        const float padding = 12f;
        RectTransform? root = bubble._rootRect;
        if (root is not null
            && RectTransformUtility.ScreenPointToLocalPointInRectangle(
                root,
                new Vector2(point.x, point.y),
                bubble._uiCamera,
                out Vector2 localPoint))
        {
            Rect rect = root.rect;
            rect.xMin -= padding;
            rect.xMax += padding;
            rect.yMin -= padding;
            rect.yMax += padding;
            if (rect.Contains(localPoint))
            {
                return true;
            }
        }

        if (!bubble.TryGetBubbleScreenBounds(
            out float minX,
            out float maxX,
            out float minY,
            out float maxY))
        {
            return false;
        }
        bool XInside(float x) => x >= minX - padding && x <= maxX + padding;
        bool YInside(float y) => y >= minY - padding && y <= maxY + padding;
        return XInside(point.x) && (YInside(point.y) || YInside(Screen.height - point.y));
    }

    [HarmonyPatch(typeof(CharacterInteractionHandler), nameof(CharacterInteractionHandler.HandleHookChatRequest))]
    private static class RightClickChatScopePatch
    {
        private static void Prefix()
        {
            _rightClickChatRequest = true;
            LilithModPlugin.PluginLog.LogInfo("Character hook chat request entered");
        }

        private static Exception? Finalizer(Exception? __exception)
        {
            _rightClickChatRequest = false;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(CharacterInteractionHandler), nameof(CharacterInteractionHandler.OpenChatPanel))]
    private static class RightClickChatMenuPatch
    {
        private static bool Prefix(CharacterInteractionHandler __instance)
        {
            if (!_rightClickChatRequest)
            {
                return true;
            }

            ShowRightClickChatMenu(__instance);
            return false;
        }
    }

    private static void ShowRightClickChatMenu(CharacterInteractionHandler handler)
    {
        if (handler._playerLineController is null)
        {
            handler._playerLineController = UnityEngine.Object.FindObjectOfType<PlayerLineController>(true);
        }
        if (handler._playerLineController is null)
        {
            LilithModPlugin.PluginLog.LogWarning("Character right-click menu has no PlayerLineController");
            return;
        }
        var options = new Il2CppSystem.Collections.Generic.List<string>();
        options.Add("和莉莉丝聊天");
        options.Add("一起听歌吧");
        _chatSelection ??= (System.Action<int>)OnChatSelected;
        handler._playerLineController.ShowOptions(
            options.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<string>>(),
            _chatSelection);
        _lastMenuFrame = Time.frameCount;
        LilithModPlugin.PluginLog.LogInfo("Character right-click chat option shown");
    }

    private static void OnChatSelected(int index)
    {
        if (index == 0)
        {
            TrayIntegration.ShowChat();
            return;
        }
        if (index == 1)
        {
            TrayIntegration.OpenPreferredPlayer();
        }
    }

    [HarmonyPatch(typeof(DialogueBubbleUI), nameof(DialogueBubbleUI.OnDialogueStart))]
    private static class DialogueBubbleStartPatch
    {
        private static void Postfix(DialogueBubbleUI __instance, DialogueNode node)
        {
            CacheDialogueBubble(__instance);
            CaptureAutonomousDialogue(__instance, node);
        }
    }

    [HarmonyPatch(typeof(DialogueBubbleUI), nameof(DialogueBubbleUI.OnDialogueAdvance))]
    private static class DialogueBubbleAdvancePatch
    {
        private static void Postfix(DialogueBubbleUI __instance, DialogueNode node)
        {
            CacheDialogueBubble(__instance);
            CaptureAutonomousDialogue(__instance, node);
        }
    }

    [HarmonyPatch(typeof(DialogueBubbleUI), nameof(DialogueBubbleUI.ShowNode))]
    private static class DialogueBubbleShowNodePatch
    {
        private static void Postfix(DialogueBubbleUI __instance, DialogueNode node)
        {
            CacheDialogueBubble(__instance);
            CaptureAutonomousDialogue(__instance, node);
        }
    }

    [HarmonyPatch(typeof(TypewriterEffect), nameof(TypewriterEffect.Play))]
    private static class DialogueTypewriterPlayPatch
    {
        private static void Postfix(TypewriterEffect __instance, string text)
        {
            if (_companionDialogueScope || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            DialogueBubbleUI? bubble = GetCachedDialogueBubble();
            if (bubble is null || bubble._typewriter is null)
            {
                // This hook runs only when new dialogue starts, so a one-off lookup is
                // acceptable and also seeds the cache for subsequent frame polling.
                bubble = UnityEngine.Object.FindObjectOfType<DialogueBubbleUI>(true);
            }
            if (bubble is null || bubble._typewriter is null)
            {
                return;
            }

            CacheDialogueBubble(bubble);
            _autonomousDialogueBubble = bubble;
            _autonomousDialogueLine = CleanDialogueText(text);
            LilithModPlugin.PluginLog.LogInfo("Dialogue bubble armed from typewriter");
        }
    }

    [HarmonyPatch(typeof(DialogueBubbleUI), nameof(DialogueBubbleUI.OnDialogueEnd))]
    private static class DialogueBubbleEndPatch
    {
        private static void Postfix()
        {
            ClearAutonomousDialogue();
        }
    }

    [HarmonyPatch(typeof(DialogueBubbleUI), nameof(DialogueBubbleUI.OnBubbleClicked))]
    private static class AutonomousDialogueClickPatch
    {
        private static void Prefix(DialogueBubbleUI __instance)
        {
            CacheDialogueBubble(__instance);
            _lastNativeBubbleClickTime = Time.unscaledTime;
            OpenAutonomousDialogue(__instance);
        }
    }

    [HarmonyPatch(typeof(NoteImageSaver), nameof(NoteImageSaver.SaveNote))]
    private static class NoteSavedPatch
    {
        private static void Prefix(string __0)
        {
            _latestNoteLine = NoteConversationText.Normalize(CleanDialogueText(__0));
            LilithModPlugin.PluginLog.LogInfo(
                $"Lilith note armed for chat; chars={_latestNoteLine.Length}");
        }
    }

    [HarmonyPatch(typeof(ShowSystemTray), "OnNoteBadgeClicked")]
    private static class NoteBadgeClickedPatch
    {
        private static void Postfix()
        {
            DeliverLatestNote("note-badge");
        }
    }

    [HarmonyPatch(typeof(NoteViewerWindow), nameof(NoteViewerWindow.TryShowUnread))]
    private static class UnreadNoteViewerPatch
    {
        private static void Postfix(bool __result)
        {
            if (__result)
            {
                DeliverLatestNote("note-viewer");
            }
        }
    }

    private static void DeliverLatestNote(string source)
    {
        string line = _latestNoteLine;
        if (line.Length == 0)
        {
            LilithModPlugin.PluginLog.LogInfo(
                $"{source} opened without a note captured in this game session");
            return;
        }
        DeliverBubbleLine(line, source);
    }

    private static void CaptureAutonomousDialogue(DialogueBubbleUI bubble, DialogueNode? node)
    {
        CacheDialogueBubble(bubble);
        if (_companionDialogueScope || node is null || node.HasOptions || node.HasPlayerLineOptions)
        {
            ClearAutonomousDialogue();
            return;
        }

        _autonomousDialogueBubble = bubble;
        _autonomousDialogueLine = CleanDialogueText(
            node.text ?? bubble._dialogueText?.text ?? string.Empty);
        LilithModPlugin.PluginLog.LogInfo($"Dialogue bubble armed for chat: node={node.id}");
    }

    private static void OpenAutonomousDialogue(DialogueBubbleUI bubble)
    {
        bool capturedForThisBubble = IsCapturedBubble(bubble)
            && _autonomousDialogueLine.Length > 0;
        string line = capturedForThisBubble
            ? _autonomousDialogueLine
            : FindRenderedBubbleLine(bubble);
        string source = capturedForThisBubble ? "captured" : "visible";
        if (line.Length == 0)
        {
            line = _autonomousDialogueLine;
        }
        ClearAutonomousDialogue();
        if (line.Length == 0)
        {
            return;
        }
        DeliverBubbleLine(line, "native-" + source);
    }

    private static void DeliverBubbleLine(string line, string source)
    {
        if (line.Length == 0)
        {
            return;
        }
        if (Time.unscaledTime <= _companionDialogueUntil
            && string.Equals(line, _companionDialogueText, StringComparison.Ordinal))
        {
            LilithModPlugin.PluginLog.LogInfo("Companion reply bubble click ignored");
            return;
        }
        if (Time.unscaledTime - _lastDeliveredBubbleTime <= 0.25f
            && string.Equals(line, _lastDeliveredBubbleLine, StringComparison.Ordinal))
        {
            LilithModPlugin.PluginLog.LogInfo("Duplicate dialogue bubble delivery ignored");
            return;
        }

        _lastDeliveredBubbleLine = line;
        _lastDeliveredBubbleTime = Time.unscaledTime;
        _recentVisibleBubbleLine = string.Empty;
        _recentVisibleBubbleTime = -10f;
        _hasRecentBubbleBounds = false;
        LilithModPlugin.PluginLog.LogInfo(
            $"Dialogue bubble clicked; opening Companion chat; source={source}; chars={line.Length}");
        TrayIntegration.BeginChatFromLilithLine(line);
    }

    private static bool IsCapturedBubble(DialogueBubbleUI bubble)
    {
        try
        {
            return _autonomousDialogueBubble is not null
                && _autonomousDialogueBubble.GetInstanceID() == bubble.GetInstanceID();
        }
        catch
        {
            return ReferenceEquals(_autonomousDialogueBubble, bubble);
        }
    }

    private static string FindRenderedBubbleLine(DialogueBubbleUI bubble)
    {
        string best = CleanDialogueText(bubble._dialogueText?.text ?? string.Empty);
        RectTransform? root = bubble._rootRect;
        if (root is null)
        {
            return best;
        }

        try
        {
            foreach (TextMeshProUGUI label in root.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (label is null || !label.enabled || !label.gameObject.activeInHierarchy)
                {
                    continue;
                }
                CanvasRenderer renderer = label.canvasRenderer;
                if (renderer is not null && renderer.GetAlpha() <= 0.01f)
                {
                    continue;
                }
                string candidate = CleanDialogueText(label.text ?? string.Empty);
                if (candidate.Length > best.Length)
                {
                    best = candidate;
                }
            }
        }
        catch (Exception error)
        {
            LilithModPlugin.PluginLog.LogDebug(
                $"Rendered bubble text scan failed: {error.GetType().Name}: {error.Message}");
        }
        return best;
    }

    private static void ClearAutonomousDialogue()
    {
        _autonomousDialogueBubble = null;
        _autonomousDialogueLine = string.Empty;
    }

    private static string CleanDialogueText(string value)
    {
        var result = new StringBuilder(value.Length);
        bool inTag = false;
        foreach (char character in value)
        {
            if (character == '<')
            {
                inTag = true;
                continue;
            }
            if (inTag)
            {
                if (character == '>')
                {
                    inTag = false;
                }
                continue;
            }
            result.Append(character);
        }
        return result.ToString().Replace("\r", string.Empty).Trim();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [HarmonyPatch(typeof(PlayerLineController), nameof(PlayerLineController.ShowMusicMenu))]
    private static class MusicMenuPatch
    {
        [ThreadStatic]
        private static bool _allowOriginal;

        private static PlayerLineController? _controller;
        private static Il2CppSystem.Action<int>? _selection;

        private static bool Prefix(PlayerLineController __instance)
        {
            if (_allowOriginal)
            {
                return true;
            }

            _controller = __instance;
            _selection ??= (System.Action<int>)OnSelected;
            var options = new Il2CppSystem.Collections.Generic.List<string>();
            options.Add("QQ 音乐");
            options.Add("网易云音乐");
            options.Add("本地音乐");
            __instance.ShowOptions(
                options.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<string>>(),
                _selection);
            return false;
        }

        private static void OnSelected(int index)
        {
            if (index == 0)
            {
                TrayIntegration.OpenPlayer(ExternalMusicPlayer.QqMusic, "QQ 音乐");
                return;
            }
            if (index == 1)
            {
                TrayIntegration.OpenPlayer(ExternalMusicPlayer.NeteaseCloudMusic, "网易云音乐");
                return;
            }
            if (index != 2 || _controller is null)
            {
                return;
            }

            _allowOriginal = true;
            try
            {
                _controller.ShowMusicMenu();
            }
            finally
            {
                _allowOriginal = false;
            }
        }
    }
}
