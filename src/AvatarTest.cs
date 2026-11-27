using Godot;

namespace StreamRaid;

/// <summary>
/// 開発用: アバターを近くから映して、立ち・歩き・攻撃を確認する。
/// 例: godot --path . -- --avatartest --avatar=C:/path/model.vrm
/// </summary>
public partial class AvatarTest : Node3D
{
    VrmAvatar _a;
    double _t;
    Label _label;

    public override void _Ready()
    {
        string path = null;
        foreach (var a in OS.GetCmdlineUserArgs())
            if (a.StartsWith("--avatar=")) path = a["--avatar=".Length..];
        Toon.Init();
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color("2a2440"),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color("b0aad8"),
                AmbientLightEnergy = 0.7f,
            },
        });
        var sun = new DirectionalLight3D { ShadowEnabled = true };
        AddChild(sun);
        sun.RotationDegrees = new Vector3(-45, -30, 0);
        AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(10, 10) }, MaterialOverride = Toon.Mat(new Color("3d6b4f"), false) });

        var model = AvatarLoader.Load(path, out var err);
        if (model == null) { GD.PrintErr("avatar: " + err); return; }
        _a = VrmAvatar.Create(model, 1.75f);
        AddChild(_a);
        // ゲームと同じく -Z が前。カメラは斜め前から
        var cam = new Camera3D { Fov = 40, Current = true };
        AddChild(cam);
        cam.Position = new Vector3(2.2f, 1.6f, -3.6f);
        cam.LookAt(new Vector3(0, 0.95f, 0), Vector3.Up);

        _label = new Label { Position = new Vector2(20, 20), LabelSettings = new LabelSettings { Font = Toon.Font, FontSize = 36 } };
        var layer = new CanvasLayer();
        layer.AddChild(_label);
        AddChild(layer);
    }

    public override void _Process(double delta)
    {
        if (_a == null) return;
        _t += delta;
        // 0〜3 秒: 立ち / 3〜7 秒: 歩き / 7 秒〜: 立ったまま攻撃を繰り返す
        if (_t < 3) { _a.Move = 0; _label.Text = "立ち"; }
        else if (_t < 7) { _a.Move = 1; _label.Text = "歩き"; }
        else
        {
            _a.Move = 0;
            _label.Text = "攻撃";
            if ((int)(_t * 2.5) != (int)((_t - delta) * 2.5)) _a.Attack();
        }
    }
}
