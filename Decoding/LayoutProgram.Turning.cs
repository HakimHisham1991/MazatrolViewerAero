using System.Globalization;
using System.Text;

namespace MazatrolViewerAero.Decoding;

/// <summary>
/// M640T/MT turning programs (T6M, T6I). Two 250-byte headers are followed by 50-byte records.
/// A record is: +0 type, +1 number, +2..+3 flags, +4..+5 a mask of entered fields, +6 section or shape,
/// +8..+17 small integers, +18..+49 eight int32 values in thousandths.
/// Mask bit n marks the field at: 1 → +8, 2 → +10, 4 → +14, 5 → +16, 6..13 → +18, +22 … +46.
/// Field positions were confirmed against program lists exported by Mazatrol Viewer.
/// </summary>
public static partial class LayoutProgram
{
    private const int TurningStart = 500;
    private const int TurningSlot = 50;

    private static bool IsTurning(string extension) =>
        extension.Equals(".t6m", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".t6i", StringComparison.OrdinalIgnoreCase);

    private static List<Block> ParseTurning(byte[] data)
    {
        var blocks = new List<Block>();
        if (data.Length < TurningStart + TurningSlot || data[TurningStart] != 1)
        {
            return blocks;
        }

        var size = Int32At(data, 0xA4);
        var end = size > 0 && TurningStart + size <= data.Length ? TurningStart + size : data.Length;
        for (var address = TurningStart; address + TurningSlot <= end; address += TurningSlot)
        {
            int type = data[address];
            int number = data[address + 1];
            if (type is 0 or >= 0x80)
            {
                continue;
            }

            var built = type switch
            {
                0x01 => TurningMaterial(data, address, number),
                0x03 => TurningCodes(data, address, number),
                0x05 => TurningEnd(data, address, number),
                0x10 => TurningBar(data, address, number),
                0x11 => TurningCopy(data, address, number),
                0x13 => TurningEdge(data, address, number),
                0x15 => TurningGroove(data, address, number),
                0x16 => TurningDrill(data, address, number),
                0x18 => TurningManual(data, address, number),
                0x50 => TurningShape(data, address, number),
                0x52 => TurningEdgeLine(data, address, number),
                0x54 => TurningGrooveLine(data, address, number),
                0x55 => TurningDrillLine(data, address, number),
                0x56 => TurningManualLine(data, address, number),
                < 0x50 => new Block(TurningUnitName(data, address), number, true, []),
                _ => new Block("SEQ", number, false, []) { Columns = true }
            };

            // Tool path control records (0x80 and up) follow the unit they belong to; the list marks that unit with +.
            var next = address + TurningSlot;
            if (built.IsUnitHeader && next < end && data[next] >= 0x80)
            {
                built = built with { Badge = "+ " };
            }

            blocks.Add(built);
        }

        return blocks;
    }

    private static Block TurningMaterial(byte[] data, int address, int number)
    {
        var mask = WordAt(data, address + 4);
        var name = new StringBuilder();
        for (var at = address + 6; at < address + 14 && data[at] is >= 32 and < 127; at++)
        {
            name.Append((char)data[at]);
        }

        return new Block("MAT", number, true,
        [
            new Cell("MAT", CellKind.Text, name.ToString().Trim(), 0, true),
            Milli(data, address, mask, 6, "OD-MAX", 18),
            Milli(data, address, mask, 7, "ID-MIN", 22),
            Milli(data, address, mask, 8, "LENGTH", 26),
            Count(data, address, mask, 4, "RPM", 14),
            Milli(data, address, mask, 9, "FIN-X", 30),
            Milli(data, address, mask, 10, "FIN-Z", 34),
            Milli(data, address, mask, 11, "WORK FACE", 38),
            Blank("MPX"),
            Blank("FIN-LENGTH")
        ]);
    }

