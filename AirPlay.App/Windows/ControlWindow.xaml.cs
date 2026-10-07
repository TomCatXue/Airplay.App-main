using AirPlay.App.Extensions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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

        // 注意：不能把整个 RootGrid 设为标题栏 —— caption 区域会拦截滚轮/滑动手势，
        // 导致页面（如设置页 ScrollViewer）无法滚动。标题栏由各页面自己的顶栏
        // （ControlPage/SettingsPage 的 PageTitleBar）在 Loaded 时设置。
        // this.SetTitleBar(RootGrid);  // 移除

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
        else if (e.Message.MessageId == 0x020A)   // WM_MOUSEWHEEL
        {
            // 兜底：即便标题栏/caption 区域吞掉了 XAML 的滚轮输入，
            // 也在消息层直接驱动页面内的 ScrollViewer，保证设置页等可滚动。
            if (ForwardMouseWheel(e.Message.WParam))
            {
                e.Handled = true;
                e.Result = 0;
            }
        }
    }

    /// <summary>
    /// 把 Win32 滚轮消息转换为当前页面 ScrollViewer 的纵向滚动。
    /// 返回 true 表示已消费（调用方应把消息标记为已处理，避免与 XAML 重复滚动）。
    /// </summary>
    private bool ForwardMouseWheel(nuint wParam)
    {
        try
        {
            ScrollViewer? scrollViewer = FindFirstScrollViewer(Frame);
            if (scrollViewer == null) return false;

            double scrollable = scrollViewer.ExtentHeight - scrollViewer.ViewportHeight;
            if (scrollable <= 0.5) return false;

            int hi = (int)((wParam >> 16) & 0xFFFF);
            int delta = (short)hi;                                     // 正值 = 向上滚
            double target = scrollViewer.VerticalOffset - delta * 0.8; // 每档约 96px
            if (target < 0) target = 0;
            if (target > scrollable) target = scrollable;

            scrollViewer.ChangeView(null, target, null);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static ScrollViewer? FindFirstScrollViewer(DependencyObject? root)
    {
        if (root == null) return null;
        if (root is ScrollViewer sv) return sv;

        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            if (FindFirstScrollViewer(VisualTreeHelper.GetChild(root, i)) is ScrollViewer found)
                return found;
        }

        return null;
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
