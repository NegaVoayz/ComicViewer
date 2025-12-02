using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ComicViewer.Services
{
    public class ObservableTask<T> : INotifyPropertyChanged
    {
        private readonly Task<T> _task;

        public ObservableTask(Task<T> task)
        {
            _task = task;

            if (!task.IsCompleted)
            {
                var _ = WatchTaskAsync(task);
            }
        }

        private async Task WatchTaskAsync(Task task)
        {
            try
            {
                await task;
            }
            catch { }

            OnPropertyChanged(nameof(Result));
            OnPropertyChanged(nameof(IsCompleted));
            OnPropertyChanged(nameof(IsFaulted));
        }

        public T Result => _task.IsCompletedSuccessfully ? _task.Result : default;
        public bool IsCompleted => _task.IsCompleted;
        public bool IsFaulted => _task.IsFaulted;

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
