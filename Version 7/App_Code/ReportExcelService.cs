using System;
using System.Data;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Web;

namespace PayrollWebApp
{
    /// <summary>
    /// Builds a simple single-sheet .xlsx from a DataTable for the Reports page.
    /// No third-party library. Layout: org name, salary month, report heading,
    /// column headers, data rows, optional TOTAL row for numeric columns.
    /// </summary>
    public static class ReportExcelService
    {
        public static void Send(HttpResponse response, DataTable data, string fileName, string heading, DateTime month)
        {
            if (response == null) throw new ArgumentNullException("response");
            if (data == null) data = new DataTable();

            byte[] bytes = Build(data, heading, month);

            response.Clear();
            response.Buffer = true;
            response.ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
            response.AddHeader("Content-Disposition", "attachment; filename=\"" + SanitizeFileName(fileName) + "\"");
            response.BinaryWrite(bytes);
            response.Flush();
            HttpContext.Current.ApplicationInstance.CompleteRequest();
        }

        public static byte[] Build(DataTable data, string heading, DateTime month)
        {
            string orgName = "";
            try { orgName = Auth.CurrentOrganizationName() ?? ""; }
            catch { orgName = ""; }

            string monthLabel = month.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
            if (string.IsNullOrEmpty(heading)) heading = "REPORT";

            int colCount = Math.Max(1, data.Columns.Count);
            // Row layout:
            // 1 = org, 2 = month, 3 = heading, 4 = blank, 5 = headers, 6+ = data, last = totals (if any)
            int headerRow = 5;
            int firstDataRow = 6;
            int dataRows = data.Rows.Count;
            int totalRow = dataRows > 0 ? firstDataRow + dataRows : 0;

            decimal[] sums = new decimal[colCount];
            bool[] isNumeric = new bool[colCount];
            for (int c = 0; c < colCount; c++)
                isNumeric[c] = IsNumericColumn(data, c);

            using (MemoryStream output = new MemoryStream())
            {
                using (ZipArchive archive = new ZipArchive(output, ZipArchiveMode.Create, true))
                {
                    WriteEntry(archive, "[Content_Types].xml",
                        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                        + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
                        + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>"
                        + "<Default Extension=\"xml\" ContentType=\"application/xml\"/>"
                        + "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>"
                        + "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>"
                        + "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>"
                        + "</Types>");

                    WriteEntry(archive, "_rels/.rels",
                        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                        + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                        + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>"
                        + "</Relationships>");

                    WriteEntry(archive, "xl/workbook.xml",
                        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                        + "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">"
                        + "<sheets><sheet name=\"Report\" sheetId=\"1\" r:id=\"rId1\"/></sheets>"
                        + "</workbook>");

                    WriteEntry(archive, "xl/_rels/workbook.xml.rels",
                        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                        + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                        + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>"
                        + "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>"
                        + "</Relationships>");

                    WriteEntry(archive, "xl/styles.xml", StylesXml());

                    // Worksheet
                    StringBuilder sheet = new StringBuilder();
                    sheet.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
                    sheet.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
                    sheet.Append("<sheetData>");

                    // Row 1 – organisation
                    sheet.Append(RowOpen(1));
                    sheet.Append(CellInline(1, 1, orgName, 1));
                    sheet.Append("</row>");

                    // Row 2 – month
                    sheet.Append(RowOpen(2));
                    sheet.Append(CellInline(2, 1, "Salary Month: " + monthLabel, 0));
                    sheet.Append("</row>");

                    // Row 3 – heading
                    sheet.Append(RowOpen(3));
                    sheet.Append(CellInline(3, 1, heading, 1));
                    sheet.Append("</row>");

                    // Row 4 – blank
                    sheet.Append(RowOpen(4));
                    sheet.Append("</row>");

                    // Row 5 – column headers
                    sheet.Append(RowOpen(headerRow));
                    for (int c = 0; c < colCount; c++)
                    {
                        string name = data.Columns.Count > 0 ? data.Columns[c].ColumnName : "Column";
                        sheet.Append(CellInline(headerRow, c + 1, name, 2));
                    }
                    sheet.Append("</row>");

                    // Data rows
                    for (int r = 0; r < dataRows; r++)
                    {
                        int excelRow = firstDataRow + r;
                        sheet.Append(RowOpen(excelRow));
                        DataRow row = data.Rows[r];
                        for (int c = 0; c < colCount; c++)
                        {
                            object v = row[c];
                            if (v == null || v == DBNull.Value)
                            {
                                sheet.Append(CellInline(excelRow, c + 1, "", 0));
                                continue;
                            }

                            decimal n;
                            if (isNumeric[c] && decimal.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out n))
                            {
                                sums[c] += n;
                                sheet.Append(CellNumber(excelRow, c + 1, n, 3));
                            }
                            else
                            {
                                sheet.Append(CellInline(excelRow, c + 1, Convert.ToString(v), 0));
                            }
                        }
                        sheet.Append("</row>");
                    }

                    // TOTAL row
                    if (dataRows > 0)
                    {
                        sheet.Append(RowOpen(totalRow));
                        for (int c = 0; c < colCount; c++)
                        {
                            string name = (data.Columns[c].ColumnName ?? "").ToUpperInvariant();
                            if (c == 0)
                            {
                                sheet.Append(CellInline(totalRow, c + 1, "TOTAL", 2));
                            }
                            else if (name.Contains("NAME") || name.Contains("BANK") || name.Contains("ACCOUNT") || name.Contains("BRANCH"))
                            {
                                sheet.Append(CellInline(totalRow, c + 1, "", 0));
                            }
                            else if (isNumeric[c])
                            {
                                sheet.Append(CellNumber(totalRow, c + 1, sums[c], 2));
                            }
                            else
                            {
                                sheet.Append(CellInline(totalRow, c + 1, "", 0));
                            }
                        }
                        sheet.Append("</row>");
                    }

                    sheet.Append("</sheetData>");
                    sheet.Append("</worksheet>");
                    WriteEntry(archive, "xl/worksheets/sheet1.xml", sheet.ToString());
                }

                return output.ToArray();
            }
        }

