# Capture 交接文档

> 交接日期：2026-10-28（Asia/Shanghai）  
> 产品名（当前）：**Capture**（v1.3.0；内部命名空间与 exe 仍叫 CaptureDemo）  
> 目标读者：接手到 **本机 Cursor Agent** 继续开发的同事（或本机自己）  
> 仓库形态：.NET 8 WinForms 绿色工程；云端曾在 Cursor Origin 草稿仓上迭代（agent `bc-f0195be6-8390-5a70-bca7-97bbb2557d95`），本 zip 以**当前可编译源码 + 预发布包**为准，不依赖云端仓是否已 publish。

本文尽量把「已经做成什么、怎么做成的、怎么跑、已知坑、产品取向」写全，方便本地 Agent 不依赖聊天上下文直接开工。更偏实现细节的图见同目录 [ARCHITECTURE.md](ARCHITECTURE.md)；面向用户的操作说明见 [README.md](README.md)。

---

## 1. 产品背景与取向

产品负责人 **Guth** 要做一款 **Windows 轻量截图工具**，厌恶市面工具功能冗杂、付费墙、托盘后约 500MB 内存。

**优先做 / 已做：**

- 元素截图（UX 参照 GitHub 上 Snow Shot 的体验，**绝不拷贝其 GPL 代码**）
- 长截图（选区滚动 + overlap 拼接；固定栏尽量不重复）
- 四个基础能力：全屏、矩形、窗口、任意形状
- 成品向 UI：顶栏胶囊、设置、托盘常驻

**刻意不做 / 后做：** 单纯矩形/窗口/全屏若只靠系统自带已够用时可不优先；OCR、录屏、云同步、账号体系均未做。

**许可红线：**

- 自有代码：**MIT**
- 元素命中：NuGet **FlaUI 5.x（MIT）**，勿把第三方源码 vendoring 进仓
- **禁止**直接拷贝 Snow Shot（GPL-3.0）、ShareX（GPL）等源码；只允许公开 UX 思路级参照

**质量标准（2026-10-28 起明确）：** 已从「功能可用 demo」改为 **向完整品靠拢**——UI 裁切、文案、托盘行为、图标圆滑度都要按成品标准改，不能只看单元测试绿。

---

## 2. 本 zip 里有什么

解压后建议目录（本包即按此打包）：

```text
CaptureDemo-handoff-YYYYMMDD/
  HANDOFF.md                 ← 本文件
  README.md                  ← 用户向说明（与源码仓一致）
  ARCHITECTURE.md            ← 模块与算法说明
  CaptureDemo.sln
  publish.ps1 / run-element.ps1 / run-long.ps1
  src/
    CaptureDemo/             ← WinForms 宿主（UI、钩子、会话）
    CaptureDemo.Core/        ← 无 Win32：拼接、设置模型、标注光栅
  tests/
    CaptureDemo.Core.Tests/  ← 可在 Linux/macOS/Windows 跑的核心测试
  publish/                   ← 已编好的 win-x64 框架依赖发布目录
    CaptureDemo.exe
    *.dll / *.json
  CaptureDemo-win-x64.zip    ← 仅 publish/ 的压缩副本，便于只发运行包
  CaptureDemo-src.zip        ← 源码仓快照压缩副本（便于整包归档）
```

**给本机 Agent 的最短路径：** 解压 → 用 Visual Studio / `dotnet` 打开 `CaptureDemo.sln` → 在 Windows 上 `dotnet run --project src/CaptureDemo` 或直接跑 `publish\CaptureDemo.exe`。

---

## 3. 运行环境

| 场景 | 要求 |
| --- | --- |
| 跑 `publish\CaptureDemo.exe` | **Windows 10/11 x64** + [.NET 8 **Desktop** Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) |
| 改代码并编译 | 同上 OS + [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) |
| 只跑核心单测 | 任意有 .NET 8 SDK 的机器（含 Linux CI）：`dotnet test` |
| Linux 交叉编 WinForms | 工程已 `EnableWindowsTargeting`，**能编不能跑 GUI** |

其它：

