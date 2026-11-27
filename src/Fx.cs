using System;
using System.Collections.Generic;
using Godot;

namespace StreamRaid;

/// <summary>エフェクト(ダメージ数字・爆発・攻撃予告の円・飛び道具)。</summary>
public static class Fx
{
    static Node3D Root => Main.I.Level;

    public record Danger(Vector3 Pos, float Radius, double End);

    /// <summary>今出ている攻撃予告(デモ操作で避けるのに使う)。</summary>
    public static readonly List<Danger> Dangers = new();

    public static void Popup(Vector3 pos, string text, Color color, int size = 48)
    {
        var l = Toon.Label(text, size, color);
        Root.AddChild(l);
        var p = pos + new Vector3((float)GD.RandRange(-0.3, 0.3), 0, (float)GD.RandRange(-0.2, 0.2));
        l.GlobalPosition = p;
        var t = l.CreateTween().SetParallel();
        t.TweenProperty(l, "global_position", p + Vector3.Up * 1.1f, 0.8).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        t.TweenProperty(l, "modulate:a", 0f, 0.4).SetDelay(0.45);
        t.TweenProperty(l, "outline_modulate:a", 0f, 0.4).SetDelay(0.45);
        t.Chain().TweenCallback(Callable.From(l.QueueFree));
    }

    public static void Burst(Vector3 pos, Color color, float radius, float time)
    {
        var mat = Toon.Glow(color with { A = 0.85f }, 2.5f, true);
        var m = Toon.Mesh(new SphereMesh { Radius = 1, Height = 2, RadialSegments = 24, Rings = 12 }, mat, Vector3.Zero, false);
        Root.AddChild(m);
        m.GlobalPosition = pos;
        m.Scale = Vector3.One * 0.2f;
        var t = m.CreateTween().SetParallel();
        t.TweenProperty(m, "scale", Vector3.One * radius, time).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quad);
        t.TweenProperty(mat, "albedo_color:a", 0f, time);
        t.Chain().TweenCallback(Callable.From(m.QueueFree));
    }

    /// <summary>剣を振った軌跡(前方に平たい弧)。</summary>
    public static void Swing(Vector3 pos, Vector3 dir, Color color, float reach = 1.9f)
    {
        var mat = Toon.Glow(color with { A = 0.7f }, 2f, true);
        mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        var m = Toon.Mesh(new CylinderMesh { TopRadius = reach, BottomRadius = reach, Height = 0.05f, RadialSegments = 24 }, mat, Vector3.Zero, false);
        Root.AddChild(m);
        m.GlobalPosition = pos + Vector3.Up * 0.8f + dir * reach * 0.35f;
        m.Scale = new Vector3(0.6f, 1, 0.35f);
        m.LookAt(m.GlobalPosition + dir, Vector3.Up);
        var t = m.CreateTween().SetParallel();
        t.TweenProperty(m, "scale", new Vector3(1f, 1, 0.55f), 0.14);
        t.TweenProperty(mat, "albedo_color:a", 0f, 0.16);
        t.Chain().TweenCallback(Callable.From(m.QueueFree));
    }

    /// <summary>地面に攻撃予告の円を出し、time 秒後に onFire を呼ぶ。</summary>
    public static void Telegraph(Vector3 pos, float radius, float time, Action<Vector3> onFire, Color? color = null)
    {
        pos.Y = 0.04f;
        var c = color ?? new Color(1f, 0.22f, 0.3f);
        var root = new Node3D();
        Root.AddChild(root);
        root.GlobalPosition = pos;
        var ringMat = Toon.Unshaded(c with { A = 0.28f }, true);
        root.AddChild(Toon.Mesh(new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = 0.02f, RadialSegments = 48 }, ringMat, Vector3.Zero, false));
        var fillMat = Toon.Glow(c with { A = 0.45f }, 1.2f, true);
        var fill = Toon.Mesh(new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = 0.03f, RadialSegments = 48 }, fillMat, Vector3.Zero, false);
        fill.Scale = new Vector3(0.01f, 1, 0.01f);
        root.AddChild(fill);

        var d = new Danger(pos, radius, Main.I.Now + time);
        Dangers.Add(d);
        var t = root.CreateTween();
        t.TweenProperty(fill, "scale", Vector3.One, time);
        t.TweenCallback(Callable.From(() =>
        {
            Dangers.Remove(d);
            onFire?.Invoke(pos);
            Burst(pos + Vector3.Up * 0.4f, new Color(1f, 0.55f, 0.25f), radius, 0.3f);
            root.QueueFree();
        }));
    }

    /// <summary>飛び道具。target は毎フレーム呼んで追いかける。</summary>
    public static void Shot(Vector3 from, Func<Vector3> target, float time, Color color, float size, float arc,
                            Action onHit, string label = null, Color? labelColor = null)
    {
        var p = new Projectile();
        Root.AddChild(p);
        p.Setup(from, target, time, color, size, arc, onHit, label, labelColor ?? Colors.White);
    }

    public static void Clear() => Dangers.Clear();
}
