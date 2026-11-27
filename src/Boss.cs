using System.Linq;
using Godot;

namespace StreamRaid;

/// <summary>最下層のボス。地面に予告を出す大技を順番に使い、HP が半分を切ると怒って速くなる。</summary>
public partial class Boss : Unit
{
    public const string BossName = "深淵の番人";
    float _t = 2.5f;
    int _step;
    bool _angry;
    StandardMaterial3D _eye;

    public override void _Ready()
    {
        Team = Team.Monster;
        DisplayName = BossName;
        FlashStrength = 0.25f;
        var v = new Node3D();
        var body = new Color("4b3a78");
        v.AddChild(Toon.Mesh(new CapsuleMesh { Radius = 1.5f, Height = 4.2f }, Toon.Mat(body, true, 0.06f), new Vector3(0, 2.1f, 0)));
        v.AddChild(Toon.Mesh(new SphereMesh { Radius = 1.2f, Height = 2.2f }, Toon.Mat(body.Lightened(0.15f), true, 0.06f), new Vector3(0, 4.3f, -0.2f)));
        foreach (var sx in new[] { -1f, 1f })
        {
            var horn = Toon.Mesh(new CylinderMesh { TopRadius = 0, BottomRadius = 0.32f, Height = 1.3f }, Toon.Mat(new Color("f2e6c9"), true, 0.04f), new Vector3(sx * 0.8f, 5.2f, -0.1f));
            horn.RotationDegrees = new Vector3(0, 0, -sx * 28);
            v.AddChild(horn);
            var arm = Toon.Mesh(new CapsuleMesh { Radius = 0.45f, Height = 2.6f }, Toon.Mat(body.Darkened(0.15f), true, 0.05f), new Vector3(sx * 1.9f, 2.4f, -0.3f));
            arm.RotationDegrees = new Vector3(20, 0, sx * 18);
            v.AddChild(arm);
        }
        _eye = Toon.Glow(new Color("ffd34d"), 4f);
        foreach (var sx in new[] { -1f, 1f })
            v.AddChild(Toon.Mesh(new BoxMesh { Size = new Vector3(0.36f, 0.18f, 0.1f) }, _eye, new Vector3(sx * 0.45f, 4.45f, -1.25f), false));
        Build(v, 5.4f, 1.6f, null, Colors.White, 0);
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        if (Dead) return;
        var p = Main.I.Player;
        if (p != null && IsInstanceValid(p) && !p.Dead)
        {
            var to = Flat(p.GlobalPosition - GlobalPosition);
            Face(to, dt, 3f);
            Velocity = to.Length() > 6 ? to.Normalized() * (_angry ? 2.4f : 1.6f) : Vector3.Zero;
            MoveAndSlide();
        }
        _t -= dt;
        if (_t <= 0)
        {
            DoAttack(_step++ % 4);
            _t = _angry ? 2.4f : 3.4f;
        }
    }

    void DoAttack(int i)
    {
        var game = Main.I;
        var heroes = game.Units.Where(u => u.Team == Team.Hero && !u.Dead).ToList();
        switch (i)
        {
            case 0: // 全員の足元に落雷
                foreach (var h in heroes)
                    Fx.Telegraph(h.GlobalPosition, 2.3f, _angry ? 1.2f : 1.5f, pos => HitArea(pos, 2.3f, 34));
                break;
            case 1: // 自分のまわりを薙ぎ払う(離れれば避けられる)
                Fx.Telegraph(GlobalPosition, 6.5f, 1.9f, pos => { HitArea(pos, 6.5f, 48); game.Shake(0.5f); });
                break;
            case 2: // 配信者のまわりに連続で落とす
                var p = game.Player;
                if (p == null || p.Dead) break;
                int n = _angry ? 7 : 5;
                for (int k = 0; k < n; k++)
                {
                    var at = p.GlobalPosition + new Vector3((float)GD.RandRange(-4.5, 4.5), 0, (float)GD.RandRange(-4.5, 4.5));
                    float time = 1.0f + k * 0.18f;
                    Fx.Telegraph(at, 2f, time, pos => HitArea(pos, 2f, 30));
                }
                break;
            default: // 手下を呼ぶ
                game.Hud.Log($"{BossName}が手下を呼んだ!", new Color("ff9a9a"));
                for (int k = 0; k < (_angry ? 4 : 3); k++)
                    game.SpawnMonster(k % 2 == 0 ? Kind.Bat : Kind.Slime, null,
                        GlobalPosition + new Vector3((float)GD.RandRange(-4, 4), 0, (float)GD.RandRange(2, 5)));
                break;
        }
    }

    static void HitArea(Vector3 pos, float r, float dmg)
    {
        dmg *= Main.I.Cfg.EnemyScale; // 難しさ
        foreach (var u in Main.I.Units.ToArray())
            if (u.Team == Team.Hero && !u.Dead && Flat(u.GlobalPosition - pos).Length() - u.Radius < r)
                u.Damage(dmg, null);
    }

    protected override void OnDamaged(float amount)
    {
        if (!_angry && Hp < MaxHp * 0.5f)
        {
            _angry = true;
            _eye.AlbedoColor = _eye.Emission = new Color("ff3b3b");
            Main.I.Hud.Banner($"{BossName}が怒った!", 2f, new Color("ff6b6b"));
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
