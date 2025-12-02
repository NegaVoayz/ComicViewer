using System.Windows.Media.Imaging;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using System.IO;
using ComicViewer.Services;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;
using System.Windows.Media;

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

        public ComicModel GetComicModel()
        {
            return new ComicModel
            {
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
            set
            {
                _progress = value;
                OnPropertyChanged();
            }
        }

        public int Length
        {
            get
            {
                // 当访问Length时，如果为空则触发懒加载
                if (_length == 0 && _lengthTask == null)
                {
                    _length = CountLengthFromArchive();
                }
                return _length;
            }
            private set
            {
                _length = value;
                OnPropertyChanged();
            }
        }

        public BitmapImage CoverImage
        {
            get
            {
                // 当访问CoverImage时，如果为空则触发懒加载
                if (_coverImage == null && _coverTask == null)
                {
                    // 开始加载，但不等待
                    _coverTask = new ObservableTask<BitmapImage>(LoadCoverFromArchiveAsync());
                    _coverTask.PropertyChanged += OnCoverTaskPropertyChanged;
                }
                return _coverImage ?? LoadPlaceholderImage();
            }
            private set
            {
                _coverImage = value;
                OnPropertyChanged();
            }
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

        // 为XAML绑定提供的方法
        public Task<BitmapImage> GetLazyCoverSource()
        {
            return Task.Run(async () =>
            {
                if (_coverImage != null)
                    return _coverImage;

                await LoadCoverFromArchiveAsync();
                return _coverImage!;
            });
        }
        private int CountLengthFromArchive()
        {
            string filename = $"{Key}.zip";
            string filePath = Path.Combine(Configs.GetFilePath(), filename);
            using var archive = SharpCompress.Archives.ArchiveFactory.Open(filePath);
            // set length
            return archive.Entries.Count();
        }

        private async Task<BitmapImage> LoadCoverFromArchiveAsync()
        {
            return await Task.Run(async () =>
            {
                string filename = $"{Key}.zip";
                string filePath = Path.Combine(Configs.GetFilePath(), filename);
                using var archive = SharpCompress.Archives.ArchiveFactory.Open(filePath);
                // set length
                _length = archive.Entries.Count();
                var imageEntry = archive.Entries
                    .FirstOrDefault(e => e.Key != null && Path.GetFileName(e.Key)!.Equals("cover.jpg"))
                    ?? archive.Entries.Where(e => !e.IsDirectory).OrderBy(e => e.Key).FirstOrDefault();

                if (imageEntry == null)
                {
                    return LoadPlaceholderImage();
                }

                // 1. 从压缩包读取流
                using var archiveStream = imageEntry.OpenEntryStream();

                // 2. 复制到MemoryStream（保持存活）
                var memoryStream = new MemoryStream();
                await archiveStream.CopyToAsync(memoryStream);
                memoryStream.Position = 0; // 重要：重置位置

                // 3. 同步创建BitmapImage（必须在UI线程或使用Dispatcher）
                return await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad; // 完全加载到内存
                    bitmap.StreamSource = memoryStream;
                    bitmap.DecodePixelWidth = 280;
                    bitmap.CreateOptions = BitmapCreateOptions.None;
                    bitmap.EndInit();
                    bitmap.Freeze(); // 允许跨线程访问

                    return bitmap;
                });
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
            //bitmap.DecodePixelWidth = 280;
            bitmap.CreateOptions = BitmapCreateOptions.None;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
    }
}
