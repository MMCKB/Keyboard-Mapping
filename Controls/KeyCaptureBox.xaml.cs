using KeyRemapper.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace KeyRemapper.Controls;

/// <summary>
/// 按键捕获控件：点击后进入捕获状态，按下的第一个物理键即为捕获结果，Esc 取消。
/// </summary>
public sealed partial class KeyCaptureBox : UserControl
{
    private bool _capturing;

    /// <summary>捕获到的虚拟键码（已归一化）；未捕获时为 null。</summary>
    public int? CapturedVk { get; private set; }

    public KeyCaptureBox()
    {
        InitializeComponent();
    }

    private void StartCapturing()
    {
        _capturing = true;
        Box.Text = "按下任意键…";
    }

    private void OnGotFocus(object sender, RoutedEventArgs e) => StartCapturing();

    private void OnBoxPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // 已有捕获结果时再点一次可重新捕获（此时不会再触发 GotFocus）
        if (!_capturing) StartCapturing();
    }

    private void OnLostFocus(object sender, RoutedEventArgs e)
    {
        _capturing = false;
        Box.Text = CapturedVk is int vk ? KeyNames.Get(vk) : "";
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_capturing) return;
        e.Handled = true; // 拦下按键（Esc 也照常捕获，用户可以映射到 Esc）
        _capturing = false;

        CapturedVk = KeyNames.Normalize((int)e.Key);
        Box.Text = KeyNames.Get(CapturedVk.Value);
    }
}
