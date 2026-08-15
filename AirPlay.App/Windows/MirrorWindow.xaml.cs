using AirPlay.App.Extensions;
using AirPlay.Core2.Models;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Buffers;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Windows.Graphics.DirectX;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using WinUIEx;

using Timer = System.Timers.Timer;

namespace AirPlay.App.Windows;

public sealed partial class MirrorWindow : WindowEx
{
    private readonly Timer _timer = new(TimeSpan.FromSeconds(1));
    private readonly Lock _bitmapLock = new();
    private const double InitialScaleFactor = 0.85;

    private Size _frameSize;
    private int _decodedFrames = 0;
    private int _droppedFrames = 0;

    private CanvasDevice? _canvasDevice;
    private CanvasBitmap? _currentBitmap;

    private bool _isRendering = false;
    private bool _isDisposed = false;
    private bool _isFullScreen = false;

    public MirrorWindow(DeviceSession session, Size size)
    {
        Session = session;
        _frameSize = size;

        this.IsMaximizable = false;
        this.IsTitleBarVisible = false;
        this.ExtendsContentIntoTitleBar = true;
        this.Title = session.DeviceDisplayName;

        InitializeComponent();

        // 窗口大小 = 视频原始分辨率 ÷ DPI，保证 1:1 像素映射清晰度
        double scale = GetDpiScale();
        Width = size.Width / scale * InitialScaleFactor;
        Height = size.Height / scale * InitialScaleFactor;

        (Canvas.Width, Canvas.Height) = (size.Width, size.Height);
        Canvas.TargetElapsedTime = TimeSpan.FromSeconds(1 / (double)this.GetRefreshRate());

        // 设置整个 Grid 为可拖拽标题栏区域
        this.SetTitleBar(RootGrid);

        Closed += OnWindowClosed;

        _timer.Elapsed += OnElapsed;
        _timer.Start();
    }

    /// <summary>
    /// 安全获取 DPI 缩放比例：优先从 ControlWindow 获取，否则从屏幕 DC 获取
    /// </summary>
    private static double GetDpiScale()
    {
        try
        {
            if (ControlWindow.ControlWindowXamlRoot is not null)
                return ControlWindow.ControlWindowXamlRoot.RasterizationScale;
        }
        catch { }

        // 从屏幕 DC 获取 DPI
        try
        {
            HDC? hDC = PInvoke.GetDC(HWND.Null);
            if (hDC.HasValue)
            {
                int dpiX = PInvoke.GetDeviceCaps(hDC.Value, GET_DEVICE_CAPS_INDEX.LOGPIXELSX);
                PInvoke.ReleaseDC(HWND.Null, hDC.Value);
                return dpiX / 96.0;
            }
        }
        catch { }

        return 1.0;
    }

