using System.Globalization;

namespace MazatrolViewerAero.Decoding;

/// <summary>
/// Machining-center programs saved as PBM or MAZ. The records use the MATRIX machining layout:
/// bytes +4..+5 of every record flag which fields were entered, and measures are int32 / 10000.
/// Field positions were confirmed against program lists printed by the MATRIX engine.
/// </summary>
public static partial class LayoutProgram
{
    private const int PbmType = 38;

    private static bool IsMachining(string extension) =>
        extension.Equals(".pbm", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".maz", StringComparison.OrdinalIgnoreCase);

    private static List<Block> ParseMachining(byte[] data, UnitDef[] structure, HashSet<int> displayed)
    {
        var blocks = new List<Block>();
        var start = RecordStart(data);
        var speedDivisor = start > 0 && Int32At(data, 0x20) == PbmType ? 10000 : 1;
        for (var address = start; address + Slot <= data.Length; address += Slot)
        {
            int type = data[address];
            int number = BitConverter.ToUInt16(data, address + 2);
            if (type == 222)
            {
                // Tool path control data sits between a unit and its tools; the list marks that unit with "+".
                if (blocks.Count > 0 && blocks[^1].IsUnitHeader)
                {
                    blocks[^1] = blocks[^1] with { Badge = "+ " };
                }

                continue;
            }

            var built = type switch
            {
                1 => MachiningMaterial(data, address, number),
                2 => MachiningWpc(data, address, number),
                4 => MachiningEnd(data, address, number),
                5 => MachiningSubProgram(data, address, number),
                6 => MachiningManual(data, address, number),
                10 => MachiningPallet(data, address, number),
                12 => MachiningIndex(data, address, number),
                32 => MachiningDrilling(data, address, number),
                35 => MachiningReaming(data, address, number),
                38 => MachiningCircle(data, address, number),
                64 or 66 or 68 => MachiningLine(data, address, number, type),
                96 => MachiningFace(data, address, number),
                99 => MachiningPocket(data, address, number),
                160 => MachiningOffset(data, address, number),
                161 => MachiningManualLine(data, address, number),
                176 => MachiningPointTool(data, address, number),
                177 or 178 => MachiningTool(data, address, number, speedDivisor),
                192 => MachiningPoint(data, address, number),
                193 => MachiningSquare(data, address, number),
                194 => MachiningContour(data, address, number),
                _ => null
            };
            built ??= Described(data, structure, displayed, type, address, number);
            if (built != null)
            {
                blocks.Add(built);
            }
        }

        return blocks;
    }

    /// <summary>
    /// A MAZ file is the bare record list, so it starts at 0. PBM files carry a 260-byte header where
    /// PBD/PBF/PBG carry 252; the header stores the byte size of the record area at 0xA0, so the first
    /// record sits at file length minus that size.
    /// </summary>
    private static int RecordStart(byte[] data)
    {
        if (data.Length >= Slot && data.Length % Slot == 0 && data[0] == 1 && data[1] == 0 && data[2] == 0 && data[3] == 0 && data[4] == 0xC3)
        {
            return 0;
        }

        var size = Int32At(data, 0xA0);
        var computed = data.Length - size;
        int[] candidates = size > 0 && size % Slot == 0 ? [computed, 0x104, Start] : [0x104, Start];
        foreach (var candidate in candidates)
        {
            if (candidate >= Start && candidate + Slot <= data.Length && data[candidate] == 1 && data[candidate + 2] == 0)
            {
                return candidate;
            }
        }

        return Start;
    }

    /// <summary>
    /// Units without a confirmed layout: use the structure table when it describes the record, otherwise
    /// keep a named row so a unit is never dropped from the list without a trace.
    /// </summary>
    private static Block? Described(byte[] data, UnitDef[] structure, HashSet<int> displayed, int type, int address, int number)
    {
        if (type < structure.Length && structure[type].Parameters.Length > 0 && displayed.Contains(type))
        {
            return ReadBlock(data, structure[type], type, address, number);
        }

        return type is > 0 and < 160
            ? new Block("UNIT " + type.ToString(CultureInfo.InvariantCulture), number, true, [])
            : null;
    }

