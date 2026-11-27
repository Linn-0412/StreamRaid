using System.Collections.Generic;
using Godot;

namespace StreamRaid;

/// <summary>
/// キー・マウス・ゲームパッドの割り当て。設定の「操作」タブで変えられる。
/// 割り当ては文字列で保存する: キーボード "K:キーコード" / マウス "M:ボタン" / パッドのボタン "B:番号" / パッドの軸 "A:軸:+1 か -1"。
/// 矢印キー・左スティック(移動)と、左クリック(攻撃)・右クリック(回避)はいつでも使える(設定を間違えても操作できなくならないように)。
/// </summary>
public static class Inputs
{
    public record Action(string Id, string Name, string Key, string Pad);

    /// <summary>変えられる操作と、最初の割り当て。</summary>
    public static readonly Action[] Actions =
    {
        new("move_forward", "上へ移動", "K:" + (int)Godot.Key.W, "B:" + (int)JoyButton.DpadUp),
        new("move_back", "下へ移動", "K:" + (int)Godot.Key.S, "B:" + (int)JoyButton.DpadDown),
        new("move_left", "左へ移動", "K:" + (int)Godot.Key.A, "B:" + (int)JoyButton.DpadLeft),
        new("move_right", "右へ移動", "K:" + (int)Godot.Key.D, "B:" + (int)JoyButton.DpadRight),
        new("attack", "攻撃", "K:" + (int)Godot.Key.J, "B:" + (int)JoyButton.X),
        new("dodge", "回避", "K:" + (int)Godot.Key.Space, "B:" + (int)JoyButton.A),
        new("start", "出発・次へ・会話を進める", "K:" + (int)Godot.Key.Enter, "B:" + (int)JoyButton.Start),
    };

    /// <summary>設定にある割り当て(無ければ最初の割り当て)で InputMap を作り直す。</summary>
    public static void Apply(Settings s)
    {
        foreach (var a in Actions)
        {
            if (!InputMap.HasAction(a.Id)) InputMap.AddAction(a.Id, 0.3f);
            InputMap.ActionEraseEvents(a.Id);
            foreach (var code in new[] { KeyOf(s, a), PadOf(s, a) })
            {
                var e = ToEvent(code);
                if (e != null) InputMap.ActionAddEvent(a.Id, e);
            }
            foreach (var e in Fixed(a.Id)) InputMap.ActionAddEvent(a.Id, e);
        }
    }

    public static string KeyOf(Settings s, Action a) => s.KeyBindings != null && s.KeyBindings.TryGetValue(a.Id, out var v) ? v : a.Key;
    public static string PadOf(Settings s, Action a) => s.PadBindings != null && s.PadBindings.TryGetValue(a.Id, out var v) ? v : a.Pad;

    /// <summary>いつでも使える割り当て(変えられない)。</summary>
    static IEnumerable<InputEvent> Fixed(string id)
    {
        switch (id)
        {
            case "move_forward":
                yield return new InputEventKey { PhysicalKeycode = Godot.Key.Up };
                yield return new InputEventJoypadMotion { Axis = JoyAxis.LeftY, AxisValue = -1 };
                break;
            case "move_back":
                yield return new InputEventKey { PhysicalKeycode = Godot.Key.Down };
                yield return new InputEventJoypadMotion { Axis = JoyAxis.LeftY, AxisValue = 1 };
                break;
            case "move_left":
                yield return new InputEventKey { PhysicalKeycode = Godot.Key.Left };
                yield return new InputEventJoypadMotion { Axis = JoyAxis.LeftX, AxisValue = -1 };
                break;
            case "move_right":
                yield return new InputEventKey { PhysicalKeycode = Godot.Key.Right };
                yield return new InputEventJoypadMotion { Axis = JoyAxis.LeftX, AxisValue = 1 };
                break;
            case "attack":
                yield return new InputEventMouseButton { ButtonIndex = MouseButton.Left };
                break;
            case "dodge":
                yield return new InputEventMouseButton { ButtonIndex = MouseButton.Right };
                break;
        }
    }

