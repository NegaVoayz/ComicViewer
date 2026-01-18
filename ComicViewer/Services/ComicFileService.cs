using ComicViewer.Infrastructure;
using ComicViewer.Models;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Media.Imaging;

namespace ComicViewer.Services
{
    public class ComicFileService
    {
        public class PathEntry : IDisposable
        {
            private readonly ComicFileService _fileService;
            private readonly string _path;
            public PathEntry(ComicFileService fileService, string path)
            {
                _fileService = fileService;
                _path = path;
            }
            public static implicit operator string(PathEntry path)
            {
                return path._path;
            }
            public bool EndsWith(string value) => _path.EndsWith(value);
            public bool EndsWith(string value, StringComparison comparisonType) => _path.EndsWith(value, comparisonType);
            public bool StartsWith(string value) => _path.StartsWith(value);
            public bool StartsWith(string value, StringComparison comparisonType) => _path.StartsWith(value, comparisonType);
            public string Extension => System.IO.Path.GetExtension(_path);
            public override string ToString() => _path;
            public void Dispose()
            {
                _fileService.ReleaseComicPath(_path);
            }
        }
        private class EntryData
        {
            public int UseCount = 0;
            public bool Deprecated = false;
        }

        // todo refactor:
        private Dictionary<string, string> comicPathDict = new();

        private ConcurrentDictionary<string, EntryData> pathDataDict = new();

        private readonly ComicService service;

        public ComicFileService(ComicService service)
        {
            this.service = service;

            service.Load.Add(new DAGTask
            {
                name = "FileService",
                task = Initialize,
                requirements = { "DataService" }
            });
        }

        private async Task Initialize()
        {
            foreach(var comic in await service.DataService.GetAllComicsAsync())
            {
                var path = ComicUtils.ComicNormalPath(comic.Title);
                if (!File.Exists(path))
                {
                    var oldPath = ComicUtils.ComicNormalPath(comic.Key);
                    if (!File.Exists(oldPath)) continue;
                    File.Move(oldPath, path);
                }
                comicPathDict[comic.Key] = path;
                pathDataDict[path] = new EntryData { UseCount = 0, Deprecated = false };
            }
        }
        private static async Task RemoveFile(string path)
        {
            await Task.Run(() =>
            {
                if (File.Exists(path))
                {
                    Debug.WriteLine($"Deleting file: {path}");
                    File.Delete(path);
                    return;
                }
                if (Directory.Exists(path))
                {
                    Debug.WriteLine($"Deleting directory: {path}");
                    Directory.Delete(path, recursive: true);
                    return;
                }
                Debug.WriteLine($"File or directory not found for deletion: {path}");
            });
        }

        public bool AddComicPath(string Key, string path)
        {
            if (comicPathDict.TryGetValue(Key, out var OldPath))
            {
                if (OldPath == path) return false;
                pathDataDict[OldPath].Deprecated = true;
            }
            comicPathDict[Key] = path;
            pathDataDict[path] = new EntryData { UseCount = 0, Deprecated = false };
            return true;
        }

        public bool DeprecateComic(string key)
        {
            if (!comicPathDict.TryGetValue(key, out var path))
            {
                return false;
            }
            if (!pathDataDict.TryGetValue(path, out var entry))
            {
                return false;
            }
            if (entry.UseCount == 0)
            {
                pathDataDict.Remove(path, out _);
                _ = RemoveFile(path);
                return true;
            }

            entry.Deprecated = true;
            return true;
        }

        public PathEntry GetComicPath(string Key)
        {
            var path = comicPathDict[Key];
            pathDataDict[path].UseCount++;
            return new PathEntry(this, path);
        }

        private void ReleaseComicPath(string path)
        {
            var entry = pathDataDict[path];
            entry.UseCount--;
            Debug.WriteLine($"{path} used {pathDataDict[path].UseCount} times");
            if (entry.Deprecated && entry.UseCount == 0)
            {
                pathDataDict.Remove(path,out _);
                _ = RemoveFile(path);
            }
        }

        public async Task RemoveComicAsync(string Key)
        {
            await service.FileLoader.StopMovingTask(Key);
            DeprecateComic(Key);
            return;
        }

        public async Task<int> CountComicLengthAsync(ComicModel comic)
        {
            return await Task.Run(() =>
            {
                using var path = GetComicPath(comic.Key);

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
            });
        }

        private int CountComicLengthFromCmcStreaming(string cmcPath)
        {
            return GetComicZipInfo(cmcPath).fileCount;
        }

        private int CountComicLengthFromArchive(string archivePath)
        {
            try
            {
                if (!File.Exists(archivePath))
                    return 0;
                using var archive = ArchiveFactory.Open(archivePath);

                return archive.Entries.Count();
            }
            catch (FileNotFoundException)
            {
                return 0;
            }
        }

        public async Task<List<string>> LoadImageEntriesAsync(ComicModel comic)
        {
            return await Task.Run(() =>
            {
                using var path = GetComicPath(comic.Key);
                // 检查是否为.cmc文件
                if (path.Extension.Equals(".cmc", StringComparison.OrdinalIgnoreCase))
                {
                    return LoadImageEntriesFromCmcStreaming(path);
                }
                if(Directory.Exists(path))
                {
                    return LoadImageEntriesFromFolder(path);
                }
                // 普通压缩包
                return LoadImageEntriesFromArchive(path);
            });
        }

        private List<string> LoadImageEntriesFromCmcStreaming(string cmcPath)
        {
            return GetComicZipInfo(cmcPath, true).fileNames!
                .OrderBy(e => e, new NaturalStringComparer())
                .ToList();
        }
        private List<string> LoadImageEntriesFromFolder(string folderPath)
        {
            var files = Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories);

            return files.AsParallel()
                .Where(IsImageFile)
                .Select(f => Path.GetRelativePath(folderPath, f))
                .OrderBy(f => f, new NaturalStringComparer())
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
                using var archivePath = GetComicPath(comic.Key);
                MemoryStream? stream;
                if (archivePath.Extension.Equals(".cmc", StringComparison.OrdinalIgnoreCase))
                {
                    // 处理.cmc文件（tar包里的漫画包）
                    stream = LoadImageFromCmc(archivePath, entryName);
                }
                else if(Directory.Exists(archivePath))
                {
                    stream = LoadImageEntriesFromFolder(archivePath, entryName);
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

        private MemoryStream LoadImageEntriesFromFolder(string folderPath, string entryName)
        {
            var fullPath = Path.Combine(folderPath, entryName);
            var ms = new MemoryStream();
            using (var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read))
            {
                fs.CopyTo(ms);
            }
            ms.Position = 0;
            return ms;
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