    private static Block TurningCodes(byte[] data, int address, int number)
    {
        var mask = WordAt(data, address + 4);
        var cells = new List<Cell>();
        for (var slot = 0; slot < 12; slot++)
        {
            var name = "#" + (slot + 1).ToString(CultureInfo.InvariantCulture).PadLeft(2);
            cells.Add((mask & (1 << slot)) != 0
                ? WordCell(name, WordAt(data, address + 18 + (slot * 2)).ToString(CultureInfo.InvariantCulture))
                : Blank(name));
        }

        return new Block("M", number, true, cells);
    }

    private static Block TurningEnd(byte[] data, int address, int number)
    {
        var mask = WordAt(data, address + 4);
        return new Block("END", number, true,
        [
            Count(data, address, mask, 1, "COUNTER", 8),
            Blank("RETURN"),
            Blank("WK.No."),
            Blank("CONT."),
            Blank("NUM."),
            Blank("SHIFT")
        ]);
    }

    private static Block TurningBar(byte[] data, int address, int number)
    {
        var mask = WordAt(data, address + 4);
        var cells = new List<Cell>
        {
            Count(data, address, mask, 1, "#", 8),
            Milli(data, address, mask, 6, "CPT-X", 18),
            Milli(data, address, mask, 7, "CPT-Z", 22)
        };
        cells.AddRange(TurningCutting(data, address, mask, "R-FEED", "R-DEP."));
        return new Block(TurningUnitName(data, address), number, true, cells);
    }

    private static Block TurningCopy(byte[] data, int address, int number)
    {
        var mask = WordAt(data, address + 4);
        var cells = new List<Cell>
        {
            Milli(data, address, mask, 6, "CPT-X", 18),
            Milli(data, address, mask, 7, "CPT-Z", 22),
            Milli(data, address, mask, 8, "SRV-X", 26),
            Milli(data, address, mask, 9, "SRV-Z", 30)
        };
        cells.AddRange(TurningCutting(data, address, mask, "R-FEED", "R-DEP."));
        return new Block(TurningUnitName(data, address), number, true, cells);
    }

    private static Block TurningEdge(byte[] data, int address, int number)
    {
        var mask = WordAt(data, address + 4);
        return new Block(TurningUnitName(data, address), number, true,
            TurningCutting(data, address, mask, "R-FEED", "R-DEP.").ToList());
    }

    private static Block TurningGroove(byte[] data, int address, int number)
    {
        var mask = WordAt(data, address + 4);
        var cells = new List<Cell>
        {
            Count(data, address, mask, 1, "#", 8),
            Count(data, address, mask, 2, "No.", 10),
            Milli(data, address, mask, 7, "PITCH", 22),
            Milli(data, address, mask, 6, "WIDTH", 18),
            Milli(data, address, mask, 8, "FINISH", 26)
        };
        cells.AddRange(TurningCutting(data, address, mask, "FEED", "DEP."));
        return new Block(TurningUnitName(data, address), number, true, cells);
    }

    /// <summary>
    /// The columns shared by the cutting units: speeds, feed, depth, and the rough and finish tools.
    /// A speed that was not entered prints ? because the control works it out.
    /// </summary>
    private static IEnumerable<Cell> TurningCutting(byte[] data, int address, int mask, string feedName, string depthName)
    {
        yield return Count(data, address, mask, 4, "RV", 14, "?");
        yield return Count(data, address, mask, 5, "FV", 16, "?");
        yield return Milli(data, address, mask, 11, feedName, 38);
        yield return Milli(data, address, mask, 10, depthName, 34);
        yield return Count(data, address, mask, 12, "R-TOOL", 42);
        yield return Count(data, address, mask, 13, "F-TOOL", 46);
    }

    private static Block TurningDrill(byte[] data, int address, int number)
    {
        var mask = WordAt(data, address + 4);
        return new Block(TurningUnitName(data, address), number, true,
        [
            Count(data, address, mask, 1, "#", 8),
            Milli(data, address, mask, 6, "DIA", 18),
            Milli(data, address, mask, 8, "DEP-1", 26),
            Milli(data, address, mask, 9, "DEP-2", 30),
            Milli(data, address, mask, 10, "DEP-3", 34),
            Count(data, address, mask, 5, "C-SP", 16),
            Milli(data, address, mask, 11, "FEED", 38),
            Count(data, address, mask, 13, "TOOL", 46)
        ]);
    }

