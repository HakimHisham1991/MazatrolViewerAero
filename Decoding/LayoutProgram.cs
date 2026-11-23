using System.Globalization;
using System.Reflection;
using System.Text;
using System.Xml.Linq;

namespace MazatrolViewerAero.Decoding;

/// <summary>
/// Renders Mazatrol units with the operation name and the column header used by the original program list.
/// Definitions come from the M6M, PBF, PBD, and PBG structure tables; PBM and T6M have their own readers.
/// </summary>
public static partial class LayoutProgram
{
    private const int Start = 0xFC;
    private const int Slot = 100;
    private const int Column = 11;

    public static bool TryAppend(StringBuilder sb, byte[] data, string path, bool html, bool inch)
    {
        var extension = System.IO.Path.GetExtension(path);
        List<Block> blocks;
        if (IsTurning(extension))
        {
            blocks = ParseTurning(data);
        }
        else if (Catalog.TryGet(extension, out var structure, out var displayed, out var m6m))
        {
            blocks = m6m
                ? ParseM6m(data, structure, displayed)
                : IsMachining(extension)
                    ? ParseMachining(data, structure, displayed)
                    : ParseStandard(data, structure, displayed);
        }
        else
        {
            return false;
        }

        if (blocks.Count == 0)
        {
            return false;
        }

        if (html)
        {
            sb.Append("<TABLE CELLSPACING=\"0\" CELLPADDING=\"0\">\r\n");
        }

        string? lastHeader = null;
        foreach (var block in blocks)
        {
            var labels = HeaderLabels(block).ToList();
            var header = string.Join("\t", labels);
            if (!string.Equals(header, lastHeader, StringComparison.Ordinal))
            {
                if (block.IsUnitHeader && lastHeader != null)
                {
                    WriteGap(sb, html);
                }

                WriteRow(sb, html, labels.Select(label => (label, "rgb(0,255,255)")), header: true);
                lastHeader = header;
            }

            WriteRow(sb, html, DataCells(block, inch), header: false);
        }

        if (html)
        {
            sb.Append("</TABLE>\r\n");
        }

        return true;
    }

    private static List<(string Text, string Color)> DataCells(Block block, bool inch)
    {
        var cells = new List<(string Text, string Color)>();
        if (IsManualSequence(block))
        {
            cells.Add((block.NumberText(), "rgb(0,255,255)"));
            foreach (var cell in block.Cells)
            {
                cells.Add((Shown(cell, inch), ColorOf(cell)));
            }

            return cells;
        }

        if (block.Plain || block.Columns)
        {
            cells.Add((block.NumberText(), "rgb(0,255,255)"));
            foreach (var cell in block.Cells)
            {
                cells.Add((Shown(cell, inch), ColorOf(cell)));
            }

            return cells;
        }

        if (block.Lead != null)
        {
            cells.Add((block.Lead ?? "", "rgb(0,255,0)"));
            cells.Add((block.NumberText(), "rgb(0,255,255)"));
            foreach (var cell in block.Cells)
            {
                cells.Add((Shown(cell, inch), ColorOf(cell)));
            }

            return cells;
        }

        cells.Add((block.NumberText(), "rgb(0,255,255)"));
        cells.Add((block.NameText().Trim(), "rgb(0,255,0)"));
        foreach (var cell in block.ValueCells)
        {
            cells.Add((Shown(cell, inch), ColorOf(cell)));
        }

        return cells;
    }

    private static IEnumerable<string> HeaderLabels(Block block)
    {
        if (IsManualSequence(block))
        {
            yield return "SNo.";
            foreach (var cell in block.Cells)
            {
                yield return cell.Name;
            }

            yield break;
        }

        if (block.Plain || block.Columns)
        {
            yield return block.Columns ? block.UnitName : "SNo.";
            foreach (var cell in block.Cells)
            {
                yield return cell.Name;
            }

            yield break;
        }

        if (block.Lead != null || block.DisplayName != null)
        {
            yield return "SNo.";
            yield return "";
            foreach (var cell in block.Cells)
            {
                yield return cell.Name;
            }

            yield break;
        }

        if (block.UnitName == "MAT" && block.Cells.Count > 0 && block.Cells[0].Kind == CellKind.Text)
        {
            yield return "UNo.";
            yield return block.Cells[0].Name;
            foreach (var cell in block.ValueCells)
            {
                yield return cell.Name;
            }

            yield break;
        }

        if (!block.IsUnitHeader && block.Cells.Count > 0 && block.Cells[0].Kind == CellKind.Code)
        {
            yield return block.UnitName;
            yield return block.Cells[0].Name;
            foreach (var cell in block.ValueCells)
            {
                yield return cell.Name;
            }

            yield break;
        }

        yield return "UNo.";
        yield return "UNIT";
        foreach (var cell in block.Cells)
        {
            yield return cell.Name;
        }
    }

    private static string Shown(Cell cell, bool inch) => Format(cell, inch).Trim();

    private static string ColorOf(Cell cell)
    {
        if (cell.Kind == CellKind.Text)
        {
            return "rgb(0,255,0)";
        }

        if (cell.Kind == CellKind.Code)
        {
            return "rgb(255,255,0)";
        }

        return "rgb(255,255,255)";
    }

    private static void WriteRow(StringBuilder sb, bool html, IEnumerable<(string Text, string Color)> cells, bool header)
    {
        var values = cells.ToList();
        if (!html)
        {
            sb.AppendLine(string.Join("\t", values.Select(cell => cell.Text)));
            return;
        }

        sb.Append("<TR>");
        foreach (var cell in values)
        {
            sb.Append("<TD STYLE=\"color:");
            sb.Append(header ? "rgb(0,255,255)" : cell.Color);
            sb.Append("\">");
            sb.Append(cell.Text.Length == 0 ? "&nbsp;" : Esc(cell.Text));
            sb.Append("</TD>");
        }

        sb.Append("</TR>\r\n");
    }

    private static void WriteGap(StringBuilder sb, bool html)
    {
        if (!html)
        {
            sb.AppendLine();
            return;
        }

        sb.Append("<TR data-gap=\"1\"><TD STYLE=\"border:none;height:8px\">&nbsp;</TD></TR>\r\n");
    }

