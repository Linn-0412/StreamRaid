using System.Linq;
using Godot;

namespace StreamRaid;

/// <summary>自動で遊ぶデモ(--demo)。動作確認と紹介動画の録画に使う。</summary>
public partial class DemoBot : Node
{
    public TestChat Chat;
    double _join, _summon, _skill, _cheer, _paid;
    int _joined;
    float _orbit;

    public override void _Process(double delta)
    {
        var g = Main.I;
        var p = g.Player;
        if (p == null || !IsInstanceValid(p)) return;
        p.Bot = true;
        double now = g.Now;

        switch (g.State)
        {
            case Main.Phase.Lobby:
                if (now > _join && _joined < 6)
                {
                    _join = now + 0.6;
                    Chat.Join(TestChat.Names[_joined++]);
                }
                p.BotMove = new Vector3(Mathf.Sin((float)now * 0.8f), 0, Mathf.Cos((float)now * 0.8f)) * 0.4f;
                break;

            case Main.Phase.Explore:
                if (now > _summon) { _summon = now + 4.5; Chat.Summon(TestChat.Names[6 + (int)(GD.Randi() % 6)]); }
                if (now > _skill && g.PartyNames.Count > 0) { _skill = now + 3; Chat.Skill(g.PartyNames[(int)(GD.Randi() % g.PartyNames.Count)]); }
                var target = g.Units.Where(u => u.Team == Team.Monster && !u.Dead)
                              .OrderBy(u => (u.GlobalPosition - p.GlobalPosition).LengthSquared()).FirstOrDefault();
                if (target != null)
                {
                    var to = Unit.Flat(target.GlobalPosition - p.GlobalPosition);
                    p.BotMove = to.Length() > 1.6f ? to.Normalized() : to.Normalized() * 0.05f;
                    if (to.Length() < 2.2f) p.BotAttack = true;
                }
                else if (g.Portal != null)
                {
                    p.BotMove = Unit.Flat(g.Portal.GlobalPosition - p.GlobalPosition).Normalized();
                }
                else p.BotMove = Vector3.Zero;
                break;

            case Main.Phase.Boss:
                if (now > _cheer) { _cheer = now + 0.12; Chat.Cheer(); }
                if (now > _paid) { _paid = now + 5.5; Chat.Paid(); }
                if (now > _skill && g.PartyNames.Count > 0) { _skill = now + 2.5; Chat.Skill(g.PartyNames[(int)(GD.Randi() % g.PartyNames.Count)]); }
                var boss = g.BossUnit;
                if (boss == null || !IsInstanceValid(boss)) { p.BotMove = Vector3.Zero; break; }
                // 予告の円の中にいたら外へ回避、それ以外はボスのまわりを回りながら殴る
                var danger = Fx.Dangers.FirstOrDefault(d => Unit.Flat(p.GlobalPosition - d.Pos).Length() < d.Radius + 0.5f);
                if (danger != null)
                {
                    var away = Unit.Flat(p.GlobalPosition - danger.Pos);
                    if (away.LengthSquared() < 0.01f) away = Vector3.Right;
                    p.BotMove = away.Normalized();
                    if (danger.End - now < 0.45) p.BotDodge = true;
                    break;
                }
                _orbit += (float)delta * 0.6f;
                var want = boss.GlobalPosition + new Vector3(Mathf.Cos(_orbit), 0, Mathf.Sin(_orbit)) * 3.4f;
                var dv = Unit.Flat(want - p.GlobalPosition);
                p.BotMove = dv.Length() > 0.3f ? dv.Normalized() : Vector3.Zero;
                if (p.DistTo(boss) < 1.8f) p.BotAttack = true;
                break;
        }
    }
}
