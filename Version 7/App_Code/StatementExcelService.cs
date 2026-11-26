using System;
using System.Collections.Generic;
using System.Data;
using System.Data.OleDb;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Web;

namespace PayrollWebApp
{
    // One line of the Staff Salary Disbursement statement.
    public class StatementRow
    {
        public string Branch;
        public string Name;
        public decimal Lates;
        public decimal Leaves;
        public decimal Permissions;
    }

    // Builds "Staff_Salary_Disbursement_dd-MM-yyyy_<Branch>.xlsx" in the same
    // layout as the paper/Excel statement: organisation heading, disbursement
    // date line, branch heading, S.No / Name / Lates / Leaves / Permissions /
    // Amount to be Deducted (a live Excel formula), and the footnote.
    // One worksheet per branch. No third-party library is needed.
    public static class StatementExcelService
    {
        // Same rule as Attendance finalisation (kept as an Excel formula so the
        // sheet stays correct if Lates/Leaves/Permissions are edited by hand):
        // Leaves + whole groups of 3 Lates + whole pairs of Permissions x 0.5 - 1 free day.
        public static decimal Deduction(decimal lates, decimal leaves, decimal permissions)
        {
            decimal value =
                leaves
                + Math.Floor(lates / 3m)
                + Math.Floor(permissions / 2m) * 0.5m
                - 1m;

            return value < 0m ? 0m : value;
        }


        // Organisation heading shown at the top of the statement.
        public static void GetOrganisationHeader(out string orgName, out string addressLine)
        {
            orgName = "";
            addressLine = "";

            try
            {
                DataTable org = Db.Query(
                    @"SELECT OrganizationName, AddressLine, City, StateName, Pincode, Mobile
                      FROM Tenants
                      WHERE TenantID=?",
                    new OleDbParameter("?", Auth.CurrentTenantId()));

                if (org.Rows.Count > 0)
                {
                    DataRow o = org.Rows[0];
                    orgName = Text(o["OrganizationName"]);

                    List<string> parts = new List<string>();
                    string[] columns = new string[] { "AddressLine", "City", "StateName", "Pincode" };

                    foreach (string column in columns)
                    {
                        string part = Text(o[column]);
                        if (part.Length > 0) parts.Add(part);
                    }

                    addressLine = String.Join(", ", parts.ToArray());

                    string phone = Text(o["Mobile"]);
                    if (phone.Length > 0)
                        addressLine += (addressLine.Length > 0 ? ". " : "") + "Ph. : " + phone;
                }
            }
            catch
            {
                // Heading details are optional.
            }

            if (orgName.Length == 0)
                orgName = Auth.CurrentOrganizationName();
        }


        public static string DateLine(DateTime disbursement)
        {
            return "Staff Salary Disbursement Dt: "
                + disbursement.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)
                + " ("
                + disbursement.ToString("dddd", CultureInfo.InvariantCulture).ToUpperInvariant()
                + ")";
        }


