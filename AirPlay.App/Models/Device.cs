using AirPlay.Core2.Extensions;
using AirPlay.Core2.Models;
using AirPlay.Core2.Models.Messages;
using AirPlay.Core2.Models.Messages.Audio;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media;

namespace AirPlay.App.Models;

public partial class Device : ObservableObject
{
    private static readonly HttpClient _httpClient = new();
    private readonly Action<double> _setVolumeAction;

    public DeviceSession Session { get; private set; }

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

    [ObservableProperty]
    public partial bool ShowVolumeIcon { get; set; }

    [ObservableProperty]
    public partial bool ShowMirrorIcon { get; set; }

    [ObservableProperty]
    public partial BitmapImage? Cover { get; set; }

    [ObservableProperty]
    public partial string? PlayingItemName { get; set; }

    [ObservableProperty]
    public partial string? Artist { get; set; }

    [ObservableProperty]
    public partial string? Album { get; set; }

    [ObservableProperty]
    public partial MediaProgressInfo? ProgressInfo { get; set; }

    [ObservableProperty]
    public partial bool EnableControl { get; set; }

    [ObservableProperty]
    public partial string PlayPauseIcon { get; set; } = "\uf5b0";

    [ObservableProperty]
    public partial string PlayPauseTag { get; set; } = "Play";

    [ObservableProperty]
    public partial double Volume { get; set; }

    /// <summary>镜像会话的投屏分辨率（如 740×1600），无曲目元数据时显示在卡片第二行。</summary>
    [ObservableProperty]
    public partial string? MirrorResolution { get; set; }

    /// <summary>卡片第二行：有曲目元数据显示艺术家；镜像会话无元数据时显示投屏分辨率。</summary>
    public string? DisplaySubtitle => !string.IsNullOrEmpty(Artist) ? Artist : MirrorResolution;

    /// <summary>音量整数文本，供 UI 绑定显示（避免 x:Bind 函数绑定可空链崩溃）。</summary>
    public string VolumeText => Math.Round(Volume).ToString();

    [ObservableProperty]
    public partial MediaPlaybackStatus PlaybackStatus { get; set; }

    public Device(DeviceSession deviceSession)
    {
        Session = deviceSession;
        EnableControl = deviceSession.DacpServiceEndPoint != null;
        // 镜像会话拿不到曲目元数据（iOS 投屏时不下发），显示"正在投屏"
        PlayingItemName = deviceSession.IsMirrorSession ? "正在投屏" : "Audio";
        Volume = deviceSession.Volume;

        deviceSession.AudioControllerCreated += OnAudioControllerCreated;

        deviceSession.MirrorControllerCreated += OnMirrorControllerCreated;
        deviceSession.MirrorControllerClosed += OnMirrorControllerClosed;

        deviceSession.MediaProgressInfoReceived += OnMediaProgressInfoReceived;
        deviceSession.MediaWorkInfoReceived += OnMediaWorkInfoReceived;
        deviceSession.MediaCoverReceived += OnMediaCoverReceived;
        deviceSession.RemoteSetVolumeRequest += OnRemoteSetVolumeRequest;

        deviceSession.DacpServiceFound += OnDacpServiceFound;

        _setVolumeAction = (arg) => _ = Session.SetVolumeAsync(arg, _httpClient);
        _setVolumeAction = _setVolumeAction.Debounce(500);
    }

    partial void OnVolumeChanged(double value)
    {
        if (Session.Volume == value) return;
        _setVolumeAction(value);
        OnPropertyChanged(nameof(VolumeText));
    }

    partial void OnArtistChanged(string? value) => OnPropertyChanged(nameof(DisplaySubtitle));

    private void OnMirrorControllerCreated(object? sender, EventArgs e) => App.DispatcherQueue.TryEnqueue(() =>
    {
        ShowMirrorIcon = true;

        // 订阅投屏分辨率变化，无曲目元数据时卡片显示"投屏中 740×1600"
        if (Session.MirrorController is { } mirrorController)
        {
            mirrorController.FrameSizeChanged += OnMirrorFrameSizeChanged;
            if (mirrorController.FrameSize is { } fs)
                UpdateMirrorResolution(fs);
        }
    });

