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
            _reportService = new ReportService();
            Loaded += (s, e) => LoadData();
        }

        public void LoadData()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(LoadData);
                return;
            }
            if (dgSwapHistory == null || dgPaymentHistory == null) return;
            try
            {
                dgSwapHistory.ItemsSource = _reportService.GetSwapHistory();
                dgPaymentHistory.ItemsSource = _reportService.GetTopUpHistory();
            }
            catch
            {
                // Safe ignore
            }
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            LoadData();
        }
    }
}
