using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading.Tasks;

namespace TempHumiditySystem.Services.SerialAssistant
{
    public static class SerialPortUtil
    {
        public static IReadOnlyList<SerialPortInfo> GetPorts()
        {
            var list = new List<SerialPortInfo>();
            try
            {
                var searcher = new ManagementObjectSearcher("SELECT Caption FROM Win32_PnPEntity WHERE Caption LIKE '%(COM%'");
                var result = searcher.Get();
                foreach (var item in result)
                {
                    var caption = item["Caption"]?.ToString();
                    if (string.IsNullOrEmpty(caption)) continue;
                    int idx = caption.LastIndexOf("(COM");
                    if (idx == -1) continue;
                    int endIdx = caption.IndexOf(')', idx);
                    if (endIdx == -1) continue;
                    var com = caption.Substring(idx + 1, endIdx - idx - 1).Trim();
                    var desc = caption[..idx].Trim();
                    list.Add(new SerialPortInfo() { PortName = com, Description = desc });
                }
            }
            catch (Exception)
            {

                //wmi 获取失败
            }

            var know = new HashSet<string>(list.Select(p => p.PortName), StringComparer.OrdinalIgnoreCase);
            foreach (var item in SerialPort.GetPortNames())
            {
                if (know.Add(item)) list.Add(new SerialPortInfo() { PortName = item, Description = item });
            }
            
            return list;
        }
    }
}
