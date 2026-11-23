using System.Globalization;
using System.Text;
using MazatrolViewerAero.Language;

namespace MazatrolViewerAero.Decoding;

public enum OpenFailure
{
    None,
    NotMazatrol,
    TypeIncorrect,
    SizeIncorrect,
    OpenError
}

public sealed class MazDocument
{
    private MazDocument(string path, byte[] data, string kind, int typeCode, string comment, string material, IReadOnlyList<ProgramUnit> units, IReadOnlyList<double> measures, bool textProgram, bool htmlPage = false)
    {
        Path = path;
        Data = data;
        Kind = kind;
        TypeCode = typeCode;
        Comment = comment;
        Material = material;
        Units = units;
        Measures = measures;
        TextProgram = textProgram;
        HtmlPage = htmlPage;
    }

    public string Path { get; }
    public byte[] Data { get; }
    public string Kind { get; }
    public int TypeCode { get; }
    public string Comment { get; }
    public string Material { get; }
    public IReadOnlyList<ProgramUnit> Units { get; }
    public IReadOnlyList<double> Measures { get; }
    public bool TextProgram { get; }
    public bool HtmlPage { get; }
    public string WorkNumber => WorkNumberFrom(Path, Comment);

    public static MazDocument? TryOpen(string path, out OpenFailure failure)
    {
        failure = OpenFailure.None;
        byte[] data;
        try
        {
            data = File.ReadAllBytes(path);
        }
        catch (Exception)
        {
            failure = OpenFailure.OpenError;
            return null;
        }

        if (data.Length == 0)
        {
            failure = OpenFailure.SizeIncorrect;
            return null;
        }

        var extension = System.IO.Path.GetExtension(path);
        if (IsHtmlExtension(extension))
        {
            return new MazDocument(path, data, "HTML", 0, "", "", [], [], false, htmlPage: true);
        }

        if (IsIsoText(data))
        {
            if (extension.Length > 0 && !IsTextExtension(extension) && ExpectedType(extension) is int expected && expected != 0)
            {
                failure = OpenFailure.TypeIncorrect;
                return null;
            }

            return new MazDocument(path, data, "ISO", 0, FirstIsoComment(data), "", [], [], true);
        }

        if (data.Length < 0x120)
        {
            failure = OpenFailure.SizeIncorrect;
            return null;
        }

        if (IsMaz(data))
        {
            if (extension.Length > 0 && !extension.Equals(".maz", StringComparison.OrdinalIgnoreCase) && ExpectedType(extension) is int)
            {
                failure = OpenFailure.TypeIncorrect;
                return null;
            }

            var mazComment = AsciiAt(data, 0x50, 48);
            var mazMaterial = FirstMaterial(data);
            return new MazDocument(path, data, "MAZ", 0, mazComment, mazMaterial, ReadUnits(data), ReadMeasures(data, mazMaterial), false);
        }

        int type = BitConverter.ToInt32(data, 0x20);
        int? expectedType = ExpectedType(extension);
        if (expectedType is int required && required != type)
        {
            failure = OpenFailure.TypeIncorrect;
            return null;
        }

        if (!KnownType(type))
        {
            failure = OpenFailure.NotMazatrol;
            return null;
        }

        var comment = AsciiAt(data, 0x50, 48);
        var material = FirstMaterial(data);
        return new MazDocument(path, data, FamilyName(type), type, comment, material, ReadUnits(data), ReadMeasures(data, material), false);
    }

