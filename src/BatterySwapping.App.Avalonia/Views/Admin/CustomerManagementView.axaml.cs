using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using BatterySwapping.App.Avalonia.Models;
using BatterySwapping.App.Avalonia.Services;

namespace BatterySwapping.App.Avalonia.Views.Admin;

public partial class CustomerManagementView : UserControl
{
    private readonly ReportService _reportService = new();
    private List<KhachHang> _allCustomers = new();
    private KhachHang? _targetCustomer;

    public CustomerManagementView()
    {
        InitializeComponent();

        AttachedToVisualTree += (_, _) => LoadCustomers();
    }

    public async void LoadCustomers()
    {
        try
        {
            _allCustomers = await _reportService.GetAllCustomers() ?? new();
            FilterData();
        }
        catch (Exception ex)
        {
            BssApi.ReportError(ex);
        }
    }

    private void FilterData()
    {
        if (dgCustomers == null) return;

        string keyword = txtSearch?.Text?.Trim().ToLower() ?? "";
        var query = (_allCustomers ?? new()).AsEnumerable();

        if (!string.IsNullOrEmpty(keyword))
        {
            query = query.Where(c => (c.HoTen != null && c.HoTen.ToLower().Contains(keyword)) ||
                                     (c.SoDienThoai != null && c.SoDienThoai.Contains(keyword)) ||
                                     (c.BienSoXe != null && c.BienSoXe.ToLower().Contains(keyword)));
        }

        dgCustomers.ItemsSource = query.ToList();
    }

    private void TxtSearch_KeyUp(object? sender, KeyEventArgs e)
    {
        FilterData();
    }

    private void BtnRefresh_Click(object? sender, RoutedEventArgs e)
    {
        LoadCustomers();
    }

    private void BtnTopUp_Click(object? sender, RoutedEventArgs e)
    {
        if (dgCustomers?.SelectedItem is KhachHang selected)
        {
            OpenTopUpPanel(selected);
        }
        else if (_allCustomers.Count > 0)
        {
            OpenTopUpPanel(_allCustomers[0]);
        }
    }

    private void BtnRowTopUp_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is KhachHang customer)
        {
            OpenTopUpPanel(customer);
        }
    }

    private void OpenTopUpPanel(KhachHang customer)
    {
        _targetCustomer = customer;
        if (pnlTopUp != null && txtCustomerTarget != null)
        {
            pnlTopUp.IsVisible = true;
            txtCustomerTarget.Text = $"{customer.HoTen} ({customer.SoDienThoai}) • Ví: {customer.SoDuFormatted}";
            if (txtTopUpFeedback != null) txtTopUpFeedback.Text = "";
        }
    }

    private void BtnCloseTopUp_Click(object? sender, RoutedEventArgs e)
    {
        if (pnlTopUp != null) pnlTopUp.IsVisible = false;
        _targetCustomer = null;
    }

    private void BtnQuick_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string amt && txtAmount != null)
        {
            txtAmount.Text = amt;
        }
    }

    string? paymentKey;string paymentBody="";
    private async void BtnConfirmTopUp_Click(object? sender, RoutedEventArgs e)
    {
        if (_targetCustomer == null) return;

        string amtStr = txtAmount?.Text?.Trim() ?? "";
        if (!decimal.TryParse(amtStr, out decimal amount) || amount <= 0)
        {
            if (txtTopUpFeedback != null) txtTopUpFeedback.Text = "Số tiền nạp không hợp lệ!";
            return;
        }

        string method = (cboMethod?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "ChuyenKhoan";

        var btn = sender as Button;
        if (btn != null) btn.IsEnabled = false;

        try
        {
            string body=_targetCustomer.MaKH+":"+amount;if(body!=paymentBody){paymentKey=null;paymentBody=body;}paymentKey??=Guid.NewGuid().ToString();
            var op = await _reportService.TopUpWallet(_targetCustomer.MaKH, amount, method,paymentKey);
            if (op.Success)
            {paymentKey=null;
                if (txtTopUpFeedback != null)
                {
                    txtTopUpFeedback.Text = $"Nạp thành công {amount:N0} đ cho {_targetCustomer.HoTen}!";
                }
                LoadCustomers();
            }
            else
            {
                if (txtTopUpFeedback != null)
                {
                    txtTopUpFeedback.Text = $"❌ Lỗi nạp tiền: {op.Error}";
                }
            }
        }
        catch (Exception ex)
        {
            BssApi.Error(ex);
        }
        finally
        {
            if (btn != null) btn.IsEnabled = true;
        }
    }
}
