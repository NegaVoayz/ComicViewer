using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace ComicViewer.Models
{
    public class ComicViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<ComicModel> Comics { get; } = new();
        public ObservableCollection<TagModel> UnselectedTags { get; } = new();
        public ObservableCollection<TagModel> SelectedTags { get; } = new();

        public bool HasSelectedTags => SelectedTags.Count > 0;

        private string _currentSaveDirectory;

        public string CurrentSaveDirectory
        {
            get => _currentSaveDirectory;
            set
            {
                if (_currentSaveDirectory != value)
                {
                    _currentSaveDirectory = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
