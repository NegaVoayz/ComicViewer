using ComicViewer.Infrastructure;
using ComicViewer.Services;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace ComicViewer.Models
{
    public class ComicData
    {
        [Key]
        [StringLength(32)] // Text(32) 对应 MD5 长度
        [Comment("MD5主键")]
        public required string Key { get; set; }

        [Column("Title", TypeName = "TEXT")]
        [Comment("漫画标题")]
        public required string Title { get; set; }

        [Column("Source", TypeName = "TEXT")]
        [Comment("漫画源")]
        public required string Source { get; set; }

        [Column("CreatedTime", TypeName = "DATETIME")]
        [Comment("创建时间")]
        public DateTime CreatedTime { get; set; }

        [Column("LastAccess", TypeName = "DATETIME")]
        [Comment("最后访问时间")]
        public DateTime LastAccess { get; set; }

        [Column("Progress", TypeName = "INTEGER")]
        [Comment("阅读进度")]
        public int Progress { get; set; }

        [Column("Rating", TypeName = "INTEGER")]
        [Comment("评分 0-5")]
        public int Rating { get; set; }

        [Comment("漫画标签关联")]
        public virtual ICollection<ComicTag> ComicTags { get; set; } = null!;

        public ComicModel ToComicModel(ComicService service)
        {
            return new ComicModel(service)
            {
                Title = Title,
                Source = Source,
                Progress = Progress,
                Key = Key,
                LastAccess = LastAccess,
                CreatedTime = CreatedTime,
                Rating = Rating
            };
        }

        public ComicMetadata ToComicMetadata()
        {
            return new ComicMetadata
            {
                Version = "1.0",
                Title = Title,
                Source = Source,
                Tags = ComicTags?.Select(ct => ct.Tag.Name).ToList() ?? new List<string>(),
                System = new SystemInfo
                {
                    CreatedTime = CreatedTime,
                    LastAccess = LastAccess,
                    ReadProgress = Progress,
                    Rating = Rating
                }
            };
        }
    }
    public class ComicModel : INotifyPropertyChanged, IUnloadableViewModel
    {
        private readonly ComicService service;
        public required string Key { get; set; }
        private string _title = null!;
        private string _source = null!;
        private ObservableTask<List<TagData>>? _tagsTask;
        private string? _tagsPreview;
        private string? _author;
        private int _progress;
        private int _length = 0;
        private ObservableTask<int>? _lengthTask;
        private ObservableTask? _coverTask;
        public int Rating;
        public DateTime CreatedTime;
        public DateTime LastAccess;
        private static readonly BitmapImage _PlaceholderImage = LoadPlaceholderImage();

        public ComicModel(ComicService service)
        {
            this.service = service;
        }

        public string Title
        {
            get => _title;
            set => SetField(ref _title, value);
        }

        public string Source
        {
            get => _source;
            set => SetField(ref _source, value);
        }

        public string TagsPreview
        {
            get
            {
                // 当访问Tags时，如果为空则触发懒加载
                if (_tagsPreview == null && _tagsTask == null)
                {
                    // 开始加载，但不等待
                    RefreshTags();
                }
                return _tagsPreview ?? "N/A";
            }
            set => SetField(ref _tagsPreview, value);
        }

        public string Author
        {
            get
            {
                // 当访问Tags时，如果为空则触发懒加载
                if (_author == null && _tagsTask == null)
                {
                    // 开始加载，但不等待
                    RefreshTags();
                }
                return _author ?? "N/A";
            }
            set => SetField(ref _author, value);
        }

        public int Progress
        {
            get => _progress;
            set => SetField(ref _progress, value);
        }

        public int Length
        {
            get
            {
                // 当访问Length时，如果为空则触发懒加载
                if (_length == 0)
                {
                    if (_lengthTask == null)
                    {
                        // 开始加载，但不等待
                        _lengthTask = new ObservableTask<int>(service.FileService.CountComicLengthAsync(this));
                        _lengthTask.PropertyChanged += OnLengthTaskPropertyChanged;
                    }
                    return 0;// 占位，表示正在加载
                }
                return _length;
            }
            private set => SetField(ref _length, value);
        }

        public BitmapImage CoverImage
        {
            get
            {
                // 当访问CoverImage时，在视口内会自动加载
                if (service.CoverCache.TryGet(Key, out var cachedCover))
                {
                    return cachedCover;
                }
                Load();
                return _PlaceholderImage;
            }
        }

        public void RefreshTags()
        {
            _tagsTask = new ObservableTask<List<TagData>>(service.DataService.GetTagsOfComic(Key));
            _tagsTask.PropertyChanged += OnTagsTaskPropertyChanged;
        }
        public void RefreshCover()
        {
            // 开始加载，但不等待
            _coverTask = new ObservableTask(LoadCoverAsync());
            _coverTask.PropertyChanged += OnCoverTaskPropertyChanged;
        }

        private void OnTagsTaskPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ObservableTask<List<TagData>>.Result))
            {
                if (_tagsTask?.Result != null)
                {
                    var result = _tagsTask.Result;
                    var authors = result.Where(e => e.Name.StartsWith(ComicUtils.AuthorPrefix)).Select(e => e.Name.Substring(ComicUtils.AuthorPrefix.Length));
                    Author = authors.Any() ? String.Join(", ", authors) : "Unknown";
                    var tags = result
                        .Where(e => !e.Name.StartsWith(ComicUtils.AuthorPrefix))
                        .Select(e => e.Name)
                        .Take(3); // Take the first 3 non-author tags
                    TagsPreview = tags.Any() ? String.Join(", ", tags) : "TagMe";
                    OnPropertyChanged();
                    _tagsTask = null;
                }
            }
        }
        private void OnCoverTaskPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            OnPropertyChanged("CoverImage");
            _coverTask = null;
        }
        private void OnLengthTaskPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ObservableTask<int>.Result))
            {
                if (_lengthTask != null && _lengthTask.Result != 0)
                {
                    Length = _lengthTask.Result;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private async Task LoadCoverAsync()
        {
            await Task.Run(async () =>
            {
                if(service.CoverCache.TryGet(Key, out _))
                {
                    return;
                }

                var images = await service.FileService.LoadImageEntriesAsync(this);
                var coverName = images.First();

                var cover = await service.FileService.LoadImageAsync(this, coverName, 350, 280);
                if (cover != null)
                {
                    service.CoverCache.Put(Key, cover);
                }
            });
        }

        private static BitmapImage LoadPlaceholderImage()
        {
            // 从应用程序资源加载占位图
            var uri = new Uri("pack://application:,,,/Resources/placeholder.jpg");

            int originalWidth, originalHeight;
            {
                // 首先加载原始图像
                BitmapImage originalBitmap = new();
                originalBitmap.BeginInit();
                originalBitmap.UriSource = uri;
                originalBitmap.CacheOption = BitmapCacheOption.None; // only load size info
                originalBitmap.CreateOptions = BitmapCreateOptions.None;
                originalBitmap.EndInit();
                originalWidth = originalBitmap.PixelWidth;
                originalHeight = originalBitmap.PixelHeight;
            }

            // 计算缩放比例
            double widthRatio = (double)280 / originalWidth;
            double heightRatio = (double)350 / originalHeight;

            // 选择较小的比例以确保图像完全在限制内
            double ratio = Math.Min(Math.Min(widthRatio, heightRatio), 1.0);

            // 计算新尺寸
            int newWidth = (int)(originalWidth * ratio);
            int newHeight = (int)(originalHeight * ratio);

            // 确保至少为1像素
            newWidth = Math.Max(1, newWidth);
            newHeight = Math.Max(1, newHeight);

            // 创建新的BitmapImage并应用缩放
            BitmapImage resizedBitmap = new();
            resizedBitmap.BeginInit();
            resizedBitmap.DecodePixelWidth = newWidth;
            resizedBitmap.DecodePixelHeight = newHeight;

            // 重新读取
            resizedBitmap.UriSource = uri;
            resizedBitmap.CacheOption = BitmapCacheOption.OnLoad;
            resizedBitmap.CreateOptions = BitmapCreateOptions.None;
            resizedBitmap.EndInit();
            resizedBitmap.Freeze();

            return resizedBitmap;
        }

        public void Load()
        {
            if (_coverTask == null)
            {
                RefreshCover();
            }
        }

        public void Unload()
        {
            _coverTask = null;
        }
    }
}
