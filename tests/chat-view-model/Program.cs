using System;
using System.Collections.Generic;
using LilithMod;

var model = new ChatViewModel();
model.ReplaceHistory(new List<CompanionHistoryMessage>
{
    new("user", "你好"),
    new("assistant", "我在。")
});

if (model.Messages.Count != 2
    || model.Messages[0].Speaker != ChatSpeaker.User
    || model.Messages[1].Speaker != ChatSpeaker.Lilith)
{
    throw new InvalidOperationException("Persisted roles were not mapped into the view model.");
}

if (model.AddLilithIfNotLast("我在。"))
{
    throw new InvalidOperationException("The last assistant opener must be deduplicated.");
}
if (!model.AddLilithIfNotLast("我们继续吧。") || model.Messages.Count != 3)
{
    throw new InvalidOperationException("A new assistant opener must be appended.");
}

model.MergeHistory(new List<CompanionHistoryMessage>
{
    new("user", "更早的问题"),
    new("assistant", "更早的回答"),
    new("user", "你好"),
    new("assistant", "我在。")
});
if (model.Messages.Count != 5
    || model.Messages[0].Text != "更早的问题"
    || model.Messages[^1].Text != "我们继续吧。")
{
    throw new InvalidOperationException("Loaded history and immediate local messages were not merged correctly.");
}

model.SetBusy(true, "正在想……");
if (!model.IsBusy || model.Status != "正在想……")
{
    throw new InvalidOperationException("Busy state was not retained.");
}

model.Clear();
if (model.Messages.Count != 0)
{
    throw new InvalidOperationException("Clearing the session must clear the visible history.");
}

static void ScaleEquals(float expected, int height)
{
    float actual = ChatUiScale.Calculate(height);
    if (Math.Abs(actual - expected) > 0.001f)
    {
        throw new InvalidOperationException($"Unexpected UI scale for {height}px: {actual}");
    }
}

ScaleEquals(0.85f, 768);
ScaleEquals(1f, 1080);
ScaleEquals(1.3333334f, 1440);
ScaleEquals(2f, 2160);
ScaleEquals(2.5f, 4320);

if (ChatUiScale.ClampUserScale(0.2f) != 0.5f
    || ChatUiScale.ClampUserScale(1.25f) != 1.25f
    || ChatUiScale.ClampUserScale(3f) != 2f)
{
    throw new InvalidOperationException("User UI scale must stay within 50%-200%.");
}
float constrainedHeight = ChatUiScale.FitPanelHeight(904f, 2f, 156f, 500f, 18f);
if (Math.Abs(constrainedHeight - 434f) > 0.001f
    || ChatUiScale.FitPanelHeight(1080f, 2f, 156f, 500f, 18f) != 500f)
{
    throw new InvalidOperationException("Expanded chat height must remain visible at large user scales.");
}

if (ChatPanelPlacement.OppositeEdgeX(120f, 1920f, 560f, 1f, 18f) != 1342f)
{
    throw new InvalidOperationException("A character on the left must dock the chat panel on the right.");
}
if (ChatPanelPlacement.OppositeEdgeX(1800f, 1920f, 560f, 1f, 18f) != 18f)
{
    throw new InvalidOperationException("A character on the right must dock the chat panel on the left.");
}
if (!ChatPanelPlacement.OverlapsCharacter(24f, 560f, 1f, 600f)
    || ChatPanelPlacement.OverlapsCharacter(24f, 560f, 1f, 1100f))
{
    throw new InvalidOperationException("Character overlap clearance was calculated incorrectly.");
}

if (NoteConversationText.Normalize(
        "出门要好好照顾自己。桌面我帮你看着，一切安好。——莉莉丝")
    != "出门要好好照顾自己。桌面我帮你看着，一切安好。"
    || NoteConversationText.Normalize("普通台词") != "普通台词")
{
    throw new InvalidOperationException("Lilith note signatures must not be repeated in chat history.");
}

var seedQueue = new ConversationSeedQueue();
seedQueue.Start();
if (!seedQueue.TryQueue("立即发送") || !seedQueue.TryQueue("重复发送"))
{
    throw new InvalidOperationException("Messages must be queued while an assistant opener is being written.");
}
if (seedQueue.Complete() != "立即发送" || seedQueue.Pending)
{
    throw new InvalidOperationException("Only the first immediate message should run after the opener completes.");
}
if (seedQueue.TryQueue("不应排队"))
{
    throw new InvalidOperationException("Messages must not queue when no opener is pending.");
}

Console.WriteLine("Chat view model tests passed.");

namespace LilithMod
{
    internal sealed record CompanionHistoryMessage(string Role, string Content);
}
