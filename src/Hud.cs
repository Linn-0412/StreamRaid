using System.Collections.Generic;
using Godot;

namespace StreamRaid;

/// <summary>画面の文字(状態・ログ・パーティー・ボスの HP・結果発表)。</summary>
public partial class Hud : CanvasLayer
{
    Label _top, _status, _hint, _banner, _party, _help, _bossName;
    VBoxContainer _log;
    ProgressBar _boss;
    PanelContainer _result;
    VBoxContainer _resultBox;
    Tween _bannerTween;

    static LabelSettings Ls(int size, Color color, int outline = 8) => new()
    {
        Font = Toon.Font,
        FontSize = size,
        FontColor = color,
        OutlineSize = outline,
        OutlineColor = new Color(0.05f, 0.03f, 0.08f, 0.95f),
    };

    public override void _Ready()
    {
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        _top = new Label { LabelSettings = Ls(34, Colors.White, 10), Position = new Vector2(28, 20) };
        root.AddChild(_top);

        _status = new Label { LabelSettings = Ls(22, new Color("bff6ff"), 6), Position = new Vector2(30, 66) };
        root.AddChild(_status);

        _hint = new Label { LabelSettings = Ls(30, new Color("ffe9a8"), 10), HorizontalAlignment = HorizontalAlignment.Center };
        _hint.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        _hint.OffsetLeft = -900; _hint.OffsetRight = 900; _hint.OffsetTop = -84; _hint.OffsetBottom = -36;
        root.AddChild(_hint);

        _banner = new Label { LabelSettings = Ls(84, new Color("fff3a0"), 18), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Modulate = Colors.Transparent };
        _banner.SetAnchorsPreset(Control.LayoutPreset.Center);
        _banner.OffsetLeft = -900; _banner.OffsetRight = 900; _banner.OffsetTop = -220; _banner.OffsetBottom = -100;
        root.AddChild(_banner);

        _log = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        _log.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        _log.OffsetLeft = 28; _log.OffsetRight = 900; _log.OffsetTop = -420; _log.OffsetBottom = -100;
        _log.AddThemeConstantOverride("separation", 2);
        root.AddChild(_log);

        _party = new Label { LabelSettings = Ls(26, Colors.White, 8), HorizontalAlignment = HorizontalAlignment.Right };
        _party.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _party.OffsetLeft = -620; _party.OffsetRight = -28; _party.OffsetTop = 90; _party.OffsetBottom = 600;
        root.AddChild(_party);

        _help = new Label
        {
            LabelSettings = Ls(20, new Color("c9c3e6"), 6),
            HorizontalAlignment = HorizontalAlignment.Right,
            Text = "テスト: F1 参加 / F2 召喚 / F3 コメント / F4 投げ銭 / F5 技 / F6 コメント 20 件   操作: WASD 移動・J 攻撃・Space 回避",
        };
        _help.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _help.OffsetLeft = -1400; _help.OffsetRight = -28; _help.OffsetTop = 24; _help.OffsetBottom = 60;
        root.AddChild(_help);

        _bossName = new Label { LabelSettings = Ls(32, new Color("ffd0d0"), 10), HorizontalAlignment = HorizontalAlignment.Center, Visible = false };
        _bossName.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _bossName.OffsetLeft = -500; _bossName.OffsetRight = 500; _bossName.OffsetTop = 70; _bossName.OffsetBottom = 110;
        root.AddChild(_bossName);

        _boss = new ProgressBar { ShowPercentage = false, Visible = false, MaxValue = 1, Step = 0 };
        _boss.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _boss.OffsetLeft = -500; _boss.OffsetRight = 500; _boss.OffsetTop = 116; _boss.OffsetBottom = 146;
        _boss.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = new Color("ff4d6d"), CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6 });
        _boss.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.6f), BorderColor = new Color("1b1424"), BorderWidthBottom = 3, BorderWidthTop = 3, BorderWidthLeft = 3, BorderWidthRight = 3, CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6 });
        root.AddChild(_boss);

        _result = new PanelContainer { Visible = false };
        _result.SetAnchorsPreset(Control.LayoutPreset.Center);
        _result.GrowHorizontal = Control.GrowDirection.Both;
        _result.GrowVertical = Control.GrowDirection.Both;
        _result.OffsetLeft = -520; _result.OffsetRight = 520; _result.OffsetTop = -330; _result.OffsetBottom = 330;
        _result.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.06f, 0.14f, 0.88f), BorderColor = new Color("ffd34d"),
            BorderWidthBottom = 4, BorderWidthTop = 4, BorderWidthLeft = 4, BorderWidthRight = 4,
            CornerRadiusTopLeft = 18, CornerRadiusTopRight = 18, CornerRadiusBottomLeft = 18, CornerRadiusBottomRight = 18,
            ContentMarginLeft = 40, ContentMarginRight = 40, ContentMarginTop = 28, ContentMarginBottom = 28,
        });
        _resultBox = new VBoxContainer();
        _resultBox.AddThemeConstantOverride("separation", 8);
        _result.AddChild(_resultBox);
        root.AddChild(_result);
    }

    public void SetTop(string text) => _top.Text = text;
    public void ShowTestKeys(bool show) => _help.Visible = show;
    public void SetStatus(string text) => _status.Text = text;
    public void SetHint(string text) => _hint.Text = text;

    public void Banner(string text, float seconds, Color? color = null)
    {
        _banner.Text = text;
        _banner.LabelSettings.FontColor = color ?? new Color("fff3a0");
        _bannerTween?.Kill();
        _banner.Modulate = Colors.White;
        _banner.Scale = Vector2.One;
        _banner.PivotOffset = _banner.Size / 2;
        _bannerTween = CreateTween();
        _bannerTween.TweenProperty(_banner, "scale", Vector2.One * 1.08f, 0.12);
        _bannerTween.TweenProperty(_banner, "scale", Vector2.One, 0.12);
        _bannerTween.TweenInterval(seconds);
        _bannerTween.TweenProperty(_banner, "modulate:a", 0f, 0.4);
    }

    public void Log(string text, Color? color = null)
    {
        var l = new Label { Text = text, LabelSettings = Ls(26, color ?? Colors.White, 8) };
        _log.AddChild(l);
        while (_log.GetChildCount() > 9) _log.GetChild(0).Free();
        var t = l.CreateTween();
        t.TweenInterval(7);
        t.TweenProperty(l, "modulate:a", 0f, 1);
        t.TweenCallback(Callable.From(l.QueueFree));
    }

    public void SetParty(string text) => _party.Text = text;

    /// <summary>エンディングのスタッフロール。参加してくれた人の名前を下から上へ流し、終わったら done。</summary>
    public void Credits(string title, System.Collections.Generic.IList<string> names, System.Action done)
    {
        var layer = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        layer.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.75f), MouseFilter = Control.MouseFilterEnum.Ignore };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(dim);
        var box = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Begin };
        box.AddThemeConstantOverride("separation", 14);
        box.Position = new Vector2(0, 1100);
        box.Size = new Vector2(1920, 0);
        box.AddChild(new Label { Text = title, HorizontalAlignment = HorizontalAlignment.Center, LabelSettings = Ls(54, new Color("ffd34d"), 12) });
        box.AddChild(new Label { Text = " " });
        foreach (var n in names)
            box.AddChild(new Label { Text = n, HorizontalAlignment = HorizontalAlignment.Center, LabelSettings = Ls(40, Colors.White, 10) });
        box.AddChild(new Label { Text = " " });
        box.AddChild(new Label { Text = "ありがとう。", HorizontalAlignment = HorizontalAlignment.Center, LabelSettings = Ls(48, new Color("8ff0ff"), 12) });
        layer.AddChild(box);
        AddChild(layer);
        float height = 220 + names.Count * 58f;
        float seconds = Mathf.Clamp(8 + names.Count * 0.6f, 10, 60);
        var t = CreateTween();
        t.TweenProperty(box, "position:y", -height, seconds);
        t.TweenProperty(layer, "modulate:a", 0f, 0.8);
        t.TweenCallback(Callable.From(() => { layer.QueueFree(); done?.Invoke(); }));
    }

    public void BossBar(Unit boss)
    {
        bool show = boss != null && IsInstanceValid(boss) && !boss.Dead;
        _boss.Visible = _bossName.Visible = show;
        if (!show) return;
        _boss.Value = boss.Hp / boss.MaxHp;
        _bossName.Text = $"{boss.DisplayName}   {Mathf.CeilToInt(boss.Hp)} / {Mathf.CeilToInt(boss.MaxHp)}";
    }

    public void ShowResult(string title, IEnumerable<(string text, Color color, int size)> lines)
    {
        foreach (var c in _resultBox.GetChildren()) c.QueueFree();
        _result.Visible = title != null;
        if (title == null) return;
        _resultBox.AddChild(new Label { Text = title, LabelSettings = Ls(64, new Color("ffd34d"), 14), HorizontalAlignment = HorizontalAlignment.Center });
        foreach (var (text, color, size) in lines)
            _resultBox.AddChild(new Label
            {
                Text = text,
                LabelSettings = Ls(size, color, 8),
                HorizontalAlignment = HorizontalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                CustomMinimumSize = new Vector2(960, 0),
            });
        _result.Scale = Vector2.One * 0.8f;
        _result.PivotOffset = _result.Size / 2;
        CreateTween().TweenProperty(_result, "scale", Vector2.One, 0.25).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }
}
