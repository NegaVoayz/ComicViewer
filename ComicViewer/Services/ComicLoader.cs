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
                if (Path.GetExtension(filePath) == ".zip")
                {
                    var comment = ComicUtils.GetCommentOfZip(filePath);
                    comicMetadata = JsonSerializer.Deserialize<ComicMetadata>(comment);
                }
                if (comicMetadata == null)
                {
                    comicMetadata = ComicUtils.CreateComicDataFromFilePath(filePath);
                }
            }
            var mappedTags = await GetMappedTagNames(comicMetadata.Tags);
            comicMetadata.Tags = [.. mappedTags.resolvedTags];
            comic = comicMetadata.ToComicData();
            await service.DataService.AddTagsAsync(mappedTags.newTags);

            if (service.DataService.FindComic(comic.Key)) return null;

            var dest = ComicUtils.ComicNormalPath(comic.Title);
            if (Path.GetExtension(filePath).Equals(".zip", StringComparison.OrdinalIgnoreCase)
                && Path.GetPathRoot(filePath) == Path.GetPathRoot(Configs.GetFilePath()))
            {
                if (!File.Exists(dest))
                    File.Move(filePath, dest);
                service.FileService.AddComicPath(comic.Key, dest);
                await service.DataService.AddComicAsync(comic);
            }
            else
            {
                service.FileService.AddComicPath(comic.Key, filePath);
                await service.DataService.AddComicAsync(comic);
                await service.FileLoader.AddMovingTask(new MovingFileModel
                {
                    Key = comic.Key,
                    SourcePath = filePath,
                    DestinationPath = dest
                });
            }

            await service.DataService.AddTagsToComicAsync(comic.Key, comicMetadata.GetTagKeys());
            await service.Cache.RefreshTagsAsync();
            await service.Cache.AddComic(comic);
            return comic;
        }

        public async Task MigrateComicLibrary(string sourcePath, string destinationPath)
        {
            var comics = await service.DataService.GetAllComicsAsync();
            if (Path.GetPathRoot(sourcePath) == Path.GetPathRoot(destinationPath))
            {
                Directory.Move(sourcePath, destinationPath);
                return;
            }
            foreach (var comic in comics)
            {
                var fileName = $"{comic.Key}.zip";
                var sourceFilePath = Path.Combine(sourcePath, fileName);
                var destFilePath = Path.Combine(destinationPath, fileName);
                service.FileService.AddComicPath(comic.Key, sourceFilePath);
                await service.FileLoader.AddMovingTask(new MovingFileModel
                {
                    Key = comic.Key,
                    SourcePath = sourceFilePath,
                    DestinationPath = destFilePath
                });
            }
        }

        private async Task<(IEnumerable<string> resolvedTags, IEnumerable<string> newTags)> GetMappedTagNames(IEnumerable<string> initialTags)
        {
            HashSet<string> resolvedTags = new();
            HashSet<string> newTags = new();
            foreach (var tag in initialTags)
            {
                if (resolvedTags.Contains(tag))
                    continue;
                var standardTag = await service.DataService.FindTagNameByAliasAsync(tag);
                if (standardTag == null)
                    newTags.Add(tag);
                resolvedTags.Add(standardTag ?? tag);
            }
            return (resolvedTags, newTags);
        }
    }
}
