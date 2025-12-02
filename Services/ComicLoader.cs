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
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Windows.Media.Imaging;

    public class ComicLoader
    {
        private static readonly string COMIC_LIBRARY_PATH = Configs.GetFilePath();

        public async Task<ComicModel?> AddComicAsync(string path)
        {
            var comic = CreateComicDataFromFile(path);
            if(ComicService.Instance.FindComic(comic.Key))
            {
                return null;
            }
            await MoveToComicLibrary(path, comic.Key);
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
                TagsPreview = string.Empty,  // 空标签预览
                                             // 集合属性可以初始化为空列表
                ComicTags = new List<ComicTag>()
            };
        }
        private string CalculateMD5(string input)
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