- 清单声明 **Per-Monitor V2 DPI**；钩子坐标、UIA 边界、`CopyFromScreen` 统一按物理像素处理。
- 配置文件：`%AppData%\Capture\settings.json`（旧 `%AppData%\CaptureDemo\settings.json` 首次启动自动迁移）
- 默认输出：`%USERPROFILE%\Pictures\Capture\`（目录空时回落此路径）
- 空闲不常驻 OCR、不常驻捕获会话；元素模式结束释放 UIA。

---

## 4. 功能清单（当前实现）

### 4.1 壳与生命周期

- 启动后进 **系统托盘**，默认不占任务栏。
- 主窗口关闭 = **隐藏到托盘**，不是退出。
- 托盘 **左键**：打开主窗口。
- 托盘 **右键**：打开主窗口 / 截图 / 长截图 / 元素截图 / 剪贴板 / 设置 / 退出，右侧带热键提示。  
  - **设置只开设置窗，不得连带拉起主窗**（已修过一次回归，接手时请回归测）。  
  - 只有「退出」注销热键并结束进程。
- **单实例**：第二个进程不建窗，只通过 `RegisterWindowMessage` 广播唤起已有实例。
- 主窗口成品化：CaptureMark 品牌标 + 标题、开始截图、三个快捷 chip、剪贴板卡、设置、打开保存目录。

### 4.2 全局热键（可在设置改）

默认：

| 热键 | 作用 |
| --- | --- |
| `Ctrl+Alt+S` | 唤起顶部截图胶囊（默认选中矩形） |
| `Ctrl+Alt+E` | 唤起同一胶囊，预选「元素截图」 |
| `Ctrl+Alt+L` | 唤起同一胶囊，预选「长截图」 |
| `Ctrl+Alt+V` | 打开剪贴板历史面板 |
| `Esc` | 取消当前覆盖层；长截图中 = 停止并 **保存已拼接部分** |

### 4.3 顶部截图条（成品向）

唤起后全屏约 **60% 压暗**（alpha≈153），顶中央 **胶囊条**（46px 高、40px 按钮、约 26px 图标、圆角、近黑半透明），尺度对齐 Windows 截图工具。36px 那版被反馈偏小，整条放大了一档；标注条仍是 36px（21 个格子）。

六模式 **平铺、不折叠**（因本产品无录屏/OCR 等旁路，不需要 Windows 那种下拉收纳）：

1. 全屏  
2. 矩形  
3. 窗口  
4. 长截图  
5. 元素截图  
6. 任意形状  

当前模式是一块**填充的蓝色圆角底板 + 白图标**（原来是细下划线）。这不是装饰：选中长截图/元素截图后胶囊不再销毁，而是留在原地等框选，需要持续可见的选中态。悬停提示在图标下方；右侧齿轮 + 关闭（提示含 Esc）。

**长截图图标（1.3.1 改）**：原图形是「上方封闭圆角矩形 + 下方单根居中竖线 + 箭头横臂」，和 ♀ 同构，被用户指出。现在改成**上下两块宽扁屏幕 + 上屏一条内容线**（`SnipIcons.LongPaths`）：上屏 y3.4–10.0、下屏 y14.0–20.6，各高 6.6、缝 4.0、宽 17.2、圆角 1.4，垂直水平都居中于 12。
几何余量很紧，改之前先看这几个数：26px（100% DPI 胶囊）两块屏之间只剩 **1 个纯背景行**，内容线与上下边框各只剩 **1px 净空**——再放大缝、或加粗 `Weight`、或给内容线加粗，就会糊成一坨。QA 实测该格是全条胶囊里墨量最重的一格（比窗口图标重约 38%），属观感偏重不是缺陷；若哪天觉得太重，删掉第 3 段内容线即可退回「两块纯屏」，但会读成「两个抽屉」，不建议。
`Dev/IconSheet.cs` + `--icons <folder>` 是这套图标的候选对照表（26px/40px/52px/16px/浅底/整条胶囊六列），改任何 glyph 都可以先在那里加一行试，再落到 `SnipIcons`。

**模式切换不再结束会话**：六个模式都在同一个 `SnipSession` 里，切模式只改高亮 + 覆盖层提示。长截图拖完矩形后才 `Finish(Follow=Long, preset)` 交给滚动拼接；元素截图直接在同一个覆盖层里做 UIA 悬停（`Element/ElementHover.cs`，滚轮换层级），单击才交出去。三个热键、托盘项、主窗口按钮都走 `MainForm.StartSnipAsync(SnipMode initial)` 这一个入口。

### 4.4 基础四种截图 + 标注条

选区确认后（全屏则在画面底），选区下出现 **同风格标注胶囊**：

顺序（五组，靠 gap 分隔）：

**长截图** ｜ 选择 · 画笔 · 直线 · 箭头 ｜ 矩形 · 圆角矩形 · 椭圆 · 圆 ｜ 高亮 · 马赛克 ｜ 颜色 · 粗细 · 撤销 · **✓** · **✗**

- 只有 **✓** 才按设置写 PNG（默认可同时复制剪贴板）。  
- **✗ / Esc** 丢弃，不落盘。  
- 标注条「长截图」用 **当前矩形** 直接进入滚动会话（任意形状用外接矩形）。  
- 十种工具：`Select / Pen / Line / Arrow / Rect / RoundRect / Ellipse / Circle / Highlight / Mosaic`。自由笔画是采样路径，其余是"拖对角即定形"，`Circle` 强制正圆。  
- 八色预设 + 四档粗细，**不做色盘**。改色/改粗细会同时作用于当前选中对象。  
- **选择工具**：单击命中 → 框内拖动＝移动，八个手柄＝自由缩放（线宽不随缩放变化）。拖动时每帧从拖动起点快照重建几何，避免自由笔画反复相对变换累积取整误差。  
- 标注模型在 `CaptureDemo.Core/AnnoItem.cs`（纯几何：Bounds / Move / SetBounds / DragHandle / HitTest / Clone），不含任何绘制代码，可单测。

模式细节：

| 模式 | 行为摘要 |
| --- | --- |
| 全屏 | 鼠标所在显示器 `rcMonitor`（**含任务栏**），非多屏拼接 |
| 矩形 | 拖矩形；洞外暗、洞内原亮度 |
| 窗口 | 悬停高亮顶层窗，单击；`DWMWA_EXTENDED_FRAME_BOUNDS` 去阴影 |
| 任意形状 | 自由路径；PNG 路径外透明；进长图时用外接矩形 |

### 4.5 元素截图

- FlaUI UIA3 `FromPoint` / `ElementFromPoint`；沿 Parent 链；滚轮切换父子层级。  
- **已经并入统一覆盖层**：`Element/ElementHover.cs` 只负责「指针下是哪个控件 / 滚轮换到哪一层」，`SnipSession` 负责画洞和提示。胶囊条在这一步保持可见、模式保持高亮。  
- 覆盖层点击穿透 + 低级钩子吃左键/滚轮，避免点穿。  
- UIA 不可用（锁定环境 / Provider 崩）时自动退回矩形模式并在提示条上说明原因，不会把胶囊停在一个死掉的模式上。  
- 单击后走 `SnipFollow.Element` + `preset`，`ElementCaptureSession.RunAsync(settings, preset)` 直接抓那一块，**不会让用户选第二次**；`preset` 为空时才走它自带的拖矩形覆盖层（老入口保留）。  
- 截取前藏蒙版；`WDA_EXCLUDEFROMCAPTURE` 双保险。

### 4.6 长截图（重点算法）

交互要点：

- 拖选后 **蒙版不撤**；**亮洞锁定用户松开时的矩形**（不得再被「运动区域检测」改成窄条/两截亮带——曾是严重 UX bug）。  
- 洞内实时看到滚动内容（区域录屏隐喻，实际仍是抓帧拼接）。  
- 自动滚动：独立线程连续注入小步滚轮（速度 1 ≈ 1000 wheel units/s，约 4ms 一片、单次≤40）；**抓帧时也不停注入**。  
- 用户真滚轮 → **永久切手动**，注入立刻停。  
- 内部可对「真正在动的内容」收缩抓取/瞄准（避开 ribbon/任务栏），但 **UI 洞形状不变**。  
- Sticky 顶/底栏：相邻帧静止带检测，中间段才做 overlap，固定条只留第一帧一份。  
  - 一行的差异若全挤在 ≤25% 宽的窄列里（状态栏的页码 / 字数 / 缩放百分比），仍按「没动」处理，底边最多再向内吃 64 行（`MaxLocalRun`）。否则只有状态栏最后十几行被认出来，上面那截带页码的会被当正文反复拼接，几帧后匹配错位、新增 0 行、捕获提前结束。
  - 先检测固定带再裁剪：`MotionBand.SuggestCrop(..., keepTop, keepBottom)` 对有固定带的边不再裁到底，避免状态栏只剩 8 行残边。
  - `Lock` 只接受第一次的非零值：白页处处静止，允许每帧重测会把整幅吞掉。
- 位移搜索：`StitchOptions.SearchWindowFactor`（默认 2.2）先在「上一帧滚过的距离」附近找，窗口内最佳得分仍属低置信才回退全局。白页底部那一截在任何位移上都对得上，全局搜索会选到「整帧重叠」→ 新增 0 行。窗口上界强制 ≤ `nextHeight - 1`，整帧重叠永远不会是窗口的答案。
- 停止：无新行连续 `ZeroAppendLimit`（默认 3）次、两帧完全相同连续 2 次、高 20000、帧 150、Esc。「新增 0 行」不再一帧即停——状态栏读数变化或某个采样恰好没滚动都会造成单帧 0 新增。

主参数（设置 / settings.json）：滚动速度 1–10、采样间隔 ms（默认 120）。

### 4.7 设置页

分组：快捷键 / 保存 / 剪贴板 / 长截图 / 常规（常规里依次是外观、开机自动启动、**关闭窗口后常驻托盘**）。  
要求：无大段说明废话、标签不截断、「浏览…」完整；勿再出现文字重叠绘制 bug。  
实现：`ShellForm` + `ScrollSheet` + `CardPanel` 卡片；下拉是自绘 `Picker`（原生 `ComboBox` 的 3D 箭头和卡片风格不搭）；开关是 `ToggleSwitch`，**与输入框同为左对齐**（曾被居中放在编辑列，读起来像错位）。

**窗口高度固定**（`S(672)`，小屏时收敛到工作区），内容超长时由 `Ui/ScrollSheet` 滚动：

- 内容放在内层 `Panel` 上，滚动只平移这一个面板（`Top = -offset`），卡片不重排，每帧一次 `SetWindowPos` + 滑块重绘。
- 滚动条是自绘的 8dip 圆角滑块，不用原生条：17px 的系统灰贴在圆角卡片旁边像补丁，而且无法跟随深色主题。
- 滚轮走 `IMessageFilter` 提前拦截：单行 `TextBox` 会吞掉 `WM_MOUSEWHEEL`，否则光标停在输入框上时页面就滚不动了。
- Tab 到视口外的控件会自动滚过去（`EnsureVisible` 递归挂 `Enter`）。
- **版本号在滚动区最末尾**，贴底居中。加设置项只会让内容变长，不会再改窗口尺寸。

「关闭窗口后常驻托盘」默认开：关窗口只是回托盘，热键继续有效。关掉它之后，关窗口就是退出——`MainForm.OnFormClosing` 会 `AllowExit + BeginInvoke(ExitApp)`，把全局热键一并释放，方便让给别的软件，不会留下一个看不见却占着热键的进程。标签列宽 `S(136)` 是为了这行 9 个汉字不被折成两行。

两个踩过的布局坑，改这块前先读：

1. `Dock=Fill` + `Dock=Bottom` 混用时，Fill 会先占满整个客户区，Bottom 只是盖在上面——内容被按钮条实心遮挡，看着像「卡片底部被切」。改成显式 `SetBounds` 堆叠。
2. `Controls.Add` 是追加到 Z 序**最下层**；后加的滑块被子面板整块盖住，画了但看不见，要 `BringToFront()`。

### 4.7.1 版本号

只有一处定义：仓库根 `Directory.Build.props` 的 `<Version>1.3.0</Version>`。`CaptureDemo.Core/AppInfo.cs` 从程序集 `AssemblyInformationalVersion` 读，并截掉 SourceLink 追加的 `+<commit>`。设置页页脚（当前显示 `Capture v1.3.0`）和 `--version` 都走这个值，不会出现界面与 exe 属性不一致。

### 4.8 剪贴板历史

- 监听：`AddClipboardFormatListener` + `WM_CLIPBOARDUPDATE`，130ms 防抖后读取，失败重试 6 次。  
- 支持三类：图片 / 文本 / 资源管理器文件。按内容指纹去重（图片走 32×32 像素哈希），重复项移回顶部。  
- 面板：`ClipboardForm` 分层窗口自绘，一次 8 行，滚轮翻页；表头显示条数，页脚「清空历史」+「双击打开 · Esc 关闭」。  
- 双击：文本 → 写回剪贴板；图片/文件 → 系统默认程序打开。右键二级菜单：打开/复制、复制到剪贴板、复制路径、另存为…、在文件夹中显示、置顶/取消置顶、删除、清空历史。  
- 置顶项优先排序；容量满先删最旧的未置顶项。自身写剪贴板有 500ms 回声窗口，不会生成新记录。  
- 落盘：`%AppData%\Capture\clipboard\`（旧目录首次启动 `Directory.Move` 过来）（PNG ≤4096 边长 + 正方形缩略图 + `index.json`），明文。  
- 纯逻辑（排序/去重/置顶/裁剪/索引往返）在 `CaptureDemo.Core/ClipboardHistory.cs`，有 13 个单测。

---

## 5. 技术路径总览

```text
MainForm（热键、托盘、主壳）
  └─ SnipSession
       SnipOverlayForm（暗角）
       CapturePillForm / PillChrome（顶栏六模式）
       AnnoPillForm（标注条）+ StylePillForm（色板/粗细浮层）
       AnnoLayerForm（覆盖选区的标注层，脏矩形增量渲染）
         └─ AnnoPainter（GDI+，预览与导出同一份绘制）
       ├─ 全屏/矩形/窗口/任意形状 → AnnoLayerForm.RenderTo → ✓ SavePng
       ├─ 长截图 → LongCaptureSession
       └─ 元素 → ElementCaptureSession
  LongCaptureSession
       RegionOverlayForm（洞锁定）
       ScrollAim + WheelScroll.Streamer
       MotionBand + ScrollAssembler
         StickyBandDetector + OverlapStitcher
  ElementCaptureSession
       ElementOverlayForm + ElementPicker（FlaUI）
