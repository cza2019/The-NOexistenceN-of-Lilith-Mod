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

    public static string WrapForLocale(string text, string locale)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }
        string normalizedLocale = NormalizeLocale(locale);
        string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var result = new StringBuilder(normalized.Length + 8);
        string[] paragraphs = normalized.Split('\n');
        for (int index = 0; index < paragraphs.Length; index++)
        {
            if (index > 0)
            {
                result.Append('\n');
            }
            AppendVisualWrappedParagraph(result, paragraphs[index], normalizedLocale == "en", 30);
        }
        return result.ToString();
    }

    private static string NormalizeLocale(string locale)
    {
        string value = (locale ?? string.Empty).Trim().Replace('_', '-').ToLowerInvariant();
        if (value == "en" || value.StartsWith("en-", StringComparison.Ordinal)) return "en";
        if (value == "ja" || value.StartsWith("ja-", StringComparison.Ordinal)) return "ja";
        if (value is "zh-tw" or "zh-hant" || value.StartsWith("zh-hant-", StringComparison.Ordinal)) return "zh-TW";
        return "zh-CN";
    }

    private static void AppendVisualWrappedParagraph(
        StringBuilder result,
        string paragraph,
        bool preferWords,
        int maxVisualUnits)
    {
        List<string> elements = TextElements(paragraph);
        int start = 0;
        while (start < elements.Count)
        {
            int end = start;
            int units = 0;
            int lastWhitespace = -1;
            int lastPunctuation = -1;
            while (end < elements.Count)
            {
                int nextUnits = VisualUnits(elements[end]);
                if (end > start && units + nextUnits > maxVisualUnits)
                {
                    break;
                }
                units += nextUnits;
                if (string.IsNullOrWhiteSpace(elements[end]))
                {
                    lastWhitespace = end;
                }
                if (IsClosing(elements[end]))
                {
                    lastPunctuation = end + 1;
                }
                end++;
            }
            if (end >= elements.Count)
            {
                AppendRange(result, elements, start, elements.Count);
                break;
            }

            int split = preferWords && lastWhitespace > start
                ? lastWhitespace
                : (lastPunctuation > start ? lastPunctuation : end);
            while (split > start + 1 && split < elements.Count && IsClosing(elements[split]))
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
    }

    private static int VisualUnits(string element)
    {
        if (string.IsNullOrWhiteSpace(element))
        {
            return 1;
        }
        int codePoint = char.ConvertToUtf32(element, 0);
        return codePoint >= 0x1100
            && (codePoint <= 0x115F
                || codePoint is 0x2329 or 0x232A
                || (codePoint >= 0x2E80 && codePoint <= 0xA4CF)
                || (codePoint >= 0xAC00 && codePoint <= 0xD7A3)
                || (codePoint >= 0xF900 && codePoint <= 0xFAFF)
                || (codePoint >= 0xFE10 && codePoint <= 0xFE6F)
                || (codePoint >= 0xFF00 && codePoint <= 0xFF60)
                || (codePoint >= 0xFFE0 && codePoint <= 0xFFE6)
                || (codePoint >= 0x1F300 && codePoint <= 0x1FAFF))
            ? 2
            : 1;
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
