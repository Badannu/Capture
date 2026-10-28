# 打包与分发

这份文档回答两件事：**这个程序跑起来到底需要什么**，以及**在线版 / 便携版分别怎么出包、怎么验**。

结论先放前面：

| | 便携版（推荐主推） | 在线版（框架依赖） |
| --- | --- | --- |
| 压缩包 | `Capture-<ver>-portable-win-x64.zip` **60.7 MB** | `Capture-<ver>-online-win-x64.zip` **1.0 MB** |
| 解压后 | 1 个 exe，**66.3 MB** | 13 个程序文件，**2.6 MB** |
| 用户前提 | Windows 10/11 x64，**别的什么都不用装** | 先装 **.NET 8 Desktop Runtime (x64)** |
| 首次启动 | 需要自解压原生库到 `%TEMP%\.net\`，实测冷启 0.6 s | 直接起 |
| 升级 | 换掉那个 exe | 换掉整个文件夹 |
| 卸载 | 删文件夹 | 删文件夹 + 删 `%AppData%\Capture` |

---

## 1. 运行环境

### 1.1 操作系统

- Windows 10 1809 及以上 / Windows 11，**仅 x64**（工程只出 `win-x64`；没有 arm64、没有 x86、没有 Windows 7）。
- 清单（`app.manifest`）声明 **Per-Monitor V2** DPI。钩子坐标、UIA 边界、`CopyFromScreen` 全部按物理像素处理，高 DPI 缩放不会错位。
- 执行级别 **asInvoker**：不要求管理员权限，也不应该用管理员跑（提权后热键只能收到高权限窗口的按键，反而更差）。

### 1.2 .NET 运行时

| 包 | 要求 |
| --- | --- |
| 便携版 | 无。运行时已打包进 exe（self-contained + single-file + 压缩） |
| 在线版 | `Microsoft.WindowsDesktop.App` **8.0.x**，即 [.NET 8 Desktop Runtime x64](https://dotnet.microsoft.com/download/dotnet/8.0)。工程设了 `RollForward=Major`，所以装了 .NET 9 的桌面运行时也能跑 |

在线版缺运行时的表现：双击后弹「需要安装 .NET」并给下载链接（apphost 自带提示）。

### 1.3 明确**不需要**的东西

这类问题被问太多次，单独列出来：

- **不需要 VC++ 运行库**。工程里没有任何原生 C++ 依赖，P/Invoke 只调 `user32` / `gdi32` / `dwmapi` / `uiautomationcore` 这些系统自带 DLL。
- **不需要 WebView2 / Edge**。界面是 WinForms 自绘，没有 Web 容器。
- **不打包字体**。字体按 `Microsoft YaHei UI → Microsoft YaHei → Segoe UI → Tahoma → 通用无衬线` 依次回落，Win10/11 上第一项就命中。
- **不联网、不装服务、不装驱动**、不写计划任务。全部状态就是 `%AppData%` 下一个目录（或便携版 exe 旁的目录）。
- **元素截图用的 UI Automation 是 Windows 自带组件**（`UIAutomationCore.dll`），不用另装。

### 1.4 在线版的 13 个程序文件，谁能省

解压目录里少一个都可能在特定操作时炸，**不要只拷 exe**。

| 文件 | 作用 | 少了会怎样 |
| --- | --- | --- |
| `CaptureDemo.exe` | 入口（apphost），负责找运行时并加载 dll | 没有入口 |
| `CaptureDemo.dll` | 主程序：UI、钩子、会话 | 启动即失败 |
| `CaptureDemo.Core.dll` | 拼接算法、设置模型、热键解析 | 启动即失败 |
| `CaptureDemo.deps.json` | 依赖清单 | 启动时报找不到程序集 |
| `CaptureDemo.runtimeconfig.json` | 运行时配置（TFM、roll-forward、DPI） | 提示要装运行时 / DPI 行为变 | 
| `FlaUI.Core.dll`、`FlaUI.UIA3.dll` | 元素截图的 UIA 封装（MIT） | 元素截图崩，其它模式正常 |
| `Interop.UIAutomationClient.dll` | 上面两个的 COM 互操作 | 同上 |
| `System.Configuration.ConfigurationManager.dll`、`System.Diagnostics.EventLog.dll`、`System.Diagnostics.PerformanceCounter.dll`、`System.Management.dll` | FlaUI 的传递依赖 | 元素模式初始化时炸 |
| `System.Drawing.Common.dll` | GDI+ 托管封装（截图、标注、缩略图） | 启动即失败 |

> 便携版把这 13 个全部塞进了一个 exe，所以目录里只剩 `CaptureDemo.exe` + `portable.txt` + `使用说明.txt`。

### 1.5 程序会往哪写

部署位置要保证**这些路径可写**，否则会出现「能启动但存不了图 / 存不了设置」。

| 路径 | 内容 | 能不能改 |
| --- | --- | --- |
| `%AppData%\Capture\settings.json` | 全部设置 | 界面里改；便携版改为 exe 同级 |
| `%AppData%\Capture\clipboard\` | 剪贴板历史的 PNG、缩略图、`index.json` | 设置里可关记录、可改保留条数 |
| `图片\Capture\` | 截图输出（`{kind}-{yyyyMMdd}-{HHmmss}-{fff}.png`） | 设置里可改目录 |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\CaptureDemo` | 仅当打开「开机自动启动」 | 关掉开关即删 |
| `%TEMP%\.net\CaptureDemo\` | 便携版首启解压出的原生库 | 系统临时目录，删了下次自动重建 |

单实例靠一个命名互斥体（`SingleInstance`），不落地文件；热键是进程内注册，退出即释放。

### 1.6 便携模式怎么判定

exe 同级存在 **`portable.txt`** 时：

- `settings.json` 落到 exe 旁边；
- 剪贴板历史落到 `exe\clipboard\`；
- 不做老的 `%AppData%\CaptureDemo\clipboard` 迁移（避免把已安装那份的历史搬走）。

判定逻辑在 `AppSettings.IsPortable()` / `SettingsFilePath()`，两个分支都有单测（`PortableTests`）。

**限制**：便携目录必须可写；挪动目录后「开机自动启动」里的旧路径失效，重开一次开关即可。

---

## 2. 出包

```powershell
powershell -ExecutionPolicy Bypass -File .\pack.ps1
# 只要便携版
powershell -ExecutionPolicy Bypass -File .\pack.ps1 -PortableOnly
# 只要在线版
powershell -ExecutionPolicy Bypass -File .\pack.ps1 -OnlineOnly
```

产物落在 `dist\`（已 gitignore，不进仓库）：

- 版本号自动从 `Directory.Build.props` 的 `<Version>` 读，不用手写。
- 每个包都带一份 `使用说明.txt`（`packaging\` 下的模板，写包时转成带 BOM 的 UTF-8，老记事本不乱码）。
- 便携包额外塞 `portable.txt`。

`publish.ps1` 与 `pack.ps1` 的分工：前者刷新仓库里的 `publish\`（开发/自用目录），后者产出可对外分发的 zip。

---

## 3. 发布前验收

改过代码或依赖后，至少跑这四条：

1. `dotnet build CaptureDemo.sln -c Release` → 0 错 0 警。
2. `dotnet test CaptureDemo.sln -c Release` → 全绿（当前 87 个）。
3. 便携包：解压到空目录 → 双击 → 托盘出现 → 截一张图 → 确认 `settings.json` 出现在**这个目录**里，`%AppData%\Capture` 没有被新建。
4. 在线包：在没装运行时的机器（或虚拟机）上双击 → 应提示安装 .NET 8 Desktop Runtime，而不是闪退。

---

## 4. 如果以后要做「双击安装器」

现在两个包都是**压缩包形态**，没有安装程序。真要做，建议 Inno Setup（免费、单文件 exe、支持 per-user 安装）：

- 安装位置：`{localappdata}\Programs\Capture`，per-user，不触发 UAC。
- 装前检测：注册表 `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*` / `HKLM\SOFTWARE\WOW6432Node\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App` 下有没有 `8.*`；没有就调用微软官方的 `windowsdesktop-runtime-8.0.x-win-x64.exe /install /quiet /norestart`（需 UAC）。
- 卸载要删：程序目录、开始菜单快捷方式、`HKCU\...\Run\CaptureDemo`；**问一句**再决定要不要删 `%AppData%\Capture`（用户的剪贴板历史和设置）。
- 签名：不签名的话 SmartScreen 会拦一次（「更多信息 → 仍要运行」），便携版同理。

---

## 5. 已知缺口

- **exe 名仍是 `CaptureDemo.exe`**（产品名是 Capture）。改名要同步：`AssemblyName`、`SingleInstance` 的互斥体名、`StartupRegistration` 的 Run 值名、`publish.ps1` / `run-*.ps1` / 文档。老用户的自启项和快捷方式会失效，所以一直没动。
- **未做代码签名**，首次运行会有 SmartScreen 提示。
- **只出 win-x64**，ARM 设备靠 x64 仿真跑。
- 便携版与在线版**共用同一个单实例互斥体**，不要同时装两份在同一个用户下（会互相把对方「唤醒」）。