    private void OnMirrorFrameSizeChanged(object? sender, System.Drawing.Size e) =>
        App.DispatcherQueue.TryEnqueue(() => UpdateMirrorResolution(e));

    private void UpdateMirrorResolution(System.Drawing.Size size)
    {
        MirrorResolution = $"{size.Width}×{size.Height}";
        OnPropertyChanged(nameof(DisplaySubtitle));
    }

    private void OnMirrorControllerClosed(object? sender, EventArgs e) => App.DispatcherQueue.TryEnqueue(() => ShowMirrorIcon = false);

    private void OnDacpServiceFound(object? sender, EventArgs e) => App.DispatcherQueue.TryEnqueue(() => EnableControl = true);

    private void OnAudioControllerCreated(object? sender, EventArgs e)
    {
        DateTime? lastReceiveData = null;

        Session.AudioController?.AudioDataReceived += (sender, e) =>
        {
            lastReceiveData = DateTime.Now;
            bool value = e.Data.Any(b => b != 0);

            // 所有绑定属性与 SMTC 相关状态都必须在 UI 线程更新，
            // 否则 SmtcControlService 在后台线程设置 SystemMediaTransportControls
            // 会抛出跨线程 COMException。
            App.DispatcherQueue.TryEnqueue(() =>
            {
                PlaybackStatus = value
                    ? MediaPlaybackStatus.Playing
                    : MediaPlaybackStatus.Paused;

                if (ShowVolumeIcon != value)
                {
                    ShowVolumeIcon = value;
                    PlayPauseTag = value ? "Pause" : "Play";
                    PlayPauseIcon = value ? "\uf8ae" : "\uf5b0";
                }
            });
        };

        if (Session.IsMirrorSession)
        {
            TimeSpan timeSpan = TimeSpan.FromSeconds(0.5);
            CancellationTokenSource cancellationTokenSource = new();

            void OnAudioControllerClosed(object? sender, EventArgs e)
            {
                cancellationTokenSource.Cancel();
                Session.AudioControllerClosed -= OnAudioControllerClosed;
            }

            Session.AudioControllerClosed += OnAudioControllerClosed;

            Task.Run(async () =>
            {
                while (!cancellationTokenSource.IsCancellationRequested && Session.AudioController != null)
                {
                    await Task.Delay(timeSpan, cancellationTokenSource.Token);

                    if (lastReceiveData != null && DateTime.Now - lastReceiveData > timeSpan && ShowVolumeIcon != false)
                    {
                        App.DispatcherQueue.TryEnqueue(() =>
                        {
                            PlaybackStatus = MediaPlaybackStatus.Paused;
                            ShowVolumeIcon = false;
                            PlayPauseTag = "Play";
                            PlayPauseIcon = "\uf5b0";
                        });
                    }
                }
            }, cancellationTokenSource.Token);
        }
    }

    private void OnRemoteSetVolumeRequest(object? sender, double e) => App.DispatcherQueue.TryEnqueue(() => Volume = e);

    private void OnMediaProgressInfoReceived(object? sender, MediaProgressInfo e) => App.DispatcherQueue.TryEnqueue(() => ProgressInfo = e);

    private void OnMediaWorkInfoReceived(object? sender, MediaWorkInfo e) => App.DispatcherQueue.TryEnqueue(() =>
    {
        PlayingItemName = e.Name;
        Artist = e.Artist;
        Album = e.Album;
    });

    private void OnMediaCoverReceived(object? sender, byte[] e)
    {
        App.DispatcherQueue.TryEnqueue(() =>
        {
            using MemoryStream memoryStream = new(e);

            BitmapImage bitmapImage = new();
            bitmapImage.SetSource(memoryStream.AsRandomAccessStream());
            Cover = bitmapImage;
        });
    }
}
