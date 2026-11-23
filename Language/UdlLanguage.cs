using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace MazatrolViewerAero.Language;

public sealed class UdlLanguage
{
    public required string Name { get; init; }
    public required string SourceFile { get; init; }
    public IReadOnlyList<string> Extensions { get; init; } = [];
    public bool CaseIgnored { get; init; }
    public bool[] PrefixKeywords { get; init; } = new bool[8];
    public IReadOnlyList<string> LineComments { get; init; } = [];
    public string BlockCommentOpen { get; init; } = "";
    public string BlockCommentClose { get; init; } = "";
    public IReadOnlyList<string>[] Keywords { get; init; } = Enumerable.Range(0, 8).Select(_ => (IReadOnlyList<string>)[]).ToArray();
    public IReadOnlyList<string> Operators { get; init; } = [];
    public IReadOnlyList<string> FoldOpen { get; init; } = [];
    public IReadOnlyList<string> FoldClose { get; init; } = [];
    public Dictionary<string, UdlStyle> Styles { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public static UdlLanguage? TryLoad(string path)
    {
        try
        {
            // Notepad++ UDL exports often put binary junk in fontName. That breaks strict XML readers.
            var xml = SanitizeXml(File.ReadAllText(path));
            var doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
            var lang = doc.Root?.Element("UserLang");
            if (lang == null)
            {
                return null;
            }

            var settings = lang.Element("Settings");
            var global = settings?.Element("Global");
            var prefix = settings?.Element("Prefix");
            var lists = lang.Element("KeywordLists");
            var styles = lang.Element("Styles");
            var comments = ParseComments(lists?.Elements("Keywords").FirstOrDefault(e => (string?)e.Attribute("name") == "Comments")?.Value ?? "");
            var prefixFlags = new bool[8];
            for (int i = 0; i < 8; i++)
            {
                prefixFlags[i] = string.Equals((string?)prefix?.Attribute($"Keywords{i + 1}"), "yes", StringComparison.OrdinalIgnoreCase);
            }

            var keywordSets = new IReadOnlyList<string>[8];
            for (int i = 0; i < 8; i++)
            {
                keywordSets[i] = SplitWords(lists?.Elements("Keywords").FirstOrDefault(e => (string?)e.Attribute("name") == $"Keywords{i + 1}")?.Value);
            }

            var styleMap = new Dictionary<string, UdlStyle>(StringComparer.OrdinalIgnoreCase);
            if (styles != null)
            {
                foreach (var style in styles.Elements("WordsStyle"))
                {
                    var name = (string?)style.Attribute("name");
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    styleMap[name] = new UdlStyle(
                        (string?)style.Attribute("fgColor") ?? "000000",
                        (string?)style.Attribute("bgColor") ?? "FFFFFF",
                        int.TryParse((string?)style.Attribute("fontStyle"), out var fontStyle) ? fontStyle : 0);
                }
            }

            var operators = SplitWords(lists?.Elements("Keywords").FirstOrDefault(e => (string?)e.Attribute("name") == "Operators1")?.Value)
                .Concat(SplitWords(lists?.Elements("Keywords").FirstOrDefault(e => (string?)e.Attribute("name") == "Operators2")?.Value))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var foldOpen = SplitWords(lists?.Elements("Keywords").FirstOrDefault(e => (string?)e.Attribute("name") == "Folders in code1, open")?.Value)
                .Concat(SplitWords(lists?.Elements("Keywords").FirstOrDefault(e => (string?)e.Attribute("name") == "Folders in code2, open")?.Value))
                .Concat(SplitWords(lists?.Elements("Keywords").FirstOrDefault(e => (string?)e.Attribute("name") == "Folders in comment, open")?.Value))
                .ToList();
            var foldClose = SplitWords(lists?.Elements("Keywords").FirstOrDefault(e => (string?)e.Attribute("name") == "Folders in code1, close")?.Value)
                .Concat(SplitWords(lists?.Elements("Keywords").FirstOrDefault(e => (string?)e.Attribute("name") == "Folders in code2, close")?.Value))
                .Concat(SplitWords(lists?.Elements("Keywords").FirstOrDefault(e => (string?)e.Attribute("name") == "Folders in comment, close")?.Value))
                .ToList();

            var extensions = ((string?)lang.Attribute("ext") ?? "")
                .Split([' ', '\t', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(ext => ext.StartsWith('.') ? ext : "." + ext)
                .ToList();

            return new UdlLanguage
            {
                Name = (string?)lang.Attribute("name") ?? Path.GetFileNameWithoutExtension(path),
                SourceFile = path,
                Extensions = extensions,
                CaseIgnored = string.Equals((string?)global?.Attribute("caseIgnored"), "yes", StringComparison.OrdinalIgnoreCase),
                PrefixKeywords = prefixFlags,
                LineComments = comments.Line,
                BlockCommentOpen = comments.BlockOpen,
                BlockCommentClose = comments.BlockClose,
                Keywords = keywordSets,
                Operators = operators,
                FoldOpen = foldOpen,
                FoldClose = foldClose,
                Styles = styleMap
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static readonly Regex HexEntity = new(@"&#x([0-9A-Fa-f]+);", RegexOptions.Compiled);
    private static readonly Regex DecEntity = new(@"&#([0-9]+);", RegexOptions.Compiled);

    private static string SanitizeXml(string text)
    {
        // Notepad++ often stores binary junk as numeric character references inside fontName.
        text = HexEntity.Replace(text, match =>
        {
            var code = Convert.ToInt32(match.Groups[1].Value, 16);
            return IsLegalXmlChar(code) ? match.Value : "";
        });
        text = DecEntity.Replace(text, match =>
        {
            var code = int.Parse(match.Groups[1].Value);
            return IsLegalXmlChar(code) ? match.Value : "";
        });

        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (IsLegalXmlChar(ch))
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

    private static bool IsLegalXmlChar(int code) =>
        code is 0x9 or 0xA or 0xD
        || code >= 0x20 && code <= 0xD7FF
        || code >= 0xE000 && code <= 0xFFFD;

    private static (IReadOnlyList<string> Line, string BlockOpen, string BlockClose) ParseComments(string raw)
    {
        // Notepad++ UDL packs each field as NN + delimiter with no space, e.g. "00% 01 02 03( 04)".
        // 00 = line open, 01 = line continue, 02 = line close, 03 = block open, 04 = block close.
        // ((EOL)) means "to end of line" (default for line comments; for block close, treat open as line).
        var line = new List<string>();
        string blockOpen = "";
        string blockClose = "";
        int mode = -1;
        foreach (var token in raw.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            string value;
            if (token.Length >= 2 && char.IsDigit(token[0]) && char.IsDigit(token[1]))
            {
                mode = (token[0] - '0') * 10 + (token[1] - '0');
                value = token.Length > 2 ? token[2..] : "";
                if (value.Length == 0)
                {
                    continue;
                }
            }
            else if (mode < 0)
            {
                continue;
            }
            else
            {
                value = token;
            }

            if (value is "((EOL))")
            {
                if (mode == 4 && blockOpen.Length > 0)
                {
                    line.Add(blockOpen);
                    blockOpen = "";
                    blockClose = "";
                }

                continue;
            }

            switch (mode)
            {
                case 0:
                    line.Add(value);
                    break;
                case 3:
                    blockOpen = value;
                    break;
                case 4:
                    blockClose = value;
                    break;
            }
        }

        return (line, blockOpen, blockClose);
    }

    private static List<string> SplitWords(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        // Notepad++: spaces/newlines separate keywords; "quoted phrases" keep internal spaces.
        var results = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        foreach (var ch in raw)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && ch is ' ' or '\t' or '\n' or '\r')
            {
                if (sb.Length > 0)
                {
                    results.Add(sb.ToString());
                    sb.Clear();
                }

                continue;
            }

            sb.Append(ch);
        }

        if (sb.Length > 0)
        {
            results.Add(sb.ToString());
        }

        return results
            .Where(word => word.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(word => word.Length)
            .ToList();
    }
}

public readonly record struct UdlStyle(string ForeColor, string BackColor, int FontStyle)
{
    public bool Bold => (FontStyle & 1) != 0;
    public bool Italic => (FontStyle & 2) != 0;
}
