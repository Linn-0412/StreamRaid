using Godot;

namespace StreamRaid;

/// <summary>トゥーン調の見た目(マテリアル・仮キャラ・文字)をまとめて作る。</summary>
public static class Toon
{
    public static SystemFont Font { get; private set; }
    static Shader _outline;

    public static void Init()
    {
        Font = new SystemFont
        {
            FontNames = new[] { "Yu Gothic UI", "Meiryo", "Yu Gothic", "MS Gothic", "Noto Sans CJK JP", "Noto Sans JP" },
            FontWeight = 700,
        };
        ThemeDB.FallbackFont = Font;
        ThemeDB.FallbackFontSize = 24;
        _outline = GD.Load<Shader>("res://shaders/outline.gdshader");
    }

    public static StandardMaterial3D Mat(Color c, bool outline = true, float width = 0.035f)
    {
        var m = new StandardMaterial3D
        {
            AlbedoColor = c,
            DiffuseMode = BaseMaterial3D.DiffuseModeEnum.Toon,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.Toon,
            Roughness = 0.55f,
            RimEnabled = true,
            Rim = 0.45f,
            RimTint = 0.6f,
        };
        if (outline)
        {
            var o = new ShaderMaterial { Shader = _outline };
            o.SetShaderParameter("width", width);
            m.NextPass = o;
        }
        return m;
    }

    public static StandardMaterial3D Unshaded(Color c, bool transparent = false)
    {
        var m = new StandardMaterial3D { AlbedoColor = c, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        if (transparent) m.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        return m;
    }

    public static StandardMaterial3D Glow(Color c, float energy = 2.5f, bool transparent = false)
    {
        var m = Unshaded(c, transparent);
        m.EmissionEnabled = true;
        m.Emission = c;
        m.EmissionEnergyMultiplier = energy;
        return m;
    }

    public static MeshInstance3D Mesh(Mesh mesh, Material mat, Vector3 pos, bool shadow = true)
    {
        return new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = mat,
            Position = pos,
            CastShadow = shadow ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    /// <summary>仮のキャラ(胴体・頭・目)。前は -Z。</summary>
    public static Node3D Character(Color body, Color head, float height, float radius)
    {
        var root = new Node3D();
        float bodyH = height * 0.62f;
        root.AddChild(Mesh(new CapsuleMesh { Radius = radius, Height = Mathf.Max(bodyH, radius * 2) }, Mat(body), new Vector3(0, bodyH / 2, 0)));
        float headR = radius * 0.9f;
        float headY = bodyH + headR * 0.75f;
        root.AddChild(Mesh(new SphereMesh { Radius = headR, Height = headR * 2 }, Mat(head), new Vector3(0, headY, 0)));
        var eye = Unshaded(new Color("1b1424"));
        foreach (var sx in new[] { -1f, 1f })
            root.AddChild(Mesh(new BoxMesh { Size = new Vector3(headR * 0.16f, headR * 0.34f, 0.04f) }, eye,
                new Vector3(sx * headR * 0.36f, headY + headR * 0.08f, -headR * 0.92f), false));
        return root;
    }

    public static Label3D Label(string text, int size, Color color)
    {
        return new Label3D
        {
            Text = text,
            Font = Font,
            FontSize = size,
            OutlineSize = size / 3,
            Modulate = color,
            OutlineModulate = new Color(0.05f, 0.03f, 0.08f, 0.95f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            PixelSize = 0.008f,
            RenderPriority = 5,
            OutlineRenderPriority = 4,
        };
    }
}
