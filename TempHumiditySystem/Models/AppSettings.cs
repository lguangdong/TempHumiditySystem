using System;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TempHumiditySystem.Models
{
    /// <summary>
    /// 应用级设置（与业务配置 <see cref="ModBus.SystemConfig"/> 解耦）：目前只记录 config.json 的存放目录。
    /// 自身固定保存在 exe 同目录的 settings.json，便于绿色部署（整个程序文件夹拷贝即可迁移）。
    /// 若 exe 同目录不可写，则降级到 %AppData%；两处都写失败时仅本次运行内存生效。
    /// </summary>
    public sealed class AppSettings
    {
        /// <summary>exe 同目录的 settings.json（首选位置）。</summary>
        public static string SettingsPath => Path.Combine(AppContext.BaseDirectory, "settings.json");

        /// <summary>exe 同目录不可写时的降级位置：%AppData%\TempHumiditySystem\settings.json。</summary>
        public static string FallbackSettingsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TempHumiditySystem", "settings.json");

        /// <summary>当前生效的设置实例（程序启动后首次访问时从磁盘加载）。</summary>
        public static AppSettings Current { get; private set; } = LoadFromDisk();

        /// <summary>重新从磁盘加载设置（外部改过 settings.json 后刷新）。</summary>
        public static void Reload() => Current = LoadFromDisk();

        /// <summary>用户所选的 config.json 存放目录；为空表示沿用默认目录（%AppData%\TempHumiditySystem）。</summary>
        public string? ConfigDirectory { get; set; }

        /// <summary>通信页上次使用的串口名（重启后恢复）。</summary>
        public string? CommPortName { get; set; }

        /// <summary>通信页上次使用的波特率。</summary>
        public int? CommBaudRate { get; set; }

        /// <summary>通信页上次使用的校验位。</summary>
        public Parity? CommParity { get; set; }

        /// <summary>通信页上次使用的停止位。</summary>
        public StopBits? CommStopBits { get; set; }

        /// <summary>最近一次导出 CSV 的完整路径（用于「打开最近导出目录」）。</summary>
        public string? LastExportPath { get; set; }

        /// <summary>最近一次保存是否成功落盘（false 表示仅本次运行内存生效）。</summary>
        [JsonIgnore]
        public bool LastSavePersisted { get; private set; } = true;

        /// <summary>最近一次落盘的实际路径（用于状态提示）。</summary>
        [JsonIgnore]
        public string? LastSavePath { get; private set; }

        /// <summary>统一用带 BOM 的 UTF-8 读写，避免记事本等工具把无 BOM 的 UTF-8 中文当成乱码。</summary>
        private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            // 让中文等非 ASCII 字符直接以 UTF-8 明文落盘，而不是转义成 \uXXXX
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            // 串口枚举以可读字符串保存（如 "None" / "One"），便于人工查看
            Converters = { new JsonStringEnumConverter() },
        };

        /// <summary>从磁盘加载设置；文件不存在或损坏时返回空设置（视为未做选择，回退默认目录）。</summary>
        private static AppSettings LoadFromDisk()
        {
            foreach (var path in new[] { SettingsPath, FallbackSettingsPath })
            {
                try
                {
                    if (!File.Exists(path))
                        continue;

                    var json = File.ReadAllText(path, Encoding.UTF8);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json, Options);
                    if (settings != null)
                        return settings;
                }
                catch
                {
                    // 读取失败或内容损坏：视为未设置，继续尝试下一个位置
                }
            }

            return new AppSettings();
        }

        /// <summary>
        /// 保存设置：优先写 exe 同目录，不可写则降级到 %AppData%；均失败时返回 false 且仅内存生效（不抛异常）。
        /// </summary>
        public bool Save()
        {
            LastSavePersisted = false;
            LastSavePath = null;

            string json;
            try
            {
                json = JsonSerializer.Serialize(this, Options);
            }
            catch
            {
                return false;
            }

            if (TryWrite(SettingsPath, json))
            {
                LastSavePersisted = true;
                LastSavePath = SettingsPath;
                return true;
            }

            if (TryWrite(FallbackSettingsPath, json))
            {
                LastSavePersisted = true;
                LastSavePath = FallbackSettingsPath;
                return true;
            }

            return false;
        }

        private static bool TryWrite(string path, string json)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(path, json, Utf8WithBom);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
