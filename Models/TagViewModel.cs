using ComicViewer.Services.ComicViewer.Core;
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
    public class TagViewModel
    {
        public ReadOnlyObservableCollection<TagModel> UnselectedTags { get; }
        public ReadOnlyObservableCollection<TagModel> SelectedTags { get; }
        public ObservableObject<string> CurrentSaveDirectory { get; } = Configs.GetFilePath();

        public TagViewModel(
            ReadOnlyObservableCollection<TagModel> unselectedTags,
            ReadOnlyObservableCollection<TagModel> selectedTags)
        {
            UnselectedTags = unselectedTags;
            SelectedTags = selectedTags;
        }
    }
}
