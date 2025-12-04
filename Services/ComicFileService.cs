using ComicViewer.Models;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
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

        private readonly ComicService service;

        public ComicFileService(ComicService service)
        {
            this.service = service;
        }

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
                // if there's one, replace
                comicPathDictRemove[key] = entry;
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
            var stopped = await service.FileLoader.StopMovingTask(Key);
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
            return GetComicZipInfo(cmcPath, true).fileNames
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
            using var cmcArchive = ArchiveFactory.Open(cmcPath);

            var comicArchiveEntry = cmcArchive.Entries
                .FirstOrDefault(e =>
                    e.Key != null &&
                    e.Key.Equals("comic.zip", StringComparison.OrdinalIgnoreCase));

            if (comicArchiveEntry == null)
                throw new InvalidDataException("CMC文件中未找到漫画压缩包");

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
                    return CreateBitmapImage(imageStream);
                }
            }

            throw new FileNotFoundException($"图片不存在: {entryName}");
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

        private BitmapImage CreateBitmapImage(Stream stream)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = stream;
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

        public (int fileCount, List<string> fileNames) GetComicZipInfo(string cmcPath,
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
            List<string> fileNames = includeNames ? new List<string>() : null;

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

                    if (includeNames)
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

        public int Compare(string x, string y)
        {
            return StrCmpLogicalW(x, y);
        }
    }
}
