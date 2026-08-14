# 更新日志 / 发布列表

版本号沿用 MSIX 包版本，按时间记录各版本变更。

---

## v1.0.0 · 2026-08-14（当前发布版 · 自包含 + 全面修复）

### 🐛 修复

- **应用启动崩溃**：WindowsAppRuntime 系统更新导致 WinUI 启动即崩溃（0xc000027b）。
  改为**自包含打包**，内置 WindowsAppSDK + .NET 运行时，彻底免疫系统运行时更新。
- **投屏「搜得到但连不上」**（iOS 转圈后报错）：
  - 修复 7100 投屏通道为空壳的问题：改用完整 RTSP 实现（配对 / FairPlay / 视频流 SETUP / RECORD / TEARDOWN）。
  - 修复 mDNS 与 `/info` 中公钥与实际签名密钥不一致导致的 iOS 配对失败。
  - 修复 mDNS 广播了 WSL / Hyper-V 虚拟网卡 IP 导致 iOS 连错地址的问题：只广播真实局域网 IP。
- 修复构建时 `libfdk-aac.dll` 重复复制导致的打包失败。

### ✨ 新增 / 增强

- **自包含 MSIX**：单文件安装，无 WindowsAppRuntime / .NET 运行时依赖。
- **镜像分辨率跟随真实屏幕**（原硬编码 1920×1080）。
- 投屏窗口初始 **85%** 大小，**铺满无黑边**。
- 悬浮控件实时 **FPS / 丢帧** 显示。
- 控制面板**中文化** + 排版优化（字号 / 间距 / 层次）。
- 托盘菜单增强（打开控制面板 / 退出）。
- 正在播放信息（封面 / 歌手 / 专辑）。
- Apple 风格圆角。
- 文件日志（`%LOCALAPPDATA%\Packages\AirPlay.App_*\LocalState\applog-*.txt`），便于排查。

### 📦 安装包

`AirPlay.App/AppPackages-SelfContained/AirPlay.App_1.0.0.0_arm64_Test/AirPlay.App_1.0.0.0_arm64.msix`

---

## v1.0.0 · 2026-06-10（初始 ARM64 适配版）

- 基于 [natsurainko/Airplay.App](https://github.com/natsurainko/Airplay.App) 适配 Windows on ARM64。
- 基础 AirPlay 接收（mDNS 发现 / 音频通道）。
- `Win + Alt + A` 控制面板雏形。
- 已知问题：投屏通道未完成、依赖系统 WindowsAppRuntime（后续系统更新可能导致启动崩溃）。
