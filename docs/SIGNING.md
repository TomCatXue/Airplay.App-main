# 证书与签名说明

本应用为 MSIX 侧载包，必须签名才能安装。本文说明证书、密码与打包的完整流程。

---

## 1. 证书信息

| 项目 | 值 |
| --- | --- |
| 文件 | `AirPlay.App/AirPlay.App_TemporaryKey.pfx`（**私钥，带密码**） |
| 主题 | `CN=Natsurainko` |
| 指纹 | `5293174A2DDA0C95258C654D9AE1622BB16753A9` |
| 有效期 | 2026-06-05 ~ 2027-06-05 |
| 类型 | 自签名测试证书 |

> `.pfx` 含私钥，**不进版本库**（已被 `.gitignore` 的 `*.pfx` 规则排除）。
> 打包时同目录会生成 `AirPlay.App_<版本>_arm64.cer`——这只是**公钥证书，没有密码**，用于安装到系统信任库，**不能用于签名**。

### 安全提示

该 `.pfx` 的密码**曾经**被硬编码进本仓库的早期提交中，该仓库是公开的。
历史已于 2026-10-07 用 `git filter-repo` 重写清除（提交哈希已改变，克隆者需重新克隆）。

**如果你曾从旧历史中获取过该密码，请视为已泄露。** 更换证书的方法见第 5 节。

---

## 2. 密码注入方式（不要硬编码）

`AirPlay.App.csproj` 中密码引用的是 MSBuild 属性，不写死：

```xml
<PackageCertificateKeyFile>AirPlay.App_TemporaryKey.pfx</PackageCertificateKeyFile>
<PackageCertificatePassword>$(AirPlayCertificatePassword)</PackageCertificatePassword>
```

因此打包时必须把密码作为 MSBuild 属性传入。**三种方式，任选其一：**

### 方式 A：命令行直接传（最简单）

```powershell
dotnet build AirPlay.App/AirPlay.App.csproj -c Release `
  -p:Platform=ARM64 -p:RuntimeIdentifier=win-arm64 `
  -p:AppxPackageDir=AppPackages-SelfContained/ `
  -p:GenerateAppxPackageOnBuild=true `
  -p:AirPlayCertificatePassword=你的密码
```

### 方式 B：环境变量（推荐，避免密码进 shell 历史）

```powershell
$env:AirPlayCertificatePassword = '你的密码'
dotnet build AirPlay.App/AirPlay.App.csproj -c Release `
  -p:Platform=ARM64 -p:RuntimeIdentifier=win-arm64 `
  -p:AppxPackageDir=AppPackages-SelfContained/ `
  -p:GenerateAppxPackageOnBuild=true
```

### 方式 C：`Directory.Build.props`（本机长期开发用）

在仓库根目录建 `Directory.Build.props`（**已加入 `.gitignore`**，不会误提交）：

```xml
<Project>
  <PropertyGroup>
    <AirPlayCertificatePassword>你的密码</AirPlayCertificatePassword>
  </PropertyGroup>
</Project>
```

---

## 3. 不出包 / 签名失败的排查

| 现象 | 原因 | 解决 |
| --- | --- | --- |
| 报错要求提供证书密码 | 未提供 `-p:AirPlayCertificatePassword` | 按第 2 节传入 |
| 能出包但装不上（提示签名不受信任） | 用了 `-p:AppxPackageSigningEnabled=false`，产物**未签名** | 去掉该参数，正常签名重打 |
| 证书指纹不匹配 | `.pfx` 被换过 | 更新 `csproj` 里的 `PackageCertificateThumbprint` |
| 证书过期 | 有效期到 2027-06-05 | 按第 5 节重新生成 |

> **注意**：`-p:AppxPackageSigningEnabled=false` 产出的 msix **无法直接 `Add-AppxPackage` 安装**。
> 想要"给大多数人用"的安装包，必须走签名流程。

---

## 4. 验证签名是否成功

```powershell
# 包内应存在签名文件
# AirPlay.App_<版本>_arm64.msix -> AppxSignature.p7x
```

用任意 zip 工具打开 `.msix`，看到 `AppxSignature.p7x` 即签名成功。

---

## 5. 重新生成证书（密码泄露或过期时）

```powershell
# 生成新自签名证书（有效期 1 年，可按需调整）
New-SelfSignedCertificate `
  -Type Custom `
  -Subject "CN=Natsurainko" `
  -KeyUsage DigitalSignature `
  -FriendlyName "AirPlay App" `
  -CertStoreLocation "Cert:\CurrentUser\My" `
  -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
```

导出为 `.pfx` 后：

1. 替换 `AirPlay.App/AirPlay.App_TemporaryKey.pfx`
2. 用新指纹更新 `AirPlay.App.csproj` 的 `PackageCertificateThumbprint`
3. 重新打包（第 2 节）
4. **用户侧需卸载旧版并重新安装新 `.cer` 证书**，否则会因为发布者不同导致升级失败

---

## 6. 用户安装时为什么要装 `.cer`

Windows 只信任系统信任库里的根证书。自签名证书不在其中，所以首次安装必须把
`.cer` 装到「受信任的根证书颁发机构」或「受信任人」，详见 [README](./README.md) 安装章节。
