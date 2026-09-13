namespace KeyRemapper.Models;

/// <summary>一条映射规则：把 FromVk 替换成 ToVk。</summary>
public sealed class KeyMapping
{
    public int FromVk { get; set; }
    public string FromName { get; set; } = "";
    public int ToVk { get; set; }
    public string ToName { get; set; } = "";
}

public sealed class AppSettings
{
    public bool Enabled { get; set; } = true;
    public List<KeyMapping> Mappings { get; set; } = new();
}
