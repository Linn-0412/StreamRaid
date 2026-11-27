using System.Collections.Generic;
using Godot;

namespace StreamRaid;

public enum Team { Hero, Monster }

/// <summary>戦うキャラの共通部分(HP・被弾・名前・HP バー)。</summary>
public partial class Unit : CharacterBody3D
{
    public string DisplayName = "";
    public Team Team;
    public float MaxHp = 100, Hp = 100;
    public float Radius = 0.45f, Height = 1.6f;
    public float Invuln;
    public bool Dead => Hp <= 0 || _dying;
    public event System.Action<Unit> Died;

    protected Node3D Visual;
    protected Label3D NameLabel;
    HpBar3D _bar;
    readonly List<(StandardMaterial3D m, Color c)> _mats = new();
    float _flash;
    /// <summary>被弾したときに白く光る強さ(ボスは何度も当たるので弱め)。</summary>
    protected float FlashStrength = 0.75f;
    bool _dying;

    public override void _EnterTree() => Main.I?.Units.Add(this);
    public override void _ExitTree() => Main.I?.Units.Remove(this);

    protected void Build(Node3D visual, float height, float radius, string label, Color labelColor,
                         float barWidth = 0.9f, Color? barColor = null)
    {
        Height = height;
        Radius = radius;
        MotionMode = MotionModeEnum.Floating;
        CollisionLayer = 2;
        CollisionMask = 1;
        AddChild(new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = radius, Height = Mathf.Max(height, radius * 2) },
            Position = new Vector3(0, height / 2, 0),
        });
        Visual = visual;
        AddChild(visual);
        CollectMats(visual);
        if (label != null)
        {
            NameLabel = Toon.Label(label, 30, labelColor);
            NameLabel.Position = new Vector3(0, height + 0.5f, 0);
            AddChild(NameLabel);
        }
        if (barWidth > 0)
        {
            _bar = new HpBar3D();
            _bar.Setup(barWidth, barColor ?? new Color("7cfc7c"));
            _bar.Position = new Vector3(0, height + 0.22f, 0);
            AddChild(_bar);
        }
    }

    void CollectMats(Node n)
    {
        if (n is MeshInstance3D mi && mi.MaterialOverride is StandardMaterial3D sm &&
            sm.ShadingMode != BaseMaterial3D.ShadingModeEnum.Unshaded)
            _mats.Add((sm, sm.AlbedoColor));
        foreach (var c in n.GetChildren()) CollectMats(c);
    }

    public virtual void Damage(float amount, string source = null)
    {
        if (Dead || Invuln > 0) return;
        Hp = Mathf.Max(0, Hp - amount);
        _flash = 0.12f;
        Fx.Popup(GlobalPosition + new Vector3(0, Height + 0.3f, 0), Mathf.RoundToInt(amount).ToString(),
                 Team == Team.Hero ? new Color("ff6b6b") : new Color("fff3a0"), Team == Team.Hero ? 40 : 48);
        Main.I.Credit(source, amount, this);
        OnDamaged(amount);
        if (Hp <= 0) Die();
    }

    protected virtual void OnDamaged(float amount) { }

    public void Heal(float amount)
    {
        if (Dead) return;
        float before = Hp;
        Hp = Mathf.Min(MaxHp, Hp + amount);
        if (Hp > before + 0.5f)
            Fx.Popup(GlobalPosition + new Vector3(0, Height + 0.3f, 0), "+" + Mathf.RoundToInt(Hp - before), new Color("8dff9a"), 38);
    }

    protected virtual void Die()
    {
        _dying = true;
        Main.I.Units.Remove(this);
        Died?.Invoke(this);
        CollisionLayer = 0;
        Fx.Burst(GlobalPosition + Vector3.Up * Height * 0.5f, new Color("ffffff"), Radius * 2.4f, 0.3f);
        var t = CreateTween();
        t.TweenProperty(Visual, "scale", new Vector3(1.4f, 0.05f, 1.4f), 0.25);
        t.TweenCallback(Callable.From(QueueFree));
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (Invuln > 0) Invuln -= dt;
        if (_flash > 0)
        {
            _flash -= dt;
            foreach (var (m, c) in _mats) m.AlbedoColor = _flash > 0 ? c.Lerp(Colors.White, FlashStrength) : c;
        }
        _bar?.Set(Hp / MaxHp);
    }

    protected void Face(Vector3 dir, float dt, float speed = 12f)
    {
        dir.Y = 0;
        if (dir.LengthSquared() < 0.0001f) return;
        float target = Mathf.Atan2(-dir.X, -dir.Z);
        var r = Visual.Rotation;
        r.Y = Mathf.LerpAngle(r.Y, target, Mathf.Min(1, speed * dt));
        Visual.Rotation = r;
    }

    public Vector3 Forward => -Visual.GlobalBasis.Z with { Y = 0 };

    public static Vector3 Flat(Vector3 v) => v with { Y = 0 };

    public float DistTo(Unit u) => Flat(u.GlobalPosition - GlobalPosition).Length() - u.Radius - Radius;

    /// <summary>同じ陣営どうしで重ならないように少し押し合う。</summary>
    protected void Separate()
    {
        foreach (var u in Main.I.Units)
        {
            if (u == this || u.Team != Team) continue;
            var d = Flat(GlobalPosition - u.GlobalPosition);
            float min = Radius + u.Radius;
            float l = d.Length();
            if (l < min && l > 0.001f) Velocity += d / l * (min - l) * 8f;
        }
    }
}
