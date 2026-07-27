using System;
using System.Collections.Generic;
using Godot;
using GArray = Godot.Collections.Array;
using GDictionary = Godot.Collections.Dictionary;

namespace Diablo1Like.W3.Mdx;

/// <summary>
/// MDX 骨骼动画采样 + 蒙皮桥接，对应旧版 addons/w3kit/w3_mdx_anim.gd。
/// 算法（线性插值/球面插值/整数阶梯采样 + Warsmash 等权重蒙皮）与原 GDScript
/// 版本逐行对应，直接操作 Godot 原生数组类型（int[]/Vector3[]/...），
/// 避免逐帧构造 MdxTrack&lt;T&gt; 等 W3Compat.Core 包装对象的额外分配。
/// bones 参数为 w3_mdx_model.gd 中 Bone 类的实例数组（duck-typed 属性访问）。
/// </summary>
[GlobalClass]
public partial class W3MdxAnim : RefCounted
{
    // ---- 关键帧采样 --------------------------------------------------------

    public static Vector3 SampleVec3(int[] times, Vector3[] values, int timeMs, Vector3 defaultV, int seqStart = -1, int seqEnd = -1)
    {
        FilterVec3(times, values, seqStart, seqEnd, out List<int> ft, out List<Vector3> fv);
        if (ft.Count == 0)
            return defaultV;
        if (ft.Count == 1 || timeMs <= ft[0])
            return fv[0];
        if (timeMs >= ft[ft.Count - 1])
            return fv[fv.Count - 1];
        for (int i = 0; i < ft.Count - 1; i++)
        {
            if (timeMs >= ft[i] && timeMs <= ft[i + 1])
            {
                int span = Math.Max(1, ft[i + 1] - ft[i]);
                float a = (timeMs - ft[i]) / (float)span;
                return fv[i].Lerp(fv[i + 1], a);
            }
        }
        return fv[fv.Count - 1];
    }

    public static Vector3 sample_vec3(int[] times, Vector3[] values, int timeMs, Vector3 defaultV, int seqStart = -1, int seqEnd = -1)
        => SampleVec3(times, values, timeMs, defaultV, seqStart, seqEnd);

    public static Quaternion SampleQuat(int[] times, GArray values, int timeMs, int seqStart = -1, int seqEnd = -1)
    {
        FilterQuat(times, values, seqStart, seqEnd, out List<int> ft, out List<Quaternion> fv);
        if (ft.Count == 0)
            return Quaternion.Identity;
        if (ft.Count == 1 || timeMs <= ft[0])
            return fv[0];
        if (timeMs >= ft[ft.Count - 1])
            return fv[fv.Count - 1];
        for (int i = 0; i < ft.Count - 1; i++)
        {
            if (timeMs >= ft[i] && timeMs <= ft[i + 1])
            {
                int span = Math.Max(1, ft[i + 1] - ft[i]);
                float a = (timeMs - ft[i]) / (float)span;
                return fv[i].Slerp(fv[i + 1], a);
            }
        }
        return fv[fv.Count - 1];
    }

    public static Quaternion sample_quat(int[] times, GArray values, int timeMs, int seqStart = -1, int seqEnd = -1)
        => SampleQuat(times, values, timeMs, seqStart, seqEnd);

    public static float SampleFloat(int[] times, float[] values, int timeMs, float defaultV, int seqStart = -1, int seqEnd = -1)
    {
        var ft = new List<int>();
        var fv = new List<float>();
        int count = Math.Min(times.Length, values.Length);
        for (int i = 0; i < count; i++)
        {
            int t = times[i];
            if (seqStart >= 0 && seqEnd >= 0 && (t < seqStart || t > seqEnd))
                continue;
            ft.Add(t);
            fv.Add(values[i]);
        }
        if (ft.Count == 0)
            return defaultV;
        if (ft.Count == 1 || timeMs <= ft[0])
            return fv[0];
        if (timeMs >= ft[ft.Count - 1])
            return fv[fv.Count - 1];
        for (int i = 0; i < ft.Count - 1; i++)
        {
            if (timeMs >= ft[i] && timeMs <= ft[i + 1])
            {
                int span = Math.Max(1, ft[i + 1] - ft[i]);
                return Mathf.Lerp(fv[i], fv[i + 1], (timeMs - ft[i]) / (float)span);
            }
        }
        return fv[fv.Count - 1];
    }

