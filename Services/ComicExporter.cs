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
                    var tags = await service.DataService.GetTagsOfComic(comicData.Key);
                    metadata.Tags = tags.Select(e => e.Name).ToList();
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
                Key = ComicUtils.CalculateMD5(Title),
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
                    Key = ComicUtils.CalculateMD5(t),
                    Name = t,
                    Count = 1
                }).ToList();
        }
        public List<string> GetTagKeys()
        {
            return Tags.Select(t => ComicUtils.CalculateMD5(t)).ToList();
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