    public string ToHtml(ViewOptions options)
    {
        if (HtmlPage)
        {
            return HtmlSource();
        }

        var title = BuildTitle(options);
        var sb = new StringBuilder();
        sb.Append("<HTML><HEAD><TITLE>");
        sb.Append(Esc(title));
        sb.Append("</TITLE></HEAD><BODY BGCOLOR=black TEXT=white>");
        sb.Append("<STYLE>BODY{background:black;color:white;margin:10px;font-family:\"Courier New\",monospace;}");
        sb.Append("TD{border:1px solid white;padding:2px 8px;white-space:nowrap;font-family:\"Courier New\",monospace;font-size:16px;}");
        sb.Append("TABLE{border-collapse:collapse;}</STYLE>");
        if (options.InsertProgramInfo)
        {
            sb.Append("<DIV STYLE=\"background-color:rgb(255,0,0);color:white;font-family:Courier New;font-size:16px;margin-bottom:8px\">");
            sb.Append(Esc(title));
            sb.Append("</DIV>");
        }

        if (TextProgram)
        {
            var language = UdlCatalog.Shared.FindByName(options.SyntaxLanguage);
            if (language != null)
            {
                return UdlHighlighter.ToHtmlDocument(Encoding.ASCII.GetString(Data).Replace("\0", ""), language, title);
            }

            sb.Append("<PRE STYLE=\"font-family:Courier New;font-size:16px\">");
            sb.Append(IsoBody(Data));
            sb.Append("</PRE>");
        }
        else if (!LayoutProgram.TryAppend(sb, Data, Path, html: true, options.Inch))
        {
            sb.Append("<PRE STYLE=\"font-family:Courier New;font-size:16px\">");
            sb.Append(BinaryBody(options));
            sb.Append("</PRE>");
        }

        sb.Append("<script>");
        sb.Append("function aeroPrint(){window.print();}");
        sb.Append("function aeroSelect(){document.execCommand('SelectAll');}");
        sb.Append("function aeroRows(rows){var lines=[];for(var i=0;i<rows.length;i++){if(rows[i].getAttribute('data-gap')=='1'){lines.push('');continue;}var cells=rows[i].cells;if(!cells||cells.length==0)continue;var parts=[];for(var j=0;j<cells.length;j++){var t=cells[j].innerText||'';t=t.replace(/\\u00a0/g,'').replace(/^\\s+|\\s+$/g,'');parts.push(t);}lines.push(parts.join('\\t'));}return lines.join('\\r\\n');}");
        sb.Append("function aeroGrid(){return aeroRows(document.getElementsByTagName('TR'));}");
        sb.Append("function aeroHit(sel,el){var elRange=document.body.createTextRange();elRange.moveToElementText(el);return sel.compareEndPoints('StartToEnd',elRange)<0&&sel.compareEndPoints('EndToStart',elRange)>0;}");
        sb.Append("function aeroSelectedLines(sel){var rows=document.getElementsByTagName('TR');var lines=[];for(var i=0;i<rows.length;i++){var row=rows[i];if(row.getAttribute('data-gap')=='1'||!row.cells||row.cells.length==0)continue;var parts=[];for(var j=0;j<row.cells.length;j++){try{if(aeroHit(sel,row.cells[j])){var t=row.cells[j].innerText||'';t=t.replace(/\\u00a0/g,'').replace(/^\\s+|\\s+$/g,'');parts.push(t);}}catch(e){}}if(parts.length)lines.push(parts.join('\\t'));}return lines.join('\\r\\n');}");
        sb.Append("function aeroGridText(){try{var selObj=document.selection;if(selObj&&selObj.type=='Text'){var range=selObj.createRange();if(range&&range.compareEndPoints('StartToEnd',range)!=0){var picked=aeroSelectedLines(range);if(picked.length)return picked;var plain=(range.text||'').replace(/\\u00a0/g,'');if(plain.replace(/\\s/g,'').length)return plain;}}}catch(e){}return aeroGrid();}");
        sb.Append("function aeroCopy(){document.execCommand('Copy');}");
        sb.Append("function aeroCut(){document.execCommand('Cut');}function aeroPaste(){document.execCommand('Paste');}function aeroFind(t){return window.find(t);}");
        sb.Append("</script>");
        sb.Append("</BODY></HTML>");
        return sb.ToString();
    }