    public static InputEvent ToEvent(string code)
    {
        if (string.IsNullOrEmpty(code)) return null;
        var p = code.Split(':');
        if (p.Length < 2 || !int.TryParse(p[1], out int n)) return null;
        return p[0] switch
        {
            "K" => new InputEventKey { PhysicalKeycode = (Godot.Key)n },
            "M" => new InputEventMouseButton { ButtonIndex = (MouseButton)n },
            "B" => new InputEventJoypadButton { ButtonIndex = (JoyButton)n },
            "A" when p.Length >= 3 && float.TryParse(p[2], out var v) => new InputEventJoypadMotion { Axis = (JoyAxis)n, AxisValue = v },
            _ => null,
        };
    }

    /// <summary>押された入力を保存用の文字列にする(割り当て変更の画面で使う)。pad = true ならゲームパッドだけ受け付ける。</summary>
    public static string FromEvent(InputEvent e, bool pad)
    {
        if (!pad)
        {
            if (e is InputEventKey { Pressed: true, Echo: false } k)
                return "K:" + (int)(k.PhysicalKeycode != Godot.Key.None ? k.PhysicalKeycode : k.Keycode);
            if (e is InputEventMouseButton { Pressed: true } m && m.ButtonIndex is MouseButton.Left or MouseButton.Right or MouseButton.Middle or MouseButton.Xbutton1 or MouseButton.Xbutton2)
                return "M:" + (int)m.ButtonIndex;
            return null;
        }
        if (e is InputEventJoypadButton { Pressed: true } b) return "B:" + (int)b.ButtonIndex;
        if (e is InputEventJoypadMotion j && Mathf.Abs(j.AxisValue) > 0.6f) return $"A:{(int)j.Axis}:{(j.AxisValue > 0 ? 1 : -1)}";
        return null;
    }

    /// <summary>画面に出す名前(「J」「左クリック」「A ボタン」など)。Xbox 配置の名前で表示する。</summary>
    public static string Name(string code)
    {
        if (string.IsNullOrEmpty(code)) return "なし";
        var p = code.Split(':');
        if (p.Length < 2 || !int.TryParse(p[1], out int n)) return "なし";
        switch (p[0])
        {
            case "K":
                var key = (Godot.Key)n;
                return key switch
                {
                    Godot.Key.Space => "Space",
                    Godot.Key.Enter => "Enter",
                    _ => OS.GetKeycodeString(key),
                };
            case "M":
                return (MouseButton)n switch
                {
                    MouseButton.Left => "左クリック",
                    MouseButton.Right => "右クリック",
                    MouseButton.Middle => "中クリック",
                    MouseButton.Xbutton1 => "マウス戻る",
                    MouseButton.Xbutton2 => "マウス進む",
                    _ => "マウス" + n,
                };
            case "B":
                return (JoyButton)n switch
                {
                    JoyButton.A => "A ボタン",
                    JoyButton.B => "B ボタン",
                    JoyButton.X => "X ボタン",
                    JoyButton.Y => "Y ボタン",
                    JoyButton.LeftShoulder => "LB",
                    JoyButton.RightShoulder => "RB",
                    JoyButton.Back => "Back",
                    JoyButton.Start => "Start",
                    JoyButton.LeftStick => "左スティック押し込み",
                    JoyButton.RightStick => "右スティック押し込み",
                    JoyButton.DpadUp => "十字キー↑",
                    JoyButton.DpadDown => "十字キー↓",
                    JoyButton.DpadLeft => "十字キー←",
                    JoyButton.DpadRight => "十字キー→",
                    _ => "ボタン" + n,
                };
            case "A":
                string dir = p.Length >= 3 && p[2].StartsWith("-") ? "-" : "+";
                return (JoyAxis)n switch
                {
                    JoyAxis.TriggerLeft => "LT",
                    JoyAxis.TriggerRight => "RT",
                    JoyAxis.LeftX => dir == "+" ? "左スティック→" : "左スティック←",
                    JoyAxis.LeftY => dir == "+" ? "左スティック↓" : "左スティック↑",
                    JoyAxis.RightX => dir == "+" ? "右スティック→" : "右スティック←",
                    JoyAxis.RightY => dir == "+" ? "右スティック↓" : "右スティック↑",
                    _ => $"軸{n}{dir}",
                };
        }
        return "なし";
    }

    /// <summary>今の割り当てのキーボード側の名前(画面の案内用)。</summary>
    public static string KeyName(Settings s, string id)
    {
        foreach (var a in Actions) if (a.Id == id) return Name(KeyOf(s, a));
        return "";
    }
}
