using System.Linq;
using System.Collections.Generic;
using Godot;

namespace StreamRaid;

/// <summary>
/// 読み込んだ VRM を包んで、大きさ・向きをそろえ、手続きで動かす(T ポーズ → 腕を下ろす・歩く・攻撃)。
/// VRM にはアニメーションが入っていないので、骨を直接回す。
/// </summary>
public partial class VrmAvatar : Node3D
{
    /// <summary>0〜1。歩きの強さ。</summary>
    public float Move;

    Node3D _model;
    Skeleton3D _sk;
    readonly Dictionary<string, int> _bone = new();
    float _phase, _attack, _time;
    bool _attackRight = true;
    /// <summary>スケルトンの中でキャラが向いている向き(+Z なら 1、-Z なら -1)。</summary>
    float _face = 1;

    public static VrmAvatar Create(Node3D model, float height)
    {
        var a = new VrmAvatar();
        a._model = model;
        a.AddChild(model);
        a.Normalize(height);
        return a;
    }

    void Normalize(float height)
    {
        _sk = FindSkeleton(_model);
        if (_sk != null)
            foreach (var n in new[] { "Hips", "Spine", "Chest", "UpperChest", "Neck", "Head",
                                      "LeftShoulder", "LeftUpperArm", "LeftLowerArm", "RightShoulder", "RightUpperArm", "RightLowerArm",
                                      "LeftUpperLeg", "LeftLowerLeg", "RightUpperLeg", "RightLowerLeg" })
            {
                int i = _sk.FindBone(n);
                if (i >= 0) _bone[n] = i;
            }

        // モデルによって向きが逆(VRM 0.x と 1.0 で違う)なので、左腕がどちら側にあるかで向きを決める。
        // +Z を向いている人は左腕が +X 側にある
        if (_sk != null && _bone.TryGetValue("LeftUpperArm", out int la))
            _face = _sk.GetBoneGlobalRest(la).Origin.X >= 0 ? 1 : -1;
        // ゲームでは -Z が前なので、+Z を向いているモデルは半回転させる
        _model.RotationDegrees = new Vector3(0, _face > 0 ? 180 : 0, 0);
        if (OS.GetCmdlineUserArgs().Contains("--avatartest"))
        {
            GD.Print($"avatar: skeleton={_sk?.Name} bones={_sk?.GetBoneCount()} found={_bone.Count} face={_face}");
            if (_sk != null) GD.Print("avatar bones: " + string.Join(",", System.Linq.Enumerable.Range(0, System.Math.Min(40, _sk.GetBoneCount())).Select(b => _sk.GetBoneName(b))));
            foreach (var n in new[] { "LeftUpperArm", "RightUpperArm" })
                if (_bone.TryGetValue(n, out int bi)) GD.Print($"  {n} rest global = {_sk.GetBoneGlobalRest(bi).Origin}");
        }

        // 見た目の高さをそろえ、足元を地面に合わせる
        var box = Bounds(_model, Transform3D.Identity);
        if (box.Size.Y > 0.01f)
        {
            float s = height / box.Size.Y;
            _model.Scale = Vector3.One * s;
            var b2 = Bounds(_model, Transform3D.Identity);
            _model.Position = new Vector3(-(b2.Position.X + b2.Size.X / 2), -b2.Position.Y, -(b2.Position.Z + b2.Size.Z / 2));
        }
    }

    static Skeleton3D FindSkeleton(Node n)
    {
        if (n is Skeleton3D s) return s;
        foreach (var c in n.GetChildren())
            if (FindSkeleton(c) is { } f) return f;
        return null;
    }

    /// <summary>メッシュ全体の箱(このノードから見た座標)。</summary>
    Aabb Bounds(Node n, Transform3D parent)
    {
        var box = new Aabb();
        bool has = false;
        void Walk(Node node, Transform3D t)
        {
            if (node is Node3D n3) t = t * n3.Transform;
            if (node is MeshInstance3D mi && mi.Mesh != null)
            {
                var b = t * mi.GetAabb();
                box = has ? box.Merge(b) : b;
                has = true;
            }
            foreach (var c in node.GetChildren()) Walk(c, t);
        }
        Walk(n, parent);
        return box;
    }

