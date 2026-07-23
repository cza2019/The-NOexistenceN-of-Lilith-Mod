using System;
using System.Globalization;
using LilithMod;

static int Length(string value) => StringInfo.ParseCombiningCharacters(value).Length;

static void AssertWrapped(string source)
{
    string wrapped = DialogueTextFormatter.Wrap(source, 15);
    foreach (string line in wrapped.Split('\n'))
    {
        if (Length(line) > 15)
        {
            throw new InvalidOperationException($"Line exceeds 15 text elements: {line}");
        }
        if (line.Length > 0 && "，。！？；：、）》】」』〉〕］｝…—,.!?;:)]}>".Contains(line[0]))
        {
            throw new InvalidOperationException($"Line begins with closing punctuation: {line}");
        }
        if (line.Length > 0 && "（《【「『〈〔［｛([{<".Contains(line[^1]))
        {
            throw new InvalidOperationException($"Line ends with opening punctuation: {line}");
        }
    }
}

AssertWrapped("这是一段刚好需要在标点附近换行的莉莉丝台词，希望读起来仍然很自然。");
AssertWrapped("我想告诉你，《不存在的你和我》并不是一部陌生的游戏，而是我们的共同经历。");
AssertWrapped("第一行由原文决定。\n第二行也会单独计算，不会破坏已有换行。😀😀😀😀😀😀😀😀");

string shortText = "不到十五个字。";
if (DialogueTextFormatter.Wrap(shortText, 15) != shortText)
{
    throw new InvalidOperationException("Short text should not be changed.");
}

Console.WriteLine("Dialogue formatter tests passed.");
