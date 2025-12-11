using ComicViewer.Services.ComicViewer.Core;
using System.Collections.ObjectModel;

namespace ComicViewer.Models
{
    public class TagViewModel
    {
        public ReadOnlyObservableCollection<TagData> UnselectedTags { get; }
        public ReadOnlyObservableCollection<TagData> SelectedTags { get; }

        public TagViewModel(
            ReadOnlyObservableCollection<TagData> unselectedTags,
            ReadOnlyObservableCollection<TagData> selectedTags)
        {
            UnselectedTags = unselectedTags;
            SelectedTags = selectedTags;
        }
    }
}
