# 键盘映射（WinUI 3）

[![Build](https://github.com/MMCKB/Keyboard-Mapping/actions/workflows/build.yml/badge.svg)](https://github.com/MMCKB/Keyboard-Mapping/actions/workflows/build.yml)

基于 WinUI 3（Windows App SDK 1.6）的按键重映射工具，提供两种互不依赖的映射方式：

## 下载

到 [Releases](https://github.com/MMCKB/Keyboard-Mapping/releases) 页面下载 `KeyRemapper-win-x64.zip`，
解压后直接运行 `KeyRemapper.exe`（自包含发布，无需安装 .NET 运行时）。

CI 每次推送到 main 自动构建；在 Actions 页面手动触发（Run workflow）时，
可选择同时创建 GitHub Release 并上传构建产物。

| 模式 | 原理 | 生效条件 | 适合场景 |
|------|------|----------|----------|
| **即时映射** | 全局低级键盘钩子（WH_KEYBOARD_LL）+ SendInput 注入 | 程序运行期间始终生效，开关即开即用 | 临时改键、随时切换 |
| **系统级映射** | 写入注册表 `Scancode Map`（键盘驱动层，SharpKeys 同款机制） | 写入后**重启电脑**生效；之后**无需运行本软件**、无需开机自启，永久生效 | 一劳永逸的固定改键 |

## 功能

- 单键 → 单键映射（字母、数字、F 键、小键盘、方向键、媒体键、修饰键、Esc 等）
- 映射列表管理：添加 / 删除，保存后即时生效
- 「启用映射」总开关；「开机自启」（写 HKCU Run 注册表键，可随时关闭）
- 系统级映射：一键写入 / 恢复默认；缺管理员权限时自动弹 UAC 提权重启自身完成操作
- 配置持久化：`%LOCALAPPDATA%\KeyRemapper\settings.json`

## 构建

需要 Windows + .NET 8 SDK：

```bash
dotnet build -c Release -p:Platform=x64
```

产物：`bin\x64\Release\net8.0-windows10.0.19041.0\KeyRemapper.exe`

项目配置了 `WindowsAppSDKSelfContained=true`（自带 Windows App SDK 运行时），
只需机器装有 **.NET 8 桌面运行时**。若想完全免依赖发布：

```bash
dotnet publish -c Release -p:Platform=x64 -r win-x64 --self-contained true
```

## 使用

**即时映射**

1. 启动程序 → 点「添加映射」→ 点第一个框按“要被替换的键”，点第二个框按“想要的键” → 确定
2. 打开「启用映射」开关即可；保持程序运行（可最小化），关闭软件即失效

**系统级映射（回答“不开机自启也生效”的做法）**

1. 在上方添加好映射规则
2. 展开「系统级映射」→ 点「写入系统映射…」（会弹出 UAC，选“是”）
3. **重启电脑**后永久生效：此后不开机自启、不运行本软件、任何界面下都保持映射
4. 想还原：点「恢复系统默认」→ 再重启电脑即可

## 技术要点

- `Services/KeyboardHookService.cs`：后台线程安装 WH_KEYBOARD_LL 并跑消息泵；
  物理按下 → 查表 → 吞掉原事件并注入目标键（记录按下/抬起配对，支持自动重复；
  暂停/停止时补发抬起事件避免按键卡住）。注入事件带 LLKHF_INJECTED 标记会被直接放行，不会死循环。
- `Services/ScancodeMapService.cs`：用 `MapVirtualKey(VK, MAPVK_VK_TO_VSC_EX)` 把虚拟键转扫描码，
  组装 Scancode Map 二进制（每条目低 16 位=目标扫描码、高 16 位=原始扫描码，扩展键高字节 0xE0），
  写入 `HKLM\SYSTEM\CurrentControlSet\Control\Keyboard Layout`。
- `App.OnLaunched` 处理 `--apply-scanmap` / `--remove-scanmap` 参数：UAC 提权后的子进程
  完成注册表操作并弹框报告结果，不打开主窗口。

## 图标

`Assets/app.ico` 为扁平化键盘图标（256/48/32/16 四种尺寸），同时用于 exe 内嵌图标与窗口标题栏。
想调整配色或样式，修改 `tools/make-icon.ps1` 顶部的颜色常量后重新运行即可：

```bash
powershell -ExecutionPolicy Bypass -File tools/make-icon.ps1
```

## 已知限制

- **即时映射**：注入的按键进不了管理员权限的程序（需以管理员身份运行本软件）；
  不支持组合键（如 Ctrl+A → X）与鼠标键；左右 Ctrl/Shift/Alt 视为同一个键；
  部分带反作弊的游戏可能忽略 SendInput 注入
- **系统级映射**：写入/还原各需重启一次电脑；左右修饰键只能映射左键（扫描码层决定）；
  仅支持有物理扫描码的按键（绝大多数标准按键都支持）
- 两种模式可并存：即时映射收到的是系统级映射**之后**的按键，注意不要设置相互干扰的规则
