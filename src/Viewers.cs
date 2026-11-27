using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace StreamRaid;

/// <summary>視聴者ごとの成長(職業・レベル・経験値・称号の元になる回数)。この PC の中にだけ保存する。</summary>
public class Viewers
{
    public class Data
    {
        public string Job { get; set; } = "";
        public int Level { get; set; } = 1;
        public int Exp { get; set; }
        public int Runs { get; set; }
        public int Mvp { get; set; }
    }

    readonly string _path;
    readonly Dictionary<string, Data> _d;

    public Viewers(string channel)
    {
        DirAccess.MakeDirRecursiveAbsolute("user://viewers");
        _path = $"user://viewers/{channel}.json";
        _d = new Dictionary<string, Data>();
        if (!FileAccess.FileExists(_path)) return;
        try
        {
            using var f = FileAccess.Open(_path, FileAccess.ModeFlags.Read);
            _d = JsonSerializer.Deserialize<Dictionary<string, Data>>(f.GetAsText()) ?? new();
        }
        catch { _d = new(); }
    }

    public Data Get(string name)
    {
        if (!_d.TryGetValue(name, out var v)) _d[name] = v = new Data();
        return v;
    }

    public static int ExpToNext(int level) => 60 + level * 40;

    /// <summary>経験値を足して、上がったレベル数を返す。</summary>
    public int AddExp(string name, int exp)
    {
        var v = Get(name);
        v.Exp += exp;
        int ups = 0;
        while (v.Exp >= ExpToNext(v.Level))
        {
            v.Exp -= ExpToNext(v.Level);
            v.Level++;
            ups++;
        }
        return ups;
    }

    /// <summary>このチャンネルの記録をすべて消す。</summary>
    public void Clear()
    {
        _d.Clear();
        Save();
    }

    public void Save()
    {
        using var f = FileAccess.Open(_path, FileAccess.ModeFlags.Write);
        f?.StoreString(JsonSerializer.Serialize(_d, new JsonSerializerOptions { WriteIndented = true }));
    }
}
