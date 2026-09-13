namespace KeyRemapper.Services;

public static class KeyNames
{
    /// <summary>左右 Ctrl/Shift/Alt 归一化为同一个键，便于映射与命名。</summary>
    public static int Normalize(int vk) => vk switch
    {
        0xA0 or 0xA1 => 0x10, // Shift
        0xA2 or 0xA3 => 0x11, // Ctrl
        0xA4 or 0xA5 => 0x12, // Alt
        _ => vk,
    };

    private static readonly Dictionary<int, string> Names = new()
    {
        [0x08] = "Backspace",
        [0x09] = "Tab",
        [0x0D] = "回车",
        [0x13] = "Pause",
        [0x14] = "大写锁定",
        [0x1B] = "Esc",
        [0x20] = "空格",
        [0x21] = "Page Up",
        [0x22] = "Page Down",
        [0x23] = "End",
        [0x24] = "Home",
        [0x25] = "←",
        [0x26] = "↑",
        [0x27] = "→",
        [0x28] = "↓",
        [0x2C] = "Print Screen",
        [0x2D] = "Insert",
        [0x2E] = "Delete",
        [0x5B] = "左 Win",
        [0x5C] = "右 Win",
        [0x5D] = "菜单键",
        [0x60] = "小键盘0", [0x61] = "小键盘1", [0x62] = "小键盘2", [0x63] = "小键盘3",
        [0x64] = "小键盘4", [0x65] = "小键盘5", [0x66] = "小键盘6", [0x67] = "小键盘7",
        [0x68] = "小键盘8", [0x69] = "小键盘9",
        [0x6A] = "小键盘*", [0x6B] = "小键盘+", [0x6C] = "小键盘回车",
        [0x6D] = "小键盘-", [0x6E] = "小键盘.", [0x6F] = "小键盘/",
        [0x90] = "数字锁定",
        [0x91] = "滚动锁定",
        [0x10] = "Shift", [0x11] = "Ctrl", [0x12] = "Alt",
        [0xA6] = "浏览器后退", [0xA7] = "浏览器前进", [0xA8] = "浏览器刷新",
        [0xA9] = "浏览器停止", [0xAA] = "浏览器搜索", [0xAB] = "浏览器收藏", [0xAC] = "浏览器主页",
        [0xAD] = "静音", [0xAE] = "音量-", [0xAF] = "音量+",
        [0xB0] = "下一曲", [0xB1] = "上一曲", [0xB2] = "停止播放", [0xB3] = "播放/暂停",
        [0xB4] = "邮件", [0xB5] = "媒体播放器", [0xB6] = "我的电脑",
        [0xBA] = ";", [0xBB] = "=", [0xBC] = ",", [0xBD] = "-", [0xBE] = ".",
        [0xBF] = "/", [0xC0] = "`",
        [0xDB] = "[", [0xDC] = "\\", [0xDD] = "]", [0xDE] = "'",
    };

    public static string Get(int vk)
    {
        vk = Normalize(vk);
        if (vk is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A) return ((char)vk).ToString();
        if (vk is >= 0x70 and <= 0x87) return $"F{vk - 0x6F}";
        return Names.TryGetValue(vk, out var name) ? name : $"VK_0x{vk:X2}";
    }
}
