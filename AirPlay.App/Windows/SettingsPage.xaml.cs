using AirPlay.App.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace AirPlay.App.Windows;

public sealed partial class SettingsPage : Page
{
    private readonly AppSettingsService _settingsService = ((App)App.Current).Host.Services.GetRequiredService<AppSettingsService>();

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ServiceNameBox.Text = _settingsService.Settings.ServiceName;
        AirTunesPortBox.Text = _settingsService.Settings.AirTunesPort.ToString();
        AirPlayPortBox.Text = _settingsService.Settings.AirPlayPort.ToString();
        StartWithWindowsToggle.IsOn = _settingsService.Settings.StartWithWindows;

        NetworkCombo.Items.Add(new ComboBoxItem { Content = "自动", Tag = null });
        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces()
            .Where(nic => nic.OperationalStatus == OperationalStatus.Up &&
                nic.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                nic.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
            .OrderBy(nic => nic.Name))
        {
            string addresses = string.Join(", ", nic.GetIPProperties().UnicastAddresses
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address))
                .Select(a => a.Address));
            NetworkCombo.Items.Add(new ComboBoxItem
            {
                Content = string.IsNullOrEmpty(addresses) ? nic.Name : $"{nic.Name} ({addresses})",
                Tag = nic.Id
            });
        }

        int selectedIndex = 0;
        if (_settingsService.Settings.NetworkAdapterId is { } adapterId)
        {
            int index = NetworkCombo.Items.Cast<ComboBoxItem>().ToList().FindIndex(item => Equals(item.Tag, adapterId));
            if (index >= 0) selectedIndex = index;
        }
        NetworkCombo.SelectedIndex = selectedIndex;

        // 顶栏作为窗口拖拽区（只在顶栏范围内）——不能把整个窗口设为标题栏，
        // 否则 caption 区域会拦截滚轮手势，设置页 ScrollViewer 无法滚动。
        try
        {
            var controlWindow = ((App)App.Current).Host.Services.GetRequiredService<ControlWindow>();
            controlWindow.SetTitleBar(PageTitleBar);
        }
        catch { }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => Frame.GoBack();

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        _settingsService.Settings.ServiceName = string.IsNullOrWhiteSpace(ServiceNameBox.Text)
            ? "AirPlay Windows App"
            : ServiceNameBox.Text.Trim();
        _settingsService.Settings.AirTunesPort = ParsePort(AirTunesPortBox.Text, 5000);
        _settingsService.Settings.AirPlayPort = ParsePort(AirPlayPortBox.Text, 7100);
        _settingsService.Settings.NetworkAdapterId = (NetworkCombo.SelectedItem as ComboBoxItem)?.Tag as string;

        // 显式持久化（此前依赖 SetStartWithWindowsAsync 内部的隐式 Save，脆弱）
        _settingsService.Save();
        await _settingsService.SetStartWithWindowsAsync(StartWithWindowsToggle.IsOn);

        Frame.GoBack();
    }

    private static ushort ParsePort(string? text, ushort fallback)
    {
        if (ushort.TryParse(text, out ushort port) && port >= 1024)
            return port;

        return fallback;
    }
}
