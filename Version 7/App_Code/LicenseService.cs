using System;
using System.Configuration;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace PayrollWebApp
{
    public sealed class LicenseInfo
    {
        public string CustomerCode { get; set; }
        public string CustomerName { get; set; }
        public string Edition { get; set; }
        public DateTime? ValidFrom { get; set; }
        public DateTime? ValidUntil { get; set; }
        public string LicenseKey { get; set; }
    }

    public static class LicenseService
    {
        private static string LicensePath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App_Data", "License.xml"); } }

        public static bool IsConfigured { get { return File.Exists(LicensePath); } }

        public static LicenseInfo GetCurrent()
        {
            if (!File.Exists(LicensePath)) return null;
            try
            {
                XDocument doc = XDocument.Load(LicensePath);
                XElement x = doc.Element("License");
                if (x == null) return null;
                return new LicenseInfo
                {
                    CustomerCode = (string)x.Attribute("CustomerCode"),
                    CustomerName = (string)x.Attribute("CustomerName"),
                    Edition = (string)x.Attribute("Edition"),
                    LicenseKey = (string)x.Attribute("LicenseKey"),
                    ValidFrom = ParseDate((string)x.Attribute("ValidFrom")),
                    ValidUntil = ParseDate((string)x.Attribute("ValidUntil"))
                };
            }
            catch { return null; }
        }

        public static string Validate(string xml)
        {
            if (String.IsNullOrWhiteSpace(xml)) return "License XML is required.";
            try
            {
                XDocument doc = XDocument.Parse(xml, LoadOptions.None);
                XElement x = doc.Element("License");
                if (x == null) return "Root element License is missing.";
                string key = (string)x.Attribute("LicenseKey");
                string customer = (string)x.Attribute("CustomerCode");
                string edition = (string)x.Attribute("Edition");
                string from = (string)x.Attribute("ValidFrom");
                string until = (string)x.Attribute("ValidUntil");
                string signature = (string)x.Attribute("Signature");
                if (String.IsNullOrWhiteSpace(key) || String.IsNullOrWhiteSpace(customer) || String.IsNullOrWhiteSpace(signature))
                    return "LicenseKey, CustomerCode and Signature are required.";

                string publicKey = ConfigurationManager.AppSettings["LicensePublicKeyXml"];
                if (String.IsNullOrWhiteSpace(publicKey)) return "LicensePublicKeyXml is not configured in Web.config.";

                string payload = String.Join("|", key, customer, edition ?? "", from ?? "", until ?? "");
                byte[] data = Encoding.UTF8.GetBytes(payload);
                byte[] sig = Convert.FromBase64String(signature);
                using (RSA rsa = RSA.Create())
                {
                    rsa.FromXmlString(publicKey);
                    if (!rsa.VerifyData(data, sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                        return "License signature is invalid.";
                }

                DateTime? validFrom = ParseDate(from);
                DateTime? validUntil = ParseDate(until);
                DateTime today = DateTime.UtcNow.Date;
                if (validFrom.HasValue && today < validFrom.Value.Date) return "License is not active yet.";
                if (validUntil.HasValue && today > validUntil.Value.Date) return "License has expired.";
                return null;
            }
            catch (FormatException) { return "License signature/date format is invalid."; }
            catch (Exception ex) { return "License validation failed: " + ex.Message; }
        }

        public static void Install(string xml, string installedBy)
        {
            string error = Validate(xml);
            if (error != null) throw new InvalidOperationException(error);
            Directory.CreateDirectory(Path.GetDirectoryName(LicensePath));
            File.WriteAllText(LicensePath, xml, Encoding.UTF8);
            LicenseInfo info = GetCurrent();
            FutureScopeService.EnsureSchema();
            Db.Execute("INSERT INTO PlatformLicenses(LicenseKey,CustomerCode,CustomerName,Edition,ValidFrom,ValidUntil,InstalledBy) VALUES(?,?,?,?,?,?,?)",
                new System.Data.OleDb.OleDbParameter("", info.LicenseKey),
                new System.Data.OleDb.OleDbParameter("", info.CustomerCode),
                new System.Data.OleDb.OleDbParameter("", info.CustomerName),
                new System.Data.OleDb.OleDbParameter("", info.Edition),
                new System.Data.OleDb.OleDbParameter("", (object)info.ValidFrom ?? DBNull.Value),
                new System.Data.OleDb.OleDbParameter("", (object)info.ValidUntil ?? DBNull.Value),
                new System.Data.OleDb.OleDbParameter("", installedBy));
            FutureScopeService.Audit("LicenseInstalled", info.CustomerCode);
        }

        private static DateTime? ParseDate(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            DateTime dt;
            if (!DateTime.TryParse(value, out dt)) throw new FormatException();
            return dt.Date;
        }
    }
}
