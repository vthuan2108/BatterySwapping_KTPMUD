using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Controls;
using Avalonia.Threading;
using BatterySwapping.App.Avalonia.Services;
using BatterySwapping.App.Avalonia.Models;
using BatterySwapping.Contracts;
namespace BatterySwapping.App.Avalonia.Views.Kiosk;
public sealed class SwapWorkflowView:UserControl
{
 readonly ComboBox stations=new(){MinWidth=240};readonly TextBox identifier=new();
 readonly TextBlock summary=new(){TextWrapping=TextWrapping.Wrap},status=new(){TextWrapping=TextWrapping.Wrap};
 readonly DataGrid sessions=new(){Height=280,IsReadOnly=true,AutoGenerateColumns=true};
 readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(2)};
 bool loading,acting;string key=Guid.NewGuid().ToString();
 bool Customer=>AuthService.CurrentUser?.VaiTro=="KhachHang";
 public SwapWorkflowView(){var panel=new StackPanel{Margin=new Thickness(24)};panel.Children.Add(new TextBlock{Text=Customer?"ĐỔI PIN VÀ PHIÊN CỦA TÔI":"PHIÊN ĐỔI PIN CẦN HỖ TRỢ",FontSize=22});panel.Children.Add(summary);panel.Children.Add(status);
 if(Customer){panel.Children.Add(new TextBlock{Text="Chọn trạm"});stations.ItemTemplate=new global::Avalonia.Controls.Templates.FuncDataTemplate<TramPin>((s,_)=>new TextBlock{Text=s?.TenTram}); panel.Children.Add(stations);panel.Children.Add(new TextBlock{Text="QR / RFID / số điện thoại (tùy chọn, chỉ đối chiếu tài khoản đang đăng nhập)"});panel.Children.Add(identifier);panel.Children.Add(Btn("Xác nhận giữ chỗ và bắt đầu đổi pin",async()=>{if(stations.SelectedItem is not TramPin s)throw new InvalidOperationException("Chọn trạm.");if(!string.IsNullOrWhiteSpace(identifier.Text))await new SwapService().ValidateCustomerWallet(identifier.Text);await BssApi.Client.Post<SwapSession>("swaps",new StartSwap(s.MaTram,key));key=Guid.NewGuid().ToString();await Reload();}));}
 panel.Children.Add(sessions);panel.Children.Add(Btn("Mở tủ Web3D — thao tác thiết bị",()=>{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(BssApi.Url+"/?station="+Uri.EscapeDataString((sessions.SelectedItem as SwapSession)?.StationId??(stations.SelectedItem as TramPin)?.MaTram??"TRAM-BK01")){UseShellExecute=true});return Task.CompletedTask;}));
 panel.Children.Add(Btn("Yêu cầu hủy phiên đang chọn",()=>Act("cancel")));
 if(!Customer){panel.Children.Add(Btn("Đối soát cấp pin — phiên đã thu phí",()=>Act("resume")));panel.Children.Add(Btn("Trả lại pin cũ — phiên chưa thu phí",()=>Act("return-old")));}
 panel.Children.Add(Btn("Làm mới",Reload));Content=new ScrollViewer{Content=panel};timer.Tick+=async(_,_)=>await Reload();AttachedToVisualTree+=async(_,_)=>{await Reload();timer.Start();};DetachedFromVisualTree+=(_,_)=>timer.Stop();
 }
 async Task Act(string action){if(sessions.SelectedItem is not SwapSession s)throw new InvalidOperationException("Chọn phiên.");await BssApi.Client.Post<SwapSession>($"swaps/{s.Id}/{action}",new{});await Reload();}
 Button Btn(string title,Func<Task> action){var b=new Button{Content=title,Margin=new Thickness(0,10,8,0)};b.Classes.Add("primary"); b.Click+=async(_,_)=>{if(acting)return;acting=true;b.IsEnabled=false;try{await action();}catch(Exception e){status.Text=e.Message;}finally{acting=false;b.IsEnabled=true;}};return b;}
 async Task Reload(){if(loading)return;loading=true;try{if(Customer&&stations.ItemsSource==null)stations.ItemsSource=await new StationService().GetAllStations();var selected=(sessions.SelectedItem as SwapSession)?.Id;var rows=await BssApi.Client.Get<List<SwapSession>>("swaps");sessions.ItemsSource=Customer?rows:rows.Where(s=>s.State=="NeedsAssistance").ToList();sessions.SelectedItem=rows.Find(s=>s.Id==selected);if(Customer){var w=await BssApi.Client.Get<KhachHang>("wallet");var p=await BssApi.Client.Get<PricePolicy>("policy");summary.Text=$"Tài xế: {w.HoTen} • Pin đang giữ: {w.MaPinDangMuon??"Chưa có — liên hệ admin bàn giao lần đầu"}\nSố dư: {w.SoDuVi:N0} đ • Phí phiên mới: {p.Fee:N0} đ. Phí được giữ chỗ trước, chỉ trừ khi nhận pin cũ hợp lệ.";}status.Text="Chỉ thao tác cắm/rút trên Web3D; trạng thái được xác nhận bởi thiết bị. Không tự hoàn tất giao dịch.";}catch(Exception e){status.Text="Chưa kết nối: "+e.Message;}finally{loading=false;}}
}