        // ------------------------------------------------------------------
        // Cell / style helpers
        // ------------------------------------------------------------------

        private static bool IsNumericColumn(DataTable data, int colIndex)
        {
            if (data == null || data.Columns.Count == 0 || colIndex >= data.Columns.Count)
                return false;

            Type t = data.Columns[colIndex].DataType;
            if (t == typeof(decimal) || t == typeof(double) || t == typeof(float)
                || t == typeof(int) || t == typeof(long) || t == typeof(short)
                || t == typeof(byte) || t == typeof(uint) || t == typeof(ulong))
                return true;

            // Heuristic: if most non-empty values parse as numbers, treat as numeric
            int checkedCount = 0, numericCount = 0;
            foreach (DataRow r in data.Rows)
            {
                object v = r[colIndex];
                if (v == null || v == DBNull.Value) continue;
                string s = Convert.ToString(v, CultureInfo.InvariantCulture);
                if (string.IsNullOrWhiteSpace(s)) continue;
                checkedCount++;
                decimal n;
                if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out n))
                    numericCount++;
                if (checkedCount >= 20) break;
            }
            return checkedCount > 0 && numericCount * 2 >= checkedCount;
        }

        private static string RowOpen(int rowIndex)
        {
            return "<row r=\"" + rowIndex.ToString(CultureInfo.InvariantCulture) + "\">";
        }

        private static string CellRef(int row, int col)
        {
            return ColLetter(col) + row.ToString(CultureInfo.InvariantCulture);
        }

        private static string ColLetter(int col1Based)
        {
            // Supports A..Z, AA.. (enough for report columns)
            col1Based = Math.Max(1, col1Based);
            StringBuilder sb = new StringBuilder();
            int n = col1Based;
            while (n > 0)
            {
                n--;
                sb.Insert(0, (char)('A' + (n % 26)));
                n /= 26;
            }
            return sb.ToString();
        }

        private static string CellInline(int row, int col, string text, int style)
        {
            string t = EscapeXml(text ?? "");
            string s = style > 0 ? " s=\"" + style.ToString(CultureInfo.InvariantCulture) + "\"" : "";
            return "<c r=\"" + CellRef(row, col) + "\" t=\"inlineStr\"" + s + "><is><t>" + t + "</t></is></c>";
        }

        private static string CellNumber(int row, int col, decimal value, int style)
        {
            string s = style > 0 ? " s=\"" + style.ToString(CultureInfo.InvariantCulture) + "\"" : "";
            return "<c r=\"" + CellRef(row, col) + "\"" + s + "><v>"
                + value.ToString("0.##", CultureInfo.InvariantCulture)
                + "</v></c>";
        }

        private static string StylesXml()
        {
            // 0 = default, 1 = bold title, 2 = bold header/total, 3 = number 0.00
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                + "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
                + "<fonts count=\"3\">"
                + "<font><sz val=\"11\"/><name val=\"Calibri\"/></font>"
                + "<font><b/><sz val=\"12\"/><name val=\"Calibri\"/></font>"
                + "<font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font>"
                + "</fonts>"
                + "<fills count=\"2\">"
                + "<fill><patternFill patternType=\"none\"/></fill>"
                + "<fill><patternFill patternType=\"gray125\"/></fill>"
                + "</fills>"
                + "<borders count=\"1\"><border/></borders>"
                + "<cellStyleXfs count=\"1\"><xf/></cellStyleXfs>"
                + "<cellXfs count=\"4\">"
                + "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>"
                + "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>"
                + "<xf numFmtId=\"0\" fontId=\"2\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>"
                + "<xf numFmtId=\"2\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>"
                + "</cellXfs>"
                + "</styleSheet>";
        }

        private static string EscapeXml(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Report.xlsx";
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            if (!name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                name += ".xlsx";
            return name;
        }

        private static void WriteEntry(ZipArchive archive, string path, string content)
        {
            ZipArchiveEntry entry = archive.CreateEntry(path, CompressionLevel.Fastest);
            using (Stream stream = entry.Open())
            using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(content);
            }
        }
    }
}
