using System.Collections.Generic;
using System.Linq;
using Godot;

namespace StreamRaid;

/// <summary>
/// 5 階ごとの番人。上の階ほど技が増える。
///   Tier 1(〜20 階): 近接の叩きつけ・薙ぎ払い(予告の範囲表示あり)。15 階から手下
///   Tier 2(〜40 階): 遠距離の弾が加わる。予告が短くなる
///   Tier 3(〜60 階): 予告なしの一撃(番人が光るのが合図)。手下が増える
///   Tier 4(〜80 階): HP が半分を切ると第 2 形態(速くなり、弾が渦を巻く)
///   Tier 5(〜100 階): 全部入り。100 階はさらに強い
/// 会話が終わるまでは動かない(Active = false)。
/// </summary>
public partial class Boss : Unit
{
    public Story.Guardian Def;
    public int Floor;
    public bool Active;

    int _tier;
    float _t = 2.2f, _speed;
    int _step;
    bool _angry;
    StandardMaterial3D _eye;
    // 予兆用: 右手・左手・体の光と、頭上のセリフ
    MeshInstance3D _handR, _handL, _aura;
    StandardMaterial3D _handRMat, _handLMat, _auraMat;

    float _dmgScale, _teleScale;

    public void Setup(Story.Guardian def, int floor)
    {
        Def = def;
        Floor = floor;
    }

    public override void _Ready()
    {
        Team = Team.Monster;
        DisplayName = Def.Name;
        FlashStrength = 0.25f;
        _tier = Story.TierOf(Floor);
        _dmgScale = Main.I.FloorDamageScale(Floor) * Main.I.Cfg.EnemyScale * (Floor >= 100 ? 1.25f : 1f);
        _teleScale = _tier switch { 1 => 1f, 2 => 0.85f, 3 => 0.75f, 4 => 0.7f, _ => 0.62f };
        _speed = 1.5f + _tier * 0.2f;

        float s = Def.Size;
        var body = Def.Color;
        var v = new Node3D();
        v.AddChild(Toon.Mesh(new CapsuleMesh { Radius = 1.5f, Height = 4.2f }, Toon.Mat(body, true, 0.06f), new Vector3(0, 2.1f, 0)));
        v.AddChild(Toon.Mesh(new SphereMesh { Radius = 1.2f, Height = 2.2f }, Toon.Mat(body.Lightened(0.15f), true, 0.06f), new Vector3(0, 4.3f, -0.2f)));
        foreach (var sx in new[] { -1f, 1f })
        {
            var arm = Toon.Mesh(new CapsuleMesh { Radius = 0.45f, Height = 2.6f }, Toon.Mat(body.Darkened(0.15f), true, 0.05f), new Vector3(sx * 1.9f, 2.4f, -0.3f));
            arm.RotationDegrees = new Vector3(20, 0, sx * 18);
            v.AddChild(arm);
            // 上の階の番人ほど角が増える
            for (int h = 0; h < Mathf.Min(_tier, 3); h++)
            {
                var horn = Toon.Mesh(new CylinderMesh { TopRadius = 0, BottomRadius = 0.3f - h * 0.05f, Height = 1.2f - h * 0.2f },
                                     Toon.Mat(new Color("f2e6c9"), true, 0.04f), new Vector3(sx * (0.7f + h * 0.35f), 5.1f - h * 0.35f, -0.1f));
                horn.RotationDegrees = new Vector3(0, 0, -sx * (25 + h * 22));
                v.AddChild(horn);
            }
        }
        if (_tier >= 4) // 王冠のような輪
            v.AddChild(Toon.Mesh(new TorusMesh { InnerRadius = 0.9f, OuterRadius = 1.1f }, Toon.Glow(body.Lightened(0.5f), 2f), new Vector3(0, 5.6f, 0), false));
        // 手の光(番人の右手 = 体の +X 側。ゲームでは -Z が前なので右は +X)
        _handRMat = Toon.Glow(new Color(1f, 0.85f, 0.3f, 0f), 4f, true);
        _handLMat = Toon.Glow(new Color(0.4f, 0.8f, 1f, 0f), 4f, true);
        _handR = Toon.Mesh(new SphereMesh { Radius = 0.7f, Height = 1.4f }, _handRMat, new Vector3(2.35f, 1.3f, -0.8f), false);
        _handL = Toon.Mesh(new SphereMesh { Radius = 0.7f, Height = 1.4f }, _handLMat, new Vector3(-2.35f, 1.3f, -0.8f), false);
        _auraMat = Toon.Glow(new Color(1f, 0.6f, 0.15f, 0f), 2.5f, true); // オレンジ(目の赤と見分けやすく)
        _aura = Toon.Mesh(new SphereMesh { Radius = 2.6f, Height = 5.2f }, _auraMat, new Vector3(0, 2.6f, 0), false);
        v.AddChild(_handR);
        v.AddChild(_handL);
        v.AddChild(_aura);
        _eye = Toon.Glow(new Color("ffd34d"), 4f);
        foreach (var sx in new[] { -1f, 1f })
            v.AddChild(Toon.Mesh(new BoxMesh { Size = new Vector3(0.36f, 0.18f, 0.1f) }, _eye, new Vector3(sx * 0.45f, 4.45f, -1.25f), false));
        v.Scale = Vector3.One * s;
        Build(v, 5.4f * s, 1.6f * s, null, Colors.White, 0);

    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        if (Dead || !Active) return;
        var p = Main.I.Player;
        if (p != null && IsInstanceValid(p) && !p.Dead)
        {
            var to = Flat(p.GlobalPosition - GlobalPosition);
            Face(to, dt, 3f);
            Velocity = to.Length() > 6 ? to.Normalized() * (_angry ? _speed * 1.4f : _speed) : Vector3.Zero;
            MoveAndSlide();
        }
        _t -= dt;
        if (_t <= 0)
        {
            DoAttack();
            float gap = 3.6f - _tier * 0.25f;
            _t = _angry ? gap * 0.7f : gap;
        }
    }

