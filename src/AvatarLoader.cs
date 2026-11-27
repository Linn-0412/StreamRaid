using System.Collections.Generic;
using System.Linq;
using Godot;

namespace StreamRaid;

/// <summary>
/// VRM(0.x / 1.0)を実行中に読み込む。godot-vrm(addons/vrm, MIT)の glTF 拡張を使う。
/// 骨は Godot の人型(SkeletonProfileHumanoid)の名前にそろえて読み込まれる。
/// </summary>
public static class AvatarLoader
{
    // godot-vrm 本体と同じ組み合わせ・順番で登録する(順番が違うと骨の名前付けや材質がずれる)
    static readonly string[] Vrm0 = { "res://addons/vrm/vrm_extension.gd" };
    static readonly string[] Vrm1 =
    {
        "res://addons/vrm/1.0/VRMC_vrm.gd",
        "res://addons/vrm/1.0/VRMC_node_constraint.gd",
        "res://addons/vrm/1.0/VRMC_springBone.gd",                     // 髪・服の揺れ
        "res://addons/vrm/1.0/VRMC_materials_hdr_emissiveMultiplier.gd",
        "res://addons/vrm/1.0/VRMC_materials_mtoon.gd",                // アニメ調の見た目(MToon)
    };

    /// <summary>VRM 1.0 なら true(glb の先頭の JSON に VRMC_vrm がある)。</summary>
    public static bool IsVrm1(string path)
    {
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (f == null || f.GetLength() < 20) return false;
        f.Seek(12);
        uint len = f.Get32();
        f.Get32(); // チャンクの種類(JSON)
        var json = System.Text.Encoding.UTF8.GetString(f.GetBuffer(System.Math.Min(len, 4_000_000)));
        return json.Contains("\"VRMC_vrm\"");
    }

    public static Node3D Load(string path, out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(path) || !FileAccess.FileExists(path))
        {
            error = "VRM ファイルが見つかりません";
            return null;
        }
        var exts = new List<GltfDocumentExtension>();
        foreach (var p in IsVrm1(path) ? Vrm1 : Vrm0)
        {
            var script = GD.Load<GDScript>(p);
            if (script?.New().AsGodotObject() is GltfDocumentExtension e) exts.Add(e);
        }
        foreach (var e in exts) GltfDocument.RegisterGltfDocumentExtension(e, false);
        try
        {
            var doc = new GltfDocument();
            var state = new GltfState();
            // 8 = 接線を作る(表情のブレンドシェイプに必要)
            var err = doc.AppendFromFile(path, state, 8);
            if (err != Error.Ok)
            {
                error = $"VRM を読み込めませんでした({err})";
                return null;
            }
            if (doc.GenerateScene(state) is not Node3D scene)
            {
                error = "VRM の中身を作れませんでした";
                return null;
            }
            return scene;
        }
        finally
        {
            foreach (var e in exts) GltfDocument.UnregisterGltfDocumentExtension(e);
        }
    }
}
