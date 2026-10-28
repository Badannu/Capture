# 打包与分发

这份文档回答两件事：**这个程序跑起来到底需要什么**，以及**安装版 / 便携版分别怎么出包、怎么验**。

结论先放前面：

| | 安装版（推荐主推） | 便携版 | 在线版（框架依赖） |
| --- | --- | --- | --- |
| 产物 | `Capture-<ver>-setup-win-x64.exe` **46.50 MB** | `Capture-<ver>-portable-win-x64.zip` **57.97 MB** | `Capture-<ver>-online-win-x64.zip` **1.0 MB** |
| 装完 | 249 个文件，**150.1 MB** | 1 个 exe，**66.5 MB** | 13 个程序文件，**2.6 MB** |
| 用户前提 | Windows 10/11 x64，**别的什么都不用装** | 同左 | 先装 **.NET 8 Desktop Runtime (x64)** |
| 配置位置 | `%AppData%\Capture` | exe 同级目录 | `%AppData%\Capture` |
| 安装位置 | 默认 `%LOCALAPPDATA%\Programs\Capture`，可改 | 解压到哪就是哪 | 解压到哪就是哪 |
| 首次启动 | 直接起（多文件，不用自解压） | 需要自解压原生库到 `%TEMP%\.net\`，实测冷启 0.6 s | 直接起 |
| 升级 | 跑新版安装器覆盖 | 换掉那个 exe | 换掉整个文件夹 |
| 卸载 | 卸载器（**永不删用户数据**，只提示数据在哪） | 删文件夹 | 删文件夹 + 删 `%AppData%\Capture` |

便携版和安装版**可以同时运行**：单实例互斥体与唤醒消息按运行形态分成了两套，见 1.5。


---

## 1. 运行环境

### 1.1 操作系统

- Windows 10 1809 及以上 / Windows 11，**仅 x64**（工程只出 `win-x64`；没有 arm64、没有 x86、没有 Windows 7）。
- 清单（`app.manifest`）声明 **Per-Monitor V2** DPI。钩子坐标、UIA 边界、`CopyFromScreen` 全部按物理像素处理，高 DPI 缩放不会错位。
- 执行级别 **asInvoker**：不要求管理员权限，也不应该用管理员跑（提权后热键只能收到高权限窗口的按键，反而更差）。

### 1.2 .NET 运行时

| 包 | 要求 |
| --- | --- |
| 安装版 | 无。运行时已打包进程序目录（self-contained，**多文件**，见 4 节为什么不用单文件），安装器不去检测也不去下载运行时 |
| 便携版 | 无。同上，只是整个打包成一个 exe（self-contained + single-file） |
| 在线版 | `Microsoft.WindowsDesktop.App` **8.0.x**，即 [.NET 8 Desktop Runtime x64](https://dotnet.microsoft.com/download/dotnet/8.0)。工程设了 `RollForward=Major`，所以装了 .NET 9 的桌面运行时也能跑 |

在线版缺运行时的表现：双击后弹「需要安装 .NET」并给下载链接（apphost 自带提示）。
在线版**默认不再产出**，要出必须显式传 `-OnlineOnly`：它最容易让用户下错、装完打不开。

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
| `Capture.exe` | 入口（apphost），负责找运行时并加载 dll | 没有入口 |
| `Capture.dll` | 主程序：UI、钩子、会话 | 启动即失败 |
| `Capture.Core.dll` | 拼接算法、设置模型、热键解析 | 启动即失败 |
| `Capture.deps.json` | 依赖清单 | 启动时报找不到程序集 |
| `Capture.runtimeconfig.json` | 运行时配置（TFM、roll-forward、DPI） | 提示要装运行时 / DPI 行为变 | 
| `FlaUI.Core.dll`、`FlaUI.UIA3.dll` | 元素截图的 UIA 封装（MIT） | 元素截图崩，其它模式正常 |
| `Interop.UIAutomationClient.dll` | 上面两个的 COM 互操作 | 同上 |
| `System.Configuration.ConfigurationManager.dll`、`System.Diagnostics.EventLog.dll`、`System.Diagnostics.PerformanceCounter.dll`、`System.Management.dll` | FlaUI 的传递依赖 | 元素模式初始化时炸 |
| `System.Drawing.Common.dll` | GDI+ 托管封装（截图、标注、缩略图） | 启动即失败 |

> 便携版把这 13 个全部塞进了一个 exe，所以目录里只剩 `Capture.exe` + `portable.txt` + `使用说明.txt`。

### 1.5 程序会往哪写

部署位置要保证**这些路径可写**，否则会出现「能启动但存不了图 / 存不了设置」。

| 路径 | 内容 | 能不能改 |
| --- | --- | --- |
| `%AppData%\Capture\settings.json` | 全部设置 | 界面里改；便携版改为 exe 同级 |
| `%AppData%\Capture\clipboard\` | 剪贴板历史的 PNG、缩略图、`index.json` | 设置里可关记录、可改保留条数 |
| `%AppData%\Capture\clipboard\orphan\` | 自动清扫搬过来的孤儿文件（`<yyyyMMdd-HHmmss>-<原文件名>`，平铺无子目录） | 自动；清扫**只搬不删**，确认无用后可整目录删除 |
| `%AppData%\Capture\clipboard\index.bak.json` | 索引上一版备份，主索引读不出来时回退到它 | 自动；删掉只是少一层保险 |
| `图片\Capture\` | 截图输出（`{kind}-{yyyyMMdd}-{HHmmss}-{fff}.png`） | 设置里可改目录 |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Capture` | 仅当打开「开机自动启动」 | 关掉开关即删；安装器卸载时也会删 |
| `%TEMP%\.net\Capture\` | **仅便携版**（单文件）：首启解压出的原生库 | 系统临时目录，删了下次自动重建 |

单实例靠一个命名互斥体（`SingleInstance`），不落地文件。互斥体名和唤醒消息名都带运行形态：
`Local\Capture.Instance.installed.v1` / `Local\Capture.Instance.portable.v1`，
所以装了安装版又解压了一份便携版时，两者各自都算「第一个实例」，互不干扰。
热键是进程内注册，退出即释放（两份同时跑时，后来的那份会报热键冲突，属正常提示）。

### 1.6 便携模式怎么判定

exe 同级存在 **`portable.txt`** 时：

- `settings.json` 落到 exe 旁边；
- 剪贴板历史落到 `exe\clipboard\`；
- 不做老的 `%AppData%\CaptureDemo\clipboard` 迁移（避免把已安装那份的历史搬走）。

判定逻辑在 `AppSettings.IsPortable()` / `SettingsFilePath()`，两个分支都有单测（`PortableTests`）。

**限制**：便携目录必须可写；挪动目录后「开机自动启动」里的旧路径失效，重开一次开关即可。

### 1.7 热键冲突怎么提示，什么时候弹气泡

热键注册不上时有两套提示，成本不同，策略也不同：

- **程序内部提示**（主窗口底部状态行 + 设置页每行后面的红色感叹号）：每次启动**都显示**当前真实情况，不节流。看到它们不需要任何代价。
- **托盘气泡**：只在「冲突组合首次出现或发生变化」时弹一次，之后每次启动都安静。

判据是一份写到 `settings.json` 的指纹（`HotkeyConflictNotice`，由 `CaptureDemo.Core` 的
`HotkeyConflictFingerprint` 计算）：**动作名 + 冲突类型 + 规范化后的组合键**，排序后拼接。
两处设计是刻意的：

- **推断出的占用者不参与指纹**。「疑似微信 截图」是从「常见默认快捷键表 + 当前运行的进程」
  猜出来的（Windows 不提供"谁占用了热键"的 API）。如果把它算进指纹，那么每次开机程序的启动
  顺序不同（今天微信先起来、明天没起来），同一个冲突就会被判成新情况，于是天天弹——这正是要
  避免的。
- **冲突消失时把记录清空**。否则弹过的那一次会永久静音，下次出现新冲突反而不会被发现。

| 情况 | 气泡 | 内部提示 |
| --- | --- | --- |
| 首次运行（设置里没有记录）且有冲突 | 弹 | 有 |
| 之后每次启动，冲突组合与上次记录的相同 | 不弹 | 有 |
| 用户改了热键，产生新的冲突组合 | 再弹一次 | 有 |
| 冲突全部消失 | 不弹 | 无（同时清除记录） |

老用户升级上来第一次启动仍会弹一次：旧 `settings.json` 里没有这个字段，缺字段等同于
「从未提示过」。

---

## 2. 出包

```powershell
# 默认：安装器 + 便携包（不出在线版）
powershell -ExecutionPolicy Bypass -File .\pack.ps1
# 只出安装器
powershell -ExecutionPolicy Bypass -File .\pack.ps1 -SetupOnly
# 只出便携包
powershell -ExecutionPolicy Bypass -File .\pack.ps1 -PortableOnly
# 只要 zip（跳过安装器）
powershell -ExecutionPolicy Bypass -File .\pack.ps1 -NoSetup
# 在线版（框架依赖）必须显式要
powershell -ExecutionPolicy Bypass -File .\pack.ps1 -OnlineOnly
```

产物落在 `dist\`（已 gitignore，不进仓库）：

- 版本号自动从 `Directory.Build.props` 的 `<Version>` 读，不用手写；安装器通过
  `ISCC.exe /DMyAppVersion=<ver> packaging\Capture.iss` 注入，脚本里不存第二份。
- 每个 zip 都带一份 `使用说明.txt`（`packaging\` 下的模板，写包时转成带 BOM 的 UTF-8，老记事本不乱码）。
- 便携包额外塞 `portable.txt`；安装版**故意不塞**，这样它写 `%AppData%\Capture`。
- 安装器内容来自 `artifacts\pack\install`（self-contained **多文件** 的 publish 输出；便携版是同一个
  源发布的单文件版本，两者只是 publish 参数不同，见 4 节）。

`publish.ps1` 与 `pack.ps1` 的分工：前者刷新仓库里的 `publish\`（开发/自用目录），后者产出可对外分发的包。

### 2.1 安装器（Inno Setup 6）

- 脚本：`packaging\Capture.iss`，**必须存成 UTF-8 with BOM**，否则 Inno 6 按 ANSI 读中文会乱码。
- `pack.ps1` 会自动找编译器，三个位置依次探测：
  `%LOCALAPPDATA%\Programs\Inno Setup 6\`、`%ProgramFiles%\Inno Setup 6\`、`%ProgramFiles(x86)%\Inno Setup 6\`。
  三个都没有时**只警告并跳过安装器**，便携包照常产出（不让一台没装 Inno 的机器整个打包失败）。
- 装 Inno Setup：`winget install --id JRSoftware.InnoSetup -e`，或 https://jrsoftware.org/isdl.php
- `AppId` 固定为 `{8C3F5D21-7A4B-4E9C-9B6F-2D1E5A4C8B70}`，**发布后不要再改**：升级安装靠它认出是同一个产品。
- 权限：`PrivilegesRequired=lowest` + `PrivilegesRequiredOverridesAllowed=dialog`，
  启动时问「为所有用户安装 / 仅为我安装」，默认按当前用户装、不弹 UAC。
- 开机自启**故意不做成安装任务**：程序内已有开关，安装器再写一份 Run 值会和它抢同一项。
- 卸载时**只**清 `HKCU\...\Run\Capture`，然后用 `SuppressibleMsgBox` 告诉用户设置和历史还在哪个
  目录，让用户自己决定要不要删。**绝不代替用户删数据**——保留数据的代价是手动删一个文件夹，
  删掉数据的代价是不可恢复的历史截图，这两者不对等。`/VERYSILENT` 下这条提示不会显示也不会
  阻塞卸载（这正是 `SuppressibleMsgBox` 存在的意义，外面不要再包一层判断）。
  > `[Code]` 里有一条红线：`WizardSilent` / `WizardForm` / `ShouldSkipPage` 等**只有安装阶段可用**，
  > 在卸载阶段调用会让卸载直接崩（`Internal error: Cannot call "WizardSilent" function during Uninstall`）。
  > 那段脚本里出现的 `WizardSilent` 只在注释里，作为这条红线的说明。

---

## 3. 发布前验收

改过代码或依赖后，至少跑这几条：

1. `dotnet build CaptureDemo.sln -c Release` → 0 错 0 警。
2. `dotnet build CaptureDemo.sln -c Debug` → 同样 0 错 0 警（`Dev\` 下的 `--ui-demo` / `--icons` / `--bench` 只在 Debug 编进去）。
3. `dotnet test CaptureDemo.sln -c Release` → 全绿（当前 125 个）。
4. 便携包：解压到空目录 → 双击 → 托盘出现 → 截一张图 → 确认 `settings.json` 出现在**这个目录**里，`%AppData%\Capture` 没有被新建。
5. 安装器：装一次 → 开始菜单有 `Capture` → 托盘出现 → 设置里改一个热键 → 卸载后确认
   `%AppData%\Capture` **仍在**（文件数、总字节数、`settings.json` 的 SHA256 三项与卸载前一致），
   且 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Capture` 已被删除。
   卸载脚本里没有删除用户数据的语句，所以「设置还在」不需要用户去点某个对话框保护。