    /// <summary>今の Tier で使える技を順番に回す。</summary>
    void DoAttack()
    {
        var moves = new List<System.Action> { Slam, Sweep };
        if (Floor >= 15) moves.Add(Summon);
        if (_tier >= 2) moves.Add(Volley);
        if (_tier >= 3) { moves.Add(SideSweep); moves.Add(Nova); moves.Add(HiddenStrike); }
        if (_tier >= 2) moves.Add(Rain);
        if (_angry && _tier >= 4) moves.Add(Spiral);
        moves[_step++ % moves.Count]();
    }

    float Tele(float t) => t * _teleScale * (_angry ? 0.85f : 1f);

    // ---- 技

    /// <summary>全員の足元に叩きつけ(予告あり)。</summary>
    void Slam()
    {
        foreach (var h in Heroes())
            Fx.Telegraph(h.GlobalPosition, 2.3f, Tele(1.5f), pos => HitArea(pos, 2.3f, 30));
    }

    /// <summary>自分のまわりを薙ぎ払う(離れれば避けられる)。</summary>
    void Sweep()
    {
        float r = 6f + _tier * 0.4f;
        Fx.Telegraph(GlobalPosition, r, Tele(1.9f), pos => { HitArea(pos, r, 42); Main.I.Shake(0.5f); Sfx.Play("impact", 0.8f); });
    }

    /// <summary>手下を呼ぶ。</summary>
    void Summon()
    {
        Main.I.Hud.Log($"{Def.Name}が手下を呼んだ!", new Color("ff9a9a"));
        int n = 2 + _tier / 2;
        for (int k = 0; k < n; k++)
            Main.I.SpawnMonster((Kind)(k % 3), null, GlobalPosition + new Vector3((float)GD.RandRange(-4, 4), 0, (float)GD.RandRange(2, 5)));
    }

    /// <summary>遠距離: 配信者と仲間へ弾を撃つ。撃った瞬間の位置に飛ぶので、動けば避けられる。</summary>
    void Volley()
    {
        Sfx.Play("tell_volley");
        var heroes = Heroes().OrderBy(_ => GD.Randi()).Take(2 + _tier).ToList();
        int i = 0;
        foreach (var h in heroes)
        {
            var at = h.GlobalPosition + Vector3.Up * 0.8f;
            float delay = i++ * 0.15f;
            var from = GlobalPosition + Vector3.Up * 4 * Def.Size;
            Main.I.GetTree().CreateTimer(delay, false).Timeout += () =>
            {
                if (!IsInstanceValid(this) || Dead) return;
                Fx.Shot(from, () => at, 0.9f, Def.Color.Lightened(0.4f), 0.35f, 1.2f, () => HitArea(at, 1.0f, 24));
            };
        }
    }

