using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ComicViewer.Models
{
    public class ComicViewModel
    {
        public ObservableCollection<ComicModel> Comics { get; } = new();
        public ObservableCollection<TagModel> UnselectedTags { get; } = new();
        public ObservableCollection<TagModel> SelectedTags { get; } = new();

        public bool HasSelectedTags => SelectedTags.Count > 0;
    }
}
