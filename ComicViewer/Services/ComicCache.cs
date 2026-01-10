using ComicViewer.Infrastructure;
using ComicViewer.Models;
using DynamicData;
using DynamicData.Binding;
using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Windows.Input;

namespace ComicViewer.Services
{
    public enum Order
    {
        CreatedTime,
        LastAccess,
        Title,
        TitleInverse,
        Rating
    }

    public class ComicCache
    {
        private readonly ComicService service;

        private readonly LRUCache<string, ComicModel> _comicCache = new(32);

        // DynamicData 数据源
        private readonly SourceList<ComicData> _comicsSource = new();
        private readonly SourceList<TagModel> _tagsSource = new();
        private readonly SourceList<TagModel> _selectedTagsSet = new();

        // 可观察的状态
        private readonly BehaviorSubject<string> _searchNameSubject = new(string.Empty);
        private readonly BehaviorSubject<string> _searchTagNameSubject = new(string.Empty);
        private readonly BehaviorSubject<Order> _orderSubject = new(Order.CreatedTime);

        // ViewModel 绑定的集合
        private readonly ReadOnlyObservableCollection<ComicModel> _comics;
        private readonly ReadOnlyObservableCollection<TagModel> _unselectedTags;
        private readonly ReadOnlyObservableCollection<TagModel> _selectedTags;

        public ComicViewModel ViewModel { get; }

        private ComicModel ConvertComicDataToModel(ComicData data)
        {
            if (_comicCache.TryGet(data.Key, out var model))
            {
                return model;
            }
            model = data.ToComicModel(service);
            _comicCache.Put(data.Key, model);
            return model;
        }

        public ComicCache(ComicService service)
        {
            this.service = service;

            // 初始化数据流

            // 设置漫画数据流
            _comicsSource.Connect()
                .Filter(_searchNameSubject.Select(CreateNameFilter))
                .Transform(data => ConvertComicDataToModel(data))
                .Sort(_orderSubject.Select(CreateComparer))
                //.ObserveOn(DispatcherScheduler.Current)  // 已移除，warning太多
                .Bind(out _comics)
                .Subscribe();

            // 未选中的标签（按名称过滤）
            _tagsSource.Connect()
                .AutoRefresh(tag => tag.Count)  // 监听 Count 变化
                .Filter(_searchTagNameSubject.Select(CreateTagNameFilter))
                .Except(_selectedTagsSet.Connect())
                .Sort(CreateTagComparer())
                .Bind(out _unselectedTags)
                .Subscribe();

            // 已选中的标签
            _selectedTagsSet.Connect()
                .AutoRefresh(tag => tag.Count)  // 监听 Count 变化
                .Sort(CreateTagComparer())
                .Bind(out _selectedTags)
                .Subscribe();

            SelectTagCommand = new RelayCommand<string>(tagKey => SelectTag(tagKey));
            DeselectTagCommand = new RelayCommand<string>(tagKey => DeselectTag(tagKey));
            ClearSearchCommand = new RelayCommand(() => ClearSelectedTags());

            // 创建 ViewModel
            ViewModel = new ComicViewModel(_comics, _unselectedTags, _selectedTags);

            service.Load.Add(new DAGTask
            {
                name = "ComicCache",
                task = InitializeAsync,
                requirements = { "DataService", "FileLoader" }
            });
        }

        private async Task InitializeAsync()
        {
            var comicsTask = service.DataService.GetAllComicsAsync();
            var tagsTask = service.DataService.GetAllTagsAsync();

            await Task.WhenAll(comicsTask, tagsTask);

            var comics = comicsTask.Result;
            var tags = tagsTask.Result.Select(e => new TagModel(e));

            // 使用 Edit() 进行批量操作，避免多次事件触发
            _comicsSource.Edit(list =>
            {
                list.Clear();
                list.AddRange(comics);
            });

            _tagsSource.Edit(list =>
            {
                list.Clear();
                list.AddRange(tags);
            });
        }
        private IComparer<TagModel> CreateTagComparer()
        {
            return SortExpressionComparer<TagModel>
                .Descending(m => m.Count)
                .ThenByAscending(m => m.Name);
        }

        private Func<ComicData, bool> CreateNameFilter(string searchName)
        {
            return comic => string.IsNullOrEmpty(searchName) ||
                           comic.Title.Contains(searchName, StringComparison.OrdinalIgnoreCase);
        }

        private Func<TagModel, bool> CreateTagNameFilter(string searchTagName)
        {
            return tag => string.IsNullOrEmpty(searchTagName) ||
                         tag.Name.Contains(searchTagName, StringComparison.OrdinalIgnoreCase);
        }

        private IComparer<ComicModel> CreateComparer(Order order)
        {
            return order switch
            {
                Order.CreatedTime => SortExpressionComparer<ComicModel>
                    .Descending(m => m.CreatedTime),

                Order.LastAccess => SortExpressionComparer<ComicModel>
                    .Descending(m => m.LastAccess),

                Order.Title => SortExpressionComparer<ComicModel>
                    .Ascending(m => m.Title),

                Order.TitleInverse => SortExpressionComparer<ComicModel>
                    .Descending(m => m.Title),

                Order.Rating => SortExpressionComparer<ComicModel>
                    .Descending(m => m.Rating)
                    .ThenByAscending(m => m.Title),

                _ => SortExpressionComparer<ComicModel>
                    .Descending(m => m.CreatedTime)
            };
        }