    public string ToPlainText(ViewOptions options)
    {
        if (HtmlPage)
        {
            return HtmlSource();
        }

        var sb = new StringBuilder();
        if (options.InsertProgramInfo)
        {
            sb.AppendLine(BuildTitle(options));
        }

        if (TextProgram)
        {
            sb.Append(Encoding.ASCII.GetString(Data).Replace("\0", ""));
            return sb.ToString();
        }

        if (LayoutProgram.TryAppend(sb, Data, Path, html: false, options.Inch))
        {
            return sb.ToString();
        }

        if (Material.Length > 0 || Measures.Count > 0)
        {
            sb.Append("MAT. ").Append(Material);
            foreach (var value in Measures)
            {
                sb.Append("  ").Append(FormatMeasure(value, options.Inch));
            }

            sb.AppendLine();
        }

        foreach (var unit in Units)
        {
            sb.Append("UNo ").Append(unit.Sequence.ToString(CultureInfo.InvariantCulture));
            sb.Append("  UNIT ").Append(unit.Code.ToString("X", CultureInfo.InvariantCulture));
            foreach (var value in unit.Values)
            {
                sb.Append("  ").Append(FormatMeasure(value, options.Inch));
            }

            if (unit.Text.Length > 0)
            {
                sb.Append("  ").Append(unit.Text);
            }

            sb.AppendLine();
            if (options.AddBinaryLine)
            {
                sb.AppendLine(unit.Hex);
            }
        }

        return sb.ToString();
    }

    public string BuildTitle(ViewOptions options)
    {
        var machine = string.IsNullOrWhiteSpace(options.MachineName) ? Kind : options.MachineName;
        var disp = options.Inch ? "INCH" : "MM";
        var head = options.Head is 1 or 2 ? $"  HD{options.Head}" : "";
        var comment = Comment.Length > 0 ? Comment : System.IO.Path.GetFileNameWithoutExtension(Path);
        return $"{Kind} WNo.{WorkNumber} ( {comment} )  Current Machine: {machine}  Disp: {disp}{head}";
    }

    private string BinaryBody(ViewOptions options)
    {
        var sb = new StringBuilder();
        sb.Append("<FONT COLOR=rgb(0,255,255)><SPAN STYLE=\"background-color:rgb(0,0,0)\">");
        sb.Append(Esc($"{"UNo.",-6}{"MAT.",-12}"));
        sb.Append("DATA");
        sb.Append("</SPAN></FONT>\r\n");
        sb.Append("<FONT COLOR=rgb(0,255,255)>  0 </FONT><FONT COLOR=rgb(0,255,0)>");
        sb.Append(Esc(Material.PadRight(12)));
        sb.Append("</FONT><FONT COLOR=rgb(255,255,255)>");
        foreach (var value in Measures)
        {
            sb.Append(Esc(FormatMeasure(value, options.Inch).PadLeft(10)));
        }

        sb.Append("</FONT>\r\n");

        if (options.ValidDetail)
        {
            sb.Append("<FONT COLOR=rgb(255,255,0)>");
            sb.Append(Esc($"R392 {options.R392:X4}   R393 {options.R393:X4}   R394 {options.R394:X4}"));
            sb.Append("</FONT>\r\n");
        }

        foreach (var row in Units)
        {
            sb.Append("<FONT COLOR=rgb(0,255,255)>");
            sb.Append(Esc($" {row.Sequence,3} "));
            sb.Append("</FONT><FONT COLOR=rgb(255,255,0)>");
            sb.Append(Esc($"UNIT {row.Code:X4}".PadRight(16)));
            sb.Append("</FONT><FONT COLOR=rgb(255,255,255)>");
            foreach (var value in row.Values)
            {
                sb.Append(Esc(FormatMeasure(value, options.Inch).PadLeft(10)));
            }

            if (row.Text.Length > 0)
            {
                sb.Append("  ").Append(Esc(row.Text));
            }

            sb.Append("</FONT>\r\n");
            if (options.AddBinaryLine && row.Hex.Length > 0)
            {
                sb.Append("<FONT COLOR=rgb(128,128,128)>");
                sb.Append(Esc(row.Hex));
                sb.Append("</FONT>\r\n");
            }
        }

        return sb.ToString();
    }

