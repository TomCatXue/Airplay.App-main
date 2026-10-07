using AirPlay.App.Extensions;
using AirPlay.App.Models;
using AirPlay.App.Services;
using AirPlay.Core2.Models;
using AirPlay.Core2.Models.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Serilog;
using System;
using System.Buffers;
using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Windows.Graphics.DirectX;
using Windows.Storage;
using Windows.Storage.Streams;
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
    private const double LeftControlPanelWidth = 240;
    // 横屏参考 iPad 4:3，竖屏参考现代手机 9:19.5
    private const double LandscapeReferenceRatio = 4.0 / 3.0;
    private const double PortraitReferenceRatio = 9.0 / 19.5;

    // 视频铺满窗口边缘（无留白），圆角由"窗口形状"与"视频裁切"共用同一半径，
    // 因此二者同心、不会露出深色环；半径见 ApplyFrameLayout。
    private const double WindowSideMargin = 0;
    private const double WindowTopMargin = 0;
    private const double WindowBottomMargin = 0;

    private Size _frameSize;
    private int _decodedFrames = 0;
    private int _droppedFrames = 0;

    private CanvasDevice? _canvasDevice;
    private CanvasBitmap? _currentBitmap;

    private bool _isFullScreen = false;
    private bool _largeScreenMode = false;
    private int _isRendering = 0;   // 0=空闲 1=渲染中，用 Interlocked 保证线程安全
    private bool _isDisposed = false;

    private readonly SmtcControlService _smtcControlService =
        ((App)App.Current).Host.Services.GetService<SmtcControlService>()!;
    private readonly ControlPageVM _vm =
        ((App)App.Current).Host.Services.GetService<ControlPageVM>()!;
    public ControlPageVM VM => _vm;

    private DeviceFrameProfile _profile = DeviceFrameProfile.Rectangular;

    // 屏幕区圆角半径（由 ApplyFrameLayout 计算，圆角裁剪层复用，保证与容器一致）
    private double _screenRadius = 40;
    private Microsoft.UI.Composition.CompositionRoundedRectangleGeometry? _screenClipGeometry;

    public MirrorWindow(DeviceSession session, Size size)
    {
        Session = session;
        _frameSize = size;
        _profile = DeviceFrameProfileResolver.Resolve(session.DeviceModel, (uint)size.Width, (uint)size.Height);

        this.IsMaximizable = false;
        this.IsTitleBarVisible = false;
        this.ExtendsContentIntoTitleBar = true;
        this.Title = session.DeviceDisplayName;

        InitializeComponent();

        // 真正去掉系统那圈边框：移除 WS_THICKFRAME（含 WS_BORDER）使窗口无边框，
        // 再加 SetWindowRgn 的同心圆角，就从根上不再有白边/褐边。
        try { MakeWindowBorderless(); } catch { }

        (Canvas.Width, Canvas.Height) = (size.Width, size.Height);
        // 刷新率可能返回 0（部分显示器/远程会话），保护避免 1/0 抛异常导致闪退
        int refreshRate = this.GetRefreshRate();
        if (refreshRate <= 0) refreshRate = 60;
        Canvas.TargetElapsedTime = TimeSpan.FromSeconds(1.0 / refreshRate);

        // 窗口已无标题栏（去边框），改为在 RootGrid 上手动实现拖拽（见 RootGrid_Pointer*）

        // 顶部悬浮控制胶囊：独立 Popup，始终置顶显示（不随窗口内层级变化被覆盖）
        try { ControlCapsule.IsOpen = true; }
        catch { }

        // 窗口圆角形状在 ApplyFrameLayout 末尾通过 SetWindowRgn 与应用（与视频圆角同心）

        Closed += OnWindowClosed;

        _timer.Elapsed += OnElapsed;
        _timer.Start();

        // 按设备外形与朝向计算窗口尺寸（保留视频自动变大）
        ApplyFrameLayout(size);

        Log.Information("[MirrorWindow] Constructed for {Device} ({W}x{H}, refresh={Rr})",
            session.DeviceDisplayName, size.Width, size.Height, refreshRate);
    }

    /// <summary>
    /// 根据视频真实尺寸、设备朝向参考比例与设备外形，计算屏幕区/外框/圆角/灵动岛并布局窗口。
    /// 视频在屏幕区内按自身宽高比 letterbox 自适应；窗口随解码帧尺寸增长（保留自动变大效果）。
    /// </summary>
    private void ApplyFrameLayout(Size videoSize)
    {
        try
        {
            if (videoSize.Width <= 0 || videoSize.Height <= 0)
                videoSize = _frameSize;
            if (videoSize.Width <= 0 || videoSize.Height <= 0)
                return;

            double dipScale = GetDpiScale();          // dpi / 96
            double dvw = videoSize.Width / dipScale * InitialScaleFactor;
            double dvh = videoSize.Height / dipScale * InitialScaleFactor;

            // 视频铺满屏幕区，不做额外深色边框（整窗已是设备深色，不会出现褐色遮罩圈）。
            // 屏幕圆角半径：短边比例 12% 且封顶 30dip —— 接近 iPhone 真机连续曲线手感；
            // 该半径同时用于"窗口形状(SetWindowRgn)"与"视频裁切"，保证两者同心。
            double shortEdge = Math.Min(dvw, dvh);
            double screenW = dvw;
            double screenH = dvh;
            _screenRadius = Math.Min(shortEdge * 0.12, 30);

            DeviceFrame.Padding = new Thickness(0);
            DeviceFrame.CornerRadius = new CornerRadius(0);
            ScreenBorder.CornerRadius = new CornerRadius(_screenRadius);

            // 同步视频层圆角裁剪（合成层，抗锯齿）
            UpdateScreenClip();

            // 灵动岛/刘海：仅竖屏显示，按 iOS 真机比例对齐
            ApplyNotch(screenW, screenH);

            double frameW = screenW;
            double frameH = screenH;

            double panelW = _largeScreenMode ? LeftControlPanelWidth : 0;
            double totalW = frameW + 2 * WindowSideMargin + panelW;
            double totalH = frameH + WindowTopMargin + WindowBottomMargin;
            if (_largeScreenMode)
                totalH = Math.Max(totalH, 500);

            Width = totalW;
            Height = totalH;

            // 用窗口区域把整窗裁成与视频同半径的圆角，使窗口外轮廓与视频圆角同心，
            // 消除"窗口圆角(系统)与视频圆角不一致"的错位黑边/深色环。
            ApplyWindowRegion();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ApplyFrameLayout error: {ex.Message}");
        }
        finally
        {
            CenterWindow();
        }
    }

    /// <summary>
    /// 把视频层裁成与屏幕容器一致的圆角矩形。
    /// Border 的 CornerRadius 不保证裁剪子内容，必须显式 Clip；
    /// 且要在尺寸变化时重算 Rect，否则圆角不跟随、四角出现直角残角。
    /// </summary>
    private void UpdateScreenClip()
    {
        try
        {
            if (ScreenClipHost == null) return;

            double w = ScreenClipHost.ActualWidth;
            double h = ScreenClipHost.ActualHeight;
            if (w <= 0 || h <= 0) return;

            var visual = ElementCompositionPreview.GetElementVisual(ScreenClipHost);
            var compositor = visual.Compositor;

            if (_screenClipGeometry == null)
            {
                _screenClipGeometry = compositor.CreateRoundedRectangleGeometry();
                // 几何体需用 CreateGeometricClip 包装后才能赋给 Visual.Clip
                visual.Clip = compositor.CreateGeometricClip(_screenClipGeometry);
            }

            // 尺寸与半径都由布局驱动，尺寸变化时同步更新，保证圆角始终跟随容器
            _screenClipGeometry.Size = new Vector2((float)w, (float)h);
            _screenClipGeometry.CornerRadius = new Vector2((float)_screenRadius, (float)_screenRadius);
        }
        catch { }
    }

    private void ScreenClipHost_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateScreenClip();

    /// <summary>
    /// 灵动岛/刘海挖孔：仅竖屏显示（横竖屏翻转后挖孔物理位置不再位于顶边中央），
    /// 几何按 iOS 真机比例（宽 ≈ 30% 屏宽、高 ≈ 4% 屏高、顶部 ≈ 1.3% 屏高），与镜像内容中的挖孔对齐。
    /// </summary>
    private void ApplyNotch(double screenW, double screenH)
    {
        bool portrait = screenH >= screenW;
        if (_profile.Notch == NotchKind.None || !portrait ||
            _profile.IslandWidthRatio <= 0 || _profile.IslandHeightRatio <= 0)
        {
            Notch.Visibility = Visibility.Collapsed;
            return;
        }

        Notch.Visibility = Visibility.Visible;
        double w = screenW * _profile.IslandWidthRatio;
        double h = Math.Max(9, screenH * _profile.IslandHeightRatio);

        Notch.Width = w;
        Notch.Height = h;

        if (_profile.Notch == NotchKind.Notch)
        {
            // 老款刘海：紧贴屏幕顶边，只有下方两角是圆的（上边平齐）
            Notch.CornerRadius = new CornerRadius(0, 0, h / 2, h / 2);
            Notch.Margin = new Thickness(0);
        }
        else
        {
            // 灵动岛：四周全圆的胶囊，按真机距顶比例内缩
            Notch.CornerRadius = new CornerRadius(h / 2);
            Notch.Margin = new Thickness(0, Math.Max(3, screenH * _profile.IslandTopRatio), 0, 0);
        }
    }

    private static double GetDpiScale()
    {
        try
        {
            if (ControlWindow.ControlWindowXamlRoot is not null)
                return ControlWindow.ControlWindowXamlRoot.RasterizationScale;
        }
        catch { }

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
            _frameSize = size;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                // Canvas 像素尺寸与朝向重解析都在 UI 线程进行
                Canvas.Width = size.Width;
                Canvas.Height = size.Height;

                _profile = DeviceFrameProfileResolver.Resolve(
                    Session.DeviceModel, (uint)size.Width, (uint)size.Height);

                // 迷你形态下不重排窗口，否则会把迷你条撑回完整尺寸
                if (!_compactMode)
                    ApplyFrameLayout(size);

                // 尺寸变化后强制重绘，避免旧位图残留
                if (_canvasDevice != null)
                    Canvas.Invalidate();
            }
            catch { }
        });
    }

    /// <summary>
    /// 校正窗口记录的帧尺寸为解码器实际输出尺寸。
    /// 协议声明尺寸（FrameSizeChanged）与解码实际输出不一致时，
    /// expectedSize 检查会丢弃所有帧导致无画面，此方法按实际值同步。
    /// </summary>
    public void EnsureFrameSize(int width, int height)
    {
        if (width <= 0 || height <= 0) return;

        lock (_bitmapLock)
        {
            if ((int)_frameSize.Width == width && (int)_frameSize.Height == height)
                return;

            Log.Information("[MirrorWindow] Frame size corrected: protocol {PW}x{PH} -> decoded {W}x{H}",
                _frameSize.Width, _frameSize.Height, width, height);
            _frameSize = new Size(width, height);
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                Canvas.Width = width;
                Canvas.Height = height;
                Canvas.Invalidate();
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

    /// <summary>
    /// 把窗口裁成与视频同半径的圆角矩形（SetWindowRgn 直接改变窗口形状，
    /// 因此窗口外轮廓的圆角半径 = 视频裁切半径，二者同心，不会再出现
    /// "系统窗口圆角(≈11px) 与 视频圆角(较大) 错位" 导致的深色环/黑边）。
    /// </summary>
    private void ApplyWindowRegion()
    {
        try
        {
            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            // SetWindowRgn 使用设备像素（屏幕坐标），需按 DPI 把 DIP 尺寸/半径换算。
            double dipScale = GetDpiScale();
            int w = (int)Math.Round(Width * dipScale);
            int h = (int)Math.Round(Height * dipScale);
            if (w <= 0 || h <= 0) return;

            int r = (int)Math.Round(_screenRadius * dipScale);
            // 直径不得超过较短边，否则椭圆参数非法
            int ellipse = Math.Min(2 * r, Math.Min(w, h));

            IntPtr hRgn = CreateRoundRectRgn(0, 0, w, h, ellipse, ellipse);
            if (hRgn != IntPtr.Zero)
            {
                SetWindowRgn(hwnd, hRgn, true);
            }
        }
        catch { }
    }

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int nLeft, int nTop, int nRight, int nBottom, int nWidthEllipse, int nHeightEllipse);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private const int GWL_STYLE = -16;
    private const uint WS_CAPTION = 0x00C00000;
    private const uint WS_THICKFRAME = 0x00040000;
    private const uint WS_BORDER = 0x00800000;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;

    /// <summary>
    /// 彻底移除窗口系统边框（WS_CAPTION / WS_THICKFRAME / WS_BORDER 全部去掉），
    /// 这样 DWM 没有可绘制的非客户区，就从根上不存在白边。
    /// 副作用：没有标题栏，因此拖拽改为在 RootGrid 上手动实现（见 RootGrid_Pointer*）。
    /// </summary>
    private void MakeWindowBorderless()
    {
        IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        int style = GetWindowLong(hwnd, GWL_STYLE);
        style &= ~(int)(WS_CAPTION | WS_THICKFRAME | WS_BORDER);
        SetWindowLong(hwnd, GWL_STYLE, style);
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
    }

    /// <summary>清除窗口圆角区域（还原为矩形），用于全屏等场景。</summary>
    private void ClearWindowRegion()
    {
        try
        {
            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            SetWindowRgn(hwnd, IntPtr.Zero, true);
        }
        catch { }
    }

    public void OnFrameDataReceived(byte[] frameData)
    {
        // 入口即判：窗口已销毁或 Canvas 尚未就绪时，仅计数并安全归还，
        // 不做任何 UI 元素访问（本方法可能运行在解码后台线程）。
        if (_isDisposed)
        {
            ArrayPool<byte>.Shared.Return(frameData);
            return;
        }

        Interlocked.Increment(ref _decodedFrames);

        // 用 Interlocked 抢占渲染权，避免并发帧叠加；抢不到则丢帧计数。
        if (Interlocked.Exchange(ref _isRendering, 1) == 1)
        {
            Interlocked.Increment(ref _droppedFrames);
            ArrayPool<byte>.Shared.Return(frameData);
            return;
        }

        // 所有 UI 元素访问一律切到 UI 线程执行。
        bool enqueued = DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                if (_isDisposed || _canvasDevice == null)
                    return;

                // 最小化或迷你形态时暂停渲染，不更新位图。
                if (this.WindowState == WindowState.Minimized || _compactMode)
                {
                    Canvas.Paused = true;
                    return;
                }

                lock (_bitmapLock)
                {
                    int expectedSize = _frameSize.Width * _frameSize.Height * 4; // BGRA = 4 bytes/pixel
                    if (frameData.Length < expectedSize)
                    {
                        Log.Warning("[MirrorWindow] Frame size mismatch: data={Data}, expected={Expected}, frame={W}x{H} — 帧被丢弃",
                            frameData.Length, expectedSize, _frameSize.Width, _frameSize.Height);
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
                        Log.Information("[MirrorWindow] First bitmap created ({W}x{H}, {Bytes} bytes)",
                            _frameSize.Width, _frameSize.Height, frameData.Length);
                    }
                    else
                    {
                        _currentBitmap.SetPixelBytes(frameData);
                    }
                }

                // 迷你形态下保持暂停（画面已隐藏），正常形态才恢复渲染
                if (!_compactMode)
                {
                    if (Canvas.Paused) Canvas.Paused = false;
                    Canvas.Invalidate();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[MirrorWindow] OnFrameDataReceived UI dispatch error");
            }
            finally
            {
                Interlocked.Exchange(ref _isRendering, 0);
                ArrayPool<byte>.Shared.Return(frameData);
            }
        });

        // TryEnqueue 失败（调度器已关闭）：归还缓冲区并复位渲染标志。
        if (!enqueued)
        {
            Interlocked.Exchange(ref _isRendering, 0);
            ArrayPool<byte>.Shared.Return(frameData);
        }
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
        // 先置位，阻止后续 OnFrameDataReceived 继续入队/渲染
        _isDisposed = true;

        _timer.Stop();
        _timer.Dispose();

        // 先关掉悬浮层，避免窗口销毁后 Popup 仍残留
        try { ControlCapsule.IsOpen = false; }
        catch { }

        // 暂停并断开渲染控件，释放位图与设备引用
        try
        {
            Canvas?.RemoveFromVisualTree();
        }
        catch { }

        lock (_bitmapLock)
        {
            _currentBitmap?.Dispose();
            _currentBitmap = null;
        }

        _canvasDevice = null;
        GC.Collect();

        Log.Information("[MirrorWindow] Closed and resources released for {Device}", Session.DeviceDisplayName);
    }

    private bool _firstDrawLogged = false;

    private void Canvas_Draw(ICanvasAnimatedControl sender, CanvasAnimatedDrawEventArgs args)
    {
        if (_isDisposed) return;

        lock (_bitmapLock)
        {
            if (_currentBitmap != null)
            {
                if (!_firstDrawLogged)
                {
                    _firstDrawLogged = true;
                    Log.Information("[MirrorWindow] First Draw executing — 渲染链路已打通");
                }
                args.DrawingSession.DrawImage(_currentBitmap);
            }
        }
    }

    private void Canvas_CreateResources(CanvasAnimatedControl sender, CanvasCreateResourcesEventArgs args)
    {
        _canvasDevice = sender.Device;

        // 设备就绪后立即解除暂停并请求重绘，确保首帧能显示（此前 Paused=True 会吞掉首帧）
        try
        {
            sender.Paused = false;
            sender.Invalidate();
        }
        catch { }

        Log.Information("[MirrorWindow] CreateResources done (device={HasDevice}, size={W}x{H})",
            _canvasDevice != null, _frameSize.Width, _frameSize.Height);
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

    /// <summary>x:Bind 函数绑定：音量显示取整（避免出现 93.75 这类原始值）</summary>
    public string FormatVolume(double value) => Math.Round(value).ToString();

    // —— 迷你悬浮形态（托盘形态）：缩为顶部迷你控制条，贴屏幕右上角，置顶显示 ——

    private bool _compactMode = false;

    // 手动拖拽（窗口已无标题栏）。
    // 关键：不使用相对 RootGrid 的指针坐标（窗口移动会改变该相对值，形成"越移越偏"的反馈环 → 抖动）。
    // 改为读取屏幕绝对光标坐标(GetCursorPos)，按下时记录"光标-窗口左上角"的固定偏移，
    // 移动时直接 windowPos = 光标绝对坐标 + 固定偏移，与窗口当前位置完全解耦，不再抖动。
    private bool _dragging = false;
    private global::Windows.Graphics.PointInt32 _dragOffset;   // 窗口左上角 - 光标绝对坐标（按下时固定）

    private void RootGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            if (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Touch)
                return; // 触摸交给 ScrollViewer/缩放等原生手势，不触发窗口拖拽

            if (GetCursorPos(out POINT cur))
            {
                _dragOffset = new global::Windows.Graphics.PointInt32(
                    this.AppWindow.Position.X - cur.X,
                    this.AppWindow.Position.Y - cur.Y);
                _dragging = true;
                // 注意：不 CapturePointer。我们用 GetCursorPos 读取屏幕绝对坐标，与可视化树无关，
                // 无需捕获；捕获反而可能抢占控制条按钮(Popup)的点击，因此刻意不捕获。
            }
        }
        catch { }
    }

    private void RootGrid_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) return;
        try
        {
            if (GetCursorPos(out POINT cur))
            {
                int nx = cur.X + _dragOffset.X;
                int ny = cur.Y + _dragOffset.Y;
                this.AppWindow.Move(new global::Windows.Graphics.PointInt32(nx, ny));
            }
        }
        catch { }
    }

    private void RootGrid_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _dragging = false;
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => ToggleCompactMode();

    /// <summary>
    /// 切换迷你悬浮形态：
    /// 进入 —— 隐藏设备画面区与左侧面板，窗口缩为约 320×64 的迷你控制条，
    ///         始终置顶并停靠在主显示器工作区右上角（最小化按钮变为"还原"图标）。
    /// 退出 —— 恢复设备画面、取消置顶、按当前帧尺寸重新布局窗口。
    /// </summary>
    private void ToggleCompactMode()
    {
        _compactMode = !_compactMode;

        try
        {
            if (_compactMode)
            {
                // 收起画面区与左侧面板
                DeviceAreaGrid.Visibility = Visibility.Collapsed;
                LeftPanelColumn.Width = new GridLength(0);
                LeftControlPanel.Visibility = Visibility.Collapsed;
                this.IsAlwaysOnTop = true;

                Width = 320;
                Height = 64;

                // 迷你条也裁成圆角（与视频同心的小圆角）
                ApplyWindowRegion();

                // 停靠主显示器工作区右上角。
                // 注意：Width/Height 是 DIP，AppWindow.Move 用物理像素，必须按 DPI 缩放换算。
                var area = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(
                    this.AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
                double dipScale = GetDpiScale();
                int wPx = (int)Math.Round(Width * dipScale);
                int x = area.WorkArea.X + area.WorkArea.Width - wPx - 16;
                int y = area.WorkArea.Y + 16;
                this.AppWindow.Move(new global::Windows.Graphics.PointInt32(Math.Max(0, x), Math.Max(0, y)));

                // 迷你形态下隐藏与画面相关的按钮，只保留还原/截图/断开
                FullScreenButton.Visibility = Visibility.Collapsed;
                LargeScreenButton.Visibility = Visibility.Collapsed;

                // 画面已隐藏，暂停渲染循环，避免空转消耗 GPU
                Canvas.Paused = true;

                MinimizeIcon.Glyph = "\uE923";   // 还原图标
            }
            else
            {
                DeviceAreaGrid.Visibility = Visibility.Visible;
                FullScreenButton.Visibility = Visibility.Visible;
                LargeScreenButton.Visibility = Visibility.Visible;
                this.IsAlwaysOnTop = false;
                MinimizeIcon.Glyph = "\uE921";   // 最小化图标

                ApplyFrameLayout(_frameSize);   // 按当前帧尺寸恢复完整布局

                Canvas.Paused = false;
                Canvas.Invalidate();
            }

            Log.Information("[MirrorWindow] Compact mode {State}", _compactMode ? "ON (迷你悬浮条)" : "OFF (完整窗口)");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[MirrorWindow] ToggleCompactMode failed");
        }
    }

    private void FullScreenButton_Click(object sender, RoutedEventArgs e)
    {
        _isFullScreen = !_isFullScreen;
        // 屏幕区已按真实宽高比 letterbox，全屏下保持 Uniform 避免裁切
        VideoViewbox.Stretch = Stretch.Uniform;

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

        // 全屏时取消窗口圆角区域（铺满）；退出全屏时按当前尺寸重新裁圆角
        if (_isFullScreen)
            ClearWindowRegion();
        else
            ApplyWindowRegion();
    }

    private void LargeScreenButton_Click(object sender, RoutedEventArgs e)
    {
        _largeScreenMode = !_largeScreenMode;
        LeftPanelColumn.Width = new GridLength(_largeScreenMode ? LeftControlPanelWidth : 0);
        LeftControlPanel.Visibility = _largeScreenMode ? Visibility.Visible : Visibility.Collapsed;

        try
        {
            LargeScreenButton.Background = _largeScreenMode
                ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentFillColorDefaultBrush"]
                : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
        catch { }

        ApplyFrameLayout(_frameSize);
    }

    private void ControlButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button &&
            Enum.TryParse<MediaControlCommand>(button.Tag?.ToString(), out var command))
        {
            _smtcControlService.SendMediaControlCommand(command);
        }
    }

    private async void ScreenshotButton_Click(object sender, RoutedEventArgs e) => await SaveScreenshotAsync();

    private async Task SaveScreenshotAsync()
    {
        try
        {
            CanvasBitmap? snapshot;
            lock (_bitmapLock)
            {
                snapshot = _currentBitmap;
            }

            if (snapshot == null)
            {
                await ShowInfoAsync("截图失败", "当前没有可截取的画面，请先开始镜像。");
                return;
            }

            var folder = await KnownFolders.PicturesLibrary.CreateFolderAsync(
                "AirPlay", CreationCollisionOption.OpenIfExists);
            var file = await folder.CreateFileAsync(
                $"AirPlay_{DateTime.Now:yyyyMMdd_HHmmss}.png",
                CreationCollisionOption.ReplaceExisting);

            using (IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.ReadWrite))
            {
                await snapshot.SaveAsync(stream, CanvasBitmapFileFormat.Png);
            }

            await ShowInfoAsync("截图已保存", $"已保存到图片库：\n{file.Path}");
        }
        catch (Exception ex)
        {
            await ShowInfoAsync("截图失败", ex.Message);
        }
    }

    private async Task ShowInfoAsync(string title, string message)
    {
        try
        {
            InfoDialog.Title = title;
            InfoDialogText.Text = message;
            await InfoDialog.ShowAsync();
        }
        catch { }
    }

    private async void CloseButton_Click(object sender, RoutedEventArgs e) => await ConfirmDialog.ShowAsync();

    private void ConfirmDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args) => Session.Disconnect();

    private void Grid_Unloaded(object sender, RoutedEventArgs e)
    {
        // 仅断开渲染控件与可视化树；不要再置 null 破坏其它成员对 Canvas 的引用。
        // 资源释放统一交给 OnWindowClosed。
        try
        {
            this.Canvas.RemoveFromVisualTree();
        }
        catch { }
    }
}
