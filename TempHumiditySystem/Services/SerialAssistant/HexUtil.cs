using System;
using System.Globalization;
using System.Text;

namespace TempHumiditySystem.Services.SerialAssistant
{
    /// <summary>
    /// ASCII / HEX 转换工具。
    /// 目前只处理可打印 ASCII（0x20~0x7E），不支持中文与多字节编码。
    /// 将来要支持中文，只需修改 FromAscii / ToAscii 两处（换成 GBK 等编码）。
    /// </summary>
    public static class HexUtil
    {
        // ==================== 发送方向：文本 → 字节 ====================

        /// <summary>
        /// ASCII 文本 → 字节数组。例："AB" → [0x41, 0x42]
        /// 非 ASCII 字符会被替换成 '?'。
        /// </summary>
        public static byte[] FromAscii(string text)
            => Encoding.ASCII.GetBytes(text ?? string.Empty);

        /// <summary>
        /// HEX 文本 → 字节数组。
        /// 支持 "01 03 AB"、"0103AB"、"01,03,AB"、"01-03-AB"、"0x01 0x03"。
        /// </summary>
        public static bool TryFromHex(string text, out byte[] bytes, out string error)
        {
            bytes = [];
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(text))
            {
                error = "输入为空";
                return false;
            }

            // 去掉常见分隔符与 0x 前缀
            string cleaned = text
                .Replace(" ", string.Empty).Replace("\t", string.Empty)
                .Replace("\r", string.Empty).Replace("\n", string.Empty)
                .Replace(",", string.Empty).Replace("-", string.Empty).Replace(":", string.Empty)
                .Replace("0x", string.Empty).Replace("0X", string.Empty);

            if (cleaned.Length == 0)
            {
                error = "没有有效的 HEX 字符";
                return false;
            }

            if (cleaned.Length % 2 != 0)
            {
                error = "HEX 字符个数必须是偶数（两个字符一个字节）";
                return false;
            }

            var result = new byte[cleaned.Length / 2];
            for (int i = 0; i < result.Length; i++)
            {
                if (!byte.TryParse(cleaned.AsSpan(i * 2, 2),
                        NumberStyles.HexNumber, null, out result[i]))
                {
                    error = $"非法 HEX 字符：\"{cleaned.Substring(i * 2, 2)}\"";
                    return false;
                }
            }

            bytes = result;
            return true;
        }

        // ==================== 接收方向：字节 → 文本 ====================

        /// <summary>
        /// 字节 → ASCII 文本。0x80 以上的字节会自动变成 '?'。
        /// </summary>
        public static string ToAscii(byte[] data)
            => Encoding.ASCII.GetString(data);

        /// <summary>
        /// 字节 → "01 03 AB" 形式的十六进制文本。
        /// </summary>
        public static string ToHex(byte[] data)
            => BitConverter.ToString(data).Replace("-", " ");
    }
}
