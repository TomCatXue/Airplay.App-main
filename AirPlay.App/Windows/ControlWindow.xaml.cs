using AirPlay.App.Extensions;
using Microsoft.UI.Xaml;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using WinRT.Interop;
using WinUIEx;
using WinUIEx.Messaging;

namespace AirPlay.App.Windows;

public sealed partial class ControlWindow : WindowEx
{
    public static XamlRoot ControlWindowXamlRoot { get; private set; } = null!;

    private WindowMessageMonitor? _messageMonitor;

    public ControlWindow()
    {
        this.Width = 420;
        this.Height = 400;

        this.ExtendsContentIntoTitleBar = true;
        this.IsTitleBarVisible = false;
        this.IsAlwaysOnTop = true;
        this.IsShownInSwitchers = false;

        this.Move(16, 16);

        InitializeComponent();

        // 设置整个 Grid 为可拖拽标题栏区域
        this.SetTitleBar(RootGrid);

        // Apple 风格圆角：通过 DWM API 设置
        SetWindowCorner(WinRT.Interop.WindowNative.GetWindowHandle(this));
    }

    private void OnWindowMessageReceived(object? sender, WindowMessageEventArgs e)
    {
        if (e.Message.MessageId == 0x0100)
        {
            if (e.Message.WParam == 0x1B)
                this.Hide();
        }
        else if (e.Message.MessageId == 0x0312)
        {
            this.Activate();
            this.SetForegroundWindow();
            this.SetFocus();
        }
    }

    private void Frame_Loaded(object sender, RoutedEventArgs e)
    {
        ControlWindowXamlRoot = Frame.XamlRoot;

        Frame.Navigate(typeof(ControlPage));
        _messageMonitor = new WindowMessageMonitor(this);
        _messageMonitor.WindowMessageReceived += OnWindowMessageReceived;

        PInvoke.RegisterHotKey
        (
            new HWND(WindowNative.GetWindowHandle(this)),
            1,
            HOT_KEY_MODIFIERS.MOD_WIN | HOT_KEY_MODIFIERS.MOD_ALT,
            0x41
        );
    }
    private static void SetWindowCorner(System.IntPtr hwnd)
    {
        try
        {
            int preference = 2; // Round (Apple 风格)
            DwmSetWindowAttribute(hwnd, 33, ref preference, sizeof(int));
        }
        catch { }
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(System.IntPtr hwnd, int attr, ref int value, int size);
}
