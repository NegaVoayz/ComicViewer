using ComicViewer.Models;
using SharpCompress.Archives.Tar;
using System.IO;
using System.Text.Json;

namespace ComicViewer.Services
{
    public class ComicLoader
    {
        private readonly ComicService service;

        public ComicLoader(ComicService service)
        {
            this.service = service;
        }

        public async Task<ComicData?> AddComicAsync(string filePath)
        {
            filePath = ComicUtils.GetFileRealPath(filePath);
            ComicData comic;
            ComicMetadata? comicMetadata = null;
            if (Path.GetExtension(filePath) == ".cmc")
            {
                using var archive = TarArchive.Open(filePath);

                var tarEntry = archive.Entries.FirstOrDefault(e => e.Key != null &&
                    e.Key.Equals("metadata.json", StringComparison.OrdinalIgnoreCase));
                if (tarEntry == null || tarEntry.IsDirectory) return null;

                using var entryStream = tarEntry.OpenEntryStream();
                using var reader = new StreamReader(entryStream);
                var data = await reader.ReadToEndAsync();

                comicMetadata = JsonSerializer.Deserialize<ComicMetadata>(data);
                if (comicMetadata == null) return null;
            }
            else
            {
                comicMetadata = ComicUtils.CreateComicDataFromFilePath(filePath);
            }
            comic = comicMetadata.ToComicData();
            await service.DataService.AddTagsAsync(comicMetadata.Tags);

            if (service.DataService.FindComic(comic.Key)) return null;

            if (Path.GetExtension(filePath).Equals(".zip", StringComparison.OrdinalIgnoreCase)
                && Path.GetPathRoot(filePath) == Path.GetPathRoot(Configs.GetFilePath()))
            {
                File.Move(filePath, ComicUtils.ComicNormalPath(comic.Key));
                await service.DataService.AddComicAsync(comic);
            }
            else
            {
                service.FileService.AddComicTempPath(comic.Key, filePath);
                await service.DataService.AddComicAsync(comic);
                await service.FileLoader.AddMovingTask(comic.Key, filePath);
            }

            await service.DataService.AddTagsToComicAsync(comic.Key, comicMetadata.GetTagKeys());
            await service.Cache.RefreshTagsAsync();
            await service.Cache.AddComic(comic);
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
