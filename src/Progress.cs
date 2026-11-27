using System.Text.Json;
using Godot;

namespace StreamRaid;

/// <summary>塔の進み具合(番人を倒した一番上の階)。チャンネルごとにこの PC に保存し、「続きから」に使う。</summary>
public class Progress
{
    /// <summary>倒した番人の一番上の階(0 = まだ誰も倒していない)。</summary>
    public int Checkpoint { get; set; }
    /// <summary>100 階を登りきった回数。</summary>
    public int Clears { get; set; }

    string _path;

    public static Progress Load(string channel)
    {
        DirAccess.MakeDirRecursiveAbsolute("user://progress");
        var path = $"user://progress/{channel}.json";
        Progress p = null;
        try
        {
            if (FileAccess.FileExists(path))
            {
                using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
                p = JsonSerializer.Deserialize<Progress>(f.GetAsText());
            }
        }
        catch { }
        p ??= new Progress();
        p._path = path;
        return p;
    }

    public void Save()
    {
        using var f = FileAccess.Open(_path, FileAccess.ModeFlags.Write);
        f?.StoreString(JsonSerializer.Serialize(this));
    }
}
