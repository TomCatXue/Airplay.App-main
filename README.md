# AirPlay Windows App (ARM64)

将 Windows on ARM64 设备变为 AirPlay 接收器，支持 iPhone / iPad / Mac 屏幕镜像与音频投送。

基于 .NET 10 + WinUI 3 **自包含**打包，由 [natsurainko](https://github.com/natsurainko) 的 [原项目](https://github.com/natsurainko/Airplay.App) 适配 ARM64 并深度修复。

> 原作者：[natsurainko](https://github.com/natsurainko)
> 核心协议：[AirPlay.Core2](https://github.com/natsurainko/AirPlay.Core2)（已并入本仓库并修复投屏 / 配对等关键问题）

---

## ✨ 功能

- [x] iPhone / iPad / Mac **屏幕镜像**（H.264，分辨率跟随屏幕，初始 85%，铺满无黑边）
- [x] **多设备**同时连接
- [x] AAC / AAC-ELD / ALAC 音频投送
- [x] `Win + Alt + A` 控制面板（中文界面）
- [x] 托盘菜单（打开控制面板 / 退出）
- [x] 正在播放信息（封面 / 歌手 / 专辑）
- [x] 悬浮控件实时 **FPS / 丢帧** 显示
- [x] Apple 风格圆角
- [x] **自包含打包**：内置 WindowsAppSDK 与 .NET 运行时，不受系统运行时更新影响

---

## 📦 安装

1. 下载最新 `AirPlay.App_1.0.1.0_arm64.msix`（位于 `AirPlay.App/AppPackages-SelfContained/`）
2. 启用 Windows **开发人员模式**
3. 首次安装：将同目录 `AirPlay.App_1.0.1.0_arm64.cer` 证书安装到 **受信任的根证书颁发机构 / 受信任人**
4. 若已安装旧版，先卸载：
   ```powershell
   Get-AppxPackage -Name "AirPlay.App" | Remove-AppxPackage
   ```
5. 安装：
   ```powershell
   Add-AppxPackage -Path "AirPlay.App_1.0.0.0_arm64.msix"
   ```
6. iOS / iPad / Mac 与 PC 同一局域网 → 控制中心 → **屏幕镜像** → 选择 "AirPlay Windows App"

> 本版本为**自包含**打包：内置运行时，不依赖系统组件，系统更新不会破坏应用。

---

## 🔧 使用提示

- 投屏窗口默认 85% 大小，铺满无黑边（窗口比例与画面不一致时自动裁剪）
- 投屏悬浮控件：设备信息、实时 FPS、最小化、断开连接
- 控制面板：`Win + Alt + A` 或托盘图标打开
- **搜不到设备**：确认与 iPhone 同一 Wi-Fi、关闭路由器 AP 隔离、防火墙放行 TCP 5000/7100

---

## 📄 变更记录

完整发布列表见 [CHANGELOG.md](CHANGELOG.md)

---

## 📄 许可证

MIT License © 2025 [natsurainko](https://github.com/natsurainko)