    private static Block MachiningMaterial(byte[] data, int address, int number)
    {
        var mode = ByteAt(data, address + 9);
        var modeText = mode switch
        {
            0 or 1 => "OFF",
            3 => "OFFSET TYPE",
            _ => mode.ToString(CultureInfo.InvariantCulture)
        };
        var pitchX = Int32At(data, address + 72);
        var pitchY = Int32At(data, address + 76);
        return new Block("MAT", number, true,
        [
            new Cell("MAT.", CellKind.Text, ReadText(data, address + 84), 0, true),
            Metric("INITIAL-Z", Int32At(data, address + 40)),
            WordCell("ATC MODE", ByteAt(data, address + 10).ToString(CultureInfo.InvariantCulture)),
            WordCell("MULTI MODE", modeText),
            Unset("MULTI FLAG"),
            Metric("PITCH-X", pitchX, pitchX != 0),
            Metric("PITCH-Y", pitchY, pitchY != 0)
        ]);
    }

    private static Block MachiningOffset(byte[] data, int address, int number)
    {
        var mask = WordAt(data, address + 4);
        return new Block("OFS", number, false,
        [
            Entered(data, address, mask, 0, "X", 36),
            Entered(data, address, mask, 1, "Y", 40),
            Entered(data, address, mask, 2, "th", 44),
            Entered(data, address, mask, 3, "Z", 48)
        ])
        { Columns = true };
    }

    private static Block MachiningPallet(byte[] data, int address, int number) =>
        new("PALT CHG", number, true,
        [
            WordCell("PALLET No.", WordAt(data, address + 26).ToString(CultureInfo.InvariantCulture))
        ]);

    /// <summary>
    /// Only the INDEX record without turning positions is confirmed. The value at +40 is not printed while
    /// its flag is clear. A record that carries other values is left to the structure table.
    /// </summary>
    private static Block? MachiningIndex(byte[] data, int address, int number)
    {
        var mask = WordAt(data, address + 4);
        for (var at = address + 36; at < address + Slot; at++)
        {
            var angle = at >= address + 40 && at < address + 44 && (mask & 0x02) == 0;
            if (data[at] != 0 && !angle)
            {
                return null;
            }
        }

        return new Block("INDEX", number, true,
        [
            Metric("TURN POS X", 0),
            Metric("TURN POS Y", 0),
            Metric("TURN POS Z", 0),
            (mask & 0x02) != 0 ? Metric("ANGLEC", 0) : Blank("ANGLEC"),
            (mask & 0x08) != 0 ? Metric("ANGLEA", 0) : Blank("ANGLEA")
        ]);
    }

    private static Block MachiningWpc(byte[] data, int address, int number)
    {
        var mask = WordAt(data, address + 4);
        return new Block("WPC- 0", number, true,
        [
            Unset("ADD. WPC"),
            Entered(data, address, mask, 0, "X", 36),
            Entered(data, address, mask, 1, "Y", 40),
            Entered(data, address, mask, 2, "th", 44),
            Entered(data, address, mask, 3, "Z", 48),
            Optional(data, address, mask, 5, "C", 52),
            Optional(data, address, mask, 7, "A", 56)
        ]);
    }

    private static Block MachiningSubProgram(byte[] data, int address, int number)
    {
        var mask = WordAt(data, address + 4);
        var work = ReadText(data, address + 36);
        return new Block("SUB PRO", number, true,
        [
            WordCell("WORK No.", work.Length > 0 ? work + "M" : ""),
            Blank("REPEAT"),
            (mask & 0x0800) != 0 ? WordCell("No.", WordAt(data, address + 22).ToString(CultureInfo.InvariantCulture)) : Blank("No.")
        ]);
    }

    private static Block MachiningManual(byte[] data, int address, int number)
    {
        var family = ByteAt(data, address + 9);
        return new Block("MANL PRG", number, true,
        [
            new Cell("TOOL", CellKind.Text, family == 0 ? "" : MachiningToolName(family), 0, true),
            family == 0 ? Blank("NOM-Ø") : Metric("NOM-Ø", Int32At(data, address + 36)),
            WordCell("No.", ToolNumberText(data, address))
        ]);
    }

