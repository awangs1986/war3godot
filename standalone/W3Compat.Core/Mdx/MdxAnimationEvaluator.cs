using System.Numerics;

namespace W3Compat.Mdx;

/// <summary>纯 C# MDX 关键帧采样、骨骼层级和蒙皮计算。</summary>
public static class MdxAnimationEvaluator
{
    /// <summary>线性采样浮点轨道；可选范围用于序列局部时间。</summary>
    public static float SampleFloat(MdxTrack<float> track, int timeMs, float fallback, int sequenceStart = -1, int sequenceEnd = -1)
    {
        List<int> times = track.Times;
        List<float> values = track.Values;
        int first = FindFirstInRange(times, sequenceStart, sequenceEnd);
        int last = FindLastInRange(times, sequenceStart, sequenceEnd);
        if (first < 0 || first >= values.Count)
            return fallback;
        if (first == last || timeMs <= times[first])
            return values[first];
        if (timeMs >= times[last])
            return values[Math.Min(last, values.Count - 1)];
        for (int i = first; i < last && i + 1 < values.Count; i++)
            if (timeMs <= times[i + 1])
                return values[i] + (values[i + 1] - values[i]) * ((timeMs - times[i]) / (float)Math.Max(1, times[i + 1] - times[i]));
        return values[Math.Min(last, values.Count - 1)];
    }

    /// <summary>线性采样位置或缩放向量。</summary>
    public static Vector3 SampleVector3(MdxTrack<Vector3> track, int timeMs, Vector3 fallback, int sequenceStart = -1, int sequenceEnd = -1)
    {
        int first = FindFirstInRange(track.Times, sequenceStart, sequenceEnd);
        int last = FindLastInRange(track.Times, sequenceStart, sequenceEnd);
        if (first < 0 || first >= track.Values.Count)
            return fallback;
        if (first == last || timeMs <= track.Times[first])
            return track.Values[first];
        if (timeMs >= track.Times[last])
            return track.Values[Math.Min(last, track.Values.Count - 1)];
        for (int i = first; i < last && i + 1 < track.Values.Count; i++)
            if (timeMs <= track.Times[i + 1])
                return Vector3.Lerp(track.Values[i], track.Values[i + 1], (timeMs - track.Times[i]) / (float)Math.Max(1, track.Times[i + 1] - track.Times[i]));
        return track.Values[Math.Min(last, track.Values.Count - 1)];
    }

    /// <summary>球面插值旋转四元数。</summary>
    public static Quaternion SampleQuaternion(MdxTrack<Quaternion> track, int timeMs, int sequenceStart = -1, int sequenceEnd = -1)
    {
        int first = FindFirstInRange(track.Times, sequenceStart, sequenceEnd);
        int last = FindLastInRange(track.Times, sequenceStart, sequenceEnd);
        if (first < 0 || first >= track.Values.Count)
            return Quaternion.Identity;
        if (first == last || timeMs <= track.Times[first])
            return Quaternion.Normalize(track.Values[first]);
        if (timeMs >= track.Times[last])
            return Quaternion.Normalize(track.Values[Math.Min(last, track.Values.Count - 1)]);
        for (int i = first; i < last && i + 1 < track.Values.Count; i++)
            if (timeMs <= track.Times[i + 1])
                return Quaternion.Slerp(Quaternion.Normalize(track.Values[i]), Quaternion.Normalize(track.Values[i + 1]), (timeMs - track.Times[i]) / (float)Math.Max(1, track.Times[i + 1] - track.Times[i]));
        return Quaternion.Normalize(track.Values[Math.Min(last, track.Values.Count - 1)]);
    }

    /// <summary>KMTF 等整数轨道采用最新关键帧，不做插值。</summary>
    public static int SampleIntStep(MdxTrack<int> track, int timeMs, int fallback, int sequenceStart = -1, int sequenceEnd = -1)
    {
        int chosen = fallback;
        bool found = false;
        for (int i = 0; i < track.Times.Count && i < track.Values.Count; i++)
        {
            int time = track.Times[i];
            if (!InRange(time, sequenceStart, sequenceEnd))
                continue;
            if (!found) { chosen = track.Values[i]; found = true; }
            if (time > timeMs)
                break;
            chosen = track.Values[i];
        }
        return chosen;
    }

