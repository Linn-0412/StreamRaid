using Godot;

namespace StreamRaid;

/// <summary>配信者が変えられる設定(のちに設定画面から編集する)。</summary>
public class Settings
{
    public string StreamerName { get; set; } = "配信者";
    public int FloorCount { get; set; } = 3;
    public int PartySize { get; set; } = 6;
    public float LobbySeconds { get; set; } = 60;
    /// <summary>ボス戦で 1 人が攻撃できる間隔(秒)。</summary>
    public float RaidInterval { get; set; } = 1.0f;
    /// <summary>1 人が敵を召喚できる間隔(秒)。</summary>
    public float SummonCooldown { get; set; } = 20f;

    /// <summary>"channel"(@ハンドルから配信を探す)か "url"(配信の URL を直接指定。限定公開用)。</summary>
    public string YouTubeMode { get; set; } = "channel";
    public string YouTubeChannel { get; set; } = "";
    public string YouTubeUrl { get; set; } = "";
    public string TwitchChannel { get; set; } = "";
    /// <summary>配信者のアバター(VRM ファイルの場所)。空なら標準のキャラ。</summary>
    public string AvatarPath { get; set; } = "";

    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasConnection =>
        !string.IsNullOrWhiteSpace(YouTubeMode == "url" ? YouTubeUrl : YouTubeChannel) || !string.IsNullOrWhiteSpace(TwitchChannel);

    /// <summary>視聴者の記録を分ける名前(接続先ごと)。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string ChannelKey
    {
        get
        {
            var k = !string.IsNullOrWhiteSpace(YouTubeChannel) ? YouTubeChannel : TwitchChannel;
            k = string.IsNullOrWhiteSpace(k) ? "test" : k.Trim().TrimStart('@', '#');
            foreach (var c in System.IO.Path.GetInvalidFileNameChars()) k = k.Replace(c, '_');
            return k.Replace('/', '_').Replace(':', '_');
        }
    }

    const string Path = "user://settings.json";

    public static Settings Load()
    {
        try
        {
            if (FileAccess.FileExists(Path))
            {
                using var f = FileAccess.Open(Path, FileAccess.ModeFlags.Read);
                return System.Text.Json.JsonSerializer.Deserialize<Settings>(f.GetAsText()) ?? new Settings();
            }
        }
        catch { }
        return new Settings();
    }

    public void Save()
    {
        using var f = FileAccess.Open(Path, FileAccess.ModeFlags.Write);
        f?.StoreString(System.Text.Json.JsonSerializer.Serialize(this, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
}

/// <summary>キー・マウス・ゲームパッドの割り当て。</summary>
public static class Inputs
{
    public static void Setup()
    {
        Add("move_left", new InputEventKey { PhysicalKeycode = Key.A }, new InputEventKey { PhysicalKeycode = Key.Left },
            new InputEventJoypadMotion { Axis = JoyAxis.LeftX, AxisValue = -1 });
        Add("move_right", new InputEventKey { PhysicalKeycode = Key.D }, new InputEventKey { PhysicalKeycode = Key.Right },
            new InputEventJoypadMotion { Axis = JoyAxis.LeftX, AxisValue = 1 });
        Add("move_forward", new InputEventKey { PhysicalKeycode = Key.W }, new InputEventKey { PhysicalKeycode = Key.Up },
            new InputEventJoypadMotion { Axis = JoyAxis.LeftY, AxisValue = -1 });
        Add("move_back", new InputEventKey { PhysicalKeycode = Key.S }, new InputEventKey { PhysicalKeycode = Key.Down },
            new InputEventJoypadMotion { Axis = JoyAxis.LeftY, AxisValue = 1 });
        Add("attack", new InputEventKey { PhysicalKeycode = Key.J }, new InputEventMouseButton { ButtonIndex = MouseButton.Left },
            new InputEventJoypadButton { ButtonIndex = JoyButton.X });
        Add("dodge", new InputEventKey { PhysicalKeycode = Key.Space }, new InputEventKey { PhysicalKeycode = Key.K },
            new InputEventMouseButton { ButtonIndex = MouseButton.Right }, new InputEventJoypadButton { ButtonIndex = JoyButton.A });
        Add("start", new InputEventKey { PhysicalKeycode = Key.Enter }, new InputEventJoypadButton { ButtonIndex = JoyButton.Start });
    }

    static void Add(string name, params InputEvent[] events)
    {
        if (!InputMap.HasAction(name)) InputMap.AddAction(name, 0.25f);
        foreach (var e in events) InputMap.ActionAddEvent(name, e);
    }
}