    public void OnFrameSizeChanged(Size size)
    {
        lock (_bitmapLock)
        {
            (Canvas.Width, Canvas.Height) = (size.Width, size.Height);
            _frameSize = size;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                double scale = GetDpiScale();
                Width = size.Width / scale * InitialScaleFactor;
                Height = size.Height / scale * InitialScaleFactor;
                // 视频切换时重新居中
                CenterWindow();
            }
            catch { }
        });
    }

    private void CenterWindow()
    {
        try
        {
            var area = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(
                this.AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
            if (area != null)
            {
                int x = (area.WorkArea.Width - this.AppWindow.Size.Width) / 2 + area.WorkArea.X;
                int y = (area.WorkArea.Height - this.AppWindow.Size.Height) / 2 + area.WorkArea.Y;
                this.AppWindow.Move(new global::Windows.Graphics.PointInt32(Math.Max(0, x), Math.Max(0, y)));
            }
        }
        catch { }
    }

    private static void SetWindowCorner(IntPtr hwnd)
    {
        try
        {
            int preference = 2; // Round (Apple 风格)
            DwmSetWindowAttribute(hwnd, 33, ref preference, sizeof(int));
        }
        catch { }
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public void OnFrameDataReceived(byte[] frameData)
    {
        Interlocked.Increment(ref _decodedFrames);

        if (_isDisposed || _canvasDevice == null)
        {
            ArrayPool<byte>.Shared.Return(frameData);
            return;
        }
        if (this.WindowState == WindowState.Minimized)
        {
            Canvas.Paused = true;
            ArrayPool<byte>.Shared.Return(frameData);
            return;
        }
        if (_isRendering)
        {
            Interlocked.Increment(ref _droppedFrames);
            ArrayPool<byte>.Shared.Return(frameData);
            return;
        }

        _isRendering = true;

        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                if (_isDisposed || _canvasDevice == null) return;

                lock (_bitmapLock)
                {
                    int expectedSize = _frameSize.Width * _frameSize.Height * 4; // BGRA = 4 bytes/pixel
                    if (frameData.Length < expectedSize)
                    {
                        Debug.WriteLine($"Frame size mismatch: data={frameData.Length}, expected={expectedSize}, frame={_frameSize.Width}x{_frameSize.Height}");
                        ArrayPool<byte>.Shared.Return(frameData);
                        return;
                    }

                    if (_currentBitmap == null ||
                        _currentBitmap.Size.Width != _frameSize.Width ||
                        _currentBitmap.Size.Height != _frameSize.Height)
                    {
                        _currentBitmap?.Dispose();
                        _currentBitmap = CanvasBitmap.CreateFromBytes(
                            _canvasDevice,
                            frameData,
                            _frameSize.Width,
                            _frameSize.Height,
                            DirectXPixelFormat.B8G8R8A8UIntNormalized
                        );
                    }
                    else _currentBitmap.SetPixelBytes(frameData);
                }

                if (Canvas.Paused) Canvas.Paused = false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"OnFrameDataReceived error: {ex.Message}");
            }
            finally
            {
                _isRendering = false;
                ArrayPool<byte>.Shared.Return(frameData);
            }
        });
    }

    private void OnElapsed(object? sender, ElapsedEventArgs e)
    {
        var dropped = Interlocked.Exchange(ref _droppedFrames, 0);
        var fps = Interlocked.Exchange(ref _decodedFrames, 0);

        if (dropped > 0)
            Debug.WriteLine($"FPS: {fps} (Dropped: {dropped})");
        else
            Debug.WriteLine($"FPS: {fps}");

        DispatcherQueue.TryEnqueue(() =>
        {
            if (FpsText != null)
                FpsText.Text = dropped > 0 ? $"{fps} FPS | 丢帧 {dropped}" : $"{fps} FPS";
        });
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        lock (_bitmapLock)
        {
            _currentBitmap?.Dispose();
            _currentBitmap = null;
        }

        _timer.Stop();
        _timer.Dispose();

        Canvas?.Paused = true;
        _canvasDevice = null;

        _isDisposed = true;
        GC.Collect();
    }

    private void Canvas_Draw(ICanvasAnimatedControl sender, CanvasAnimatedDrawEventArgs args)
    {
        if (_isDisposed) return;

        lock (_bitmapLock)
        {
            if (_currentBitmap != null)
                args.DrawingSession.DrawImage(_currentBitmap);
        }
    }

    private void Canvas_CreateResources(CanvasAnimatedControl sender, CanvasCreateResourcesEventArgs args)
    {
        _canvasDevice = sender.Device;
        Debug.WriteLine($"Canvas device created: {_canvasDevice != null}");
    }

    public string DeviceIcon
    {
        get
        {
            if (string.IsNullOrEmpty(Session.DeviceModel)) return "\ue7f4";
            if (Session.DeviceModel.Contains("Phone")) return "\ue8ea";
            if (Session.DeviceModel.Contains("Pad")) return "\ue70a";

            return "\ue7f4";
        }
    }

    public DeviceSession Session { get; private set; }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => this.Minimize();

    private void FullScreenButton_Click(object sender, RoutedEventArgs e)
    {
        _isFullScreen = !_isFullScreen;
        VideoViewbox.Stretch = _isFullScreen ? Stretch.Uniform : Stretch.UniformToFill;

        try
        {
            this.AppWindow.SetPresenter(
                _isFullScreen
                    ? Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen
                    : Microsoft.UI.Windowing.AppWindowPresenterKind.Overlapped);
        }
        catch
        {
        }
    }

    private async void CloseButton_Click(object sender, RoutedEventArgs e) => await ConfirmDialog.ShowAsync();

    private void ConfirmDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args) => Session.Disconnect();

    private async void Border_Loaded(object sender, RoutedEventArgs e)
    {
        CenterWindow();

        // Apple 风格圆角：通过 DWM API 设置
        SetWindowCorner(WinRT.Interop.WindowNative.GetWindowHandle(this));

        this.AppWindow.TitleBar.SetDragRectangles(
        [
            new()
            {
                X = 0,
                Y = 0,
                Width = (int)(Border.ActualWidth * Border.XamlRoot.RasterizationScale),
                Height = (int)(48 * Border.XamlRoot.RasterizationScale)
            }
        ]);

        await Task.Delay(500);
        Popup.IsOpen = true;
    }

    private void Border_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        this.AppWindow.TitleBar.SetDragRectangles(
        [
            new()
            {
                X = 0,
                Y = 0,
                Width = (int)(Border.ActualWidth * Border.XamlRoot.RasterizationScale),
                Height = (int)(48 * Border.XamlRoot.RasterizationScale)
            }
        ]);
    }

    private void Grid_Unloaded(object sender, RoutedEventArgs e)
    {
        this.Canvas.RemoveFromVisualTree();
        this.Canvas = null;
    }
}