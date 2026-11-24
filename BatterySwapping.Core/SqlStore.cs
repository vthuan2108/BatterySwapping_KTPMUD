using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace BatterySwapping.Core;
public sealed class SqlStore(string connectionString)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public T Read<T>(Func<Db, T> action)
    {
        using var conn = new SqlConnection(connectionString); conn.Open();
        return action(new Db(conn, null));
    }
    public T Write<T>(Func<Db, T> action)
    {
        using var conn = new SqlConnection(connectionString); conn.Open();
        using var tx = conn.BeginTransaction();
        var db = new Db(conn, tx);
        // One lock order across wallet, inventory, telemetry and command outbox.
        db.Exec("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource='BssRuntime',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000; IF @r<0 THROW 50001,'System busy',1;");
        try { var result = action(db); tx.Commit(); return result; }
        catch { tx.Rollback(); throw; }
    }
    public void Migrate(string script) => Read(db => { db.Exec(script); return true; });
}
public sealed class Db(SqlConnection connection, SqlTransaction? transaction)
{
    public SqlCommand Command(string sql, params (string, object?)[] args)
    {
        var cmd = new SqlCommand(sql, connection, transaction) { CommandTimeout = 15 };
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }
    public int Exec(string sql, params (string, object?)[] args) { using var c = Command(sql,args); return c.ExecuteNonQuery(); }
    public object? Scalar(string sql, params (string, object?)[] args) { using var c=Command(sql,args); var o=c.ExecuteScalar(); return o == DBNull.Value ? null : o; }
    public List<Dictionary<string,object?>> Rows(string sql, params (string, object?)[] args)
    {
        using var c=Command(sql,args); using var r=c.ExecuteReader(); var rows=new List<Dictionary<string,object?>>();
        while(r.Read()) { var row=new Dictionary<string,object?>(StringComparer.OrdinalIgnoreCase); for(int i=0;i<r.FieldCount;i++) row[r.GetName(i)]=r.IsDBNull(i)?null:r.GetValue(i); rows.Add(row); }
        return rows;
    }
    public T? Payload<T>(string table, string id) where T:class => Scalar($"SELECT Payload FROM dbo.{table} WHERE Id=@id",("@id",id)) is string s ? JsonSerializer.Deserialize<T>(s,SqlStore.Json) : null;
}
public class DomainException(string message, int status=409) : Exception(message) { public int Status { get; } = status; }
