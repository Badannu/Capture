# CaptureDemo 交接文档

> 交接日期：2026-10-28（Asia/Shanghai）  
> 产品名（当前）：CaptureDemo / 截图演示  
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
- 配置文件：`%AppData%\CaptureDemo\settings.json`
- 默认输出：`%USERPROFILE%\Pictures\CaptureDemo\`（目录空时回落此路径）
- 空闲不常驻 OCR、不常驻捕获会话；元素模式结束释放 UIA。

---

## 4. 功能清单（当前实现）

### 4.1 壳与生命周期

- 启动后进 **系统托盘**，默认不占任务栏。
- 主窗口关闭 = **隐藏到托盘**，不是退出。
- 托盘 **左键**：打开主窗口。
- 托盘 **右键**：打开主窗口 / 截图 / 设置 / 退出。  
  - **设置只开设置窗，不得连带拉起主窗**（已修过一次回归，接手时请回归测）。  
  - 只有「退出」注销热键并结束进程。
- 主窗口成品化：标题、开始截图、设置、打开保存目录、短状态行（不要堆调试说明）。

### 4.2 全局热键（可在设置改）

默认：

| 热键 | 作用 |
| --- | --- |
| `Ctrl+Alt+S` | 唤起顶部截图胶囊（默认进矩形相关流程） |
| `Ctrl+Alt+E` | 直接元素截图 |
| `Ctrl+Alt+L` | 直接长截图 |
| `Esc` | 取消当前覆盖层；长截图中 = 停止并 **保存已拼接部分** |

### 4.3 顶部截图条（成品向）

唤起后全屏约 **60% 压暗**（alpha≈153），顶中央 **矮胶囊条**（约 36px 高、约 16px 图标、圆角、近黑半透明），风格对齐 Windows 截图工具的位置/尺度。

六模式 **平铺、不折叠**（因本产品无录屏/OCR 等旁路，不需要 Windows 那种下拉收纳）：

1. 全屏  
2. 矩形  
3. 窗口  
4. 长截图  
5. 元素截图  
6. 任意形状  

当前模式蓝色短下划线；悬停提示在图标下方；右侧齿轮 + 关闭（提示含 Esc）。图标已按圆角描边重画一轮，仍可能继续打磨。

### 4.4 基础四种截图 + 标注条

选区确认后（全屏则在画面底），选区下出现 **同风格标注胶囊**：

顺序大致： **长截图（第二入口）** → 画笔 → 箭头 → 高亮 → 马赛克 → 撤销 → **✓** → **✗**

- 只有 **✓** 才按设置写 PNG（默认可同时复制剪贴板）。  
- **✗ / Esc** 丢弃，不落盘。  
- 标注条「长截图」用 **当前矩形** 直接进入滚动会话（任意形状用外接矩形）。  
- 画笔/箭头红细线；高亮半透明黄；马赛克块化；撤销丢最后一笔（从原图重放笔划列表）。

模式细节：

| 模式 | 行为摘要 |
| --- | --- |
| 全屏 | 鼠标所在显示器 `rcMonitor`（**含任务栏**），非多屏拼接 |
| 矩形 | 拖矩形；洞外暗、洞内原亮度 |
| 窗口 | 悬停高亮顶层窗，单击；`DWMWA_EXTENDED_FRAME_BOUNDS` 去阴影 |
| 任意形状 | 自由路径；PNG 路径外透明；进长图时用外接矩形 |

### 4.5 元素截图

- FlaUI UIA3 `FromPoint` / `ElementFromPoint`；沿 Parent 链；滚轮切换父子层级。  
- 覆盖层点击穿透 + 低级钩子吃左键/滚轮，避免点穿。  
- 无控件树 → 提示后允许同一次会话拖矩形回退。  
- 截取前藏蒙版；`WDA_EXCLUDEFROMCAPTURE` 双保险。

### 4.6 长截图（重点算法）

交互要点：

- 拖选后 **蒙版不撤**；**亮洞锁定用户松开时的矩形**（不得再被「运动区域检测」改成窄条/两截亮带——曾是严重 UX bug）。  
- 洞内实时看到滚动内容（区域录屏隐喻，实际仍是抓帧拼接）。  
- 自动滚动：独立线程连续注入小步滚轮（速度 1 ≈ 1000 wheel units/s，约 4ms 一片、单次≤40）；**抓帧时也不停注入**。  
- 用户真滚轮 → **永久切手动**，注入立刻停。  
- 内部可对「真正在动的内容」收缩抓取/瞄准（避开 ribbon/任务栏），但 **UI 洞形状不变**。  
- Sticky 顶/底栏：相邻帧静止带检测，中间段才做 overlap，固定条只留第一帧一份。  
- 停止：几乎无位移（稀疏变化规则）、无新行、高 20000、帧 150、Esc。

主参数（设置 / settings.json）：滚动速度 1–10、采样间隔 ms（默认 120）。

### 4.7 设置页

分组：快捷键 / 保存 / 长截图 / 常规。  
要求：无大段说明废话、标签不截断、「浏览…」完整；勿再出现文字重叠绘制 bug。

---

## 5. 技术路径总览

```text
MainForm（热键、托盘、主壳）
  └─ SnipSession
       SnipOverlayForm（暗角）
       CapturePillForm / PillChrome（顶栏六模式）
       AnnoPillForm（标注条）
       ├─ 全屏/矩形/窗口/任意形状 → AnnotationComposer → ✓ SavePng
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

