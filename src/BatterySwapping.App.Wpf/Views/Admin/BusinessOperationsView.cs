using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using BatterySwapping.App.Services;
using BatterySwapping.App.Models;
using BatterySwapping.Contracts;
namespace BatterySwapping.App.Views.Admin;
public sealed class BusinessOperationsView:UserControl
{
 readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
 readonly ComboBox customers=new(){MinWidth=240},warehouse=new(){MinWidth=200};
 readonly TextBox username=new(),name=new(),phone=new(),email=new(),plate=new(),note=new(),fee=new(),type=new();
 readonly PasswordBox password=new();
 readonly DataGrid payments=new(){Height=260,IsReadOnly=true,AutoGenerateColumns=true};
 readonly DataGrid inventory=new(){Height=240,IsReadOnly=true,AutoGenerateColumns=true};
 readonly TextBlock issues=new(){TextWrapping=TextWrapping.Wrap};
 bool busy;
 readonly Dictionary<string,string> keys=new();
 string Key(string action,string body){string k=action+body;if(!keys.TryGetValue(k,out var id))keys[k]=id=Guid.NewGuid().ToString();return id;}
 public BusinessOperationsView()
 {
  customers.DisplayMemberPath="HoTen";warehouse.DisplayMemberPath="BatteryId";
  var root=new StackPanel{Margin=new Thickness(24)};
  root.Children.Add(new TextBlock{Text="HỒ SƠ · KHO PIN · GIÁ PHÍ · CỔNG THANH TOÁN DEMO",FontSize=20});root.Children.Add(status);
  root.Children.Add(Btn("Làm mới dữ liệu",Reload));
  var tabs=new TabControl();root.Children.Add(tabs);
  var create=Panel();Field(create,"Tên đăng nhập",username);Field(create,"Mật khẩu ban đầu (không dùng mật khẩu demo cố định)",password);Field(create,"Họ tên",name);Field(create,"Điện thoại",phone);Field(create,"Email (không bắt buộc)",email);Field(create,"Biển số",plate);
  create.Children.Add(Btn("Tạo hồ sơ — ví 0 đ, chưa có pin",async()=>{var body=username.Text+phone.Text;await BssApi.Client.Post<JsonElement>("customers/create",new CreateCustomerRequest(username.Text??"",password.Password,name.Text??"",phone.Text??"",email.Text,plate.Text??"",Key("customer",body)));password.Password="";await Reload();}));Tab(tabs,"Tạo khách hàng",create);
  var stock=Panel();stock.Children.Add(new TextBlock{Text="Chọn khách chưa nhận pin và pin kho có sẵn. Bàn giao thủ công phải được admin xác nhận.",TextWrapping=TextWrapping.Wrap});stock.Children.Add(customers);stock.Children.Add(warehouse);Field(stock,"Ghi chú bàn giao",note);stock.Children.Add(Btn("Xác nhận đã bàn giao pin đầu",async()=>{if(customers.SelectedItem is not KhachHang c||warehouse.SelectedItem is not BatteryInventoryItem pin)throw new InvalidOperationException("Chọn khách và pin kho.");await BssApi.Client.Post<JsonElement>("handovers",new HandoverRequest(c.MaKH,pin.BatteryId,note.Text??"",Key("handover",c.MaKH+pin.BatteryId+(note.Text??""))));await Reload();}));stock.Children.Add(inventory);stock.Children.Add(new TextBlock{Text="Bất nhất dữ liệu cần đối soát (không tự sửa):"});stock.Children.Add(issues);Tab(tabs,"Kho và bàn giao",stock);
  var pricing=Panel();Field(pricing,"Phí đổi pin (đ)",fee);Field(pricing,"Loại pin được hỗ trợ",type);pricing.Children.Add(Btn("Lưu chính sách cho phiên mới",async()=>{if(!decimal.TryParse(fee.Text,out var f))throw new InvalidOperationException("Phí không hợp lệ.");await BssApi.Client.Put<PricePolicy>("policy",new PricePolicy(f,type.Text??""));await Reload();}));Tab(tabs,"Giá phí",pricing);
  var gateway=Panel();gateway.Children.Add(new TextBlock{Text="CỔNG THANH TOÁN GIẢ LẬP — không chuyển tiền thật. Chỉ yêu cầu chờ xử lý được xác nhận.",TextWrapping=TextWrapping.Wrap});gateway.Children.Add(payments);
  foreach(var result in new[]{"Succeeded","Failed","Cancelled"}){string value=result;gateway.Children.Add(Btn(result=="Succeeded"?"Giả lập thanh toán thành công":result=="Failed"?"Giả lập thanh toán thất bại":"Giả lập hủy thanh toán",async()=>{if(payments.SelectedItem is not PaymentRequest p||p.Status!="Pending")throw new InvalidOperationException("Chọn yêu cầu đang chờ thanh toán.");await BssApi.Client.Post<PaymentRequest>($"payments/{p.Id}/result",new PaymentResultRequest(value));await Reload();}));}Tab(tabs,"Công cụ demo: thanh toán",gateway);
  Content=new ScrollViewer{Content=root};Loaded+=async(_,_)=>await Reload();
 }
 static StackPanel Panel()=>new(){Margin=new Thickness(0,12,0,12)};
 static void Field(StackPanel p,string label,Control input){p.Children.Add(new TextBlock{Text=label,Margin=new Thickness(0,8,0,4)});p.Children.Add(input);}
 static void Tab(TabControl tabs,string title,StackPanel panel)=>tabs.Items.Add(new TabItem{Header=title,Content=panel});
 Button Btn(string title,Func<Task> action){var b=new Button{Content=title,Margin=new Thickness(0,8,8,8)};b.Style=(Style)FindResource("PrimaryButtonStyle"); b.Click+=async(_,_)=>{if(busy)return;busy=true;b.IsEnabled=false;try{await action();status.Text="Đã cập nhật từ backend.";}catch(Exception e){status.Text=e.Message;}finally{busy=false;b.IsEnabled=true;}};return b;}
 async Task Reload(){try{var people=await BssApi.Client.Get<List<KhachHang>>("customers");customers.ItemsSource=people.Where(c=>string.IsNullOrEmpty(c.MaPinDangMuon)).ToList();var pins=await BssApi.Client.Get<List<BatteryInventoryItem>>("inventory");inventory.ItemsSource=pins;warehouse.ItemsSource=pins.Where(p=>p.Location=="Warehouse"&&!p.Reserved).ToList();payments.ItemsSource=await BssApi.Client.Get<List<PaymentRequest>>("payments");var policy=await BssApi.Client.Get<PricePolicy>("policy");fee.Text=policy.Fee.ToString("0");type.Text=policy.SupportedBatteryType;issues.Text=(await BssApi.Client.Get<JsonElement>("consistency")).ToString();}catch(Exception e){status.Text=e.Message;}}
}
