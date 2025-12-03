using ComicViewer.Models;
using SharpCompress.Archives;
using SharpCompress.Common;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace ComicViewer.Services
{
    struct Entry
    {
        public string Path { get; set; }
        public int UseCount { get; set; }

        // 或者使用构造函数
        public Entry(string path, int useCount = 0)
        {
            Path = path;
            UseCount = useCount;
        }
    }
    public class ComicFileService
    {
        private Dictionary<string, Entry> comicPathDict
            = new Dictionary<string, Entry>();
        
        private Dictionary<string, Entry> comicPathDictRemove
            = new Dictionary<string, Entry>();

        private readonly object _lock = new object();

        private static readonly Lazy<ComicFileService> _instance = new(() => new ComicFileService());
        public static ComicFileService Instance => _instance.Value;

        private async Task RemoveTempFile(string path)
        {
            await Task.Run(() =>
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            });
        }

        public bool AddComicPath(string key, string path)
        {
            lock (_lock)
            {
                if (comicPathDict.ContainsKey(key))
                {
                    var entry = comicPathDict[key];
                    comicPathDict[key] = new Entry { Path = entry.Path, UseCount = entry.UseCount + 1 };
                    return false;
                }
                comicPathDict[key] = new Entry{Path=path, UseCount=1};
                return true;
            }
        }

        public bool RemoveComicPath(string key)
        {
            lock (_lock)
            {
                if (!comicPathDict.ContainsKey(key))
                {
                    return false;
                }
                var entry = comicPathDict[key];
                entry.UseCount--;// the copier no longer occupies the file.

                comicPathDict.Remove(key);
                if(entry.UseCount == 0)
                {
                    _ = RemoveTempFile(entry.Path);// silent remove
                    return true;
                }
                comicPathDictRemove.Add(key, entry);
                return true;
            }
        }

        public string GetComicPath(ComicModel comic)
        {
            lock (_lock)
            {
                if(comicPathDict.TryGetValue(comic.Key, out var entry))
                {
                    comicPathDict[comic.Key] = new Entry { Path=entry.Path, UseCount=entry.UseCount + 1 };
                    return entry.Path;
                }
            }
            return System.IO.Path.Combine(Configs.GetFilePath(), $"{comic.Key}.zip");
        }

        public void ReleaseComicPath(string Key)
        {
            lock (_lock)
            {
                Entry entry;
                if (comicPathDict.TryGetValue(Key, out entry))
                {
                    comicPathDict[Key] = new Entry { Path = entry.Path, UseCount = entry.UseCount - 1 };
                    return;
                }
                if (comicPathDictRemove.TryGetValue(Key, out entry))
                {
                    int newCount = entry.UseCount - 1;
                    if (newCount <= 0)
                    {
                        comicPathDictRemove.Remove(Key);
                        _ = RemoveTempFile(entry.Path);// silent remove
                    }
                    else
                    {
                        comicPathDictRemove[Key] = new Entry { Path = entry.Path, UseCount = entry.UseCount - 1 };
                    }
                }
            }
        }

        public async Task RemoveComicAsync(string Key)
        {
            Entry entry;
            bool found;
            string fullPath = System.IO.Path.Combine(Configs.GetFilePath(), $"{Key}.zip");
            lock (_lock)
            {
                found = comicPathDict.TryGetValue(Key, out entry);
            }
            if(!found)
            {
                // closed
                // remove
                await Task.Run(()=>File.Delete(fullPath));
                return;
            }
            // open
            // stop
            var stopped = await SilentFileLoader.Instance.StopMovingTask(Key);
            if (!stopped)
            {
                await Task.Run(() => File.Delete(fullPath));
            }
            return;
        }

        public async Task<int> CountComicLengthAsync(ComicModel comic)
        {
            return await Task.Run(() =>
            {
                var path = GetComicPath(comic);

                try
                {
                    // 检查是否为.cmc文件
                    if (path.EndsWith(".cmc", StringComparison.OrdinalIgnoreCase))
                    {
                        // 关键：流式读取，不解压
                        return CountComicLengthFromCmcStreaming(path);
                    }
                    else
                    {
                        // 普通压缩包
                        return CountComicLengthFromArchive(path);
                    }
                }
                finally
                {
                    ReleaseComicPath(comic.Key);
                }
            });
        }

        private int CountComicLengthFromCmcStreaming(string cmcPath)
        {
            using var cmcArchive = ArchiveFactory.Open(cmcPath);

            // 找到comic.zip条目
            var comicZipEntry = cmcArchive.Entries
                .FirstOrDefault(e =>
                    e.Key != null &&
                    e.Key.Equals("comic.zip", StringComparison.OrdinalIgnoreCase));

            if (comicZipEntry == null || comicZipEntry.IsDirectory)
                throw new InvalidDataException($"CMC文件中未找到comic.zip: {cmcPath}");

            // 获取zip条目的流
            using var zipStream = comicZipEntry.OpenEntryStream();

            // 从流中打开zip
            using var zipArchive = ArchiveFactory.Open(zipStream);

            return zipArchive.Entries.Count();
        }

        private int CountComicLengthFromArchive(string archivePath)
        {
            using var archive = ArchiveFactory.Open(archivePath);

            return archive.Entries.Count();
        }

        public async Task<List<string>> LoadImageEntriesAsync(ComicModel comic)
        {
            return await Task.Run(() =>
            {
                var path = GetComicPath(comic);

                try
                {
                    // 检查是否为.cmc文件
                    if (path.EndsWith(".cmc", StringComparison.OrdinalIgnoreCase))
                    {
                        // 关键：流式读取，不解压
                        return LoadImageEntriesFromCmcStreaming(path);
                    }
                    else
                    {
                        // 普通压缩包
                        return LoadImageEntriesFromArchive(path);
                    }
                }
                finally
                {
                    ReleaseComicPath(comic.Key);
                }
            });
        }

        private List<string> LoadImageEntriesFromCmcStreaming(string cmcPath)
        {
            using var cmcArchive = ArchiveFactory.Open(cmcPath);

            // 找到comic.zip条目
            var comicZipEntry = cmcArchive.Entries
                .FirstOrDefault(e =>
                    e.Key != null &&
                    e.Key.Equals("comic.zip", StringComparison.OrdinalIgnoreCase));

            if (comicZipEntry == null || comicZipEntry.IsDirectory)
                throw new InvalidDataException($"CMC文件中未找到comic.zip: {cmcPath}");

            // 获取zip条目的流，但先不读取内容
            using var zipStream = comicZipEntry.OpenEntryStream();

            // 从流中打开zip，不需要解压到磁盘或内存
            using var zipArchive = ArchiveFactory.Open(zipStream);

            // 只读取文件名列表，不读取文件内容
            return zipArchive.Entries
                .Where(e => e.Key != null && IsImageFile(e.Key))
                .OrderBy(e => e.Key!, new NaturalStringComparer())
                .Select(e => e.Key!)
                .ToList();
        }

        private List<string> LoadImageEntriesFromArchive(string archivePath)
        {
            using var archive = ArchiveFactory.Open(archivePath);

            return archive.Entries
                .Where(e => e.Key != null && IsImageFile(e.Key))
                .OrderBy(e => e.Key!, new NaturalStringComparer())
                .Select(e => e.Key!)
                .ToList();
        }

        private bool IsImageFile(string fileName)
        {
            var ext = System.IO.Path.GetExtension(fileName).ToLower();
            return ext == ".jpg" || ext == ".jpeg" || ext == ".png" ||
                   ext == ".bmp" || ext == ".gif" || ext == ".webp";
        }

        public async Task<BitmapImage> LoadImageAsync(ComicModel comic, string entryName)
        {
            return await Task.Run(async () =>
            {
                var archivePath = GetComicPath(comic);

                try
                {
                    if (System.IO.Path.GetExtension(archivePath).Equals(".cmc", StringComparison.OrdinalIgnoreCase))
                    {
                        // 处理.cmc文件（tar包里的漫画包）
                        return await LoadImageFromCmcAsync(archivePath, entryName);
                    }
                    else
                    {
                        // 处理普通压缩包
                        return await LoadImageFromRegularArchiveAsync(archivePath, entryName);
                    }
                }
                finally
                {
                    ReleaseComicPath(comic.Key);
                }
            });
        }

        private async Task<BitmapImage> LoadImageFromCmcAsync(string cmcPath, string entryName)
        {
            // 1. 打开.cmc文件（tar格式）
            using var cmcArchive = ArchiveFactory.Open(cmcPath);

            // 2. 查找comic.zip（或其他格式的漫画包）
            var comicArchiveEntry = cmcArchive.Entries
                .FirstOrDefault(e =>
                    e.Key != null &&
                    e.Key.Equals("comic.zip", StringComparison.OrdinalIgnoreCase));

            if (comicArchiveEntry == null)
                throw new InvalidDataException("CMC文件中未找到漫画压缩包");

            // 3. 打开漫画包的流
            using var comicStream = comicArchiveEntry.OpenEntryStream();

            // 4. 直接从流中打开漫画包
            using var comicArchive = ArchiveFactory.Open(comicStream);

            // 5. 查找目标图片
            var imageEntry = comicArchive.Entries
                .First(e => e.Key == entryName);

            if (imageEntry == null || imageEntry.IsDirectory)
                throw new FileNotFoundException($"图片不存在: {entryName}");

            // 6. 读取图片数据
            using var imageStream = imageEntry.OpenEntryStream();
            using var memoryStream = new MemoryStream();
            await imageStream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            return CreateBitmapImage(memoryStream);
        }

        private async Task<BitmapImage> LoadImageFromRegularArchiveAsync(string archivePath, string entryName)
        {
            using var archive = ArchiveFactory.Open(archivePath);
            var entry = archive.Entries.First(e => e.Key == entryName);

            using var stream = entry.OpenEntryStream();
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            return CreateBitmapImage(memoryStream);
        }

        private BitmapImage CreateBitmapImage(MemoryStream memoryStream)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = memoryStream;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.None;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        private bool IsComicArchiveFile(string fileName)
        {
            var comicExtensions = new[]
            {
                ".zip", ".cbz",
                ".rar", ".cbr",
                ".7z", ".cb7",
                ".tar", ".cbt"
            };

            var ext = System.IO.Path.GetExtension(fileName).ToLowerInvariant();
            return comicExtensions.Contains(ext);
        }
    }

    // 自然字符串排序比较器
    public class NaturalStringComparer : IComparer<string>
    {
        [System.Runtime.InteropServices.DllImport("shlwapi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int StrCmpLogicalW(string psz1, string psz2);

        public int Compare(string x, string y)
        {
            return StrCmpLogicalW(x, y);
        }
    }
}
