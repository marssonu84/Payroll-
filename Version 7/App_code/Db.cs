using System;
using System.Configuration;
using System.Data;
using System.Data.OleDb;
using System.Data.SqlClient;
using System.Text.RegularExpressions;

namespace PayrollWebApp
{
    public static class Db
    {
        public static string ConnectionString
        {
            get
            {
                ConnectionStringSettings settings =
                    ConfigurationManager.ConnectionStrings["PayrollDb"];

                if (settings == null ||
                    String.IsNullOrWhiteSpace(settings.ConnectionString))
                {
                    throw new InvalidOperationException(
                        "PayrollDb connection string is missing."
                    );
                }

                return settings.ConnectionString;
            }
        }

        private static SqlCommand CreateCommand(
            SqlConnection con,
            string sql,
            OleDbParameter[] parameters)
        {
            if (con == null)
            {
                throw new ArgumentNullException("con");
            }

            if (String.IsNullOrWhiteSpace(sql))
            {
                throw new ArgumentException(
                    "SQL query cannot be empty.",
                    "sql"
                );
            }

            sql = ConvertAccessSql(sql);

            SqlCommand cmd = new SqlCommand();
            cmd.Connection = con;

            int count =
                parameters == null
                    ? 0
                    : parameters.Length;

            for (int i = 0; i < count; i++)
            {
                string paramName =
                    "@p" + i.ToString();

                int pos =
                    sql.IndexOf(
                        "?",
                        StringComparison.Ordinal
                    );

                if (pos < 0)
                {
                    throw new InvalidOperationException(
                        "SQL parameter count does not match query."
                    );
                }

                sql =
                    sql.Substring(0, pos) +
                    paramName +
                    sql.Substring(pos + 1);

                object value =
                    parameters[i] == null
                        ? DBNull.Value
                        : parameters[i].Value;

                if (value == null)
                {
                    value = DBNull.Value;
                }

                cmd.Parameters.AddWithValue(
                    paramName,
                    value
                );
            }

            if (sql.IndexOf(
                    "?",
                    StringComparison.Ordinal) >= 0)
            {
                throw new InvalidOperationException(
                    "SQL query contains unmatched parameter."
                );
            }

            cmd.CommandText = sql;

            return cmd;
        }

        private static string ConvertAccessSql(
            string sql)
        {
            if (String.IsNullOrEmpty(sql))
            {
                return sql;
            }

            sql = Regex.Replace(
                sql,
                @"\bTrue\b",
                "1",
                RegexOptions.IgnoreCase
            );

            sql = Regex.Replace(
                sql,
                @"\bFalse\b",
                "0",
                RegexOptions.IgnoreCase
            );

            return sql;
        }

        public static DataTable Query(
            string sql,
            params OleDbParameter[] parameters)
        {
            using (SqlConnection con =
                new SqlConnection(ConnectionString))
            using (SqlCommand cmd =
                CreateCommand(
                    con,
                    sql,
                    parameters
                ))
            using (SqlDataAdapter da =
                new SqlDataAdapter(cmd))
            {
                DataTable dt =
                    new DataTable();

                da.Fill(dt);

                return dt;
            }
        }

        public static int Execute(
            string sql,
            params OleDbParameter[] parameters)
        {
            using (SqlConnection con =
                new SqlConnection(ConnectionString))
            using (SqlCommand cmd =
                CreateCommand(
                    con,
                    sql,
                    parameters
                ))
            {
                con.Open();

                return cmd.ExecuteNonQuery();
            }
        }

        public static object Scalar(
            string sql,
            params OleDbParameter[] parameters)
        {
            using (SqlConnection con =
                new SqlConnection(ConnectionString))
            using (SqlCommand cmd =
                CreateCommand(
                    con,
                    sql,
                    parameters
                ))
            {
                con.Open();

                return cmd.ExecuteScalar();
            }
        }
    }
}



