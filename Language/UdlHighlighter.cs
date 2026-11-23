using System.Net;
using System.Text;

namespace MazatrolViewerAero.Language;

public static class UdlHighlighter
{
    public static string ToHtmlDocument(string text, UdlLanguage language, string? title = null)
    {
        var defaults = language.Styles.TryGetValue("DEFAULT", out var style) ? style : new UdlStyle("000000", "FFFFFF", 0);
        var sb = new StringBuilder();
        sb.Append("<HTML><HEAD><TITLE>");
        sb.Append(WebUtility.HtmlEncode(title ?? language.Name));
        sb.Append("</TITLE></HEAD><BODY BGCOLOR=#");
        sb.Append(defaults.BackColor);
        sb.Append(" TEXT=#");
        sb.Append(defaults.ForeColor);
        sb.Append(">");
        sb.Append("<STYLE>BODY{margin:10px;font-family:\"Courier New\",monospace;font-size:16px;background:#");
        sb.Append(defaults.BackColor);
        sb.Append(";color:#");
        sb.Append(defaults.ForeColor);
        sb.Append(";}PRE{margin:0;font-family:\"Courier New\",monospace;font-size:16px;}</STYLE><PRE>");
        sb.Append(Highlight(text, language));
        sb.Append("</PRE>");
        sb.Append("<script>");
        sb.Append("function aeroPrint(){window.print();}");
        sb.Append("function aeroSelect(){document.execCommand('SelectAll');}");
        sb.Append("function aeroGridText(){try{var selObj=document.selection;if(selObj&&selObj.type=='Text'){var range=selObj.createRange();if(range&&range.compareEndPoints('StartToEnd',range)!=0){var plain=(range.text||'');if(plain.replace(/\\s/g,'').length)return plain;}}}catch(e){}return document.body.innerText||'';}");
        sb.Append("function aeroCopy(){document.execCommand('Copy');}");
        sb.Append("function aeroCut(){document.execCommand('Cut');}function aeroPaste(){document.execCommand('Paste');}function aeroFind(t){return window.find(t);}");
        sb.Append("</script></BODY></HTML>");
        return sb.ToString();
    }

    public static string Highlight(string text, UdlLanguage language)
    {
        var compare = language.CaseIgnored ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var rules = BuildRules(language);
        var sb = new StringBuilder(text.Length * 2);
        int index = 0;
        while (index < text.Length)
        {
            if (text[index] is '\r')
            {
                if (index + 1 < text.Length && text[index + 1] == '\n')
                {
                    sb.Append("\r\n");
                    index += 2;
                }
                else
                {
                    sb.Append('\r');
                    index++;
                }

                continue;
            }

            if (text[index] == '\n')
            {
                sb.Append('\n');
                index++;
                continue;
            }

            if (TryComment(text, ref index, language, compare, sb))
            {
                continue;
            }

            if (TryRule(text, ref index, rules, compare, sb))
            {
                continue;
            }

            if (TryNumber(text, ref index, language, sb))
            {
                continue;
            }

            AppendStyled(sb, text.AsSpan(index, 1), StyleOrDefault(language, "DEFAULT"));
            index++;
        }

        return sb.ToString();
    }

    private static bool TryComment(string text, ref int index, UdlLanguage language, StringComparison compare, StringBuilder sb)
    {
        foreach (var open in language.LineComments.OrderByDescending(item => item.Length))
        {
            if (open.Length == 0 || index + open.Length > text.Length)
            {
                continue;
            }

            if (!text.AsSpan(index, open.Length).Equals(open.AsSpan(), compare))
            {
                continue;
            }

            int end = index;
            while (end < text.Length && text[end] is not ('\r' or '\n'))
            {
                end++;
            }

            AppendStyled(sb, text.AsSpan(index, end - index), StyleOrDefault(language, "LINE COMMENTS", "COMMENTS"));
            index = end;
            return true;
        }

        if (language.BlockCommentOpen.Length > 0
            && index + language.BlockCommentOpen.Length <= text.Length
            && text.AsSpan(index, language.BlockCommentOpen.Length).Equals(language.BlockCommentOpen.AsSpan(), compare))
        {
            int end = index + language.BlockCommentOpen.Length;
            if (language.BlockCommentClose.Length == 0)
            {
                end = text.Length;
            }
            else
            {
                int close = text.IndexOf(language.BlockCommentClose, end, compare);
                end = close < 0 ? text.Length : close + language.BlockCommentClose.Length;
            }

            AppendStyled(sb, text.AsSpan(index, end - index), StyleOrDefault(language, "COMMENTS"));
            index = end;
            return true;
        }

        return false;
    }

