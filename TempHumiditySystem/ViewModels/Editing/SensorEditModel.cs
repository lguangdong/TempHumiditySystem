using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using TempHumiditySystem.Models.ModBus;

namespace TempHumiditySystem.ViewModels.Editing
{
    /// <summary>
    /// 测点编辑模型：只包含可编辑字段，与运行时对象 <see cref="SensorPoint"/> 显式互转。
    /// 独立成类可以避免配置页直接改到运行中的对象，也让校验规则集中在一处。
    /// </summary>
    public sealed partial class SensorEditModel : EditModelBase
    {
        [ObservableProperty]
        [NotifyDataErrorInfo]
        [Required(ErrorMessage = "名称不能为空")]
        [MaxLength(32, ErrorMessage = "名称最多 32 个字符")]
        private string _name = "";

        [ObservableProperty]
        private string _groupName = "";

        [ObservableProperty]
        [NotifyDataErrorInfo]
        [Range(1, 247, ErrorMessage = "从站地址需在 1~247 之间")]
        private byte _slaveId = 1;

        [ObservableProperty]
        [NotifyDataErrorInfo]
        [Range(0, 65535, ErrorMessage = "寄存器地址需在 0~65535 之间")]
        private ushort _registerAddress;

        [ObservableProperty]
        private ModbusDataType _dataType = ModbusDataType.Int16;

        [ObservableProperty]
        [NotifyDataErrorInfo]
        [Range(0.000001d, 1000000d, ErrorMessage = "系数需在 0.000001~1000000 之间")]
        private double _scale = 0.1;

        [ObservableProperty]
        private string _unit = "℃";

        [ObservableProperty]
        private SensorCategory _category = SensorCategory.Temperature;

        [ObservableProperty]
        [NotifyDataErrorInfo]
        [Range(-1000000d, 1000000d, ErrorMessage = "报警上限超出范围")]
        private double _highLimit = 35;

        [ObservableProperty]
        [NotifyDataErrorInfo]
        [Range(-1000000d, 1000000d, ErrorMessage = "报警下限超出范围")]
        private double _lowLimit = 0;

        /// <summary>转换为运行时/持久化模型。</summary>
        public SensorPoint ToModel() => new()
        {
            Name = Name.Trim(),
            GroupName = GroupName.Trim(),
            SlaveId = SlaveId,
            RegisterAddress = RegisterAddress,
            DataType = DataType,
            Scale = Scale,
            Unit = Unit.Trim(),
            Category = Category,
            HighLimit = HighLimit,
            LowLimit = LowLimit,
        };

        /// <summary>从配置文件加载的模型创建编辑对象。</summary>
        public static SensorEditModel FromModel(SensorPoint s) => new()
        {
            Name = s.Name,
            GroupName = s.GroupName,
            SlaveId = s.SlaveId,
            RegisterAddress = s.RegisterAddress,
            DataType = s.DataType,
            Scale = s.Scale,
            Unit = s.Unit,
            Category = s.Category,
            HighLimit = s.HighLimit,
            LowLimit = s.LowLimit,
        };
    }
}