    public void Attack()
    {
        _attack = 1;
        _attackRight = !_attackRight;
    }

    public override void _Process(double delta)
    {
        if (_sk == null) return;
        float dt = (float)delta;
        _time += dt;
        _phase += dt * Mathf.Lerp(0, 11f, Move);
        _attack = Mathf.Max(0, _attack - dt * 4.5f);

        float walk = Move;
        float swing = Mathf.Sin(_phase) * 0.6f * walk;
        float breath = Mathf.Sin(_time * 2.2f) * 0.03f;

        // 体(歩くと少し前のめり、上下に弾む)
        Rot("Spine", Basis.FromEuler(new Vector3(0.12f * walk + breath, swing * 0.25f, 0)));
        Rot("Neck", Basis.FromEuler(new Vector3(-0.08f * walk, 0, 0)));
        if (_bone.TryGetValue("Hips", out int hips))
        {
            var rest = _sk.GetBoneRest(hips).Origin;
            _sk.SetBonePosePosition(hips, rest + new Vector3(0, Mathf.Abs(Mathf.Cos(_phase)) * 0.03f * walk, 0));
        }

        // 腕: T ポーズから下ろして、歩きに合わせて前後に振る。攻撃は片腕を振り下ろす
        float atk = _attack > 0 ? Mathf.Sin(_attack * Mathf.Pi) : 0;
        float lAtk = !_attackRight ? atk : 0, rAtk = _attackRight ? atk : 0;
        ArmPose("Left", -swing - lAtk * 2.2f);
        ArmPose("Right", swing - rAtk * 2.2f);

        // 脚: 交互に振って、後ろに来た方の膝を曲げる
        Rot("LeftUpperLeg", Basis.FromEuler(new Vector3(swing * 1.1f, 0, 0)));
        Rot("RightUpperLeg", Basis.FromEuler(new Vector3(-swing * 1.1f, 0, 0)));
        Rot("LeftLowerLeg", Basis.FromEuler(new Vector3(Mathf.Max(0, -Mathf.Sin(_phase)) * -1.1f * walk, 0, 0)));
        Rot("RightLowerLeg", Basis.FromEuler(new Vector3(Mathf.Max(0, Mathf.Sin(_phase)) * -1.1f * walk, 0, 0)));
    }

    /// <summary>forward は前後の振り(ラジアン、マイナスが前)。</summary>
    void ArmPose(string prefix, float forward)
    {
        if (!_bone.TryGetValue(prefix + "UpperArm", out int i)) return;
        // 腕が体のどちら側(±X)に伸びているかで、下ろす向きを決める
        float side = _sk.GetBoneGlobalRest(i).Origin.X >= 0 ? 1 : -1;
        // 体から見た向きで回す: Z 軸まわりで下ろし、X 軸まわりで前後に振る(前の向きはモデルの向きに合わせる)
        var down = new Basis(Vector3.Back, -side * 1.2f);
        var fwd = new Basis(Vector3.Right, forward * _face);
        RotModel(prefix + "UpperArm", fwd * down);
        Rot(prefix + "LowerArm", Basis.FromEuler(new Vector3(0, 0, 0)));
    }

    /// <summary>骨を「休み姿勢に対して」親基準で回す。</summary>
    void Rot(string name, Basis local)
    {
        if (!_bone.TryGetValue(name, out int i)) return;
        var rest = _sk.GetBoneRest(i).Basis;
        _sk.SetBonePoseRotation(i, (rest * local).GetRotationQuaternion());
    }

    /// <summary>骨を「体(スケルトン)から見た向き」で回す。腕の上げ下げのように体基準で考えたいとき用。</summary>
    void RotModel(string name, Basis modelRot)
    {
        if (!_bone.TryGetValue(name, out int i)) return;
        int parent = _sk.GetBoneParent(i);
        var parentGlobal = parent >= 0 ? _sk.GetBoneGlobalPose(parent).Basis : Basis.Identity;
        var restGlobal = (parent >= 0 ? _sk.GetBoneGlobalRest(parent).Basis : Basis.Identity) * _sk.GetBoneRest(i).Basis;
        var wantGlobal = modelRot * restGlobal;
        _sk.SetBonePoseRotation(i, (parentGlobal.Inverse() * wantGlobal).GetRotationQuaternion());
    }
}
