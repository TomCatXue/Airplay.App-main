using FFmpeg.AutoGen.Abstractions;
using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace AirPlay.App.FFmmpeg;

public unsafe partial class H264Decoder : IDisposable
{
    private readonly Lock _lock = new();

    private readonly AVCodecContext* _codecContext;
    private readonly AVFrame* _frame;
    private readonly AVPacket* _packet;

    public bool Disposed { get; private set; }

    public void Flush()
    {
        if (Disposed) return;
        lock (_lock)
        {
            NativeFFmpeg.avcodec_flush_buffers(_codecContext);
        }
    }

    public H264Decoder()
    {
        AVCodec* codec = NativeFFmpeg.avcodec_find_decoder(AVCodecID.AV_CODEC_ID_H264);
        if (codec == null) throw new ApplicationException("Codec not found.");

        _codecContext = NativeFFmpeg.avcodec_alloc_context3(codec);
        if (_codecContext == null) throw new ApplicationException("Could not allocate codec context.");

        if (NativeFFmpeg.avcodec_open2(_codecContext, codec, null) < 0)
            throw new ApplicationException("Could not open codec.");

        _frame = NativeFFmpeg.av_frame_alloc();
        _packet = NativeFFmpeg.av_packet_alloc();
    }

    public bool Decode(byte[] h264Data, [NotNullWhen(true)] out byte[]? rgbData, out int width, out int height)
    {
        rgbData = null;
        width = height = 0;

        if (Disposed) return false;

        lock (_lock)
        {
            fixed (byte* p = h264Data)
            {
                NativeFFmpeg.av_packet_unref(_packet);
                _packet->data = p;
                _packet->size = h264Data.Length;

                int ret = NativeFFmpeg.avcodec_send_packet(_codecContext, _packet);
                if (ret < 0) return false;

                ret = NativeFFmpeg.avcodec_receive_frame(_codecContext, _frame);
                if (ret < 0) return false;

                width = _frame->width;
                height = _frame->height;

                // 保护：分辨率异常时跳过
                if (width <= 0 || height <= 0 || width > 7680 || height > 4320)
                {
                    width = height = 0;
                    return false;
                }

                int rgbStride = width * 4;
                long totalBytes = (long)rgbStride * height;
                if (totalBytes is <= 0 or > 256L * 1024 * 1024)
                {
                    width = height = 0;
                    return false;
                }

                rgbData = ArrayPool<byte>.Shared.Rent((int)totalBytes);
                bool success = false;

                AVFrame* rgbFrame = NativeFFmpeg.av_frame_alloc();
                try
                {
                    rgbFrame->format = (int)AVPixelFormat.AV_PIX_FMT_BGRA;
                    rgbFrame->width = width;
                    rgbFrame->height = height;

                    AVPixelFormat srcFmt = (AVPixelFormat)_frame->format;

                    fixed (byte* prgb = rgbData)
                    {
                        rgbFrame->data[0] = prgb;
                        rgbFrame->linesize[0] = rgbStride;

                        SwsContext* swsCtx = NativeFFmpeg.sws_getContext(
                            width, height, srcFmt,
                            width, height, AVPixelFormat.AV_PIX_FMT_BGRA,
                            NativeFFmpeg.SWS_FAST_BILINEAR, null, null, null);

                        if (swsCtx != null)
                        {
                            NativeFFmpeg.sws_scale(
                                swsCtx,
                                _frame->data, _frame->linesize, 0, height,
                                rgbFrame->data, rgbFrame->linesize);
                            NativeFFmpeg.sws_freeContext(swsCtx);
                            success = true;
                        }
                    }
                }
                finally
                {
                    NativeFFmpeg.av_frame_free(&rgbFrame);
                }

                if (!success)
                {
                    ArrayPool<byte>.Shared.Return(rgbData);
                    rgbData = null;
                    width = height = 0;
                }

                return success;
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            Disposed = true;

            if (_frame != null)
            {
                fixed (AVFrame** frame_ptr = &_frame)
                {
                    NativeFFmpeg.av_frame_free(frame_ptr);
                }
            }

            if (_packet != null)
            {
                fixed (AVPacket** packet_ptr = &_packet)
                {
                    NativeFFmpeg.av_packet_free(packet_ptr);
                }
            }

            if (_codecContext != null)
            {
                fixed (AVCodecContext** codecContext_ptr = &_codecContext)
                {
                    NativeFFmpeg.avcodec_free_context(codecContext_ptr);
                }
            }
        }
    }
}