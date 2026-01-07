using ComicViewer.Models;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using System.IO;
using System.Text;
using System.Windows.Media.Imaging;

namespace ComicViewer.Services
{
    class Entry
    {
        public required string Path { get; set; }
        public int UseCount { get; set; }
        public Func<Entry, Task> OnRemove { get; set; } = _ => Task.CompletedTask;
    }
    public class ComicFileService
    {
        private Dictionary<string, Entry> comicNormalPathDict = new();

        private Dictionary<string, Entry> comicTempPathDict = new();

        private readonly object _lock = new object();

        private readonly ComicService service;

        public ComicFileService(ComicService service)
        {
            this.service = service;
        }
        private static async Task RemoveFile(Entry entry)
        {
            await Task.Run(() =>
            {
                if (File.Exists(entry.Path))
                {
                    File.Delete(entry.Path);
                }
            });
        }
        private static readonly Func<Entry, Task> DoNothing = _ => Task.CompletedTask;

        public bool AddComicTempPath(string Key, string path)
        {
            lock (_lock)
            {
                if (comicTempPathDict.TryGetValue(Key, out var entry))
                {
                    entry.UseCount++;
                    return false;
                }
                else
                {
                    comicTempPathDict[Key] = new Entry { Path = path, OnRemove = RemoveFile, UseCount = 1 };
                    return true;
                }
            }
        }

        public bool RemoveComicTempPath(string Key)
        {
            lock (_lock)
            {
                if (!comicTempPathDict.TryGetValue(Key, out var entry))
                {
                    return false;
                }
                entry.UseCount--;// the copier no longer occupies the file.

                if (entry.UseCount == 0)
                {
                    comicTempPathDict.Remove(Key);
                    entry.OnRemove(entry);
                    return true;
                }
                return true;
            }
        }

        public void GenerateComicPath(string Key)
        {
            lock (_lock)
            {
                if (comicNormalPathDict.TryGetValue(Key, out var entry))
                {
                    return;
                }
                var path = ComicUtils.ComicNormalPath(Key);
                comicNormalPathDict[Key] = new Entry { Path = path, OnRemove = DoNothing, UseCount = 0 };
                return;
            }
        }

        public string GetComicPath(string Key)
        {
            lock (_lock)
            {
                Entry? entry;
                foreach (var dict in new Dictionary<string, Entry>[] { comicNormalPathDict, comicTempPathDict })
                {
                    if (dict.TryGetValue(Key, out entry))
                    {
                        entry.UseCount++;
                        return entry.Path;
                    }
                }
                var path = ComicUtils.ComicNormalPath(Key);
                comicNormalPathDict[Key] = new Entry { Path = path, OnRemove = DoNothing, UseCount = 1 };
                return path;
            }
        }

        public void ReleaseComicPath(string Key, string path)
        {
            lock (_lock)
            {
                Entry? entry;
                if (comicNormalPathDict.TryGetValue(Key, out entry) && entry.Path == path)
                {
                    entry.UseCount--;
                    // if no using and no routing overwrite needed
                    if (entry.UseCount <= 0 && !comicTempPathDict.TryGetValue(Key, out _))
                    {
                        comicNormalPathDict.Remove(Key);
                        entry.OnRemove(entry);
                    }
                    return;
                }
                if (comicTempPathDict.TryGetValue(Key, out entry) && entry.Path == path)
                {
                    entry.UseCount--;
                    if (entry.UseCount <= 0)
                    {
                        comicTempPathDict.Remove(Key);
                        entry.OnRemove(entry);
                    }
                }
            }
        }

        public async Task RemoveComicAsync(string Key)
        {
            bool loaded = true;
            if (comicTempPathDict.TryGetValue(Key, out _))// not opened operation
            {
                // if stoppable, it isn't loaded
                loaded = !(await service.FileLoader.StopMovingTask(Key));
            }
            lock (_lock)
            {
                if (comicNormalPathDict.TryGetValue(Key, out var entry))
                {
                    entry.OnRemove = RemoveFile;
                    if (entry.UseCount == 0)
                    {
                        comicNormalPathDict.Remove(Key);
                        entry.OnRemove(entry).Wait();
                    }
                    return;
                }
            }
            if (loaded)
            {
                await RemoveFile(new Entry { Path = ComicUtils.ComicNormalPath(Key), OnRemove = RemoveFile, UseCount = 0 });
            }
            return;
        }

        public async Task<int> CountComicLengthAsync(ComicModel comic)
        {
            return await Task.Run(() =>
            {
                var path = GetComicPath(comic.Key);

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
                    ReleaseComicPath(comic.Key, path);
                }
            });
        }

