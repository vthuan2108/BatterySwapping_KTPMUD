using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using BatterySwapping.App.Services;
using BatterySwapping.Contracts;
namespace BatterySwapping.App.Views.Admin;
public sealed class MaintenanceHistoryWindow:Window
{
 public MaintenanceHistoryWindow(){Title="Phiếu bảo trì và xác nhận thiết bị";Width=1000;Height=560;var panel=new StackPanel{Margin=new Thickness(20)};var grid=new DataGrid{Height=400,IsReadOnly=true,AutoGenerateColumns=true};var status=new TextBlock();var refresh=new Button{Content="Làm mới phiếu bảo trì"};panel.Children.Add(status);panel.Children.Add(refresh);var retry=new Button{Content="Gửi lại phiếu đã thất bại",IsEnabled=false};panel.Children.Add(retry);panel.Children.Add(grid);Content=panel;
 async System.Threading.Tasks.Task Load(){try{grid.ItemsSource=await BssApi.Client.Get<List<MaintenanceOrder>>("maintenance");status.Text="Theo dõi mã phiếu, mã lệnh, người thực hiện và kết quả. Chưa xác nhận không phải hoàn thành.";}catch(Exception e){status.Text=e.Message;}}
 bool retrying=false;
 grid.SelectionChanged+=(_,_)=>retry.IsEnabled=!retrying&&grid.SelectedItem is MaintenanceOrder{Status:"Failed"};
 retry.Click+=async(_,_)=>{
  if(retrying||grid.SelectedItem is not MaintenanceOrder{Status:"Failed"} order)return;
  retrying=true;retry.IsEnabled=false;
  try{
   var pending=await BssApi.Client.Post<MaintenanceOrder>($"maintenance/{order.Id}/retry",new{});
   await Load();
   status.Text=$"Phiếu {pending.Id} / lệnh {pending.CommandId}: đang chờ thiết bị xác nhận. Không tự gửi lại khi chưa rõ kết quả.";
  }catch(Exception e){status.Text=e.Message+" Làm mới để đối soát; chỉ phiếu thất bại rõ ràng mới được gửi lại.";}
  finally{retrying=false;retry.IsEnabled=grid.SelectedItem is MaintenanceOrder{Status:"Failed"};}
 };
 refresh.Click+=async(_,_)=>await Load();Loaded+=async(_,_)=>await Load();
 }
}
