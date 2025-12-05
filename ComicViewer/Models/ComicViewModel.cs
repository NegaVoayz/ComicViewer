using ComicViewer.Services.ComicViewer.Core;
using System.Collections.ObjectModel;

namespace ComicViewer.Models
{
    public class ComicViewModel
    {
        public ReadOnlyObservableCollection<ComicModel> Comics { get; }
        public ReadOnlyObservableCollection<TagModel> UnselectedTags { get; }
        public ReadOnlyObservableCollection<TagModel> SelectedTags { get; }
        public ObservableObject<string> CurrentSaveDirectory { get; } = Configs.GetFilePath();

        public ComicViewModel(
            ReadOnlyObservableCollection<ComicModel> comics,
            ReadOnlyObservableCollection<TagModel> unselectedTags,
            ReadOnlyObservableCollection<TagModel> selectedTags)
        {
            Comics = comics;
            UnselectedTags = unselectedTags;
            SelectedTags = selectedTags;
        }
    }
}
