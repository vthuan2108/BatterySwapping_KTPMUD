using System.Windows;
using BatterySwapping.App.Helpers;
using BatterySwapping.App.Services;

namespace BatterySwapping.App.Views
{
    /// <summary>
    /// Interaction logic for LoginWindow.xaml
    /// </summary>
    public partial class LoginWindow : Window
    {
        private readonly AuthService _authService;

        public LoginWindow()
        {
            InitializeComponent();
            _authService = new AuthService();
            txtConnString.Text = DatabaseHelper.ConnectionString;
            txtPassword.Password = "123456";
        }

        private void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            if (lblError != null) lblError.Visibility = Visibility.Collapsed;
            if (txtConnString != null && !string.IsNullOrWhiteSpace(txtConnString.Text))
            {
                DatabaseHelper.ConnectionString = txtConnString.Text.Trim();
            }

            string username = txtUsername?.Text?.Trim() ?? string.Empty;
            string password = txtPassword?.Password ?? string.Empty;

            if (_authService.Login(username, password, out string errorMessage))
            {
                var mainWindow = new MainWindow();
                mainWindow.Show();
                this.Close();
            }
            else
            {
                if (lblError != null)
                {
                    lblError.Text = errorMessage;
                    lblError.Visibility = Visibility.Visible;
                }
            }
        }

        private void BtnKioskDirect_Click(object sender, RoutedEventArgs e)
        {
            if (txtConnString != null && !string.IsNullOrWhiteSpace(txtConnString.Text))
            {
                DatabaseHelper.ConnectionString = txtConnString.Text.Trim();
            }
            _authService.Logout();
            var mainWindow = new MainWindow(openInKioskMode: true);
            mainWindow.Show();
            this.Close();
        }

        private void BtnTestConnection_Click(object sender, RoutedEventArgs e)
        {
            if (txtConnString != null && !string.IsNullOrWhiteSpace(txtConnString.Text))
            {
                DatabaseHelper.ConnectionString = txtConnString.Text.Trim();
            }
            if (DatabaseHelper.TestConnection(out string error))
            {
                MessageBox.Show("Kết nối tới SQL Server thành công!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show($"Kết nối thất bại!\nLỗi: {error}\n\nHướng dẫn: Hãy đảm bảo bạn đã chạy file script SQL tạo cơ sở dữ liệu BatterySwappingDB trong SQL Server.", 
                                "Lỗi kết nối", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
