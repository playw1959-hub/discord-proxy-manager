# Discord 代理助手

Windows 原生的 Discord 本地代理管理工具。以 [discord-proxy](https://github.com/aiqinxuancai/discord-proxy) 的 DLL 为代理核心，参考 [chatgpt-proxy-manager](https://github.com/xztsummer/chatgpt-proxy-manager) 的操作方式，独立实现 C# / WPF 管理界面。

## 使用

1. 运行 `build/DiscordProxyManager.exe`。Windows 10 / 11 x64，需系统自带的 .NET Framework 4.8。
2. 启动自己的代理客户端，填写地址，例如 `http://127.0.0.1:7897` 或 `socks5://127.0.0.1:1080`。端口以客户端实际配置为准。
3. 选择 Discord、DiscordPTB 或 DiscordCanary；默认检测 `%LOCALAPPDATA%` 下的安装目录。自定义目录请选择其 `Update.exe`，不是 `app-*` 下的 Discord.exe。
4. 点击“测试连通性”，完全退出 Discord（包括托盘），再点击“一键启用并启动”。这会完成配置并启动 Discord。
5. 点击“启动 Discord”，或关闭助手后双击桌面的 `Discord（本地代理）` 快捷方式。每次启动会重新检测已有的版本目录并补齐 DLL。
6. 还原时完全退出所有 Discord 版本，点击“一键还原”。

首次启用会将助手复制到 `%LOCALAPPDATA%/DiscordProxyManager/DiscordProxyManager.exe`，快捷方式指向这个固定位置，下载目录中的 EXE 可以移动。界面主按钮会启用并启动；命令行 `--enable` 仅配置。程序不会自动关闭现有 Discord。

## 功能

- 中文 WPF 界面，浅色、深色、跟随 Windows 主题。
- 三个 Discord 渠道和自定义安装目录；每个快捷方式保留其渠道，自定义目录分别记忆，代理地址共用。
- 代理 DLL 内嵌在 EXE 中，无需用户手动放置；固定上游 v0.0.13 x64，构建前校验哈希。
- 通过官方 `Update.exe --processStart ... --a=--proxy-server=...` 启动。
- 代理环境变量仅传给子进程，不写用户环境变量、不修改系统代理或 TUN。
- 真实连接测试：HTTP CONNECT / SOCKS5 → 验证 Discord 的 TLS 证书 → Discord gateway API 返回 HTTP 200。
- 安装、版本、运行状态、DLL 检测；持久化日志和一键复制。
- 一键还原只删除部署记录中哈希匹配的文件。遇到已有 DLL 或同名快捷方式时拒绝覆盖，保留原文件。

原有 Discord 快捷方式保持原样；日常需要通过新建的“本地代理”快捷方式启动。

## 构建与验证

不需要安装 .NET SDK 或 NuGet 包，使用 Windows 自带的 .NET Framework C# 编译器。

```powershell
# 首次获取固定版本核心，需要网络
.\fetch-core.ps1
# 编译为单文件 EXE
.\build.ps1
# 运行隔离文件测试、本地伪代理失败测试、WPF 渲染检查
.\test.ps1
```

首次获取会生成 `assets/core.json`，后续获取和构建均核对 SHA-256。哈希用于检测后续文件变化，首次下载的真实性依赖 GitHub HTTPS；并非上游签名验证。

源码位于 `src/Core.cs`（检测、部署、还原和连接测试）与 `src/Program.cs`（界面及命令入口）。测试只写 `tests/out`，不启动 Discord、不修改用户的系统代理、安装文件或桌面快捷方式。

## 命令行

```powershell
$p = Start-Process .\build\DiscordProxyManager.exe -ArgumentList '--detect' -Wait -PassThru
$p.ExitCode
Get-Content "$env:LOCALAPPDATA\DiscordProxyManager\last-result.txt"
```

支持 `--detect`、`--test`、`--enable`、`--launch`、`--launch=DiscordPTB`、`--launch=DiscordCanary`、`--restore`。GUI 程序不直接输出控制台，结果写入 `last-result.txt`，操作日志写入 `manager.log`。失败退出码为 1，已有助手实例为 2，还原遇到外部修改而保留文件为 3。命令行操作读取已保存配置；`--launch` 的错误会弹窗，供桌面快捷方式使用。

## 已知范围

- 当前仅支持普通安装方式的 x64 Discord，暂不支持 Store 安装、x86 / ARM64、代理账号密码、IPv6 代理地址。
- 代理客户端需自行提供，工具不是代理服务器。
- 连通性测试覆盖 Discord HTTPS API，不能证明语音、视频 UDP 可用。
- Discord 更新如果在本次启动过程中创建了新的 `app-*` 目录，新目录可能还没有 DLL；完全退出后再次通过助手启动，即会重新检测部署。当前没有后台监视服务。
- 已有 Discord 进程不会接受新的启动参数；程序要求先完全退出。
- 助手窗口打开期间，桌面快捷方式会将启动请求交给现有窗口；未启用时点击“启动 Discord”会先自动配置。
- 不创建开机启动项、不接管原始快捷方式，不对 Discord 登录成功或完整联网作自动判断。
- EXE 未进行代码签名。

## 还原与卸载

“一键还原”移除所有渠道由助手部署的 DLL 和创建的快捷方式，保留配置与日志。外部修改过的文件会保留并记录，处理后可以重试还原。还原成功后关闭助手，再自行删除 `%LOCALAPPDATA%/DiscordProxyManager` 与下载的 EXE 即可卸载。

上游 DLL 本身的限制和适用范围请参阅其 README；第三方来源见 `THIRD-PARTY-NOTICES.md`。
