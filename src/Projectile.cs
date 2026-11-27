using System;
using Godot;

namespace StreamRaid;

public partial class Projectile : Node3D
{
    Vector3 _start;
    Func<Vector3> _target;
    float _time, _t, _arc;
    Action _onHit;

    public void Setup(Vector3 from, Func<Vector3> target, float time, Color color, float size, float arc,
                      Action onHit, string label, Color labelColor)
    {
        _start = from;
        _target = target;
        _time = Mathf.Max(0.05f, time);
        _arc = arc;
        _onHit = onHit;
        GlobalPosition = from;
        AddChild(Toon.Mesh(new SphereMesh { Radius = size, Height = size * 2, RadialSegments = 16, Rings = 8 }, Toon.Glow(color, 3f), Vector3.Zero, false));
        AddChild(Toon.Mesh(new SphereMesh { Radius = size * 1.8f, Height = size * 3.6f, RadialSegments = 16, Rings = 8 }, Toon.Glow(color with { A = 0.25f }, 1.5f, true), Vector3.Zero, false));
        if (label != null)
        {
            var l = Toon.Label(label, 30, labelColor);
            l.Position = Vector3.Up * (size + 0.35f);
            AddChild(l);
        }
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        float k = Mathf.Min(1, _t / _time);
        Vector3 end;
        try { end = _target(); } catch { end = GlobalPosition; }
        GlobalPosition = _start.Lerp(end, k) + Vector3.Up * _arc * 4 * k * (1 - k);
        if (k >= 1)
        {
            _onHit?.Invoke();
            QueueFree();
        }
    }
}
