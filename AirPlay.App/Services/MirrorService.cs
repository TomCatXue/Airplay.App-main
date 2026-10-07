using AirPlay.App.FFmmpeg;
using AirPlay.App.Windows;
using AirPlay.Core2.Models;
using AirPlay.Core2.Services;
using Microsoft.Extensions.Hosting;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using WinUIEx;

namespace AirPlay.App.Services;

internal class MirrorService(SessionManager sessionManager) : IHostedService
{
    private readonly ConcurrentDictionary<DeviceSession, H264Decoder> _mirroringDecodes = [];
    private readonly ConcurrentDictionary<DeviceSession, MirrorWindow> _mirroringWindows = [];
    private readonly ConcurrentDictionary<DeviceSession, CancellationTokenSource> _pendingCloses = [];

    // 数据流统计（用于文件日志定位断点）
    private long _decodeOkFrames = 0;
    private long _decodeFailFrames = 0;
    private long _noWindowFrames = 0;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        sessionManager.SessionCreated += (_, session) =>
        {
            session.MirrorControllerCreated += (_, _) =>
            {
                var controller = session.MirrorController;
                if (controller is null) return;

                Log.Information("[MirrorService] Controller created, wiring data pipeline for [{Device}]", session.DeviceDisplayName);

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
                    // 解码器初始化失败：写文件日志（MessageBox 在非 UI 线程调用本身有崩溃风险，弃用）
                    Log.Fatal(ex, "[MirrorService] H264Decoder creation failed. FFmpeg path: {BaseDir}", AppContext.BaseDirectory);
                }

                if (decoder != null)
                {
                    controller.H264DataReceived += (_, e) =>
                    {
                        try
                        {
                            if (decoder.Decode(e.Data, out var rgbData, out var width, out var height))
                            {
                                // 关键泄漏修复：窗口尚未创建/已销毁时必须归还 ArrayPool 缓冲区，
                                // 否则 30fps 下每帧数 MB 的缓冲区永不回收 → OOM → 闪退。
                                if (mirrorWindow is null)
                                {
                                    Interlocked.Increment(ref _noWindowFrames);
                                    ArrayPoolReturn(rgbData);
                                    return;
                                }

                                Interlocked.Increment(ref _decodeOkFrames);

                                // 尺寸同步：解码器实际输出尺寸若与协议声明尺寸（FrameSizeChanged）不一致，
                                // MirrorWindow 的 expectedSize 检查会丢弃所有帧 → 永远无画面。此处按解码实际值校正。
                                mirrorWindow.EnsureFrameSize(width, height);

                                mirrorWindow.OnFrameDataReceived(rgbData);

                                // 每 300 帧记录一次数据流统计（定位"画面不显示"断点用）
                                if (Interlocked.Read(ref _decodeOkFrames) % 300 == 1)
                                    Log.Information("[MirrorService] Decode stats: ok={Ok}, fail={Fail}, noWindow={NoWindow}, last={W}x{H}",
                                        _decodeOkFrames, _decodeFailFrames, _noWindowFrames, width, height);
                            }
                            else
                            {
                                Interlocked.Increment(ref _decodeFailFrames);
                                if (Interlocked.Read(ref _decodeFailFrames) % 300 == 1)
                                    Log.Information("[MirrorService] Decode failed count={Fail} (ok={Ok}) — 解码器未输出帧（可能仍在缓冲或数据异常）",
                                        _decodeFailFrames, _decodeOkFrames);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "[MirrorService] Decode/relay error");
                        }
                    };
                }

                controller.FrameSizeChanged += (_, e) =>
                {
                    Log.Information("[MirrorService] FrameSizeChanged: {W}x{H} (window exists: {HasWindow})",
                        e.Width, e.Height, mirrorWindow != null);

                    if (_mirroringWindows.TryGetValue(session, out mirrorWindow))
                    {
                        App.DispatcherQueue.TryEnqueue(() =>
                        {
                            try { mirrorWindow.OnFrameSizeChanged(e); }
                            catch (Exception ex) { Log.Error(ex, "[MirrorService] OnFrameSizeChanged error"); }
                        });
                        return;
                    }

                    App.DispatcherQueue.TryEnqueue(() =>
                    {
                        try
                        {
                            mirrorWindow = new(session, e);
                            mirrorWindow.Show();
                            _mirroringWindows.TryAdd(session, mirrorWindow);
                            Log.Information("[MirrorService] MirrorWindow created and shown ({W}x{H})", e.Width, e.Height);
                        }
                        catch (Exception ex)
                        {
                            // 此回调此前无异常保护：窗口构造/Show 抛出的异常会成为
                            // XAML stowed exception (0xC000027B) 直接闪退。现在记录并吞掉。
                            Log.Fatal(ex, "[MirrorService] MirrorWindow creation/show FAILED");
                            mirrorWindow = null;
                        }
                    });
                };

                if (decoder != null)
                    _mirroringDecodes.TryAdd(session, decoder);
            };

            session.MirrorControllerClosed += (_, _) =>
            {
                Log.Information("[MirrorService] MirrorController closed for [{Device}]", session.DeviceDisplayName);

                // 延迟关闭窗口：给新 MirrorController 2 秒时间创建（视频模式切换不关闭）
                var closeCts = new CancellationTokenSource();
                _pendingCloses.TryAdd(session, closeCts);

                _ = Task.Delay(2000, closeCts.Token).ContinueWith(_ =>
                {
                    if (!closeCts.Token.IsCancellationRequested)
                    {
                        _pendingCloses.TryRemove(session, out CancellationTokenSource? removed);
                        if (_mirroringWindows.TryRemove(session, out var mirrorWindow))
                        {
                            App.DispatcherQueue.TryEnqueue(() =>
                            {
                                try { mirrorWindow.Close(); }
                                catch (Exception ex) { Log.Warning(ex, "[MirrorService] Delayed window close failed (可能已手动关闭)"); }
                            });
                        }
                    }
                }, TaskContinuationOptions.NotOnCanceled);

                if (_mirroringDecodes.TryRemove(session, out var decoder))
                {
                    try { decoder.Dispose(); }
                    catch (Exception ex) { Log.Warning(ex, "[MirrorService] Decoder dispose failed"); }
                }
            };
        };

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private static void ArrayPoolReturn(byte[] buffer)
    {
        try { System.Buffers.ArrayPool<byte>.Shared.Return(buffer); }
        catch { }
    }
}
