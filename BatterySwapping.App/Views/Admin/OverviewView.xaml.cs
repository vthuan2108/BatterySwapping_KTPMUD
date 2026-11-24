using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Text.Json;
using BatterySwapping.App.Services;
using BatterySwapping.App.Models;
namespace BatterySwapping.App.Views.Admin;
public partial class OverviewView:UserControl
{
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(2)};bool loading;readonly bool wallet;
    public event Action<string>? StationSelected;
    public OverviewView(bool customerWallet=false){InitializeComponent();wallet=customerWallet;if(wallet){heading.Text="VÍ VÀ THÔNG TIN CỦA TÔI";stations.Visibility=Visibility.Collapsed;}Loaded+=async(_,_)=>{await Reload();if(IsLoaded)timer.Start();};Unloaded+=(_,_)=>timer.Stop();timer.Tick+=async(_,_)=>await Reload();}
    async Task Reload(){if(loading)return;loading=true;try{if(wallet){var w=await BssApi.Client.Get<JsonElement>("wallet");decimal balance=w.GetProperty("SoDuVi").GetDecimal(),held=w.GetProperty("ReservedBalance").GetDecimal();summary.Text=$"Số dư: {balance:N0} đ\nĐang giữ chỗ: {held:N0} đ\nCó thể sử dụng: {balance-held:N0} đ";status.Text=$"{w.GetProperty("HoTen").GetString()} • Pin đang mượn: {w.GetProperty("MaPinDangMuon").ToString()}";}else{var s=await new ReportService().GetSummaryStats();summary.Text=$"Doanh thu hôm nay: {s.DoanhThuHomNay:N0} đ    |    Lượt đổi: {s.TongLuotDoiPin}\nPin khả dụng: {s.SoPinKhaDung}/{s.TongSoPin}    |    Cảnh báo chưa xử lý: {s.SoCanhBaoChuaXuLy}";stations.ItemsSource=await new StationService().GetAllStations();status.Text=$"Cập nhật {DateTime.Now:HH:mm:ss}. Bấm đúp trạm để mở vận hành.";}}catch(Exception ex){status.Text="Không đọc được dữ liệu: "+ex.Message;}finally{loading=false;}}
    void OpenStation(object sender,MouseButtonEventArgs e){if(stations.SelectedItem is TramPin station)StationSelected?.Invoke(station.MaTram);}
}
