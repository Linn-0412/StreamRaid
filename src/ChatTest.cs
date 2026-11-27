using Godot;

namespace StreamRaid;

/// <summary>
/// 開発用: コメント取得だけを動かして受け取ったものを表示する。
/// 例: godot --headless --path . -- --chattest --yt=@LofiGirl --tw=some_channel --seconds=40
/// </summary>
public partial class ChatTest : Node
{
    double _left = 40;
    int _count;
    ChatHub _hub;
    string _last = "";

    public override void _Ready()
    {
        var s = new Settings();
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            if (a.StartsWith("--yt=")) { s.YouTubeChannel = a[5..]; if (a.Contains("watch?v=") || a.Contains("youtu.be") || a.Contains("/live/")) { s.YouTubeMode = "url"; s.YouTubeUrl = a[5..]; } }
            if (a.StartsWith("--tw=")) s.TwitchChannel = a[5..];
            if (a.StartsWith("--seconds=") && double.TryParse(a[10..], out var sec)) _left = sec;
        }
        _hub = new ChatHub();
        AddChild(_hub);
        _hub.Message += m =>
        {
            _count++;
            if (_count <= 25)
                GD.Print($"[{m.Platform}] {m.User}: {m.Text}" + (m.PaidYen > 0 ? $"  <{m.PaidText} ≒{m.PaidYen}円>" : "") + (m.Member ? " (member)" : "") + (m.Moderator ? " (mod)" : ""));
        };
        CallDeferred(nameof(Begin), s.YouTubeMode == "url" ? s.YouTubeUrl : s.YouTubeChannel, s.TwitchChannel);
        _settings = s;
    }

    Settings _settings;

    void Begin(string yt, string tw)
    {
        GD.Print($"chattest: youtube='{yt}' twitch='{tw}'");
        _hub.Apply(_settings);
    }

    public override void _Process(double delta)
    {
        var st = $"YT[{_hub.State("youtube")}] {_hub.Detail("youtube")} | TW[{_hub.State("twitch")}] {_hub.Detail("twitch")}";
        if (st != _last) { GD.Print("status: " + st); _last = st; }
        _left -= delta;
        if (_left <= 0)
        {
            GD.Print($"chattest: received {_count} messages");
            GetTree().Quit();
        }
    }
}
