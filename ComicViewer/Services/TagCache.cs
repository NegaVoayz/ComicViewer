using ComicViewer.Models;
using DynamicData;
using ReactiveUI;
using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Windows.Input;

namespace ComicViewer.Services
{
    public class TagCache
    {
        private readonly ComicService service;
        private readonly string comicKey;

        // DynamicData 数据源
        private readonly SourceList<TagModel> _tagsSource = new();
        private readonly SourceList<TagModel> _selectedTagsSet = new();

        // 可观察的状态
        private readonly BehaviorSubject<string> _searchTagNameSubject = new(string.Empty);

        // ViewModel 绑定的集合
        private readonly ReadOnlyObservableCollection<TagModel> _unselectedTags;
        private readonly ReadOnlyObservableCollection<TagModel> _selectedTags;

        public TagViewModel ViewModel { get; }

        public TagCache(ComicService service, string comicKey)
        {
            this.service = service;
            this.comicKey = comicKey;

            // 初始化数据流

            // 未选中的标签（按名称过滤）
            _tagsSource.Connect()
                .Filter(_searchTagNameSubject.Select(CreateTagNameFilter))
                .Except(_selectedTagsSet.Connect())
                .Bind(out _unselectedTags)
                .Subscribe();

            // 已选中的标签
            _selectedTagsSet.Connect()
                .Bind(out _selectedTags)
                .Subscribe();

            SelectTagCommand = new RelayCommand<string>(tagKey => SelectTag(tagKey));
            DeselectTagCommand = new RelayCommand<string>(tagKey => DeselectTag(tagKey));

            // 创建 ViewModel
            ViewModel = new(_unselectedTags, _selectedTags);
        }

        public async Task InitializeAsync()
        {
            var tags = await service.DataService.GetAllTagsAsync();
            var comicTagKeys = (await service.DataService.GetTagsOfComic(comicKey)).Select(e => e.Key).ToHashSet();

            // 使用 Edit() 进行批量操作，避免多次事件触发
            _tagsSource.Edit(list =>
            {
                list.Clear();
                list.AddRange(tags);
            });

            var selectedTags = tags.Where(e => comicTagKeys.Contains(e.Key));

            _selectedTagsSet.Edit(list =>
            {
                list.Clear();
                list.AddRange(selectedTags);
            });
        }

        private Func<TagModel, bool> CreateTagNameFilter(string searchTagName)
        {
            return tag => string.IsNullOrEmpty(searchTagName) ||
                         tag.Name.Contains(searchTagName, StringComparison.OrdinalIgnoreCase);
        }

        #region 状态设置方法（替换原来的事件方法）

        public void SetSearchTagName(string searchTagName)
        {
            _searchTagNameSubject.OnNext(searchTagName);
        }

        public void AddTag(TagModel tag)
        {
            _tagsSource.Add(tag);
            _selectedTagsSet.Add(_tagsSource.Items.First(t => t.Key == tag.Key));
        }

        public void SelectTag(string tagKey)
        {
            TagModel tag = _tagsSource.Items.First(t => t.Key == tagKey);
            tag.Count++;
            _selectedTagsSet.Add(tag);
        }

        public void DeselectTag(string tagKey)
        {
            TagModel tag = _tagsSource.Items.First(e => e.Key == tagKey);
            tag.Count--;
            _selectedTagsSet.Remove(_selectedTagsSet.Items.First(t => t.Key == tagKey));
        }

        #endregion

        #region 批量加载方法

        public async Task LoadInitialDataAsync()
        {
            // 异步加载初始数据
            var tags = service.DataService.GetAllTagsAsync();
            var comicTags = service.DataService.GetTagsOfComic(comicKey);

            await Task.WhenAll(tags, comicTags);

            _tagsSource.Edit(list =>
            {
                list.Clear();
                list.AddRange(tags.Result);
            });

            _selectedTagsSet.Edit(list =>
            {
                list.Clear();
                list.AddRange(comicTags.Result);
            });
        }

        #endregion

        #region 辅助属性

        // 用于 UI 绑定的命令属性
        public ICommand SelectTagCommand { get; }
        public ICommand DeselectTagCommand { get; }

        #endregion
    }
}