    private static Block TurningManual(byte[] data, int address, int number)
    {
        var mask = WordAt(data, address + 4);
        return new Block(TurningUnitName(data, address), number, true,
        [
            Count(data, address, mask, 1, "CHANGE-PT", 8),
            Count(data, address, mask, 2, "GEAR", 10),
            Count(data, address, mask, 13, "TOOL", 46)
        ]);
    }

    private static Block TurningShape(byte[] data, int address, int number)
    {
        var mask = WordAt(data, address + 4);
        var shape = ByteAt(data, address + 6);
        var name = shape switch
        {
            1 => "LIN",
            2 => "TPR",
            3 => "ar1",
            4 => "ar2",
            5 => "CTR",
            _ => shape.ToString(CultureInfo.InvariantCulture)
        };

        // A straight line takes its start point from the previous line and has no radius; the list prints @ there.
        var line = shape == 1;
        return new Block("SEQ", number, false,
        [
            new Cell("SHP", CellKind.Code, name, shape, true),
            TurningCorner(data, address, mask, 10, "S-CNR", 34, 0x08),
            MilliOr(data, address, mask, 6, "SPT-X", 18, line),
            MilliOr(data, address, mask, 7, "SPT-Z", 22, line),
            TurningFinishX(data, address, mask),
            Milli(data, address, mask, 9, "FPT-Z", 30),
            TurningCorner(data, address, mask, 11, "F-CNR/$", 38, 0x10),
            MilliOr(data, address, mask, 12, "RADIUS/th.", 42, line),
            Milli(data, address, mask, 13, "RGH", 46)
        ]);
    }

    private static Block TurningEdgeLine(byte[] data, int address, int number)
    {
        var mask = WordAt(data, address + 4);
        return new Block("SEQ", number, false,
        [
            Milli(data, address, mask, 6, "SPT-X", 18),
            Milli(data, address, mask, 7, "SPT-Z", 22),
            Milli(data, address, mask, 8, "FPT-X", 26),
            Milli(data, address, mask, 9, "FPT-Z", 30),
            Milli(data, address, mask, 13, "RGH", 46)
        ])
        { Columns = true };
    }

    private static Block TurningGrooveLine(byte[] data, int address, int number)
    {
        var mask = WordAt(data, address + 4);
        return new Block("SEQ", number, false,
        [
            TurningCorner(data, address, mask, 10, "S-CNR", 34, 0x08),
            Milli(data, address, mask, 6, "SPT-X", 18),
            Milli(data, address, mask, 7, "SPT-Z", 22),
            Milli(data, address, mask, 8, "FPT-X", 26),
            Milli(data, address, mask, 9, "FPT-Z", 30),
            TurningCorner(data, address, mask, 11, "CNR", 38, 0x10),
            Milli(data, address, mask, 12, "ANG", 42),
            Milli(data, address, mask, 13, "RGH", 46)
        ])
        { Columns = true };
    }

    private static Block TurningDrillLine(byte[] data, int address, int number)
    {
        var mask = WordAt(data, address + 4);
        return new Block("SEQ", number, false,
        [
            Milli(data, address, mask, 7, "SPT-Z", 22),
            Milli(data, address, mask, 9, "FPT-Z", 30)
        ])
        { Columns = true };
    }

    private static Block TurningManualLine(byte[] data, int address, int number)
    {
        var mask = WordAt(data, address + 4);
        return new Block("SEQ", number, false,
        [
            Blank("G"),
            Blank("DATA-1"),
            Blank("DATA-2"),
            Blank("DATA-3"),
            Blank("RADIUS/VARIABLE"),
            Blank("RPM"),
            Blank("FEED"),
            Count(data, address, mask, 5, "M", 16),
            Blank("OFS")
        ])
        { Columns = true };
    }

