# AirPlay Windows App (ARM64)

将 Windows on ARM64 设备变为 AirPlay 接收器，支持 iPhone/iPad/Mac 投屏。

基于 .NET 10 + WinUI 3，由 [natsurainko](https://github.com/natsurainko) 的 [原项目](https://github.com/natsurainko/Airplay.App) 适配 ARM64。

> 原作者：[natsurainko](https://github.com/natsurainko)  
> 核心协议：[AirPlay.Core2](https://github.com/natsurainko/AirPlay.Core2)

---

## ✨ 功能

- [x] 多设备同时连接 AirPlay
- [x] AAC / AAC-ELD 音频投送（MSYS2 ARM64 原生 libfdk-aac）
- [x] ALAC 音频投送（FFmpeg avcodec）
- [x] H.264 屏幕镜像投送
- [x] `Win + Alt + A` 控制面板
- [x] 窗口居中 + 自适应分辨率 + 铺满无黑边
- [x] Apple 风格圆角

---

## 📦 安装

1. 下载 `AirPlay.App_1.0.0.0_arm64.msix`
2. 启用 Windows **开发人员模式**
3. 安装 `.cer` 证书到**受信任人**
4. 双击 `.msix` 安装
5. iOS 和 PC 同一局域网 → 控制中心 → 屏幕镜像

---

## 📄 许可证

MIT License © 2025 [natsurainko](https://github.com/natsurainko)


