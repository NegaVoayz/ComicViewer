using ComicViewer.Services.ComicViewer.Core;
using System.Collections.ObjectModel;

namespace ComicViewer.Models
{
    public class TagViewModel
    {
        public ReadOnlyObservableCollection<TagModel> UnselectedTags { get; }
        public ReadOnlyObservableCollection<TagModel> SelectedTags { get; }

        public TagViewModel(
            ReadOnlyObservableCollection<TagModel> unselectedTags,
            ReadOnlyObservableCollection<TagModel> selectedTags)
        {
            UnselectedTags = unselectedTags;
            SelectedTags = selectedTags;
        }
    }
}