    /// <summary>
    /// Flag 0x40 at +3 marks a finish point the control calculated itself. Mazatrol Viewer prints such a value
    /// with "o" in place of the decimal point, as in 22o008.
    /// </summary>
    private static Cell TurningFinishX(byte[] data, int address, int mask)
    {
        if ((mask & (1 << 8)) == 0 || (ByteAt(data, address + 3) & 0x40) == 0)
        {
            return Milli(data, address, mask, 8, "FPT-X", 26);
        }

        return WordCell("FPT-X", MilliText(Int32At(data, address + 26)).Replace('.', 'o'));
    }

    /// <summary>A corner: R for a radius, C for a chamfer; the flag byte at +2 says which.</summary>
    private static Cell TurningCorner(byte[] data, int address, int mask, int bit, string name, int offset, int roundFlag)
    {
        if ((mask & (1 << bit)) == 0)
        {
            return Blank(name);
        }

        var letter = (ByteAt(data, address + 2) & roundFlag) != 0 ? "R " : "C ";
        return WordCell(name, letter + MilliText(Int32At(data, address + offset)));
    }

    private static string TurningUnitName(byte[] data, int address)
    {
        int type = data[address];
        var unit = type switch
        {
            2 => "MTR",
            3 => "M",
            4 => "MES",
            5 => "END",
            6 => "TLY",
            7 => "MCR",
            8 => "SNS",
            9 => "SEP",
            10 => "PRP",
            11 => "SUB",
            15 => "PAT",
            16 => "BAR",
            17 => "CPY",
            18 => "CNR",
            19 => "EDG",
            20 => "THR",
            21 => "GRV",
            22 => "DRL",
            23 => "TAP",
            24 => "MNP",
            32 => "MDR",
            33 => "MTP",
            34 => "BOR",
            35 => "MGV",
            36 => "LCT",
            37 => "RGT",
            38 => "LFT",
            39 => "MMP",
            40 => "CIR",
            _ => "UNIT " + type.ToString(CultureInfo.InvariantCulture)
        };
        var part = ByteAt(data, address + 6);
        var section = part switch
        {
            0 => "",
            1 or 2 or 17 or 18 => "OUT",
            3 or 4 => "IN",
            5 or 6 or 21 or 22 => "FCE",
            7 or 8 or 23 or 24 => "BAK",
            _ => part.ToString(CultureInfo.InvariantCulture)
        };
        return type is 16 or 17 or 18 or 19 or 20 or 21 or 22 or 23 && section.Length > 0
            ? unit + " " + section
            : unit;
    }

    /// <summary>A length in thousandths: its value when entered, empty otherwise.</summary>
    private static Cell Milli(byte[] data, int address, int mask, int bit, string name, int offset) =>
        (mask & (1 << bit)) != 0 ? MilliCell(name, Int32At(data, address + offset)) : Blank(name);

    /// <summary>
    /// A field a straight line does not use: always @ on a line, even when the record still holds an old value.
    /// On other shapes it prints its value when entered and stays empty otherwise.
    /// </summary>
    private static Cell MilliOr(byte[] data, int address, int mask, int bit, string name, int offset, bool line) =>
        line ? Unset(name) : Milli(data, address, mask, bit, name, offset);

    /// <summary>A whole number: its value when entered, otherwise the given placeholder.</summary>
    private static Cell Count(byte[] data, int address, int mask, int bit, string name, int offset, string unset = "") =>
        WordCell(name, (mask & (1 << bit)) != 0
            ? WordAt(data, address + offset).ToString(CultureInfo.InvariantCulture)
            : unset);

    /// <summary>
    /// The millimetre text is built from the stored integer so a thousandth is never lost to rounding.
    /// </summary>
    private static Cell MilliCell(string name, int raw) =>
        new(name, CellKind.Measure, MilliText(raw), raw / 1000d, true);

    private static string MilliText(int raw)
    {
        var size = Math.Abs((long)raw);
        var fraction = (size % 1000).ToString("000", CultureInfo.InvariantCulture).TrimEnd('0');
        return (raw < 0 ? "-" : "") + (size / 1000).ToString(CultureInfo.InvariantCulture) + "." + fraction;
    }
}