    /// <summary>
    /// One manual program line. Bits 0..3 of the flags say whether G1, G2, S and M are present; bytes
    /// +8..+13 hold the address letter of each data word as a base-36 digit (15 = F, 33 = X).
    /// </summary>
    private static Block MachiningManualLine(byte[] data, int address, int number)
    {
        var flags = WordAt(data, address + 4);
        var cells = new List<Cell>
        {
            (flags & 0x01) != 0 ? WordCell("G1", WordAt(data, address + 20).ToString(CultureInfo.InvariantCulture)) : Blank("G1"),
            (flags & 0x02) != 0 ? WordCell("G2", WordAt(data, address + 22).ToString(CultureInfo.InvariantCulture)) : Blank("G2")
        };
        for (var slot = 0; slot < 6; slot++)
        {
            var name = "DATA " + (slot + 1).ToString(CultureInfo.InvariantCulture);
            var code = ByteAt(data, address + 8 + slot);
            cells.Add(code is >= 10 and <= 35
                ? WordCell(name, (char)('A' + code - 10) + " " + MetricText(Int32At(data, address + 36 + (slot * 4))))
                : Blank(name));
        }

        cells.Add((flags & 0x04) != 0 ? WordCell("S", Int32At(data, address + 60).ToString(CultureInfo.InvariantCulture)) : Blank("S"));
        cells.Add((flags & 0x08) != 0 ? WordCell("M/B", "M " + WordAt(data, address + 24).ToString(CultureInfo.InvariantCulture)) : Blank("M/B"));
        return new Block("SNo", number, false, cells) { Plain = true };
    }

    private static Block MachiningDrilling(byte[] data, int address, int number)
    {
        var mask = WordAt(data, address + 4);
        return new Block("DRILLING", number, true,
        [
            Entered(data, address, mask, 0, "DIA", 36),
            Entered(data, address, mask, 1, "DEPTH", 40),
            Entered(data, address, mask, 2, "CHMF", 44)
        ]);
    }

    private static Block MachiningReaming(byte[] data, int address, int number)
    {
        var mask = WordAt(data, address + 4);
        var before = ByteAt(data, address + 18);
        var beforeText = before switch
        {
            1 => "DRILL",
            2 => "BORING",
            3 => "END MILL",
            _ => before.ToString(CultureInfo.InvariantCulture)
        };
        return new Block("REAMING", number, true,
        [
            Entered(data, address, mask, 0, "DIA", 36),
            Entered(data, address, mask, 1, "DEPTH", 40),
            Entered(data, address, mask, 2, "CHMF", 44),
            (mask & 0x08) != 0 ? WordCell("PRE-REAM", beforeText) : Unset("PRE-REAM"),
            WordCell("CHP", ByteAt(data, address + 19).ToString(CultureInfo.InvariantCulture))
        ]);
    }

    /// <summary>
    /// Circular milling. Only the record without a pre-hole is confirmed; one that carries a pre-hole
    /// diameter or its chamfer is left to the structure table.
    /// </summary>
    private static Block? MachiningCircle(byte[] data, int address, int number)
    {
        var mask = WordAt(data, address + 4);
        if ((mask & 0x18) != 0)
        {
            return null;
        }

        return new Block("CIRC MIL", number, true,
        [
            WordCell("TORNAD", ByteAt(data, address + 13).ToString(CultureInfo.InvariantCulture)),
            Entered(data, address, mask, 0, "DIA", 36),
            Entered(data, address, mask, 1, "DEPTH", 40),
            Entered(data, address, mask, 2, "CHMF", 44),
            Grade(data, address, mask, 5, "BTM", 9),
            Unset("PRE-DIA"),
            Unset("CHMF"),
            Entered(data, address, mask, 6, "PITCH1", 48),
            Entered(data, address, mask, 7, "PITCH2", 52)
        ]);
    }

