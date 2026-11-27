using Godot;

namespace StreamRaid;

/// <summary>配信者が変えられる設定(F10 の設定画面で編集し、user://settings.json に保存)。</summary>
public class Settings
{
    public string StreamerName { get; set; } = "配信者";

    // ---- ゲーム
    /// <summary>塔の最上階(20〜100 階、5 刻み)。5 階ごとに番人。</summary>
    public int FloorCount { get; set; } = 20;
    public int PartySize { get; set; } = 6;
    public float LobbySeconds { get; set; } = 60;
    /// <summary>0 = やさしい / 1 = ふつう / 2 = むずかしい。</summary>
    public int Difficulty { get; set; } = 1;

    // ---- コメント
    /// <summary>ボス戦で 1 人が攻撃できる間隔(秒)。</summary>
    public float RaidInterval { get; set; } = 1.0f;
    /// <summary>視聴者が敵を召喚できるか。</summary>
    public bool AllowSummon { get; set; } = true;
    /// <summary>1 人が敵を召喚できる間隔(秒)。</summary>
    public float SummonCooldown { get; set; } = 20f;
    /// <summary>投げ銭・Bits などをボスへの必殺技にするか。</summary>
    public bool PaidSpecial { get; set; } = true;
    /// <summary>仲間になれるのをメンバー(サブスク)・モデレーターだけにするか。</summary>
    public bool MembersOnlyJoin { get; set; } = false;
    /// <summary>コメントを無視する人(名前。大文字小文字は区別しない)。</summary>
    public string[] BlockedUsers { get; set; } = System.Array.Empty<string>();

    // ---- その他
    /// <summary>画面右上のテスト用キーの説明を出すか。</summary>
    public bool ShowTestKeys { get; set; } = true;

    /// <summary>難しさに応じた敵の強さ(HP・攻撃力の倍率)。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public float EnemyScale => Difficulty switch { 0 => 0.7f, 2 => 1.35f, _ => 1f };

    public bool IsBlocked(string user)
    {
        if (string.IsNullOrEmpty(user) || BlockedUsers == null) return false;
        foreach (var b in BlockedUsers)
            if (string.Equals(b?.Trim().TrimStart('@'), user.Trim().TrimStart('@'), System.StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

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
                var s = System.Text.Json.JsonSerializer.Deserialize<Settings>(f.GetAsText()) ?? new Settings();
                // 古い設定(階の数 1〜8)や半端な値を 20〜100 の 5 刻みにそろえる
                s.FloorCount = Mathf.Clamp((int)(Mathf.Round(s.FloorCount / 5f) * 5), 20, 100);
                return s;
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
