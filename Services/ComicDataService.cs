using ComicViewer.Database;
using ComicViewer.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ComicViewer.Services
{
    public class ComicDataService
    {
        private readonly IDbContextFactory<ComicContext> _contextFactory = new PooledDbContextFactory<ComicContext>(
            new DbContextOptionsBuilder<ComicContext>()
            .UseSqlite($"Data Source={Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "comics.db")}")
            .Options);

        private readonly ComicService service;

        public ComicDataService(ComicService service)
        {
            this.service = service;
        }

        public async Task AddTagAsync(string tagName)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            var tagKey = ComicExporter.CalculateMD5(tagName);

            var existingTag = await context.Tags.FirstOrDefaultAsync(t => t.Key == tagKey);

            if(existingTag != null)
            {
                return;
            }
            var tag = new TagModel
            {
                Key = tagKey,
                Name = tagName,
                Count = 0
            };
            await context.Tags.AddAsync(tag);

            await context.SaveChangesAsync();
        }

        public async Task AddTagsAsync(IEnumerable<string> tagNames)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            var existingTags = await context.Tags
                    .Where(t => tagNames.Contains(t.Name))
                    .ToDictionaryAsync(t => t.Name, t => t);

            foreach (var tagName in tagNames)
            {
                if (!existingTags.TryGetValue(tagName, out var tag))
                {
                    // 不存在，创建
                    var newTag = new TagModel
                    {
                        Key = ComicExporter.CalculateMD5(tagName),
                        Name = tagName,
                        Count = 0
                    };
                    context.Tags.Add(newTag);
                }
            }

            await context.SaveChangesAsync();
        }

        public async Task AddTagToComicAsync(string comicKey, string tagName)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            var tag = await context.Tags.FirstAsync(e => e.Key == ComicExporter.CalculateMD5(tagName));
            if(tag == null)
            {
                return;
            }
            tag.Count++;

            await context.ComicTags.AddAsync(
                new ComicTag
                {
                    ComicKey = comicKey,
                    TagKey = tag.Key
                }
            );

            await context.SaveChangesAsync();
        }

        public async Task AddTagsToComicAsync(string comicKey, IEnumerable<string> tagKeys)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            await context.Tags.Where(e => tagKeys.Contains(e.Key)).ForEachAsync(e => e.Count++);

            await context.ComicTags.AddRangeAsync(
                tagKeys.Select(e =>
                new ComicTag
                {
                    ComicKey = comicKey,
                    TagKey = e
                })
            );

            await context.SaveChangesAsync();
        }

        public MovingFileModel? GetMovingTask(string Key)
        {
            using var context = _contextFactory.CreateDbContext();
            return context.MovingFiles.FirstOrDefault(e => e.Key == Key);
        }
        public async Task UpdateMovingTaskAsync(MovingFileModel model)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            context.MovingFiles.Update(model);
            await context.SaveChangesAsync();
        }
        public async Task AddMovingTask(MovingFileModel model)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            context.MovingFiles.Add(model);
            context.SaveChanges();
        }
        public async Task DoneMovingTaskAsync(MovingFileModel model)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            context.MovingFiles.Remove(model);
            await context.SaveChangesAsync();
        }
        public async Task<List<MovingFileModel>> GetAllMovingFilesAsync()
        {
            // 先从数据库获取 ComicData
            await using var context = await _contextFactory.CreateDbContextAsync();
            return await context.MovingFiles
                .AsNoTracking()  // 提高性能，不需要追踪变更
                .ToListAsync();
        }
        public async Task UpdateComicAsync(ComicData comic)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            context.Comics.Update(comic);
            await context.SaveChangesAsync();
        }
        public ComicData? GetComicData(string comicKey)
        {
            using var context = _contextFactory.CreateDbContext();
            return context.Comics.FirstOrDefault(e => e.Key == comicKey);
        }
        public bool FindComic(string comicKey)
        {
            using var context = _contextFactory.CreateDbContext();
            return context.Comics.Any(e => e.Key == comicKey);
        }
        public async Task AddComicAsync(ComicData comic)
        {
            await using var context = _contextFactory.CreateDbContext();
            context.Comics.Add(comic);
            await context.SaveChangesAsync();
        }
        public async Task RemoveComicAsync(string comicKey)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var comic = await context.Comics.FindAsync(comicKey);
            if (comic == null) return;

            var comicTags = comic.ComicTags;
            if(comicTags != null)
            {
                context.Tags.UpdateRange(
                    comicTags.Select(ct => new TagModel
                    {
                        Key = ct.Tag.Key,
                        Name = ct.Tag.Name,
                        Count = ct.Tag.Count - 1
                    })
                );
            }
            // comicTags are automatically removed by CASCADE DELETE constraint

            context.Comics.Remove(comic);
            await context.SaveChangesAsync();
            context.Entry(comic).State = EntityState.Detached;
        }
        public async Task<List<ComicData>> GetAllComicsAsync()
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            return await context.Comics
                .AsNoTracking()  // 提高性能，不需要追踪变更
                .ToListAsync();
        }
        public async Task<List<TagModel>> GetAllTagsAsync()
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            return await context.Tags
                .AsNoTracking()  // 提高性能，不需要追踪变更
                .ToListAsync();
        }
        public async Task<List<ComicData>> GetComicsWithAllTagKeysAsync(List<string> tagKeys)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            if (tagKeys == null || tagKeys.Count == 0)
                return await GetAllComicsAsync();

            return await context.Comics
                .Where(comic => tagKeys.All(tagKey =>
                    context.ComicTags
                        .Any(ct => ct.ComicKey == comic.Key && ct.TagKey == tagKey)
                ))
                .ToListAsync();
        }
    }
}