6. 在线包（只在确实要发时）：在没装运行时的机器上双击 → 应提示安装 .NET 8 Desktop Runtime，而不是闪退。
7. Release 的 exe 上跑 `Capture.exe --bench` / `--icons` / `--ui-demo`，应该**什么都不发生**、正常启动主程序——
   这是「调试入口没漏进生产包」的现场验证。

---

## 4. 安装器细节（已实现）

- 形态：一个 Inno Setup 6 安装包（lzma2/ultra64 压缩），per-user 免 UAC，也可选 all-users：
  装完后磁盘上 249 个文件 / 150.1 MB，压进 46.50 MB 的包里。
- 安装位置：默认 `{autopf}\Capture`（per-user 时展开为 `%LOCALAPPDATA%\Programs\Capture`），
  `DisableDirPage=no`，用户可以改到任意目录。
- 内容：`artifacts\pack\install\*` 整个目录（**self-contained 但不是单文件**，装完 249 个文件 / 150.1 MB）。
  之所以放弃单文件形态，是 A/B 测出来的：同一份程序，多文件经 lzma2/ultra64 压缩后安装包**只有
  46.50 MB**，单文件形态要 58.6 MB，差 12 MB；而且多文件启动时不需要把原生库自解压到 `%TEMP%\.net\`，
  冷启更快，也不依赖临时目录。
- 便携包**仍是单文件**（解压后只有一个 66.5 MB 的 exe）：它不赢体积，赢的是「解压就能用、
  删文件夹就卸载干净」这件事不需要解释。便携 zip 57.97 MB 大于安装包 46.50 MB，
  是因为 zip 用 deflate，压缩率低于 lzma2/ultra64。
- 快捷方式：开始菜单 `{autoprograms}\Capture`；桌面快捷方式由 `[Tasks] desktopicon` 控制，默认勾选。
- `CloseApplications=yes`：覆盖安装时文件被占用会提示用户关闭，不强杀进程。
- `MinVersion=10.0.17763`（Win10 1809）：DWM 圆角属性、分层窗口脏矩形、`WDA_EXCLUDEFROMCAPTURE`
  都在这条线之后，低于它的系统跑起来会退化。
- 未签名：SmartScreen 会拦一次（「更多信息 → 仍要运行」），和 exe 本身一样。

---

## 5. 已知缺口与遗留限制

- **~~安装器 UI 是英文。~~** 已解决：`packaging\ChineseSimplified.isl`（取自 Inno Setup 官方仓库
  `Files/Languages/`，逐字未改）已在 `[Languages]` 里以 `Name: "chinesesimplified"` 挂上，向导页面、
  按钮、卸载提示都是中文。自定义的卸载提示本来就是中文。
- **~~没有 `.ico`，只能用 Inno 默认图标。~~** 已解决：`src\CaptureDemo\Assets\Capture.ico`
  （16/32/48/64/128/256 六档齐备，其中 16 与 32 是单独重绘而不是把大图缩放）已在 `[Setup]` 里以
  `SetupIconFile` 指向它；exe 自身的图标由 `<ApplicationIcon>` 提供。托盘与窗口图标仍然是
  `Native/IconFactory.cs` 运行时用 GDI+ 画的，因为它们要跟着明暗主题变。
- **未做代码签名**，首次运行会有 SmartScreen 提示（exe 和安装器各一次）。
- **只出 win-x64**，ARM 设备靠 x64 仿真跑。
- 安装版与便携版可以同时跑，但**共用同一组全局热键**：后启动的那份会报「热键被占用」，属预期行为。
- **索引读不出来的那一轮，新复制的条目不落盘。** 当 `index.json` 与 `index.bak.json` 都读不出来
  （被独占锁定或内容损坏）时，程序进入只读历史模式：本轮新复制的条目只在内存里可见，退出后不保留，
  也不做孤儿清扫；重启后若备份可读则历史恢复、写入也随之恢复。这条路径很少走到，但一旦走到，
  宁可不写也不能用空数据覆盖掉一份本来完好的索引。

