# Sora V2 鼠标电量托盘

专用于 **Ninjutso Sora V2** 的 Windows 10/11 电量监控工具。原生 C# / Windows Forms 实现，无需安装 Python。

## 下载与运行

前往 [Releases](https://github.com/ydc156439/mouse-battery-tray/releases/latest) 下载带版本号的 ZIP，解压到固定目录，双击其中的 EXE（当前为 `MouseBatteryTray-v1.3.0-Windows.exe`）。更新前先退出正在运行的旧版。

- 仅保留 Sora V2 的设备识别、电量查询、单设备卡片和托盘图标。
- 托盘直接显示完整的 0–100 电量数字，悬停查看设备名称和状态。
- 只在检测到 Sora V2 接收器或有线连接时显示；拔出后隐藏并清除旧读数。
- 每 2 秒检查插拔，新连接立即读取；常规电量查询可设为 30 秒、1 分钟、2 分钟或 5 分钟。
- 提供充电状态、低电量提醒、阈值设置、开机启动、连接排查及诊断导出。
- 关闭窗口继续后台运行；再次双击 EXE 或托盘图标打开面板。无设备图标时也可再次运行 EXE 打开面板。
- 主窗口、文件名、Windows 文件属性和诊断文件均显示版本号。

连接状态依据 USB HID 枚举。接收器仍在但鼠标休眠时保留设备，电量未知时显示 `--`，不会伪造百分比。

## 系统与设备范围

标准 Windows 10/11 桌面环境，使用系统自带的 .NET Framework 4.6 或更高版本。EXE 为 AnyCPU，不依赖 Python、第三方 HID DLL 或另行安装的 .NET 8；不能直接运行于 macOS 或 Linux。

| 设备 | VID / PID | 接口 |
| --- | --- | --- |
| Sora V2 无线接收器 | `1915:AE1C` | Usage Page `FFA0` |
| Sora V2 有线连接 | `1915:AE11` | Usage Page `FFA0` |

通过 Feature Report 5 查询电量。其他鼠标不会被识别或查询。不同固件及接收器变体可能需要额外适配。当前按单只 Sora V2 设计；多个同型号设备取第一个有效读数。

## 连接排查

先移动或点击鼠标唤醒，再刷新；关闭 Ninjutso 官方软件及网页驱动，避免占用 HID 配置接口。必要时重新插拔接收器，并在「连接排查」中导出诊断。

设置、日志和诊断保存在 `%LOCALAPPDATA%\MouseBatteryTrayNative`。诊断仅列出 Ninjutso 设备，不含设备序列号或完整路径，不自动上传。卸载前先取消开机启动、退出程序，再删除程序文件与本机设置目录。

## 构建与测试

Windows 双击 `native/build.bat`，使用系统 .NET Framework 编译器生成 `release/MouseBatteryTray.exe`；源码使用 C# 5。

开发者安装 .NET 8 SDK 后可运行：

```sh
dotnet run --project native/tests/ProtocolTests.csproj
```

.NET 8 仅用于开发测试，运行 EXE 不需要安装。

35 项测试通过，覆盖 Sora 识别、拒绝其他设备、状态解析、插拔、缓存与托盘数字。EXE 已交叉编译并核对版本信息，**尚未在实际 Windows 桌面及实物 Sora V2 上验证**。

数字绘制器测试预览（16px 原尺寸及 4 倍放大，非 Windows 截图）：

![托盘数字绘制预览](docs/tray-digits-preview.png)

下载包附带 `SHA256.txt`，可核对 EXE。

## 协议参考

[Sora Tray 原作者公开的 Sora V2 电量查询实现](https://github.com/Fan4Metal/Sora_tray/blob/master/sora_tray.py)。本项目代码独立编写，仅查询电量，不改写设备设置、配对或固件。