        #region 公共方法 - 替换原来的异步方法

        public async Task AddComic(ComicData comic)
        {
            // 异步检查标签（如果需要数据库查询）
            if (_selectedTagsSet.Items.Any())
            {
                var comicTagKeys = new HashSet<string>(
                    comic.ComicTags?.Select(ct => ct.Tag.Key) ?? (await service.DataService.GetTagsOfComic(comic.Key)).Select(e => e.Key)
                );

                var selectedKeys = new HashSet<string>(
                    _selectedTagsSet.Items.Select(ct => ct.Key) ?? Enumerable.Empty<string>()
                );

                if (!selectedKeys.IsSubsetOf(comicTagKeys))
                {
                    return;
                }
            }

            _comicsSource.Add(comic);
            await Task.CompletedTask;
        }

        public async Task RemoveComic(string Key)
        {
            var toRemove = _comicsSource.Items.FirstOrDefault(c => c.Key == Key);
            if (toRemove != null)
            {
                _comicCache.Remove(Key);
                _comicsSource.Remove(toRemove);
            }
            await Task.CompletedTask;
        }

        public async Task AddTag(TagData tag)
        {
            _tagsSource.Add(new TagModel(tag));
            await Task.CompletedTask;
        }

        public async Task RemoveTag(string Key)
        {
            var toRemove = _tagsSource.Items.FirstOrDefault(t => t.Key == Key);
            if (toRemove != null)
            {
                _tagsSource.Remove(toRemove);
            }
            await Task.CompletedTask;
        }

        #endregion

        #region 状态设置方法（替换原来的事件方法）

        public void SetOrder(Order order)
        {
            _orderSubject.OnNext(order);
        }

        public void SetSearchName(string searchName)
        {
            _searchNameSubject.OnNext(searchName);
        }

        public void SetSearchTagName(string searchTagName)
        {
            _searchTagNameSubject.OnNext(searchTagName);
        }

        public void SelectTag(string tagKey)
        {
            _selectedTagsSet.Add(_tagsSource.Items.First(t => t.Key == tagKey));
            _ = RefreshComicsAsync();
        }

        public void DeselectTag(string tagKey)
        {
            _selectedTagsSet.Remove(_selectedTagsSet.Items.First(t => t.Key == tagKey));
            _ = RefreshComicsAsync();
        }
        public void ClearSelectedTags()
        {
            _selectedTagsSet.Clear();
            _ = RefreshComicsAsync();
        }

        #endregion

        #region 批量加载方法

        public async Task LoadInitialDataAsync()
        {
            // 异步加载初始数据
            var task1 = service.DataService.GetAllComicsAsync();
            var task2 = service.DataService.GetAllTagsAsync();
            await Task.WhenAll(task1, task2);
            var comics = task1.Result;
            var tags = task2.Result.Select(e => new TagModel(e));

            _comicsSource.Edit(list =>
            {
                list.Clear();
                list.AddRange(comics);
            });

            _tagsSource.Edit(list =>
            {
                list.Clear();
                list.AddRange(tags);
            });
        }

        public async Task RefreshTagsAsync()
        {
            var allTags = (await service.DataService.GetAllTagsAsync())
                .Select(e => new TagModel(e)); // 使用包装类

            _tagsSource.Edit(list =>
            {
                var existingKeys = list.Select(x => x.Key).ToHashSet();
                var newKeys = allTags.Select(x => x.Key).ToHashSet();

                var toRemove = list.Where(x => !newKeys.Contains(x.Key)).ToList();
                foreach (var item in toRemove)
                {
                    list.Remove(item);
                }

                foreach (var newTag in allTags)
                {
                    var existing = list.FirstOrDefault(x => x.Key == newTag.Key);
                    if (existing != null)
                    {
                        // 更新现有对象的属性
                        existing.Count = newTag.Count;
                    }
                    else
                    {
                        list.Add(newTag);
                    }
                }
            });
        }

        public async Task RefreshComicsAsync()
        {
            var selectedTags = _selectedTagsSet.Items.Select(e => e.Key).ToList();
            if (!selectedTags.Any())
            {
                // 无标签选择时加载所有漫画
                var allComics = await service.DataService.GetAllComicsAsync();
                _comicsSource.Edit(list =>
                {
                    list.Clear();
                    list.AddRange(allComics);
                });
            }
            else
            {
                // 有标签选择时查询数据库
                var filteredComics = await service.DataService.GetComicsWithAllTagKeysAsync(selectedTags);
                _comicsSource.Edit(list =>
                {
                    list.Clear();
                    list.AddRange(filteredComics);
                });
            }
        }

        #endregion

        #region 辅助属性

        public int TotalComicCount => _comicsSource.Count;
        public int FilteredComicCount => _comics.Count;

        // 用于 UI 绑定的命令属性
        public ICommand SelectTagCommand { get; }
        public ICommand DeselectTagCommand { get; }
        public ICommand ClearSearchCommand { get; }

        #endregion
    }
}