using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace StreamRaid;

/// <summary>Twitch のチャット(公式 IRC over WebSocket)。ログイン不要の読み取り専用接続。</summary>
public class TwitchChat
{
    public event Action<ChatMsg> Message;
    public event Action<string, string> Status;

    const string Url = "wss://irc-ws.chat.twitch.tv:443";
    CancellationTokenSource _cts;

    public static string Normalize(string input)
    {
        var s = (input ?? "").Trim().TrimStart('#', '@');
        int i = s.LastIndexOf("twitch.tv/", StringComparison.OrdinalIgnoreCase);
        if (i >= 0) s = s[(i + "twitch.tv/".Length)..];
        int cut = s.IndexOfAny(new[] { '/', '?' });
        if (cut >= 0) s = s[..cut];
        return s.ToLowerInvariant();
    }

    public void Start(string input)
    {
        Stop();
        var channel = Normalize(input);
        if (channel.Length == 0) { Status?.Invoke("error", "チャンネル名を入れてください"); return; }
        var cts = _cts = new CancellationTokenSource();
        Task.Run(() => Run(channel, cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
    }

    async Task Run(string channel, CancellationToken ct)
    {
        int wait = 2;
        while (!ct.IsCancellationRequested)
        {
            Status?.Invoke("connecting", $"#{channel} に接続しています…");
            try
            {
                await Session(channel, ct);
                wait = 2;
            }
            catch (OperationCanceledException) { return; }
            catch (Exception e) { Status?.Invoke("error", $"{e.Message}({wait} 秒後に再接続します)"); }
            try { await Task.Delay(wait * 1000, ct); } catch { return; }
            wait = Math.Min(wait * 2, 60);
        }
    }

    async Task Session(string channel, CancellationToken ct)
    {
        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri(Url), ct);
        var nick = "justinfan" + Random.Shared.Next(10000, 90000);
        foreach (var line in new[] { "CAP REQ :twitch.tv/tags twitch.tv/commands", "PASS SCHMOOPIIE", $"NICK {nick}", $"JOIN #{channel}" })
            await Send(ws, line, ct);

        var buf = new byte[16384];
        var sb = new StringBuilder();
        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var r = await ws.ReceiveAsync(buf, ct);
            if (r.MessageType == WebSocketMessageType.Close) throw new Exception("Twitch との接続が切れました");
            sb.Append(Encoding.UTF8.GetString(buf, 0, r.Count));
            if (!r.EndOfMessage) continue;
            var text = sb.ToString();
            sb.Clear();
            foreach (var line in text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith("PING")) { await Send(ws, "PONG" + line[4..], ct); continue; }
                var irc = Parse(line);
                switch (irc.Command)
                {
                    case "ROOMSTATE": Status?.Invoke("connected", $"#{channel} のチャットにつながりました"); break;
                    case "PRIVMSG": Message?.Invoke(PrivMsg(irc)); break;
                    case "USERNOTICE": if (UserNotice(irc) is { } m) Message?.Invoke(m); break;
                    case "NOTICE":
                        var low = irc.Trailing.ToLowerInvariant();
                        if (low.Contains("login") || low.Contains("suspended")) Status?.Invoke("error", irc.Trailing);
                        break;
                    case "RECONNECT": return;
                }
            }
        }
        throw new Exception("Twitch との接続が切れました");
    }

    static Task Send(ClientWebSocket ws, string line, CancellationToken ct) =>
        ws.SendAsync(Encoding.UTF8.GetBytes(line), WebSocketMessageType.Text, true, ct);

    record Irc(Dictionary<string, string> Tags, string Command, string Trailing);

    static Irc Parse(string line)
    {
        var rest = line;
        var tags = new Dictionary<string, string>();
        if (rest.StartsWith('@'))
        {
            int sp = rest.IndexOf(' ');
            var t = sp >= 0 ? rest[1..sp] : rest[1..];
            rest = sp >= 0 ? rest[(sp + 1)..] : "";
            foreach (var kv in t.Split(';'))
            {
                int eq = kv.IndexOf('=');
                tags[eq >= 0 ? kv[..eq] : kv] = eq >= 0 ? Unescape(kv[(eq + 1)..]) : "";
            }
        }
        if (rest.StartsWith(':'))
        {
            int sp = rest.IndexOf(' ');
            rest = sp >= 0 ? rest[(sp + 1)..] : "";
        }
        int ti = rest.IndexOf(" :", StringComparison.Ordinal);
        var head = ti >= 0 ? rest[..ti] : rest;
        var trailing = ti >= 0 ? rest[(ti + 2)..] : "";
        return new Irc(tags, head.Split(' ')[0], trailing);
    }

    static string Unescape(string v)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < v.Length; i++)
        {
            if (v[i] != '\\' || i + 1 >= v.Length) { sb.Append(v[i]); continue; }
            char n = v[++i];
            sb.Append(n switch { 's' => ' ', ':' => ';', 'r' => '\r', 'n' => '\n', _ => n });
        }
        return sb.ToString();
    }

    static ChatMsg Base(Irc irc, string text, int yen = 0, string paidText = null)
    {
        var t = irc.Tags;
        var name = t.TryGetValue("display-name", out var dn) && dn.Length > 0 ? dn : t.GetValueOrDefault("login", "");
        var badges = t.GetValueOrDefault("badges", "");
        bool member = badges.Contains("subscriber/") || badges.Contains("founder/");
        return new ChatMsg(name, text, "twitch", member, yen, paidText, t.GetValueOrDefault("user-id"),
                           badges.Contains("moderator/"), badges.Contains("broadcaster/"));
    }

    static ChatMsg PrivMsg(Irc irc)
    {
        var text = irc.Trailing;
        if (text.StartsWith("\u0001ACTION ") && text.EndsWith('\u0001')) text = text[8..^1];
        if (irc.Tags.TryGetValue("bits", out var b) && int.TryParse(b, out var bits) && bits > 0)
            return Base(irc, text, (int)(bits * 1.4), $"{bits} bits"); // 1 bit ≒ 1.4 円として強さを決める
        return Base(irc, text);
    }

    static ChatMsg UserNotice(Irc irc)
    {
        var id = irc.Tags.GetValueOrDefault("msg-id", "");
        var system = irc.Tags.GetValueOrDefault("system-msg", "");
        var text = irc.Trailing.Length > 0 ? irc.Trailing : system;
        switch (id)
        {
            case "sub": case "resub": case "primepaidupgrade": case "giftpaidupgrade":
                return Base(irc, text, 500, "サブスク") with { Member = true };
            case "subgift": case "anonsubgift":
                return Base(irc, text, 500, "ギフト");
            case "submysterygift":
                int.TryParse(irc.Tags.GetValueOrDefault("msg-param-mass-gift-count", "1"), out var n);
                return Base(irc, text, 500 * Math.Max(1, n), $"ギフト {Math.Max(1, n)}");
            default:
                return null;
        }
    }
}
