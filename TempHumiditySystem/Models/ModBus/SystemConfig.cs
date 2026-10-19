using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TempHumiditySystem.Models.ModBus
{
    /// <summary>
    /// 系统配置：测点 + 设备。支持从 JSON 文件加载/保存。
    /// </summary>
    public sealed class SystemConfig
    {
        public List<SensorPoint> Sensors { get; set; } = new();
        public List<Device> Devices { get; set; } = new();

        /// <summary>默认回退路径：%AppData%\TempHumiditySystem\config.json（用户未选择目录时使用）。</summary>
        public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TempHumiditySystem", "config.json");

        /// <summary>配置文件名（固定，用户只选目录）。</summary>
        public const string FileName = "config.json";

        /// <summary>
        /// 解析实际使用的配置文件路径：优先用户在参数配置页选定的目录，未选择时回退到 <see cref="DefaultPath"/>。
        /// 所选目录非法或为空时同样回退，保证任何时候都能拿到可用路径。
        /// </summary>
        public static string ResolvePath()
        {
            var dir = AppSettings.Current.ConfigDirectory;
            if (!string.IsNullOrWhiteSpace(dir))
            {
                try
                {
                    return Path.Combine(dir.Trim(), FileName);
                }
                catch
                {
                    // 路径含非法字符等：回退默认路径
                }
            }

            return DefaultPath;
        }

        /// <summary>当前生效的配置文件所在目录（用于界面展示）。</summary>
        public static string CurrentDirectory => Path.GetDirectoryName(ResolvePath()) ?? ResolvePath();


        /// <summary>统一用带 BOM 的 UTF-8 读写，避免记事本等工具把无 BOM 的 UTF-8 中文当成乱码。</summary>
        private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            // 反序列化时忽略字段名大小写（Sensors/sensors 都能匹配），避免字段未匹配导致集合被静默跳过为空
            PropertyNameCaseInsensitive = true,
            // 让中文等非 ASCII 字符直接以 UTF-8 明文落盘，而不是转义成 \uXXXX
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Converters = { new JsonStringEnumConverter() },
        };

        /// <summary>加载配置；文件不存在或损坏时，用示例配置生成并落盘。</summary>
        public static SystemConfig LoadOrCreate(string? path = null)
        {
            path ??= ResolvePath();
            try
            {
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path, Encoding.UTF8);
                    var cfg = JsonSerializer.Deserialize<SystemConfig>(json, Options);
                    if (cfg != null)
                        return cfg;
                }
            }
            catch
            {
                // 配置损坏时回退到示例配置
            }

            var sample = CreateSample();
            sample.Save(path);
            return sample;
        }

        /// <summary>保存到 JSON 文件。</summary>
        public void Save(string? path = null)
        {
            path ??= ResolvePath();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(this, Options);
            File.WriteAllText(path, json, Utf8WithBom);
        }


        /// <summary>创建示例配置：3 个传感器（6 测点）+ 4 类设备。</summary>
        public static SystemConfig CreateSample()
        {
            var cfg = new SystemConfig();

            AddSensor(cfg, "1#仓库温度", "1#仓库", 1, 0x0000, ModbusDataType.Int16,  0.1, "℃",   SensorCategory.Temperature, 35, 25);
            AddSensor(cfg, "1#仓库湿度", "1#仓库", 1, 0x0001, ModbusDataType.UInt16, 0.1, "%RH", SensorCategory.Humidity,    70, 50);
            //AddSensor(cfg, "2#仓库温度", "2#仓库", 2, 0x0000, ModbusDataType.Int16, 0.1, "℃", SensorCategory.Temperature, 10, 0);
            //AddSensor(cfg, "2#仓库湿度", "2#仓库", 2, 0x0001, ModbusDataType.UInt16, 0.1, "%RH", SensorCategory.Humidity, 80, 70);
            //AddSensor(cfg, "3#车间温度", "3#车间", 3, 0x0000, ModbusDataType.Int16, 0.1, "℃", SensorCategory.Temperature, 25, 20);
            //AddSensor(cfg, "3#车间湿度", "3#车间", 3, 0x0001, ModbusDataType.UInt16, 0.1, "%RH", SensorCategory.Humidity, 50, 45);

            AddDevice(cfg, "加热器1",  DeviceType.Heater,       1, 0x0100, "1#仓库温度", 25, 35, turnsOnBelow: true);
            AddDevice(cfg, "加湿器1",  DeviceType.Humidifier,   1, 0x0101, "1#仓库湿度", 50, 70, turnsOnBelow: true);
            AddDevice(cfg, "除湿机1",  DeviceType.Dehumidifier, 1, 0x0102, "1#仓库湿度", 70, 50, turnsOnBelow: false);
            AddDevice(cfg, "风机1",    DeviceType.Fan,         1, 0x0103, "1#仓库温度", 35, 25, turnsOnBelow: false);

            return cfg;
        }

        private static void AddSensor(SystemConfig cfg, string name, string groupName, byte slave, ushort addr,
            ModbusDataType type, double scale, string unit, SensorCategory cat, double high, double low)
        {
            cfg.Sensors.Add(new SensorPoint
            {
                Name = name, SlaveId = slave, RegisterAddress = addr,
                DataType = type, Scale = scale, Unit = unit,
                Category = cat, HighLimit = high, LowLimit = low, GroupName = groupName,
            });
        }

        private static void AddDevice(SystemConfig cfg, string name, DeviceType type, byte slave,
            ushort addr, string linkedSensor, double onAt, double offAt, bool turnsOnBelow)
        {
            cfg.Devices.Add(new Device
            {
                Name = name, Type = type, SlaveId = slave, WriteAddress = addr,
                LinkedSensorName = linkedSensor,
                OnAt = onAt, OffAt = offAt, TurnsOnBelow = turnsOnBelow,
            });
        }
    }
}
