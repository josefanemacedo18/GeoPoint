using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace GeoPoint.Core.Office;

public enum XlStyle { Text = 0, Header = 1, Num2 = 2, Num3 = 3, Num4 = 4, Int = 5, Bold = 6, Num8 = 7 }

public sealed class XlCell
{
    public XlCell(string text, XlStyle style = XlStyle.Text) { Text = text; Style = style; }
    public XlCell(double number, XlStyle style = XlStyle.Num2) { Number = number; Style = style; }
    public string? Text { get; }
    public double? Number { get; }
    public XlStyle Style { get; }

    public static implicit operator XlCell(string s) => new(s);
    public static implicit operator XlCell(double d) => new(d);
}

public sealed class XlSheet
{
    public XlSheet(string name) { Name = SanitizeName(name); }
    public string Name { get; }
    public List<List<XlCell?>> Rows { get; } = new();
    public List<double> ColumnWidths { get; } = new();
    public bool FreezeHeader { get; set; } = true;
    public bool AutoFilter { get; set; } = true;

    public void AddRow(params XlCell?[] cells) => Rows.Add(cells.ToList());
    public void AddHeader(params string[] titles) => Rows.Add(titles.Select(t => (XlCell?)new XlCell(t, XlStyle.Header)).ToList());

    private static string SanitizeName(string n)
    {
        foreach (var ch in new[] { '\\', '/', '?', '*', '[', ']', ':' }) n = n.Replace(ch, '-');
        if (n.Length > 31) n = n[..31];
        return string.IsNullOrWhiteSpace(n) ? "Planilha" : n;
    }
}

