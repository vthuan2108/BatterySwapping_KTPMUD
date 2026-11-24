using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using BatterySwapping.App.Models;
using BatterySwapping.App.Services;
using BatterySwapping.Contracts;
namespace BatterySwapping.App.Views.Kiosk;

public partial class KioskSwappingView:UserControl
{
    readonly KhachHang customer;string station;readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(2)};
    readonly string key=Guid.NewGuid().ToString("N");SwapSession? session;bool reading,finished;
    public event Action<SwapResult>? OnSwapCompleted;public event Action? OnCancelled;
    public KioskSwappingView(KhachHang customer,string stationId="TRAM-BK01")
    {
        InitializeComponent();this.customer=customer;station=stationId;
        lblCustomerName.Text=customer.HoTen;lblPlate.Text=customer.BienSoXe;lblBalance.Text=customer.SoDuFormatted;
        Loaded+=Start;Unloaded+=(_,_)=>timer.Stop();timer.Tick+=async(_,_)=>await Refresh();
    }
    async void Start(object sender,RoutedEventArgs e)
    {
        try{var active=(await BssApi.Client.Get<List<SwapSession>>("swaps")).FirstOrDefault(s=>s.State is not("Completed" or "Cancelled" or "Expired"));
            session=active??await BssApi.Client.Post<SwapSession>("swaps",new StartSwap(station,key));station=session.StationId;await Refresh();timer.Start();}
        catch(Exception ex){BssApi.Error(ex);OnCancelled?.Invoke();}
    }
    async Task Refresh()
    {
        if(reading||session==null||finished)return;reading=true;
        try
        {
            session=await BssApi.Client.Get<SwapSession>($"swaps/{session.Id}");
            var slots=await new StationService().GetCabinetSlots(station);icKioskCabinetSlots.ItemsSource=slots;
            int current=session.State=="AwaitingOldBattery"?2:session.State=="Completed"?6:session.Charged?5:3;
            Border[] steps={step1,step2,step3,step4,step5,step6};
            for(int i=0;i<steps.Length;i++)steps[i].BorderBrush=i<current?Brushes.MediumSeaGreen:Brushes.SlateGray;
            txtStepIcon.Text=session.State=="NeedsAssistance"?"⚠":"🔋";
            txtStepTitle.Text=session.State switch{"AwaitingOldBattery"=>$"CHỜ CẮM PIN CŨ — NGĂN #{session.ReturnSlot}","Dispensing"=>$"CHỜ NHẬN PIN MỚI — NGĂN #{session.ReceiveSlot}","NeedsAssistance"=>"CẦN KỸ THUẬT VIÊN HỖ TRỢ","ClosingCancelled"=>"ĐANG ĐÓNG NGĂN ĐỂ HỦY","ClosingExpired"=>"HẾT THỜI GIAN — ĐANG ĐÓNG NGĂN",_=>session.State};
            txtStepDescription.Text=$"Trả {session.OldBatteryId} vào ngăn #{session.ReturnSlot}; nhận {session.NewBatteryId} tại ngăn #{session.ReceiveSlot}.\n{(session.Charged?"Phí đã được chốt một lần.":"Phí và pin đã được giữ chỗ.")} Thao tác cắm/rút pin trên tủ Web3D.\n{session.Note}";
            boxDetails.Visibility=Visibility.Visible;lblDetailHeader.Text=$"PHIÊN {session.Id[..8]}";lblDetail1.Text=$"Pin trả: {session.OldBatteryId}";lblDetail2.Text=$"Pin nhận: {session.NewBatteryId}";lblDetail3.Text=$"Phí: {session.Fee:N0} đ";lblDetail4.Text=$"Trạng thái: {session.State}";
            btnAction.Content="MỞ TỦ MÔ PHỎNG 3D";
            btnSimulateOld.Visibility = (session.State == "AwaitingOldBattery") ? Visibility.Visible : Visibility.Collapsed;
            btnSimulateNew.Visibility = (session.State == "Dispensing") ? Visibility.Visible : Visibility.Collapsed;
            if(session.State is "Cancelled" or "Expired"){finished=true;timer.Stop();OnCancelled?.Invoke();}
            if(session.State=="Completed")
            {
                var wallet=await BssApi.Client.Get<KhachHang>("wallet");finished=true;timer.Stop();
                OnSwapCompleted?.Invoke(new SwapResult{Success=true,TransactionId=session.TransactionId??0,OldBatteryId=session.OldBatteryId,NewBatteryId=session.NewBatteryId,SlotOldBattery=session.ReturnSlot,SlotNewBattery=session.ReceiveSlot,Fee=session.Fee,RemainingBalance=wallet.SoDuVi,OldBatterySoC=slots.Find(n=>n.SoThuTu==session.ReturnSlot)?.SoC??0});
            }
        }
        catch(Exception ex){txtStepTitle.Text="CHƯA XÁC NHẬN ĐƯỢC TRẠNG THÁI";txtStepDescription.Text=ex.Message+"\nPhiên được lưu tại server; không tự khởi tạo lại giao dịch.";}
        finally{reading=false;}
    }
    async void BtnSimulateOld_Click(object sender, RoutedEventArgs e)
    {
        if(session==null||reading)return;
        try
        {
            btnSimulateOld.IsEnabled=false;
            session=await BssApi.Client.Post<SwapSession>($"swaps/{session.Id}/events",new SwapEventRequest("OLD_INSERTED"));
            await Refresh();
        }
        catch(Exception ex){BssApi.Error(ex);}
        finally{btnSimulateOld.IsEnabled=true;}
    }
    async void BtnSimulateNew_Click(object sender, RoutedEventArgs e)
    {
        if(session==null||reading)return;
        try
        {
            btnSimulateNew.IsEnabled=false;
            session=await BssApi.Client.Post<SwapSession>($"swaps/{session.Id}/events",new SwapEventRequest("NEW_TAKEN"));
            await Refresh();
        }
        catch(Exception ex){BssApi.Error(ex);}
        finally{btnSimulateNew.IsEnabled=true;}
    }
    void BtnAction_Click(object sender,RoutedEventArgs e)=>Process.Start(new ProcessStartInfo(BssApi.Url+"/?station="+Uri.EscapeDataString(station)){UseShellExecute=true});
    async void BtnCancel_Click(object sender,RoutedEventArgs e)
    {
        if(session==null)return;
        try{session=await BssApi.Client.Post<SwapSession>($"swaps/{session.Id}/cancel",new{});await Refresh();}catch(Exception ex){BssApi.Error(ex);}
    }
}