    private static bool TryRule(string text, ref int index, List<HighlightRule> rules, StringComparison compare, StringBuilder sb)
    {
        if (!IsTokenStart(text, index))
        {
            return false;
        }

        foreach (var rule in rules)
        {
            if (index + rule.Word.Length > text.Length)
            {
                continue;
            }

            if (!text.AsSpan(index, rule.Word.Length).Equals(rule.Word.AsSpan(), compare))
            {
                continue;
            }

            int end = index + rule.Word.Length;
            if (rule.Prefix)
            {
                // Prefix mode: absorb a numeric/alpha suffix (FMAX, S2000), but stop before the next NC address (X0.0Y0.0).
                while (end < text.Length && IsWordContinue(text[end]))
                {
                    if (char.IsLetter(text[end]) && end > index + rule.Word.Length)
                    {
                        var prev = text[end - 1];
                        if (char.IsDigit(prev) || prev == '.')
                        {
                            break;
                        }
                    }

                    end++;
                }
            }
            else if (!IsNonPrefixBoundary(text, end, rule.Word))
            {
                continue;
            }

            AppendStyled(sb, text.AsSpan(index, end - index), rule.Style);
            index = end;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Keywords start at the beginning of a token, or after a digit when the next letter begins a new NC word (G40G49).
    /// </summary>
    private static bool IsTokenStart(string text, int index)
    {
        if (index <= 0)
        {
            return true;
        }

        var prev = text[index - 1];
        if (!IsWordContinue(prev))
        {
            return true;
        }

        return char.IsDigit(prev) && char.IsLetter(text[index]);
    }

    /// <summary>
    /// Non-prefix keywords must be complete tokens. Digits may continue a longer code (G4 vs G40),
    /// but a letter after a digit-ending code starts the next NC word (G40G49).
    /// </summary>
    private static bool IsNonPrefixBoundary(string text, int end, string word)
    {
        if (end >= text.Length || !IsWordContinue(text[end]))
        {
            return true;
        }

        var next = text[end];
        var last = word[^1];
        if (char.IsDigit(last) && char.IsLetter(next))
        {
            return true;
        }

        if (char.IsLetter(last) && char.IsDigit(next))
        {
            return false;
        }

        return false;
    }

    private static bool TryNumber(string text, ref int index, UdlLanguage language, StringBuilder sb)
    {
        int start = index;
        if (text[index] == '-' && index + 1 < text.Length && char.IsDigit(text[index + 1]))
        {
            index++;
        }

        if (index >= text.Length || !char.IsDigit(text[index]))
        {
            index = start;
            return false;
        }

        while (index < text.Length && (char.IsDigit(text[index]) || text[index] == '.'))
        {
            index++;
        }

        AppendStyled(sb, text.AsSpan(start, index - start), StyleOrDefault(language, "NUMBERS"));
        return true;
    }

    private static List<HighlightRule> BuildRules(UdlLanguage language)
    {
        var rules = new List<HighlightRule>();
        void Add(IEnumerable<string> words, string styleName, bool prefix, int group)
        {
            var style = StyleOrDefault(language, styleName);
            foreach (var word in words)
            {
                if (word.Length > 0)
                {
                    rules.Add(new HighlightRule(word, style, prefix, group));
                }
            }
        }

        // Lower group number = higher priority (Keywords1 before Keywords2 … Keywords8).
        // Longest match still wins first so M03 beats prefix M, TOOL CALL beats TOOL, etc.
        Add(language.FoldOpen, "FOLDER IN CODE1", false, 0);
        Add(language.FoldClose, "FOLDER IN CODE1", false, 0);
        for (int i = 0; i < 8; i++)
        {
            Add(language.Keywords[i], $"KEYWORDS{i + 1}", language.PrefixKeywords[i], i + 1);
        }

        Add(language.Operators, "OPERATORS", false, 9);
        return rules
            .OrderByDescending(rule => rule.Word.Length)
            .ThenBy(rule => rule.Group)
            .ThenBy(rule => rule.Prefix ? 1 : 0)
            .ToList();
    }

    private static UdlStyle StyleOrDefault(UdlLanguage language, params string[] names)
    {
        foreach (var name in names)
        {
            if (language.Styles.TryGetValue(name, out var style))
            {
                return style;
            }
        }

        return language.Styles.TryGetValue("DEFAULT", out var defaults) ? defaults : new UdlStyle("000000", "FFFFFF", 0);
    }

    private static void AppendStyled(StringBuilder sb, ReadOnlySpan<char> text, UdlStyle style)
    {
        sb.Append("<SPAN STYLE=\"color:#");
        sb.Append(style.ForeColor);
        if (style.Bold)
        {
            sb.Append(";font-weight:bold");
        }

        if (style.Italic)
        {
            sb.Append(";font-style:italic");
        }

        sb.Append("\">");
        sb.Append(WebUtility.HtmlEncode(text.ToString()));
        sb.Append("</SPAN>");
    }

    private static bool IsWordContinue(char c) => char.IsLetterOrDigit(c) || c is '.' or '_' or '-';

    private sealed record HighlightRule(string Word, UdlStyle Style, bool Prefix, int Group);
}
