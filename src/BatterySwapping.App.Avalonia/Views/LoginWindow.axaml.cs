using System;
using System.Net.Http;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using BatterySwapping.App.Avalonia.Services;

namespace BatterySwapping.App.Avalonia.Views;

public partial class LoginWindow : Window
{
    private readonly AuthService _authService;

    public LoginWindow()
    {
        InitializeComponent();
        _authService = new AuthService();
        if (txtConnString != null)
        {
            txtConnString.Text = BssApi.Url;
        }

        BssApi.OnErrorNotification += OnApiErrorReceived;
    }

    private void OnApiErrorReceived(string error)
    {
        if (lblError != null)
        {
            lblError.Text = error;
            lblError.IsVisible = true;
        }
    }

    private async void BtnLogin_Click(object? sender, RoutedEventArgs e)
    {
        var sourceButton = sender as Button;
        if (sourceButton != null) sourceButton.IsEnabled = false;

        try
        {
            if (lblError != null) lblError.IsVisible = false;

            if (txtConnString != null && !string.IsNullOrWhiteSpace(txtConnString.Text))
            {
                BssApi.Configure(txtConnString.Text.Trim());
            }

            string username = txtUsername?.Text?.Trim() ?? string.Empty;
            string password = txtPassword?.Text ?? string.Empty;

            if (string.IsNullOrWhiteSpace(username))
            {
                if (lblError != null)
                {
                    lblError.Text = "Vui lòng nhập tên đăng nhập hoặc số điện thoại Kiosk.";
                    lblError.IsVisible = true;
                }
                return;
            }

            Operation outcome;
            // Nếu người dùng nhập số điện thoại hoặc mã định danh Kiosk
            if (string.IsNullOrEmpty(password) || (username.Length >= 9 && username.StartsWith("09")))
            {
                outcome = await _authService.KioskLogin(username);
            }
            else
            {
                outcome = await _authService.Login(username, password);
            }

            if (outcome.Success)
            {
                var mainWindow = new MainWindow();
                mainWindow.Show();
                Close();
            }
            else
            {
                if (lblError != null)
                {
                    lblError.Text = outcome.Error;
                    lblError.IsVisible = true;
                }
            }
        }
        catch (Exception ex)
        {
            BssApi.Error(ex);
        }
        finally
        {
            if (sourceButton != null) sourceButton.IsEnabled = true;
        }
    }

    private async void BtnKioskDirect_Click(object? sender, RoutedEventArgs e)
    {
        var sourceButton = sender as Button;
        if (sourceButton != null) sourceButton.IsEnabled = false;

        try
        {
            if (txtConnString != null && !string.IsNullOrWhiteSpace(txtConnString.Text))
            {
                BssApi.Configure(txtConnString.Text.Trim());
            }

            await _authService.Logout();
            var mainWindow = new MainWindow(openInKioskMode: true);
            mainWindow.Show();
            Close();
        }
        catch (Exception ex)
        {
            BssApi.Error(ex);
        }
        finally
        {
            if (sourceButton != null) sourceButton.IsEnabled = true;
        }
    }

    private void BtnQuickAdmin_Click(object? sender, RoutedEventArgs e)
    {
        if (txtUsername != null) txtUsername.Text = "admin";
        if (txtPassword != null) txtPassword.Text = "admin123";
        BtnLogin_Click(btnLogin, e);
    }

    private void BtnQuickTech_Click(object? sender, RoutedEventArgs e)
    {
        if (txtUsername != null) txtUsername.Text = "tech_bk01";
        if (txtPassword != null) txtPassword.Text = "tech123";
        BtnLogin_Click(btnLogin, e);
    }

    private void BtnQuickKiosk_Click(object? sender, RoutedEventArgs e)
    {
        if (txtUsername != null) txtUsername.Text = "0912345678";
        if (txtPassword != null) txtPassword.Text = "";
        BtnLogin_Click(btnLogin, e);
    }

    private async void BtnTestConnection_Click(object? sender, RoutedEventArgs e)
    {
        var sourceButton = sender as Button;
        if (sourceButton != null) sourceButton.IsEnabled = false;

        try
        {
            if (txtConnString != null && !string.IsNullOrWhiteSpace(txtConnString.Text))
            {
                BssApi.Configure(txtConnString.Text.Trim());
            }

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var resp = await http.GetAsync(BssApi.Url + "/health");
            resp.EnsureSuccessStatusCode();

            if (lblConnStatus != null)
            {
                lblConnStatus.Text = "✓ Đã kết nối backend thành công!";
                lblConnStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
                lblConnStatus.IsVisible = true;
            }
        }
        catch (Exception ex)
        {
            if (lblConnStatus != null)
            {
                lblConnStatus.Text = "✗ Không thể kết nối: " + ex.Message;
                lblConnStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
                lblConnStatus.IsVisible = true;
            }
        }
        finally
        {
            if (sourceButton != null) sourceButton.IsEnabled = true;
        }
    }
}
