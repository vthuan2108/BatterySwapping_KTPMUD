using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using BatterySwapping.App.Avalonia.Models;
using BatterySwapping.App.Avalonia.Services;

namespace BatterySwapping.App.Avalonia.Views.Admin;

public partial class SafetyAlertsView : UserControl
{
    private readonly SafetyMonitorService _safetyService = new();

    public SafetyAlertsView()
    {
        InitializeComponent();

        AttachedToVisualTree += (_, _) => LoadAlerts();
    }

    public async void LoadAlerts()
    {
        if (dgAlerts == null || chkOnlyUnresolved == null) return;
        try
        {
            bool onlyUnresolved = chkOnlyUnresolved.IsChecked == true;
            dgAlerts.ItemsSource = await _safetyService.GetAlerts(onlyUnresolved);
        }
        catch (Exception ex)
        {
            BssApi.ReportError(ex);
        }
    }

    private void BtnRefresh_Click(object? sender, RoutedEventArgs e)
    {
        LoadAlerts();
    }

    private void ChkOnlyUnresolved_Changed(object? sender, RoutedEventArgs e)
    {
        LoadAlerts();
    }

    private async void BtnResolve_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is CanhBaoIoT alert)
        {
            btn.IsEnabled = false;
            try
            {
                if (alert.DaXuLy)
                {
                    if (txtStatusFeedback != null)
                    {
                        txtStatusFeedback.Text = $"ℹ Sự cố #{alert.MaCB} đã được xử lý trước đó.";
                    }
                    return;
                }

                string tech = AuthService.CurrentUser != null
                    ? $"{AuthService.CurrentUser.HoTen} ({AuthService.CurrentUser.VaiTro})"
                    : "Kỹ thuật viên Trạm";

                string note = $"Đã kiểm tra an toàn tại ngăn #{alert.MaNgan} ({alert.MaTram}), pin {alert.MaPin}. Nhiệt độ an toàn, đã mở khóa khay tủ.";

                bool success = await _safetyService.ResolveAlert(alert.MaCB, tech, note, moKhoaNganTu: true);
                if (success)
                {
                    if (txtStatusFeedback != null)
                    {
                        txtStatusFeedback.Text = $"✔ Đã khắc phục thành công sự cố #{alert.MaCB}! Ngăn #{alert.MaNgan} đã được mở khóa an toàn.";
                    }
                    LoadAlerts();
                }
                else
                {
                    if (txtStatusFeedback != null)
                    {
                        txtStatusFeedback.Text = $"❌ Không thể cập nhật trạng thái sự cố #{alert.MaCB}. Vui lòng thử lại!";
                    }
                }
            }
            catch (Exception ex)
            {
                BssApi.Error(ex);
            }
            finally
            {
                btn.IsEnabled = true;
            }
        }
    }
}
