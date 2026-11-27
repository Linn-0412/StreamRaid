using Godot;

namespace StreamRaid;

/// <summary>頭の上の HP バー(常にカメラを向く)。</summary>
public partial class HpBar3D : Node3D
{
    QuadMesh _fg;
    float _w;

    public void Setup(float width, Color color)
    {
        _w = width;
        var bg = Toon.Unshaded(new Color(0, 0, 0, 0.65f), true);
        bg.BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled;
        bg.NoDepthTest = true;
        bg.RenderPriority = 2;
        AddChild(Toon.Mesh(new QuadMesh { Size = new Vector2(width + 0.06f, 0.15f) }, bg, Vector3.Zero, false));

        var fg = Toon.Unshaded(color, true);
        fg.BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled;
        fg.NoDepthTest = true;
        fg.RenderPriority = 3;
        _fg = new QuadMesh { Size = new Vector2(width, 0.09f) };
        AddChild(Toon.Mesh(_fg, fg, Vector3.Zero, false));
    }

    public void Set(float ratio)
    {
        if (_fg == null) return;
        ratio = Mathf.Clamp(ratio, 0, 1);
        _fg.Size = new Vector2(Mathf.Max(0.001f, _w * ratio), 0.09f);
        _fg.CenterOffset = new Vector3(-_w * (1 - ratio) / 2f, 0, 0);
    }
}
