using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using Microsoft.Win32;

namespace DiscordProxyManager {
public static class Program {
    [STAThread]
    public static int Main(string[] args) {
        string home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiscordProxyManager");
        string report = Path.Combine(home, "last-result.txt");
        try {
            using (var mutex = new Mutex(false, @"Local\DiscordProxyManager-" + Environment.UserName)) {
                bool acquired;
                try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) {
                    if (args.Length == 1 && args[0].StartsWith("--launch", StringComparison.OrdinalIgnoreCase)) {
                        var settings = new Manager(home).Config;
                        string selected = args[0].StartsWith("--launch=", StringComparison.OrdinalIgnoreCase) ? args[0].Substring(9) : settings.Channel;
                        if (!Manager.Channels.Contains(selected)) throw new ArgumentException("未知 Discord 版本。");
                        using (var signal = EventWaitHandle.OpenExisting(@"Local\DiscordProxyLaunch-" + Environment.UserName + "-" + selected)) signal.Set();
                        return 0;
                    }
                    MessageBox.Show("助手已打开，请在现有窗口操作。", "Discord 代理助手"); return 2;
                }
                try {
                    var manager = new Manager(home);
                    if (args.Length > 0) {
                        if (args.Length != 1) throw new ArgumentException("每次仅支持一个命令。");
                        string command = args[0].ToLowerInvariant();
                        if (command.StartsWith("--launch=")) {
                            string selected = args[0].Substring(9);
                            if (!Manager.Channels.Contains(selected)) throw new ArgumentException("未知 Discord 版本。");
                            manager.Config.Channel = selected;
                            string custom;
                            manager.Config.InstallRoot = manager.Config.Roots.TryGetValue(selected, out custom) ? custom : "";
                            command = "--launch";
                        }
                        string result;
                        switch (command) {
                            case "--launch": manager.Launch(); result = "已发送启动请求。"; break;
                            case "--enable": manager.Enable(); result = "已启用。"; break;
                            case "--restore": manager.Restore(); result = "还原结束，剩余文件：" + manager.Config.Files.Count; break;
                            case "--detect": result = manager.Describe(); break;
                            case "--test": result = Connectivity.Test(manager.Config.Proxy); manager.Log(result); break;
                            default: throw new ArgumentException("支持 --launch / --enable / --restore / --detect / --test");
                        }
                        File.WriteAllText(report, result);
                        return manager.Config.Files.Count > 0 && args[0] == "--restore" ? 3 : 0;
                    }
                    var app = new Application();
                    app.Run(new MainWindow(manager)); return 0;
                } finally { mutex.ReleaseMutex(); }
            }
        } catch (Exception e) {
            Directory.CreateDirectory(home);
            File.WriteAllText(report, "失败：" + e.GetBaseException().Message);
            if (args.Length == 0 || args.Any(a => a.StartsWith("--launch", StringComparison.OrdinalIgnoreCase))) MessageBox.Show(e.GetBaseException().Message, "Discord 代理助手", MessageBoxButton.OK, MessageBoxImage.Warning);
            return 1;
        }
    }
}
public sealed class MainWindow : Window {
    readonly Manager manager;
    TextBox proxy, root, status, logs;
    ComboBox channel, theme;
    TextBlock notice;
    StackPanel actions;
    bool ready;
    readonly System.Collections.Generic.List<EventWaitHandle> signals = new System.Collections.Generic.List<EventWaitHandle>();
    System.Windows.Threading.DispatcherTimer launchTimer;
    public MainWindow(Manager manager) {
        this.manager = manager;
        Title = "Discord 代理助手"; Width = 850; Height = 850; MinWidth = 760; MinHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 13;
        Content = XamlReader.Parse(Layout) as UIElement;
        using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("AppLogo")) {
            var logo = new System.Windows.Media.Imaging.BitmapImage();
            logo.BeginInit(); logo.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            logo.StreamSource = stream; logo.EndInit(); logo.Freeze();
            Icon = logo; Find<Image>("AppLogo").Source = logo;
        }
        proxy = Find<TextBox>("Proxy"); root = Find<TextBox>("InstallRoot");
        status = Find<TextBox>("Status"); logs = Find<TextBox>("Logs");
        channel = Find<ComboBox>("Channel"); theme = Find<ComboBox>("Theme");
        notice = Find<TextBlock>("Notice"); actions = Find<StackPanel>("Actions");
        channel.ItemsSource = Manager.Channels; channel.SelectedItem = manager.Config.Channel;
        theme.ItemsSource = new[] {"浅色", "深色", "跟随系统"}; theme.SelectedItem = manager.Config.Theme;
        proxy.Text = manager.Config.Proxy; root.Text = manager.Config.InstallRoot;
        string logPath = Path.Combine(manager.Home, "manager.log");
        if (File.Exists(logPath)) { var lines = File.ReadAllLines(logPath); logs.Text = String.Join(Environment.NewLine, lines.Skip(Math.Max(0, lines.Length - 100))); }
        manager.Logged += line => Dispatcher.Invoke(new Action(() => { logs.AppendText(line + Environment.NewLine); logs.ScrollToEnd(); }));
        Find<Button>("Enable").Click += async (s,e) => await Run(() => { manager.Enable(); manager.Launch(); }, "已启用代理并发送 Discord 启动请求。");
        Find<Button>("Launch").Click += async (s,e) => await Run(() => manager.Launch(), "已发送启动请求，请在 Discord 中确认连接。");
        Find<Button>("Restore").Click += async (s,e) => await Run(() => manager.Restore(), "还原操作结束，详情见日志。", false);
        Find<Button>("Test").Click += async (s,e) => {
            string result = "";
            await Run(() => { result = Connectivity.Test(manager.Config.Proxy); manager.Log(result); }, null);
            if (result != "") notice.Text = result;
        };
        Find<Button>("Refresh").Click += (s,e) => { try { ReadFields(); Refresh(); } catch(Exception ex) { notice.Text = ex.Message; } };
        Find<Button>("Browse").Click += (s,e) => {
            var dialog = new OpenFileDialog { Filter = "Discord 更新器 (Update.exe)|Update.exe", Title = "选择安装根目录中的 Update.exe" };
            if (dialog.ShowDialog(this) == true) root.Text = Path.GetDirectoryName(dialog.FileName);
        };
        Find<Button>("Copy").Click += (s,e) => { Clipboard.SetText(logs.Text); notice.Text = "日志已复制。"; };
        theme.SelectionChanged += (s,e) => { if (ready) { manager.Config.Theme = (string)theme.SelectedItem; manager.Save(); ApplyTheme(); } };
        channel.SelectionChanged += (s,e) => { if (ready) { string custom; manager.Config.Channel = (string)channel.SelectedItem; root.Text = manager.Config.Roots.TryGetValue(manager.Config.Channel, out custom) ? custom : ""; manager.Config.InstallRoot = root.Text; Refresh(); } };
        SystemEvents.UserPreferenceChanged += OnPreference;
        Closed += (s,e) => SystemEvents.UserPreferenceChanged -= OnPreference;
        ready = true; ApplyTheme(); Refresh();
        foreach (string name in Manager.Channels) signals.Add(new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\DiscordProxyLaunch-" + Environment.UserName + "-" + name));
        launchTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        launchTimer.Tick += async (s,e) => {
            if (!actions.IsEnabled) return;
            for (int i=0;i<signals.Count;i++) if (signals[i].WaitOne(0)) {
                channel.SelectedItem = Manager.Channels[i];
                await Run(() => manager.Launch(), "已收到快捷方式请求并启动 Discord。");
            }
        };
        launchTimer.Start();
        Closed += (s,e) => { launchTimer.Stop(); foreach(var signal in signals) signal.Dispose(); };
    }
    T Find<T>(string name) where T : FrameworkElement { return (T)((FrameworkElement)Content).FindName(name); }
    void ReadFields(bool validate = true) {
        if (validate) Manager.ParseProxy(proxy.Text.Trim());
        manager.Config.Proxy = proxy.Text.Trim(); manager.Config.Channel = (string)channel.SelectedItem;
        manager.Config.InstallRoot = root.Text.Trim(); manager.Config.Roots[manager.Config.Channel] = manager.Config.InstallRoot; manager.Save();
    }
    void Refresh() { status.Text = manager.Describe(); }
    async Task Run(Action action, string success, bool validate = true) {
        try {
            ReadFields(validate); actions.IsEnabled = false; Find<Grid>("Configuration").IsEnabled = false;
            notice.Text = "正在处理…";
            // Shortcut COM creation must stay on the STA dispatcher.
            if (success != null) { await Dispatcher.InvokeAsync(action, System.Windows.Threading.DispatcherPriority.Background); }
            else await Task.Run(action);
            if (success != null) notice.Text = success;
        } catch(Exception e) { notice.Text = e.GetBaseException().Message; manager.Log("操作失败：" + e.GetBaseException().Message); }
        finally { actions.IsEnabled = true; Find<Grid>("Configuration").IsEnabled = true; Refresh(); }
    }
    void OnPreference(object sender, UserPreferenceChangedEventArgs e) { Dispatcher.BeginInvoke(new Action(ApplyTheme)); }
    void ApplyTheme() {
        bool dark = (string)theme.SelectedItem == "深色";
        if ((string)theme.SelectedItem == "跟随系统") {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                dark = key != null && Convert.ToInt32(key.GetValue("AppsUseLightTheme", 1)) == 0;
        }
        var resources = ((FrameworkElement)Content).Resources;
        resources["Canvas"] = Brush(dark ? "#171922" : "#F4F5FA");
        resources["Card"] = Brush(dark ? "#232633" : "#FFFFFF");
        resources["Ink"] = Brush(dark ? "#F1F2F7" : "#242739");
        resources["Muted"] = Brush(dark ? "#B1B6CB" : "#6F758A");
        resources["Line"] = Brush(dark ? "#3A3F53" : "#E1E4EF");
        Background = (Brush)resources["Canvas"];
    }
    static Brush Brush(string color) { return (Brush)new BrushConverter().ConvertFromString(color); }
    const string Layout = @"
<Grid xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' Background='{DynamicResource Canvas}'>
 <Grid.Resources>
  <SolidColorBrush x:Key='Canvas' Color='#F4F5FA'/><SolidColorBrush x:Key='Card' Color='White'/><SolidColorBrush x:Key='Ink' Color='#242739'/><SolidColorBrush x:Key='Muted' Color='#6F758A'/><SolidColorBrush x:Key='Line' Color='#E1E4EF'/>
  <Style TargetType='TextBlock'><Setter Property='Foreground' Value='{DynamicResource Ink}'/></Style>
  <Style TargetType='TextBox'><Setter Property='Padding' Value='10,8'/><Setter Property='Background' Value='{DynamicResource Card}'/><Setter Property='Foreground' Value='{DynamicResource Ink}'/><Setter Property='BorderBrush' Value='{DynamicResource Line}'/><Setter Property='BorderThickness' Value='1'/><Setter Property='VerticalContentAlignment' Value='Center'/></Style>
  <Style TargetType='Button'><Setter Property='Padding' Value='16,10'/><Setter Property='Margin' Value='0,0,10,0'/><Setter Property='Background' Value='{DynamicResource Card}'/><Setter Property='Foreground' Value='{DynamicResource Ink}'/><Setter Property='BorderBrush' Value='{DynamicResource Line}'/><Setter Property='Cursor' Value='Hand'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'><Border x:Name='ButtonBorder' CornerRadius='7' Padding='{TemplateBinding Padding}' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='ButtonBorder' Property='Opacity' Value='0.8'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter TargetName='ButtonBorder' Property='Opacity' Value='0.45'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
  <Style x:Key='CardStyle' TargetType='Border'><Setter Property='Background' Value='{DynamicResource Card}'/><Setter Property='CornerRadius' Value='12'/><Setter Property='Padding' Value='22'/><Setter Property='Margin' Value='0,0,0,16'/><Setter Property='BorderBrush' Value='{DynamicResource Line}'/><Setter Property='BorderThickness' Value='1'/></Style>
 </Grid.Resources>
 <ScrollViewer VerticalScrollBarVisibility='Auto'><StackPanel Margin='30,24'>
  <Grid Margin='0,0,0,22'><Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='130'/></Grid.ColumnDefinitions>
   <StackPanel Orientation='Horizontal'><Border Width='58' Height='58' Background='White' CornerRadius='10' Margin='0,0,14,0' VerticalAlignment='Center'><Image x:Name='AppLogo' Margin='5' Stretch='Uniform'/></Border><StackPanel><TextBlock Text='Discord 代理助手' FontSize='27' FontWeight='SemiBold'/><TextBlock Text='让连接回归简单。为 Discord 单独配置本地代理。' Foreground='{DynamicResource Muted}' Margin='0,8,0,0'/></StackPanel></StackPanel>
   <ComboBox x:Name='Theme' Grid.Column='1' VerticalAlignment='Center' Padding='8'/>
  </Grid>
  <Border Style='{StaticResource CardStyle}'>
   <StackPanel><TextBlock Text='代理配置' FontSize='16' FontWeight='SemiBold' Margin='0,0,0,18'/>
    <Grid x:Name='Configuration'><Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='16'/><ColumnDefinition Width='160'/></Grid.ColumnDefinitions><Grid.RowDefinitions><RowDefinition Height='Auto'/><RowDefinition Height='Auto'/><RowDefinition Height='18'/><RowDefinition Height='Auto'/><RowDefinition Height='Auto'/></Grid.RowDefinitions>
     <TextBlock Text='代理地址' Foreground='{DynamicResource Muted}' Margin='0,0,0,8'/><TextBlock Grid.Column='2' Text='Discord 版本' Foreground='{DynamicResource Muted}'/>
     <TextBox x:Name='Proxy' Grid.Row='1' ToolTip='支持无认证 HTTP / SOCKS5，例如 http://127.0.0.1:7897'/><ComboBox x:Name='Channel' Grid.Column='2' Grid.Row='1' Padding='8' VerticalContentAlignment='Center'/>
     <TextBlock Grid.Row='3' Text='Discord 程序位置（一般无需填写，自动查找；不是助手的安装位置）' Foreground='{DynamicResource Muted}' Margin='0,0,0,8'/>
     <TextBox x:Name='InstallRoot' Grid.Row='4' ToolTip='留空即可。仅自动检测失败时，选择 Discord 目录里的 Update.exe。'/><Button x:Name='Browse' Grid.Column='2' Grid.Row='4' Content='手动指定 Discord' Margin='0'/>
    </Grid>
    <StackPanel x:Name='Actions' Orientation='Horizontal' Margin='0,20,0,0'>
     <Button x:Name='Enable' Content='一键启用并启动' Background='#5865F2' Foreground='White' BorderBrush='#5865F2'/>
     <Button x:Name='Launch' Content='启动 Discord'/><Button x:Name='Test' Content='测试连通性'/><Button x:Name='Restore' Content='一键还原'/>
    </StackPanel>
    <TextBlock x:Name='Notice' Text='填写代理地址后，点击“一键启用并启动”即可。操作前请先从托盘完全退出 Discord。' Foreground='{DynamicResource Muted}' TextWrapping='Wrap' Margin='0,16,0,0'/>
   </StackPanel>
  </Border>
  <Border Style='{StaticResource CardStyle}'><StackPanel>
   <DockPanel Margin='0,0,0,10'><Button x:Name='Refresh' Content='重新检测' DockPanel.Dock='Right' Padding='10,5' Margin='0'/><TextBlock Text='环境检测' FontSize='16' FontWeight='SemiBold' VerticalAlignment='Center'/></DockPanel>
   <TextBox x:Name='Status' IsReadOnly='True' BorderThickness='0' Padding='0' TextWrapping='Wrap' FontSize='12' Background='Transparent'/>
  </StackPanel></Border>
  <Border Style='{StaticResource CardStyle}'><StackPanel>
   <DockPanel Margin='0,0,0,10'><Button x:Name='Copy' Content='复制日志' DockPanel.Dock='Right' Padding='10,5' Margin='0'/><TextBlock Text='运行日志' FontSize='16' FontWeight='SemiBold' VerticalAlignment='Center'/></DockPanel>
   <TextBox x:Name='Logs' IsReadOnly='True' Height='130' VerticalScrollBarVisibility='Auto' TextWrapping='Wrap' FontSize='12' BorderThickness='0' Padding='0' Background='Transparent'/>
  </StackPanel></Border>
  <TextBlock Text='代理核心 discord-proxy v0.0.13 · x64 · 不修改系统代理 · 语音 UDP 请单独验证' Foreground='{DynamicResource Muted}' FontSize='11' HorizontalAlignment='Center'/>
 </StackPanel></ScrollViewer>
</Grid>";
}
}
