using System;
using System.Collections.Generic;

namespace LilithMod;

internal enum ChatSpeaker
{
    User,
    Lilith,
    System
}

internal enum ChatOpenReason
{
    Command,
    LilithBubble
}

internal sealed record ChatMessage(ChatSpeaker Speaker, string Text);

internal static class NoteConversationText
{
    private static readonly string[] SignatureTails =
    {
        "——莉莉丝",
        "——莉莉絲",
        "——リリス",
        "——Lilith",
        "—莉莉丝",
        "—莉莉絲",
        "—リリス",
        "—Lilith"
    };

    public static string Normalize(string text)
    {
        string normalized = text.Replace("\r", string.Empty, StringComparison.Ordinal).Trim();
        foreach (string tail in SignatureTails)
        {
            if (normalized.EndsWith(tail, StringComparison.OrdinalIgnoreCase))
            {
                return normalized[..^tail.Length].Trim();
            }
        }
        return normalized;
    }
}

internal static class ChatUiScale
{
    public static float Calculate(int screenHeight)
    {
        return Math.Clamp(screenHeight / 1080f, 0.85f, 2.5f);
    }

    public static float ClampUserScale(float scale)
    {
        return Math.Clamp(scale, 0.5f, 2f);
    }

    public static float FitPanelHeight(
        float canvasHeight,
        float userScale,
        float compactHeight,
        float expandedHeight,
        float safeMargin)
    {
        float available = (canvasHeight - safeMargin * 2f)
            / Math.Max(ClampUserScale(userScale), 0.01f);
        return Math.Clamp(available, compactHeight, expandedHeight);
    }
}

internal static class ChatPanelPlacement
{
    public static bool OverlapsCharacter(
        float panelLeft,
        float panelWidth,
        float userScale,
        float characterX,
        float clearance = 170f)
    {
        float panelRight = panelLeft + panelWidth * userScale;
        return characterX >= panelLeft - clearance
            && characterX <= panelRight + clearance;
    }

    public static float OppositeEdgeX(
        float characterX,
        float canvasWidth,
        float panelWidth,
        float userScale,
        float margin)
    {
        return characterX < canvasWidth * 0.5f
            ? Math.Max(margin, canvasWidth - panelWidth * userScale - margin)
            : margin;
    }

    public static float OppositeEdgeXInArea(
        float characterX,
        float areaLeft,
        float areaRight,
        float panelWidth,
        float userScale,
        float margin)
    {
        float left = areaLeft + margin;
        float right = Math.Max(left, areaRight - panelWidth * userScale - margin);
        float center = (areaLeft + areaRight) * 0.5f;
        return characterX < center ? right : left;
    }
}

internal interface IChatView : IDisposable
{
    bool IsVisible { get; }

    bool PointerOverPanel { get; }

    void Show(ChatOpenReason reason);

    void Hide();

    void SetMessages(IReadOnlyList<ChatMessage> messages);

    void AddMessage(ChatMessage message);

    void ClearMessages();

    void SetBusy(bool busy, string status = "");

    void Tick();
}

internal sealed class ChatViewModel
{
    private readonly List<ChatMessage> _messages = new();

    public IReadOnlyList<ChatMessage> Messages => _messages;

    public bool IsBusy { get; private set; }

    public string Status { get; private set; } = string.Empty;

    public void ReplaceHistory(IReadOnlyList<CompanionHistoryMessage> history)
    {
        _messages.Clear();
        foreach (CompanionHistoryMessage item in history)
        {
            _messages.Add(new ChatMessage(
                item.Role == "assistant" ? ChatSpeaker.Lilith : ChatSpeaker.User,
                item.Content));
        }
    }

    public void MergeHistory(IReadOnlyList<CompanionHistoryMessage> history)
    {
        ChatMessage[] local = _messages.ToArray();
        ReplaceHistory(history);

        int maxOverlap = Math.Min(_messages.Count, local.Length);
        int overlap = 0;
        for (int candidate = maxOverlap; candidate > 0; candidate--)
        {
            bool matches = true;
            int serverStart = _messages.Count - candidate;
            for (int index = 0; index < candidate; index++)
            {
                if (_messages[serverStart + index] != local[index])
                {
                    matches = false;
                    break;
                }
            }
            if (matches)
            {
                overlap = candidate;
                break;
            }
        }
        for (int index = overlap; index < local.Length; index++)
        {
            _messages.Add(local[index]);
        }
    }

    public void Add(ChatSpeaker speaker, string text)
    {
        string normalized = text.Trim();
        if (normalized.Length > 0)
        {
            _messages.Add(new ChatMessage(speaker, normalized));
        }
    }

    public bool AddLilithIfNotLast(string text)
    {
        string normalized = text.Trim();
        if (normalized.Length == 0)
        {
            return false;
        }
        ChatMessage? last = _messages.Count > 0 ? _messages[^1] : null;
        if (last is not null && last.Speaker == ChatSpeaker.Lilith && last.Text == normalized)
        {
            return false;
        }
        _messages.Add(new ChatMessage(ChatSpeaker.Lilith, normalized));
        return true;
    }

    public void Clear() => _messages.Clear();

    public void SetBusy(bool busy, string status = "")
    {
        IsBusy = busy;
        Status = status;
    }
}

internal sealed class ConversationSeedQueue
{
    private string? _pendingMessage;

    public bool Pending { get; private set; }

    public void Start()
    {
        Pending = true;
        _pendingMessage = null;
    }

    public bool TryQueue(string message)
    {
        if (!Pending)
        {
            return false;
        }
        _pendingMessage ??= message;
        return true;
    }

    public string? Complete()
    {
        Pending = false;
        string? result = _pendingMessage;
        _pendingMessage = null;
        return result;
    }

    public void Clear()
    {
        Pending = false;
        _pendingMessage = null;
    }
}

internal sealed class RuntimeChatState
{
    public static RuntimeChatState Shared { get; } = new();

    public ChatViewModel Model { get; } = new();

    public ConversationSeedQueue SeedQueue { get; } = new();

    public IChatView? View { get; set; }

    public void Reset()
    {
        View?.Dispose();
        View = null;
        Model.Clear();
        Model.SetBusy(false);
        SeedQueue.Clear();
    }
}
