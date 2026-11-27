using System;
using System.Collections.Generic;
using Godot;

namespace StreamRaid;

/// <summary>会話ウィンドウ。文字が流れて、Enter / クリック / A ボタンで次へ。表示中はゲームを止める。</summary>
public partial class DialogueBox : CanvasLayer
{
    public bool Open => _panel.Visible;
    /// <summary>デモ録画用: 自動で読み進める(1 行あたりの秒数。0 なら手動)。</summary>
    public float AutoAdvance;

    PanelContainer _panel;
    Label _speaker, _text, _next;
    readonly Queue<Story.Line> _lines = new();
    Action _done;
    string _name = "";
    float _shown, _wait;

    public override void _Ready()
    {
        Layer = 8;
        ProcessMode = ProcessModeEnum.Always;
        _panel = new PanelContainer { Visible = false };
        _panel.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        _panel.GrowHorizontal = Control.GrowDirection.Both;
        _panel.GrowVertical = Control.GrowDirection.Begin;
        // 左下・右下(配信者の立ち絵の場所)にかからない幅にする
        _panel.OffsetLeft = -560; _panel.OffsetRight = 560; _panel.OffsetTop = -330; _panel.OffsetBottom = -40;
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.04f, 0.11f, 0.92f), BorderColor = new Color("ffe9a8"),
            BorderWidthBottom = 3, BorderWidthTop = 3, BorderWidthLeft = 3, BorderWidthRight = 3,
            CornerRadiusTopLeft = 16, CornerRadiusTopRight = 16, CornerRadiusBottomLeft = 16, CornerRadiusBottomRight = 16,
            ContentMarginLeft = 40, ContentMarginRight = 40, ContentMarginTop = 22, ContentMarginBottom = 22,
        });
        AddChild(_panel);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        _panel.AddChild(box);
        _speaker = new Label { LabelSettings = new LabelSettings { Font = Toon.Font, FontSize = 34, FontColor = new Color("ffd34d"), OutlineSize = 8, OutlineColor = new Color(0.05f, 0.03f, 0.08f) } };
        box.AddChild(_speaker);
        _text = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(1040, 160),
            LabelSettings = new LabelSettings { Font = Toon.Font, FontSize = 36, FontColor = Colors.White, OutlineSize = 8, OutlineColor = new Color(0.05f, 0.03f, 0.08f), LineSpacing = 6 },
        };
        box.AddChild(_text);
        _next = new Label { Text = "▼ Enter", HorizontalAlignment = HorizontalAlignment.Right, LabelSettings = new LabelSettings { Font = Toon.Font, FontSize = 22, FontColor = new Color("c9c3e6") } };
        box.AddChild(_next);
    }

    /// <summary>セリフを順に表示し、最後まで読んだら done を呼ぶ。</summary>
    public void Play(IEnumerable<Story.Line> lines, string streamerName, Action done)
    {
        _lines.Clear();
        foreach (var l in lines) _lines.Enqueue(l);
        _name = streamerName;
        _done = done;
        _panel.Visible = true;
        Main.I.UpdatePause();
        NextLine();
    }

    void NextLine()
    {
        if (_lines.Count == 0)
        {
            _panel.Visible = false;
            Main.I.UpdatePause();
            var d = _done;
            _done = null;
            d?.Invoke();
            return;
        }
        var l = _lines.Dequeue();
        _speaker.Text = Story.Fill(l.Speaker, _name);
        _speaker.LabelSettings.FontColor = l.Speaker == Story.Guide ? new Color("8ff0ff") : new Color("ffd34d");
        _text.Text = Story.Fill(l.Text, _name);
        _text.VisibleRatio = 0;
        _shown = 0;
        _wait = 0;
    }

    public override void _Process(double delta)
    {
        if (!_panel.Visible) return;
        float dt = (float)delta;
        int total = Mathf.Max(1, _text.Text.Length);
        _shown = Mathf.Min(total, _shown + dt * 40f); // 1 秒に 40 文字
        _text.VisibleRatio = _shown / total;
        _next.Visible = _shown >= total && Mathf.PosMod(Time.GetTicksMsec() / 400, 2) == 0;
        if (AutoAdvance > 0 && _shown >= total)
        {
            _wait += dt;
            if (_wait >= AutoAdvance) NextLine();
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!_panel.Visible) return;
        bool press = e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Enter or Key.KpEnter }
                     || e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }
                     || e is InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.A };
        if (!press) return;
        GetViewport().SetInputAsHandled();
        int total = Mathf.Max(1, _text.Text.Length);
        if (_shown < total) _shown = total; // 流れている途中なら、まず全部出す
        else NextLine();
    }
}
