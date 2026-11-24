using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using BatterySwapping.App.Services;
using BatterySwapping.Contracts;
namespace BatterySwapping.App.Views.Admin;
public partial class SwapSessionsView:UserControl
{
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(2)};List<SessionRow> rows=new();bool loading,acting;
    bool Staff=>NavigationPolicy.For(AuthService.CurrentUser?.VaiTro).Staff;
    public SwapSessionsView(){InitializeComponent();if(AuthService.CurrentUser?.VaiTro=="KyThuatVien")filter.SelectedIndex=2;resume.Visibility=Staff?Visibility.Visible:Visibility.Collapsed;Loaded+=async(_,_)=>{await Reload();if(IsLoaded)timer.Start();};Unloaded+=(_,_)=>timer.Stop();timer.Tick+=async(_,_)=>await Reload();}
    async Task Reload(){if(loading||acting)return;loading=true;try{var id=(sessions.SelectedItem as SessionRow)?.Id;rows=(await BssApi.Client.Get<List<SwapSession>>("swaps")).Select(s=>new SessionRow(s)).OrderByDescending(s=>s.Value.CreatedAt).ToList();Apply(id);txtStatus.Text=$"{rows.Count} phiên • {rows.Count(s=>s.Value.State=="NeedsAssistance")} cần hỗ trợ • Cập nhật {DateTime.Now:HH:mm:ss}";}catch(Exception e){txtStatus.Text="Không tải được phiên: "+e.Message;}finally{loading=false;}}
    void Apply(string? selected=null){if(sessions==null)return;var filtered=rows.Where(s=>filter.SelectedIndex switch{1=>s.Active&&s.Value.State!="NeedsAssistance",2=>s.Value.State=="NeedsAssistance",3=>!s.Active,_=>true}).ToList();sessions.ItemsSource=filtered;sessions.SelectedItem=filtered.Find(s=>s.Id==selected);UpdateActions();}
    void FilterChanged(object sender,SelectionChangedEventArgs e)=>Apply((sessions?.SelectedItem as SessionRow)?.Id);
    void SelectionChanged(object sender,SelectionChangedEventArgs e)=>UpdateActions();
    void UpdateActions(){if(resume==null||cancel==null||detail==null)return;var row=sessions.SelectedItem as SessionRow;resume.IsEnabled=!acting&&Staff&&row?.Value is {State:"NeedsAssistance",Charged:true};cancel.IsEnabled=!acting&&row?.Active==true;if(row==null){detail.Text="Chọn một phiên để xem chi tiết.";return;}var s=row.Value;detail.Text=$"Phiên: {s.Id}\n{s.OldBatteryId} → ngăn {s.ReturnSlot}; nhận {s.NewBatteryId} tại ngăn {s.ReceiveSlot}.\n{row.PaymentLabel} • Phí {s.Fee:N0} đ\n{s.Note}\nHủy sau khi nhận pin sẽ giữ phiên để đối soát; không tự hoàn tiền hoặc thu thêm phí.";}
    async Task Act(string action){if(acting||sessions.SelectedItem is not SessionRow row)return;if(MessageBox.Show(action=="resume"?"Đối soát lệnh cấp pin của phiên đã thu phí? Thao tác không thu thêm phí.":"Hủy nếu chưa giao pin; nếu đã giao pin, chuyển sang cần hỗ trợ?","Xác nhận phiên",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;acting=true;UpdateActions();try{var s=await BssApi.Client.Post<SwapSession>($"swaps/{row.Id}/{action}",new{});txtStatus.Text=SessionRow.Label(s.State);detail.Text=s.Note??txtStatus.Text;}catch(Exception ex){txtStatus.Text="Không hoàn tất: "+ex.Message;}finally{acting=false;await Reload();}}
    async void ResumeClick(object sender,RoutedEventArgs e)=>await Act("resume");
    async void CancelClick(object sender,RoutedEventArgs e)=>await Act("cancel");
    async void RefreshClick(object sender,RoutedEventArgs e)=>await Reload();
}
public record SessionRow(SwapSession Value)
{
    public string Id=>Value.Id;public string StationId=>Value.StationId;public int CustomerId=>Value.CustomerId;public string OldBatteryId=>Value.OldBatteryId;public string NewBatteryId=>Value.NewBatteryId;public DateTimeOffset CreatedAt=>Value.CreatedAt;
    public bool Active=>Value.State is not("Completed" or "Cancelled" or "Expired");
    public string StateLabel=>Label(Value.State);public string PaymentLabel=>Value.Charged?"Đã trừ phí":Active&&!Value.State.StartsWith("Closing")?"Đang giữ chỗ":"Chưa thu phí";
    public static string Label(string state)=>state switch{"AwaitingOldBattery"=>"Chờ nhận pin cũ","Dispensing"=>"Chờ khách lấy pin","NeedsAssistance"=>"Cần hỗ trợ","ClosingCancelled"=>"Đóng ngăn để hủy","ClosingExpired"=>"Đóng ngăn / hết hạn","Completed"=>"Hoàn tất","Cancelled"=>"Đã hủy","Expired"=>"Hết hạn",_=>state};
}

