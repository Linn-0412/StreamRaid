using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace StreamRaid;

/// <summary>
/// YouTube のライブチャット(CommentDeck と同じ仕組み)。
/// 公式 API は 1 日の上限が厳しいため、YouTube のチャット画面が使っている live_chat ページ + get_live_chat から読む。
/// 別スレッドで動くので、イベントはメインスレッドで受け取り直すこと(ChatHub がやる)。
/// </summary>
public class YouTubeChat
{
    public event Action<ChatMsg> Message;
    public event Action<string, string> Status; // (connecting/connected/error, 説明)

    const string UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36";
    static readonly HttpClient Http = CreateHttp();
    CancellationTokenSource _cts;

    static HttpClient CreateHttp()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd(UA);
        h.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ja,en;q=0.8");
        return h;
    }

    public void Start(string input)
    {
        Stop();
        var cts = _cts = new CancellationTokenSource();
        Task.Run(() => Run(input, cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
    }

    /// <summary>入力が動画(配信)を直接指しているならその ID。</summary>
    public static string VideoId(string input)
    {
        var s = (input ?? "").Trim();
        if (Regex.IsMatch(s, @"^[\w-]{11}$") && !s.StartsWith('@')) return s;
        var m = Regex.Match(s, @"(?:v=|youtu\.be/|/live/|/shorts/)([\w-]{11})");
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>入力をチャンネルの URL にする(@ハンドル / URL / チャンネル ID)。</summary>
    public static string ChannelUrl(string input)
    {
        var s = (input ?? "").Trim();
        if (s.Length == 0) return null;
        if (s.StartsWith("http"))
        {
            var m = Regex.Match(s, @"youtube\.com/(@[^/?#]+|channel/[\w-]+|c/[^/?#]+|user/[^/?#]+)");
            return m.Success ? "https://www.youtube.com/" + m.Groups[1].Value : null;
        }
        if (s.StartsWith('@')) return "https://www.youtube.com/" + s;
        if (Regex.IsMatch(s, @"^UC[\w-]{22}$")) return "https://www.youtube.com/channel/" + s;
        return "https://www.youtube.com/@" + s;
    }

    async Task Run(string input, CancellationToken ct)
    {
        var direct = VideoId(input);
        var channel = direct == null ? ChannelUrl(input) : null;
        if (direct == null && channel == null)
        {
            Status?.Invoke("error", "チャンネル(@ハンドル)か配信の URL を入れてください");
            return;
        }
        while (!ct.IsCancellationRequested)
        {
            try
            {
                Status?.Invoke("connecting", "配信を探しています…");
                var video = direct ?? await FindLiveVideo(channel, ct);
                if (video == null)
                    Status?.Invoke("connecting", "配信中・予約中の公開の枠が見つかりません。始まるのを待っています(限定公開は URL で指定)");
                else
                {
                    await Session(video, ct);
                    Status?.Invoke("connecting", "配信が終了しました。次の配信を待っています…");
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception e) { Status?.Invoke("connecting", $"{e.Message}(30 秒後にもう一度試します)"); }
            try { await Task.Delay(30000, ct); } catch { return; }
        }
    }

    static async Task<string> FindLiveVideo(string channel, CancellationToken ct)
    {
        var html = await Http.GetStringAsync(channel + "/live", ct);
        var m = Regex.Match(html, @"<link rel=""canonical"" href=""https://www\.youtube\.com/watch\?v=([\w-]{11})""");
        return m.Success ? m.Groups[1].Value : null;
    }

    async Task Session(string video, CancellationToken ct)
    {
        var html = await Http.GetStringAsync($"https://www.youtube.com/live_chat?is_popout=1&v={video}", ct);
        var key = Capture(html, @"""INNERTUBE_API_KEY"":""([^""]+)""");
        var version = Capture(html, @"""INNERTUBE_CLIENT_VERSION"":""([^""]+)""") ?? Capture(html, @"""clientVersion"":""([^""]+)""") ?? "2.20250101.00.00";
        var data = InitialData(html) ?? throw new Exception("チャットが見つかりません(まだ配信が始まっていない可能性があります)");
        var renderer = Get(data, "contents", "liveChatRenderer") ?? throw new Exception("この配信のチャットを読み込めませんでした(チャットが無効か、配信前です)");

        // 「上位のチャット」ではなく「すべてのチャット」に切り替える
        var cont = Get(renderer, "header", "liveChatHeaderRenderer", "viewSelector", "sortFilterSubMenuRenderer", "subMenuItems", 1,
                       "continuation", "reloadContinuationData", "continuation")?.GetValue<string>()
                   ?? NextContinuation(renderer["continuations"])?.cont
                   ?? throw new Exception("チャットの続きを取得できませんでした");

        Status?.Invoke("connected", $"https://youtu.be/{video} のチャットにつながりました");
        var url = "https://www.youtube.com/youtubei/v1/live_chat/get_live_chat?prettyPrint=false" + (key != null ? "&key=" + key : "");
        bool first = true; // 最初の取得はつなぐ前のコメントなので使わない
        int errors = 0;
        while (!ct.IsCancellationRequested)
        {
            var body = new JsonObject
            {
                ["context"] = new JsonObject { ["client"] = new JsonObject { ["clientName"] = "WEB", ["clientVersion"] = version, ["hl"] = "ja", ["gl"] = "JP" } },
                ["continuation"] = cont,
            };
            JsonNode value;
            try
            {
                using var res = await Http.PostAsync(url, new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"), ct);
                if (!res.IsSuccessStatusCode)
                {
                    if (++errors > 5) throw new Exception($"YouTube からの応答がエラーになりました({(int)res.StatusCode})");
                    await Task.Delay(3000, ct);
                    continue;
                }
                value = JsonNode.Parse(await res.Content.ReadAsStringAsync(ct));
            }
            catch (HttpRequestException e)
            {
                if (++errors > 5) throw new Exception("YouTube との通信に失敗しました: " + e.Message);
                await Task.Delay(3000, ct);
                continue;
            }
            errors = 0;
            var lcc = Get(value, "continuationContents", "liveChatContinuation");
            if (!first && lcc?["actions"] is JsonArray actions)
                foreach (var a in actions)
                {
                    var item = Get(a, "addChatItemAction", "item");
                    if (item == null) continue;
                    var m = ParseItem(item);
                    if (m != null) Message?.Invoke(m);
                }
            first = false;
            var next = NextContinuation(lcc?["continuations"]);
            if (next == null) return; // 続きがない = 配信終了
            cont = next.Value.cont;
            await Task.Delay(Math.Clamp(next.Value.wait, 500, 5000), ct);
        }
    }

    static (string cont, int wait)? NextContinuation(JsonNode conts)
    {
        if (conts is not JsonArray arr) return null;
        foreach (var c in arr)
            foreach (var key in new[] { "invalidationContinuationData", "timedContinuationData", "reloadContinuationData" })
            {
                var d = c?[key];
                var cont = d?["continuation"]?.GetValue<string>();
                if (cont != null)
                {
                    int wait = d["timeoutMs"] is JsonValue v && v.TryGetValue<int>(out var w) ? w : 1500;
                    return (cont, wait);
                }
            }
        return null;
    }

    public static ChatMsg ParseItem(JsonNode item)
    {
        if (item["liveChatTextMessageRenderer"] is JsonNode t) return Base(t, 0, null);
        if (item["liveChatPaidMessageRenderer"] is JsonNode p)
        {
            var amount = TextOf(p["purchaseAmountText"]);
            return Base(p, ToYen(amount), amount);
        }
        if (item["liveChatPaidStickerRenderer"] is JsonNode s)
        {
            var amount = TextOf(s["purchaseAmountText"]);
            return Base(s, ToYen(amount), amount) with { Text = "(ステッカー)" };
        }
        if (item["liveChatMembershipItemRenderer"] is JsonNode mem)
        {
            var header = TextOf(mem["headerSubtext"]);
            var m = Base(mem, 500, "メンバー加入");
            return m with { Member = true, Text = m.Text.Length > 0 ? m.Text : header };
        }
        if (item["liveChatSponsorshipsGiftPurchaseAnnouncementRenderer"] is JsonNode g)
        {
            var h = Get(g, "header", "liveChatSponsorshipsHeaderRenderer");
            var primary = TextOf(h?["primaryText"]);
            var cm = Regex.Match(primary, @"(\d+)");
            int count = cm.Success ? int.Parse(cm.Groups[1].Value) : 1;
            return new ChatMsg(TextOf(h?["authorName"]), primary, "youtube", true, 500 * count, $"ギフト {count}",
                               g["authorExternalChannelId"]?.GetValue<string>());
        }
        return null;
    }

    static ChatMsg Base(JsonNode r, int yen, string paidText)
    {
        bool member = false, mod = false, owner = false;
        if (r["authorBadges"] is JsonArray badges)
            foreach (var b in badges)
            {
                var br = b?["liveChatAuthorBadgeRenderer"];
                switch (Get(br, "icon", "iconType")?.GetValue<string>())
                {
                    case "OWNER": owner = true; break;
                    case "MODERATOR": mod = true; break;
                }
                if (br?["customThumbnail"] != null) member = true;
            }
        return new ChatMsg(TextOf(r["authorName"]), TextOf(r["message"]), "youtube", member, yen, paidText,
                           r["authorExternalChannelId"]?.GetValue<string>(), mod, owner);
    }

    static string TextOf(JsonNode v)
    {
        if (v == null) return "";
        if (v["simpleText"] is JsonValue st) return st.GetValue<string>();
        if (v["runs"] is not JsonArray runs) return "";
        var sb = new StringBuilder();
        foreach (var r in runs)
        {
            if (r?["text"] is JsonValue tv) sb.Append(tv.GetValue<string>());
            else if (Get(r, "emoji", "shortcuts", 0) is JsonValue sc) sb.Append(sc.GetValue<string>());
        }
        return sb.ToString();
    }

    /// <summary>金額の文字(「¥1,000」「$5.00」など)をだいたいの円にする(必殺技の強さ用)。</summary>
    public static int ToYen(string text)
    {
        double amount = ParseAmount(text);
        double rate = text.Contains('¥') || text.Contains('￥') || text.Contains("JP") ? 1
                    : text.Contains('€') ? 160 : text.Contains('£') ? 190 : text.Contains("₩") ? 0.11
                    : text.Contains('$') ? 150 : 1;
        return (int)Math.Round(amount * rate);
    }

    public static double ParseAmount(string text)
    {
        var digits = new string((text ?? "").Where(c => char.IsAsciiDigit(c) || c == '.' || c == ',').ToArray());
        string n;
        if (digits.Contains('.') && digits.Contains(',')) n = digits.Replace(",", "");
        else if (digits.LastIndexOf(',') is var pos and >= 0)
            n = digits.Length - pos - 1 == 3 ? digits.Replace(",", "") : digits.Replace(',', '.'); // "1,000" と "5,00"
        else n = digits;
        return double.TryParse(n.Trim('.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    static string Capture(string html, string pattern)
    {
        var m = Regex.Match(html, pattern);
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>ページに埋め込まれた ytInitialData の JSON を取り出す。</summary>
    static JsonNode InitialData(string html)
    {
        int pos = html.IndexOf("ytInitialData", StringComparison.Ordinal);
        if (pos < 0) return null;
        int start = html.IndexOf('{', pos);
        if (start < 0) return null;
        int depth = 0;
        bool inStr = false, esc = false;
        for (int i = start; i < html.Length; i++)
        {
            char c = html[i];
            if (inStr)
            {
                if (esc) esc = false;
                else if (c == '\\') esc = true;
                else if (c == '"') inStr = false;
                continue;
            }
            if (c == '"') inStr = true;
            else if (c == '{') depth++;
            else if (c == '}' && --depth == 0)
            {
                try { return JsonNode.Parse(html.AsSpan(start, i - start + 1).ToString()); }
                catch { return null; }
            }
        }
        return null;
    }

    static JsonNode Get(JsonNode n, params object[] path)
    {
        foreach (var p in path)
        {
            if (n == null) return null;
            n = p switch
            {
                string k when n is JsonObject o => o[k],
                int i when n is JsonArray a => i < a.Count ? a[i] : null,
                _ => null,
            };
        }
        return n;
    }
}
