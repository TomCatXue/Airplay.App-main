using FFmpeg.AutoGen.Abstractions;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace AirPlay.App.FFmmpeg;

/// <summary>
/// 手动加载 FFmpeg 函数，绕过 DynamicallyLoadedBindings 在 ARM64 MSIX 中的兼容性问题。
/// </summary>
internal static unsafe class NativeFFmpeg
{
    // ── avcodec ──────────────────────────────────────
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate AVCodec* avcodec_find_decoder_t(AVCodecID id);
    public static avcodec_find_decoder_t avcodec_find_decoder = null!;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate AVCodecContext* avcodec_alloc_context3_t(AVCodec* codec);
    public static avcodec_alloc_context3_t avcodec_alloc_context3 = null!;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int avcodec_open2_t(AVCodecContext* ctx, AVCodec* codec, AVDictionary** options);
    public static avcodec_open2_t avcodec_open2 = null!;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int avcodec_send_packet_t(AVCodecContext* ctx, AVPacket* pkt);
    public static avcodec_send_packet_t avcodec_send_packet = null!;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int avcodec_receive_frame_t(AVCodecContext* ctx, AVFrame* frame);
    public static avcodec_receive_frame_t avcodec_receive_frame = null!;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void avcodec_free_context_t(AVCodecContext** ctx);
    public static avcodec_free_context_t avcodec_free_context = null!;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void avcodec_flush_buffers_t(AVCodecContext* ctx);
    public static avcodec_flush_buffers_t avcodec_flush_buffers = null!;

    // ── avutil ──────────────────────────────────────
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate AVFrame* av_frame_alloc_t();
    public static av_frame_alloc_t av_frame_alloc = null!;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void av_frame_free_t(AVFrame** frame);
    public static av_frame_free_t av_frame_free = null!;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate AVPacket* av_packet_alloc_t();
    public static av_packet_alloc_t av_packet_alloc = null!;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void av_packet_free_t(AVPacket** pkt);
    public static av_packet_free_t av_packet_free = null!;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void av_packet_unref_t(AVPacket* pkt);
    public static av_packet_unref_t av_packet_unref = null!;

    // ── swscale ──────────────────────────────────────
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate SwsContext* sws_getContext_t(int srcW, int srcH, AVPixelFormat srcFmt, int dstW, int dstH, AVPixelFormat dstFmt, int flags, SwsFilter* srcFilter, SwsFilter* dstFilter, double* param);
    public static sws_getContext_t sws_getContext = null!;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int sws_scale_t(SwsContext* ctx, byte*[] srcSlice, int[] srcStride, int srcSliceY, int srcSliceH, byte*[] dst, int[] dstStride);
    public static sws_scale_t sws_scale = null!;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void sws_freeContext_t(SwsContext* ctx);
    public static sws_freeContext_t sws_freeContext = null!;

    // ── 常量 ─────────────────────────────────────────
    public const int SWS_FAST_BILINEAR = 1;

    private static bool _initialized;

    /// <summary>
    /// 使用 NativeLibrary 加载 FFmpeg 函数。必须在任何 FFmpeg 调用前执行。
    /// </summary>
    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        string baseDir = AppContext.BaseDirectory;

        string[] dlls = ["avutil-60", "swresample-6", "avcodec-62", "swscale-9", "avformat-62"];
        IntPtr[] handles = new IntPtr[dlls.Length];

        // 按依赖顺序加载 DLL
        for (int i = 0; i < dlls.Length; i++)
        {
            string path = Path.Combine(baseDir, dlls[i] + ".dll");
            if (!File.Exists(path))
                throw new FileNotFoundException($"FFmpeg DLL not found: {path}");
            handles[i] = NativeLibrary.Load(path);
        }

        IntPtr avutil = handles[0];
        IntPtr avcodec = handles[2];
        IntPtr swscale = handles[3];

        // 加载函数
        avcodec_find_decoder  = Load<avcodec_find_decoder_t>(avcodec, nameof(avcodec_find_decoder));
        avcodec_alloc_context3= Load<avcodec_alloc_context3_t>(avcodec, "avcodec_alloc_context3");
        avcodec_open2         = Load<avcodec_open2_t>(avcodec, "avcodec_open2");
        avcodec_send_packet   = Load<avcodec_send_packet_t>(avcodec, "avcodec_send_packet");
        avcodec_receive_frame = Load<avcodec_receive_frame_t>(avcodec, "avcodec_receive_frame");
        avcodec_free_context  = Load<avcodec_free_context_t>(avcodec, "avcodec_free_context");
        avcodec_flush_buffers = Load<avcodec_flush_buffers_t>(avcodec, "avcodec_flush_buffers");

        av_frame_alloc  = Load<av_frame_alloc_t>(avutil, "av_frame_alloc");
        av_frame_free   = Load<av_frame_free_t>(avutil, "av_frame_free");

        // av_packet_* 在 FFmpeg master (8.0-dev) 中已从 avutil 迁移到 avcodec
        // 先尝试 avcodec，再回退到 avutil
        av_packet_alloc = TryLoad<av_packet_alloc_t>(avcodec, "av_packet_alloc")
                       ?? TryLoad<av_packet_alloc_t>(avutil, "av_packet_alloc");
        av_packet_free  = TryLoad<av_packet_free_t>(avcodec, "av_packet_free")
                       ?? TryLoad<av_packet_free_t>(avutil, "av_packet_free");
        av_packet_unref = TryLoad<av_packet_unref_t>(avcodec, "av_packet_unref")
                       ?? TryLoad<av_packet_unref_t>(avutil, "av_packet_unref");

        if (av_packet_alloc == null || av_packet_free == null || av_packet_unref == null)
            throw new EntryPointNotFoundException("av_packet_* functions not found in avcodec or avutil");

        sws_getContext  = Load<sws_getContext_t>(swscale, "sws_getContext");
        sws_scale       = Load<sws_scale_t>(swscale, "sws_scale");
        sws_freeContext = Load<sws_freeContext_t>(swscale, "sws_freeContext");
    }

    private static T Load<T>(IntPtr handle, string name) where T : Delegate
    {
        T? result = TryLoad<T>(handle, name);
        if (result == null)
            throw new EntryPointNotFoundException(
                $"FFmpeg 函数未找到: '{name}'\n尝试了: {name}, _{name}, {name}_\n" +
                $"请确认 ARM64 FFmpeg DLL 版本是否兼容。");
        return result;
    }

    private static T? TryLoad<T>(IntPtr handle, string name) where T : Delegate
    {
        string[] candidates = [name, "_" + name, name + "_"];
        foreach (var candidate in candidates)
        {
            try
            {
                IntPtr ptr = NativeLibrary.GetExport(handle, candidate);
                if (ptr != IntPtr.Zero)
                    return Marshal.GetDelegateForFunctionPointer<T>(ptr);
            }
            catch { }
        }
        return null;
    }
}
