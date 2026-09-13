using System.Runtime.InteropServices;
using KeyRemapper.Services;
using Microsoft.UI.Xaml;

namespace KeyRemapper;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // UAC 提权后的子进程：执行系统级映射的写入/删除，弹框报告结果后直接退出，不打开主窗口
        var argv = Environment.GetCommandLineArgs();
        if (argv.Contains("--apply-scanmap") || argv.Contains("--remove-scanmap"))
        {
            string message;
            if (argv.Contains("--remove-scanmap"))
            {
                var err = ScancodeMapService.Remove();
                message = err == null
                    ? "已删除系统级映射，重启电脑后恢复系统默认的键盘行为。"
                    : "操作失败：" + err;
            }
            else
            {
                var settings = KeyMappingStore.Load();
                if (settings.Mappings.Count == 0)
                {
                    message = "当前没有任何映射规则可写入。";
                }
                else
                {
                    var err = ScancodeMapService.Apply(settings.Mappings);
                    message = err == null
                        ? $"已写入 {settings.Mappings.Count} 条系统级映射，重启电脑后生效。\n生效后无需运行本软件，任何界面下都保持映射。"
                        : "操作失败：" + err;
                }
            }
            MessageBoxW(IntPtr.Zero, message, "键盘映射", 0x40);
            Environment.Exit(0);
            return;
        }

        _window = new MainWindow();
        _window.Activate();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
