using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TempHumiditySystem.Models.ModBus
{
    /// <summary>
    /// <see cref="ModbusDataType"/> 的容错转换器：
    /// 读取到未知/已移除的枚举名（例如旧配置文件里的 Float32）时回退到 <see cref="ModbusDataType.UInt16"/>，
    /// 避免因为一个字段反序列化失败就把整份配置判定为"损坏"并生成示例配置。
    /// </summary>
    public sealed class ModbusDataTypeConverter : JsonConverter<ModbusDataType>
    {
        public override ModbusDataType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return ModbusDataType.UInt16;

            if (reader.TokenType == JsonTokenType.Number)
            {
                return reader.TryGetInt32(out var number) && Enum.IsDefined(typeof(ModbusDataType), number)
                    ? (ModbusDataType)number
                    : ModbusDataType.UInt16;
            }

            var text = reader.GetString();
            return Enum.TryParse<ModbusDataType>(text, ignoreCase: true, out var value)
                ? value
                : ModbusDataType.UInt16;
        }

        public override void Write(Utf8JsonWriter writer, ModbusDataType value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.ToString());
    }
}
