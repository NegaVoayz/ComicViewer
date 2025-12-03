using ComicViewer.Database;
using ComicViewer.Models;
using SharpCompress.Archives.Tar;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ComicViewer.Services
{
    class ComicLoader
    {
        private ComicData CreateComicDataFromFile(string filePath)
        {
            // 获取文件名（不包含路径和扩展名）
            string fileName = Path.GetFileNameWithoutExtension(filePath);

            // 使用文件名作为Title
            string title = fileName;

            // 计算MD5作为Key
            string key = ComicExporter.CalculateMD5(title);

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

        public async Task<ComicModel?> AddComicAsync(string filePath)
        {
            ComicData comic;
            if (Path.GetExtension(filePath) == ".cmc")
            {
                using var archive = TarArchive.Open(filePath);

                var tarEntry = archive.Entries.FirstOrDefault(e =>
                    e.Key.Equals("metadata.json", StringComparison.OrdinalIgnoreCase));
                if (tarEntry == null || tarEntry.IsDirectory) return null;

                using var entryStream = tarEntry.OpenEntryStream();
                using var reader = new StreamReader(entryStream);
                var data = await reader.ReadToEndAsync();

                var comicMetadata = JsonSerializer.Deserialize<ComicMetadata>(data);
                if (comicMetadata == null) return null;

                comic = comicMetadata.ToComicData();
            }
            else
            {
                comic = CreateComicDataFromFile(filePath);
            }
            if (ComicService.Instance.FindComic(comic.Key)) return null;

            SilentFileLoader.Instance.AddMovingTask(new MovingFileModel
            {
                Key = comic.Key,
                SourcePath = filePath,
                DestinationPath = Path.Combine(Configs.GetFilePath(), $"{comic.Key}.zip"),
            });
            await ComicService.Instance.AddComicAsync(comic);
            return comic.ToComicModel();
        }
    }
}