ScreenGrabber（CopyFromScreen）
CaptureSaver（目录/模板/剪贴板）
Native（钩子、分层窗、WDA_EXCLUDEFROMCAPTURE、热键、DWM bounds）
```

**分层原则：**

- `CaptureDemo.Core`：**零 Win32**，可在非 Windows 上单测拼接/设置/标注/滚轮节奏。  
- `CaptureDemo`：WinForms + P/Invoke，只在 Windows 验证真实体验。

**关键实现关键词（搜代码）：**

| 主题 | 线索 |
| --- | --- |
| 胶囊 UI / 图标 | `Snip/PillChrome.cs`, `Snip/SnipIcons.cs`, `UiTheme.cs` |
| 会话编排 | `Snip/SnipSession.cs` |
| 蒙版洞 | `Capture/ScreenOverlayForm.cs`, `Capture/LayeredWindow.cs`, `OverlayPainter.cs` |
| 连续滚轮 | `Capture/WheelScroll.cs`, `Core/WheelPace.cs` |
| 拼接 | `Core/OverlapStitcher.cs`, `ScrollAssembler.cs`, `StickyBandDetector.cs`, `MotionBand.cs` |
| 设置 | `Core/AppSettings.cs`, `SettingsForm.cs` |
| 托盘 | `MainForm.cs` |

**屏幕采集：** 当前以 `Graphics.CopyFromScreen`（GDI）为主；未上 WGC/DXGI。若以后要抗遮挡/更高性能，可换采集后端但保持 BGRA 缓冲接口。

**滚动注入：** `mouse_event(MOUSEEVENTF_WHEEL)` 小增量流；注入带 `LLMHF_INJECTED`，钩子不误判为用户接管。

---

## 6. 构建、发布、测试命令

在仓库根（Windows PowerShell）：

```powershell
# 调试跑
dotnet run --project src/CaptureDemo/CaptureDemo.csproj
dotnet run --project src/CaptureDemo/CaptureDemo.csproj -- --snip
dotnet run --project src/CaptureDemo/CaptureDemo.csproj -- --element
dotnet run --project src/CaptureDemo/CaptureDemo.csproj -- --long

