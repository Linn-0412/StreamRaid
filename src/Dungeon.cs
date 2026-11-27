using System.Collections.Generic;
using Godot;

namespace StreamRaid;

/// <summary>フロア(四角い部屋・柱・飾り)を作る。</summary>
public static class Dungeon
{
    record Theme(Color A, Color B, Color Wall, Color Light);

    static readonly Theme[] Themes =
    {
        new(new Color("3d6b4f"), new Color("355f45"), new Color("6b5a8e"), new Color("ffd08a")), // 0: 拠点
        new(new Color("5a5f55"), new Color("4f5449"), new Color("7d8a6a"), new Color("ffd08a")), // 1: 石の回廊
        new(new Color("5e4632"), new Color("533d2b"), new Color("8a6a4a"), new Color("ffb36b")), // 2: 嘆きの書庫
        new(new Color("6b6488"), new Color("5f587a"), new Color("c8c0e8"), new Color("e8d8ff")), // 3: 鏡の庭
        new(new Color("4a6a86"), new Color("405d77"), new Color("9fd0f0"), new Color("bff0ff")), // 4: 凍える鐘楼
        new(new Color("241e36"), new Color("1c172b"), new Color("4a3a78"), new Color("c08bff")), // 5: しじまの玉座
    };

    public record Built(float Half, List<Vector3> Blocked);

    public static Built Build(Node3D parent, float size, int pillars, int theme, RandomNumberGenerator rng)
    {
        var th = Themes[Mathf.Clamp(theme, 0, Themes.Length - 1)];
        float h = size / 2;
        var blocked = new List<Vector3>();

        // 床(市松模様)
        var img = Image.CreateEmpty(2, 2, false, Image.Format.Rgb8);
        img.SetPixel(0, 0, th.A); img.SetPixel(1, 1, th.A);
        img.SetPixel(1, 0, th.B); img.SetPixel(0, 1, th.B);
        var fm = Toon.Mat(Colors.White, false);
        fm.AlbedoTexture = ImageTexture.CreateFromImage(img);
        fm.TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest;
        fm.Uv1Scale = new Vector3(size / 4, size / 4, 1);
        var floor = new StaticBody3D { CollisionLayer = 1, CollisionMask = 0 };
        floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(size, 1, size) }, Position = new Vector3(0, -0.5f, 0) });
        floor.AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(size, size) }, MaterialOverride = fm });
        parent.AddChild(floor);

        // 壁
        var wm = Toon.Mat(th.Wall, true, 0.05f);
        foreach (var (pos, sz) in new[]
        {
            (new Vector3(0, 1.1f, -h - 0.5f), new Vector3(size + 2, 2.2f, 1)),
            (new Vector3(0, 1.1f, h + 0.5f), new Vector3(size + 2, 2.2f, 1)),
            (new Vector3(-h - 0.5f, 1.1f, 0), new Vector3(1, 2.2f, size + 2)),
            (new Vector3(h + 0.5f, 1.1f, 0), new Vector3(1, 2.2f, size + 2)),
        })
            parent.AddChild(Block(pos, sz, wm));

        // 柱(中央と入口の通り道は空ける)
        var pm = Toon.Mat(th.Wall.Lightened(0.1f), true, 0.05f);
        for (int i = 0, tries = 0; i < pillars && tries < 200; tries++)
        {
            var p = new Vector3(rng.RandfRange(-h + 3, h - 3), 0, rng.RandfRange(-h + 3, h - 3));
            if (Mathf.Abs(p.X) < 2.5f || p.Length() < 4) continue;
            bool near = false;
            foreach (var b in blocked) if (b.DistanceTo(p) < 4) near = true;
            if (near) continue;
            parent.AddChild(Block(p + new Vector3(0, 1.4f, 0), new Vector3(1.4f, 2.8f, 1.4f), pm));
            blocked.Add(p);
            i++;
        }

        // 飾り(光る結晶と明かり)
        var crystal = Toon.Glow(th.Light, 2.2f);
        for (int i = 0; i < 6; i++)
        {
            float a = i / 6f * Mathf.Tau + 0.4f;
            var p = new Vector3(Mathf.Cos(a) * (h - 1.2f), 0, Mathf.Sin(a) * (h - 1.2f));
            var c = Toon.Mesh(new CylinderMesh { TopRadius = 0, BottomRadius = 0.28f, Height = 0.9f, RadialSegments = 6 }, crystal, p + new Vector3(0, 0.45f, 0), false);
            parent.AddChild(c);
            if (i % 2 == 0)
                parent.AddChild(new OmniLight3D { Position = p + new Vector3(0, 1.6f, 0), LightColor = th.Light, LightEnergy = 1.4f, OmniRange = 9 });
        }
        return new Built(h, blocked);
    }

    static StaticBody3D Block(Vector3 pos, Vector3 size, Material mat)
    {
        var b = new StaticBody3D { Position = pos, CollisionLayer = 1, CollisionMask = 0 };
        b.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        b.AddChild(Toon.Mesh(new BoxMesh { Size = size }, mat, Vector3.Zero));
        return b;
    }

    /// <summary>次の階への出口(光る輪)。</summary>
    public static Node3D Portal(Node3D parent, Vector3 pos)
    {
        var root = new Node3D();
        parent.AddChild(root);
        root.GlobalPosition = pos;
        var ring = Toon.Mesh(new TorusMesh { InnerRadius = 1.1f, OuterRadius = 1.4f, Rings = 32 }, Toon.Glow(new Color("8ff0ff"), 3f), new Vector3(0, 0.1f, 0), false);
        root.AddChild(ring);
        root.AddChild(Toon.Mesh(new CylinderMesh { TopRadius = 1.1f, BottomRadius = 1.1f, Height = 3f }, Toon.Glow(new Color(0.55f, 0.95f, 1f, 0.22f), 1.5f, true), new Vector3(0, 1.5f, 0), false));
        root.AddChild(new OmniLight3D { Position = new Vector3(0, 1.5f, 0), LightColor = new Color("8ff0ff"), LightEnergy = 2.5f, OmniRange = 7 });
        var l = Toon.Label("次の階へ", 40, new Color("bff6ff"));
        l.Position = new Vector3(0, 3.4f, 0);
        root.AddChild(l);
        var t = ring.CreateTween().SetLoops();
        t.TweenProperty(ring, "rotation:y", Mathf.Tau, 3).AsRelative();
        return root;
    }
}
