using System;
using System.Linq;
using Godot;

namespace StreamRaid;

/// <summary>設定画面(F10)。接続 / ゲーム / コメント / その他 のタブ。</summary>
public partial class SettingsPanel : CanvasLayer
{
    public event Action<Settings> Applied;
    /// <summary>開いた(true)・閉じた(false)。</summary>
    public event Action<bool> Toggled;
    /// <summary>「視聴者の記録を消す」が押された。</summary>
    public event Action ResetViewers;
    /// <summary>「更新を確認」/「今すぐ更新」が押された。</summary>
    public event Action CheckUpdate, ApplyUpdate;

    Settings _s;
    ChatHub _hub;
    PanelContainer _panel;
    TabContainer _tabs;
    Label _paused, _ytStatus, _twStatus, _avatarLabel;
    string _avatarPath = "";
    FileDialog _file;

    // 接続
    LineEdit _name, _ytChannel, _ytUrl, _twitch;
    OptionButton _ytMode;
    // ゲーム
    SpinBox _floors, _lobby, _party;
    OptionButton _difficulty;
    // コメント
    SpinBox _raidInterval, _summonCd;
    CheckButton _allowSummon, _paidSpecial, _membersOnly;
    TextEdit _blocked;
    // その他
    CheckButton _testKeys;
    Label _updateLabel;
    Button _applyUpdate;

    public bool Open => _panel.Visible;

    public void Setup(Settings s, ChatHub hub)
    {
        _s = s;
        _hub = hub;
    }