    // ---- 予告の範囲表示がない技(45 階〜)。代わりに「光る場所・セリフ・音」で見分けられる
    //   右手が光る + 「我が右手に宿れ…」 → 番人の右側を薙ぎ払う
    //   左手が光る + 「左の刃よ…」     → 番人の左側を薙ぎ払う
    //   体がオレンジに光る + 「大地よ、震えろ!」→ 番人のまわり全体に衝撃(離れる)
    //   左右の薙ぎ払いは、危ない側の床を最初に一瞬だけ光らせる(番人がこちらを向いていると左右が画面と逆になるため)
    //   目が赤く点滅 + 「――逃さぬ」 + 頭上の「!」→ 配信者の足元に一撃(動く)

    /// <summary>予兆のセリフ。番人の頭上は画面の外に出やすいので、HP バーの下に出す。</summary>
    void Say(string text, Color color, float seconds) => Main.I.Hud.BossLine(Def.Name, text, color, seconds);

    /// <summary>光らせる(time 秒かけて強く光り、撃った瞬間に消える)。</summary>
    void Charge(StandardMaterial3D mat, MeshInstance3D mesh, float time)
    {
        var c = mat.AlbedoColor;
        mat.AlbedoColor = c with { A = 0.15f };
        mesh.Scale = Vector3.One * 0.6f;
        var t = mesh.CreateTween().SetParallel();
        t.TweenProperty(mat, "albedo_color:a", 0.9f, time);
        t.TweenProperty(mesh, "scale", Vector3.One * 1.3f, time).SetTrans(Tween.TransitionType.Sine);
        t.Chain().TweenCallback(Callable.From(() => mat.AlbedoColor = c with { A = 0f }));
    }

    void SideSweep()
    {
        bool right = GD.Randf() < 0.5f;
        float time = Tele(1.25f);
        // 向きは予兆を始めたときで固定(途中で振り向いて当たり方が変わらないように)
        var fwd = Forward.Normalized();
        var side = fwd.Cross(Vector3.Up).Normalized() * (right ? 1 : -1);
        if (right) { Charge(_handRMat, _handR, time); Say("我が右手に宿れ…", new Color("ffd34d"), time); Sfx.Play("tell_right"); }
        else { Charge(_handLMat, _handL, time); Say("左の刃よ…", new Color("8fd6ff"), time); Sfx.Play("tell_left"); }
        float r = 7.5f * Def.Size;
        Fx.SideFlash(GlobalPosition, side, r, right ? new Color(1f, 0.8f, 0.3f) : new Color(0.5f, 0.85f, 1f), 0.45f);
        Main.I.GetTree().CreateTimer(time, false).Timeout += () =>
        {
            if (!IsInstanceValid(this) || Dead) return;
            var center = GlobalPosition + side * r * 0.5f;
            Fx.Burst(center + Vector3.Up * 0.6f, right ? new Color(1f, 0.8f, 0.3f) : new Color(0.5f, 0.85f, 1f), r * 0.55f, 0.3f);
            Sfx.Play("impact");
            Main.I.Shake(0.4f);
            foreach (var u in Main.I.Units.ToArray())
            {
                if (u.Team != Team.Hero || u.Dead) continue;
                var to = Flat(u.GlobalPosition - GlobalPosition);
                if (to.Length() - u.Radius < r && to.Normalized().Dot(side) > 0.15f) u.Damage(40 * _dmgScale, null);
            }
        };
    }

    void Nova()
    {
        float time = Tele(1.4f);
        Charge(_auraMat, _aura, time);
        Say("大地よ、震えろ!", new Color("ffa64d"), time);
        Sfx.Play("tell_nova");
        float r = 6.5f * Def.Size;
        Main.I.GetTree().CreateTimer(time, false).Timeout += () =>
        {
            if (!IsInstanceValid(this) || Dead) return;
            Fx.Burst(GlobalPosition + Vector3.Up * 0.5f, new Color(1f, 0.6f, 0.2f), r, 0.4f);
            Sfx.Play("impact", 0.7f);
            HitArea(GlobalPosition, r, 44);
            Main.I.Shake(0.6f);
        };
    }

