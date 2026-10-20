using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TempHumiditySystem.ViewModels.Editing
{
    /// <summary>
    /// 配置编辑模型基类：
    /// 单字段校验用 DataAnnotations（<see cref="ObservableValidator"/> 自动执行），
    /// 跨行校验（重名、地址冲突、迟滞关系等）由 <c>ConfigViewModel</c> 写入 <see cref="ExternalError"/>。
    /// </summary>
    public abstract partial class EditModelBase : ObservableValidator
    {
        /// <summary>跨行校验结果（只用于列表行提示，不参与序列化）。</summary>
        [ObservableProperty]
        private string? _externalError;

        /// <summary>该行是否存在任何错误（字段级或跨行）。</summary>
        public bool HasError => HasErrors || !string.IsNullOrEmpty(ExternalError);

        /// <summary>
        /// 该行所有错误的合并文本（字段级 + 跨行），用于鼠标悬停提示。
        /// 单字段校验结果来自 <see cref="ObservableValidator"/>，跨行结果来自 <see cref="ExternalError"/>。
        /// </summary>
        public string ErrorText
        {
            get
            {
                var messages = new List<string>();
                foreach (var result in GetErrors(null).OfType<ValidationResult>())
                {
                    var message = result.ErrorMessage;
                    if (!string.IsNullOrWhiteSpace(message) && !messages.Contains(message)) messages.Add(message);
                }

                if (!string.IsNullOrWhiteSpace(ExternalError) && !messages.Contains(ExternalError!))
                    messages.Add(ExternalError!);

                return string.Join("；", messages);
            }
        }

        protected EditModelBase()
        {
            // 字段级校验结果变化时，同步刷新列表行上的错误标记与悬停提示
            ErrorsChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(HasError));
                OnPropertyChanged(nameof(ErrorText));
            };
        }

        partial void OnExternalErrorChanged(string? value)
        {
            OnPropertyChanged(nameof(HasError));
            OnPropertyChanged(nameof(ErrorText));
        }

        /// <summary>执行全部字段级校验（基类的 ValidateAllProperties 是 protected，这里开放给页面 VM）。</summary>
        public void ValidateFields() => ValidateAllProperties();

        /// <summary>清空跨行错误（每次全量校验前调用）。</summary>
        public void ClearExternalError() => ExternalError = null;

        /// <summary>追加一条跨行错误，多条用"；"分隔。</summary>
        public void AppendExternalError(string message)
            => ExternalError = string.IsNullOrEmpty(ExternalError) ? message : $"{ExternalError}；{message}";
    }
}
