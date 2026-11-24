using System.Windows;
using System.Windows.Controls;
using BatterySwapping.App.Models;
using BatterySwapping.App.Services;
namespace BatterySwapping.App.Views.Kiosk;
public partial class KioskWelcomeView:UserControl
{
    public event Action<KhachHang>? OnCustomerValidated;
    bool checking;
    public KioskWelcomeView(){InitializeComponent();}
    async void BtnStartSwap_Click(object sender,RoutedEventArgs e)
    {
        if(checking)return;checking=true;lblKioskError.Visibility=Visibility.Collapsed;
        try{var customer=await new SwapService().ValidateCustomerWallet(txtPhoneOrQr.Text.Trim());OnCustomerValidated?.Invoke(customer);}
        catch(Exception ex){lblKioskError.Text=ex.Message;lblKioskError.Visibility=Visibility.Visible;}
        finally{checking=false;}
    }
    void BtnQuickDriver_Click(object sender,RoutedEventArgs e){if(sender is Button b&&b.Tag is string phone){txtPhoneOrQr.Text=phone;BtnStartSwap_Click(sender,e);}}
}
