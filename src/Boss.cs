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
        if (_tier >= 3) moves.Add(HiddenStrike);
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
        Fx.Telegraph(GlobalPosition, r, Tele(1.9f), pos => { HitArea(pos, r, 42); Main.I.Shake(0.5f); });
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

    /// <summary>予告なしの一撃: 番人が光ったら、少しして配信者のまわりに衝撃(範囲表示なし)。</summary>
    void HiddenStrike()
    {
        var p = Main.I.Player;
        if (p == null || p.Dead) return;
        Fx.Burst(GlobalPosition + Vector3.Up * 3 * Def.Size, new Color("ffffff"), 3f * Def.Size, 0.5f);
        Fx.Popup(p.GlobalPosition + Vector3.Up * 2.6f, "!", new Color("ff5c5c"), 64);
        Main.I.GetTree().CreateTimer(Tele(0.8f), false).Timeout += () =>
        {
            if (!IsInstanceValid(this) || Dead || !IsInstanceValid(p)) return;
            var at = p.GlobalPosition;
            Fx.Burst(at + Vector3.Up * 0.5f, new Color(1f, 0.4f, 0.4f), 2.6f, 0.3f);
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
            var at = p.GlobalPosition + new Vector3((float)GD.RandRange(-5, 5), 0, (float)GD.RandRange(-5, 5));
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
