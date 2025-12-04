using ComicViewer.Models;
using SharpCompress.Archives.Tar;
using SharpCompress.Common;
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
    public class ComicLoader
    {
        private readonly ComicService service;

        public ComicLoader(ComicService service)
        {
            this.service = service;
            _ = RecoverLoads();
        }

        private async Task RecoverLoads()
        {
            var movingTasks = await service.DataService.GetAllMovingFilesAsync();
            foreach (var movingTask in movingTasks)
            {
                service.FileService.AddComicTempPath(movingTask.Key, movingTask.SourcePath);
                await service.FileLoader.AddMovingTask(movingTask);
            }
        }

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

        public async Task<ComicData?> AddComicAsync(string filePath)
        {
            ComicData comic;
            ComicMetadata? comicMetadata = null;
            if (Path.GetExtension(filePath) == ".cmc")
            {
                using var archive = TarArchive.Open(filePath);

                var tarEntry = archive.Entries.FirstOrDefault(e =>
                    e.Key.Equals("metadata.json", StringComparison.OrdinalIgnoreCase));
                if (tarEntry == null || tarEntry.IsDirectory) return null;

                using var entryStream = tarEntry.OpenEntryStream();
                using var reader = new StreamReader(entryStream);
                var data = await reader.ReadToEndAsync();

                comicMetadata = JsonSerializer.Deserialize<ComicMetadata>(data);
                if (comicMetadata == null) return null;

                comic = comicMetadata.ToComicData();
                await service.DataService.AddTagsAsync(comicMetadata.Tags);
            }
            else
            {
                comic = CreateComicDataFromFile(filePath);
            }
            if (service.DataService.FindComic(comic.Key)) return null;

            service.FileService.AddComicTempPath(comic.Key, filePath);
            await service.DataService.AddComicAsync(comic);
            if (comicMetadata != null)
            {
                await service.DataService.AddTagsToComicAsync(comic.Key, comicMetadata.GetTagKeys());
            }
            await service.Cache.AddComic(comic);
            await service.FileLoader.AddMovingTask(comic.Key, filePath);
            return comic;
        }

        public async Task MigrateComicLibrary(string sourcePath, string destinationPath)
        {
            var comics = await service.DataService.GetAllComicsAsync();
            foreach (var comic in comics)
            {
                var fileName = $"{comic.Key}.zip";
                var sourceFilePath = Path.Combine(sourcePath, fileName);
                var destFilePath = Path.Combine(destinationPath, fileName);
                service.FileService.AddComicTempPath(comic.Key, sourceFilePath);
                await service.FileLoader.AddMovingTask(new MovingFileModel
                {
                    Key = comic.Key,
                    SourcePath = sourceFilePath,
                    DestinationPath = destFilePath
                });
            }
        }
    }
}