        public static string FileName(DateTime disbursement, string branchFilter)
        {
            string branchPart = String.IsNullOrWhiteSpace(branchFilter) ? "All_Branches" : branchFilter;

            foreach (char invalid in Path.GetInvalidFileNameChars())
                branchPart = branchPart.Replace(invalid, '_');

            return "Staff_Salary_Disbursement_"
                + disbursement.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)
                + "_" + branchPart.Replace(' ', '_') + ".xlsx";
        }


        // Sends the workbook to the browser.
        public static void Send(HttpResponse response, byte[] workbook, string fileName)
        {
            response.Clear();
            response.Buffer = true;
            response.ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
            response.AddHeader("Content-Disposition", "attachment; filename=\"" + fileName + "\"");
            response.BinaryWrite(workbook);
            response.Flush();
            HttpContext.Current.ApplicationInstance.CompleteRequest();
        }


        public static byte[] Build(
            string orgName,
            string addressLine,
            DateTime disbursement,
            List<StatementRow> rows)
        {
            // Group rows by branch, keeping the incoming order.
            List<string> branches = new List<string>();
            Dictionary<string, List<StatementRow>> byBranch = new Dictionary<string, List<StatementRow>>();

            foreach (StatementRow row in rows)
            {
                string key = row.Branch ?? "";

                if (!byBranch.ContainsKey(key))
                {
                    byBranch[key] = new List<StatementRow>();
                    branches.Add(key);
                }

                byBranch[key].Add(row);
            }

            if (branches.Count == 0)
            {
                branches.Add("Statement");
                byBranch["Statement"] = new List<StatementRow>();
            }

            // Worksheet names: max 31 chars, no []:*?/\ and unique.
            List<string> sheetNames = new List<string>();
            foreach (string branch in branches)
                sheetNames.Add(UniqueSheetName(branch, sheetNames));

            string dateLine = DateLine(disbursement);

            using (MemoryStream output = new MemoryStream())
            {
                using (ZipArchive archive = new ZipArchive(output, ZipArchiveMode.Create, true))
                {
                    StringBuilder contentTypes = new StringBuilder();
                    contentTypes.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
                    contentTypes.Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
                    contentTypes.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
                    contentTypes.Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");
                    contentTypes.Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
                    contentTypes.Append("<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
                    for (int i = 0; i < branches.Count; i++)
                    {
                        contentTypes.Append("<Override PartName=\"/xl/worksheets/sheet" + (i + 1).ToString(CultureInfo.InvariantCulture)
                            + ".xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
                    }
                    contentTypes.Append("</Types>");
                    WriteEntry(archive, "[Content_Types].xml", contentTypes.ToString());

                    WriteEntry(archive, "_rels/.rels",
                        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                        + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                        + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>"
                        + "</Relationships>");

                    StringBuilder workbook = new StringBuilder();
                    workbook.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
                    workbook.Append("<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
                    workbook.Append("<sheets>");
                    for (int i = 0; i < branches.Count; i++)
                    {
                        workbook.Append("<sheet name=\"" + Esc(sheetNames[i]) + "\" sheetId=\""
                            + (i + 1).ToString(CultureInfo.InvariantCulture) + "\" r:id=\"rId"
                            + (i + 1).ToString(CultureInfo.InvariantCulture) + "\"/>");
                    }
                    workbook.Append("</sheets><calcPr calcId=\"191029\" fullCalcOnLoad=\"1\"/></workbook>");
                    WriteEntry(archive, "xl/workbook.xml", workbook.ToString());

                    StringBuilder rels = new StringBuilder();
                    rels.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
                    rels.Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
                    for (int i = 0; i < branches.Count; i++)
                    {
                        rels.Append("<Relationship Id=\"rId" + (i + 1).ToString(CultureInfo.InvariantCulture)
                            + "\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet"
                            + (i + 1).ToString(CultureInfo.InvariantCulture) + ".xml\"/>");
                    }
                    rels.Append("<Relationship Id=\"rId" + (branches.Count + 1).ToString(CultureInfo.InvariantCulture)
                        + "\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
                    rels.Append("</Relationships>");
                    WriteEntry(archive, "xl/_rels/workbook.xml.rels", rels.ToString());

                    WriteEntry(archive, "xl/styles.xml", StylesXml());

                    for (int i = 0; i < branches.Count; i++)
                    {
                        WriteEntry(
                            archive,
                            "xl/worksheets/sheet" + (i + 1).ToString(CultureInfo.InvariantCulture) + ".xml",
                            SheetXml(orgName, addressLine, dateLine, branches[i], byBranch[branches[i]]));
                    }
                }

                return output.ToArray();
            }
        }


        // ---------------------------------------------------------
        // Styles (index used by SheetXml):
        // 0 default | 1 title 18 bold | 2 line 12 bold | 3 branch 14 bold
        // 4 header (grey, border, wrap) | 5 serial (bold centre, border)
        // 6 name (left, border) | 7 number centre (border)
        // 8 deduction centre (border, zero shows blank) | 9 footnote 9pt
        // ---------------------------------------------------------
        private static string StylesXml()
        {
            StringBuilder s = new StringBuilder();
            s.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            s.Append("<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
            s.Append("<numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"General;\\-General;;@\"/></numFmts>");
            s.Append("<fonts count=\"6\">");
            s.Append("<font><sz val=\"11\"/><name val=\"Cambria\"/></font>");
            s.Append("<font><b/><sz val=\"18\"/><name val=\"Cambria\"/></font>");
            s.Append("<font><b/><sz val=\"12\"/><name val=\"Cambria\"/></font>");
            s.Append("<font><b/><sz val=\"14\"/><name val=\"Cambria\"/></font>");
            s.Append("<font><b/><sz val=\"11\"/><name val=\"Cambria\"/></font>");
            s.Append("<font><sz val=\"12\"/><name val=\"Cambria\"/></font>");
            s.Append("</fonts>");
            s.Append("<fills count=\"3\">");
            s.Append("<fill><patternFill patternType=\"none\"/></fill>");
            s.Append("<fill><patternFill patternType=\"gray125\"/></fill>");
            s.Append("<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFE9ECEF\"/><bgColor indexed=\"64\"/></patternFill></fill>");
            s.Append("</fills>");
            s.Append("<borders count=\"2\">");
            s.Append("<border><left/><right/><top/><bottom/><diagonal/></border>");
            s.Append("<border><left style=\"thin\"><color auto=\"1\"/></left><right style=\"thin\"><color auto=\"1\"/></right><top style=\"thin\"><color auto=\"1\"/></top><bottom style=\"thin\"><color auto=\"1\"/></bottom><diagonal/></border>");
            s.Append("</borders>");
            s.Append("<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>");
            s.Append("<cellXfs count=\"10\">");
            s.Append("<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>");
            s.Append("<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\"/></xf>");
            s.Append("<xf numFmtId=\"0\" fontId=\"2\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\"/></xf>");
            s.Append("<xf numFmtId=\"0\" fontId=\"3\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\"/></xf>");
            s.Append("<xf numFmtId=\"0\" fontId=\"4\" fillId=\"2\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\" wrapText=\"1\"/></xf>");
            s.Append("<xf numFmtId=\"0\" fontId=\"2\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\"/></xf>");
            s.Append("<xf numFmtId=\"0\" fontId=\"5\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"left\" vertical=\"center\"/></xf>");
            s.Append("<xf numFmtId=\"0\" fontId=\"5\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\"/></xf>");
            s.Append("<xf numFmtId=\"164\" fontId=\"5\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyFont=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\"/></xf>");
            s.Append("<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyAlignment=\"1\"><alignment horizontal=\"left\" vertical=\"top\" wrapText=\"1\"/></xf>");
            s.Append("</cellXfs>");
            s.Append("<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>");
            s.Append("</styleSheet>");
            return s.ToString();
        }


        private static string SheetXml(
            string orgName,
            string addressLine,
            string dateLine,
            string branch,
            List<StatementRow> rows)
        {
            StringBuilder x = new StringBuilder();
            x.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            x.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
            x.Append("<sheetPr><pageSetUpPr fitToPage=\"1\"/></sheetPr>");
            x.Append("<sheetViews><sheetView workbookViewId=\"0\"/></sheetViews>");
            x.Append("<cols>"
                + "<col min=\"1\" max=\"1\" width=\"8\" customWidth=\"1\"/>"
                + "<col min=\"2\" max=\"2\" width=\"34\" customWidth=\"1\"/>"
                + "<col min=\"3\" max=\"4\" width=\"10\" customWidth=\"1\"/>"
                + "<col min=\"5\" max=\"5\" width=\"14\" customWidth=\"1\"/>"
                + "<col min=\"6\" max=\"6\" width=\"24\" customWidth=\"1\"/>"
                + "</cols>");
            x.Append("<sheetData>");

            // Rows 1-4: heading (merged A:F)
            x.Append("<row r=\"1\" ht=\"22.05\" customHeight=\"1\">" + TextCell("A1", orgName, 1) + "</row>");
            x.Append("<row r=\"2\" ht=\"15\" customHeight=\"1\">" + TextCell("A2", addressLine, 2) + "</row>");
            x.Append("<row r=\"3\" ht=\"15\" customHeight=\"1\">" + TextCell("A3", dateLine, 2) + "</row>");
            x.Append("<row r=\"4\" ht=\"17.35\" customHeight=\"1\">" + TextCell("A4", branch, 3) + "</row>");

            // Row 5: column headings
            x.Append("<row r=\"5\" ht=\"48\" customHeight=\"1\">"
                + TextCell("A5", "S. No.", 4)
                + TextCell("B5", "Name of the Staff", 4)
                + TextCell("C5", "Lates", 4)
                + TextCell("D5", "Leaves", 4)
                + TextCell("E5", "Permissions", 4)
                + TextCell("F5", "Amount to be Deducted /Days Present", 4)
                + "</row>");

            int r = 6;
            int serial = 0;

            foreach (StatementRow row in rows)
            {
                serial++;
                string n = r.ToString(CultureInfo.InvariantCulture);

                x.Append("<row r=\"" + n + "\" ht=\"24\" customHeight=\"1\">");
                x.Append("<c r=\"A" + n + "\" s=\"5\"><v>" + serial.ToString(CultureInfo.InvariantCulture) + "</v></c>");
                x.Append(TextCell("B" + n, row.Name, 6));
                x.Append(NumberCell("C" + n, row.Lates, 7));
                x.Append(NumberCell("D" + n, row.Leaves, 7));
                x.Append(NumberCell("E" + n, row.Permissions, 7));
                x.Append("<c r=\"F" + n + "\" s=\"8\"><f>MAX(0,D" + n + "+INT(C" + n + "/3)+INT(E" + n + "/2)*0.5-1)</f><v>"
                    + Num(Deduction(row.Lates, row.Leaves, row.Permissions)) + "</v></c>");
                x.Append("</row>");
                r++;
            }

            // Footnote two rows below the table (as in the paper format)
            int noteRow = r + 1;
            string noteN = noteRow.ToString(CultureInfo.InvariantCulture);
            x.Append("<row r=\"" + noteN + "\" ht=\"28\" customHeight=\"1\">"
                + TextCell("A" + noteN,
                    "Note: Amount to be Deducted = Leaves + (Lates \u00F7 3, whole groups) + (Permissions \u00F7 2, whole pairs \u00D7 0.5) \u2212 1 free day; zero shows blank. "
                    + "Leaves = Casual Leave + Absent + Half Days. Lates, Leaves, Permissions come from the finalised Attendance statement.", 9)
                + "</row>");

            x.Append("</sheetData>");

            x.Append("<mergeCells count=\"5\">"
                + "<mergeCell ref=\"A1:F1\"/><mergeCell ref=\"A2:F2\"/><mergeCell ref=\"A3:F3\"/><mergeCell ref=\"A4:F4\"/>"
                + "<mergeCell ref=\"A" + noteN + ":F" + noteN + "\"/>"
                + "</mergeCells>");

            x.Append("<printOptions horizontalCentered=\"1\"/>");
            x.Append("<pageMargins left=\"0.5\" right=\"0.5\" top=\"0.6\" bottom=\"0.6\" header=\"0.3\" footer=\"0.3\"/>");
            x.Append("<pageSetup paperSize=\"9\" orientation=\"landscape\" fitToWidth=\"1\" fitToHeight=\"0\"/>");
            x.Append("</worksheet>");
            return x.ToString();
        }


        private static string TextCell(string reference, string value, int style)
        {
            return "<c r=\"" + reference + "\" t=\"inlineStr\" s=\""
                + style.ToString(CultureInfo.InvariantCulture)
                + "\"><is><t xml:space=\"preserve\">" + Esc(value) + "</t></is></c>";
        }


        // Zero is left empty (like the paper sheet) but keeps the bordered style.
        private static string NumberCell(string reference, decimal value, int style)
        {
            if (value == 0m)
                return "<c r=\"" + reference + "\" s=\"" + style.ToString(CultureInfo.InvariantCulture) + "\"/>";

            return "<c r=\"" + reference + "\" s=\"" + style.ToString(CultureInfo.InvariantCulture)
                + "\"><v>" + Num(value) + "</v></c>";
        }


        private static string Num(decimal value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }


        private static string Text(object value)
        {
            if (value == null || value == DBNull.Value) return "";
            return Convert.ToString(value, CultureInfo.InvariantCulture).Trim();
        }


        private static string Esc(string value)
        {
            return System.Security.SecurityElement.Escape(value ?? String.Empty) ?? String.Empty;
        }


        private static string UniqueSheetName(string branch, List<string> used)
        {
            string name = String.IsNullOrWhiteSpace(branch) ? "Statement" : branch.Trim();

            foreach (char bad in new char[] { '[', ']', ':', '*', '?', '/', '\\' })
                name = name.Replace(bad, '_');

            if (name.Length > 31) name = name.Substring(0, 31);

            string candidate = name;
            int counter = 2;

            while (used.Exists(delegate (string u) { return String.Equals(u, candidate, StringComparison.OrdinalIgnoreCase); }))
            {
                string suffix = "_" + counter.ToString(CultureInfo.InvariantCulture);
                candidate = (name.Length + suffix.Length > 31 ? name.Substring(0, 31 - suffix.Length) : name) + suffix;
                counter++;
            }

            return candidate;
        }


        private static void WriteEntry(ZipArchive archive, string entryName, string content)
        {
            ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);

            using (Stream stream = entry.Open())
            using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(content);
            }
        }
    }
}
