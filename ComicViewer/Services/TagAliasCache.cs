using ComicViewer.Infrastructure;
using ComicViewer.Models;
using DynamicData;
using DynamicData.Binding;
using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace ComicViewer.Services
{
    public class TagAliasCache
    {
        private readonly ComicService service;

        // DynamicData 数据源
        private readonly SourceList<TagAlias> _mappingsSource = new();

        // 可观察的状态
        private readonly BehaviorSubject<string> _searchAliasSubject = new(string.Empty);
        private readonly BehaviorSubject<string> _searchTagNameSubject = new(string.Empty);

        // ViewModel 绑定的集合
        private readonly ReadOnlyObservableCollection<TagAlias> _selectedTagMappings;

        public TagAliasViewModel ViewModel { get; }

        public IEnumerable<TagAlias> AllEntries
        {
            get => _mappingsSource.Items;
        }

        public TagAliasCache(ComicService service)
        {
            this.service = service;

            // 初始化数据流

            // 已筛选的条目（按名称过滤）
            _mappingsSource.Connect()
                .Filter(_searchAliasSubject.Select(CreateAliasFilter))
                .Filter(_searchTagNameSubject.Select(CreateTagNameFilter))
                .Sort(CreateComparer())
                .Bind(out _selectedTagMappings)
                .Subscribe();

            // 创建 ViewModel
            ViewModel = new(_selectedTagMappings);

            service.Load.Add(new DAGTask
            {
                name = "AliasCache",
                task = InitializeAsync,
                requirements = { "DataService" }
            });
        }

        public async Task InitializeAsync()
        {
            await RefreshAsync();
        }

        public async Task RefreshAsync()
        {
            var mappings = await service.DataService.GetAllTagAliasesAsync();

            // 使用 Edit() 进行批量操作，避免多次事件触发
            _mappingsSource.Edit(list =>
            {
                list.Clear();
                list.AddRange(mappings);
            });
        }
        public bool ContainsAlias(string alias)
        {
            return _mappingsSource.Items.Any(t => t.Alias.Equals(alias, StringComparison.OrdinalIgnoreCase));
        }

        public string GetNameByAlias(string alias)
        {
            var entry = _mappingsSource.Items
                .FirstOrDefault(t => t.Alias.Equals(alias, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
                return alias;
            return entry.Name;
        }

        private IComparer<TagAlias> CreateComparer()
        {
            return SortExpressionComparer<TagAlias>
                    .Ascending(e => e.Name)
                    .ThenByAscending(e => e.Alias);
        }

        private Func<TagAlias, bool> CreateAliasFilter(string searchAlias)
        {
            return tag => string.IsNullOrEmpty(searchAlias) ||
                         tag.Alias.Contains(searchAlias, StringComparison.OrdinalIgnoreCase);
        }

        private Func<TagAlias, bool> CreateTagNameFilter(string searchTagName)
        {
            return tag => string.IsNullOrEmpty(searchTagName) ||
                         tag.Name.Contains(searchTagName, StringComparison.OrdinalIgnoreCase);
        }

        #region 状态设置方法（替换原来的事件方法）

        public void SetSearchAlias(string searchAlias)
        {
            _searchAliasSubject.OnNext(searchAlias);
        }
        public void SetSearchTagName(string searchTagName)
        {
            _searchTagNameSubject.OnNext(searchTagName);
        }

        public void AddTagAlias(TagAlias entry)
        {
            _mappingsSource.Add(entry);
        }

        public void RemoveTagAlias(TagAlias entry)
        {
            _mappingsSource.Remove(entry);
        }

        public void AddTagAliases(IEnumerable<TagAlias> entries)
        {
            _mappingsSource.AddRange(entries);
        }

        public void RemoveTagAliases(IEnumerable<TagAlias> entries)
        {
            _mappingsSource.RemoveMany(entries);
        }

        #endregion
    }
}
