using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using BatterySwapping.App.Avalonia.Helpers;
using BatterySwapping.App.Avalonia.Models;
using BatterySwapping.App.Avalonia.Services;
using BatterySwapping.Contracts;

namespace BatterySwapping.App.Avalonia.Views.Kiosk;

public partial class CustomerDashboardView : UserControl
{
    public event Action? StartSwap;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool _loading;
    private bool _paying;
    private string? _requestKey;
    private decimal? _requestAmount;

    public CustomerDashboardView()
    {
        InitializeComponent();

        _timer.Tick += async (_, _) => await Reload();

        AttachedToVisualTree += async (_, _) =>
        {
            await Reload();
            _timer.Start();
        };

        DetachedFromVisualTree += (_, _) =>
        {
            _timer.Stop();
        };
    }

    public async Task Reload()
    {
        if (_loading) return;
        _loading = true;

        try
        {
            var w = await BssApi.Client.Get<JsonElement>("wallet");
            string Field(string n) => w.TryGetProperty(n, out var v) && v.ValueKind != JsonValueKind.Null ? v.ToString() : "Chưa có";

            decimal total = w.GetProperty("SoDuVi").GetDecimal();
            decimal held = w.GetProperty("ReservedBalance").GetDecimal();
            int khId = w.TryGetProperty("MaKH", out var mid) ? mid.GetInt32() : 1;
            string phone = Field("SoDienThoai");

            if (txtProfile != null)
            {
                txtProfile.Text = $"• Họ và tên: {Field("HoTen")}\n" +
                                  $"• Điện thoại: {phone}\n" +
                                  $"• Biển số xe: {Field("BienSoXe")}\n" +
                                  $"• Pin đang dùng: {Field("MaPinDangMuon")}";
            }

            if (txtBalance != null) txtBalance.Text = $"{total:N0} đ";
            if (txtAvailable != null) txtAvailable.Text = $"Đang giữ chỗ: {held:N0} đ • Có thể sử dụng: {total - held:N0} đ";

            if (txtQrPayload != null)
            {
                txtQrPayload.Text = QrCodeHelper.GenerateCustomerQrPayload(khId, phone);
            }

            if (dgSwapHistory != null)
            {
                dgSwapHistory.ItemsSource = await BssApi.Client.Get<List<GiaoDichDoiPin>>("me/history/swaps");
            }

            if (dgTopUpHistory != null)
            {
                dgTopUpHistory.ItemsSource = await BssApi.Client.Get<List<PaymentRequest>>("payments");
            }

            if (txtStatus != null)
            {
                txtStatus.Text = $"Tài khoản: {Field("HoTen")} ({phone}) • Cập nhật lúc {DateTime.Now:HH:mm:ss}";
            }
        }
        catch (Exception ex)
        {
            if (txtStatus != null)
            {
                txtStatus.Text = $"Chưa thể tải thông tin ví: {ex.Message}";
            }
        }
        finally
        {
            _loading = false;
        }
    }

    private void QuickAmount_Click(object? sender, RoutedEventArgs e)
    {
        if (!_paying && sender is Button b && txtTopUpAmount != null)
        {
            txtTopUpAmount.Text = b.Tag?.ToString();
        }
    }

    private async void TopupClick(object? sender, RoutedEventArgs e)
    {
        if (_paying) return;

        string amtText = txtTopUpAmount?.Text?.Trim() ?? "";
        if (!decimal.TryParse(amtText, out var value) || value <= 0 || value > 10000000 || decimal.Truncate(value) != value)
        {
            if (txtStatus != null) txtStatus.Text = "Vui lòng nhập số tiền hợp lệ từ 10.000 đến 10.000.000 đ.";
            return;
        }

        if (_requestAmount != value)
        {
            _requestKey = null;
            _requestAmount = value;
        }
        _requestKey ??= Guid.NewGuid().ToString();

        _paying = true;
        if (btnTopUp != null) btnTopUp.IsEnabled = false;

        try
        {
            await BssApi.Client.Post<JsonElement>("payments", new CreatePaymentRequest(value, _requestKey));
            _requestKey = null;
            if (txtStatus != null) txtStatus.Text = $"Đã tạo yêu cầu nạp {value:N0} đ; chờ xác nhận thanh toán.";
            await Reload();
        }
        catch (Exception ex)
        {
            if (txtStatus != null)
            {
                txtStatus.Text = $"Chưa xác nhận nạp ví: {ex.Message}";
            }
        }
        finally
        {
            _paying = false;
            if (btnTopUp != null) btnTopUp.IsEnabled = true;
        }
    }

    private void SwapClick(object? sender, RoutedEventArgs e)
    {
        StartSwap?.Invoke();
    }
}
