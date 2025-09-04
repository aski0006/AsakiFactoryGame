using System;

namespace Game.CSV
{
    /// <summary>
    /// 通用 CSV 自定义类型转换器接口：单个单元格 <-> 一个对象实例。
    /// </summary>
    public interface ICsvTypeConverter
    {
        Type TargetType { get; }
        string Serialize(object value);
        bool TryDeserialize(string text, out object value);
    }

    /// <summary>
    /// 泛型帮助基类，减少装箱/拆箱样板。
    /// </summary>
    public abstract class CsvTypeConverter<T> : ICsvTypeConverter
    {
        public Type TargetType => typeof(T);

        public string Serialize(object value)
        {
            if (value is T t) return SerializeTyped(t) ?? string.Empty;
            return string.Empty;
        }

        public bool TryDeserialize(string text, out object value)
        {
            if (TryDeserializeTyped(text, out var v))
            {
                value = v;
                return true;
            }
            value = default;
            return false;
        }

        protected abstract string SerializeTyped(T value);
        protected abstract bool TryDeserializeTyped(string text, out T value);
    }
}