    private static Block MachiningFace(byte[] data, int address, int number)
    {
        var mask = WordAt(data, address + 4);
        return new Block("FCE MILL", number, true,
        [
            Entered(data, address, mask, 0, "DEPTH", 36),
            Entered(data, address, mask, 1, "SRV-Z", 40),
            Grade(data, address, mask, 3, "BTM", 9),
            Entered(data, address, mask, 6, "FIN-Z", 52)
        ]);
    }

    /// <summary>Line machining: 64 LINE CTR, 66 LINE LFT, 68 LINE IN.</summary>
    private static Block MachiningLine(byte[] data, int address, int number, int type)
    {
        var mask = WordAt(data, address + 4);
        var cells = new List<Cell>
        {
            Entered(data, address, mask, 0, "DEPTH", 36),
            Entered(data, address, mask, 1, "SRV-Z", 40),
            Entered(data, address, mask, 2, "SRV-R", 44),
            Grade(data, address, mask, 4, "RGH", 17),
            Entered(data, address, mask, 5, "FIN-Z", 52)
        };
        if (type != 64)
        {
            cells.Add(Entered(data, address, mask, 6, "FIN-R", 56));
        }

        if (type != 68)
        {
            cells.Add(WordCell("START", "OPEN"));
            cells.Add(WordCell("END", "OPEN"));
        }

        if (type != 64)
        {
            cells.Add(Optional(data, address, mask, 8, "INTER-R", 64));
            cells.Add(Optional(data, address, mask, 9, "CHMF", 68));
        }

        var name = type switch
        {
            64 => "LINE CTR",
            66 => "LINE LFT",
            _ => "LINE IN"
        };
        return new Block(name, number, true, cells);
    }

    private static Block MachiningPocket(byte[] data, int address, int number)
    {
        var mask = WordAt(data, address + 4);
        return new Block("POCKET", number, true,
        [
            Entered(data, address, mask, 0, "DEPTH", 36),
            Entered(data, address, mask, 1, "SRV-Z", 40),
            Grade(data, address, mask, 3, "BTM", 9),
            Grade(data, address, mask, 4, "WAL", 10),
            Entered(data, address, mask, 6, "FIN-Z", 52),
            Entered(data, address, mask, 7, "FIN-R", 56),
            Optional(data, address, mask, 8, "INTER-R", 64),
            Optional(data, address, mask, 9, "CHMF", 68)
        ]);
    }

    private static Block MachiningTool(byte[] data, int address, int number, int speedDivisor)
    {
        var mask = WordAt(data, address + 4);
        var role = ByteAt(data, address + 8) == 2 ? "F" : "R";
        var parent = ByteAt(data, address + 20);
        var approach = ByteAt(data, address + 15);
        var type = approach switch
        {
            0x00 => "",
            0x01 => "XBI",
            0x02 => "YBI",
            0x03 => "XUN",
            0x04 => "YUN",
            0x05 => "XBI-S",
            0x06 => "YBI-S",
            0x07 => "XB-AS",
            0x08 => "YB-AS",
            0x11 => "CCW",
            0x12 => "CW",
            _ => approach.ToString(CultureInfo.InvariantCulture)
        };
        var feedMode = parent == 96 ? "" : ByteAt(data, address + 14) == 0x80 ? "G00" : "G01";
        var depth = Int32At(data, address + 48);
        var width = Int32At(data, address + 52);
        var speed = Int32At(data, address + 60);
        var feed = Int32At(data, address + 64);
        var cells = new List<Cell>
        {
            new("TOOL", CellKind.Text, MachiningToolName(ByteAt(data, address + 9)), 0, true),
            Metric("NOM-Ø", Int32At(data, address + 36)),
            WordCell("No.", ToolNumberText(data, address)),
            Approach(data, address, mask, 1, "APRCH-X", 40),
            Approach(data, address, mask, 2, "APRCH-Y", 44),
            new("TYPE", CellKind.Word, type, 0, type.Length > 0),
            new("ZFD", CellKind.Word, feedMode, 0, feedMode.Length > 0)
        };
        if (parent == 99)
        {
            cells.Add(Blank("TYPE"));
            cells.Add(Unset("PK-DEP"));
        }

        cells.Add(Metric("DEP-Z", depth, depth != 0));
        cells.Add(Metric("WID-R", width, width != 0));
        cells.Add(new Cell("C-SP", CellKind.Word, SpeedText(speed, speedDivisor), speed, speed != 0));
        cells.Add(Feed(feed));
        cells.Add(Whole("M", WordAt(data, address + 24), emptyWhenZero: true));
        cells.Add(Whole("M", WordAt(data, address + 26), emptyWhenZero: true));
        cells.Add(Whole("M", WordAt(data, address + 28), emptyWhenZero: true));
        return new Block("SNo", number, false, cells) { Lead = role };
    }