# 发布（覆盖 publish/）
powershell -ExecutionPolicy Bypass -File .\publish.ps1
# 自包含（更大）：
powershell -ExecutionPolicy Bypass -File .\publish.ps1 -SelfContained

# 脚本快捷
powershell -ExecutionPolicy Bypass -File .\run-element.ps1
powershell -ExecutionPolicy Bypass -File .\run-long.ps1
```

任意平台核心测试：

```bash
dotnet test
```

当前 **69** 个单测（设置、热键解析、标注几何与命中、剪贴板历史、拼接、WheelPace、PillMetrics 等）。**真实蒙版/钩子/滚轮必须在 Windows 手工验。**

---

## 7. 配置字段（settings.json）

路径：`%AppData%\Capture\settings.json`  
模型：`CaptureDemo.Core.AppSettings`（以源码为准）。典型字段：

- 唤醒 / 元素 / 长截图 / 剪贴板热键字符串  
- 保存目录、文件名模板（如 `{kind}-{yyyyMMdd}-{HHmmss}-{fff}`）  
- ✓ 时是否复制剪贴板  
- 长图速度、采样间隔  
- `WatchClipboard`（默认 true）、`ClipboardLimit`（5–30，默认 12）  
- `LaunchAtStartup`（HKCU\...\Run 开机自启）、`Appearance`（System/Light/Dark）

改 UI 时保持序列化兼容，或写迁移逻辑。

---

## 8. 已知限制（接手必读）

1. 无 UIA 树的界面（游戏、自绘、部分 Electron 内部）元素模式只能拖矩形。  
2. 截的是 **可见像素**；被遮挡会带上遮挡物。  
3. 长图只向下；不发横向滚轮 / PageDown。  
4. 老式「攒满 120 才滚」的控件（经典记事本等）仍可能一格一跳。  
5. 动画/过高固定栏仍可能重复进长图；尽量让用户别框进 sticky。  
6. 全屏 = 指针下单显示器；多屏需分别截。  
7. 任意形状 → 长图 = 外接矩形，不是镂空路径滚动。  
8. 混 DPI 多屏时，跨屏窗框可能差几像素。  
9. 高 20000 / 帧 150 硬顶。  
10. 剪贴板历史只认文本/图片/文件；富文本按纯文本记；明文落盘，最多 30 条。  
11. UI 仍在成品化过程中：图标、间距、设置页布局可能还要按 Windows 截图继续磨。

---

## 9. 重要历史决策（避免回退）

| 决策 | 原因 |
| --- | --- |
| 长图滚动时 UI 洞 **锁定用户矩形** | 曾用运动区域改洞 → 怪形状（两截亮带等） |
| 滚轮流 **抓帧不停** | 停一下滚一下有「段落感」；对标手机长截图丝滑 |
| 六模式 **平铺** | 无录屏/OCR，折叠无必要 |
| 关闭进托盘；设置不连带主窗 | 常驻截图工具预期 |
| 去设置页长说明、修截断/叠字 | 成品标准 |
| 不引入 GPL 源码 | 法务/可分发 |
| 图标：24×24 SVG 路径 + 统一描边 1.75，不手写像素魔数 | 一处改权重，整条栏一致 |
| `SvgPath` 变换用 `MatrixOrder.Append` | GDI+ 默认 Prepend 会让 offset 被 scale 再乘一次，大尺寸图标直接飞出画布（曾导致下拉箭头画在裁剪区外看不见） |
| 分层窗口上的文字用 GDI+ `DrawString` + `AntiAliasGridFit` | GDI ClearType 在预乘 alpha 面上有彩边 |
| 悬浮面板截图校验用 `CopyFromScreen` | `DrawToBitmap`/`WM_PRINT` 对透明自绘面板产出空白 |
| 设置页**固定高度 + 自绘滚动**，不再随内容长高 | 内容一多就把窗口撑出工作区，或者退化成原生滚动条；加一个设置项就改一次窗口尺寸，不可维护 |
| 设置页的滚动条**自己画** | 原生 17px 系统灰在圆角卡片旁像补丁，且无法跟随深色主题 |
| 版本号只有 `Directory.Build.props` 一处 | 界面写死一个、csproj 写一个，发版必然忘记其一 |
| 标注层是**独立窗口，尺寸＝选区**，不再复用全屏暗角窗 | 全屏窗每帧要重铺整幅遮罩 + 全幅 `UpdateLayeredWindow`，1080p 下光提交就 ~3 ms，多屏更差 |
| 标注预览走 **脏矩形增量**，不再每帧 CPU 全图重合成 | 旧路径每帧 `AnnotationComposer.Apply` + 全幅转位图 + 全屏提交，画笔 10 ms/帧、马赛克 25 ms/帧；拖动明显发涩。现为画笔 0.25 ms、图形 0.9 ms |
| 预览与导出**共用 `AnnoPainter`（GDI+）** | 之前预览与导出是两条绘制实现，容易"看到的不等于存下的" |
| `LayeredSurface` 缓存 DC/DIB 像素指针 | 旧 `LayeredWindow.Update` 每次都 `CreateDIBSection` + 逐行 `Marshal.Copy`（每行还新分配 byte[]） |
| 标注层拖动几何**每帧从起点快照重建** | 相对变换连乘会让自由笔画慢慢漂离指针 |
| 色板/粗细用**预设**，不做色盘 | 截图标注要的是快，不是全色域 |

用户反馈截图/视频曾保存在开发机 `capture-demo-feedback*`，本交接包未强制附带；需要视觉回归时可向负责人再要 Windows 截图工具顶栏参考图。

---

## 10. 建议本机 Agent 的下一步（可选）

产品未最终冻结，常见下一刀（**先问 Guth 或按其新反馈**，勿擅自扩 scope）：

已完成（此前几轮）：图标改为 24 网格 SVG 统一权重、设置页 Fluent 卡片化 + 自绘 `Picker`/`ToggleSwitch`、剪贴板历史、单实例、开机自启、深浅色主题、`--ui-demo` / `--preview` / `--crop` 三个 dev 校验钩子。

已完成（标注重构这一轮）：标注层独立窗口 + 脏矩形增量渲染（画笔 10→0.25 ms/帧）、十种标注工具（选择/画笔/直线/箭头/矩形/圆角矩形/椭圆/圆/高亮/马赛克）、八色预设 + 四档粗细、选择工具移动与八手柄自由缩放、`--bench` 性能基准 + `layer-shot.png` 实机证据。

已完成（设置页收尾这一轮）：窗口固定高度 + `ScrollSheet` 自绘滚动（滚轮 / 拖滑块 / 点轨道 / Tab 跟随）、页脚版本号（`Directory.Build.props` 单一来源）、`--version`、页脚细分隔线。顺带修掉 `Dock Fill+Bottom` 顺序与 `Controls.Add` Z 序两个布局 bug——前者正是「常规」卡片底部没圆角收尾的真正原因。

仍未做：

1. 采集后端评估（WGC / `PrintWindow(PW_RENDERFULLCONTENT)` 抓被遮挡窗口，失败回退 GDI）。  
2. 长图：横向、逆向、`PageDown` 兼容、浏览器专用滚动消息。  
3. 安装器 / 签名；若要连 exe 名一起改成 Capture.exe，需要同步 `publish.ps1`、`run-*.ps1`、`SingleInstance` 互斥名与已发出去的快捷方式。  
4. 若要把 Origin 草稿仓正式 publish：需负责人选定仓库名与可见性。

---

## 11. 手工回归清单（改 UI/托盘/长图后必跑）

在 Windows 11：

1. 托盘启动 → 关主窗仍在托盘 → 右键设置 **只出设置** → 退出才杀进程；设置里关掉「关闭窗口后常驻托盘」后，关窗口应直接退出且热键立刻失效。  
2. `Ctrl+Alt+S` → 六图标平铺、当前模式是蓝色底板、悬停名、Esc 关。  
3. 矩形 → 标注 → ✗ 无文件；✓ 有 PNG +（默认）剪贴板。  
4. 标注条「长截图」能进入滚动；洞外保持暗、洞形状不变。  
5. 整屏级长选区仍能滚出高于一屏的图；真滚轮后变手动。  
5.1 **底部固定栏**：Word 里把底部「第 N 页 / 共 M 页 · 字数 · 120%」那条状态栏一起框进来再滚。应能一直滚到文档末尾（不得几帧就停），状态栏在长图里只出现一次且完整；页码从 3 变到 4、5 时也不能提前结束。  
5.2 同一文档只框正文（不含状态栏）再滚一次，结果高度与 5.1 除状态栏外一致，不能因为这次改动变短或错行。  
6. 元素模式：点胶囊上的「元素截图」后**胶囊仍在**、底板亮着；记事本按钮可命中，滚轮能换层，单击后胶囊才消失并直接保存（只选一次）。  
6.1 `Ctrl+Alt+L` / `Ctrl+Alt+E` → 胶囊弹出且对应模式已选中；长截图拖完矩形才进入滚动，不是一按热键就跳走。  
6.2 标注条点「颜色」→ 选一个色 → **小组件不消失**，可连选；点画布任意处才收起，且那一下不落笔；Esc 第一下只关小组件。  
7. 设置改热键/目录 → 退出再开仍在；无文字截断/重叠；四个下拉都有 chevron，开关与输入框左边缘对齐。  
7.1 **设置页滚动**：窗口高度固定不随内容变；滚轮能滚（光标停在输入框上也要能滚）；拖右侧滑块、点轨道空白都能滚；Tab 一路按到底，焦点控件自动滚进视野。  
7.2 **设置页末尾**：滚到底后「常规」卡片必须有完整圆角收尾（不能贴平/被按钮条压住），下方居中显示 `CaptureDemo vX.Y.Z`，版本号与源码 `Directory.Build.props` 一致。  
8. 托盘右键：七项都有图标，右侧热键是灰色；「剪贴板」能开面板。  
9. 剪贴板：复制图片/文本/文件各一条 → 双击文本回剪贴板、双击图片开「照片」→ 右键二级菜单项齐全 → 置顶后仍排首位。  
10. **标注**：矩形选 800×600 以上的区域，用画笔快速划几下，笔迹要跟手、不掉帧、不断线；再拖一个椭圆和箭头，形状终点要严丝合缝跟着指针。  
11. **标注**：切到选择工具，单击任一标注出现虚线框 + 八个手柄；拖动移动、拖角缩放都跟手；按撤销删掉这一条。  
12. **标注**：点颜色换绿、点粗细换特粗，新画的线立即生效；选中已有标注再改色，改的是那一条。  
13. **标注**：✓ 存出的 PNG 与屏幕上看到的一致（颜色、线宽、马赛克位置）。  
14. `dotnet test` 全绿（当前 82 个，含版本号格式与来源校验、旧设置目录迁移、常驻开关默认值、胶囊放大与热点命中、状态栏读数变化、单帧卡住、白页位移窗口）。  
15. UI/标注改动后跑 `CaptureDemo.exe --ui-demo <folder>`，`main/settings/settings-*-bottom/clipboard/tray/clipmenu/capture-rectangle/capture-long/capture-element/anno/anno-colors/anno-weights/anno-sample`（深浅各一）都要看过再提交；`scroll.txt` 会打出视口/内容/偏移量，可作为布局回归的硬指标。  
16. 改渲染路径后跑 `CaptureDemo.exe --bench <报告.txt>`，确认画笔单帧仍在 1 ms 量级，并打开它顺带生成的 `layer-shot.png` 确认标注层真的显示在屏幕上。

---

## 12. 依赖与许可摘要

- 运行时：.NET 8 Windows Desktop  
- NuGet：FlaUI.UIA3 / FlaUI.Core（MIT）及传递依赖  
- 自有代码：MIT（见 LICENSE，若包内有）  
- 参照但不拷贝：Snow Shot 交互；拼接为自研 SAD overlap，非 ShareX/jietuba 移植  

---

## 13. 联系上下文

- 负责人偏好：中间问题自行决策，少反复确认；要可用包。  
- 时区：Asia/Shanghai。  
- 历史云端实现会话：`https://cursor.com/agents/bc-f0195be6-8390-5a70-bca7-97bbb2557d95`（若仍可访问，仅作历史；**以本 zip 源码为准**）。

— 文档结束。本地 Agent 请先读本文件 + ARCHITECTURE.md，再改代码；改动用户可见行为时更新 README 与本交接文档的「功能清单 / 已知限制」。