    /// <summary>配信者の足元に一撃: 目が赤く点滅し、頭上に「!」。</summary>
    void HiddenStrike()
    {
        var p = Main.I.Player;
        if (p == null || p.Dead) return;
        float time = Tele(0.9f);
        Say("――逃さぬ", new Color("ff5c5c"), time);
        Sfx.Play("tell_target");
        var eye = _eye.AlbedoColor;
        _eye.AlbedoColor = _eye.Emission = new Color("ff2020");
        Fx.Popup(p.GlobalPosition + Vector3.Up * 2.6f, "!", new Color("ff5c5c"), 64);
        Main.I.GetTree().CreateTimer(time, false).Timeout += () =>
        {
            if (!IsInstanceValid(this) || Dead) return;
            if (!_angry) _eye.AlbedoColor = _eye.Emission = eye;
            if (!IsInstanceValid(p)) return;
            var at = p.GlobalPosition;
            Fx.Burst(at + Vector3.Up * 0.5f, new Color(1f, 0.4f, 0.4f), 2.6f, 0.3f);
            Sfx.Play("impact", 1.3f);
            HitArea(at, 2.4f, 36);
        };
    }

    /// <summary>配信者のまわりに連続で落とす(予告あり)。</summary>
    void Rain()
    {
        var p = Main.I.Player;
        if (p == null || p.Dead) return;
        int n = 3 + _tier * 2;
        for (int k = 0; k < n; k++)
        {
            var at = p.GlobalPosition;
            for (int tries = 0; tries < 12; tries++)
            {
                at = p.GlobalPosition + new Vector3((float)GD.RandRange(-5, 5), 0, (float)GD.RandRange(-5, 5));
                if (!Main.I.InStreamerCorner(at)) break;
            }
            if (Main.I.InStreamerCorner(at)) continue;
            Fx.Telegraph(at, 1.9f, Tele(1.0f + k * 0.15f), pos => HitArea(pos, 1.9f, 26));
        }
    }

    /// <summary>第 2 形態: 自分のまわりへ渦を巻くように弾をばらまく。</summary>
    void Spiral()
    {
        int n = 10 + _tier * 2;
        for (int k = 0; k < n; k++)
        {
            float a = k * Mathf.Tau / n;
            var at = GlobalPosition + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (5 + (k % 3) * 2.5f) + Vector3.Up * 0.6f;
            var from = GlobalPosition + Vector3.Up * 3;
            float delay = k * 0.05f;
            Main.I.GetTree().CreateTimer(delay, false).Timeout += () =>
            {
                if (!IsInstanceValid(this) || Dead) return;
                Fx.Shot(from, () => at, 0.8f, new Color("ff6b9d"), 0.3f, 1.5f, () => HitArea(at, 1.2f, 22));
            };
        }
    }

    List<Unit> Heroes() => Main.I.Units.Where(u => u.Team == Team.Hero && !u.Dead).ToList();

    void HitArea(Vector3 pos, float r, float dmg)
    {
        if (!IsInstanceValid(this) || Dead) return;
        dmg *= _dmgScale;
        foreach (var u in Main.I.Units.ToArray())
            if (u.Team == Team.Hero && !u.Dead && Flat(u.GlobalPosition - pos).Length() - u.Radius < r)
                u.Damage(dmg, null);
    }

    protected override void OnDamaged(float amount)
    {
        if (!_angry && Hp < MaxHp * 0.5f && _tier >= 2)
        {
            _angry = true;
            _eye.AlbedoColor = _eye.Emission = new Color("ff3b3b");
            Main.I.Hud.Banner(_tier >= 4 ? $"{Def.Name} 第 2 形態!" : $"{Def.Name}が怒った!", 2f, new Color("ff6b6b"));
            Main.I.Shake(0.6f);
        }
    }

    /// <summary>レイドに新しい人が加わるたびに少し硬くする(人数が多くても長さが変わらないように)。</summary>
    public void AddParticipant(float hp)
    {
        MaxHp += hp;
        Hp += hp;
    }
}
