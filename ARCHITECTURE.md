# 架构

截图条负责选区和标注，长图和元素仍走各自的会话。核心拼接和标注光栅不引用 WinForms，方便在没有 Windows 的环境里用单元测试锁住结果。

```text
MainForm（热键、设置）
  └─ SnipSession
       SnipOverlayForm（点击穿透、全虚拟屏、暗角）
       CapturePillForm（顶部胶囊：六个模式图标、设置、关闭）
       AnnoPillForm（选区下同一材质的标注条）
       ├─ 全屏 / 矩形 / 窗口 / 任意形状 → AnnotationComposer → ✓ 才 SavePng
       ├─ 长截图 → LongCaptureSession（可带已经选好的矩形）
       └─ 元素 → ElementCaptureSession
  ElementCaptureSession
    ElementOverlayForm + ElementPicker（FlaUI UIA3）
  LongCaptureSession
    RegionOverlayForm（选区蒙版在滚动期间保持，洞不改形状）
    ScrollAim + WheelScroll（对准在动的区域；滚轮在抓屏期间也不停）
    MotionBand + ScrollAssembler → StickyBandDetector + OverlapStitcher
ScreenGrabber（Graphics.CopyFromScreen）
CaptureSaver（按 AppSettings 写 PNG，可选剪贴板）
```

## 模块

| 路径 | 职责 |
| --- | --- |
| `src/CaptureDemo.Core` | `BgraBuffer`、底部横带 SAD、固定边检测、`ScrollAssembler`、`WheelPace`、`AnnotationComposer`、`AppSettings` / 热键解析。无 Win32。 |
| `src/CaptureDemo/Snip` | 胶囊截图条、模式下拉、标注条、矩形 / 窗口 / 全屏 / 任意多边形。 |
| `src/CaptureDemo/Element` | 悬停高亮、滚轮换层级、拖拽矩形回退。 |
| `src/CaptureDemo/LongShot` | 选区、连续自动滚动 / 手动接管、调用拼接器。 |
| `src/CaptureDemo/Capture` | 屏幕裁切、BGRA 互转、保存、分层蒙版。 |
| `src/CaptureDemo/Native` | 钩子、虚拟屏、`UpdateLayeredWindow`、`WDA_EXCLUDEFROMCAPTURE`、热键、窗口外框。 |
| `src/CaptureDemo/MainForm.cs` | 主窗口、托盘、热键注册。设置对话框写 `%AppData%\Capture\settings.json`（便携版写 exe 同级；旧 `%AppData%\CaptureDemo\` 首次启动自动迁移）。关闭主窗口只是隐藏；托盘「退出」才卸热键并结束进程。 |

## 截图条

`SnipSession` 在暗角上叠分层绘制的胶囊条（`UpdateLayeredWindow`，圆角、浅阴影、近黑半透明）。六个模式图标从左到右是全屏、矩形、窗口、长截图、元素截图、任意形状，当前项有蓝色下划线。右侧是设置和「关闭(Esc)」。悬停提示画在图标正下方。选区下方的标注条用同一套尺寸和图标粗细。低级钩子会吃掉左键，但落在胶囊或标注条上的点击会放行。这些窗口和暗角都排除在截图之外。

托盘「设置」只弹出设置窗口。主窗口保持隐藏，除非点「打开主窗口」或左键托盘图标。

主窗口启动时不显示在任务栏。`NotifyIcon` 左键恢复窗口，右键提供打开、截图、设置和退出。

全屏用 `MonitorFromPoint` 的 `rcMonitor`（含任务栏）。窗口用 `WindowFromPoint` → 根窗口，再用 `DWMWA_EXTENDED_FRAME_BOUNDS` 去掉阴影。任意形状先取外接矩形，再用扫描线把多边形外的 alpha 清成 0。

标注是 `AnnotateStroke` 列表：画笔、箭头、半透明高亮、马赛克。每次重画都从原图重放，撤销就是丢掉最后一笔。✓ 把结果交给 `CaptureSaver`；✗ 直接结束。文件名模板在 `AppSettings.FormatFileName`。

从标注条进入长截图时，把当前矩形交给 `LongCaptureSession.RunAsync(settings, preset)`，跳过再拖一次。屏幕上的洞仍然锁在这个矩形上。

## 选择蒙版

元素模式和长图选区共用 `ScreenOverlayForm`。窗口仍是置顶、点击穿透的工具窗口，并用 `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` 让 GDI 截屏看不到它。颜色键做不到「外面变暗、里面原亮度」，所以不再使用 `TransparencyKey`。每帧把一张预乘 alpha 的 32 位图交给 `UpdateLayeredWindow`（`AC_SRC_ALPHA`）：整屏先填约 60% 黑（alpha 153），选区矩形再写成 alpha 0 的洞，亮边画在洞的外侧。`Bitmap.GetHbitmap` 会丢掉 alpha，因此像素经 `CreateDIBSection` 拷进设备位图。穿透之后窗口收不到鼠标，所以用低级鼠标钩子读坐标。

## 元素模式

覆盖层就是上面的蒙版。

每次指针移动（合并到一次 UI 调度，避免把 UIA 调用堆进钩子里）：

1. `UIA3Automation.FromPoint`，即 UI Automation 的 `ElementFromPoint`。
2. 丢掉本进程的节点，再沿 `Parent` 走到桌面或 32 层为止。运行时 ID 用来打断环。
3. 桌面类名 `#32769` / `Progman` / `WorkerW` 不算可用控件。
4. 滚轮在这条链上的可用节点之间循环。向上是父级。

