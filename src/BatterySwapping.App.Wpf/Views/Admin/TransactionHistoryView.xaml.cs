using System.Windows;
using System.Windows.Controls;
using BatterySwapping.App.Services;

namespace BatterySwapping.App.Views.Admin
{
    /// <summary>
    /// Interaction logic for TransactionHistoryView.xaml
    /// </summary>
    public partial class TransactionHistoryView : UserControl
    {
        private readonly ReportService _reportService;

        public TransactionHistoryView()
        {
            InitializeComponent();
            topupsTab.Visibility=AuthService.CurrentUser?.VaiTro=="Admin"?Visibility.Visible:Visibility.Collapsed;
            _reportService = new ReportService();
            Loaded += (s, e) => LoadData();
        }

        public async void LoadData()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(LoadData);
                return;
            }
            if (dgSwapHistory == null || dgPaymentHistory == null) return;
            try
            {
                dgSwapHistory.ItemsSource = await _reportService.GetSwapHistory();
                if(AuthService.CurrentUser?.VaiTro=="Admin")dgPaymentHistory.ItemsSource = await _reportService.GetTopUpHistory();
                else dgPaymentHistory.Visibility=Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                BssApi.ReportError(ex);
                // Safe ignore
            }
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            LoadData();
        }
    }
}
