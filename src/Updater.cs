using System;
using System.IO;
using System.IO.Compression;

using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;

namespace StreamRaid;

/// <summary>
/// GitHub Releases の latest.json を見て自動更新する。
/// 配布 zip は CI で RSA 署名され、ここに埋め込んだ公開鍵で検証できたものだけを適用する。
/// 適用は「ゲームを閉じる → 小さなバッチがファイルを置き換える → 起動し直す」の順。
/// </summary>
public static class Updater
{
    const string LatestJsonUrl = "https://github.com/Linn-0412/StreamRaid/releases/latest/download/latest.json";

    const string PublicKeyXml =
        "<RSAKeyValue><Modulus>zKdHWEM7UuHPdOpBtCW7Ri1/iqgZlbqji41XzFTmMbNtzuA+XSyD5WsizOiRvqTplA9KZRtnpMXIYJEG5kFMw4TaxSoP66LRHUuC8eluJ3DAb2YAGzLrbkk0JTewV+b1kIRfkRkuDRxmCPgM0Dz85VU1u/U2RJgO+PGIKlIFjjOPVFZhFqb0mf94WGsnYGsCU8i6PpQiVeyxD/6tf6viW6InM8vqc7nLj+xzifZSa0mj75kns1iZwDLnbsY66ugS8w7VKFKQSu74wlZifmyZIyOZJkqtmKRq9KgKjlG/hPwf0IR9L8S/c8gl3vK/CyG7UYLpygZU3siylLErToposACKQ+gr0gq3H1uqMgtUuwfpGJ1kJjdam9ygp0Cr6o5QkCOwVi9NKc0TxC8j7qAb0re8D5oMHQDqsb4vlB475LKTNvWxvwenYf6cl7MjcBbTLmJUM0ZDRmlZ/f9U0S2XRuVDBLNBgqjX97j27sAWSwsoB3hepu7pBGNJ130yoPOB</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

    public record Info(string Version, string Url, string Sha256, string Signature);

    static readonly System.Net.Http.HttpClient Http = CreateHttp();

    static System.Net.Http.HttpClient CreateHttp()
    {
        var h = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("StreamRaid-Updater");
        return h;
    }

    /// <summary>このゲームのバージョン(project.godot の application/config/version)。</summary>
    public static string CurrentVersion => (string)ProjectSettings.GetSetting("application/config/version", "0.0.0");

    /// <summary>書き出した本番のゲーム(エディタや dotnet build の確認用でない)なら true。更新はこのときだけ適用できる。</summary>
    public static bool CanApply => OS.HasFeature("template");

    /// <summary>新しいバージョンがあれば返す。なければ null。</summary>
    public static async Task<Info> CheckAsync()
    {
        // 開発用: 環境変数 STREAMRAID_UPDATE_URL で確認先を差し替えられる(更新の動作確認用)
        var url = System.Environment.GetEnvironmentVariable("STREAMRAID_UPDATE_URL");
        var json = await Http.GetStringAsync(string.IsNullOrEmpty(url) ? LatestJsonUrl : url);
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        var info = new Info(r.GetProperty("version").GetString(), r.GetProperty("url").GetString(),
                            r.GetProperty("sha256").GetString(), r.GetProperty("signature").GetString());
        return IsNewer(info.Version, CurrentVersion) ? info : null;
    }

    public static bool IsNewer(string latest, string current)
    {
        static Version P(string v)
        {
            v = (v ?? "").Trim().TrimStart('v');
            int dash = v.IndexOfAny(new[] { '-', '+' });
            if (dash >= 0) v = v[..dash];
            return System.Version.TryParse(v, out var x) ? x : new Version(0, 0);
        }
        return P(latest) > P(current);
    }

    /// <summary>ダウンロード → 署名の検証 → 展開 → 置き換え用のバッチを起動してゲームを終了する。</summary>
    public static async Task ApplyAsync(Info info, Action<string> progress)
    {
        if (!CanApply) throw new InvalidOperationException("開発中の起動では更新を適用できません(書き出したゲームで使えます)");
        progress?.Invoke("ダウンロードしています…");
        var data = await Http.GetByteArrayAsync(info.Url);

        progress?.Invoke("確認しています…");
        Verify(data, info);

        var work = Path.Combine(Path.GetTempPath(), "StreamRaid-update", info.Version);
        if (Directory.Exists(work)) Directory.Delete(work, true);
        Directory.CreateDirectory(work);
        var zip = Path.Combine(work, "update.zip");
        await File.WriteAllBytesAsync(zip, data);
        var files = Path.Combine(work, "files");
        ZipFile.ExtractToDirectory(zip, files);

        // zip の中が 1 つのフォルダにまとまっていれば、その中身を使う
        var src = files;
        var dirs = Directory.GetDirectories(files);
        if (Directory.GetFiles(files).Length == 0 && dirs.Length == 1) src = dirs[0];

        var exe = OS.GetExecutablePath();
        var dest = Path.GetDirectoryName(exe);
        var pid = OS.GetProcessId();
        var bat = Path.Combine(work, "apply.cmd");
        // ゲームが閉じるのを待ってからファイルを上書きし、起動し直す(ユーザーの設定・記録は AppData にあるので消えない)
        File.WriteAllText(bat, string.Join("\r\n",
            "@echo off",
            "chcp 65001 > nul",
            ":wait",
            $"tasklist /FI \"PID eq {pid}\" | find \"{pid}\" > nul",
            "if not errorlevel 1 (timeout /t 1 /nobreak > nul & goto wait)",
            $"robocopy \"{src}\" \"{dest}\" /E /R:5 /W:1 /NFL /NDL /NJH /NJS > nul",
            $"start \"\" \"{exe}\" -- --updated",
            ""), new System.Text.UTF8Encoding(false));

        progress?.Invoke("更新して起動し直します…");
        OS.CreateProcess("cmd.exe", new[] { "/c", bat });
        Main.I.GetTree().Quit();
    }

    static void Verify(byte[] data, Info info)
    {
        var hash = SHA256.HashData(data);
        if (!string.Equals(Convert.ToHexString(hash), info.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new Exception("ダウンロードしたファイルが壊れています(もう一度試してください)");
        using var rsa = RSA.Create();
        rsa.FromXmlString(PublicKeyXml);
        if (!rsa.VerifyHash(hash, Convert.FromBase64String(info.Signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            throw new Exception("更新ファイルの署名が正しくありません(適用しませんでした)");
    }
}
