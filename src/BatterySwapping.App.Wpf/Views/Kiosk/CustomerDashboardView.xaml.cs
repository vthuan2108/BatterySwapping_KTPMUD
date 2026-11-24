using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using BatterySwapping.App.Models;
using BatterySwapping.App.Services;
using BatterySwapping.Contracts;
namespace BatterySwapping.App.Views.Kiosk;
public partial class CustomerDashboardView:UserControl
{
    public event Action? StartSwap;
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(5)};bool loading,paying;string? requestKey;decimal? requestAmount;
    public CustomerDashboardView(){InitializeComponent();Loaded+=async(_,_)=>{await Reload();if(IsLoaded)timer.Start();};Unloaded+=(_,_)=>timer.Stop();timer.Tick+=async(_,_)=>await Reload();}
    async Task Reload(){if(loading)return;loading=true;try{
        var w=await BssApi.Client.Get<JsonElement>("wallet");string Field(string n)=>w.TryGetProperty(n,out var v)&&v.ValueKind!=JsonValueKind.Null?v.ToString():"Chưa có";
        decimal total=w.GetProperty("SoDuVi").GetDecimal(),held=w.GetProperty("ReservedBalance").GetDecimal();
        profile.Text=$"Họ tên: {Field("HoTen")}\nĐiện thoại: {Field("SoDienThoai")}\nEmail: {Field("Email")}\nBiển số xe: {Field("BienSoXe")}\nPin đang mượn: {Field("MaPinDangMuon")}";
        balance.Text=$"{total:N0} đ";available.Text=$"Đang giữ chỗ: {held:N0} đ\nCó thể sử dụng: {total-held:N0} đ";
        swapHistory.ItemsSource=await BssApi.Client.Get<List<GiaoDichDoiPin>>("me/history/swaps");topupHistory.ItemsSource=await BssApi.Client.Get<List<GiaoDichVi>>("me/history/topups");status.Text=$"Thông tin của riêng tài khoản đăng nhập • Cập nhật {DateTime.Now:HH:mm:ss}";
    }catch(Exception e){status.Text="Chưa tải được thông tin: "+e.Message;}finally{loading=false;}}
    void QuickAmount(object sender,RoutedEventArgs e){if(!paying&&sender is Button b)amount.Text=b.Tag?.ToString();}
    async void TopupClick(object sender,RoutedEventArgs e){if(paying)return;if(!decimal.TryParse(amount.Text,out var value)||value<=0||value>10000000||decimal.Truncate(value)!=value){status.Text="Nhập số tiền nguyên từ 1 đến 10.000.000 đ.";return;}
        if(MessageBox.Show($"Nạp {value:N0} đ vào ví demo của bạn? Không chuyển tiền thật.","Xác nhận nạp ví demo",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
        if(requestAmount!=value){requestKey=null;requestAmount=value;}requestKey??=Guid.NewGuid().ToString();paying=true;topup.IsEnabled=false;amount.IsEnabled=false;
        try{await BssApi.Client.Post<JsonElement>("wallet/self-topup",new SelfTopupRequest(value,requestKey));requestKey=null;status.Text="Đã nạp ví demo.";await Reload();}catch(Exception ex){status.Text="Chưa xác nhận nạp ví: "+ex.Message+" Bấm lại cùng số tiền để kiểm tra; không nạp lặp.";}finally{paying=false;topup.IsEnabled=true;amount.IsEnabled=true;}}
    void SwapClick(object sender,RoutedEventArgs e)=>StartSwap?.Invoke();
}
