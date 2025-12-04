using System.Text;
using System.Security.Cryptography;
using ComicViewer.Models;
using SharpCompress.Common;
using SharpCompress.Writers;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;

namespace ComicViewer.Services
{

    public class ComicExporter
    {
        private readonly ComicService service;

        public ComicExporter(ComicService service)
        {
            this.service = service;
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
            string sourceFilePath = service.FileService.GetComicPath(comic.Key);
            try
            {
                if (Path.GetExtension(sourceFilePath) != ".zip")
                {
                    // nope, that's not what we want
                    return;
                }
                var comicData = service.DataService.GetComicData(comic.Key);

                // if just export.
                if (Path.GetExtension(destinationPath) == ".zip")
                {
                    await Task.Run(() => File.Copy(sourceFilePath, destinationPath, true));
                    return;
                }

                await Task.Run(async () =>
                {
                    using var tarStream = File.Create(destinationPath);
                    using var tarWriter = WriterFactory.Open(tarStream, ArchiveType.Tar, new WriterOptions(CompressionType.None));

                    // 1. 添加漫画文件
                    tarWriter.Write("comic.zip", sourceFilePath);

                    // 2. 添加metadata.json
                    var metadata = comicData.ToComicMetadata();
                    var metadataJson = JsonSerializer.Serialize(metadata);
                    var metadataBytes = Encoding.UTF8.GetBytes(metadataJson);

                    // 使用MemoryStream添加内容
                    using var memoryStream = new MemoryStream(metadataBytes);
                    tarWriter.Write("metadata.json", memoryStream, DateTime.Now);

                    // 确保写入完成
                    tarWriter.Dispose();
                    await tarStream.FlushAsync();
                });
                MessageBox.Show("分享包创建成功！");
            }
            finally
            {
                service.FileService.ReleaseComicPath(comic.Key, sourceFilePath);
            }
        }
    }
    
    public class ComicMetadata
    {
        [JsonPropertyName("version")]
        public string Version { get; set; }
        [JsonPropertyName("title")]
        public string Title { get; set; }
        [JsonPropertyName("tags")]
        public List<string> Tags { get; set; }
        [JsonPropertyName("system")]
        public SystemInfo System { get; set; }
        public ComicData ToComicData()
        {
            return new ComicData
            {
                Key = ComicExporter.CalculateMD5(Title),
                Title = Title,
                CreatedTime = System?.CreatedTime,
                LastAccess = System?.LastAccess,
                Progress = System?.ReadProgress ?? 0,
                Rating = System?.Rating ?? 0
            };
        }
        public List<TagModel> GetTags()
        {
            return Tags.Select(t =>
                new TagModel
                {
                    Key = ComicExporter.CalculateMD5(t),
                    Name = t,
                    Count = 1
                }).ToList();
        }
        public List<string> GetTagKeys()
        {
            return Tags.Select(t => ComicExporter.CalculateMD5(t)).ToList();
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
