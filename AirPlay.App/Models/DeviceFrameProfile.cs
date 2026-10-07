using System;

namespace AirPlay.App.Models;

/// <summary>
/// 设备外观剖面：圆角半径以「短边比例」表达，与像素尺寸、旋转无关，
/// 平移自 iPhoneMirror 的 DeviceCornerProfile 设计（半径取短边比例，
/// 旋转/窗口缩放不会改变视觉比例）。同时携带刘海/灵动岛类型。
/// </summary>
public sealed record DeviceFrameProfile(
    string Id,
    bool IsRounded,
    double CornerRadiusRatio,
    double ScreenCornerRatio,
    NotchKind Notch,
    double IslandWidthRatio,
    double IslandHeightRatio,
    double IslandTopRatio)
{
    public static readonly DeviceFrameProfile Rectangular = new("rectangular", false, 0, 0, NotchKind.None, 0, 0, 0);

    /// <summary>按短边像素返回外框圆角半径（像素），并对极小窗口做下限保护。</summary>
    public int GetCornerRadius(double shortEdge, double dpiScale = 1.0)
    {
        if (!IsRounded || shortEdge <= 0) return 0;
        var fitted = shortEdge * Math.Clamp(CornerRadiusRatio, 0.0, 0.5);
        var minimum = Math.Min(shortEdge * 0.20, 6.0 * Math.Max(0.5, dpiScale));
        return (int)Math.Round(Math.Max(fitted, minimum));
    }

    /// <summary>
    /// 按短边像素返回「屏幕区」圆角半径（像素），严格按 iOS 真机比例（iPhone ≈ 短边 14–15%），
    /// 不再人为钳制到固定上限，以保证横屏/竖屏与设计稿一致。
    /// </summary>
    public int GetScreenCornerRadius(double shortEdge, double dpiScale = 1.0)
    {
        if (!IsRounded || shortEdge <= 0) return 0;
        var fitted = shortEdge * Math.Clamp(ScreenCornerRatio, 0.0, 0.5);
        var minimum = Math.Min(shortEdge * 0.05, 4.0 * Math.Max(0.5, dpiScale));
        return (int)Math.Round(Math.Max(fitted, minimum));
    }
}

public enum NotchKind
{
    None,
    Notch,
    DynamicIsland,
}

/// <summary>
/// 由设备型号字符串（RTSP model 字段，如 "iPhone"/"iPad"）或帧几何回退，
/// 解析出设备外观剖面。半径系数为视觉拟合值，并非 Apple 官方物理尺寸。
/// </summary>
internal static class DeviceFrameProfileResolver
{
    // 现代 iPhone：大圆角 + 灵动岛
    // 真机量测（iPhone 14 Pro/15/16，屏幕 393×852pt）：灵动岛 ≈ 125×37.3pt、距顶 ≈ 11pt
    //   → 宽 0.3180、高 0.0438、上边距 0.0129
    private static readonly DeviceFrameProfile IPhoneDynamicIsland =
        new("iphone-dynamic-island", true, 0.1784, 0.1500, NotchKind.DynamicIsland, 0.3180, 0.0438, 0.0129);
    // 老款刘海 iPhone（X~11，屏幕 375×812pt）：刘海 ≈ 209×30pt，且紧贴顶部
    //   → 宽 0.5573、高 0.0369、上边距 0（形状为"上直下方圆角"）
    private static readonly DeviceFrameProfile IPhoneNotch =
        new("iphone-notch", true, 0.1580, 0.1350, NotchKind.Notch, 0.5573, 0.0369, 0.0);
    // iPad：极小圆角，无刘海
    private static readonly DeviceFrameProfile IPad =
        new("ipad", true, 0.0380, 0.0300, NotchKind.None, 0, 0, 0);

    public static DeviceFrameProfile Resolve(string? model, uint frameWidth = 0, uint frameHeight = 0)
    {
        if (!string.IsNullOrWhiteSpace(model))
        {
            var m = model.ToLowerInvariant();
            if (m.Contains("ipad") || m.Contains("pad")) return IPad;
            if (m.Contains("iphone") || m.Contains("phone"))
                return IsModernIphone(m) ? IPhoneDynamicIsland : IPhoneNotch;
        }

        return ResolveByGeometry(frameWidth, frameHeight);
    }

    private static bool IsModernIphone(string m) =>
        !m.Contains("se") && !m.Contains("xr") && !ContainsOldGeneration(m);

    private static bool ContainsOldGeneration(string m) =>
        m.Contains("iphone 6") || m.Contains("iphone 7") || m.Contains("iphone 8") ||
        m.Contains("iphone 5") || m.Contains("iphone 4");

    private static DeviceFrameProfile ResolveByGeometry(uint width, uint height)
    {
        if (width == 0 || height == 0) return DeviceFrameProfile.Rectangular;
        var shortEdge = Math.Min(width, height);
        var longEdge = Math.Max(width, height);
        var ratio = shortEdge / (double)longEdge;

        // 圆角 iPad 比例约 0.69–0.75；现代 iPhone 约 0.42–0.50
        if (ratio is >= 0.64 and <= 0.80) return IPad;
        if (ratio is >= 0.38 and <= 0.56) return IPhoneDynamicIsland;
        return DeviceFrameProfile.Rectangular;
    }
}
