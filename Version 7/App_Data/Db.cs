using System;
using System.Configuration;
using System.Data;
using System.Data.OleDb;

namespace PayrollWebApp
{
    public static class Db
    {
        public static string ConnectionString
        {
            get
            {
                return ConfigurationManager.ConnectionStrings["PayrollDb"].ConnectionString;
            }
        }

        public static DataTable Query(string sql, params OleDbParameter[] parameters)
        {
            using (var cn = new OleDbConnection(ConnectionString))
            using (var cmd = new OleDbCommand(sql, cn))
            using (var da = new OleDbDataAdapter(cmd))
            {
                if (parameters != null) cmd.Parameters.AddRange(parameters);
                var dt = new DataTable();
                da.Fill(dt);
                return dt;
            }
        }

        public static int Execute(string sql, params OleDbParameter[] parameters)
        {
            using (var cn = new OleDbConnection(ConnectionString))
            using (var cmd = new OleDbCommand(sql, cn))
            {
                if (parameters != null) cmd.Parameters.AddRange(parameters);
                cn.Open();
                return cmd.ExecuteNonQuery();
            }
        }

        public static object Scalar(string sql, params OleDbParameter[] parameters)
        {
            using (var cn = new OleDbConnection(ConnectionString))
            using (var cmd = new OleDbCommand(sql, cn))
            {
                if (parameters != null) cmd.Parameters.AddRange(parameters);
                cn.Open();
                return cmd.ExecuteScalar();
            }
        }
    }
}
