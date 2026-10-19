using System;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Windows.Data;

namespace TempHumiditySystem.Converters
{
    /// <summary>非 null → true（用于"未选中记录时禁用右侧表单"）。</summary>
    public sealed class NotNullToBoolConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value != null;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>枚举值 → 其 <see cref="DescriptionAttribute"/> 文案；没有描述时回退为枚举名。</summary>
    public sealed class EnumDescriptionConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value == null) return string.Empty;

            var field = value.GetType().GetField(value.ToString()!);
            var description = field?.GetCustomAttribute<DescriptionAttribute>();
            return description?.Description ?? value.ToString()!;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>
    /// 报警上下限的哨兵值转换：<see cref="double.MaxValue"/> / <see cref="double.MinValue"/> ↔ 空字符串。
    /// 参数 High 表示留空对应"不设上限"（MaxValue），Low 对应"不设下限"（MinValue）。
    /// </summary>
    public sealed class LimitConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is double d && (d == double.MaxValue || d == double.MinValue))
                return string.Empty;

            return value ?? string.Empty;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var text = (value as string)?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                return string.Equals(parameter as string, "High", StringComparison.OrdinalIgnoreCase)
                    ? double.MaxValue
                    : double.MinValue;
            }

            return double.TryParse(text, NumberStyles.Float, culture, out var result)
                ? result
                : Binding.DoNothing;
        }
    }
}