    public static float sample_float(int[] times, float[] values, int timeMs, float defaultV, int seqStart = -1, int seqEnd = -1)
        => SampleFloat(times, values, timeMs, defaultV, seqStart, seqEnd);

    /// <summary>KMTF 等离散贴图帧轨道：取时间点之前（含）最新一帧，不做插值。</summary>
    public static int SampleIntStep(int[] times, int[] values, int timeMs, int defaultV, int seqStart = -1, int seqEnd = -1)
    {
        int chosen = defaultV;
        bool found = false;
        int count = Math.Min(times.Length, values.Length);
        for (int i = 0; i < count; i++)
        {
            int t = times[i];
            if (seqStart >= 0 && seqEnd >= 0 && (t < seqStart || t > seqEnd))
                continue;
            if (!found) { chosen = values[i]; found = true; }
            if (t > timeMs)
                break;
            chosen = values[i];
        }
        return chosen;
    }

    public static int sample_int_step(int[] times, int[] values, int timeMs, int defaultV, int seqStart = -1, int seqEnd = -1)
        => SampleIntStep(times, values, timeMs, defaultV, seqStart, seqEnd);

    private static void FilterVec3(int[] times, Vector3[] values, int seqStart, int seqEnd, out List<int> outT, out List<Vector3> outV)
    {
        outT = new List<int>();
        outV = new List<Vector3>();
        int count = Math.Min(times.Length, values.Length);
        for (int i = 0; i < count; i++)
        {
            int t = times[i];
            if (seqStart >= 0 && seqEnd >= 0 && (t < seqStart || t > seqEnd))
                continue;
            outT.Add(t);
            outV.Add(values[i]);
        }
    }

    private static void FilterQuat(int[] times, GArray values, int seqStart, int seqEnd, out List<int> outT, out List<Quaternion> outV)
    {
        outT = new List<int>();
        outV = new List<Quaternion>();
        int count = Math.Min(times.Length, values.Count);
        for (int i = 0; i < count; i++)
        {
            int t = times[i];
            if (seqStart >= 0 && seqEnd >= 0 && (t < seqStart || t > seqEnd))
                continue;
            outT.Add(t);
            outV.Add(values[i].AsQuaternion());
        }
    }

    // ---- 局部 / 世界矩阵 -----------------------------------------------------

    /// <summary>gl-matrix mat4.fromRotationTranslationScaleOrigin：先绕 pivot 旋转/缩放，再平移。</summary>
    public static Transform3D LocalMatrix(Quaternion rot, Vector3 loc, Vector3 scale, Vector3 pivot)
    {
        Basis basis = new Basis(rot).Scaled(scale);
        Vector3 origin = loc + pivot - basis * pivot;
        return new Transform3D(basis, origin);
    }

    public static Transform3D local_matrix(Quaternion rot, Vector3 loc, Vector3 scale, Vector3 pivot)
        => LocalMatrix(rot, loc, scale, pivot);

    /// <summary>
    /// 输入 bones 为 w3_mdx_model.gd Bone 对象数组；返回 object_id(int) -> Transform3D
    /// 的 War3 Z-up 世界矩阵字典。父骨骼必须先于子骨骼求解，与 GDS 版本相同的
    /// 分层重试 + 断链保护逻辑（guard 上限、环状引用退化为局部矩阵）。
    /// </summary>
    public static GDictionary ComputeWorldMatrices(GArray bones, int timeMs, int seqStart = -1, int seqEnd = -1)
    {
        var worlds = new GDictionary();
        if (bones.Count == 0)
            return worlds;

        var pending = new List<GodotObject>(bones.Count);
        for (int i = 0; i < bones.Count; i++)
            pending.Add(bones[i].AsGodotObject());

        int guard = 0;
        while (pending.Count > 0 && guard < bones.Count + 2)
        {
            guard++;
            var next = new List<GodotObject>();
            foreach (GodotObject b in pending)
            {
                int parentId = b.Get("parent_id").AsInt32();
                int objectId = b.Get("object_id").AsInt32();
                bool parentOk = parentId < 0 || worlds.ContainsKey(parentId);
                if (!parentOk)
                {
                    next.Add(b);
                    continue;
                }
                Transform3D local = LocalTransformOf(b, timeMs, seqStart, seqEnd);
                worlds[objectId] = parentId < 0 ? local : worlds[parentId].AsTransform3D() * local;
            }
            if (next.Count == pending.Count)
            {
                // 剩余骨骼互相依赖形成环 / 断链：退化为局部矩阵，与 GDS 行为一致。
                foreach (GodotObject b2 in next)
                {
                    int objectId2 = b2.Get("object_id").AsInt32();
                    worlds[objectId2] = LocalTransformOf(b2, timeMs, seqStart, seqEnd);
                }
                break;
            }
            pending = next;
        }
        return worlds;
    }

