using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ComicViewer.Infrastructure
{
    public class ObservableTask<T> : INotifyPropertyChanged
    {
        private readonly Task<T> _task;

        // Public properties remain the same, they reflect the underlying task state
        public T? Result => _task.IsCompletedSuccessfully ? _task.Result : default;
        public bool IsCompleted => _task.IsCompleted;
        public bool IsFaulted => _task.IsFaulted;

        public ObservableTask(Task<T> task)
        {
            _task = task ?? throw new ArgumentNullException(nameof(task));

            // --- THE CRITICAL FIX ---
            // 1. Get the current SynchronizationContext (usually the UI thread)
            var scheduler = TaskScheduler.FromCurrentSynchronizationContext();

            // 2. Attach a continuation that executes regardless of success or failure.
            // This continuation is scheduled immediately and is guaranteed to run 
            // after the task finishes, whether synchronously or asynchronously.
            _task.ContinueWith(t =>
            {
                // Update all observable properties on the synchronization context
                OnPropertyChanged(nameof(Result));
                OnPropertyChanged(nameof(IsCompleted));
                OnPropertyChanged(nameof(IsFaulted));

                // Note: If you have IsSuccessfullyCompleted or Error properties, 
                // you should notify them here too.

            }, CancellationToken.None,
               TaskContinuationOptions.None, // No specific options needed
               scheduler); // Force the execution onto the UI/context thread
        }

        // Original WatchTaskAsync method is no longer needed/used.
        // private async Task WatchTaskAsync(Task task) { ... } 

        // INotifyPropertyChanged Implementation
        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            // This check is important: only invoke if there are subscribers
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
