using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BatterySwapping.Contracts;

namespace BatterySwapping.Client;
public sealed class ApiClient : IDisposable
{
    readonly HttpClient http;
    public UserInfo? User { get; private set; }
    public ApiClient(string? baseUrl=null) => http=new HttpClient {BaseAddress=new Uri(baseUrl??Environment.GetEnvironmentVariable("BSS_API_URL")??"http://127.0.0.1:5080"),Timeout=TimeSpan.FromSeconds(12)};
    public async Task<T> Get<T>(string path,CancellationToken ct=default) => await Read<T>(await http.GetAsync("/api/v1/"+path,ct),ct);
    public async Task<T> Post<T>(string path,object value,CancellationToken ct=default) => await Read<T>(await http.PostAsJsonAsync("/api/v1/"+path,value,ct),ct);
    public async Task Login(string username,string password,CancellationToken ct=default)
    {
        var r=await Post<LoginResponse>("auth/login",new LoginRequest(username,password),ct);
        User=r.User;http.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",r.Token);
    }
    public async Task KioskLogin(string identifier, CancellationToken ct = default)
    {
        var r = await Post<LoginResponse>("auth/kiosk", new KioskAuthRequest(identifier), ct);
        User = r.User;
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", r.Token);
    }
    public async Task Logout()
    {
        try {using var r=await http.PostAsJsonAsync("/api/v1/auth/logout",new{});}
        finally {User=null;http.DefaultRequestHeaders.Authorization=null;}
    }
    static async Task<T> Read<T>(HttpResponseMessage response,CancellationToken ct)
    {
        using(response)
        {
            if(!response.IsSuccessStatusCode)
            {
                ErrorResponse? error=null;
                try {error=await response.Content.ReadFromJsonAsync<ErrorResponse>(ct);}catch(JsonException){}
                throw new InvalidOperationException(error?.Error??$"Server trả lỗi {(int)response.StatusCode}.");
            }
            return await response.Content.ReadFromJsonAsync<T>(ct)??throw new InvalidOperationException("Server không trả dữ liệu.");
        }
    }
    public void Dispose()=>http.Dispose();
}
