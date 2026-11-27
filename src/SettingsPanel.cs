using System;
using Godot;

namespace StreamRaid;

/// <summary>接続設定(F10)。配信者の名前・YouTube・Twitch。</summary>
public partial class SettingsPanel : CanvasLayer
{
    public event Action<Settings> Applied;
    /// <summary>開いた(true)・閉じた(false)。</summary>
    public event Action<bool> Toggled;

    Settings _s;
    ChatHub _hub;
    PanelContainer _panel;
    LineEdit _name, _ytChannel, _ytUrl, _twitch;
    OptionButton _ytMode;
    Label _ytStatus, _twStatus, _paused, _avatarLabel;
    string _avatarPath = "";
    FileDialog _file;

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
            LabelSettings = new LabelSettings { Font = Toon.Font, FontSize = 56, FontColor = new Color("fff3a0"), OutlineSize = 14, OutlineColor = new Color(0.05f, 0.03f, 0.08f) },
        };
        _paused.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _paused.OffsetLeft = -400; _paused.OffsetRight = 400; _paused.OffsetTop = 150; _paused.OffsetBottom = 230;
        AddChild(_paused);
        _panel = new PanelContainer { Visible = false };
        _panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        _panel.GrowHorizontal = Control.GrowDirection.Both;
        _panel.GrowVertical = Control.GrowDirection.Both;
        _panel.CustomMinimumSize = new Vector2(900, 0);
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.06f, 0.14f, 0.96f), BorderColor = new Color("8ff0ff"),
            BorderWidthBottom = 3, BorderWidthTop = 3, BorderWidthLeft = 3, BorderWidthRight = 3,
            CornerRadiusTopLeft = 14, CornerRadiusTopRight = 14, CornerRadiusBottomLeft = 14, CornerRadiusBottomRight = 14,
            ContentMarginLeft = 36, ContentMarginRight = 36, ContentMarginTop = 26, ContentMarginBottom = 26,
        });
        AddChild(_panel);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        _panel.AddChild(box);

        box.AddChild(Title("接続設定"));
        box.AddChild(Note("コメントを読むだけなので、ログインは要りません。設定はこの PC の中にだけ保存します。"));

        box.AddChild(Caption("配信者の名前(ゲーム内の表示)"));
        _name = Edit("配信者");
        box.AddChild(_name);

        box.AddChild(Caption("アバター(VRM)"));
        var arow = new HBoxContainer();
        arow.AddThemeConstantOverride("separation", 12);
        _avatarLabel = new Label
        {
            CustomMinimumSize = new Vector2(440, 0),
            ClipText = true,
            VerticalAlignment = VerticalAlignment.Center,
            LabelSettings = new LabelSettings { Font = Toon.Font, FontSize = 22, FontColor = Colors.White },
        };
        var pick = Button("VRM を選ぶ…");
        pick.Pressed += () => _file.PopupCentered(new Vector2I(1100, 700));
        var clear = Button("標準に戻す");
        clear.Pressed += () => { _avatarPath = ""; UpdateAvatarLabel(); };
        arow.AddChild(_avatarLabel);
        arow.AddChild(pick);
        arow.AddChild(clear);
        box.AddChild(arow);
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

        box.AddChild(Caption("YouTube"));
        _ytMode = new OptionButton();
        _ytMode.AddItem("チャンネル(@ハンドル)から配信中の枠を探す", 0);
        _ytMode.AddItem("配信の URL を指定する(限定公開の配信はこちら)", 1);
        _ytMode.AddThemeFontSizeOverride("font_size", 24);
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

        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        row.AddThemeConstantOverride("separation", 12);
        var connect = Button("保存して接続");
        connect.Pressed += Apply;
        var close = Button("閉じる");
        close.Pressed += () => Show(false);
        row.AddChild(connect);
        row.AddChild(close);
        box.AddChild(row);
    }

    public void Show(bool show)
    {
        if (show)
        {
            _name.Text = _s.StreamerName;
            _ytMode.Selected = _s.YouTubeMode == "url" ? 1 : 0;
            _ytChannel.Text = _s.YouTubeChannel;
            _ytUrl.Text = _s.YouTubeUrl;
            _twitch.Text = _s.TwitchChannel;
            _avatarPath = _s.AvatarPath ?? "";
            UpdateAvatarLabel();
            UpdateMode();
        }
        _panel.Visible = show;
        if (show) _name.GrabFocus();
        else GetViewport().GuiReleaseFocus();
        Toggled?.Invoke(show);
        _paused.Visible = GetTree().Paused;
    }

    void UpdateAvatarLabel() =>
        _avatarLabel.Text = string.IsNullOrWhiteSpace(_avatarPath) ? "標準のキャラ" : System.IO.Path.GetFileName(_avatarPath);

    void UpdateMode()
    {
        _ytChannel.Visible = _ytMode.Selected == 0;
        _ytUrl.Visible = _ytMode.Selected == 1;
    }

    void Apply()
    {
        _s.StreamerName = string.IsNullOrWhiteSpace(_name.Text) ? "配信者" : _name.Text.Trim();
        _s.YouTubeMode = _ytMode.Selected == 1 ? "url" : "channel";
        _s.YouTubeChannel = _ytChannel.Text.Trim();
        _s.YouTubeUrl = _ytUrl.Text.Trim();
        _s.TwitchChannel = _twitch.Text.Trim();
        _s.AvatarPath = _avatarPath;
        _s.Save();
        Applied?.Invoke(_s);
        Show(false);
    }

    public override void _Process(double delta)
    {
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

    static Label Title(string t) => new()
    {
        Text = t,
        LabelSettings = new LabelSettings { Font = Toon.Font, FontSize = 40, FontColor = new Color("8ff0ff") },
    };

    static Label Caption(string t) => new()
    {
        Text = t,
        LabelSettings = new LabelSettings { Font = Toon.Font, FontSize = 26, FontColor = new Color("ffe9a8") },
    };

    static Label Note(string t) => new()
    {
        Text = t,
        AutowrapMode = TextServer.AutowrapMode.WordSmart,
        CustomMinimumSize = new Vector2(820, 0),
        LabelSettings = new LabelSettings { Font = Toon.Font, FontSize = 20, FontColor = new Color("c9c3e6") },
    };

    static LineEdit Edit(string placeholder)
    {
        var e = new LineEdit { PlaceholderText = placeholder, CustomMinimumSize = new Vector2(820, 46) };
        e.AddThemeFontSizeOverride("font_size", 24);
        return e;
    }

    static Button Button(string t)
    {
        var b = new Button { Text = t, CustomMinimumSize = new Vector2(200, 50) };
        b.AddThemeFontSizeOverride("font_size", 24);
        return b;
    }
}
