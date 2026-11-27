using System;
using System.Collections.Concurrent;
using Godot;

namespace StreamRaid;

/// <summary>YouTube / Twitch のコメントを受け取って、メインスレッドで Message に流す。</summary>
public partial class ChatHub : Node
{
    public event Action<ChatMsg> Message;

    readonly YouTubeChat _yt = new();
    readonly TwitchChat _tw = new();
    readonly ConcurrentQueue<ChatMsg> _queue = new();
    volatile string _ytState = "off", _ytText = "", _twState = "off", _twText = "";

    public override void _Ready()
    {
        _yt.Message += m => _queue.Enqueue(m);
        _tw.Message += m => _queue.Enqueue(m);
        _yt.Status += (s, t) => { _ytState = s; _ytText = t; };
        _tw.Status += (s, t) => { _twState = s; _twText = t; };
    }

    public void Apply(Settings s)
    {
        var yt = s.YouTubeMode == "url" ? s.YouTubeUrl : s.YouTubeChannel;
        if (string.IsNullOrWhiteSpace(yt)) { _yt.Stop(); _ytState = "off"; _ytText = ""; }
        else _yt.Start(yt);
        if (string.IsNullOrWhiteSpace(s.TwitchChannel)) { _tw.Stop(); _twState = "off"; _twText = ""; }
        else _tw.Start(s.TwitchChannel);
    }

    public bool AnyConnected => _ytState == "connected" || _twState == "connected";

    public string StatusLine()
    {
        static string Mark(string s) => s switch { "connected" => "●", "connecting" => "…", "error" => "×", _ => "−" };
        return $"YouTube {Mark(_ytState)}   Twitch {Mark(_twState)}";
    }

    public string Detail(string platform) => platform == "youtube" ? _ytText : _twText;
    public string State(string platform) => platform == "youtube" ? _ytState : _twState;

    public override void _Process(double delta)
    {
        int n = 0;
        while (n++ < 200 && _queue.TryDequeue(out var m)) Message?.Invoke(m);
    }

    public override void _ExitTree()
    {
        _yt.Stop();
        _tw.Stop();
    }
}
