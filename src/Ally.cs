using System.Linq;
using Godot;

namespace StreamRaid;

public enum Job { Warrior, Mage, Priest }

/// <summary>視聴者の仲間。自動で戦い、コメント「技」で溜まったスキルを使う。</summary>
public partial class Ally : Unit
{
    public Job Job;
    public string Viewer;
    public int Level = 1;
    public float Charge;

    float _cd, _dmg, _range, _interval, _speed, _sinceCommand;
    Vector3 _offset;

    public static string JobName(Job j) => j switch { Job.Warrior => "戦士", Job.Mage => "魔法使い", _ => "僧侶" };
    public static Color JobColor(Job j) => j switch { Job.Warrior => new Color("ff8a4c"), Job.Mage => new Color("6aa8ff"), _ => new Color("8dff9a") };

    public void Setup(string viewer, Job job, int level)
    {
        Viewer = viewer;
        Job = job;
        Level = level;
    }

    public override void _Ready()
    {
        Team = Team.Hero;
        DisplayName = Viewer;
        float m = 1 + 0.08f * (Level - 1);
        (MaxHp, _dmg, _range, _interval, _speed) = Job switch
        {
            Job.Warrior => (150 * m, 12 * m, 1.6f, 0.85f, 6.2f),
            Job.Mage => (85 * m, 16 * m, 8f, 1.3f, 5.6f),
            _ => (100 * m, 6 * m, 7f, 1.6f, 5.6f),
        };
        Hp = MaxHp;
        var body = JobColor(Job);
        var v = Toon.Character(body, new Color("ffe9d6"), 1.35f, 0.34f);
        switch (Job)
        {
            case Job.Warrior: // 盾
                v.AddChild(Toon.Mesh(new BoxMesh { Size = new Vector3(0.08f, 0.55f, 0.42f) }, Toon.Mat(new Color("c9d2e3")), new Vector3(0.42f, 0.5f, -0.1f)));
                break;
            case Job.Mage: // とんがり帽子
                v.AddChild(Toon.Mesh(new CylinderMesh { TopRadius = 0, BottomRadius = 0.36f, Height = 0.55f }, Toon.Mat(new Color("3b3f9e")), new Vector3(0, 1.38f, 0)));
                break;
            default: // 光の輪
                v.AddChild(Toon.Mesh(new TorusMesh { InnerRadius = 0.16f, OuterRadius = 0.22f }, Toon.Glow(new Color("fff6a8"), 2f), new Vector3(0, 1.42f, 0), false));
                break;
        }
        Build(v, 1.35f, 0.34f, $"{Viewer}  Lv{Level}", body.Lightened(0.35f), 0.8f);
        float a = (float)GD.RandRange(0, Mathf.Tau);
        _offset = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (float)GD.RandRange(1.6, 3.2);
        _cd = (float)GD.RandRange(0, _interval);
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        if (Dead) return;
        var game = Main.I;
        var player = game.Player;
        _cd -= dt;
        _sinceCommand += dt;
        Charge = Mathf.Min(1, Charge + dt / 14f);
        // 指示がしばらく来なければ自動でスキルを使う(視聴者が離れても止まらないように)
        if (Charge >= 1 && _sinceCommand > 25) UseSkill();

        Unit target = null;
        float best = 12f;
        var anchor = player != null && !player.Dead ? player.GlobalPosition : GlobalPosition;
        foreach (var u in game.Units)
        {
            if (u.Team != Team.Monster || u.Dead) continue;
            float d = Flat(u.GlobalPosition - anchor).Length();
            if (d < best) { best = d; target = u; }
        }

        if (Job == Job.Priest && _cd <= 0)
        {
            var hurt = game.Units.Where(u => u.Team == Team.Hero && !u.Dead && u.Hp < u.MaxHp * 0.85f &&
                                             Flat(u.GlobalPosition - GlobalPosition).Length() < 9)
                                 .OrderBy(u => u.Hp / u.MaxHp).FirstOrDefault();
            if (hurt != null)
            {
                _cd = _interval;
                var h = hurt;
                Fx.Shot(GlobalPosition + Vector3.Up * 1.2f, () => GodotObject.IsInstanceValid(h) ? h.GlobalPosition + Vector3.Up : Vector3.Zero,
                        0.35f, new Color("8dff9a"), 0.14f, 0.6f, () => { if (GodotObject.IsInstanceValid(h)) h.Heal(16 * (1 + 0.08f * (Level - 1))); });
            }
        }

        Vector3 dest;
        if (target != null)
        {
            var to = Flat(target.GlobalPosition - GlobalPosition);
            float dist = to.Length() - target.Radius - Radius;
            dest = dist > _range - 0.2f ? target.GlobalPosition : GlobalPosition;
            Face(to, dt);
            if (dist <= _range && _cd <= 0) Attack(target);
        }
        else
        {
            dest = anchor + _offset;
            Face(Flat(dest - GlobalPosition), dt);
        }
        // 攻撃予告の円の中にいたら外へ逃げる
        foreach (var d in Fx.Dangers)
        {
            var away = Flat(GlobalPosition - d.Pos);
            if (away.Length() < d.Radius + Radius + 0.3f)
            {
                if (away.LengthSquared() < 0.01f) away = Vector3.Right;
                dest = d.Pos + away.Normalized() * (d.Radius + Radius + 1f);
                break;
            }
        }
        var dir = Flat(dest - GlobalPosition);
        Velocity = dir.Length() > 0.4f ? dir.Normalized() * _speed : Vector3.Zero;
        Separate();
        MoveAndSlide();
    }