左键截取当前高亮的屏幕矩形。没有可用节点时不关闭会话，提示后允许在同一次里拖矩形。截取前先隐藏覆盖层并等大约 120ms；显示亲和性是第二道保险。

坐标按 Per-Monitor V2 处理：钩子、UIA `BoundingRectangle` 和 `CopyFromScreen` 都用物理像素。不建 R 树，演示只做实时点查。

这是 FlaUI（MIT）的公开 API，加上自己的父链循环。没有复制 Snow Shot 的 Rust 代码。Snow Shot 的公开行为（点到控件、滚轮改选区层级、再按矩形裁屏）只作为交互参照。

## 长截图

选区同样走这层蒙版，而且**松开之后蒙版留着**。洞外继续约 60% 黑，洞内是桌面本身，也就是当前正在截的那一屏。状态文字画在洞外的暗区上（整屏没有暗边时贴在顶上）。覆盖层有 `WDA_EXCLUDEFROMCAPTURE`，抓屏时不把它藏起来，避免画面闪回全亮。不再使用单独的 HUD 或底部缩略图。

然后：

1. `CopyFromScreen` 取当前矩形，转成紧密的 BGRA，立刻释放 `Bitmap`。
2. 若还没收缩过**内部抓取矩形**，`MotionBand` 找出相对上一帧真正变过的行和列。静态外框够厚时，只把这块收成拼接用的抓取范围，并从裁过的两帧重新开始。蒙版的洞始终是松开鼠标时的那个矩形，不会跟着运动区域改形状。这样最大化的 Word 加上任务栏不会拿功能区当模板，屏幕上的选区框也不变。
3. `ScrollAssembler` 再看有没有不动的顶边或底边，然后拼接或停止。
4. **自动**时，滚轮落点优先用这块运动区域的中心，并且夹在当前显示器的工作区里（不打在任务栏上）。还没看见运动时，会在抓取范围高度的约 30%、42%、50%、62% 之间换点。只有落点变化时才移动光标、把窗口拉到前台并等待约 15 毫秒。`WheelScroll.Streamer` 在单独线程上一直发 `mouse_event(MOUSEEVENTF_WHEEL)`，抓屏和拼接期间也不停。`WheelPace` 按速度累计滚轮单位（速度 1 = 每秒 1000，即原来 3×120 / 180 ms 的一半），大约每 4 毫秒一段，单次最多 40，并先把计时分辨率调到 1 毫秒。默认采样 120 毫秒因此大约滚过从前的 1 格，但是摊在整段时间里，中间没有停顿。调度卡住时单次最多补 32 毫秒，避免一串大增量把重叠吃掉。注入的滚轮带 `LLMHF_INJECTED`，钩子不会把它当成用户操作。
5. **手动**：真实滚轮（钩子不吞掉，页面自己滚）会把本次捕获锁成手动。正在滑行的注入立刻停，之后不再自动滚，也不会把光标拽回去。等滚轮安静一个采样间隔之后再抓下一帧。`Esc` 仍然取消并保存已有的图。

