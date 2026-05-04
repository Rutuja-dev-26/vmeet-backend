using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;

namespace VMeetTool.Helpers
{
    public class DbHelper
    {
        private static string ConnectionString
        {
            get
            {
                var entry = ConfigurationManager.ConnectionStrings["Sqlconnection"];

                if (entry == null)
                    throw new Exception(
                        "Connection string 'Sqlconnection' not found in Web.config. " +
                        "Make sure it is inside <connectionStrings>, not <appSettings>.");

                return entry.ConnectionString;
            }
        }

        public static DataTable ExecuteStoredProcedure(string procedureName, SqlParameter[] parameters = null)
        {
            var dt = new DataTable();

            using (var conn = new SqlConnection(ConnectionString))
            using (var cmd  = new SqlCommand(procedureName, conn))
            {
                cmd.CommandType = CommandType.StoredProcedure;

                if (parameters != null)
                    cmd.Parameters.AddRange(parameters);

                conn.Open();

                using (var adapter = new SqlDataAdapter(cmd))
                {
                    adapter.Fill(dt);
                }
            }

            return dt;
        }

        public static DataTable ExecuteQuery(string sql, SqlParameter[] parameters = null)
        {
            var dt = new DataTable();

            using (var conn = new SqlConnection(ConnectionString))
            using (var cmd  = new SqlCommand(sql, conn))
            {
                cmd.CommandType = CommandType.Text;

                if (parameters != null)
                    cmd.Parameters.AddRange(parameters);

                conn.Open();

                using (var adapter = new SqlDataAdapter(cmd))
                {
                    adapter.Fill(dt);
                }
            }

            return dt;
        }

        public static int ExecuteNonQuery(string sql, SqlParameter[] parameters = null)
        {
            using (var conn = new SqlConnection(ConnectionString))
            using (var cmd  = new SqlCommand(sql, conn))
            {
                cmd.CommandType = CommandType.Text;

                if (parameters != null)
                    cmd.Parameters.AddRange(parameters);

                conn.Open();
                return cmd.ExecuteNonQuery();
            }
        }
    }
}