/// <summary>Gerador mínimo de planilhas .xlsx (SpreadsheetML), sem dependências.</summary>
public static class XlsxWriter
{
    public static byte[] Build(IReadOnlyList<XlSheet> sheets)
    {
        if (sheets.Count == 0) throw new ArgumentException("Nenhuma planilha.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var finalNames = new List<string>();
        foreach (var s in sheets)
        {
            string n = s.Name; int k = 2;
            while (!names.Add(n)) n = (s.Name.Length > 27 ? s.Name[..27] : s.Name) + " (" + k++ + ")";
            finalNames.Add(n);
        }

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            var ct = new StringBuilder();
            ct.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
            ct.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>");
            ct.Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
            ct.Append("<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
            for (int i = 0; i < sheets.Count; i++)
                ct.Append($"<Override PartName=\"/xl/worksheets/sheet{i + 1}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
            ct.Append("</Types>");
            DocxPackage.Put(zip, "[Content_Types].xml", ct.ToString());
            DocxPackage.Put(zip, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");

            var wb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>");
            var wbRels = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
            var defined = new StringBuilder();
            for (int i = 0; i < sheets.Count; i++)
            {
                wb.Append($"<sheet name=\"{Esc(finalNames[i])}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>");
                wbRels.Append($"<Relationship Id=\"rId{i + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>");
                var s = sheets[i];
                if (s.AutoFilter && s.Rows.Count > 1)
                {
                    int cols = s.Rows.Max(r => r.Count);
                    defined.Append($"<definedName name=\"_xlnm._FilterDatabase\" localSheetId=\"{i}\" hidden=\"1\">'{Esc(finalNames[i].Replace("'", "''"))}'!$A$1:${ColName(cols - 1)}${s.Rows.Count}</definedName>");
                }
            }
            wb.Append("</sheets>");
            if (defined.Length > 0) wb.Append("<definedNames>").Append(defined).Append("</definedNames>");
            wb.Append("</workbook>");
            wbRels.Append($"<Relationship Id=\"rId{sheets.Count + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
            DocxPackage.Put(zip, "xl/workbook.xml", wb.ToString());
            DocxPackage.Put(zip, "xl/_rels/workbook.xml.rels", wbRels.ToString());
            DocxPackage.Put(zip, "xl/styles.xml", Styles);
            for (int i = 0; i < sheets.Count; i++) DocxPackage.Put(zip, $"xl/worksheets/sheet{i + 1}.xml", SheetXml(sheets[i]));
        }
        return ms.ToArray();
    }

    private const string Styles = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
        "<numFmts count=\"5\"><numFmt numFmtId=\"164\" formatCode=\"#,##0.00\"/><numFmt numFmtId=\"165\" formatCode=\"#,##0.000\"/><numFmt numFmtId=\"166\" formatCode=\"#,##0.0000\"/><numFmt numFmtId=\"167\" formatCode=\"0\"/><numFmt numFmtId=\"168\" formatCode=\"0.00000000\"/></numFmts>" +
        "<fonts count=\"2\"><font><sz val=\"10\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"10\"/><name val=\"Calibri\"/></font></fonts>" +
        "<fills count=\"3\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill><fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFD9E2F3\"/><bgColor indexed=\"64\"/></patternFill></fill></fills>" +
        "<borders count=\"2\"><border><left/><right/><top/><bottom/><diagonal/></border><border><left style=\"thin\"><color rgb=\"FFA6A6A6\"/></left><right style=\"thin\"><color rgb=\"FFA6A6A6\"/></right><top style=\"thin\"><color rgb=\"FFA6A6A6\"/></top><bottom style=\"thin\"><color rgb=\"FFA6A6A6\"/></bottom><diagonal/></border></borders>" +
        "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
        "<cellXfs count=\"8\">" +
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyBorder=\"1\"/>" +
        "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\" wrapText=\"1\"/></xf>" +
        "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyBorder=\"1\"/>" +
        "<xf numFmtId=\"165\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyBorder=\"1\"/>" +
        "<xf numFmtId=\"166\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyBorder=\"1\"/>" +
        "<xf numFmtId=\"167\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyBorder=\"1\"/>" +
        "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyBorder=\"1\"/>" +
        "<xf numFmtId=\"168\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyBorder=\"1\"/>" +
        "</cellXfs><cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>";

    private static string SheetXml(XlSheet s)
    {
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
        if (s.FreezeHeader && s.Rows.Count > 1)
            sb.Append("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
        int cols = s.Rows.Count == 0 ? 0 : s.Rows.Max(r => r.Count);
        if (cols > 0)
        {
            sb.Append("<cols>");
            for (int c = 0; c < cols; c++)
            {
                double w = c < s.ColumnWidths.Count ? s.ColumnWidths[c] : AutoWidth(s, c);
                sb.Append($"<col min=\"{c + 1}\" max=\"{c + 1}\" width=\"{w.ToString("0.##", CultureInfo.InvariantCulture)}\" customWidth=\"1\"/>");
            }
            sb.Append("</cols>");
        }
        sb.Append("<sheetData>");
        for (int r = 0; r < s.Rows.Count; r++)
        {
            sb.Append($"<row r=\"{r + 1}\">");
            var row = s.Rows[r];
            for (int c = 0; c < row.Count; c++)
            {
                var cell = row[c];
                if (cell is null) continue;
                string refc = ColName(c) + (r + 1);
                int st = (int)cell.Style;
                if (cell.Number is double d && double.IsFinite(d))
                    sb.Append($"<c r=\"{refc}\" s=\"{st}\"><v>{d.ToString("R", CultureInfo.InvariantCulture)}</v></c>");
                else
                    sb.Append($"<c r=\"{refc}\" s=\"{st}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{Esc(cell.Text ?? "")}</t></is></c>");
            }
            sb.Append("</row>");
        }
        sb.Append("</sheetData>");
        if (s.AutoFilter && s.Rows.Count > 1 && cols > 0) sb.Append($"<autoFilter ref=\"A1:{ColName(cols - 1)}{s.Rows.Count}\"/>");
        sb.Append("<pageMargins left=\"0.7\" right=\"0.7\" top=\"0.75\" bottom=\"0.75\" header=\"0.3\" footer=\"0.3\"/></worksheet>");
        return sb.ToString();
    }

    private static double AutoWidth(XlSheet s, int c)
    {
        int max = 8;
        foreach (var row in s.Rows)
        {
            if (c >= row.Count || row[c] is null) continue;
            var cell = row[c]!;
            int len = cell.Text?.Length ?? (cell.Number is double d ? d.ToString("#,##0.0000", CultureInfo.InvariantCulture).Length : 0);
            max = Math.Max(max, len);
        }
        return Math.Min(60, max + 2);
    }

    public static string ColName(int index)
    {
        var sb = new StringBuilder();
        index++;
        while (index > 0)
        {
            int m = (index - 1) % 26;
            sb.Insert(0, (char)('A' + m));
            index = (index - 1) / 26;
        }
        return sb.ToString();
    }

    private static string Esc(string s) => SecurityElement.Escape(Wml.Clean(s)) ?? "";
}
