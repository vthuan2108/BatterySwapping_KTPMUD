using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using BatterySwapping.App.Avalonia.Models;
using BatterySwapping.App.Avalonia.Services;

namespace BatterySwapping.App.Avalonia.Views.Admin;

public partial class OverviewView : UserControl
{
    private readonly ReportService _reportService = new();
    private readonly StationService _stationService = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };
    private bool _loading;

    public event Action<string>? StationSelected;

    public OverviewView()
    {
        InitializeComponent();
        _timer.Tick += async (_, _) => await ReloadData();

        AttachedToVisualTree += async (_, _) =>
        {
            await ReloadData();
            _timer.Start();
        };

        DetachedFromVisualTree += (_, _) =>
        {
            _timer.Stop();
        };
    }

    public async Task ReloadData()
    {
        if (_loading) return;
        _loading = true;

        try
        {
            var stats = await _reportService.GetSummaryStats();
            var stations = await _stationService.GetAllStations();

            if (lblRevenue != null) lblRevenue.Text = $"{stats.DoanhThuHomNay:N0} đ";
            if (lblSwaps != null) lblSwaps.Text = $"{stats.TongLuotDoiPin} lượt";
            if (lblReadyBatteries != null) lblReadyBatteries.Text = $"{stats.SoPinKhaDung} / {stats.TongSoPin} pin";
            if (lblAlerts != null) lblAlerts.Text = $"{stats.SoCanhBaoChuaXuLy} sự cố";

            if (dgStations != null)
            {
                dgStations.ItemsSource = stations;
            }

            if (txtStatus != null)
            {
                txtStatus.Text = $"Cập nhật lúc {DateTime.Now:HH:mm:ss} • Mạng lưới {stations.Count} trạm sạc trực tuyến.";
            }
        }
        catch (Exception ex)
        {
            if (txtStatus != null)
            {
                txtStatus.Text = $"Không thể tải dữ liệu: {ex.Message}";
            }
        }
        finally
        {
            _loading = false;
        }
    }

    private async void BtnRefresh_Click(object? sender, RoutedEventArgs e)
    {
        await ReloadData();
    }

    private void DgStations_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (dgStations.SelectedItem is TramPin station)
        {
            StationSelected?.Invoke(station.MaTram);
        }
    }

    private void BtnInspectStation_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is TramPin station)
        {
            StationSelected?.Invoke(station.MaTram);
        }
        else if (dgStations.SelectedItem is TramPin sel)
        {
            StationSelected?.Invoke(sel.MaTram);
        }
    }
}
