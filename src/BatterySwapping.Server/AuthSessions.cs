using System.Collections.Concurrent;
using System.Security.Cryptography;
using BatterySwapping.Contracts;

namespace BatterySwapping.Server;
public sealed class AuthSessions
{
    readonly ConcurrentDictionary<string,(UserInfo User,DateTimeOffset Until)> sessions=new();
    public string Create(UserInfo user)
    {
        foreach(var e in sessions.Where(p=>p.Value.Until<DateTimeOffset.UtcNow)) sessions.TryRemove(e.Key,out _);
        string token=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        sessions[Key(token)]=(user,DateTimeOffset.UtcNow.AddHours(8));return token;
    }
    static string Key(string token)=>Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
    public UserInfo? Get(string token)=> sessions.TryGetValue(Key(token),out var s) && s.Until>DateTimeOffset.UtcNow ? s.User : null;
    public void Remove(string token)=>sessions.TryRemove(Key(token),out _);
}
public record Roles(params string[] Names);