    private static string Format(Cell cell, bool inch)
    {
        if (cell.Text == "?")
        {
            return "?";
        }

        if (!cell.Defined || cell.Text == "*")
        {
            return "@";
        }

        if (cell.Kind == CellKind.Measure)
        {
            // A measure may carry its exact millimetre text; only an inch view has to recompute it.
            if (!inch && cell.Text.Length > 0)
            {
                return cell.Text;
            }

            var value = inch ? cell.Number / 25.4 : cell.Number;
            value = Math.Truncate(value * 1000) / 1000;
            if (Math.Abs(value - Math.Round(value)) < 0.0005)
            {
                return Math.Round(value).ToString("0", CultureInfo.InvariantCulture) + ".";
            }

            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        return cell.Text;
    }

    private static List<Block> ParseStandard(byte[] data, UnitDef[] structure, HashSet<int> displayed)
    {
        var blocks = new List<Block>();
        var index = 0;
        var type = -1;
        while (type != 4)
        {
            var address = Start + (index * Slot);
            index++;
            if (address >= data.Length)
            {
                break;
            }

            type = data[address];
            if (type >= structure.Length || !displayed.Contains(type))
            {
                continue;
            }

            var number = data[address + 2];
            var built = type switch
            {
                6 => ManualHeader(data, address, number),
                18 => TransferUnit(data, address, number),
                161 => ManualLine(data, address, number),
                168 => TurnFigure(data, address, number),
                170 => FacingFigure(data, address, number),
                172 => GrooveFigure(data, address, number),
                176 => DrillTool(data, address, number),
                177 => LineMillTool(data, address, number),
                178 => PocketMillTool(data, address, number),
                180 => TurnTool(data, address, number),
                185 => DrillFigure(data, address, number),
                _ => ReadBlock(data, structure[type], type, address, number)
            };
            if (built.IsUnitHeader && address + Slot + 1 < data.Length && data[address + Slot] is 245 or 250)
            {
                built = built with { Badge = "+ " };
            }

            blocks.Add(built);
        }

        return blocks;
    }

    private static List<Block> ParseM6m(byte[] data, UnitDef[] structure, HashSet<int> displayed)
    {
        var blocks = new List<Block>();
        var index = 0;
        var expectTool = false;
        var headerNumber = -1;
        var matAddress = Start;
        while (true)
        {
            var address = Start + (index * Slot);
            var slot = index;
            index++;
            if (address + 2 >= data.Length)
            {
                break;
            }

            int rawType = data[address];
            int rawNum = data[address + 2];
            if (address + Slot > data.Length || (rawType == 0 && rawNum == 0))
            {
                break;
            }

            if (IsFigure(data, address) || IsGcode(data, address))
            {
                continue;
            }

            if (IsManual(data, address))
            {
                foreach (var manual in ManualBlocks(data, address, ++headerNumber))
                {
                    blocks.Add(manual);
                }

                continue;
            }

            var (structureId, nextExpect) = ResolveM6m(slot, rawType, rawNum, expectTool);
            expectTool = nextExpect;
            var adopted = false;
            if (structureId < 0 && address + 50 < data.Length && IsMill(data[address + 50]) && IsTool(data, address + 50))
            {
                var last = address + 50;
                while (last + 50 < data.Length && IsMill(data[last + 50]) && IsTool(data, last + 50))
                {
                    last += 50;
                }

                structureId = data[last];
                address = last;
                adopted = true;
            }

            if (!adopted && IsMill(structureId) && address >= 50 && IsMill(data[address - 50]) && IsTool(data, address - 50))
            {
                continue;
            }

            if (structureId < 0 || !displayed.Contains(structureId))
            {
                continue;
            }

            var number = IsHeader(structureId) ? ++headerNumber : rawNum;
            if (slot == 0)
            {
                matAddress = address;
            }

            if (structureId is 177 or 193 or 194)
            {
                continue;
            }

            var built = ReadBlock(data, structure[structureId], structureId, address, number);
            foreach (var block in Adjust(data, address, structureId, built))
            {
                if (!block.IsUnitHeader && block.ValueCells.All(cell => !cell.Defined))
                {
                    continue;
                }

                if (block.UnitName == "SNo" && block.ValueCells.Count(cell => cell.Kind == CellKind.Measure && cell.Defined) < 2)
                {
                    continue;
                }

                blocks.Add(block);
            }
            if (structureId == 1 && HasWpc(data, matAddress))
            {
                var wpc = WithZeros(ReadBlock(data, structure[2], 2, matAddress, ++headerNumber), "ADD. WPC", "th");
                var wpcCells = wpc.Cells.ToList();
                wpcCells.Add(Blank("C"));
                wpcCells.Add(Blank("A"));
                blocks.Add(wpc with { Cells = wpcCells });
            }
        }

        AppendProgramTail(data, blocks, ref headerNumber);
        return blocks;
    }

    private static (int Id, bool ExpectTool) ResolveM6m(int slot, int rawType, int unitNum, bool expectTool)
    {
        if (slot == 0 && rawType == 0)
        {
            return (1, false);
        }

        if (rawType is 96 or 99 or 66 or 67 or 32 or 38 or 55 or 6)
        {
            return (rawType, true);
        }

        if (rawType is 5 or 4)
        {
            return (rawType, false);
        }

        if (rawType == 0)
        {
            if (unitNum is 1 or 8)
            {
                return (161, false);
            }

            if (unitNum == 62)
            {
                return (193, false);
            }

            if (unitNum == 6)
            {
                return (expectTool ? 177 : 194, false);
            }
        }

        return (-1, expectTool);
    }

    private static bool IsHeader(int id) => id is 1 or 2 or 4 or 5 or 6 or 32 or 38 or 55 or 66 or 67 or 96 or 99;

    private static bool IsMill(int id) => id is 66 or 67 or 96 or 99;

    private static bool HasWpc(byte[] data, int matAddress)
    {
        foreach (var offset in new[] { 66, 70, 74, 78 })
        {
            if (matAddress + offset + 4 <= data.Length && BitConverter.ToInt32(data, matAddress + offset) != 0)
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<Block> Adjust(byte[] data, int address, int typeId, Block block)
    {
        if (typeId is 96 or 99 or 66 or 67)
        {
            yield return FillMillHeader(data, FirstTool(data, address), block);
            foreach (var tool in ToolRows(data, address))
            {
                yield return tool;
            }

            foreach (var figure in FigureChain(data, address))
            {
                yield return figure;
            }

            yield break;
        }

        yield return block;
    }

    private static Block FillMillHeader(byte[] data, int address, Block block)
    {
        var cells = block.Cells.ToList();
        var pairs = EqualPairs(data, Math.Max(0, address - 80), address).ToList();
        if (pairs.Count > 0)
        {
            AssignPair(cells, pairs, 0, "DEPTH", "SRV-Z");
            var finish = pairs.FindIndex(pair => Math.Abs(pair.Value) <= 5);
            if (finish >= 0)
            {
                AssignPair(cells, pairs, finish, "FIN-Z", "FIN-R");
            }

            for (var i = Math.Max(0, pairs[0].At - 16); i + 1 < pairs[0].At; i++)
            {
                if (data[i] == 1 && data[i + 1] == 1)
                {
                    SetWord(cells, "BTM", 1);
                    SetWord(cells, "WAL", 1);
                    break;
                }
            }

            SetMeasure(cells, "INTER-R", 0);
            SetMeasure(cells, "CHMF", 0);
        }
        else
        {
            var preamble = ScaledValues(data, Math.Max(0, address - 96), address);
            var stock = preamble.FirstOrDefault(value => Math.Abs(value - 1) < 0.0001);
            var finish = preamble.FirstOrDefault(value => Math.Abs(value - 0.1) < 0.0001);
            if (stock != 0)
            {
                SetMeasure(cells, "DEPTH", 0);
                SetMeasure(cells, "SRV-Z", stock);
            }

            if (finish != 0)
            {
                SetMeasure(cells, "FIN-Z", finish);
            }

            if (stock != 0)
            {
                SetWord(cells, "BTM", 1);
            }
        }

        ClearLarge(cells, "BTM", 20);
        ClearLarge(cells, "WAL", 20);
        return block with { Cells = cells };
    }

    private static void AssignPair(List<Cell> cells, List<(int At, double Value)> pairs, int index, string first, string second)
    {
        if (index < 0 || index >= pairs.Count)
        {
            return;
        }

        SetMeasure(cells, first, pairs[index].Value);
        SetMeasure(cells, second, pairs[index].Value);
    }

    private static void ClearLarge(List<Cell> cells, string name, double limit)
    {
        var index = cells.FindIndex(cell => cell.Name == name);
        if (index >= 0 && cells[index].Defined && Math.Abs(cells[index].Number) > limit)
        {
            cells[index] = cells[index] with { Defined = false };
        }
    }

    private static void SetWord(List<Cell> cells, string name, int value)
    {
        var index = cells.FindIndex(cell => cell.Name == name);
        if (index >= 0)
        {
            cells[index] = cells[index] with
            {
                Kind = CellKind.Word,
                Text = value.ToString(CultureInfo.InvariantCulture),
                Number = value,
                Defined = true
            };
        }
    }

    private static void SetMeasure(List<Cell> cells, string name, double value)
    {
        var index = cells.FindIndex(cell => cell.Name == name);
        if (index >= 0)
        {
            cells[index] = cells[index] with { Number = value, Defined = true, Text = "" };
        }
    }

    private static int FirstTool(byte[] data, int address)
    {
        while (IsTool(data, address - 50))
        {
            address -= 50;
        }

        return address;
    }

    private static IEnumerable<Block> ToolRows(byte[] data, int address)
    {
        var starts = new List<int>();
        var cursor = address;
        while (IsTool(data, cursor))
        {
            starts.Add(cursor);
            var previous = cursor - 50;
            if (!IsTool(data, previous))
            {
                break;
            }

            cursor = previous;
        }

        starts.Reverse();
        for (var i = 0; i < starts.Count; i++)
        {
            yield return ToolBlock(data, starts[i], i + 1);
        }
    }

    private static bool IsTool(byte[] data, int address)
    {
        if (address < 0 || address + 48 > data.Length)
        {
            return false;
        }

        var family = data[address + 7];
        if (family is < 1 or > 19)
        {
            return false;
        }

        var number = BitConverter.ToUInt16(data, address + 4);
        if (number is < 1 or > 80)
        {
            return false;
        }

        if (!TryScaled(data, address + 16, out var nominal) || nominal is < 0.5 or > 200)
        {
            return false;
        }

        var speed = BitConverter.ToInt32(data, address + 40);
        return speed is > 0 and <= 20000;
    }

    private static Block ToolBlock(byte[] data, int address, int sequence)
    {
        var role = data[address + 6] == 2 ? "F" : "R";
        var tool = ToolName(data[address + 7]);
        var number = ToolNumber(data[address + 8], BitConverter.ToUInt16(data, address + 4));
        var approach = data[address + 15] switch
        {
            0x01 => "XBI",
            0x02 => "YBI",
            0x11 => "CCW",
            0x12 => "CW",
            0x00 => "@",
            _ => data[address + 15].ToString(CultureInfo.InvariantCulture)
        };
        var feedMode = data[address + 15] == 0x01 ? "@" : "G01";
        TryScaled(data, address + 16, out var nominal);
        TryScaled(data, address + 20, out var approachX);
        TryScaled(data, address + 24, out var approachY);
        TryScaled(data, address + 28, out var depth);
        TryScaled(data, address + 32, out var width);
        TryScaled(data, address + 44, out var feed);
        var speed = BitConverter.ToInt32(data, address + 40);
        var firstCode = BitConverter.ToUInt16(data, address + 10);
        var secondCode = BitConverter.ToUInt16(data, address + 12);
        Cell Measure(string name, double value, bool defined) => new(name, CellKind.Measure, "", value, defined);
        var cells = new List<Cell>
        {
            new("TOOL", CellKind.Text, tool, 0, true),
            Measure("NOM-Ø", nominal, true),
            new("No.", CellKind.Code, number, 0, true),
            Measure("APRCH-X", approachX, approachX != 0),
            Measure("APRCH-Y", approachY, approachY != 0),
            new("TYPE", CellKind.Word, approach, 0, approach != "@"),
            new("ZFD", CellKind.Word, feedMode, 0, feedMode != "@"),
            Measure("DEP-Z", depth, depth != 0),
            Measure("WID-R", width, width != 0),
            new("C-SP", CellKind.Word, speed.ToString(CultureInfo.InvariantCulture), speed, true),
            Measure("FR", feed, feed != 0),
            new("M", CellKind.Word, firstCode.ToString(CultureInfo.InvariantCulture), firstCode, firstCode != 0),
            new("M", CellKind.Word, secondCode.ToString(CultureInfo.InvariantCulture), secondCode, secondCode != 0)
        };
        return new Block("SNo", sequence, false, cells) { Lead = role };
    }

    private static Block PocketMillTool(byte[] data, int address, int sequence)
    {
        var role = address + 8 < data.Length && data[address + 8] == 2 ? "F" : "R";
        var family = address + 9 < data.Length ? data[address + 9] : (byte)0;
        var tool = family == 15 ? "END MILL" : ToolName(family);
        var approachCode = address + 15 < data.Length ? data[address + 15] : (byte)0;
        var type = approachCode switch
        {
            0x01 => "XBI",
            0x02 => "YBI",
            0x11 => "CCW",
            0x12 => "CW",
            _ => "@"
        };
        var feedMode = approachCode is 0x11 or 0x12 ? "G01" : "@";
        TryScaled(data, address + 36, out var nominal);
        double depth = 0;
        var depthDefined = role != "F" && TryScaled(data, address + 48, out depth);
        TryScaled(data, address + 52, out var width);
        TryScaled(data, address + 64, out var feed);
        var speed = address + 64 <= data.Length ? BitConverter.ToInt32(data, address + 60) : 0;
        var code = address + 26 <= data.Length ? BitConverter.ToUInt16(data, address + 24) : (ushort)0;
        Cell Measure(string name, double value, bool defined) => new(name, CellKind.Measure, "", value, defined);
        Cell ApproachCell(string name, int at) => TryScaled(data, at, out var value)
            ? Measure(name, value, true)
            : new Cell(name, CellKind.Code, "?", 0, true);
        var cells = new List<Cell>
        {
            new("TOOL", CellKind.Text, tool, 0, true),
            Measure("NOM-Ø", nominal, nominal != 0),
            new("No. #", CellKind.Word, "", 0, true),
            ApproachCell("APRCH-1", address + 28),
            ApproachCell("APRCH-2", address + 32),
            new("TYPE", CellKind.Word, type, 0, type != "@"),
            new("AFD TYPE", CellKind.Word, feedMode, 0, feedMode != "@"),
            Measure("PK-DEP", 0, false),
            Measure("DEP-A", depthDefined ? depth : 0, depthDefined),
            Measure("WID-R", width, width != 0),
            new("C-SP", CellKind.Word, speed.ToString(CultureInfo.InvariantCulture), speed, speed != 0),
            Measure("FR", feed, feed != 0),
            new("M", CellKind.Word, code.ToString(CultureInfo.InvariantCulture), code, code != 0),
            new("M", CellKind.Word, "", 0, true),
            new("M", CellKind.Word, "", 0, true)
        };
        return new Block("SNo", sequence, false, cells) { Lead = role };
    }

    private static Block LineMillTool(byte[] data, int address, int sequence)
    {
        var role = address + 8 < data.Length && data[address + 8] == 2 ? "F" : "R";
        var family = address + 9 < data.Length ? data[address + 9] : (byte)0;
        var tool = family == 15 ? "END MILL" : ToolName(family);
        var letter = (address + 10 < data.Length ? data[address + 10] : (byte)0) switch
        {
            0x01 => "A",
            0x02 => "B",
            0x03 => "C",
            0x10 => "R",
            0x11 => "S",
            0x12 => "T",
            _ => ""
        };
        var approachCode = address + 15 < data.Length ? data[address + 15] : (byte)0;
        var type = approachCode switch
        {
            0x01 => "XBI",
            0x02 => "YBI",
            0x11 => "CCW",
            0x12 => "CW",
            _ => "@"
        };
        var feedMode = address + 14 < data.Length && data[address + 14] == 0x80
            ? "G00"
            : approachCode is 0x11 or 0x12 ? "G01" : "@";
        bool ReadMeasure(int at, out double value)
        {
            value = 0;
            if (at < 0 || at + 4 > data.Length)
            {
                return false;
            }

            var raw = BitConverter.ToInt32(data, at);
            if (raw == 0 || raw % 65536 == 0 || Math.Abs((long)raw) is < 100 or > 100_000_000)
            {
                return false;
            }

            value = raw / 10000d;
            return true;
        }

        ReadMeasure(address + 36, out var nominal);
        ReadMeasure(address + 40, out var approach1);
        ReadMeasure(address + 44, out var approach2);
        var depthDefined = ReadMeasure(address + 48, out var depth);
        var widthDefined = ReadMeasure(address + 52, out var width);
        var feedDefined = ReadMeasure(address + 64, out var feed);
        var speed = address + 64 <= data.Length ? BitConverter.ToInt32(data, address + 60) : 0;
        var first = address + 26 <= data.Length ? BitConverter.ToUInt16(data, address + 24) : (ushort)0;
        var second = address + 28 <= data.Length ? BitConverter.ToUInt16(data, address + 26) : (ushort)0;
        Cell Measure(string name, double value, bool defined) => new(name, CellKind.Measure, "", value, defined);
        var cells = new List<Cell>
        {
            new("TOOL", CellKind.Text, tool, 0, true),
            Measure("NOM-Ø", nominal, nominal != 0),
            new("No.", CellKind.Text, letter, 0, letter.Length > 0),
            Measure("APRCH-1", approach1, approach1 != 0),
            Measure("APRCH-2", approach2, approach2 != 0),
            new("TYPE", CellKind.Word, type, 0, type != "@"),
            new("AFD", CellKind.Word, feedMode, 0, feedMode != "@"),
            Measure("DEP-A", depth, depthDefined),
            Measure("WID-R", width, widthDefined),
            new("C-SP", CellKind.Word, speed.ToString(CultureInfo.InvariantCulture), speed, speed > 0),
            Measure("FR", feed, feedDefined),
            new("M", CellKind.Word, first.ToString(CultureInfo.InvariantCulture), first, first != 0),
            new("M", CellKind.Word, second.ToString(CultureInfo.InvariantCulture), second, second != 0),
            new("M", CellKind.Word, "", 0, true)
        };
        return new Block("SNo", sequence, false, cells) { Lead = role };
    }

    private static Block ManualHeader(byte[] data, int address, int number)
    {
        var family = address + 9 < data.Length ? data[address + 9] : (byte)0;
        var qualifier = address + 10 < data.Length ? data[address + 10] : (byte)0;
        var tool = TurnToolLabel(family, qualifier);
        var nominal = TurnNominal(family, Scaled(data, address + 36));
        var change = address + 24 < data.Length && data[address + 24] == 5 ? "********" : "RETURN";
        var cells = new List<Cell>
        {
            new("TOOL", CellKind.Text, tool, 0, true),
            MeasureCell("NOM-Ø", nominal, true),
            new("No.", CellKind.Text, Letter(data, address + 11), 0, true),
            new("CHANGE-PT", CellKind.Text, change, 0, true)
        };
        return new Block("MANL PRG", number, true, cells);
    }

    private static Block ManualLine(byte[] data, int address, int number)
    {
        var flags = address + 4 < data.Length ? data[address + 4] : (byte)0;
        var perMinute = data[address + 20] == 98 || data[address + 22] == 98;
        var cells = new List<Cell>
        {
            new("G1", CellKind.Word, (flags & 1) != 0 ? data[address + 20].ToString(CultureInfo.InvariantCulture) : "", 0, true),
            new("G2", CellKind.Word, (flags & 2) != 0 ? data[address + 22].ToString(CultureInfo.InvariantCulture) : "", 0, true)
        };
        for (var slot = 0; slot < 6; slot++)
        {
            var code = address + 8 + slot < data.Length ? data[address + 8 + slot] : (byte)0;
            cells.Add(new Cell("DATA " + (slot + 1), CellKind.Word, ManualData(data, address, code, slot, perMinute), 0, true));
        }

        var spindle = address + 64 <= data.Length ? BitConverter.ToInt32(data, address + 60) : 0;
        var motion = address + 26 <= data.Length ? BitConverter.ToUInt16(data, address + 24) : (ushort)0;
        cells.Add(new Cell("S", CellKind.Word, spindle > 0 ? spindle.ToString(CultureInfo.InvariantCulture) : "", 0, true));
        cells.Add(new Cell("M/B", CellKind.Word, (flags & 8) != 0 ? Motion(motion) : "", 0, true));
        return new Block("SNo", number, false, cells);
    }

    private static Block TurnTool(byte[] data, int address, int number)
    {
        var role = address + 8 < data.Length && data[address + 8] == 2 ? "F" : "R";
        var family = address + 9 < data.Length ? data[address + 9] : (byte)0;
        var qualifier = address + 10 < data.Length ? data[address + 10] : (byte)0;
        var parent = address + 20 < data.Length ? data[address + 20] : (byte)0;
        var barFinish = role == "F" && parent == 48;
        var depth = Scaled(data, address + 48);
        var finishX = Scaled(data, address + 68);
        var finishZ = Scaled(data, address + 72);
        var cells = new List<Cell>
        {
            new("TOOL", CellKind.Text, TurnToolLabel(family, qualifier), 0, true),
            MeasureCell("NOM.", TurnNominal(family, Scaled(data, address + 36)), true),
            new("No.", CellKind.Text, Letter(data, address + 11), 0, true),
            new("PAT.", CellKind.Word, role == "R" && parent == 48 ? "0" : "", 0, role == "R" && parent == 48),
            MeasureCell("DEP-1", depth, !barFinish && depth != 0),
            MeasureCell("DEP-2/NUM.", 0, false),
            MeasureCell("DEP-3", 0, false),
            MeasureCell("FIN-X", finishX, barFinish || finishX != 0),
            MeasureCell("FIN-Z", finishZ, barFinish || finishZ != 0),
            Whole("C-SP", address + 64 <= data.Length ? BitConverter.ToInt32(data, address + 60) : 0),
            MeasureCell("FR", Scaled(data, address + 64), Scaled(data, address + 64) != 0),
            Whole("M", address + 26 <= data.Length ? BitConverter.ToUInt16(data, address + 24) : 0, emptyWhenZero: true),
            Whole("M", address + 28 <= data.Length ? BitConverter.ToUInt16(data, address + 26) : 0, emptyWhenZero: true),
            Whole("M", address + 36 <= data.Length ? BitConverter.ToUInt16(data, address + 32) : 0, emptyWhenZero: true)
        };
        return new Block("SNo", number, false, cells) { Lead = role };
    }

    private static Block TurnFigure(byte[] data, int address, int number)
    {
        var pattern = address + 8 < data.Length ? data[address + 8] : (byte)0;
        var name = pattern switch
        {
            1 => "LIN",
            2 => "TPR",
            4 => "ARC2",
            5 => "CTR",
            _ => pattern.ToString(CultureInfo.InvariantCulture)
        };
        var line = pattern == 1;
        return new Block("FIG", number, false, new List<Cell>
        {
            new("PTN", CellKind.Code, name, pattern, true),
            Corner("S-CNR", Scaled(data, address + 52), blank: true),
            MeasureCell("SPT-X", Scaled(data, address + 36), !line),
            MeasureCell("SPT-Z", Scaled(data, address + 40), !line),
            MeasureCell("FPT-X", Scaled(data, address + 44), true),
            MeasureCell("FPT-Z", Scaled(data, address + 48), true),
            Corner("F-CNR/$", Scaled(data, address + 56), blank: true),
            OptionalMeasure("R/th", pattern == 2 ? Scaled(data, address + 60) : 0, blankWhenZero: false),
            OptionalMeasure("RGH", Scaled(data, address + 64), blankWhenZero: true)
        });
    }

    private static Block GrooveFigure(byte[] data, int address, int number)
    {
        return new Block("FIG", number, false, new List<Cell>
        {
            new("S-CNR", CellKind.Code, "", 0, true),
            MeasureCell("SPT-X", Scaled(data, address + 36), true),
            MeasureCell("SPT-Z", Scaled(data, address + 40), true),
            MeasureCell("FPT-X", Scaled(data, address + 44), true),
            MeasureCell("FPT-Z", 0, false),
            new("F-CNR", CellKind.Word, "", 0, true),
            new("ANG", CellKind.Word, "", 0, true),
            new("RGH", CellKind.Word, "", 0, true)
        });
    }

    private static Block FacingFigure(byte[] data, int address, int number)
    {
        return new Block("FIG", number, false, new List<Cell>
        {
            MeasureCell("SPT-X", Scaled(data, address + 36), true),
            MeasureCell("SPT-Z", Scaled(data, address + 40), true),
            MeasureCell("FPT-X", Scaled(data, address + 44), true),
            MeasureCell("FPT-Z", Scaled(data, address + 48), true),
            new("RGH", CellKind.Word, "", 0, true)
        })
        { Columns = true };
    }

    private static Block DrillTool(byte[] data, int address, int number)
    {
        var peck = address + 15 < data.Length ? data[address + 15] : (byte)0;
        var depth = Scaled(data, address + 56);
        var cells = new List<Cell>
        {
            new("TOOL", CellKind.Text, "DRILL", 0, true),
            MeasureCell("NOM-Ø", Scaled(data, address + 36), true),
            new("No.", CellKind.Text, Letter(data, address + 11), 0, true),
            MeasureCell("HOLE-Ø", Scaled(data, address + 40), true),
            MeasureCell("HOLE-DEP", Scaled(data, address + 44), true),
            MeasureCell("PRE-DIA", Scaled(data, address + 48), true),
            Whole("PRE-DEP", address + 56 <= data.Length ? BitConverter.ToInt32(data, address + 52) : 0),
            new("RGH", CellKind.Word, peck switch { 3 => "PCK2", 6 => "PCK4", 0 => "", _ => "PCK" + peck }, 0, peck != 0),
            new("DEPTH", CellKind.Word, depth == 0 ? "" : "T " + MeasureText(depth), 0, depth != 0)
        };
        cells.Add(Whole("C-SP", address + 64 <= data.Length ? BitConverter.ToInt32(data, address + 60) : 0));
        cells.Add(MeasureCell("FR", Scaled(data, address + 64), Scaled(data, address + 64) != 0));
        cells.Add(Whole("M", address + 26 <= data.Length ? BitConverter.ToUInt16(data, address + 24) : 0, emptyWhenZero: true));
        cells.Add(Whole("M", address + 28 <= data.Length ? BitConverter.ToUInt16(data, address + 26) : 0, emptyWhenZero: true));
        cells.Add(Whole("M", 0, emptyWhenZero: true));
        return new Block("SNo", number, false, cells) { Plain = true };
    }

    private static Block DrillFigure(byte[] data, int address, int number)
    {
        var pattern = address + 8 < data.Length ? data[address + 8] : (byte)0;
        return new Block("FIG", number, false, new List<Cell>
        {
            new("PTN", CellKind.Code, pattern == 1 ? "PT" : pattern.ToString(CultureInfo.InvariantCulture), pattern, true),
            MeasureCell("SPT-R/x", Scaled(data, address + 36), true),
            MeasureCell("SPT-C/y", Scaled(data, address + 40), true),
            MeasureCell("SPT-Z", Scaled(data, address + 44), true),
            MeasureCell("NUM.", 0, false),
            MeasureCell("ANG", 0, false),
            MeasureCell("Q", 0, false),
            MeasureCell("R", 0, true)
        });
    }

    private static Block TransferUnit(byte[] data, int address, int number)
    {
        var pattern = address + 20 < data.Length ? data[address + 20] : (byte)0;
        var move = pattern == 3;
        var second = address + 8 < data.Length && data[address + 8] != 0;
        var cells = new List<Cell>
        {
            new("PAT.", CellKind.Code, pattern switch { 2 => "BAR", 3 => "MOVE", _ => pattern.ToString(CultureInfo.InvariantCulture) }, pattern, pattern != 0),
            Whole("HEAD", WordAt(data, address + 22)),
            Whole("SPDL", WordAt(data, address + 24)),
            new("PUSH", CellKind.Word, "", 0, false),
            Whole("CHUCK", WordAt(data, address + 26)),
            MeasureCell("W1", Scaled(data, address + 36), !move && !second),
            MeasureCell("W2", Scaled(data, address + 40), move || second),
            MeasureCell("Z-OFFSET", Scaled(data, address + 52), Scaled(data, address + 52) != 0)
        };
        return new Block("TRANSFER", number, true, cells);
    }

    private static string TurnToolLabel(byte family, byte qualifier)
    {
        var name = family switch
        {
            0x02 => "DRILL",
            0x21 => "GENERAL",
            0x22 => "GROOVE",
            0x24 => "T-DRILL",
            0x2B => "SPECIAL",
            _ => "TOOL"
        };
        var side = family == 0x2B
            ? (qualifier & 0x0F).ToString("0000", CultureInfo.InvariantCulture)
            : family is 0x21 or 0x22 && qualifier == 1 ? "OUT" : "";
        return side.Length == 0 ? name : name + "  " + side;
    }

    private static double TurnNominal(byte family, double stored) =>
        family is 0x21 or 0x22 or 0x2B ? stored * 100d : stored;

    private static string Letter(byte[] data, int address)
    {
        if (address < 0 || address >= data.Length || data[address] is < 1 or > 26)
        {
            return "";
        }

        return ((char)('A' + data[address] - 1)).ToString();
    }

    private static string ManualData(byte[] data, int address, byte code, int slot, bool perMinute)
    {
        if (code == 0)
        {
            return "";
        }

        var axis = code switch
        {
            0x0F => "F",
            0x1E => "U",
            0x1F => "V",
            0x20 => "W",
            0x21 => "X",
            0x22 => "Y",
            0x23 => "Z",
            _ => ""
        };
        if (axis.Length == 0)
        {
            return "";
        }

        var raw = address + 36 + (slot * 4) + 4 <= data.Length ? BitConverter.ToInt32(data, address + 36 + (slot * 4)) : 0;
        var text = axis == "F" && perMinute
            ? raw.ToString(CultureInfo.InvariantCulture)
            : MeasureText(raw / 10000d);
        return axis + " " + text;
    }

    private static string Motion(int value) =>
        "M" + value.ToString(CultureInfo.InvariantCulture).PadLeft(3);

    private static int WordAt(byte[] data, int address) =>
        address >= 0 && address + 2 <= data.Length ? BitConverter.ToUInt16(data, address) : 0;

    private static double Scaled(byte[] data, int address)
    {
        if (address < 0 || address + 4 > data.Length)
        {
            return 0;
        }

        var raw = BitConverter.ToInt32(data, address);
        if (raw == 0 || raw % 65536 == 0 || Math.Abs((long)raw) is < 100 or > 100_000_000)
        {
            return 0;
        }

        return raw / 10000d;
    }

    private static string MeasureText(double value)
    {
        value = Math.Truncate(value * 1000d) / 1000d;
        if (Math.Abs(value - Math.Round(value)) < 0.0005)
        {
            return Math.Round(value).ToString("0", CultureInfo.InvariantCulture) + ".";
        }

        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static Cell MeasureCell(string name, double value, bool defined) =>
        new(name, CellKind.Measure, "", value, defined);

    private static Cell OptionalMeasure(string name, double value, bool blankWhenZero) =>
        value == 0
            ? new Cell(name, CellKind.Word, "", 0, blankWhenZero)
            : new Cell(name, CellKind.Measure, "", value, true);

    private static Cell Whole(string name, int value, bool emptyWhenZero = false) =>
        value == 0
            ? new Cell(name, CellKind.Word, "", 0, emptyWhenZero)
            : new Cell(name, CellKind.Word, value.ToString(CultureInfo.InvariantCulture), value, true);

    private static Cell Corner(string name, double value, bool blank) =>
        value == 0
            ? new Cell(name, CellKind.Word, "", 0, blank)
            : new Cell(name, CellKind.Word, "R " + MeasureText(value), value, true);

    private static string ToolName(byte family) => family switch
    {
        1 => "CTR-DR",
        2 => "DRILL",
        3 => "REAM",
        >= 4 and <= 9 => "TAP",
        10 => "BK FACE",
        11 => "BOR BAR",
        12 => "B-B BAR",
        13 => "CHF-C",
        14 => "F-MILL",
        15 => "E-MILL",
        16 => "OTHER",
        17 => "CHP VAC",
        18 => "T.SENS.",
        19 => "B-E-MIL",
        _ => "TOOL"
    };

    private static string ToolNumber(byte flag, int number)
    {
        var code = flag & 0x7F;
        var letter = code switch
        {
            0x01 => "A",
            0x02 => "B",
            0x03 => "C",
            0x10 => "R",
            0x11 => "S",
            0x12 => "T",
            _ => ""
        };
        var text = number.ToString(CultureInfo.InvariantCulture);
        return letter.Length > 0 && number < 10 ? letter + " " + text : letter + text;
    }

    private static bool IsManual(byte[] data, int address)
    {
        if (address < 0 || address + 80 > data.Length || data[address] != 0)
        {
            return false;
        }

        var family = data[address + 7];
        if (family is < 1 or > 19)
        {
            return false;
        }

        if (!TryScaled(data, address + 16, out var nominal) || nominal is < 0.5 or > 200)
        {
            return false;
        }

        var speed = BitConverter.ToInt32(data, address + 40);
        return speed == 0 && IsGcode(data, address + 50);
    }

    private static bool IsGcode(byte[] data, int address)
    {
        if (address < 0 || address + 28 > data.Length)
        {
            return false;
        }

        var any = false;
        for (var i = 0; i < 3; i++)
        {
            var code = data[address + 4 + i];
            if (code == 0)
            {
                continue;
            }

            if (code is < 0x21 or > 0x26)
            {
                return false;
            }

            any = true;
        }

        return any && (data[address + 12] & 0x80) != 0;
    }

    private static IEnumerable<Block> ManualBlocks(byte[] data, int address, int number)
    {
        TryScaled(data, address + 16, out var nominal);
        var toolNumber = BitConverter.ToUInt16(data, address + 4).ToString(CultureInfo.InvariantCulture);
        var toolCells = new List<Cell>
        {
            new("TOOL", CellKind.Text, ToolName(data[address + 7]), 0, true),
            new("NOM-Ø", CellKind.Measure, "", nominal, true),
            new("No.", CellKind.Code, toolNumber, 0, true)
        };
        for (var i = 0; i < 6; i++)
        {
            toolCells.Add(Blank(""));
        }

        yield return new Block("MANU PRO", number, true, toolCells);

        var sequence = 0;
        for (var at = address + 50; IsGcode(data, at); at += 50)
        {
            sequence++;
            yield return GcodeBlock(data, at, sequence);
        }
    }

    private static Block GcodeBlock(byte[] data, int address, int sequence)
    {
        var flags = data[address + 2];
        var cells = new List<Cell>
        {
            new("G1", CellKind.Word, (flags & 1) != 0 ? data[address].ToString(CultureInfo.InvariantCulture) : "", 0, true),
            new("G2", CellKind.Word, (flags & 2) != 0 ? data[address + 1].ToString(CultureInfo.InvariantCulture) : "", 0, true)
        };
        for (var i = 0; i < 6; i++)
        {
            cells.Add(new Cell("DATA " + (i + 1), CellKind.Word, i < 3 ? AxisText(data, address, i) : "", 0, true));
        }

        cells.Add(new Cell("S", CellKind.Word, "", 0, true));
        var motion = BitConverter.ToUInt16(data, address + 14);
        cells.Add(new Cell("M/B", CellKind.Word, "M " + motion.ToString(CultureInfo.InvariantCulture), motion, true));
        return new Block("SNo", sequence, false, cells);
    }

    private static bool IsManualSequence(Block block) => block.Cells.Count > 0 && block.Cells[0].Name == "G1";

    private static string AxisText(byte[] data, int address, int slot)
    {
        var code = data[address + 4 + slot];
        if (code == 0)
        {
            return "";
        }

        var letter = (code & 0x0F) switch
        {
            1 => "X",
            2 => "Y",
            3 => "Z",
            4 => "A",
            5 => "B",
            6 => "C",
            _ => ""
        };
        if (letter.Length == 0)
        {
            return "";
        }

        var raw = BitConverter.ToInt32(data, address + 16 + (slot * 4));
        return letter + " " + Format(new Cell("", CellKind.Measure, "", raw / 10000d, true), false);
    }

    private static void AppendProgramTail(byte[] data, List<Block> blocks, ref int headerNumber)
    {
        if (blocks.Exists(block => block.UnitName == "SUB PRO"))
        {
            return;
        }

        var sub = -1;
        var work = 0;
        for (var i = Start; i + 18 < data.Length; i++)
        {
            if (data[i] != 5 || data[i + 1] != 0x0A)
            {
                continue;
            }

            var candidate = BitConverter.ToInt32(data, i + 14);
            if (candidate is >= 10000 and <= 99_999_999)
            {
                sub = i;
                work = candidate;
            }
        }

        if (sub < 0)
        {
            return;
        }

        blocks.Add(new Block("SUB PRO", ++headerNumber, true,
        [
            new Cell("WORK No.", CellKind.Word, work.ToString(CultureInfo.InvariantCulture), work, true),
            new Cell("$", CellKind.Word, "*", 0, false),
            new Cell("REPEAT", CellKind.Word, "", 0, true)
        ]));
        blocks.Add(new Block("END", ++headerNumber, true,
        [
            new Cell("CONTI.", CellKind.Word, "0", 0, true),
            new Cell("NUMBER", CellKind.Word, "0", 0, true),
            new Cell("ATC", CellKind.Word, "0", 0, true),
            new Cell("X", CellKind.Measure, "", 0, true),
            new Cell("Y", CellKind.Measure, "", 0, true),
            new Cell("Z", CellKind.Measure, "", 0, true),
            new Cell("C", CellKind.Word, "", 0, true),
            new Cell("A", CellKind.Word, "", 0, true)
        ]));
    }

    private static Block WithZeros(Block block, params string[] names)
    {
        var cells = block.Cells.ToList();
        foreach (var name in names)
        {
            var index = cells.FindIndex(cell => cell.Name == name);
            if (index < 0)
            {
                continue;
            }

            var cell = cells[index];
            if (cell.Defined && cell.Text is not ("*" or "?" or "") && Math.Abs(cell.Number) > 0.0000001)
            {
                continue;
            }

            cells[index] = cell.Kind == CellKind.Measure
                ? cell with { Number = 0, Defined = true, Text = "" }
                : cell with { Kind = CellKind.Word, Text = "0", Number = 0, Defined = true };
        }

        return block with { Cells = cells };
    }

    private static IEnumerable<Block> FigureChain(byte[] data, int toolAddress)
    {
        var start = FindFigure(data, toolAddress);
        if (start < 0)
        {
            yield break;
        }

        var sequence = 0;
        for (var at = start; sequence < 400 && IsFigure(data, at); at += 50)
        {
            sequence++;
            yield return FigureRecord(data, at, sequence);
        }
    }

    private static int FindFigure(byte[] data, int tool)
    {
        var from = tool + 50;
        var to = Math.Min(data.Length - 36, tool + 90);
        for (var i = from; i <= to; i++)
        {
            if (!IsFigure(data, i))
            {
                continue;
            }

            var pattern = BitConverter.ToUInt16(data, i);
            var coord = pattern == 16 ? i + 12 : i + 16;
            if (TryScaled(data, coord, out _) || TryScaled(data, coord + 4, out _))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsFigure(byte[] data, int address)
    {
        if (address < 0 || address + 36 > data.Length)
        {
            return false;
        }

        var pattern = BitConverter.ToUInt16(data, address);
        return pattern is 16 or 32 or 33 or 34 && BitConverter.ToUInt16(data, address + 2) <= 1;
    }

    private static Block FigureRecord(byte[] data, int address, int sequence)
    {
        var pattern = BitConverter.ToUInt16(data, address);
        if (pattern == 16)
        {
            return new Block("FIG", sequence, false,
            [
                new Cell("PTN", CellKind.Code, "SQR", 0, true),
                Coord(data, "P1X/CX", address + 12, true),
                Coord(data, "P1Y/CY", address + 16, true),
                Coord(data, "P3X/R", address + 20, true),
                Coord(data, "P3Y", address + 24, true),
                Blank("CN1"),
                Blank("CN2"),
                Blank("CN3"),
                Blank("CN4")
            ]);
        }

        var name = pattern switch { 33 => "CW", 34 => "CCW", _ => "LINE" };
        var arc = pattern is 33 or 34;
        return new Block("FIG", sequence, false,
        [
            new Cell("PTN", CellKind.Code, name, 0, true),
            Coord(data, "X", address + 16, true),
            Coord(data, "Y", address + 20, true),
            Coord(data, "R/th", address + 24, arc),
            Coord(data, "I", address + 28, arc),
            Coord(data, "J", address + 32, arc),
            Blank("P"),
            Blank("CNR")
        ]);
    }

    private static Cell Coord(byte[] data, string name, int address, bool show)
    {
        if (!show || address < 0 || address + 4 > data.Length)
        {
            return Blank(name);
        }

        var raw = BitConverter.ToInt32(data, address);
        if (raw == 0 || raw % 65536 == 0 || Sentinel(raw / 10000d))
        {
            return Blank(name);
        }

        return new Cell(name, CellKind.Measure, "", raw / 10000d, true);
    }

    private static Cell Blank(string name) => new(name, CellKind.Word, "", 0, true);

    private static List<double> ScaledValues(byte[] data, int from, int to)
    {
        var values = new List<double>();
        for (var i = from; i + 4 <= to; i += 2)
        {
            if (TryScaled(data, i, out var value))
            {
                values.Add(value);
            }
        }

        return values;
    }

    private static IEnumerable<(int At, double Value)> EqualPairs(byte[] data, int from, int to)
    {
        for (var i = from; i + 8 <= to; i += 2)
        {
            if (!TryScaled(data, i, out var first) || !TryScaled(data, i + 4, out var second) || Math.Abs(first - second) > 0.0001)
            {
                continue;
            }

            yield return (i, first);
            i += 6;
        }
    }

    private static bool Sentinel(double value)
    {
        double[] artifacts = [6.5536, 39.3216, 209.7152, 222.8286, 406.3232];
        var magnitude = Math.Abs(value);
        foreach (var artifact in artifacts)
        {
            if (Math.Abs(magnitude - artifact) < 0.002)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryScaled(byte[] data, int address, out double value)
    {
        value = 0;
        if (address < 0 || address + 4 > data.Length)
        {
            return false;
        }

        var raw = BitConverter.ToInt32(data, address);
        if (raw == 0 || raw % 65536 == 0 || Math.Abs((long)raw) is < 1000 or > 100_000_000 || Sentinel(raw / 10000d))
        {
            return false;
        }

        value = raw / 10000d;
        return true;
    }

    private static Block ReadBlock(byte[] data, UnitDef definition, int typeId, int address, int number)
    {
        var cells = new List<Cell>();
        var pattern = "";
        var skipNext = false;
        foreach (var parameter in definition.Parameters)
        {
            var cell = ReadCell(data, parameter, address, typeId);
            if (parameter.Kind == CellKind.Code)
            {
                pattern = cell.Text;
            }
            else if (parameter.VisibleFor.Count > 0 && !parameter.VisibleFor.Contains(pattern))
            {
                cell = cell with { Defined = false, Text = "*" };
            }

            if (skipNext)
            {
                cell = cell with { Defined = false, Text = "" };
                skipNext = false;
            }
            else if (typeId == 161 && cell.Text == "W")
            {
                cell = cell with { Defined = false, Text = "" };
                skipNext = true;
            }

            cells.Add(cell);
        }

        return new Block(definition.Name, number, !definition.SubUnit, cells);
    }

    private static Cell ReadCell(byte[] data, ParamDef parameter, int address, int typeId)
    {
        if (parameter.BlankAlways)
        {
            return new Cell(parameter.Name, CellKind.Word, "", 0, true);
        }

        var at = address + parameter.Offset;
        if (at < 0 || at >= data.Length)
        {
            return new Cell(parameter.Name, parameter.Kind, "", 0, false);
        }

        if (parameter.Name == "MULTI FLAG")
        {
            var mode = address + 9 < data.Length ? data[address + 9] : (byte)0;
            return mode == 3
                ? new Cell(parameter.Name, CellKind.Code, "TYPE", mode, true)
                : new Cell(parameter.Name, CellKind.Code, "*", 0, false);
        }

        switch (parameter.Kind)
        {
            case CellKind.Empty:
                return new Cell(parameter.Name, parameter.Kind, "*", 0, false);
            case CellKind.Fixed:
            {
                if (at + 4 > data.Length)
                {
                    return new Cell(parameter.Name, CellKind.Measure, "", 0, false);
                }

                uint raw = BitConverter.ToUInt32(data, at);
                if (parameter.BlankUnset && raw == 0)
                {
                    return new Cell(parameter.Name, CellKind.Word, "", 0, true);
                }

                var whole = raw / 65536d;
                if (parameter.Name == "RPM")
                {
                    var digits = Math.Round(whole).ToString("0", CultureInfo.InvariantCulture);
                    return new Cell(parameter.Name, CellKind.Word, digits, whole, raw != 0);
                }

                return BlankOr(parameter, new Cell(parameter.Name, CellKind.Measure, "", whole, raw != 0));
            }
            case CellKind.Measure:
            {
                if (at + 4 > data.Length)
                {
                    return new Cell(parameter.Name, parameter.Kind, "", 0, false);
                }

                int raw = BitConverter.ToInt32(data, at);
                var scaled = raw % 65536 != 0 && Math.Abs((long)raw) is >= 100 and <= 100_000_000;
                var defined = parameter.Name.StartsWith("PITCH-", StringComparison.Ordinal)
                    ? DefinedZero(data, address, parameter.Offset)
                    : scaled || DefinedZero(data, address, parameter.Offset);
                if (typeId == 160 || (raw == 0 && (parameter.ZeroDefined || parameter.Name == "ID-MIN")))
                {
                    defined = true;
                }

                return BlankOr(parameter, new Cell(parameter.Name, parameter.Kind, "", raw / 10000d, defined));
            }
            case CellKind.Text:
            {
                var text = ReadText(data, at);
                if (text.Length == 0 && parameter.Name.Contains("MAT", StringComparison.OrdinalIgnoreCase))
                {
                    text = ReadText(data, 0x150);
                    if (text.Length == 0)
                    {
                        text = ReadText(data, 0x100);
                    }
                }

                return new Cell(parameter.Name, parameter.Kind, text, 0, text.Length > 0);
            }
            case CellKind.Word:
            {
                if (at + 2 > data.Length)
                {
                    return new Cell(parameter.Name, parameter.Kind, "", 0, false);
                }

                int raw = BitConverter.ToUInt16(data, at);
                if (parameter.Name is "ATC MODE" or "MULTI MODE" && at + 1 < data.Length
                    && data[at] is >= 32 and < 127 && data[at + 1] is >= 32 and < 127)
                {
                    var label = parameter.Name == "MULTI MODE" ? "OFF" : "0";
                    return new Cell(parameter.Name, parameter.Kind, label, 0, true);
                }

                var defined = raw != 0 || parameter.ZeroDefined || parameter.Name == "ATC MODE";
                return BlankOr(parameter, new Cell(parameter.Name, parameter.Kind, raw.ToString(CultureInfo.InvariantCulture), raw, defined));
            }
            case CellKind.Byte:
            {
                int raw = data[at];
                return new Cell(parameter.Name, parameter.Kind, raw.ToString(CultureInfo.InvariantCulture), raw, raw != 0 || parameter.ZeroDefined);
            }
            case CellKind.Letter:
            {
                var letter = (char)('a' + data[at] - 10);
                return new Cell(parameter.Name, CellKind.Code, letter.ToString(), data[at], true);
            }
            case CellKind.Code:
            {
                var word = parameter.ToolPack
                    ? (data[address + 9] << 8) | data[Math.Min(data.Length - 1, address + 13)]
                    : data[at];
                var name = parameter.Options.FirstOrDefault(option => option.Value == word)?.Name;
                if (string.IsNullOrEmpty(name))
                {
                    name = parameter.Name == "MULTI MODE" && word is >= 32 and < 127
                        ? "OFF"
                        : word.ToString(CultureInfo.InvariantCulture);
                }

                return new Cell(parameter.Name, CellKind.Code, name, word, true);
            }
            default:
                return new Cell(parameter.Name, CellKind.Text, "?", 0, false);
        }
    }

    private static Cell BlankOr(ParamDef parameter, Cell cell) =>
        parameter.BlankUnset && !cell.Defined
            ? new Cell(parameter.Name, CellKind.Word, "", 0, true)
            : cell;

    private static bool DefinedZero(byte[] data, int address, int offset)
    {
        if (address + 30 >= data.Length)
        {
            return false;
        }

        byte flag = offset switch
        {
            48 or 52 => 0b0000_0100,
            56 => 0b0000_0001,
            64 => 0b0000_0010,
            _ => 0
        };
        return flag != 0 && (data[address + 30] & flag) != 0;
    }

    private static string ReadText(byte[] data, int address)
    {
        int end = Math.Min(data.Length, address + 16);
        int n = address;
        while (n < end && data[n] != 0)
        {
            n++;
        }

        if (n == address || !data[address..n].All(b => b is >= 32 and < 127))
        {
            return "";
        }

        return Encoding.ASCII.GetString(data, address, n - address).Trim();
    }

    private static string Esc(string text) => System.Net.WebUtility.HtmlEncode(text);

    private enum LineKind { Header, Blank }

    private enum CellKind { Empty, Fixed, Measure, Text, Word, Byte, Letter, Code }

    private sealed record Cell(string Name, CellKind Kind, string Text, double Number, bool Defined);

    private sealed record Block(string UnitName, int Number, bool IsUnitHeader, IReadOnlyList<Cell> Cells)
    {
        public string? Lead { get; init; }

        public string? HeaderOverride { get; init; }

        public string? DisplayName { get; init; }

        public string? Badge { get; init; }

        public bool Plain { get; init; }

        public bool Columns { get; init; }

        public string HeaderText()
        {
            if (HeaderOverride != null)
            {
                return HeaderOverride;
            }

            if (Lead != null)
            {
                return "SNo." + string.Concat(Cells.Select(cell => cell.Name.PadLeft(Column)));
            }

            var lead = IsUnitHeader ? "UNo. UNIT" : UnitName == "SNo" ? "SNo." : UnitName;
            var names = Cells.Select(cell => cell.Name);
            if (UnitName == "MAT" && Cells.Count > 0 && Cells[0].Kind == CellKind.Text)
            {
                lead = "UNo.";
            }

            var columns = names.Select(name => name.PadLeft(Column));
            return (lead + "      ").PadRight(14) + string.Concat(columns);
        }

        public IEnumerable<Cell> ValueCells =>
            (UnitName == "MAT" && Cells.Count > 0 && Cells[0].Kind == CellKind.Text)
            || (!IsUnitHeader && Cells.Count > 0 && Cells[0].Kind == CellKind.Code)
                ? Cells.Skip(1)
                : Cells;

        public string NumberText() => (Badge ?? "") + Number.ToString(CultureInfo.InvariantCulture);

        public string NameText()
        {
            if (DisplayName != null)
            {
                return DisplayName;
            }

            if (UnitName == "MAT" && Cells.Count > 0 && Cells[0].Kind == CellKind.Text)
            {
                return Cells[0].Text;
            }

            if (!IsUnitHeader && Cells.Count > 0 && Cells[0].Kind == CellKind.Code)
            {
                return Cells[0].Text;
            }

            return UnitName;
        }
    }

    private sealed record Option(string Name, int Value);

    private sealed record ParamDef(string Name, int Offset, CellKind Kind, HashSet<string> VisibleFor, Option[] Options, bool ToolPack, bool BlankUnset, bool BlankAlways, bool ZeroDefined);

    private sealed record UnitDef(int Id, string Name, ParamDef[] Parameters, bool SubUnit);

    private static class Catalog
    {
        private static readonly Dictionary<string, (UnitDef[] Structure, HashSet<int> Shown, bool M6m)> Cache = new(StringComparer.OrdinalIgnoreCase);

        public static bool TryGet(string extension, out UnitDef[] structure, out HashSet<int> shown, out bool m6m)
        {
            structure = [];
            shown = [];
            m6m = false;
            var key = extension.ToLowerInvariant() switch
            {
                ".m6m" => "m6m",
                ".pbf" or ".pbe" => "pbf",
                ".pbd" or ".pbm" or ".maz" => "pbd",
                ".pbg" => "pbg",
                _ => ""
            };
            if (key.Length == 0)
            {
                return false;
            }

            if (!Cache.TryGetValue(key, out var entry))
            {
                entry = (Load(key + "_structure.xml"), Shown(key), key == "m6m");
                Cache[key] = entry;
            }

            structure = entry.Structure;
            shown = entry.Shown;
            m6m = entry.M6m;
            return structure.Length > 0;
        }

        private static HashSet<int> Shown(string key) => key switch
        {
            "m6m" => [1, 2, 4, 5, 6, 32, 38, 55, 66, 67, 96, 99, 161, 176, 177, 178, 192, 193, 194],
            "pbf" => [1, 4, 6, 7, 18, 19, 32, 48, 51, 52, 53, 54, 55, 64, 99, 161, 168, 170, 172, 174, 176, 177, 178, 180, 185, 201, 202],
            "pbd" => [1, 2, 4, 5, 6, 12, 32, 35, 38, 64, 66, 68, 99, 160, 161, 176, 177, 178, 192, 193, 194],
            _ => [1, 4, 6, 48, 51, 52, 53, 54, 161, 168, 170, 171, 172, 173, 180]
        };

        private static UnitDef[] Load(string fileName)
        {
            var resource = "MazatrolViewerAero.Decoding.Structures." + fileName;
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException("Missing " + fileName);
            var root = XDocument.Load(stream).Root ?? throw new InvalidOperationException("Invalid " + fileName);
            var units = Enumerable.Range(0, 257).Select(id => new UnitDef(id, "TBD", [], false)).ToArray();
            foreach (var element in root.Elements("unit"))
            {
                if (!int.TryParse(element.Attribute("id")?.Value, out var id) || id < 0 || id >= units.Length)
                {
                    continue;
                }

                var name = element.Attribute("name")?.Value ?? "TBD";
                var parameters = element.Elements().Where(item => item.Name.LocalName is "parameter" or "paremeter").Select(ParseParameter).ToArray();
                units[id] = new UnitDef(id, name, parameters, name is "SNo" or "FIG" or "OFS");
            }

            return units;
        }

        private static ParamDef ParseParameter(XElement element)
        {
            var type = element.Attribute("type")?.Value ?? "";
            var kind = type switch
            {
                "NA" => CellKind.Empty,
                "blank" => CellKind.Word,
                "wholeNumber" => CellKind.Fixed,
                "readData" => CellKind.Measure,
                "text" => CellKind.Text,
                "readFullNumber2B" => CellKind.Word,
                "readFullNumber1B" or "partType" => CellKind.Byte,
                "readLetter" => CellKind.Letter,
                "readPattern" or "readPbdTool" or "readPbdMultiFlag" => CellKind.Code,
                _ => CellKind.Text
            };
            var visible = (element.Attribute("visible")?.Value ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.Ordinal);
            var options = element.Elements("enum")
                .Select(item => new Option(item.Attribute("name")?.Value ?? "", int.Parse(item.Attribute("value")?.Value ?? "0", CultureInfo.InvariantCulture)))
                .ToArray();
            return new ParamDef(
                element.Attribute("name")?.Value ?? "",
                int.Parse(element.Attribute("pos")?.Value ?? "0", CultureInfo.InvariantCulture),
                kind,
                visible,
                options,
                type == "readPbdTool",
                element.Attribute("blank")?.Value == "1",
                type == "blank",
                element.Attribute("zero")?.Value == "1");
        }
    }
}
