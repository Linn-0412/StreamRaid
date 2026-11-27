using Godot;

namespace StreamRaid;

/// <summary>斜め上から追いかけるカメラ(揺れつき)。</summary>
public partial class CameraRig : Node3D
{
    public Node3D Target;
    public Vector3 Offset = new(0, 11, 9.5f);
    Camera3D _cam;
    float _shake;
    Vector3 _focus;

    public override void _Ready()
    {
        _cam = new Camera3D { Fov = 50, Current = true };
        AddChild(_cam);
    }

    public void Shake(float amount) => _shake = Mathf.Max(_shake, amount);

    public void Snap()
    {
        if (Target != null && IsInstanceValid(Target)) _focus = Target.GlobalPosition;
        Apply(0);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (Target != null && IsInstanceValid(Target))
            _focus = _focus.Lerp(Target.GlobalPosition, Mathf.Min(1, 6 * dt));
        Apply(dt);
    }

    void Apply(float dt)
    {
        var jitter = Vector3.Zero;
        if (_shake > 0)
        {
            _shake = Mathf.Max(0, _shake - dt * 1.6f);
            jitter = new Vector3((float)GD.RandRange(-1, 1), (float)GD.RandRange(-1, 1), 0) * _shake * 0.5f;
        }
        _cam.GlobalPosition = _focus + Offset + jitter;
        _cam.LookAt(_focus + Vector3.Up * 1.0f + jitter * 0.5f, Vector3.Up);
    }
}