    public static GDictionary compute_world_matrices(GArray bones, int timeMs, int seqStart = -1, int seqEnd = -1)
        => ComputeWorldMatrices(bones, timeMs, seqStart, seqEnd);

    private static Transform3D LocalTransformOf(GodotObject bone, int timeMs, int seqStart, int seqEnd)
    {
        Vector3 loc = SampleVec3(
            bone.Get("translation_times").AsInt32Array(),
            bone.Get("translation_values").AsVector3Array(),
            timeMs, Vector3.Zero, seqStart, seqEnd);
        Quaternion rot = SampleQuat(
            bone.Get("rotation_times").AsInt32Array(),
            bone.Get("rotation_values").AsGodotArray(),
            timeMs, seqStart, seqEnd);
        Vector3 scale = SampleVec3(
            bone.Get("scale_times").AsInt32Array(),
            bone.Get("scale_values").AsVector3Array(),
            timeMs, Vector3.One, seqStart, seqEnd);
        Vector3 pivot = bone.Get("pivot").AsVector3();
        return LocalMatrix(rot, loc, scale, pivot);
    }

    // ---- 蒙皮 --------------------------------------------------------------

    /// <summary>按四个骨骼权重（Warsmash 等权重方案）蒙皮一个顶点，输入/输出均为 War3 Z-up。</summary>
    public static Vector3 SkinVertex(Vector3 bindWar3, int[] boneIds, float[] boneWeights, int vertexIndex, GDictionary worlds)
    {
        int baseIdx = vertexIndex * 4;
        Vector3 acc = Vector3.Zero;
        float wsum = 0f;
        for (int j = 0; j < 4; j++)
        {
            float wt = baseIdx + j < boneWeights.Length ? boneWeights[baseIdx + j] : 0f;
            if (wt <= 0.0001f)
                continue;
            int oid = baseIdx + j < boneIds.Length ? boneIds[baseIdx + j] : -1;
            if (!worlds.ContainsKey(oid))
                continue;
            Transform3D xf = worlds[oid].AsTransform3D();
            acc += xf * bindWar3 * wt;
            wsum += wt;
        }
        if (wsum <= 0.0001f)
            return bindWar3;
        if (Mathf.Abs(wsum - 1f) > 0.01f)
            acc /= wsum;
        return acc;
    }

    public static Vector3 skin_vertex(Vector3 bindWar3, int[] boneIds, float[] boneWeights, int vertexIndex, GDictionary worlds)
        => SkinVertex(bindWar3, boneIds, boneWeights, vertexIndex, worlds);

    /// <summary>按四个骨骼权重蒙皮一个法线（只应用旋转/缩放部分，不含平移）。</summary>
    public static Vector3 SkinNormal(Vector3 bindWar3N, int[] boneIds, float[] boneWeights, int vertexIndex, GDictionary worlds)
    {
        int baseIdx = vertexIndex * 4;
        Vector3 acc = Vector3.Zero;
        float wsum = 0f;
        for (int j = 0; j < 4; j++)
        {
            float wt = baseIdx + j < boneWeights.Length ? boneWeights[baseIdx + j] : 0f;
            if (wt <= 0.0001f)
                continue;
            int oid = baseIdx + j < boneIds.Length ? boneIds[baseIdx + j] : -1;
            if (!worlds.ContainsKey(oid))
                continue;
            Transform3D xf = worlds[oid].AsTransform3D();
            acc += xf.Basis * bindWar3N * wt;
            wsum += wt;
        }
        if (wsum <= 0.0001f)
            return bindWar3N;
        return (acc / wsum).Normalized();
    }

    public static Vector3 skin_normal(Vector3 bindWar3N, int[] boneIds, float[] boneWeights, int vertexIndex, GDictionary worlds)
        => SkinNormal(bindWar3N, boneIds, boneWeights, vertexIndex, worlds);
}
