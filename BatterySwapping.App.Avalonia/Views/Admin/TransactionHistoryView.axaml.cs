using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using BatterySwapping.App.Avalonia.Services;

namespace BatterySwapping.App.Avalonia.Views.Admin;

public partial class TransactionHistoryView : UserControl
{
    private readonly ReportService _reportService = new();

    public TransactionHistoryView()
    {
        InitializeComponent();

        AttachedToVisualTree += (_, _) => LoadData();
    }

    public async void LoadData()
    {
        try
        {
            if (dgSwapHistory != null)
            {
                dgSwapHistory.ItemsSource = await _reportService.GetSwapHistory();
            }

            if (dgPaymentHistory != null)
            {
                dgPaymentHistory.ItemsSource = await _reportService.GetTopUpHistory();
            }
        }
        catch (Exception ex)
        {
            BssApi.ReportError(ex);
        }
    }

    private void BtnRefresh_Click(object? sender, RoutedEventArgs e)
    {
        LoadData();
    }
}
