using System;
using Godot;

namespace StreamRaid;

/// <summary>コメント 1 件。YouTube / Twitch の取得部分(次の段階)もこの形で渡す。</summary>
public record ChatMsg(string User, string Text, string Platform = "test", bool Member = false, int PaidYen = 0, string PaidText = null);

/// <summary>キーボードでコメントを出すテスト用(配信につなぐ前の確認と、デモ録画に使う)。</summary>
public partial class TestChat : Node
{
    public event Action<ChatMsg> Message;

    public static readonly string[] Names =
    {
        "みかん", "night_owl", "さくら", "gamer_kun", "しろくま", "ねこまる", "Alice", "ぽんず",
        "tanaka", "うさぎ", "Zed", "もちこ", "akira_01", "こはく", "Ren", "たいやき",
    };
    static readonly string[] Jobs = { "戦士", "魔法使い", "僧侶" };
    static readonly string[] Monsters = { "スライム", "ゴブリン", "コウモリ" };
    static readonly string[] Cheers = { "がんばれ!", "いけー!", "www", "かわいい", "つよい", "ナイス!", "草", "えらい" };

    public static string Pick(string[] a) => a[GD.Randi() % a.Length];

    public void Send(ChatMsg m) => Message?.Invoke(m);

    public void Join(string user = null) => Send(new ChatMsg(user ?? Pick(Names), "#参加 " + Pick(Jobs)));
    public void Summon(string user = null) => Send(new ChatMsg(user ?? Pick(Names), "#召喚 " + Pick(Monsters)));
    public void Cheer(string user = null) => Send(new ChatMsg(user ?? Pick(Names), Pick(Cheers), Member: GD.Randf() < 0.2f));
    public void Paid(string user = null)
    {
        int yen = new[] { 200, 500, 1000, 2000, 5000, 10000 }[GD.Randi() % 6];
        Send(new ChatMsg(user ?? Pick(Names), "応援してます!", PaidYen: yen, PaidText: $"¥{yen:N0}"));
    }
    public void Skill(string user) => Send(new ChatMsg(user, "技"));

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } k) return;
        switch (k.Keycode)
        {
            case Key.F1: Join(); break;
            case Key.F2: Summon(); break;
            case Key.F3: Cheer(); break;
            case Key.F4: Paid(); break;
            case Key.F5:
                var members = Main.I.PartyNames;
                if (members.Count > 0) Skill(members[(int)(GD.Randi() % members.Count)]);
                break;
            case Key.F6:
                for (int i = 0; i < 20; i++) Cheer();
                break;
            default: return;
        }
        GetViewport().SetInputAsHandled();
    }
}