    /// <summary>
    /// A tool of a point-machining unit. The four columns after HOLE-DEP change meaning with the tool:
    /// they are confirmed for a drill (pre-hole, feed percentage, cycle, peck depth) and for an end mill
    /// (direction, bottom grade, chamfer); for any other tool they are left empty.
    /// </summary>
    private static Block MachiningPointTool(byte[] data, int address, int number)
    {
        var mask = WordAt(data, address + 4);
        var family = ByteAt(data, address + 9);
        var speed = Int32At(data, address + 60);
        var feed = Int32At(data, address + 64);
        var cells = new List<Cell>
        {
            new("TOOL", CellKind.Text, MachiningToolName(family), 0, true),
            Metric("NOM-Ø", Int32At(data, address + 36)),
            WordCell("No.", ToolNumberText(data, address)),
            Entered(data, address, mask, 1, "HOLE-Ø", 40),
            Entered(data, address, mask, 2, "HOLE-DEP", 44)
        };
        if (family == 2)
        {
            var cycle = ByteAt(data, address + 15);
            var cycleText = cycle switch
            {
                1 => "DRIL",
                2 => "PCK1",
                3 => "PCK2",
                4 => "AUTO",
                5 => "PCK3",
                6 => "PCK4",
                7 => "PCK5",
                _ => cycle.ToString(CultureInfo.InvariantCulture)
            };
            cells.Add(Entered(data, address, mask, 3, "PRE-DIA", 48));
            cells.Add((mask & 0x10) != 0
                ? WordCell("PRE-DEP", Int32At(data, address + 52).ToString(CultureInfo.InvariantCulture))
                : Unset("PRE-DEP"));
            cells.Add(WordCell("RGH", cycleText));
            cells.Add(WordCell("DEPTH", "T " + MetricText(Int32At(data, address + 56))));
        }
        else if (family == 15)
        {
            var turn = ByteAt(data, address + 14);
            var turnText = turn switch
            {
                1 => "CW",
                2 => "CCW",
                _ => turn.ToString(CultureInfo.InvariantCulture)
            };
            cells.Add(Entered(data, address, mask, 3, "PRE-DIA", 48));
            cells.Add(WordCell("PRE-DEP", turnText));
            cells.Add(WordCell("RGH", ByteAt(data, address + 15).ToString(CultureInfo.InvariantCulture)));
            cells.Add(WordCell("DEPTH", "C " + MetricText(Int32At(data, address + 56))));
        }
        else
        {
            cells.Add(Blank("PRE-DIA"));
            cells.Add(Blank("PRE-DEP"));
            cells.Add(Blank("RGH"));
            cells.Add(Blank("DEPTH"));
        }

        cells.Add(new Cell("C-SP", CellKind.Word, speed.ToString(CultureInfo.InvariantCulture), speed, speed != 0));
        cells.Add(Feed(feed));
        cells.Add(Whole("M", WordAt(data, address + 24), emptyWhenZero: true));
        cells.Add(Whole("M", WordAt(data, address + 26), emptyWhenZero: true));
        var last = WordAt(data, address + 28);
        cells.Add(Whole("M", last != 0 ? last : WordAt(data, address + 32), emptyWhenZero: true));
        return new Block("SNo", number, false, cells) { Plain = true };
    }

