using ComicViewer.Models;
using SharpCompress.Common;
using SharpCompress.Writers;
using System.IO;
using System.Text;
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
            using var sourceFilePath = service.FileService.GetComicPath(comic.Key);
            if (!sourceFilePath.Extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                // nope, that's not what we want
                return;
            }
            var comicData = service.DataService.GetComicData(comic.Key);
            if (comicData == null)
            {
                return;
            }

            // if just export.
            if (Path.GetExtension(destinationPath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                string fileName = Path.GetFileNameWithoutExtension(destinationPath);
                string fullPath = destinationPath;
                if (fileName == comic.Title)
                {
                    string? directory = Path.GetDirectoryName(destinationPath);
                    var allTags = (await service.DataService.GetTagsOfComic(comic.Key))
                        .Select(e => e.Name);
                    var authors = allTags.Where(e => e.StartsWith(ComicUtils.AuthorPrefix))
                        .Select(e => e.Substring(ComicUtils.AuthorPrefix.Length));
                    var tags = allTags.Where(e => !e.StartsWith(ComicUtils.AuthorPrefix));
                    string processedName = ComicUtils.GetCombinedName(authors, comic.Title, tags);
                    processedName = $"{processedName}.zip";
                    if (directory != null)
                    {
                        fullPath = Path.Combine(directory, processedName);
                    }
                    else
                    {
                        fullPath = processedName;
                    }
                }
                await Task.Run(async () =>
                {
                    File.Copy(sourceFilePath, fullPath, true);
                    var metadata = comicData.ToComicMetadata();
                    var tags = await service.DataService.GetTagsOfComic(comicData.Key);
                    metadata.Tags = tags.Select(e => e.Name).ToList();
                    var metadataJson = JsonSerializer.Serialize(metadata);
                    ComicUtils.AddCommentToZip(fullPath, metadataJson);
                });
                MessageBox.Show("分享包创建成功！");
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
                MessageBox.Show("分享包创建成功！");
            });
        }
    }

    public class ComicMetadata
    {
        [JsonPropertyName("version")]
        public required string Version { get; set; }
        [JsonPropertyName("title")]
        public required string Title { get; set; }
        [JsonPropertyName("source")]
        public required string Source { get; set; }
        [JsonPropertyName("tags")]
        public required List<string> Tags { get; set; }
        [JsonPropertyName("system")]
        public required SystemInfo System { get; set; }
        public ComicData ToComicData()
        {
            return new ComicData
            {
                Key = ComicUtils.CalculateMD5(Title),
                Title = Title,
                Source = Source,
                CreatedTime = System?.CreatedTime ?? DateTime.Now,
                LastAccess = System?.LastAccess ?? DateTime.Now,
                Progress = System?.ReadProgress ?? 0,
                Rating = System?.Rating ?? 0
            };
        }
        public List<TagData> GetTags()
        {
            return Tags.Select(t =>
                new TagData
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
