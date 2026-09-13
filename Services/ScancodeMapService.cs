using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using KeyRemapper.Models;
using Microsoft.Win32;

namespace KeyRemapper.Services;

/// <summary>
/// 系统级映射：写入 HKLM\SYSTEM\...\Keyboard Layout\Scancode Map（键盘驱动层重映射，SharpKeys 同款机制）。
/// 写入/删除后需重启电脑生效；生效后完全不依赖本软件运行，也不需要开机自启。
/// </summary>
public static class ScancodeMapService
{
    /// <summary>Apply/Remove 返回此值表示需要管理员权限（可调用 RelaunchElevated 走 UAC）。</summary>
    public const string NeedAdmin = "__NEED_ADMIN__";

    private const string KeyPath = @"SYSTEM\CurrentControlSet\Control\Keyboard Layout";
    private const string ValueName = "Scancode Map";
    private const uint MAPVK_VK_TO_VSC_EX = 0x04;

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    public static bool Exists()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(KeyPath);
            return key?.GetValue(ValueName) is byte[] data && data.Length > 12;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>把映射规则写入注册表。成功返回 null；缺管理员权限返回 NeedAdmin；否则返回错误描述。</summary>
    public static string? Apply(IEnumerable<KeyMapping> mappings)
    {
        try
        {
            var data = BuildScancodeMap(mappings, out string? error);
            if (error != null) return error;

            using var key = Registry.LocalMachine.CreateSubKey(KeyPath, writable: true);
            key.SetValue(ValueName, data, RegistryValueKind.Binary);
            return null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
        {
            return NeedAdmin;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>删除系统级映射。成功返回 null；缺管理员权限返回 NeedAdmin；否则返回错误描述。</summary>
    public static string? Remove()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(KeyPath, writable: true);
            if (key != null && key.GetValue(ValueName) != null)
                key.DeleteValue(ValueName);
            return null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
        {
            return NeedAdmin;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>以管理员身份重新启动本程序执行指定操作（触发 UAC），由 App 的命令行参数分支完成实际写入。</summary>
    public static void RelaunchElevated(string arguments)
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) throw new InvalidOperationException("无法获取程序路径");
        Process.Start(new ProcessStartInfo(exe)
        {
            Arguments = arguments,
            UseShellExecute = true,
            Verb = "runas",
        });
    }

    // Scancode Map 布局：8 字节全 0 头 + 4 字节条目数（含结束条目）+ N×4 字节映射条目 + 4 字节 0 结束符。
    // 每个条目 4 字节：低 16 位 = 映射后的扫描码，高 16 位 = 原始扫描码；扩展键扫描码高字节为 0xE0。
    private static byte[] BuildScancodeMap(IEnumerable<KeyMapping> mappings, out string? error)
    {
        error = null;
        var pairs = new List<(ushort To, ushort From)>();
        foreach (var m in mappings)
        {
            ushort from = MapVkToSc(m.FromVk);
            ushort to = MapVkToSc(m.ToVk);
            if (from == 0 || to == 0)
            {
                error = $"按键「{m.FromName}」或「{m.ToName}」没有对应的物理扫描码，无法用于系统级映射。";
                return Array.Empty<byte>();
            }
            pairs.Add((to, from));
        }

        var data = new byte[16 + pairs.Count * 4];
        data[8] = (byte)(pairs.Count + 1);
        int offset = 12;
        foreach (var (to, from) in pairs)
        {
            data[offset] = (byte)(to & 0xFF);
            data[offset + 1] = (byte)(to >> 8);
            data[offset + 2] = (byte)(from & 0xFF);
            data[offset + 3] = (byte)(from >> 8);
            offset += 4;
        }
        return data;
    }

    private static ushort MapVkToSc(int vk)
    {
        uint sc = MapVirtualKey(unchecked((uint)vk), MAPVK_VK_TO_VSC_EX);
        return (ushort)(sc & 0xFFFF);
    }
}
