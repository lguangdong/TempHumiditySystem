using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using TempHumiditySystem.Models.ModBus;

namespace TempHumiditySystem.ViewModels.Editing
{
    /// <summary>
    /// 设备编辑模型：只包含可编辑字段（不含 Mode / IsRunning 等运行时状态）。
    /// </summary>
    public sealed partial class DeviceEditModel : EditModelBase
    {
        [ObservableProperty]
        [NotifyDataErrorInfo]
        [Required(ErrorMessage = "名称不能为空")]
        [MaxLength(32, ErrorMessage = "名称最多 32 个字符")]
        private string _name = "";

        [ObservableProperty]
        private DeviceType _type = DeviceType.Heater;

        [ObservableProperty]
        [NotifyDataErrorInfo]
        [Range(1, 247, ErrorMessage = "从站地址需在 1~247 之间")]
        private byte _slaveId = 1;

        [ObservableProperty]
        [NotifyDataErrorInfo]
        [Range(0, 65535, ErrorMessage = "写寄存器地址需在 0~65535 之间")]
        private ushort _writeAddress;

        [ObservableProperty]
        private string _linkedSensorName = "";

        [ObservableProperty]
        [NotifyDataErrorInfo]
        [Range(-1000000d, 1000000d, ErrorMessage = "开启值超出范围")]
        private double _onAt;

        [ObservableProperty]
        [NotifyDataErrorInfo]
        [Range(-1000000d, 1000000d, ErrorMessage = "关闭值超出范围")]
        private double _offAt;

        /// <summary>true = 低于开启值启动（升温类）；false = 高于开启值启动（降温/除湿类）。</summary>
        [ObservableProperty]
        private bool _turnsOnBelow = true;

        /// <summary>转换为运行时/持久化模型。</summary>
        public Device ToModel() => new()
        {
            Name = Name.Trim(),
            Type = Type,
            SlaveId = SlaveId,
            WriteAddress = WriteAddress,
            LinkedSensorName = LinkedSensorName.Trim(),
            OnAt = OnAt,
            OffAt = OffAt,
            TurnsOnBelow = TurnsOnBelow,
        };

        /// <summary>从配置文件加载的模型创建编辑对象。</summary>
        public static DeviceEditModel FromModel(Device d) => new()
        {
            Name = d.Name,
            Type = d.Type,
            SlaveId = d.SlaveId,
            WriteAddress = d.WriteAddress,
            LinkedSensorName = d.LinkedSensorName,
            OnAt = d.OnAt,
            OffAt = d.OffAt,
            TurnsOnBelow = d.TurnsOnBelow,
        };
    }
}
