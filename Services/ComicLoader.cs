using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;
using ComicViewer.Models;

namespace ComicViewer.Services
{
    using ComicViewer.Database;
    using Microsoft.Extensions.Configuration;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Formats.Asn1;
    using System.IO;
    using System.IO.Compression;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Windows.Media.Imaging;
    using System.Xml.Linq;

    public class ComicLoader
    {
        private static readonly string COMIC_LIBRARY_PATH = Configs.GetFilePath();
        public async Task<ComicModel?> AddComicAsync(string path)
        {
            // 检查是否为.cmc文件
            if (Path.GetExtension(path).Equals(".cmc", StringComparison.OrdinalIgnoreCase))
            {
                var ret = await LoadFromCMCAsync(path);
                // remove the origin
                File.Delete(path);
                return ret;
            }

            var comic = CreateComicDataFromFile(path);
            if (ComicService.Instance.FindComic(comic.Key))
            {
                return null;
            }
            await MoveToComicLibrary(path, comic.Key);
            await ComicService.Instance.AddComicAsync(comic);
            return comic.GetComicModel();
        }
        private async Task<ComicModel?> LoadFromCMCAsync(string path)
        {
            // 提取.cmc中的.zip文件
            using var archive = ZipFile.OpenRead(path);

            // 读取metadata.json
            var metadataEntry = archive.GetEntry("metadata.json");
            if (metadataEntry == null) return null;

            using var metadataStream = metadataEntry.Open();
            using var reader = new StreamReader(metadataStream);
            string json = await reader.ReadToEndAsync();
            ComicMetadata? metadata = JsonSerializer.Deserialize<ComicMetadata>(json);
            if (metadata == null) return null;

            ComicData comic = metadata.ToComicData();
            if (ComicService.Instance.FindComic(comic.Key)) return null;

            // 找第一个.zip文件
            var zipEntry = archive.GetEntry("comic.zip");
            if (zipEntry == null) return null;

            // 提取到临时文件
            string filename = $"{comic.Key}.zip";
            string destPath = Path.Combine(COMIC_LIBRARY_PATH, filename);

            zipEntry.ExtractToFile(destPath, true);
            await ComicService.Instance.AddComicAsync(comic);

            return comic.GetComicModel();
        }
        private async Task MoveToComicLibrary(string path, string name)
        {
            string filename = $"{name}.zip";
            string destPath = Path.Combine(COMIC_LIBRARY_PATH, filename);
            await Task.Run(() => File.Move(path, destPath));
        }
        private ComicData CreateComicDataFromFile(string filePath)
        {
            // 获取文件名（不包含路径和扩展名）
            string fileName = Path.GetFileNameWithoutExtension(filePath);

            // 使用文件名作为Title
            string title = fileName;

            // 计算MD5作为Key
            string key = CalculateMD5(title);

            // 获取文件信息
            FileInfo fileInfo = new FileInfo(filePath);

            return new ComicData
            {
                Key = key,
                Title = title,
                // 从文件系统获取时间信息
                CreatedTime = fileInfo.CreationTime,
                LastAccess = fileInfo.LastAccessTime,
                // 设置默认值
                Progress = 0,           // 未开始阅读
                Rating = 0,             // 未评分
                                             // 集合属性可以初始化为空列表
                ComicTags = new List<ComicTag>()
            };
        }
        public static string CalculateMD5(string input)
        {
            using (var md5 = MD5.Create())
            {
                // 对于中文字符，使用UTF-8编码
                byte[] inputBytes = Encoding.UTF8.GetBytes(input);
                byte[] hashBytes = md5.ComputeHash(inputBytes);

                StringBuilder sb = new StringBuilder();
                foreach (byte b in hashBytes)
                {
                    sb.Append(b.ToString("x2"));
                }

                return sb.ToString();
            }
        }

        public async Task CreateSharePackageAsync(ComicModel comic, string destinationPath)
        {
            string sourceFilePath = Path.Combine(Configs.GetFilePath(), $"{comic.Key}.zip");
            var comicData = ComicService.Instance.GetComicData(comic.Key);

            // 使用MemoryStream避免临时文件
            await Task.Run(() =>
            {
                using var zip = ZipFile.Open(destinationPath, ZipArchiveMode.Create);

                // 1. 添加漫画文件
                zip.CreateEntryFromFile(sourceFilePath, $"comic.zip", CompressionLevel.Fastest);

                // 2. 添加metadata.json
                var metadata = comicData.ToComicMetadata();
                var metadataJson = JsonSerializer.Serialize(metadata);

                var metadataEntry = zip.CreateEntry("metadata.json", CompressionLevel.NoCompression);
                using var metadataStream = metadataEntry.Open();
                using var metadataWriter = new StreamWriter(metadataStream);
                metadataWriter.Write(metadataJson);
            });
        }
    }

        // 修改后的ComicMetadata类
    public class ComicMetadata
    {
        [JsonPropertyName("version")]
        public string Version { get; set; }
        [JsonPropertyName("title")]
        public string Title { get; set; }
        [JsonPropertyName("tags")]
        public string[] Tags { get; set; }
        [JsonPropertyName("system")]
        public SystemInfo System { get; set; }
        public ComicData ToComicData()
        {
            return new ComicData
            {
                Key = ComicLoader.CalculateMD5(Title), // 使用文件哈希或生成新Key
                Title = Title,
                CreatedTime = System?.CreatedTime,
                LastAccess = System?.LastAccess,
                Progress = System?.ReadProgress ?? 0,
                Rating = System?.Rating ?? 0
            };
        }
        public List<TagModel> GetTags()
        {
            return Tags.Select(tag => new TagModel
            {
                Key = ComicLoader.CalculateMD5(tag),
                Name = tag
            }).ToList();
        }
    }

    public class SystemInfo
    {
        [JsonPropertyName("created_time")]
        public DateTime? CreatedTime { get; set; }

        [JsonPropertyName("last_access")]
        public DateTime? LastAccess { get; set; }

        [JsonPropertyName("read_progress")]
        public int ReadProgress { get; set; }

        [JsonPropertyName("rating")]
        public int Rating { get; set; }
    }
}