    /// <summary>
    /// A hole position. Only the single point (PT) is confirmed; another pattern keeps its name so the
    /// line is not lost.
    /// </summary>
    private static Block MachiningPoint(byte[] data, int address, int number)
    {
        var pattern = ByteAt(data, address + 8);
        var name = pattern switch
        {
            1 => "PT",
            2 => "LIN",
            3 => "SQR",
            4 => "GRD",
            5 => "CIR",
            6 => "ARC",
            7 => "CHD",
            _ => pattern.ToString(CultureInfo.InvariantCulture)
        };
        var code = new Cell("PTN", CellKind.Code, name, pattern, true);
        if (pattern != 1)
        {
            return new Block("FIG", number, false, [code]);
        }

        var mask = WordAt(data, address + 4);
        return new Block("FIG", number, false,
        [
            code,
            Metric("Z", Int32At(data, address + 36)),
            Entered(data, address, mask, 1, "X", 40),
            Entered(data, address, mask, 2, "Y", 44),
            Unset("AN1"),
            Unset("AN2"),
            Unset("T1"),
            Unset("T2"),
            Unset("F"),
            Unset("M"),
            Unset("N"),
            WordCell("P", "0"),
            WordCell("Q", "0"),
            WordCell("R", "0")
        ]);
    }

    private static Block? MachiningSquare(byte[] data, int address, int number)
    {
        if (ByteAt(data, address + 8) != 0x10)
        {
            return null;
        }

        var mask = WordAt(data, address + 4);
        return new Block("FIG", number, false,
        [
            new Cell("PTN", CellKind.Code, "SQR", 0x10, true),
            Optional(data, address, mask, 1, "P1X/CX", 36),
            Optional(data, address, mask, 2, "P1Y/CY", 40),
            Optional(data, address, mask, 3, "P3X/R", 44),
            Optional(data, address, mask, 4, "P3Y", 48),
            Rounded(data, address, mask, 5, "CN1", 52),
            Rounded(data, address, mask, 6, "CN2", 56),
            Rounded(data, address, mask, 7, "CN3", 60),
            Rounded(data, address, mask, 8, "CN4", 64)
        ]);
    }

    private static Block? MachiningContour(byte[] data, int address, int number)
    {
        var pattern = ByteAt(data, address + 8);
        var name = pattern switch
        {
            0x20 => "LINE",
            0x21 => "CW",
            0x22 => "CCW",
            _ => ""
        };
        if (name.Length == 0)
        {
            return null;
        }

        var mask = WordAt(data, address + 4);
        return new Block("FIG", number, false,
        [
            new Cell("PTN", CellKind.Code, name, pattern, true),
            Optional(data, address, mask, 1, "X", 40),
            Optional(data, address, mask, 2, "Y", 44),
            Optional(data, address, mask, 3, "R/th", 48),
            Optional(data, address, mask, 4, "I", 52),
            Optional(data, address, mask, 5, "J", 56),
            Blank("P"),
            Rounded(data, address, mask, 6, "CNR", 60),
            Blank("R-FEED"),
            Blank("RGH")
        ]);
    }

    /// <summary>
    /// END. A continued program prints no tool change, and the return position only when it was entered.
    /// </summary>
    private static Block MachiningEnd(byte[] data, int address, int number)
    {
        var mask = WordAt(data, address + 4);
        var continued = ByteAt(data, address + 9) != 0;
        var back = ByteAt(data, address + 12);
        var returnText = back switch
        {
            0 => "TOOL CHG",
            1 => "ZERO PT",
            2 => "FIXED PT",
            3 => "ARB PT",
            4 => "ESC 1",
            5 => "ESC 2",
            _ => back.ToString(CultureInfo.InvariantCulture)
        };
        return new Block("END", number, true,
        [
            WordCell("CONTI.", continued ? "1" : "0"),
            WordCell("NUMBER", "0"),
            (mask & 0x08) != 0 && !continued ? WordCell("ATC", "0") : Unset("ATC"),
            (mask & 0x10) != 0 ? WordCell("RETURN", returnText) : Blank("RETURN"),
            continued ? Unset("WORK No.") : Blank("WORK No."),
            Unset("EXECTE")
        ]);
    }

