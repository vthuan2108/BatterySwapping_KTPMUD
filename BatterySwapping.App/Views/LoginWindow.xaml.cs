using System.Windows;

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
            txtConnString.Text = BssApi.Url;
            txtPassword.Password = "123456";
        }

        private async void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            var sourceButton=sender as System.Windows.Controls.Button;
            if(sourceButton!=null)sourceButton.IsEnabled=false;
            try {

            if (lblError != null) lblError.Visibility = Visibility.Collapsed;
            if (txtConnString != null && !string.IsNullOrWhiteSpace(txtConnString.Text))
            {
                BssApi.Configure(txtConnString.Text.Trim());
            }

            string username = txtUsername?.Text?.Trim() ?? string.Empty;
            string password = txtPassword?.Password ?? string.Empty;

            var outcome = await _authService.Login(username, password);
            if (outcome.Success)
            {
                var mainWindow = new MainWindow();
                mainWindow.Show();
                this.Close();
            }
            else
            {
                if (lblError != null)
                {
                    lblError.Text = outcome.Error;
                    lblError.Visibility = Visibility.Visible;
                }
            }

            } catch(Exception ex){BssApi.Error(ex);}
            finally {if(sourceButton!=null)sourceButton.IsEnabled=true;}
        }

        private async void BtnKioskDirect_Click(object sender, RoutedEventArgs e)
        {
            var sourceButton=sender as System.Windows.Controls.Button;
            if(sourceButton!=null)sourceButton.IsEnabled=false;
            try {

            if (txtConnString != null && !string.IsNullOrWhiteSpace(txtConnString.Text))
            {
                BssApi.Configure(txtConnString.Text.Trim());
            }
            await _authService.Logout();
            var mainWindow = new MainWindow(openInKioskMode: true);
            mainWindow.Show();
            this.Close();

            } catch(Exception ex){BssApi.Error(ex);}
            finally {if(sourceButton!=null)sourceButton.IsEnabled=true;}
        }

        private async void BtnTestConnection_Click(object sender, RoutedEventArgs e)
        {
            var sourceButton=sender as System.Windows.Controls.Button;
            if(sourceButton!=null)sourceButton.IsEnabled=false;
            try {

            if (txtConnString != null && !string.IsNullOrWhiteSpace(txtConnString.Text))
            {
                BssApi.Configure(txtConnString.Text.Trim());
            }
            try
            {
                using var http=new System.Net.Http.HttpClient { Timeout=TimeSpan.FromSeconds(5) };
                (await http.GetAsync(BssApi.Url+"/health")).EnsureSuccessStatusCode();
                MessageBox.Show("Backend đã kết nối.");
            }
            catch(Exception ex){MessageBox.Show(ex.Message,"Backend không sẵn sàng");}

            } catch(Exception ex){BssApi.Error(ex);}
            finally {if(sourceButton!=null)sourceButton.IsEnabled=true;}
        }
    }
}
