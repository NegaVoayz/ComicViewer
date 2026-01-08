using System.Collections.ObjectModel;

namespace ComicViewer.Models
{
    /// <summary>
    /// 标签映射视图模型
    /// </summary>
    public class TagAliasViewModel
    {
        public ReadOnlyObservableCollection<TagAlias> SelectedTagAliases { get; }

        public TagAliasViewModel(
            ReadOnlyObservableCollection<TagAlias> selectedTagAliases)
        {
            SelectedTagAliases = selectedTagAliases;
        }
    }
}
