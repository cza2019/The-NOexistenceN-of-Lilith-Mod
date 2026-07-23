using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LilithMod;

internal static class DialogueTextFormatter
{
    private const string ClosingPunctuation = "，。！？；：、）》】」』〉〕］｝…—,.!?;:)]}>";
    private const string OpeningPunctuation = "（《【「『〈〔［｛([{<";

    public static string Wrap(string text, int maxCharactersPerLine)
    {
        if (string.IsNullOrEmpty(text) || maxCharactersPerLine < 1)
        {
            return text ?? string.Empty;
        }

        string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        string[] paragraphs = normalized.Split('\n');
        var result = new StringBuilder(normalized.Length + paragraphs.Length);
        for (int index = 0; index < paragraphs.Length; index++)
        {
            if (index > 0)
            {
                result.Append('\n');
            }
            AppendWrappedParagraph(result, paragraphs[index], maxCharactersPerLine);
        }
        return result.ToString();
    }

    private static void AppendWrappedParagraph(StringBuilder result, string paragraph, int max)
    {
        List<string> elements = TextElements(paragraph);
        int start = 0;
        while (elements.Count - start > max)
        {
            int end = start + max;
            int preferred = FindPreferredBreak(elements, start, end);
            int split = preferred > start ? preferred : end;

            while (split > start + 1 && IsClosing(elements[split]))
            {
                split--;
            }
            while (split > start + 1 && IsOpening(elements[split - 1]))
            {
                split--;
            }
            if (split <= start)
            {
                split = end;
            }

            AppendRange(result, elements, start, split);
            result.Append('\n');
            start = split;
            while (start < elements.Count && string.IsNullOrWhiteSpace(elements[start]))
            {
                start++;
            }
        }
        AppendRange(result, elements, start, elements.Count);
    }

    private static int FindPreferredBreak(List<string> elements, int start, int end)
    {
        int earliest = Math.Max(start + 1, end - 5);
        for (int index = end - 1; index >= earliest; index--)
        {
            if (IsClosing(elements[index]))
            {
                return index + 1;
            }
            if (string.IsNullOrWhiteSpace(elements[index]))
            {
                return index;
            }
        }
        return -1;
    }

    private static List<string> TextElements(string value)
    {
        int[] starts = StringInfo.ParseCombiningCharacters(value);
        var elements = new List<string>(starts.Length);
        for (int index = 0; index < starts.Length; index++)
        {
            int next = index + 1 < starts.Length ? starts[index + 1] : value.Length;
            elements.Add(value.Substring(starts[index], next - starts[index]));
        }
        return elements;
    }

    private static void AppendRange(StringBuilder result, List<string> elements, int start, int end)
    {
        while (end > start && string.IsNullOrWhiteSpace(elements[end - 1]))
        {
            end--;
        }
        for (int index = start; index < end; index++)
        {
            result.Append(elements[index]);
        }
    }

    private static bool IsClosing(string value) =>
        value.Length > 0 && ClosingPunctuation.IndexOf(value, StringComparison.Ordinal) >= 0;

    private static bool IsOpening(string value) =>
        value.Length > 0 && OpeningPunctuation.IndexOf(value, StringComparison.Ordinal) >= 0;
}