    private static string MachiningToolName(int family) => family switch
    {
        1 => "CTR-DR",
        2 => "DRILL",
        3 => "REAMER",
        >= 4 and <= 9 => "TAP",
        10 => "BCK FACE",
        11 => "BOR BAR",
        12 => "B-B BAR",
        13 => "CHAMFER",
        14 => "FCE MILL",
        15 => "END MILL",
        16 => "OTHER",
        17 => "CHIP VAC",
        18 => "TOL SENS",
        19 => "BAL EMIL",
        _ => "TOOL"
    };

    /// <summary>A tool number with its suffix letter, such as "B 10"; empty when no tool is assigned.</summary>
    private static string ToolNumberText(byte[] data, int address)
    {
        var toolNumber = WordAt(data, address + 22);
        if (toolNumber == 0)
        {
            return "";
        }

        var letter = Letter(data, address + 11);
        return (letter.Length > 0 ? letter + " " : "") + toolNumber.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// PBM stores the cutting speed as fixed point like every other value in the record; PBD stores a plain integer.
    /// </summary>
    private static string SpeedText(int raw, int divisor) =>
        divisor == 1
            ? raw.ToString(CultureInfo.InvariantCulture)
            : (raw / (double)divisor).ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>
    /// The exact text of a stored ten-thousandth, the way the MATRIX list prints it: up to four decimals,
    /// no trailing zeros, and a trailing point on whole numbers.
    /// </summary>
    private static string MetricText(int raw)
    {
        var magnitude = Math.Abs((long)raw);
        var whole = (magnitude / 10000).ToString(CultureInfo.InvariantCulture);
        var fraction = (magnitude % 10000).ToString("0000", CultureInfo.InvariantCulture).TrimEnd('0');
        return (raw < 0 ? "-" : "") + whole + "." + fraction;
    }

    private static Cell Metric(string name, int raw, bool defined = true) =>
        new(name, CellKind.Measure, MetricText(raw), raw / 10000d, defined);

    /// <summary>A feed rate. The list cuts it to three decimals where every other value keeps four.</summary>
    private static Cell Feed(int raw) => Metric("FR", raw - (raw % 10), raw != 0);

    /// <summary>A measure that prints its value when the record flags it as entered, and @ otherwise.</summary>
    private static Cell Entered(byte[] data, int address, int mask, int bit, string name, int offset) =>
        Metric(name, Int32At(data, address + offset), (mask & (1 << bit)) != 0);

    /// <summary>A measure that prints its value when the record flags it as entered, and stays empty otherwise.</summary>
    private static Cell Optional(byte[] data, int address, int mask, int bit, string name, int offset) =>
        (mask & (1 << bit)) != 0
            ? Metric(name, Int32At(data, address + offset))
            : Blank(name);

    /// <summary>An approach point: its value when entered, ? when the control is left to work it out.</summary>
    private static Cell Approach(byte[] data, int address, int mask, int bit, string name, int offset) =>
        (mask & (1 << bit)) != 0
            ? Metric(name, Int32At(data, address + offset))
            : new Cell(name, CellKind.Word, "?", 0, true);

    /// <summary>A corner radius: "R value" when entered, empty otherwise.</summary>
    private static Cell Rounded(byte[] data, int address, int mask, int bit, string name, int offset) =>
        (mask & (1 << bit)) != 0
            ? new Cell(name, CellKind.Word, "R " + MetricText(Int32At(data, address + offset)), 0, true)
            : Blank(name);

    /// <summary>A one-byte surface grade such as BTM or RGH.</summary>
    private static Cell Grade(byte[] data, int address, int mask, int bit, string name, int offset) =>
        (mask & (1 << bit)) != 0
            ? WordCell(name, ByteAt(data, address + offset).ToString(CultureInfo.InvariantCulture))
            : Unset(name);

    private static Cell WordCell(string name, string text) => new(name, CellKind.Word, text, 0, true);

    private static Cell Unset(string name) => new(name, CellKind.Word, "", 0, false);

    private static int Int32At(byte[] data, int address) =>
        address >= 0 && address + 4 <= data.Length ? BitConverter.ToInt32(data, address) : 0;

    private static int ByteAt(byte[] data, int address) =>
        address >= 0 && address < data.Length ? data[address] : 0;
}
