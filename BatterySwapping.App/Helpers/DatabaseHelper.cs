using System;
using System.Data;
using Microsoft.Data.SqlClient;

namespace BatterySwapping.App.Helpers
{
    /// <summary>
    /// Tầng truy cập dữ liệu (Data Access Layer - DAL) sử dụng ADO.NET thuần với Microsoft.Data.SqlClient.
    /// Tối ưu Connection Pooling, Connection Timeout, chống SQL Injection 100% bằng SqlParameter và an toàn tài nguyên.
    /// </summary>
    public static class DatabaseHelper
    {
        /// <summary>
        /// Thời gian chờ thực thi lệnh SQL mặc định (giây).
        /// </summary>
        public static int DefaultCommandTimeout { get; set; } = 30;

        // Chuỗi kết nối mặc định tới CSDL Microsoft SQL Server trên máy cục bộ
        // Tự động nhận diện .\SQLEXPRESS hoặc localhost
        public static string ConnectionString { get; set; } = AutoDetectConnectionString();

        /// <summary>
        /// Chuẩn hóa chuỗi kết nối với đầy đủ tham số Connection Pooling và Timeout tối ưu.
        /// </summary>
        public static string BuildOptimizedConnectionString(string server)
        {
            return $"Server={server};Database=BatterySwappingDB;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True;Connect Timeout=15;Pooling=True;Min Pool Size=5;Max Pool Size=100;";
        }

        /// <summary>
        /// Tự động dò tìm SQL Server instance đang hoạt động (.\SQLEXPRESS hoặc localhost hoặc localdb)
        /// </summary>
        public static string AutoDetectConnectionString()
        {
            string[] candidateServers = { @".\SQLEXPRESS", "localhost", @"(localdb)\mssqllocaldb" };
            foreach (var server in candidateServers)
            {
                string testCs = $"Server={server};Database=BatterySwappingDB;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True;Connect Timeout=2;";
                try
                {
                    using var conn = new SqlConnection(testCs);
                    conn.Open();
                    // Trả về chuỗi kết nối chuẩn với Pooling & Timeout tối ưu
                    return BuildOptimizedConnectionString(server);
                }
                catch { }
            }
            return BuildOptimizedConnectionString(@".\SQLEXPRESS");
        }

        /// <summary>
        /// Tạo và mở kết nối mới tới cơ sở dữ liệu với xử lý giải phóng socket an toàn nếu mở kết nối thất bại.
        /// </summary>
        public static SqlConnection GetOpenConnection()
        {
            var connection = new SqlConnection(ConnectionString);
            try
            {
                connection.Open();
                return connection;
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Kiểm tra kết nối tới cơ sở dữ liệu có thông suốt không.
        /// </summary>
        public static bool TestConnection(out string errorMessage)
        {
            errorMessage = string.Empty;
            try
            {
                using var conn = new SqlConnection(ConnectionString);
                conn.Open();
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        private static void PrepareCommand(SqlCommand cmd, SqlParameter[]? parameters)
        {
            cmd.CommandTimeout = DefaultCommandTimeout;
            if (parameters != null && parameters.Length > 0)
            {
                foreach (var param in parameters)
                {
                    if (param != null)
                    {
                        if (param.Value == null)
                        {
                            param.Value = DBNull.Value;
                        }
                        cmd.Parameters.Add(param);
                    }
                }
            }
        }

        /// <summary>
        /// Thực thi câu lệnh INSERT, UPDATE, DELETE và trả về số dòng bị ảnh hưởng.
        /// </summary>
        public static int ExecuteNonQuery(string sql, params SqlParameter[] parameters)
        {
            using var conn = GetOpenConnection();
            using var cmd = new SqlCommand(sql, conn);
            PrepareCommand(cmd, parameters);
            return cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Thực thi câu lệnh truy vấn trả về một giá trị duy nhất (Scalar).
        /// </summary>
        public static object? ExecuteScalar(string sql, params SqlParameter[] parameters)
        {
            using var conn = GetOpenConnection();
            using var cmd = new SqlCommand(sql, conn);
            PrepareCommand(cmd, parameters);
            var val = cmd.ExecuteScalar();
            return val == DBNull.Value ? null : val;
        }

        /// <summary>
        /// Thực thi câu lệnh truy vấn SELECT và điền dữ liệu vào DataTable.
        /// </summary>
        public static DataTable ExecuteDataTable(string sql, params SqlParameter[] parameters)
        {
            using var conn = GetOpenConnection();
            using var cmd = new SqlCommand(sql, conn);
            PrepareCommand(cmd, parameters);
            using var adapter = new SqlDataAdapter(cmd);
            var dt = new DataTable();
            adapter.Fill(dt);
            return dt;
        }

        /// <summary>
        /// Thực thi Stored Procedure trả về DataTable.
        /// </summary>
        public static DataTable ExecuteStoredProcedure(string procedureName, params SqlParameter[] parameters)
        {
            using var conn = GetOpenConnection();
            using var cmd = new SqlCommand(procedureName, conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            PrepareCommand(cmd, parameters);
            using var adapter = new SqlDataAdapter(cmd);
            var dt = new DataTable();
            adapter.Fill(dt);
            return dt;
        }

        /// <summary>
        /// Thực thi Stored Procedure dạng hành động (INSERT, UPDATE, DELETE) và trả về số dòng bị ảnh hưởng.
        /// </summary>
        public static int ExecuteStoredProcedureNonQuery(string procedureName, params SqlParameter[] parameters)
        {
            using var conn = GetOpenConnection();
            using var cmd = new SqlCommand(procedureName, conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            PrepareCommand(cmd, parameters);
            return cmd.ExecuteNonQuery();
        }
    }
}