    /// <summary>计算 object id 到 War3 世界矩阵的映射，环状层级安全回退为局部矩阵。</summary>
    public static Dictionary<int, MdxTransform> ComputeWorldTransforms(IReadOnlyList<MdxNode> nodes, int timeMs, int sequenceStart = -1, int sequenceEnd = -1)
    {
        var result = new Dictionary<int, MdxTransform>();
        var pending = new List<MdxNode>(nodes);
        for (int guard = 0; pending.Count > 0 && guard <= nodes.Count + 1; guard++)
        {
            var next = new List<MdxNode>();
            foreach (MdxNode node in pending)
            {
                MdxTransform local = LocalTransform(node, timeMs, sequenceStart, sequenceEnd);
                if (node.ParentId >= 0)
                {
                    if (!result.TryGetValue(node.ParentId, out MdxTransform parent))
                    {
                        next.Add(node);
                        continue;
                    }
                    result[node.ObjectId] = MdxTransform.Combine(local, parent);
                }
                else
                    result[node.ObjectId] = local;
            }
            if (next.Count == pending.Count)
            {
                foreach (MdxNode node in next)
                    result[node.ObjectId] = LocalTransform(node, timeMs, sequenceStart, sequenceEnd);
                break;
            }
            pending = next;
        }
        return result;
    }

    /// <summary>根据四个骨骼影响蒙皮一个顶点。</summary>
    public static Vector3 SkinVertex(Vector3 bindPosition, IReadOnlyList<int> boneIds, IReadOnlyList<float> weights, int vertexIndex, IReadOnlyDictionary<int, MdxTransform> worlds)
    {
        Vector3 sum = Vector3.Zero;
        float total = 0;
        int offset = vertexIndex * 4;
        for (int i = 0; i < 4; i++)
        {
            if (offset + i >= boneIds.Count || offset + i >= weights.Count || weights[offset + i] <= 0.0001f)
                continue;
            if (!worlds.TryGetValue(boneIds[offset + i], out MdxTransform transform))
                continue;
            sum += transform.TransformPoint(bindPosition) * weights[offset + i];
            total += weights[offset + i];
        }
        return total <= 0.0001f ? bindPosition : (MathF.Abs(total - 1f) > 0.01f ? sum / total : sum);
    }

    /// <summary>根据四个骨骼影响蒙皮一个法线。</summary>
    public static Vector3 SkinNormal(Vector3 bindNormal, IReadOnlyList<int> boneIds, IReadOnlyList<float> weights, int vertexIndex, IReadOnlyDictionary<int, MdxTransform> worlds)
    {
        Vector3 sum = Vector3.Zero;
        float total = 0;
        int offset = vertexIndex * 4;
        for (int i = 0; i < 4; i++)
        {
            if (offset + i >= boneIds.Count || offset + i >= weights.Count || weights[offset + i] <= 0.0001f)
                continue;
            if (!worlds.TryGetValue(boneIds[offset + i], out MdxTransform transform))
                continue;
            sum += transform.TransformNormal(bindNormal) * weights[offset + i];
            total += weights[offset + i];
        }
        return total <= 0.0001f ? bindNormal : Vector3.Normalize(sum / total);
    }

    /// <summary>生成绕 pivot 旋转、缩放后再平移的节点局部矩阵。</summary>
    public static MdxTransform LocalTransform(MdxNode node, int timeMs, int sequenceStart = -1, int sequenceEnd = -1)
    {
        Vector3 translation = SampleVector3(node.Translation, timeMs, Vector3.Zero, sequenceStart, sequenceEnd);
        Quaternion rotation = SampleQuaternion(node.Rotation, timeMs, sequenceStart, sequenceEnd);
        Vector3 scale = SampleVector3(node.Scale, timeMs, Vector3.One, sequenceStart, sequenceEnd);
        Matrix4x4 matrix = Matrix4x4.CreateTranslation(-node.Pivot)
            * Matrix4x4.CreateScale(scale)
            * Matrix4x4.CreateFromQuaternion(rotation)
            * Matrix4x4.CreateTranslation(node.Pivot + translation);
        return new MdxTransform(matrix);
    }

    private static int FindFirstInRange(List<int> times, int start, int end)
    {
        for (int i = 0; i < times.Count; i++)
            if (InRange(times[i], start, end))
                return i;
        return -1;
    }

    private static int FindLastInRange(List<int> times, int start, int end)
    {
        for (int i = times.Count - 1; i >= 0; i--)
            if (InRange(times[i], start, end))
                return i;
        return -1;
    }

    private static bool InRange(int time, int start, int end) => start < 0 || end < 0 || (time >= start && time <= end);
}

/// <summary>War3 行向量矩阵包装，避免核心层依赖 Godot Transform3D。</summary>
public readonly struct MdxTransform
{
    public MdxTransform(Matrix4x4 matrix) => Matrix = matrix;
    public Matrix4x4 Matrix { get; }
    public Vector3 TransformPoint(Vector3 point) => Vector3.Transform(point, Matrix);
    public Vector3 TransformNormal(Vector3 normal) => Vector3.TransformNormal(normal, Matrix);
    public static MdxTransform Combine(MdxTransform local, MdxTransform parent) => new(local.Matrix * parent.Matrix);
}
