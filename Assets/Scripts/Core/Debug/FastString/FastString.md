# FastString (中文)

一个轻量高效的调试字符串构造工具，封装对象池 + Fluent API，降低 GC 压力。

## 核心特性
- Acquire / using / Dispose 语义
- Append 多类型 (int/float/bool/Vector/Color/Rect/Bounds/集合)
- 条件追加 If()
- RichText: Color / Bold / Italic / Tag
- 一键日志：Log / LogWarning / LogError (+AndRelease)
- ToStringAndRelease() 获取字符串并回收
- 对象池：超过 MaxRetainCapacity 的实例不回收

## 简单使用
```csharp
using var fs = FastString.Acquire()
    .Tag("AI")
    .T("Agent=").I(id).SP()
    .T("State=").T(stateName).SP()
    .T("Pos=").V(transform.position);

fs.Log();
```

## 扩展
添加自定义拼接：
```csharp
public static FastStringBuilder Ability(this FastStringBuilder fs, Ability a)
{
    return fs.T("Ability(").T(a.Id).C(':').T(a.DisplayName).C(')');
}
```

## 性能建议
- 不要在热路径内使用 string + string 拼接；改用 FastString。
- 大量重复字段推荐写专用扩展方法减少分支。
- 若要在 Release 包裁剪，可定义宏 FASTSTRING_DISABLE。

## 条件编译
- 使用 UNITY_EDITOR 或 DEVELOPMENT_BUILD 控制日志输出。
- 若需要在正式包仍启用，移除条件编译或添加自定义宏。
