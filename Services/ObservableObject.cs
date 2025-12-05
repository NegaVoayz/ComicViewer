namespace ComicViewer.Services
{
    using System.ComponentModel;
    using System.Runtime.CompilerServices;

    namespace ComicViewer.Core
    {
        /// <summary>
        /// 可观察对象包装器，用于包装任何类型并支持属性更改通知
        /// </summary>
        /// <typeparam name="T">被包装的类型</typeparam>
        public class ObservableObject<T> : INotifyPropertyChanged
        {
            private T _value;

            /// <summary>
            /// 包装的值
            /// </summary>
            public T Value
            {
                get => _value;
                set
                {
                    if (!Equals(_value, value))
                    {
                        _value = value;
                        OnPropertyChanged(nameof(Value));
                    }
                }
            }

            /// <summary>
            /// 隐式转换，可以直接赋值 T 类型
            /// </summary>
            public static implicit operator T(ObservableObject<T> obj) => obj.Value;

            /// <summary>
            /// 隐式转换，可以将 T 转换为 ObservableObject<T>
            /// </summary>
            public static implicit operator ObservableObject<T>(T value)
            {
                return new ObservableObject<T> { Value = value };
            }

            public override string ToString()
            {
                return Value?.ToString() ?? string.Empty;
            }

            public event PropertyChangedEventHandler PropertyChanged;

            protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