        private int CountComicLengthFromCmcStreaming(string cmcPath)
        {
            return GetComicZipInfo(cmcPath).fileCount;
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
                var path = GetComicPath(comic.Key);

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
                    ReleaseComicPath(comic.Key, path);
                }
            });
        }

        private List<string> LoadImageEntriesFromCmcStreaming(string cmcPath)
        {
            return GetComicZipInfo(cmcPath, true).fileNames!
                .OrderBy(e => e, new NaturalStringComparer())
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
            var ext = Path.GetExtension(fileName).ToLower();
            return ext == ".jpg" || ext == ".jpeg" || ext == ".png" ||
                   ext == ".bmp" || ext == ".gif" || ext == ".webp";
        }

        public async Task<BitmapImage?> LoadImageAsync(ComicModel comic, string entryName, int maxHeight = 2560, int maxWidth = 2560)
        {
            return await Task.Run(async () =>
            {
                var archivePath = GetComicPath(comic.Key);
                try
                {
                    MemoryStream? stream;
                    if (Path.GetExtension(archivePath).Equals(".cmc", StringComparison.OrdinalIgnoreCase))
                    {
                        // 处理.cmc文件（tar包里的漫画包）
                        stream = LoadImageFromCmc(archivePath, entryName);
                    }
                    else
                    {
                        // 处理普通压缩包
                        stream = LoadImageFromRegularArchive(archivePath, entryName);
                    }
                    if (stream == null)
                    {
                        return null;
                    }
                    return CreateBitmapImage(stream, maxHeight, maxWidth);
                }
                finally
                {
                    ReleaseComicPath(comic.Key, archivePath);
                }
            });
        }

        private MemoryStream? LoadImageFromCmc(string cmcPath, string entryName)
        {
            using var cmcArchive = ArchiveFactory.Open(cmcPath);

            var comicArchiveEntry = cmcArchive.Entries
                .FirstOrDefault(e =>
                    e.Key != null &&
                    e.Key.Equals("comic.zip", StringComparison.OrdinalIgnoreCase));

            if (comicArchiveEntry == null)
                return null;

            // 使用 Reader 而不是 Archive 来避免缓存整个ZIP
            using var comicStream = comicArchiveEntry.OpenEntryStream();
            using var reader = ReaderFactory.Open(comicStream);

            // 查找目标图片
            while (reader.MoveToNextEntry())
            {
                if (reader.Entry.Key == entryName && !reader.Entry.IsDirectory)
                {
                    // 流式读取图片，不缓存整个文件
                    using var imageStream = reader.OpenEntryStream();
                    var ms = new MemoryStream();
                    imageStream.CopyTo(ms);
                    ms.Position = 0;
                    return ms;
                }
            }

            return null;
        }

        private MemoryStream LoadImageFromRegularArchive(string archivePath, string entryName)
        {
            using var archive = ArchiveFactory.Open(archivePath);
            var entry = archive.Entries.First(e => e.Key == entryName);

            using var imageStream = entry.OpenEntryStream();
            var ms = new MemoryStream();
            imageStream.CopyTo(ms);
            ms.Position = 0;
            return ms;
        }

        private BitmapImage CreateBitmapImage(MemoryStream stream, int maxHeight, int maxWidth)
        {
            int originalWidth, originalHeight;
            {
                // 首先加载原始图像
                stream.Position = 0;
                BitmapImage originalBitmap = new();
                originalBitmap.BeginInit();
                originalBitmap.StreamSource = stream;
                originalBitmap.CacheOption = BitmapCacheOption.None; // only load size info
                originalBitmap.CreateOptions = BitmapCreateOptions.None;
                originalBitmap.EndInit();
                originalWidth = originalBitmap.PixelWidth;
                originalHeight = originalBitmap.PixelHeight;
            }

            // 计算缩放比例
            double widthRatio = (double)maxWidth / originalWidth;
            double heightRatio = (double)maxHeight / originalHeight;

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

            // 重新读取流（需要重置流位置）
            stream.Position = 0;
            resizedBitmap.StreamSource = stream;
            resizedBitmap.CacheOption = BitmapCacheOption.OnLoad;
            resizedBitmap.CreateOptions = BitmapCreateOptions.None;
            resizedBitmap.EndInit();
            resizedBitmap.Freeze();

            return resizedBitmap;
        }

        public (int fileCount, List<string>? fileNames) GetComicZipInfo(string cmcPath,
        bool includeNames = false)
        {
            using var tarArchive = ArchiveFactory.Open(cmcPath);

            var comicZipEntry = tarArchive.Entries
                .FirstOrDefault(e =>
                    e.Key != null &&
                    e.Key.EndsWith("comic.zip", StringComparison.OrdinalIgnoreCase));

            if (comicZipEntry == null)
                return (0, new List<string>());

            int fileCount = 0;
            List<string>? fileNames = includeNames ? new List<string>() : null;

            // 使用缓冲流优化读取性能
            using var rawZipStream = comicZipEntry.OpenEntryStream();
            using var bufferedStream = new BufferedStream(rawZipStream, 65536);

            // 使用 ZipReader 而不是 ZipArchive
            using var reader = ReaderFactory.Open(bufferedStream, new ReaderOptions
            {
                ArchiveEncoding = new ArchiveEncoding
                {
                    // 指定编码以防中文文件名
                    Default = Encoding.UTF8
                }
            });

            while (reader.MoveToNextEntry())
            {
                if (!reader.Entry.IsDirectory)
                {
                    fileCount++;

                    if (fileNames != null && reader.Entry.Key != null)
                    {
                        fileNames.Add(reader.Entry.Key);
                    }

                    // 直接跳过文件内容 - 不读取实际数据
                    // 这是避免内存问题的关键
                    SkipEntryContent(reader);
                }
            }

            return (fileCount, fileNames);
        }

        private void SkipEntryContent(IReader reader)
        {
            try
            {
                // 最小化内存使用的跳过方法
                using var entryStream = reader.OpenEntryStream();
                byte[] smallBuffer = new byte[4096];
                int bytesRead;

                do
                {
                    bytesRead = entryStream.Read(smallBuffer, 0, smallBuffer.Length);
                }
                while (bytesRead > 0);
            }
            catch
            {
                // 忽略跳过错误，继续处理下一个文件
            }
        }
    }

    // 自然字符串排序比较器
    public class NaturalStringComparer : IComparer<string>
    {
        [System.Runtime.InteropServices.DllImport("shlwapi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int StrCmpLogicalW(string psz1, string psz2);

        public int Compare(string? x, string? y)
        {
            if (x == null || y == null)
                return 0;
            return StrCmpLogicalW(x, y);
        }
    }
}
