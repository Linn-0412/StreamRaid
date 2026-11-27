using Godot;

namespace StreamRaid;

public enum Kind { Slime, Goblin, Bat }

/// <summary>ダンジョンの敵。視聴者が召喚した敵は名前が付く。</summary>
public partial class Enemy : Unit
{
    public Kind Kind;
    public string Summoner;

    float _speed, _dmg, _range = 1.2f, _wind = -1, _cd, _retarget, _bob;
    Unit _target;

    public static string KindName(Kind k) => k switch { Kind.Slime => "スライム", Kind.Goblin => "ゴブリン", _ => "コウモリ" };

    public void Setup(Kind kind, string summoner, float scale = 1f)
    {
        Kind = kind;
        Summoner = summoner;
        _scale = scale;
    }
    float _scale = 1f;

    public override void _Ready()
    {
        Team = Team.Monster;
        Color c;
        Node3D v;
        float h, r;
        switch (Kind)
        {
            case Kind.Slime:
                (MaxHp, _dmg, _speed, c, h, r) = (45f, 8f, 2.8f, new Color("5ee07a"), 0.8f, 0.5f);
                v = new Node3D();
                v.AddChild(Toon.Mesh(new SphereMesh { Radius = r, Height = h * 1.3f }, Toon.Mat(c), new Vector3(0, h * 0.62f, 0)));
                Eyes(v, h * 0.7f, r * 0.9f);
                break;
            case Kind.Goblin:
                (MaxHp, _dmg, _speed, c, h, r) = (65f, 12f, 3.5f, new Color("b07a4a"), 1.25f, 0.36f);
                v = Toon.Character(c, new Color("9fd36a"), h, r);
                v.AddChild(Toon.Mesh(new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.07f, Height = 0.7f }, Toon.Mat(new Color("6b4a2e")), new Vector3(0.38f, 0.6f, -0.2f)));
                break;
            default:
                (MaxHp, _dmg, _speed, c, h, r) = (28f, 6f, 4.6f, new Color("9a6bff"), 0.6f, 0.32f);
                v = new Node3D();
                v.AddChild(Toon.Mesh(new SphereMesh { Radius = r, Height = r * 2 }, Toon.Mat(c), new Vector3(0, 0.9f, 0)));
                foreach (var sx in new[] { -1f, 1f })
                {
                    var wing = Toon.Mesh(new BoxMesh { Size = new Vector3(0.55f, 0.04f, 0.3f) }, Toon.Mat(c.Darkened(0.3f)), new Vector3(sx * 0.5f, 0.95f, 0));
                    wing.RotationDegrees = new Vector3(0, 0, sx * 20);
                    v.AddChild(wing);
                }
                Eyes(v, 0.95f, r);
                break;
        }
        MaxHp *= _scale;
        Hp = MaxHp;
        v.Scale = Vector3.One * _scale;
        var label = Summoner != null ? $"{Summoner}の{KindName(Kind)}" : null;
        Build(v, h * _scale, r * _scale, label, new Color("ffb3b3"), 0.7f * _scale, new Color("ff5c5c"));
        DisplayName = label ?? KindName(Kind);
        _bob = (float)GD.RandRange(0, 10);
    }

    static void Eyes(Node3D v, float y, float r)
    {
        var eye = Toon.Unshaded(new Color("1b1424"));
        foreach (var sx in new[] { -1f, 1f })
            v.AddChild(Toon.Mesh(new BoxMesh { Size = new Vector3(0.07f, 0.13f, 0.04f) }, eye, new Vector3(sx * r * 0.35f, y, -r * 0.95f), false));
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        if (Dead) return;
        _cd -= dt;
        _retarget -= dt;
        _bob += dt;
        if (Kind == Kind.Bat) Visual.Position = new Vector3(0, Mathf.Sin(_bob * 5) * 0.15f, 0);
        if (Kind == Kind.Slime) Visual.Scale = new Vector3(1 + Mathf.Sin(_bob * 6) * 0.06f, 1 - Mathf.Sin(_bob * 6) * 0.06f, 1 + Mathf.Sin(_bob * 6) * 0.06f) * _scale;

        if (_retarget <= 0 || _target == null || !IsInstanceValid(_target) || _target.Dead)
        {
            _retarget = 0.5f;
            _target = null;
            float best = 999;
            foreach (var u in Main.I.Units)
            {
                if (u.Team != Team.Hero || u.Dead) continue;
                float d = Flat(u.GlobalPosition - GlobalPosition).Length();
                if (d < best) { best = d; _target = u; }
            }
        }
        if (_target == null) { Velocity = Vector3.Zero; MoveAndSlide(); return; }

        var to = Flat(_target.GlobalPosition - GlobalPosition);
        float dist = to.Length() - _target.Radius - Radius;
        Face(to, dt, 8f);

        if (_wind >= 0)
        {
            // 攻撃の溜め(赤く光って分かるように)
            _wind -= dt;
            Visual.Scale = Visual.Scale.Lerp(Vector3.One * _scale * 1.18f, 0.3f);
            Velocity = Vector3.Zero;
            if (_wind < 0)
            {
                Visual.Scale = Vector3.One * _scale;
                _cd = 1.1f;
                if (dist < _range + 0.7f)
                {
                    float before = _target.Hp;
                    _target.Damage(_dmg, null);
                    if (Summoner != null) Main.I.CreditSabotage(Summoner, before - _target.Hp);
                }
            }
        }
        else if (dist > _range)
        {
            Velocity = to.Normalized() * _speed;
        }
        else
        {
            Velocity = Vector3.Zero;
            if (_cd <= 0) _wind = 0.45f;
        }
        Separate();
        MoveAndSlide();
    }
}
