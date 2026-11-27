using System.Linq;
using Godot;

namespace StreamRaid;

/// <summary>配信者のキャラ。移動・攻撃・回避(無敵時間つき)。</summary>
public partial class Player : Unit
{
    public bool Bot;
    public Vector3 BotMove;
    public bool BotAttack, BotDodge;

    float _atkCd, _dodgeT, _dodgeCd;
    Vector3 _dodgeDir, _facing = Vector3.Forward;

    public override void _Ready()
    {
        DisplayName = Main.I.Cfg.StreamerName;
        Team = Team.Hero;
        MaxHp = Hp = 320;
        var v = Toon.Character(new Color("ff7eb6"), new Color("ffe3ef"), 1.75f, 0.42f);
        // 王冠(配信者の目印)
        var crown = Toon.Mat(new Color("ffd34d"), true, 0.02f);
        v.AddChild(Toon.Mesh(new CylinderMesh { TopRadius = 0.26f, BottomRadius = 0.2f, Height = 0.18f, RadialSegments = 8 }, crown, new Vector3(0, 1.62f, 0)));
        Build(v, 1.75f, 0.42f, DisplayName, new Color("ffd34d"), 1.1f, new Color("ff8fc8"));
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        if (Dead) return;
        Vector3 move;
        bool atk, dodge;
        if (Bot)
        {
            move = BotMove;
            atk = BotAttack;
            dodge = BotDodge;
            BotAttack = BotDodge = false;
        }
        else
        {
            bool blocked = Main.I.InputBlocked; // 設定画面で文字を打っている間は動かない
            var v = blocked ? Vector2.Zero : Input.GetVector("move_left", "move_right", "move_forward", "move_back");
            move = new Vector3(v.X, 0, v.Y);
            atk = !blocked && Input.IsActionJustPressed("attack");
            dodge = !blocked && Input.IsActionJustPressed("dodge");
        }
        if (move.LengthSquared() > 1) move = move.Normalized();

        _atkCd -= dt;
        _dodgeCd -= dt;
        if (_dodgeT > 0)
        {
            _dodgeT -= dt;
            Velocity = _dodgeDir * 17f;
        }
        else
        {
            Velocity = move * 6.5f;
            if (move.LengthSquared() > 0.01f) _facing = move.Normalized();
            if (dodge && _dodgeCd <= 0)
            {
                _dodgeDir = move.LengthSquared() > 0.01f ? move.Normalized() : _facing;
                _dodgeT = 0.2f;
                _dodgeCd = 0.6f;
                Invuln = 0.32f;
                Fx.Burst(GlobalPosition + Vector3.Up * 0.6f, new Color("ffd1ea"), 0.9f, 0.2f);
            }
            if (atk && _atkCd <= 0) Attack();
        }
        MoveAndSlide();
        Face(_facing, dt, 18f);
    }

    void Attack()
    {
        _atkCd = 0.3f;
        Fx.Swing(GlobalPosition, _facing, new Color("ffe066"));
        foreach (var u in Main.I.Units.ToArray())
        {
            if (u.Team != Team.Monster || u.Dead) continue;
            var to = Flat(u.GlobalPosition - GlobalPosition);
            float d = to.Length() - u.Radius;
            if (d < 1.9f && (d < 0.5f || _facing.Dot(to.Normalized()) > 0.15f))
                u.Damage(26 + (float)GD.RandRange(0, 6), null);
        }
    }

    public void Rename(string name)
    {
        DisplayName = name;
        if (NameLabel != null) NameLabel.Text = name;
    }

    public void Revive(float ratio)
    {
        if (Dead) return;
        Heal(MaxHp * ratio);
    }
}