当前约 **32** 个单测（设置、热键解析、标注、拼接、WheelPace、PillMetrics 等）。**真实蒙版/钩子/滚轮必须在 Windows 手工验。**

---

## 7. 配置字段（settings.json）

路径：`%AppData%\CaptureDemo\settings.json`  
模型：`CaptureDemo.Core.AppSettings`（以源码为准）。典型字段：

- 唤醒 / 元素 / 长截图热键字符串  
- 保存目录、文件名模板（如 `{kind}-{yyyyMMdd}-{HHmmss}-{fff}`）  
- ✓ 时是否复制剪贴板  
- 长图速度、采样间隔  

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
10. UI 仍在成品化过程中：图标、间距、设置页布局可能还要按 Windows 截图继续磨。

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

用户反馈截图/视频曾保存在开发机 `capture-demo-feedback*`，本交接包未强制附带；需要视觉回归时可向负责人再要 Windows 截图工具顶栏参考图。

---

## 10. 建议本机 Agent 的下一步（可选）

产品未最终冻结，常见下一刀（**先问 Guth 或按其新反馈**，勿擅自扩 scope）：

1. 顶栏/标注条图标与 Windows 11 截图像素级对齐（间距、阴影、hover）。  
2. 设置页 Fluent 化（卡片、对齐、热键录制体验）。  
3. 启动项 / 开机托盘；单实例互斥。  
4. 采集后端评估（WGC）与多屏策略。  
5. 长图：横向、逆向、浏览器专用滚动消息。  
6. 安装器 / 签名 / 改正式产品名（勿一直叫 Demo）。  
7. 若要把 Origin 草稿仓正式 publish：需负责人选定仓库名与可见性。

---

## 11. 手工回归清单（改 UI/托盘/长图后必跑）

在 Windows 11：

1. 托盘启动 → 关主窗仍在托盘 → 右键设置 **只出设置** → 退出才杀进程。  
2. `Ctrl+Alt+S` → 六图标平铺、蓝线、悬停名、Esc 关。  
3. 矩形 → 标注 → ✗ 无文件；✓ 有 PNG +（默认）剪贴板。  
4. 标注条「长截图」能进入滚动；洞外保持暗、洞形状不变。  
5. 整屏级长选区仍能滚出高于一屏的图；真滚轮后变手动。  
6. 元素模式：记事本按钮可命中；桌面空白可拖矩形回退。  
7. 设置改热键/目录 → 退出再开仍在；无文字截断/重叠。  
8. `dotnet test` 全绿。

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