    void Attack(Unit t)
    {
        _cd = _interval;
        if (Job == Job.Warrior)
        {
            Fx.Swing(GlobalPosition, Flat(t.GlobalPosition - GlobalPosition).Normalized(), JobColor(Job), 1.5f);
            t.Damage(_dmg + (float)GD.RandRange(0, 4), Viewer);
            return;
        }
        var color = Job == Job.Mage ? new Color("6aa8ff") : new Color("fff6a8");
        var tgt = t;
        float dmg = _dmg + (float)GD.RandRange(0, 4);
        Fx.Shot(GlobalPosition + Vector3.Up * 1.1f,
                () => GodotObject.IsInstanceValid(tgt) ? tgt.GlobalPosition + Vector3.Up * tgt.Height * 0.5f : Vector3.Zero,
                0.4f, color, Job == Job.Mage ? 0.2f : 0.12f, 0.5f,
                () => { if (GodotObject.IsInstanceValid(tgt) && !tgt.Dead) tgt.Damage(dmg, Viewer); });
    }

    /// <summary>視聴者のコメント「技」で呼ばれる。溜まっていなければ false。</summary>
    public bool Command()
    {
        _sinceCommand = 0;
        if (Charge < 1) return false;
        UseSkill();
        return true;
    }

    void UseSkill()
    {
        Charge = 0;
        _sinceCommand = 0;
        float m = 1 + 0.08f * (Level - 1);
        var game = Main.I;
        game.Hud.Log($"{Viewer} の{SkillName(Job)}!", JobColor(Job));
        switch (Job)
        {
            case Job.Warrior:
                Fx.Burst(GlobalPosition + Vector3.Up * 0.6f, JobColor(Job), 3.2f, 0.35f);
                foreach (var u in game.Units.ToArray())
                    if (u.Team == Team.Monster && !u.Dead && DistTo(u) < 3.2f) u.Damage(45 * m, Viewer);
                break;
            case Job.Mage:
                var target = game.Units.Where(u => u.Team == Team.Monster && !u.Dead)
                                 .OrderBy(u => Flat(u.GlobalPosition - GlobalPosition).Length()).FirstOrDefault();
                var at = target?.GlobalPosition ?? GlobalPosition + Forward * 4;
                Fx.Shot(at + new Vector3(2, 9, 2), () => at, 0.6f, new Color("8fc2ff"), 0.6f, 0f, () =>
                {
                    Fx.Burst(at + Vector3.Up * 0.5f, new Color("8fc2ff"), 3.6f, 0.4f);
                    game.Shake(0.25f);
                    foreach (var u in game.Units.ToArray())
                        if (u.Team == Team.Monster && !u.Dead && Flat(u.GlobalPosition - at).Length() - u.Radius < 3.6f) u.Damage(60 * m, Viewer);
                });
                break;
            default:
                Fx.Burst(GlobalPosition + Vector3.Up * 0.6f, new Color("8dff9a"), 5f, 0.45f);
                foreach (var u in game.Units.ToArray())
                    if (u.Team == Team.Hero && !u.Dead) u.Heal(60 * m);
                break;
        }
    }

    public static string SkillName(Job j) => j switch { Job.Warrior => "回転斬り", Job.Mage => "メテオ", _ => "祝福" };
}
