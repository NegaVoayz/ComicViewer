using ComicViewer.Models;
using DynamicData;
using DynamicData.Binding;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
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

        // 可观察的状态
        private readonly BehaviorSubject<string> _searchNameSubject = new(string.Empty);
        private readonly BehaviorSubject<string> _searchTagNameSubject = new(string.Empty);
        private readonly BehaviorSubject<Order> _orderSubject = new(Order.CreatedTime);
        private readonly BehaviorSubject<HashSet<string>> _selectedTagKeysSubject = new(new());

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

            // 组合所有过滤条件
            var combinedFilter = Observable.CombineLatest(
                _selectedTagKeysSubject.Select(CreateTagFilter),
                _searchNameSubject.Select(CreateNameFilter),
                (tagFilter, nameFilter) => new Func<ComicData, bool>(c => tagFilter(c) && nameFilter(c))
            );

            // 设置漫画数据流
            _comicsSource.Connect()
                .Filter(combinedFilter)
                .Transform(data => ConvertComicDataToModel(data))
                .Sort(_orderSubject.Select(CreateComparer))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Bind(out _comics)
                .Subscribe();

            // 未选中的标签（按名称过滤）
            _tagsSource.Connect()
                .Filter(_searchTagNameSubject.Select(CreateTagNameFilter))
                .Filter(tag => !_selectedTagKeysSubject.Value.Contains(tag.Key))
                .Bind(out _unselectedTags)
                .Subscribe();

            // 已选中的标签
            _tagsSource.Connect()
                .Filter(tag => _selectedTagKeysSubject.Value.Contains(tag.Key))
                .Bind(out _selectedTags)
                .Subscribe();

            SelectTagCommand = new RelayCommand<string>(tagKey => SelectTag(tagKey));
            DeselectTagCommand = new RelayCommand<string>(tagKey => DeselectTag(tagKey));
            ClearSearchCommand = new RelayCommand(() => ClearSelectedTags());

            // 创建 ViewModel
            ViewModel = new ComicViewModel(_comics, _unselectedTags, _selectedTags);
        }
        
        public async Task InitializeAsync()
        {
            var comicsTask = service.DataService.GetAllComicsAsync();
            var tagsTask = service.DataService.GetAllTagsAsync();

            await Task.WhenAll(comicsTask, tagsTask);

            var comics = comicsTask.Result;
            var tags = tagsTask.Result;

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

        private Func<ComicData, bool> CreateTagFilter(HashSet<string> selectedTagKeys)
        {
            return comic =>
            {
                if (!selectedTagKeys.Any()) return true;

                var comicTagKeys = new HashSet<string>(
                    comic.ComicTags?.Select(ct => ct.Tag.Key) ?? Enumerable.Empty<string>()
                );
                return selectedTagKeys.IsSubsetOf(comicTagKeys);
            };
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
            if (_selectedTagKeysSubject.Value.Any())
            {
                var comicTagKeys = new HashSet<string>(
                    comic.ComicTags?.Select(ct => ct.Tag.Key) ?? Enumerable.Empty<string>()
                );

                if (!_selectedTagKeysSubject.Value.IsSubsetOf(comicTagKeys))
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

        public async Task AddTag(TagModel tag)
        {
            _tagsSource.Add(tag);
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

        public void SetSelectedTagKeys(IEnumerable<string> tagKeys)
        {
            var newSet = new HashSet<string>(tagKeys);
            _selectedTagKeysSubject.OnNext(newSet);
        }

        public void SelectTag(string tagKey)
        {
            var current = new HashSet<string>(_selectedTagKeysSubject.Value);
            if (current.Add(tagKey)) // 只有成功添加时才更新
            {
                _selectedTagKeysSubject.OnNext(current);
            }
        }

        public void DeselectTag(string tagKey)
        {
            var current = new HashSet<string>(_selectedTagKeysSubject.Value);
            if (current.Remove(tagKey)) // 只有成功移除时才更新
            {
                _selectedTagKeysSubject.OnNext(current);
            }
        }
        public void ClearSelectedTags()
        {
            if (_selectedTagKeysSubject.Value.Any())
            {
                _selectedTagKeysSubject.OnNext(new HashSet<string>());
            }
        }

        #endregion

        #region 批量加载方法

        public async Task LoadInitialDataAsync()
        {
            // 异步加载初始数据
            var comics = await service.DataService.GetAllComicsAsync();
            var tags = await service.DataService.GetAllTagsAsync();

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

        public async Task RefreshComicsByTagsAsync()
        {
            var selectedTags = _selectedTagKeysSubject.Value.ToList();
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
        public IReadOnlyCollection<string> SelectedTagKeys => _selectedTagKeysSubject.Value;

        // 用于 UI 绑定的命令属性
        public ICommand SelectTagCommand { get; }
        public ICommand DeselectTagCommand { get; }
        public ICommand ClearSearchCommand { get; }

        #endregion
    }
}