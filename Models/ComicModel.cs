using System.Windows.Media.Imaging;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using ComicViewer.Services;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;

namespace ComicViewer.Models
{
    public class ComicData
    {
        [Key]
        [StringLength(32)] // Text(32) 对应 MD5 长度
        [Comment("MD5主键")]
        public string Key { get; set; }

        [Column("Title", TypeName = "TEXT")]
        [Comment("漫画标题")]
        public string Title { get; set; }

        [Column("CreatedTime", TypeName = "DATETIME")]
        [Comment("创建时间")]
        public DateTime? CreatedTime { get; set; }

        [Column("LastAccess", TypeName = "DATETIME")]
        [Comment("最后访问时间")]
        public DateTime? LastAccess { get; set; }

        [Column("Progress", TypeName = "INTEGER")]
        [Comment("阅读进度")]
        public int Progress { get; set; }

        [Column("Rating", TypeName = "INTEGER")]
        [Comment("评分 0-5")]
        public int Rating { get; set; }

        [Comment("漫画标签关联")]
        public virtual ICollection<ComicTag> ComicTags { get; set; }

        public ComicModel ToComicModel(ComicService service)
        {
            return new ComicModel
            {
                Service = service,
                Title = Title,
                Progress = Progress,
                Key = Key,
                LastAccess = LastAccess,
                CreatedTime = CreatedTime,
                Rating = Rating,
                Tags = ComicTags?.Select(ct => ct.Tag.Name).ToArray() ?? Array.Empty<string>()
            };
        }

        public ComicMetadata ToComicMetadata()
        {
            return new ComicMetadata
            {
                Version = "1.0",
                Title = Title,
                // 从 ComicTags 提取标签名称
                Tags = ComicTags?.Select(ct => ct.Tag.Name).ToArray() ?? Array.Empty<string>(),
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
    public class ComicModel : INotifyPropertyChanged
    {
        private ComicService service;
        public string Key { get; set; }
        private string _title;
        private string[] _tags;
        private int _progress;
        private int _length = 0;
        private ObservableTask<int> _lengthTask;
        private BitmapImage _coverImage;
        private ObservableTask<BitmapImage> _coverTask;
        public int Rating;
        public DateTime? CreatedTime;
        public DateTime? LastAccess;

        public ComicService Service
        {
            set => service = value;
        }

        public string Title
        {
            get => _title;
            set => SetField(ref _title, value);
        }

        public string[] Tags
        {
            get => _tags;
            set => SetField(ref _tags, value);
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
                    if(_lengthTask == null)
                    {
                        // 开始加载，但不等待
                        _lengthTask = new ObservableTask<int>(service.FileService.CountComicLengthAsync(this));
                        _lengthTask.PropertyChanged += OnLengthTaskPropertyChanged;
                    }
                    return (int)1e9;// 占位，表示正在加载
                }
                return _length;
            }
            private set => SetField(ref _length, value);
        }

        public BitmapImage CoverImage
        {
            get
            {
                // 当访问CoverImage时，如果为空则触发懒加载
                if (_coverImage == null && _coverTask == null)
                {
                    // 开始加载，但不等待
                    _coverTask = new ObservableTask<BitmapImage>(LoadCoverAsync());
                    _coverTask.PropertyChanged += OnCoverTaskPropertyChanged;
                }
                return _coverImage ?? LoadPlaceholderImage();
            }
            private set => SetField(ref _coverImage, value);
        }
        public string TagsPreview
        {
            get
            {
                if (_tags == null || _tags.Length == 0)
                    return "TagMe";

                return String.Join(", ",_tags.Take(3));
            }
        }

        private void OnCoverTaskPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ObservableTask<BitmapImage>.Result))
            {
                if (_coverTask.Result != null)
                {
                    CoverImage = _coverTask.Result;
                    OnPropertyChanged();
                }
            }
        }
        private void OnLengthTaskPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ObservableTask<int>.Result))
            {
                if (_lengthTask.Result != 0)
                {
                    _length = _lengthTask.Result;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private async Task<BitmapImage> LoadCoverAsync()
        {
            return await Task.Run(async () =>
            {
                var images = await service.FileService.LoadImageEntriesAsync(this);
                var coverName = images.First();
                return await service.FileService.LoadImageAsync(this, coverName);
            });
        }

        private BitmapImage LoadPlaceholderImage()
        {
            // 从应用程序资源加载占位图
            var uri = new Uri("pack://application:,,,/Resources/placeholder.jpg");
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = uri;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.None;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
    }
}
