using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ComicViewer.Infrastructure
{
    public class ObservableTask<T> : INotifyPropertyChanged
    {
        private readonly Task<T> _task;
        private readonly object _lock = new object();
        private PropertyChangedEventArgs _resultArgs;
        private PropertyChangedEventArgs _completedArgs;
        private PropertyChangedEventArgs _faultedArgs;

        // Public properties. they reflect the underlying task state
        public T? Result => _task.IsCompletedSuccessfully ? _task.Result : default;
        public bool IsCompleted => _task.IsCompleted;
        public bool IsFaulted => _task.IsFaulted;

        public ObservableTask(Task<T> task)
        {
            _task = task;
            _resultArgs = new PropertyChangedEventArgs(nameof(Result));
            _completedArgs = new PropertyChangedEventArgs(nameof(IsCompleted));
            _faultedArgs = new PropertyChangedEventArgs(nameof(IsFaulted));
            // 1. Get the current SynchronizationContext (usually the UI thread)
            var scheduler = SynchronizationContext.Current != null
                ? TaskScheduler.FromCurrentSynchronizationContext()
                : TaskScheduler.Current;

            // 2. Attach a continuation that executes regardless of success or failure.
            // This continuation is scheduled immediately and is guaranteed to run 
            // after the task finishes, whether synchronously or asynchronously.
            _task.ContinueWith(t =>
            {
                // Update all observable properties on the synchronization context
                _propertyChanged?.Invoke(this, _resultArgs);
                _propertyChanged?.Invoke(this, _completedArgs);
                _propertyChanged?.Invoke(this, _faultedArgs);

                // Note: If you have IsSuccessfullyCompleted or Error properties, 
                // you should notify them here too.

            }, CancellationToken.None,
               TaskContinuationOptions.None, // No specific options needed
               scheduler); // Force the execution onto the UI/context thread
        }

        // INotifyPropertyChanged Implementation
        private event PropertyChangedEventHandler? _propertyChanged;
        public event PropertyChangedEventHandler? PropertyChanged
        {
            add
            {
                lock (_lock)
                {
                    // 添加订阅者
                    _propertyChanged += value;

                    // 如果任务已完成且配置为立即通知
                    if (_task.IsCompleted && value != null)
                    {
                        // 立即通知新订阅者
                        value(this, _resultArgs);
                        value(this, _completedArgs);
                        value(this, _faultedArgs);
                    }
                }
            }
            remove
            {
                lock (_lock)
                {
                    _propertyChanged -= value;
                }
            }
        }
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            // This check is important: only invoke if there are subscribers
            _propertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
    public class ObservableTask : INotifyPropertyChanged
    {
        private readonly Task _task;
        private readonly object _lock = new object();
        private PropertyChangedEventArgs _completedArgs;
        private PropertyChangedEventArgs _faultedArgs;

        // Public properties. they reflect the underlying task state
        public bool IsCompleted => _task.IsCompleted;
        public bool IsFaulted => _task.IsFaulted;

        public ObservableTask(Task task)
        {
            _task = task;
            _completedArgs = new PropertyChangedEventArgs(nameof(IsCompleted));
            _faultedArgs = new PropertyChangedEventArgs(nameof(IsFaulted));
            // 1. Get the current SynchronizationContext (usually the UI thread)
            var scheduler = SynchronizationContext.Current != null
                ? TaskScheduler.FromCurrentSynchronizationContext()
                : TaskScheduler.Current;

            // 2. Attach a continuation that executes regardless of success or failure.
            // This continuation is scheduled immediately and is guaranteed to run 
            // after the task finishes, whether synchronously or asynchronously.
            _task.ContinueWith(t =>
            {
                // Update all observable properties on the synchronization context
                _propertyChanged?.Invoke(this, _completedArgs);
                _propertyChanged?.Invoke(this, _faultedArgs);

                // Note: If you have IsSuccessfullyCompleted or Error properties, 
                // you should notify them here too.

            }, CancellationToken.None,
               TaskContinuationOptions.None, // No specific options needed
               scheduler); // Force the execution onto the UI/context thread
        }

        // INotifyPropertyChanged Implementation
        private event PropertyChangedEventHandler? _propertyChanged;
        public event PropertyChangedEventHandler? PropertyChanged
        {
            add
            {
                lock (_lock)
                {
                    // 添加订阅者
                    _propertyChanged += value;

                    // 如果任务已完成且配置为立即通知
                    if (_task.IsCompleted && value != null)
                    {
                        // 立即通知新订阅者
                        value(this, _completedArgs);
                        value(this, _faultedArgs);
                    }
                }
            }
            remove
            {
                lock (_lock)
                {
                    _propertyChanged -= value;
                }
            }
        }
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            // This check is important: only invoke if there are subscribers
            _propertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
