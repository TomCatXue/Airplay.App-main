using AirPlay.App.FFmmpeg;
using AirPlay.App.Windows;
using AirPlay.Core2.Models;
using AirPlay.Core2.Services;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using WinUIEx;

namespace AirPlay.App.Services;

internal class MirrorService(SessionManager sessionManager) : IHostedService
{
    private readonly ConcurrentDictionary<DeviceSession, H264Decoder> _mirroringDecodes = [];
    private readonly ConcurrentDictionary<DeviceSession, MirrorWindow> _mirroringWindows = [];
    private readonly ConcurrentDictionary<DeviceSession, CancellationTokenSource> _pendingCloses = [];

    public Task StartAsync(CancellationToken cancellationToken)
    {
        sessionManager.SessionCreated += (_, session) =>
        {
            session.MirrorControllerCreated += (_, _) =>
            {
                // 取消该 session 的待关闭计时器（视频模式切换时不关闭窗口）
                if (_pendingCloses.TryRemove(session, out var cts))
                    cts.Cancel();

                MirrorWindow? mirrorWindow = null;
                H264Decoder? decoder = null;

                // 尝试复用已有窗口
                _mirroringWindows.TryGetValue(session, out mirrorWindow);

                try
                {
                    decoder = new H264Decoder();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MirrorService] H264Decoder creation failed: {ex}");
                    System.Windows.Forms.MessageBox.Show(
                        $"H264 解码器初始化失败:\n{ex.Message}\n\n" +
                        $"详细信息:\n{ex}\n\n" +
                        $"FFmpeg 路径: {AppContext.BaseDirectory}\n" +
                        $"请确认该目录存在 ARM64 的 avcodec-62.dll 等文件。",
                        "AirPlay 错误",
                        System.Windows.Forms.MessageBoxButtons.OK,
                        System.Windows.Forms.MessageBoxIcon.Error);
                }

                if (decoder != null)
                {
                    session.MirrorController!.H264DataReceived += (_, e) =>
                    {
                        try
                        {
                            if (decoder.Decode(e.Data, out var rgbData, out var width, out var height))
                                mirrorWindow?.OnFrameDataReceived(rgbData);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[MirrorService] Decode error: {ex.Message}");
                        }
                    };
                }

                session.MirrorController!.FrameSizeChanged += (_, e) =>
                {
                    Debug.WriteLine($"FrameSizeChanged: {e.Width}x{e.Height}");

                    if (_mirroringWindows.TryGetValue(session, out mirrorWindow))
                    {
                        App.DispatcherQueue.TryEnqueue(() => mirrorWindow.OnFrameSizeChanged(e));
                        return;
                    }

                    App.DispatcherQueue.TryEnqueue(() =>
                    {
                        mirrorWindow = new(session, e);
                        mirrorWindow.Show();

                        _mirroringWindows.TryAdd(session, mirrorWindow);
                    });
                };

                if (decoder != null)
                    _mirroringDecodes.TryAdd(session, decoder);
            };

            session.MirrorControllerClosed += (_, _) =>
            {
                // 延迟关闭窗口：给新 MirrorController 2 秒时间创建
                // 如果视频模式切换，新控制器会取消此计时器
                var closeCts = new CancellationTokenSource();
                _pendingCloses.TryAdd(session, closeCts);

                _ = Task.Delay(2000, closeCts.Token).ContinueWith(_ =>
                {
                    if (!closeCts.Token.IsCancellationRequested)
                    {
                        _pendingCloses.TryRemove(session, out CancellationTokenSource? removed);
                        if (_mirroringWindows.TryRemove(session, out var mirrorWindow))
                            App.DispatcherQueue.TryEnqueue(() => mirrorWindow.Close());
                    }
                }, TaskContinuationOptions.NotOnCanceled);

                if (_mirroringDecodes.TryRemove(session, out var decoder))
                    decoder.Dispose();
            };
        };

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
