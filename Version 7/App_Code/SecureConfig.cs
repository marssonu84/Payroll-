using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Linq;
using System.Xml.Linq;

namespace PayrollWebApp
{
    public static class SecureConfig
    {
        private static string ConfigPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Database.config"); }
        }

        public static string GetConnectionString()
        {
            if (!File.Exists(ConfigPath))
                throw new InvalidOperationException("Database.config was not found.");

            XDocument doc = XDocument.Load(ConfigPath);
            XElement item = doc.Descendants("add").FirstOrDefault(x => (string)x.Attribute("name") == "PayrollDb");
            if (item == null)
                throw new InvalidOperationException("PayrollDb is missing from Database.config.");

            string value = (string)item.Attribute("connectionString");
            if (String.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException("PayrollDb connection string is empty.");

            if (value.StartsWith("ENC:", StringComparison.Ordinal))
                return Decrypt(value.Substring(4));

            return value;
        }

        public static void SaveEncryptedConnectionString(string connectionString)
        {
            if (String.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("Connection string is required.", "connectionString");

            XDocument doc = File.Exists(ConfigPath)
                ? XDocument.Load(ConfigPath)
                : new XDocument(new XElement("connectionStrings"));

            XElement root = doc.Element("connectionStrings");
            if (root == null)
            {
                root = new XElement("connectionStrings");
                doc.Add(root);
            }

            XElement item = root.Elements("add").FirstOrDefault(x => (string)x.Attribute("name") == "PayrollDb");
            if (item == null)
            {
                item = new XElement("add",
                    new XAttribute("name", "PayrollDb"),
                    new XAttribute("providerName", "System.Data.SqlClient"));
                root.Add(item);
            }

            item.SetAttributeValue("connectionString", "ENC:" + Encrypt(connectionString));
            item.SetAttributeValue("providerName", "System.Data.SqlClient");
            doc.Save(ConfigPath);
        }

        // True when Database.config still holds the shipped YOUR_... placeholder values.
        public static bool IsPlaceholder()
        {
            try
            {
                if (!File.Exists(ConfigPath)) return true;
                XDocument doc = XDocument.Load(ConfigPath);
                XElement item = doc.Descendants("add").FirstOrDefault(x => (string)x.Attribute("name") == "PayrollDb");
                string value = item == null ? null : (string)item.Attribute("connectionString");
                if (String.IsNullOrWhiteSpace(value)) return true;
                if (value.StartsWith("ENC:", StringComparison.Ordinal)) return false;
                return value.IndexOf("YOUR_", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        public static bool IsEncrypted()
        {
            if (!File.Exists(ConfigPath)) return false;
            XDocument doc = XDocument.Load(ConfigPath);
            XElement item = doc.Descendants("add").FirstOrDefault(x => (string)x.Attribute("name") == "PayrollDb");
            string value = item == null ? null : (string)item.Attribute("connectionString");
            return !String.IsNullOrWhiteSpace(value) && value.StartsWith("ENC:", StringComparison.Ordinal);
        }

        private static string Encrypt(string plainText)
        {
            byte[] plain = Encoding.UTF8.GetBytes(plainText);
            byte[] cipher = ProtectedData.Protect(plain, null, DataProtectionScope.LocalMachine);
            return Convert.ToBase64String(cipher);
        }

        private static string Decrypt(string base64)
        {
            try
            {
                byte[] cipher = Convert.FromBase64String(base64);
                byte[] plain = ProtectedData.Unprotect(cipher, null, DataProtectionScope.LocalMachine);
                return Encoding.UTF8.GetString(plain);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Database.config is encrypted for a different Windows machine or is corrupt.", ex);
            }
        }
    }
}
