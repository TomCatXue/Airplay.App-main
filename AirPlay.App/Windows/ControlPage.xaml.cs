using AirPlay.App.Extensions;
using AirPlay.App.Models;
using AirPlay.App.Services;
using AirPlay.Core2.Models;
using AirPlay.Core2.Models.Messages;
using AirPlay.Core2.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using H.NotifyIcon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using WinUIEx;

namespace AirPlay.App.Windows;

public sealed partial class ControlPage : Page
{
    private readonly SmtcControlService _smtcControlService = ((App)App.Current).Host.Services.GetService<SmtcControlService>()!;

    public ControlPageVM VM => (ControlPageVM)DataContext;

    public ControlPage()
    {
        this.DataContext = ((App)App.Current).Host.Services.GetService<ControlPageVM>()!;
        InitializeComponent();

        this.ActualThemeChanged += OnActualThemeChanged;
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        App.TaskbarIcon!.IconSource = App.GetIconTheme(this.ActualTheme == ElementTheme.Dark);
    }

    private void ControlButton_Click(object sender, RoutedEventArgs e)
    {
        Button button = (sender as Button)!;

        if (Enum.TryParse<MediaControlCommand>(button.Tag.ToString(), out var command))
            _smtcControlService.SendMediaControlCommand(command);
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(SettingsPage));

    private bool _wired = false;

    /// <summary>
    /// 顶栏载入：把它设为窗口标题栏（拖拽区），并完成 VM 订阅与失焦隐藏接线。
    /// 注意：标题栏只能覆盖顶栏这一块区域——若把整个 RootGrid 设为标题栏，
    /// caption（非客户区）会拦截滚轮/滑动手势，页面内的 ScrollViewer 将无法滚动。
    /// </summary>
    private void PageTitleBar_Loaded(object sender, RoutedEventArgs e)
    {
        ControlWindow controlWindow = ((App)App.Current).Host.Services.GetRequiredService<ControlWindow>();

        try { controlWindow.SetTitleBar(PageTitleBar); }
        catch { }

        if (_wired) return;
        _wired = true;

        VM.PropertyChanged += OnPropertyChanged;

        controlWindow.SetFocus();

        controlWindow.Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated)
                controlWindow.Hide();
        };
    }

    private void OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "Device")
        {
            // 镜像徽标：避免 x:Load 绑定可空链 VM.Device.ShowMirrorIcon 导致 XamlCompiler 崩溃，
            // 改为代码驱动 Visibility。
            if (MirrorBadge != null)
                MirrorBadge.Visibility = VM.Device?.ShowMirrorIcon == true
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }
    }
}

public partial class ControlPageVM : ObservableObject
{
    private readonly SessionManager _sessionManager;
    private readonly SmtcControlService _smtcControlService;

    public ObservableCollection<Device> Devices { get; init; } = [];

    [ObservableProperty]
    public partial Device? Device { get; set; }

    public bool ShowNoDevice => Devices.Count == 0;

    public ControlPageVM(SessionManager sessionManager, SmtcControlService smtcControlService)
    {
        _sessionManager = sessionManager;
        _smtcControlService = smtcControlService;

        _sessionManager.SessionCreated += OnSessionCreated;
        _sessionManager.SessionClosed += OnSessionClosed;
    }

    partial void OnDeviceChanged(Device? value)
    {
        _smtcControlService.SwitchDevice(value);
    }

    private void OnSessionCreated(object? sender, DeviceSession e)
    {
        App.DispatcherQueue.TryEnqueue(() =>
        {
            ControlWindow controlWindow = ((App)App.Current).Host.Services.GetRequiredService<ControlWindow>();
            controlWindow.Activate();
            controlWindow.SetForegroundWindow();
            controlWindow.SetFocus();

            Device device = new(e);
            Devices.Add(device);
            OnPropertyChanged(nameof(ShowNoDevice));

            Device ??= device;
        });
    }

    private void OnSessionClosed(object? sender, DeviceSession e)
    {
        App.DispatcherQueue.TryEnqueue(() =>
        {
            if (Devices.FirstOrDefault(d => d.Session == e) is Device device)
                Devices.Remove(device);

            OnPropertyChanged(nameof(ShowNoDevice));
        });
    }
}