    public override void _Ready()
    {
        Layer = 10;
        ProcessMode = ProcessModeEnum.Always; // ゲームを一時停止していても開け閉めできるように

        _paused = new Label
        {
            Text = "一時停止中",
            Visible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            LabelSettings = new LabelSettings { Font = Toon.Font, FontSize = 52, FontColor = new Color("fff3a0"), OutlineSize = 14, OutlineColor = new Color(0.05f, 0.03f, 0.08f) },
        };
        _paused.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _paused.OffsetLeft = -400; _paused.OffsetRight = 400; _paused.OffsetTop = 64; _paused.OffsetBottom = 134;
        AddChild(_paused);

        _panel = new PanelContainer { Visible = false, Theme = MakeTheme() };
        _panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        _panel.GrowHorizontal = Control.GrowDirection.Both;
        _panel.GrowVertical = Control.GrowDirection.Both;
        _panel.CustomMinimumSize = new Vector2(1040, 760);
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.06f, 0.14f, 0.97f), BorderColor = new Color("8ff0ff"),
            BorderWidthBottom = 3, BorderWidthTop = 3, BorderWidthLeft = 3, BorderWidthRight = 3,
            CornerRadiusTopLeft = 14, CornerRadiusTopRight = 14, CornerRadiusBottomLeft = 14, CornerRadiusBottomRight = 14,
            ContentMarginLeft = 32, ContentMarginRight = 32, ContentMarginTop = 22, ContentMarginBottom = 22,
        });
        AddChild(_panel);

        var outer = new VBoxContainer();
        outer.AddThemeConstantOverride("separation", 12);
        _panel.AddChild(outer);
        outer.AddChild(Title("設定"));

        _tabs = new TabContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        outer.AddChild(_tabs);
        _tabs.AddChild(ConnectionTab());
        _tabs.AddChild(GameTab());
        _tabs.AddChild(CommentTab());
        _tabs.AddChild(OtherTab());

        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        row.AddThemeConstantOverride("separation", 12);
        row.AddChild(Note("ゲームの設定は次の冒険から、それ以外はすぐ反映されます", 600));
        var save = Button("保存して閉じる");
        save.Pressed += Apply;
        var close = Button("閉じる");
        close.Pressed += () => Show(false);
        row.AddChild(save);
        row.AddChild(close);
        outer.AddChild(row);

        _file = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Access = FileDialog.AccessEnum.Filesystem,
            Filters = new[] { "*.vrm ; VRM アバター" },
            Title = "VRM アバターを選ぶ",
            UseNativeDialog = true,
        };
        _file.FileSelected += p => { _avatarPath = p; UpdateAvatarLabel(); };
        AddChild(_file);
    }

    // ---------------------------------------------------------------- タブ

    Control ConnectionTab()
    {
        var box = Page("接続", out var root);
        box.AddChild(Note("コメントを読むだけなので、ログインは要りません。設定はこの PC の中にだけ保存します。"));

        box.AddChild(Caption("配信者の名前(ゲーム内の表示)"));
        _name = Edit("配信者");
        box.AddChild(_name);

        box.AddChild(Caption("アバター(VRM)"));
        var arow = new HBoxContainer();
        arow.AddThemeConstantOverride("separation", 12);
        _avatarLabel = new Label { CustomMinimumSize = new Vector2(480, 0), ClipText = true, VerticalAlignment = VerticalAlignment.Center };
        var pick = Button("VRM を選ぶ…");
        pick.Pressed += () => _file.PopupCentered(new Vector2I(1100, 700));
        var clear = Button("標準に戻す");
        clear.Pressed += () => { _avatarPath = ""; UpdateAvatarLabel(); };
        arow.AddChild(_avatarLabel);
        arow.AddChild(pick);
        arow.AddChild(clear);
        box.AddChild(arow);

        box.AddChild(Caption("YouTube"));
        _ytMode = new OptionButton();
        _ytMode.AddItem("チャンネル(@ハンドル)から配信中の枠を探す", 0);
        _ytMode.AddItem("配信の URL を指定する(限定公開の配信はこちら)", 1);
        _ytMode.ItemSelected += _ => UpdateMode();
        box.AddChild(_ytMode);
        _ytChannel = Edit("@ハンドル(例: @your_channel)");
        _ytUrl = Edit("https://www.youtube.com/watch?v=...");
        box.AddChild(_ytChannel);
        box.AddChild(_ytUrl);
        _ytStatus = Note("");
        box.AddChild(_ytStatus);

        box.AddChild(Caption("Twitch"));
        _twitch = Edit("チャンネル名(例: your_channel)");
        box.AddChild(_twitch);
        _twStatus = Note("");
        box.AddChild(_twStatus);
        return root;
    }

    Control GameTab()
    {
        var box = Page("ゲーム", out var root);
        box.AddChild(Row("塔の最上階", _floors = Spin(20, 100, 5, " 階"), "5 階ごとに番人がいます。番人を倒すと記録され、次は「続きから」登れます。100 階はとても難しい"));
        box.AddChild(Row("参加受付の時間", _lobby = Spin(10, 300, 5, " 秒"), "受付中は Enter でいつでも出発できます"));
        box.AddChild(Row("仲間の最大人数", _party = Spin(1, 10, 1, " 人"), "あふれた人は控えになり、仲間が倒れると交代します"));
        _difficulty = new OptionButton();
        _difficulty.AddItem("やさしい", 0);
        _difficulty.AddItem("ふつう", 1);
        _difficulty.AddItem("むずかしい", 2);
        box.AddChild(Row("難しさ", _difficulty, "敵とボスの HP・攻撃力が変わります"));
        return root;
    }

    Control CommentTab()
    {
        var box = Page("コメント", out var root);
        box.AddChild(Row("ボス戦の攻撃の間隔", _raidInterval = Spin(0.2, 10, 0.1, " 秒"), "1 人がこれより速くコメントしても、攻撃は 1 回だけ(連投対策)"));
        _allowSummon = Check("視聴者が敵を召喚できる(#召喚)");
        box.AddChild(_allowSummon);
        box.AddChild(Row("召喚できる間隔", _summonCd = Spin(3, 300, 1, " 秒"), "1 人がこの間隔に 1 回だけ召喚できます"));
        _paidSpecial = Check("投げ銭・Bits・メンバー加入をボスへの必殺技にする");
        box.AddChild(_paidSpecial);
        _membersOnly = Check("仲間になれるのをメンバー・サブスク・モデレーターだけにする");
        box.AddChild(_membersOnly);
        box.AddChild(Caption("コメントを無視する人(1 行に 1 人)"));
        _blocked = new TextEdit { CustomMinimumSize = new Vector2(900, 120), PlaceholderText = "荒らし対策などに。名前をそのまま書いてください" };
        box.AddChild(_blocked);
        return root;
    }

    Control OtherTab()
    {
        var box = Page("その他", out var root);
        _testKeys = Check("画面右上にテスト用キー(F1〜F6)の説明を出す");
        box.AddChild(_testKeys);
        box.AddChild(Note("配信に映したくない場合はオフにしてください。キー自体はオフでも使えます。"));

        box.AddChild(Caption("バージョン・アップデート"));
        _updateLabel = Note($"いまのバージョン: v{Updater.CurrentVersion}");
        box.AddChild(_updateLabel);
        var urow = new HBoxContainer();
        urow.AddThemeConstantOverride("separation", 12);
        var check = Button("更新を確認");
        check.Pressed += () => CheckUpdate?.Invoke();
        _applyUpdate = Button("今すぐ更新");
        _applyUpdate.Disabled = true;
        _applyUpdate.Pressed += () => ApplyUpdate?.Invoke();
        urow.AddChild(check);
        urow.AddChild(_applyUpdate);
        box.AddChild(urow);
        box.AddChild(Note("更新するとゲームがいったん閉じて、自動で起動し直します。設定と視聴者の記録はそのまま残ります。"));

        box.AddChild(Caption("作った人"));
        box.AddChild(Note($"StreamRaid は {Author.Name}({Author.Handle})が作っています。気に入ったらチャンネルにも遊びに来てください。"));
        var channel = Button("YouTube チャンネルを開く");
        channel.CustomMinimumSize = new Vector2(320, 48);
        channel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        channel.Pressed += () => OS.ShellOpen(Author.ChannelUrl);
        box.AddChild(channel);

        box.AddChild(Caption("視聴者の記録"));
        box.AddChild(Note("仲間のレベルや MVP の回数は、接続先のチャンネルごとにこの PC に保存しています。"));
        var reset = Button("このチャンネルの記録を消す");
        reset.CustomMinimumSize = new Vector2(320, 48);
        reset.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        var confirm = new ConfirmationDialog
        {
            DialogText = "このチャンネルの視聴者のレベル・記録をすべて消します。よろしいですか?",
            Title = "記録を消す",
            OkButtonText = "消す",
            CancelButtonText = "やめる",
        };
        confirm.Confirmed += () => ResetViewers?.Invoke();
        AddChild(confirm);
        reset.Pressed += () => confirm.PopupCentered();
        box.AddChild(reset);
        return root;
    }

    /// <summary>アップデートの状態を表示する。</summary>
    public void SetUpdateStatus(string text, bool canApply)
    {
        _updateLabel.Text = $"いまのバージョン: v{Updater.CurrentVersion}    {text}";
        _applyUpdate.Disabled = !canApply;
    }

    // ---------------------------------------------------------------- 開け閉め・保存

    public void Show(bool show)
    {
        if (show) Load();
        _panel.Visible = show;
        if (!show) GetViewport().GuiReleaseFocus();
        Toggled?.Invoke(show);
        _paused.Visible = GetTree().Paused;
    }

    void Load()
    {
        _name.Text = _s.StreamerName;
        _avatarPath = _s.AvatarPath ?? "";
        UpdateAvatarLabel();
        _ytMode.Selected = _s.YouTubeMode == "url" ? 1 : 0;
        _ytChannel.Text = _s.YouTubeChannel;
        _ytUrl.Text = _s.YouTubeUrl;
        _twitch.Text = _s.TwitchChannel;
        UpdateMode();

        _floors.Value = _s.FloorCount;
        _lobby.Value = _s.LobbySeconds;
        _party.Value = _s.PartySize;
        _difficulty.Selected = Mathf.Clamp(_s.Difficulty, 0, 2);

        _raidInterval.Value = _s.RaidInterval;
        _allowSummon.ButtonPressed = _s.AllowSummon;
        _summonCd.Value = _s.SummonCooldown;
        _paidSpecial.ButtonPressed = _s.PaidSpecial;
        _membersOnly.ButtonPressed = _s.MembersOnlyJoin;
        _blocked.Text = string.Join("\n", _s.BlockedUsers ?? Array.Empty<string>());

        _testKeys.ButtonPressed = _s.ShowTestKeys;
    }

    void Apply()
    {
        _s.StreamerName = string.IsNullOrWhiteSpace(_name.Text) ? "配信者" : _name.Text.Trim();
        _s.AvatarPath = _avatarPath;
        _s.YouTubeMode = _ytMode.Selected == 1 ? "url" : "channel";
        _s.YouTubeChannel = _ytChannel.Text.Trim();
        _s.YouTubeUrl = _ytUrl.Text.Trim();
        _s.TwitchChannel = _twitch.Text.Trim();

        _s.FloorCount = (int)_floors.Value;
        _s.LobbySeconds = (float)_lobby.Value;
        _s.PartySize = (int)_party.Value;
        _s.Difficulty = _difficulty.Selected;

        _s.RaidInterval = (float)_raidInterval.Value;
        _s.AllowSummon = _allowSummon.ButtonPressed;
        _s.SummonCooldown = (float)_summonCd.Value;
        _s.PaidSpecial = _paidSpecial.ButtonPressed;
        _s.MembersOnlyJoin = _membersOnly.ButtonPressed;
        _s.BlockedUsers = _blocked.Text.Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0).Distinct().ToArray();

        _s.ShowTestKeys = _testKeys.ButtonPressed;
        _s.Save();
        Applied?.Invoke(_s);
        Show(false);
    }

    void UpdateAvatarLabel() =>
        _avatarLabel.Text = string.IsNullOrWhiteSpace(_avatarPath) ? "標準のキャラ" : System.IO.Path.GetFileName(_avatarPath);

    void UpdateMode()
    {
        _ytChannel.Visible = _ytMode.Selected == 0;
        _ytUrl.Visible = _ytMode.Selected == 1;
    }

    double _cycle = -1; // 開発用: --settings-cycle でタブを順に表示(画面確認用)

    public override void _Process(double delta)
    {
        if (_cycle < 0 && OS.GetCmdlineUserArgs().Contains("--settings-cycle")) { _cycle = 0; Show(true); }
        if (_cycle >= 0)
        {
            _cycle += delta;
            _tabs.CurrentTab = Mathf.Min(_tabs.GetTabCount() - 1, (int)(_cycle / 1.0));
        }
        if (!_panel.Visible) return;
        _ytStatus.Text = StatusText("youtube");
        _twStatus.Text = StatusText("twitch");
    }

    string StatusText(string p)
    {
        var state = _hub.State(p);
        if (state == "off") return "未接続";
        var mark = state switch { "connected" => "● つながっています", "connecting" => "… 接続中", _ => "× エラー" };
        return $"{mark}  {_hub.Detail(p)}";
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F10 })
        {
            Show(!_panel.Visible);
            GetViewport().SetInputAsHandled();
        }
        else if (_panel.Visible && e is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            Show(false);
            GetViewport().SetInputAsHandled();
        }
    }

    // ---------------------------------------------------------------- 部品

    static Theme MakeTheme()
    {
        var t = new Theme { DefaultFont = Toon.Font, DefaultFontSize = 22 };
        t.SetFontSize("font_size", "TabContainer", 24);
        return t;
    }

    /// <summary>タブ 1 枚(スクロールできる縦並び)。</summary>
    static VBoxContainer Page(string title, out Control root)
    {
        var scroll = new ScrollContainer { Name = title, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var margin = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        margin.AddThemeConstantOverride("margin_left", 8);
        margin.AddThemeConstantOverride("margin_right", 16);
        margin.AddThemeConstantOverride("margin_top", 12);
        scroll.AddChild(margin);
        var box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 10);
        margin.AddChild(box);
        root = scroll;
        return box;
    }

    /// <summary>「項目名 [入力]」+ 説明 の 1 項目。</summary>
    static Control Row(string label, Control input, string help)
    {
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 2);
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 16);
        h.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(300, 0), VerticalAlignment = VerticalAlignment.Center });
        input.CustomMinimumSize = new Vector2(Mathf.Max(input.CustomMinimumSize.X, 220), 44);
        h.AddChild(input);
        v.AddChild(h);
        if (help != null) v.AddChild(Note(help));
        return v;
    }

    static SpinBox Spin(double min, double max, double step, string suffix) =>
        new() { MinValue = min, MaxValue = max, Step = step, Suffix = suffix, CustomArrowStep = step, Alignment = HorizontalAlignment.Right };

    static CheckButton Check(string text) => new() { Text = text };

    static Label Title(string t) => new()
    {
        Text = t,
        LabelSettings = new LabelSettings { Font = Toon.Font, FontSize = 38, FontColor = new Color("8ff0ff") },
    };

    static Label Caption(string t) => new()
    {
        Text = t,
        LabelSettings = new LabelSettings { Font = Toon.Font, FontSize = 24, FontColor = new Color("ffe9a8") },
    };

    static Label Note(string t, float width = 900) => new()
    {
        Text = t,
        AutowrapMode = TextServer.AutowrapMode.WordSmart,
        CustomMinimumSize = new Vector2(width, 0),
        LabelSettings = new LabelSettings { Font = Toon.Font, FontSize = 19, FontColor = new Color("c9c3e6") },
    };

    static LineEdit Edit(string placeholder) => new() { PlaceholderText = placeholder, CustomMinimumSize = new Vector2(900, 44) };

    static Button Button(string t) => new() { Text = t, CustomMinimumSize = new Vector2(190, 48) };
}
