using System.Reflection.Metadata;
using System.Windows;
using System.Windows.Threading;

namespace ComicViewer.Services
{

    /// <summary>
    /// 防抖器
    /// </summary>
    public class Debouncer
    {
        private DispatcherTimer _timer;
        private Action _action;
        private readonly Dispatcher _dispatcher;
        private readonly int _delayMilliseconds;

        public Debouncer(int delayMilliseconds, Action? action = default, Dispatcher dispatcher = null)
        {
            _delayMilliseconds = delayMilliseconds;
            if (action != null)
                _action = action;
            _dispatcher = dispatcher ?? Application.Current.Dispatcher;
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(delayMilliseconds)
            };
            _timer.Tick += OnTimerTick;
        }

        public void Debounce(Action? action = default)
        {
            if (action != null)
                _action = action;
            _timer.Stop();
            _timer.Start();
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            _timer.Stop();
            _action?.Invoke();
        }

        public void Dispose()
        {
            _timer.Stop();
            _timer.Tick -= OnTimerTick;
        }
    }

    /// <summary>
    /// 异步防抖器
    /// </summary>
    public class AsyncDebouncer
    {
        private CancellationTokenSource _cancellationTokenSource;
        private readonly int _delayMilliseconds;

        public AsyncDebouncer(int delayMilliseconds)
        {
            _delayMilliseconds = delayMilliseconds;
        }

        public async Task DebounceAsync(Func<Task> asyncAction)
        {
            // 取消之前的任务
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource = new CancellationTokenSource();

            var currentToken = _cancellationTokenSource.Token;

            try
            {
                // 等待防抖延迟
                await Task.Delay(_delayMilliseconds, currentToken);

                // 执行异步操作
                if (!currentToken.IsCancellationRequested)
                {
                    await asyncAction();
                }
            }
            catch (TaskCanceledException)
            {
                // 正常取消，忽略
            }
        }

        public void Cancel()
        {
            _cancellationTokenSource?.Cancel();
        }

        public void Dispose()
        {
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
        }
    }

    /// <summary>
    /// 泛型防抖器（支持参数传递）
    /// </summary>
    public class Debouncer<T>
    {
        private DispatcherTimer _timer;
        private Action<T> _action;
        private T _lastParameter;
        private readonly Dispatcher _dispatcher;
        private readonly int _delayMilliseconds;

        public Debouncer(int delayMilliseconds, Action<T>? action = default, T parameter = default, Dispatcher dispatcher = null)
        {
            _delayMilliseconds = delayMilliseconds;
            if (action != null)
                _action = action;
            _lastParameter = parameter;
            _dispatcher = dispatcher ?? Application.Current.Dispatcher;
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(delayMilliseconds)
            };
            _timer.Tick += OnTimerTick;
        }

        public void Debounce(T parameter, Action<T> action = default)
        {
            _lastParameter = parameter;
            if (action != null)
                _action = action;
            _timer.Stop();
            _timer.Start();
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            _timer.Stop();
            _action?.Invoke(_lastParameter);
        }

        public void Dispose()
        {
            _timer.Stop();
            _timer.Tick -= OnTimerTick;
        }
    }

    /// <summary>
    /// 异步泛型防抖器
    /// </summary>
    public class AsyncDebouncer<T>
    {
        private CancellationTokenSource _cancellationTokenSource;
        private readonly int _delayMilliseconds;
        private T _lastParameter;

        public AsyncDebouncer(int delayMilliseconds, T parameter = default)
        {
            _delayMilliseconds = delayMilliseconds;
            _lastParameter = parameter;
        }

        public async Task DebounceAsync(T parameter, Func<T, Task> asyncAction)
        {
            // 取消之前的任务
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource = new CancellationTokenSource();

            _lastParameter = parameter;
            var currentToken = _cancellationTokenSource.Token;

            try
            {
                // 等待防抖延迟
                await Task.Delay(_delayMilliseconds, currentToken);

                // 执行异步操作
                if (!currentToken.IsCancellationRequested)
                {
                    await asyncAction(_lastParameter);
                }
            }
            catch (TaskCanceledException)
            {
                // 正常取消，忽略
            }
        }
    }
}