    private static string IsoBody(byte[] data)
    {
        var text = Encoding.ASCII.GetString(data).Replace("\0", "");
        var sb = new StringBuilder();
        var number = 1;
        foreach (var line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            sb.Append("<FONT COLOR=rgb(0,255,255)>");
            sb.Append(number.ToString("0000", CultureInfo.InvariantCulture));
            sb.Append(" </FONT><FONT COLOR=rgb(255,255,255)>");
            sb.Append(Esc(line));
            sb.Append("</FONT>\r\n");
            number++;
        }

        return sb.ToString();
    }

    public static string FamilyName(int type) => type switch
    {
        23 => "M640T/MT",
        24 => "M640MTPro",
        35 => "M640M",
        36 => "M640MPro",
        37 => "MATRIX",
        38 => "PBM",
        64 => "PBG",
        _ => "MAZATROL"
    };

    public static int? ExpectedType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".t6m" or ".t6i" => 23,
            ".pbb" => 24,
            ".m6m" => 35,
            ".pba" => 36,
            ".pbd" or ".pbe" or ".pbf" => 37,
            ".pbm" => 38,
            ".pbg" => 64,
            ".maz" => 0,
            _ => null
        };
    }

    public static bool KnownType(int type) => type is 23 or 24 or 35 or 36 or 37 or 38 or 64;

    public static bool IsHtmlExtension(string extension)
    {
        return extension.Equals(".html", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".htm", StringComparison.OrdinalIgnoreCase);
    }

    private string HtmlSource()
    {
        if (Data.Length >= 3 && Data[0] == 0xEF && Data[1] == 0xBB && Data[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(Data);
        }

        return Encoding.Latin1.GetString(Data);
    }

    private static bool IsTextExtension(string extension)
    {
        return extension.Equals(".m6p", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".nc", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMaz(byte[] data)
    {
        return data.Length > 16
            && data[0] == 0x01 && data[1] == 0x00 && data[2] == 0x00 && data[3] == 0x00
            && data[4] == 0xC3 && data[5] == 0x00 && data[6] == 0x00 && data[7] == 0x00;
    }

    private static bool IsIsoText(byte[] data)
    {
        int take = Math.Min(data.Length, 512);
        if (take < 8)
        {
            return false;
        }

        int printable = 0;
        bool newline = false;
        for (int i = 0; i < take; i++)
        {
            byte b = data[i];
            if (b is (byte)'\n' or (byte)'\r')
            {
                newline = true;
                printable++;
            }
            else if (b == 0x09 || (b >= 32 && b < 127))
            {
                printable++;
            }
        }

        if (printable < take * 8 / 10)
        {
            return false;
        }

        return newline || data[0] is (byte)'O' or (byte)'%' or (byte)'(';
    }

    private static string FirstIsoComment(byte[] data)
    {
        var text = Encoding.ASCII.GetString(data, 0, Math.Min(data.Length, 120));
        int start = text.IndexOf('(');
        int end = start >= 0 ? text.IndexOf(')', start + 1) : -1;
        if (start >= 0 && end > start)
        {
            return text[(start + 1)..end].Trim();
        }

        return "";
    }

    private static string AsciiAt(byte[] data, int offset, int count)
    {
        if (offset < 0 || offset >= data.Length)
        {
            return "";
        }

        int end = Math.Min(data.Length, offset + count);
        int n = offset;
        while (n < end && data[n] != 0)
        {
            n++;
        }

        if (n == offset)
        {
            return "";
        }

        var raw = data[offset..n];
        if (!raw.All(b => b is >= 32 and < 127))
        {
            return "";
        }

        return Encoding.ASCII.GetString(raw).Trim();
    }

    private static string FirstMaterial(byte[] data)
    {
        foreach (int origin in new[] { 0x100, 0x150, 0x80 })
        {
            var text = AsciiAt(data, origin, 24);
            if (text.Count(char.IsLetter) >= 3)
            {
                return text;
            }
        }

        return "";
    }

    private static List<double> ReadMeasures(byte[] data, string material)
    {
        if (LooksLikeMatrixStock(data))
        {
            var stock = new List<double>();
            for (int i = 0x13C; i < 0x14C; i += 4)
            {
                int raw = BitConverter.ToInt32(data, i);
                stock.Add(raw == 0 ? 0 : raw / 10000.0);
            }

            ushort rpm = BitConverter.ToUInt16(data, 0x112);
            if (rpm is >= 50 and <= 20000)
            {
                stock.Add(rpm);
            }

            return stock;
        }

        var values = new List<double>();
        int origin = IndexOfAscii(data, material);
        int from = origin >= 0 ? origin : 0x100;
        int to = Math.Min(data.Length - 4, from + 64);
        for (int i = Math.Max(0x40, from); i <= to; i += 4)
        {
            int raw = BitConverter.ToInt32(data, i);
            if (TryScale(raw, out double value))
            {
                values.Add(value);
            }

            if (values.Count == 8)
            {
                break;
            }
        }

        return values;
    }

    private static bool LooksLikeMatrixStock(byte[] data)
    {
        if (data.Length < 0x160 || AsciiAt(data, 0x150, 16).Count(char.IsLetter) < 3)
        {
            return false;
        }

        for (int i = 0x13C; i < 0x14C; i += 4)
        {
            int raw = BitConverter.ToInt32(data, i);
            if (raw != 0 && (raw % 10000 != 0 || Math.Abs(raw) > 100_000_000))
            {
                return false;
            }
        }

        return true;
    }

    private static int IndexOfAscii(byte[] data, string text)
    {
        if (text.Length < 3)
        {
            return -1;
        }

        var needle = Encoding.ASCII.GetBytes(text);
        for (int i = 0; i <= data.Length - needle.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (data[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return i;
            }
        }

        return -1;
    }

    private static List<ProgramUnit> ReadUnits(byte[] data)
    {
        if (TryReadStride(data, 100, out var packed) && packed.Count >= 2)
        {
            return packed;
        }

        var units = new List<ProgramUnit>();
        int start = Math.Min(data.Length, 0x1C0);
        for (int i = start; i + 16 <= data.Length && units.Count < 400;)
        {
            ushort code = BitConverter.ToUInt16(data, i);
            ushort sequence = BitConverter.ToUInt16(data, i + 2);
            if (!PlausibleUnit(code, sequence))
            {
                i += 16;
                continue;
            }

            units.Add(ReadUnit(data, i, Math.Min(0x60, data.Length - i)));
            i += 0x60;
        }

        return units;
    }

    private static bool TryReadStride(byte[] data, int stride, out List<ProgramUnit> units)
    {
        units = [];
        int bestStart = -1;
        int bestCount = 0;
        int scanEnd = Math.Min(data.Length, 0x800);
        for (int start = 0x40; start + 4 < scanEnd; start += 2)
        {
            int count = CountStride(data, start, stride);
            if (count > bestCount)
            {
                bestCount = count;
                bestStart = start;
            }
        }

        if (bestCount < 2 || bestStart < 0)
        {
            return false;
        }

        for (int n = 0; n < bestCount; n++)
        {
            int offset = bestStart + (n * stride);
            units.Add(ReadUnit(data, offset, Math.Min(stride, data.Length - offset)));
        }

        return true;
    }

    private static int CountStride(byte[] data, int start, int stride)
    {
        int count = 0;
        int lastSequence = 0;
        for (int i = start; i + 4 <= data.Length && count < 400; i += stride)
        {
            ushort code = BitConverter.ToUInt16(data, i);
            ushort sequence = BitConverter.ToUInt16(data, i + 2);
            if (!PlausibleUnit(code, sequence))
            {
                break;
            }

            if (count > 0 && sequence != 1 && sequence > lastSequence + 40)
            {
                break;
            }

            lastSequence = sequence;
            count++;
        }

        return count;
    }

    private static bool PlausibleUnit(ushort code, ushort sequence) =>
        code is > 0 and <= 0x2000 && sequence is > 0 and <= 500;

    private static ProgramUnit ReadUnit(byte[] data, int offset, int length)
    {
        ushort code = BitConverter.ToUInt16(data, offset);
        ushort sequence = BitConverter.ToUInt16(data, offset + 2);
        var values = new List<double>();
        int end = Math.Min(data.Length, offset + length);
        for (int p = offset + 8; p + 4 <= end && values.Count < 8; p += 4)
        {
            int raw = BitConverter.ToInt32(data, p);
            if (TryScale(raw, out double value))
            {
                values.Add(value);
            }
        }

        string text = ExtractText(data, offset + 8, Math.Max(0, length - 8));
        string hex = Convert.ToHexString(data, offset, Math.Min(32, data.Length - offset));
        return new ProgramUnit(sequence, code, values, text, hex);
    }

    private static string ExtractText(byte[] data, int offset, int count)
    {
        int end = Math.Min(data.Length, offset + count);
        var sb = new StringBuilder();
        var run = new StringBuilder();
        for (int i = offset; i < end; i++)
        {
            byte b = data[i];
            if (b is >= 32 and < 127)
            {
                run.Append((char)b);
            }
            else if (run.Length > 0)
            {
                if (run.Length >= 4 && run.ToString().Count(char.IsLetter) >= 3)
                {
                    if (sb.Length > 0)
                    {
                        sb.Append(' ');
                    }

                    sb.Append(run.ToString().Trim());
                }

                run.Clear();
            }
        }

        return sb.ToString().Trim();
    }

    private static bool TryScale(int raw, out double value)
    {
        value = 0;
        if (raw == 0 || raw == -1 || raw == int.MinValue)
        {
            return false;
        }

        long abs = raw < 0 ? -(long)raw : raw;
        if (abs >= 1000 && abs <= 100_000_000 && abs % 10 == 0)
        {
            value = raw / 10000.0;
            return Math.Abs(value) is >= 0.01 and <= 100000;
        }

        if (abs is >= 1 and <= 9999)
        {
            value = raw;
            return true;
        }

        return false;
    }

    private static string FormatMeasure(double value, bool inch)
    {
        double shown = inch ? value / 25.4 : value;
        if (Math.Abs(shown - Math.Round(shown)) < 0.0001 && Math.Abs(shown) >= 1 && Math.Abs(shown) < 10000 && !inch)
        {
            return shown.ToString("0", CultureInfo.InvariantCulture);
        }

        return shown.ToString("0.####", CultureInfo.InvariantCulture);
    }

    private static string WorkNumberFrom(string path, string comment)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        var digits = new string(name.Where(char.IsDigit).ToArray());
        if (digits.Length >= 4)
        {
            return digits;
        }

        digits = new string(comment.TakeWhile(ch => char.IsDigit(ch) || ch == ' ').Where(char.IsDigit).ToArray());
        return digits.Length > 0 ? digits : name;
    }

    private static string Esc(string text) => System.Net.WebUtility.HtmlEncode(text);
}

public sealed record ProgramUnit(int Sequence, int Code, IReadOnlyList<double> Values, string Text, string Hex);

public sealed record ViewOptions(
    bool Inch,
    bool InsertProgramInfo,
    bool AddBinaryLine,
    bool ValidDetail,
    int R392,
    int R393,
    int R394,
    string MachineName,
    int Head,
    string? SyntaxLanguage = null);
