using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace DiscordProxyManager {
public sealed class Settings {
    public string Proxy = "http://127.0.0.1:7897";
    public string Channel = "Discord";
    public string InstallRoot = "";
    public Dictionary<string, string> Roots = new Dictionary<string, string>();
    public string Theme = "浅色";
    public bool Enabled;
    public List<OwnedFile> Files = new List<OwnedFile>();
}
public sealed class OwnedFile { public string Path; public string Hash; }
public sealed class Installation {
    public string Root, ExeName, Updater;
    public List<string> Versions;
    public string Latest { get { return Versions.First(); } }
}
public sealed class Manager {
    public readonly string Home;
    public Settings Config;
    readonly JavaScriptSerializer json = new JavaScriptSerializer();
    public event Action<string> Logged;
    public static readonly string[] Channels = { "Discord", "DiscordPTB", "DiscordCanary" };
    public Manager(string home) {
        Home = home;
        Directory.CreateDirectory(Home);
        string path = System.IO.Path.Combine(Home, "settings.json");
        Config = File.Exists(path) ? json.Deserialize<Settings>(File.ReadAllText(path)) : new Settings();
        if (Config == null || Config.Files == null) throw new InvalidDataException("配置文件无效，请先备份 settings.json 后检查。");
        if (Config.Roots == null) Config.Roots = new Dictionary<string, string>();
    }
    public void Save() {
        string path = Path.Combine(Home, "settings.json"), temp = path + ".tmp";
        File.WriteAllText(temp, json.Serialize(Config), new UTF8Encoding(false));
        if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
    }
    public void Log(string message) {
        string line = DateTime.Now.ToString("HH:mm:ss") + "  " + message;
        File.AppendAllText(Path.Combine(Home, "manager.log"), line + Environment.NewLine, Encoding.UTF8);
        if (Logged != null) Logged(line);
    }
    public static Uri ParseProxy(string value) {
        Uri uri;
        if (String.IsNullOrWhiteSpace(value) || value.Any(Char.IsWhiteSpace) || value.IndexOf('"') >= 0 ||
            !Uri.TryCreate(value, UriKind.Absolute, out uri) ||
            (uri.Scheme != "http" && uri.Scheme != "socks5") ||
            String.IsNullOrEmpty(uri.Host) || uri.HostNameType == UriHostNameType.IPv6 ||
            uri.Port < 1 || uri.Port > 65535 || !String.IsNullOrEmpty(uri.UserInfo) ||
            uri.AbsolutePath != "/" || uri.Query != "" || uri.Fragment != "" ||
            !Regex.IsMatch(value, @"^(http|socks5)://[a-zA-Z0-9._-]+:\d{1,5}/?$"))
            throw new ArgumentException("请输入 http://主机:端口 或 socks5://主机:端口；第一版不支持认证或 IPv6 代理地址。");
        return uri;
    }
    public string ProxyArgument { get { var p = ParseProxy(Config.Proxy); return p.Scheme + "://" + p.Host + ":" + p.Port; } }
    public Installation Detect() {
        if (!Channels.Contains(Config.Channel)) throw new ArgumentException("未知 Discord 版本。");
        string root = String.IsNullOrWhiteSpace(Config.InstallRoot) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Config.Channel) : Path.GetFullPath(Config.InstallRoot);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("未找到 " + root + "，请安装 Discord 或选择安装根目录。");
        string exe = Config.Channel + ".exe";
        var versions = Directory.GetDirectories(root, "app-*")
            .Where(d => File.Exists(Path.Combine(d, exe)))
            .Select(d => new { Dir = d, Ver = VersionFromDirectory(d) })
            .Where(v => v.Ver != null).OrderByDescending(v => v.Ver).Select(v => v.Dir).ToList();
        if (versions.Count == 0 || !File.Exists(Path.Combine(root, "Update.exe")))
            throw new FileNotFoundException("该目录应包含 Update.exe 和 app-* 子目录，暂不支持 Microsoft Store 安装方式。");
        return new Installation { Root = root, Updater = Path.Combine(root, "Update.exe"), ExeName = exe, Versions = versions };
    }
    static Version VersionFromDirectory(string path) { Version v; return Version.TryParse(Path.GetFileName(path).Substring(4), out v) ? v : null; }
    public static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    public static string FileHash(string path) { return Hash(File.ReadAllBytes(path)); }
    public static byte[] CoreBytes() {
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ProxyCore")) {
            if (stream == null) throw new FileNotFoundException("程序未嵌入代理核心，请重新构建。");
            using (var m = new MemoryStream()) { stream.CopyTo(m); return m.ToArray(); }
        }
    }
    public static ushort PeMachine(string path) {
        using (var f = File.OpenRead(path)) using (var r = new BinaryReader(f)) {
            if (r.ReadUInt16() != 0x5a4d) throw new InvalidDataException("不是有效的 Windows 可执行文件。");
            f.Position = 0x3c; int offset = r.ReadInt32();
            if (offset < 0 || offset > f.Length - 6) throw new InvalidDataException("PE 文件损坏。");
            f.Position = offset;
            if (r.ReadUInt32() != 0x4550) throw new InvalidDataException("PE 标识无效。");
            return r.ReadUInt16();
        }
    }
    public void RequireStopped() {
        var running = Process.GetProcessesByName(Config.Channel);
        bool any = running.Length != 0;
        foreach (var p in running) p.Dispose();
        if (any) throw new InvalidOperationException("请先从系统托盘完全退出 " + Config.Channel + "，再执行此操作。");
    }
    public void WriteOwned(string path, byte[] bytes) {
        string full = Path.GetFullPath(path), hash = Hash(bytes);
        var entry = Config.Files.FirstOrDefault(x => String.Equals(x.Path, full, StringComparison.OrdinalIgnoreCase));
        if (File.Exists(full)) {
            if (entry == null || FileHash(full) != entry.Hash) throw new IOException("保留现有文件，未覆盖：" + full);
            if (entry.Hash == hash) return;
        }
        // Record intended content before writing, so a failed deployment is recoverable.
        if (entry == null) { entry = new OwnedFile { Path = full, Hash = hash }; Config.Files.Add(entry); }
        else entry.Hash = hash;
        Save();
        File.WriteAllBytes(full, bytes);
    }
    public void Deploy(Installation installation) {
        byte[] bytes = CoreBytes();
        foreach (string dir in installation.Versions) {
            if (PeMachine(Path.Combine(dir, installation.ExeName)) != 0x8664)
                throw new InvalidOperationException("第一版仅支持 x64 Discord，检测到其它架构。");
            string dest = Path.Combine(dir, "version.dll");
            var entry = Config.Files.FirstOrDefault(x => String.Equals(x.Path, dest, StringComparison.OrdinalIgnoreCase));
            if (File.Exists(dest) && (entry == null || FileHash(dest) != entry.Hash))
                throw new IOException("检测到已有 version.dll，为避免覆盖其它工具，请先处理：" + dest);
        }
        foreach (string dir in installation.Versions) WriteOwned(Path.Combine(dir, "version.dll"), bytes);
        Log("代理核心已部署，覆盖 " + installation.Versions.Count + " 个现有版本目录。");
    }
    public void Enable() {
        ParseProxy(Config.Proxy); var install = Detect(); RequireStopped();
        Deploy(install);
        string persistent = Path.Combine(Home, "DiscordProxyManager.exe");
        string self = Assembly.GetExecutingAssembly().Location;
        if (!String.Equals(self, persistent, StringComparison.OrdinalIgnoreCase)) File.Copy(self, persistent, true);
        CreateShortcut(persistent);
        Config.Enabled = true; Save();
        Log("已启用。请使用桌面“Discord（本地代理）”快捷方式启动。");
    }
    void CreateShortcut(string target) {
        string dest = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Config.Channel + "（本地代理）.lnk");
        string temp = Path.Combine(Home, Guid.NewGuid().ToString("N") + ".lnk");
        object shell = null, link = null;
        try {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            dynamic ws = shell;
            link = ws.CreateShortcut(temp);
            dynamic shortcut = link;
            shortcut.TargetPath = target;
            shortcut.Arguments = "--launch=" + Config.Channel;
            shortcut.WorkingDirectory = Home;
            shortcut.Description = "通过本地代理启动 Discord";
            shortcut.IconLocation = target + ",0";
            shortcut.Save();
            WriteOwned(dest, File.ReadAllBytes(temp));
        } finally {
            if (File.Exists(temp)) File.Delete(temp);
            if (link != null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link);
            if (shell != null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
    }
    public void Launch() {
        if (!Config.Enabled) Enable();
        ParseProxy(Config.Proxy); var install = Detect(); RequireStopped(); Deploy(install);
        Save();
        var start = new ProcessStartInfo(install.Updater, "--processStart " + install.ExeName + " --a=--proxy-server=" + ProxyArgument);
        start.UseShellExecute = false; start.WorkingDirectory = install.Root;
        // These values are scoped to the child process, never to the user or system.
        start.EnvironmentVariables["HTTP_PROXY"] = ProxyArgument;
        start.EnvironmentVariables["HTTPS_PROXY"] = ProxyArgument;
        start.EnvironmentVariables["ALL_PROXY"] = ProxyArgument;
        using (var p = Process.Start(start)) { }
        Log("已通过 Update.exe 发送代理启动请求；实际连接情况请在 Discord 中确认。");
    }
    public void Restore() {
        // DLLs belonging to any supported channel may still be loaded.
        foreach (string name in Channels) {
            var ps = Process.GetProcessesByName(name); bool running = ps.Length > 0;
            foreach (var p in ps) p.Dispose();
            if (running) throw new InvalidOperationException("请先完全退出所有 Discord 版本，再还原。");
        }
        foreach (var entry in Config.Files.ToArray()) {
            if (File.Exists(entry.Path)) {
                if (FileHash(entry.Path) != entry.Hash) { Log("文件已被修改，保留并等待手动处理：" + entry.Path); continue; }
                File.Delete(entry.Path);
            }
            Config.Files.Remove(entry); Save();
        }
        Config.Enabled = false; Save();
        Log(Config.Files.Count == 0 ? "已还原：移除本工具部署的 DLL 与快捷方式。" : "还原未完成：部分文件被外部修改，已保留。请检查日志。");
    }
    public string Describe() {
        var b = new StringBuilder();
        try {
            var i = Detect();
            b.AppendLine("安装版本    " + Config.Channel + " / " + Path.GetFileName(i.Latest).Substring(4));
            b.AppendLine("安装目录    " + i.Root);
            string dll = Path.Combine(i.Latest, "version.dll");
            b.AppendLine("代理核心    " + (File.Exists(dll) ? (FileHash(dll) == Hash(CoreBytes()) ? "已部署" : "检测到其它 DLL") : "未部署"));
        } catch (Exception e) { b.AppendLine("安装检测    " + e.Message); }
        var processes = Process.GetProcessesByName(Config.Channel);
        b.AppendLine("运行状态    " + (processes.Length > 0 ? "运行中" : "未运行"));
        foreach (var p in processes) p.Dispose();
        b.AppendLine("助手配置    " + (Config.Enabled ? "已启用" : "未启用"));
        b.AppendLine("待还原文件  " + Config.Files.Count);
        b.Append("系统网络    不修改系统代理、TUN 或用户环境变量");
        return b.ToString();
    }
}
public static class Connectivity {
    static byte[] ReadExactly(Stream stream, int count) {
        var bytes = new byte[count]; int offset = 0;
        while (offset < count) { int n = stream.Read(bytes, offset, count - offset); if (n == 0) throw new IOException("代理提前断开连接。"); offset += n; }
        return bytes;
    }
    static string Header(Stream stream) {
        var data = new List<byte>();
        while (data.Count < 16384) {
            int c = stream.ReadByte(); if (c < 0) throw new IOException("连接提前关闭。"); data.Add((byte)c);
            int n = data.Count;
            if (n >= 4 && data[n-4] == 13 && data[n-3] == 10 && data[n-2] == 13 && data[n-1] == 10) return Encoding.ASCII.GetString(data.ToArray());
        }
        throw new IOException("响应头过长。");
    }
    public static string Test(string address) {
        Uri proxy = Manager.ParseProxy(address); var watch = Stopwatch.StartNew();
        using (var tcp = new TcpClient()) {
            var task = tcp.ConnectAsync(proxy.Host, proxy.Port);
            if (!task.Wait(5000)) throw new TimeoutException("连接代理端口超时，请检查代理客户端。");
            task.GetAwaiter().GetResult();
            using (var s = tcp.GetStream()) {
                s.ReadTimeout = 7000; s.WriteTimeout = 7000;
                if (proxy.Scheme == "http") {
                    var request = Encoding.ASCII.GetBytes("CONNECT discord.com:443 HTTP/1.1\r\nHost: discord.com:443\r\n\r\n");
                    s.Write(request, 0, request.Length);
                    string h = Header(s);
                    if (!Regex.IsMatch(h, @"^HTTP/1\.[01] 200(?: |\r)")) throw new IOException("代理隧道失败：" + h.Split('\r')[0]);
                } else {
                    s.Write(new byte[] {5,1,0}, 0, 3);
                    var hello = ReadExactly(s, 2);
                    if (hello[0] != 5 || hello[1] != 0) throw new IOException("SOCKS5 不支持免认证连接。");
                    byte[] host = Encoding.ASCII.GetBytes("discord.com");
                    byte[] request = new byte[7 + host.Length]; request[0]=5; request[1]=1; request[3]=3; request[4]=(byte)host.Length;
                    Array.Copy(host, 0, request, 5, host.Length); request[request.Length-2]=1; request[request.Length-1]=187;
                    s.Write(request, 0, request.Length);
                    var reply = ReadExactly(s, 4);
                    if (reply[0] != 5 || reply[1] != 0 || reply[2] != 0) throw new IOException("SOCKS5 建立隧道失败。");
                    int size = reply[3] == 1 ? 4 : reply[3] == 4 ? 16 : reply[3] == 3 ? ReadExactly(s, 1)[0] : -1;
                    if (size < 0) throw new IOException("SOCKS5 返回了未知地址类型。");
                    ReadExactly(s, size + 2);
                }
                using (var tls = new SslStream(s, false)) {
                    var handshake = tls.AuthenticateAsClientAsync("discord.com", null, System.Security.Authentication.SslProtocols.Tls12, false);
                    if (!handshake.Wait(10000)) throw new TimeoutException("Discord TLS 握手超时。");
                    handshake.GetAwaiter().GetResult();
                    tls.ReadTimeout = 7000; tls.WriteTimeout = 7000;
                    byte[] get = Encoding.ASCII.GetBytes("GET /api/v10/gateway HTTP/1.1\r\nHost: discord.com\r\nUser-Agent: DiscordProxyManager/1.0\r\nConnection: close\r\n\r\n");
                    tls.Write(get, 0, get.Length);
                    string response = Header(tls), first = response.Split('\r')[0];
                    if (!Regex.IsMatch(first, @"^HTTP/1\.[01] 200(?: |$)")) throw new IOException("TLS 已连接，但 Discord API 返回：" + first);
                    return "连接成功 · Discord API HTTP 200 · " + watch.ElapsedMilliseconds + " ms（不代表语音 UDP 可用）";
                }
            }
        }
    }
}
}
