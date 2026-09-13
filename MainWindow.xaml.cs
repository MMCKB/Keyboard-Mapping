using System.Collections.ObjectModel;
using KeyRemapper.Controls;
using KeyRemapper.Models;
using KeyRemapper.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace KeyRemapper;

public sealed partial class MainWindow : Window
{
    private readonly KeyboardHookService _hook = new();
    private readonly ObservableCollection<KeyMapping> _mappings = new();
    private AppSettings _settings = new();
    private bool _initializing = true;

    public MainWindow()
    {
        InitializeComponent();

        Title = "键盘映射";
        SystemBackdrop = new MicaBackdrop();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"));
        AppWindow.Resize(new Windows.Graphics.SizeInt32(620, 700));

        _settings = KeyMappingStore.Load();
        foreach (var m in _settings.Mappings) _mappings.Add(m);
        MappingList.ItemsSource = _mappings;
        UpdateEmptyState();

        EnableSwitch.IsOn = _settings.Enabled;
        AutoStartToggle.IsOn = AutoStart.IsEnabled();
        _initializing = false;
        RefreshScanmapStatus();

        if (_settings.Enabled && _mappings.Count > 0)
        {
            _hook.SetMappings(_mappings);
            _hook.Start();
        }

        Closed += (_, _) => _hook.Dispose();
    }

    private void OnEnableToggled(object? sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        ApplyAndSave();
        if (EnableSwitch.IsOn && _mappings.Count == 0)
            ShowHint("已启用映射，但还没有任何规则，请先添加。");
    }

    private async void OnAddClick(object? sender, RoutedEventArgs e)
    {
        // 弹窗期间暂停映射，保证捕获到的是物理按键而不是注入后的结果
        _hook.SetPaused(true);
        try
        {
            var fromBox = new KeyCaptureBox();
            var toBox = new KeyCaptureBox();
            var panel = new StackPanel { Spacing = 6 };
            panel.Children.Add(new TextBlock { Text = "原始按键（会被拦截的键）" });
            panel.Children.Add(fromBox);
            panel.Children.Add(new TextBlock { Text = "目标按键（替换成的键）", Margin = new Thickness(0, 12, 0, 0) });
            panel.Children.Add(toBox);
            panel.Children.Add(new TextBlock
            {
                Text = "点下方框后按下按键即可捕获（Esc、修饰键也可作为目标）；再点一次可重新捕获。当前版本一次映射一个键，不支持组合键。",
                FontSize = 12,
                Opacity = 0.8,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 12, 0, 0),
            });

            var dialog = new ContentDialog
            {
                Title = "添加按键映射",
                Content = panel,
                PrimaryButtonText = "确定",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot,
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            if (fromBox.CapturedVk is not int fromVk || toBox.CapturedVk is not int toVk)
            {
                ShowHint("请先分别捕获原始按键和目标按键。", error: true);
                return;
            }
            if (fromVk == toVk)
            {
                ShowHint("原始按键和目标按键相同，无需映射。", error: true);
                return;
            }

            var existing = _mappings.FirstOrDefault(m => m.FromVk == fromVk);
            if (existing != null) _mappings.Remove(existing);

            _mappings.Add(new KeyMapping
            {
                FromVk = fromVk,
                FromName = KeyNames.Get(fromVk),
                ToVk = toVk,
                ToName = KeyNames.Get(toVk),
            });
            ApplyAndSave();
        }
        finally
        {
            _hook.SetPaused(false);
        }
    }

    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is int fromVk)
        {
            var m = _mappings.FirstOrDefault(x => x.FromVk == fromVk);
            if (m != null)
            {
                _mappings.Remove(m);
                ApplyAndSave();
            }
        }
    }

    private void OnAutoStartToggled(object? sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        try
        {
            AutoStart.Set(AutoStartToggle.IsOn);
        }
        catch (Exception ex)
        {
            ShowHint("设置开机自启失败：" + ex.Message, error: true);
        }
    }

    private void ApplyAndSave()
    {
        UpdateEmptyState();
        _settings.Mappings = _mappings.ToList();
        _settings.Enabled = EnableSwitch.IsOn;
        KeyMappingStore.Save(_settings);

        _hook.SetMappings(_mappings);
        if (EnableSwitch.IsOn && _mappings.Count > 0)
        {
            _hook.Start();
            if (!_hook.IsRunning) ShowHint("键盘钩子安装失败，映射未生效。", error: true);
        }
        else
        {
            _hook.Stop();
        }
    }

    private void OnApplyScanmapClick(object? sender, RoutedEventArgs e)
    {
        if (_mappings.Count == 0)
        {
            ShowHint("没有映射规则可写入，请先在上方添加映射。", error: true);
            return;
        }

        var err = ScancodeMapService.Apply(_mappings);
        if (err == ScancodeMapService.NeedAdmin)
        {
            try
            {
                ScancodeMapService.RelaunchElevated("--apply-scanmap");
                ShowHint("请在弹出的管理员确认框中选择「是」，写入完成后会弹出结果。");
            }
            catch (Exception ex)
            {
                ShowHint("未获得管理员权限，未写入。（" + ex.Message + "）", error: true);
            }
            return;
        }
        if (err != null)
        {
            ShowHint("写入失败：" + err, error: true);
            return;
        }

        ShowHint("系统级映射已写入，重启电脑后生效；此后不开机自启、不运行本软件也保持生效。");
        RefreshScanmapStatus();
    }

    private void OnRemoveScanmapClick(object? sender, RoutedEventArgs e)
    {
        var err = ScancodeMapService.Remove();
        if (err == ScancodeMapService.NeedAdmin)
        {
            try
            {
                ScancodeMapService.RelaunchElevated("--remove-scanmap");
                ShowHint("请在弹出的管理员确认框中选择「是」，删除完成后会弹出结果。");
            }
            catch (Exception ex)
            {
                ShowHint("未获得管理员权限，未删除。（" + ex.Message + "）", error: true);
            }
            return;
        }
        if (err != null)
        {
            ShowHint("删除失败：" + err, error: true);
            return;
        }

        ShowHint("已删除系统级映射，重启电脑后恢复系统默认的键盘行为。");
        RefreshScanmapStatus();
    }

    private void RefreshScanmapStatus()
    {
        bool exists = ScancodeMapService.Exists();
        ScanmapStatus.Text = exists
            ? "当前状态：注册表中已写入系统级映射（若刚写入，重启电脑后生效）。"
            : "当前状态：未写入，系统键盘行为未被修改。";
        ScanmapInfoBar.IsOpen = exists;
    }

    private void UpdateEmptyState() =>
        EmptyHint.Visibility = _mappings.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void ShowHint(string message, bool error = false)
    {
        StatusInfo.Severity = error ? InfoBarSeverity.Error : InfoBarSeverity.Informational;
        StatusInfo.Title = error ? "出错了" : "提示";
        StatusInfo.Message = message;
        StatusInfo.IsOpen = true;
    }
}
