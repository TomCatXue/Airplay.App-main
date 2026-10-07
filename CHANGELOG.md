# 更新日志 / 发布列表

版本号沿用 MSIX 包版本，按时间记录各版本变更。

---

## v1.0.2 · 2026-10-07（当前发布版 · 投屏稳定性 + UI 重做）

### ✨ 新增 / 增强

- **投屏窗口迷你形态**：最小化按钮不再缩到任务栏，而是折叠为停靠屏幕右上角的 320×64 迷你条，
  置顶显示、暂停渲染，再次点击恢复完整布局。
- **大屏模式**：顶部悬浮条新增「大屏」按钮，进入后展开左侧竖直控制面板
  （设备名、播放 / 暂停、上 / 下一首、音量 + 静音、截图）。
- **本地截图**：`MirrorWindow` 将当前画面帧保存为 PNG 到图片库（新增 `picturesLibrary` 能力）。
- **控制面板重做**：从「两个悬浮 Popup 堆叠」改为窗口内自上而下分区布局
  （顶部栏 / 媒体卡 / 音量条 / 设备列表），iOS 玻璃拟态风格。
- **设置页重做**：分区卡片（服务信息 / 网络 / 启动）+ 统一输入控件样式。
- **设备外框按真实机型呈现**：新增 `Models/DeviceFrameProfile.cs`，按 DeviceModel 解析
  iPhone / iPad 的圆角半径与灵动岛 / 刘海形态，横竖屏翻转自动重算。
- **防火墙一键放行脚本**：新增 `open-airplay-ports.cmd`（UDP 5353 / TCP 7100 / UDP 7000-7020 / TCP+UDP 5000-5020）。

### 🐛 修复

- **投屏无画面（多轮根因）**：
  - `MirrorWindow` 用协议声明尺寸校验帧，而 FFmpeg 解码器实际输出宽高从未回传，两者不一致时
    所有帧被判定丢弃 → 永久黑屏。现解码成功后先 `EnsureFrameSize` 校正再投递。
  - `CanvasAnimatedControl` 置于无尺寸的 `Viewbox` 内被测量为 0×0，`Draw` 事件永不触发 → 改为显式尺寸。
  - `TryProcessVideo` 对多 NALU 帧做整体丢弃 → 改为严格边界检查。
  - ArrayPool 缓冲区在窗口为空时从不归还，30fps 下秒级 OOM → 已归还并加计数。
- **投屏闪退**：
  - 解码后台线程直接访问 UI 元素导致跨线程 COMException → 全部移入 `DispatcherQueue`。
  - 全局异常处理未设 `e.Handled = true`，stowed exception 继续传播终止进程 → 已设并补
    `AppDomain.UnhandledException` / `TaskScheduler.UnobservedTaskException`。
  - `GetRefreshRate()` 返回 0 时 `1/0` 抛异常 → 兜底 60。
- **设置页无法滚动**：`SetTitleBar(RootGrid)` 把整个窗口设为标题栏，caption 拦截滚轮手势 →
  改由各页面顶栏单独 `SetTitleBar`，拖拽区限定在 44/48px。
- **控制面板徽标与失焦隐藏从未生效**：`ControlPage.Grid_Loaded` 是死代码（XAML 未挂载）→
  改名 `PageTitleBar_Loaded` 并接线。
- **投屏窗口白边**：Win32 层移除 `WS_CAPTION | WS_THICKFRAME | WS_BORDER` 实现真无边框，
  配合 `SetWindowRgn` 同心圆角。手动拖拽改用 `GetCursorPos` 屏幕绝对坐标，
  彻底消除因相对坐标反馈环导致的拖动抖动。
- **SMTC 跨线程写入**：`Device.cs` 中 `PlaybackStatus` 改为在 Dispatcher 上设置。
- **`MirrorService` 竞态 NRE**：`session.MirrorController!` 改为先取局部变量并空值守卫。
- **设置保存未生效**：`SaveButton_Click` 未显式调用 `_settingsService.Save()`。

### 🔒 安全

- **修复签名密码泄露**：`.pfx` 密码曾被硬编码进公开仓库的早期提交。
  已用 `git filter-repo` 重写历史清除（提交哈希已改变，克隆者需重新克隆）。
  密码改为只经 `$(AirPlayCertificatePassword)` MSBuild 属性注入，仓库内不留明文。
  详见 [docs/SIGNING.md](docs/SIGNING.md)。

### 📝 文档

- README 新增界面预览图（纯白背景展示图）与完整的证书安装步骤。
- 新增 [docs/SIGNING.md](docs/SIGNING.md)：证书信息、三种密码注入方式、签名失败排查、证书更换流程。

### 📦 安装包

`AirPlay.App/AppPackages-SelfContained/AirPlay.App_1.0.2.0_arm64_Test/AirPlay.App_1.0.2.0_arm64.msix`

配套证书（首次安装需导入）：同目录 `AirPlay.App_1.0.2.0_arm64.cer`

---

## v1.0.1 · 2026-08-15（设置入口 + 全屏按钮）

### ✨ 新增 / 增强

- 控制面板右上角新增设置按钮，可进入设置页修改服务名、端口、网卡与开机自启。
- 投屏悬浮控件新增全屏按钮，点击全屏完整显示画面（不裁剪），再点击恢复原窗口大小。

### 📦 安装包

`AirPlay.App/AppPackages-SelfContained/AirPlay.App_1.0.1.0_arm64_Test/AirPlay.App_1.0.1.0_arm64.msix`

---

## v1.0.0 · 2026-08-14（自包含 + 全面修复）

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