停止：

- 平均通道差已经 ≤ 2，**并且**超过阈值的行数、列数都很少。只看整帧平均会把「白页 + 功能区 + 一条窄正文」误判成没动，整屏长截图因此在一两帧后停掉。
- 最佳对齐没有带来新行（同一条件）。正文还没开始长高之前，这个条件要连续出现 8 次才停，给换落点留时间；一旦高度已经涨过，一次就停。
- 累计高度达到 20000
- 帧数达到 150（防止动画把循环拖死）
- `Esc` 取消令牌；已有的累计图仍会保存

## 行重叠

`OverlapStitcher` 自己实现，不移植 ShareX `ScrollingCaptureManager` 或 jietuba `longstitch`。

对候选重叠高度 `o`：

- 模板是累计图最底部的一条横带（默认最多 64 行；重叠更短时用整个重叠段）。
- 模板与新帧里紧挨接缝的那段比较，也就是新帧中本应已经见过的那些行。
- 代价是抽样像素的 B/G/R 平均绝对差。列步长 4，行步长 2。右边约 18 像素默认不参与，用来躲开滚动条；剩下的列太少时改比整行。
- 代价明显最低的 `o` 胜出。若几个 `o` 几乎一样，优先贴近上一帧滚过的行数，否则取得最大的重叠，少重复拼接。
- 最好的代价仍然高于阈值（默认 12）时，不把「整帧都算重叠」当成到底。没有历史步长时只保留最小重叠，避免误停；有历史步长时沿用它，并标成低置信度。

然后只把重叠线以下的行接到累计图上。宽度不一致时取较窄的左侧。超过高度上限的新行直接丢掉。

## 固定顶栏和底栏

纯平移假设在选区含有粘性底栏时会失败：底栏每一帧都在同一位置，底部模板对不上正在滚动的正文，匹配变成低置信度，于是几乎整帧（连同又一截底栏）被接上去，底栏就在长图里重复。

`StickyBandDetector` 对相邻两帧逐行算平均通道差。从上下两边各取一段连续的「没动」行（阈值 2.5，至少 3 行，单边最多 30% 高度）。`BottomIgnoreFraction` 默认 0.12、`TopIgnoreFraction` 默认 0：只有该比例带的平均差也低于阈值时，才把排除范围扩到这一带，并且最多再扩大 96 像素，避免整屏上 12% 变成一大截正文。不会把已经测出的连续静止条裁短。整帧几乎都没动时返回空，交给原来的「相同帧」停止条件。第一次得到的非零边会被锁住，后面的帧不能把它缩掉。

拼接时把累计图和新帧都切成「顶栏 + 中间 + 底栏」。只对中间做 `FindOverlap` / `Append`，再把**第一帧**的顶栏和底栏装回去。因此固定条不参与对齐，也不会被追加第二次。`lastNewRows` 只计新的中间行。没检测到固定边时，走原来的整帧路径，已有测试保持不变。

用户仍然应该尽量把固定条留在选区外面。闪烁文字、视频底栏，或固定区高过 30% 时，这条缓解会漏。
