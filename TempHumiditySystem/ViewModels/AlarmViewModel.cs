using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TempHumiditySystem.Models.ModBus;
using TempHumiditySystem.Services.Modbus;

namespace TempHumiditySystem.ViewModels
{
    /// <summary>报警记录页：活跃报警 + 历史报警 + 确认。</summary>
    public partial class AlarmViewModel : ObservableObject
    {
        private readonly AlarmEngine _alarm;

        public ObservableCollection<Alarm> ActiveAlarms { get; }
        public ObservableCollection<Alarm> HistoryAlarms { get; }

        public AlarmViewModel(AlarmEngine alarm)
        {
            _alarm = alarm;
            ActiveAlarms = alarm.ActiveAlarms;
            HistoryAlarms = alarm.HistoryAlarms;
        }

        [RelayCommand]
        private void Acknowledge(Alarm alarm) => _alarm.Acknowledge(alarm);

        [RelayCommand]
        private void AcknowledgeAll() => _alarm.AcknowledgeAll();
    }
}
