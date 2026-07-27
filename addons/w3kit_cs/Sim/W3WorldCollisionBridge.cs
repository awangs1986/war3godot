using System.Collections.Generic;
using Godot;
using CoreCollision = W3Compat.Sim.W3WorldCollision;

namespace Diablo1Like.W3.Sim;

/// <summary>
/// 动态单位碰撞索引的 Godot 桥，迁移自 addons/w3kit/sim/w3_world_collision.gd。
/// 均匀空间哈希算法委托给 W3Compat.Core 的 W3WorldCollision（与 Warsmash
/// CWorldCollision 语义一致，只处理地面单位层），这里只做 Godot Vector2 与
/// 核心 (float,float) 元组之间的转换，供 GDScript 工具与 C# 模拟层共用。
/// </summary>
[GlobalClass]
public partial class W3WorldCollisionBridge : RefCounted
{
    private readonly CoreCollision _core = new();

    public void add_unit(W3SimUnit unit) => _core.AddUnit(unit);

    public void remove_unit(W3SimUnit unit) => _core.RemoveUnit(unit);

    /// <summary>Warsmash CWorldCollision.translate：低成本增量移动。</summary>
    public void translate_unit(W3SimUnit unit, Vector2 oldMin, Vector2 oldMax) =>
        _core.TranslateUnit(unit, (oldMin.X, oldMin.Y), (oldMax.X, oldMax.Y));

    /// <summary>Warsmash CWorldCollision.intersectsAnythingOtherThan（地面层）。</summary>
    public bool intersects_anything_other_than(Vector2 rectMin, Vector2 rectMax, W3SimUnit? ignore = null) =>
        _core.IntersectsAnythingOtherThan((rectMin.X, rectMin.Y), (rectMax.X, rectMax.Y), ignore);

    /// <summary>以 (x, y) 为圆心、radius 半径内的存活单位（排除 ignore）；GDScript 工具用。</summary>
    public Godot.Collections.Array<W3SimUnit> enum_units_in_range(float x, float y, float radius, W3SimUnit? ignore = null)
    {
        var result = new Godot.Collections.Array<W3SimUnit>();
        foreach (var unit in _core.EnumUnitsInRange(x, y, radius, ignore))
            result.Add((W3SimUnit)unit);
        return result;
    }

    /// <summary>C# 内部使用：返回原生 List，避免 Godot.Collections.Array 装箱开销。</summary>
    internal List<W3SimUnit> EnumUnitsInRangeNative(float x, float y, float radius, W3SimUnit? ignore)
    {
        List<W3Compat.Sim.ISimCollisionUnit> list = _core.EnumUnitsInRange(x, y, radius, ignore);
        var result = new List<W3SimUnit>(list.Count);
        foreach (var unit in list)
            result.Add((W3SimUnit)unit);
        return result;
    }
}
