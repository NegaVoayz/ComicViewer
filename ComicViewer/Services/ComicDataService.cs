using ComicViewer.Database;
using ComicViewer.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.IO;

namespace ComicViewer.Services
{
    public class ComicDataService
    {
        private readonly IDbContextFactory<ComicContext> _contextFactory;

        private readonly ComicService service;

        public ComicDataService(ComicService service)
        {
            this.service = service;

            string dbPath = Path.Combine(Configs.UserDataPath, "comics.db");

            // 确保目录存在
            Directory.CreateDirectory(Configs.UserDataPath);

            _contextFactory = new PooledDbContextFactory<ComicContext>(
                new DbContextOptionsBuilder<ComicContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options);
        }

        public async Task<TagData> AddTagAsync(string tagName)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            var tagKey = ComicUtils.CalculateMD5(tagName);

            var existingTag = await context.Tags.FirstOrDefaultAsync(t => t.Key == tagKey);

            if (existingTag != null)
            {
                return existingTag;
            }
            var tag = new TagData
            {
                Key = tagKey,
                Name = tagName,
                Count = 0
            };
            await context.Tags.AddAsync(tag);

            await context.SaveChangesAsync();

            return tag;
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
                    var newTag = new TagData
                    {
                        Key = ComicUtils.CalculateMD5(tagName),
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

            var tag = await context.Tags.FirstAsync(e => e.Key == ComicUtils.CalculateMD5(tagName));
            if (tag == null)
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

        public async Task RemoveTagFromComicAsync(string comicKey, string tagName)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            var tag = await context.Tags.FirstAsync(e => e.Key == ComicUtils.CalculateMD5(tagName));
            if (tag == null)
            {
                return;
            }
            tag.Count--;

            context.ComicTags.Remove(
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

        public async Task ChangeTagsToComicAsync(string comicKey, IEnumerable<string> tagKeys)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            HashSet<string> newTagKeysSet = new(tagKeys);
            var oldTagKeysSet = context.ComicTags.Where(e => e.ComicKey == comicKey).Select(e => e.TagKey).ToHashSet();

            var removedTagKeys = oldTagKeysSet.Except(newTagKeysSet).ToHashSet();  // 在旧不在新
            var addedTagKeys = newTagKeysSet.Except(oldTagKeysSet).ToHashSet();    // 在新不在旧

            if (addedTagKeys.Any())
            {
                await context.Tags.Where(e => addedTagKeys.Contains(e.Key)).ForEachAsync(e => e.Count++);
                await context.ComicTags.AddRangeAsync(
                    addedTagKeys.Select(e =>
                    new ComicTag
                    {
                        ComicKey = comicKey,
                        TagKey = e
                    })
                );
            }

            if (removedTagKeys.Any())
            {
                await context.ComicTags.Where(e => e.ComicKey == comicKey && removedTagKeys.Contains(e.TagKey))
                    .ExecuteDeleteAsync();
                await context.Tags.Where(e => e.Count == 0 || e.Count == 1 && removedTagKeys.Contains(e.Key))
                    .ExecuteDeleteAsync();
                await context.Tags.Where(e => e.Count > 1 && removedTagKeys.Contains(e.Key)).ForEachAsync(e => e.Count--);
            }

            await context.SaveChangesAsync();
        }

        public async Task<List<TagData>> GetTagsOfComic(string comicKey)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            return context.ComicTags
                .Where(e => e.ComicKey == comicKey)
                .Select(e => e.Tag).ToList();
        }

        public async Task<string> GetTagsPreviewOfComic(string comicKey)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            List<string> tags = await Task.Run(() => context.ComicTags.Where(e => e.ComicKey == comicKey).Include(e => e.Tag)
                .Select(e => e.Tag.Name)
                .Take(4).ToList());

            if (!tags.Any())
            {
                return "TagMe";
            }

            if (tags.Count() == 4)
            {
                var temp = tags.Last();
                temp = "...";
            }

            return string.Join(", ", tags);
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

            var comicTags = context.ComicTags.Where(e => e.ComicKey == comicKey).Select(e => e.TagKey).ToHashSet();
            if (comicTags != null)
            {
                // comicTags are automatically removed by CASCADE DELETE constraint
                // But I don't trust it
                await context.ComicTags.Where(e => e.ComicKey == comicKey && comicTags.Contains(e.TagKey))
                    .ExecuteDeleteAsync();
                await context.Tags.Where(e => e.Count == 0 || e.Count == 1 && comicTags.Contains(e.Key))
                    .ExecuteDeleteAsync();
                await context.Tags.Where(e => e.Count > 1 && comicTags.Contains(e.Key)).ForEachAsync(e => e.Count--);
            }

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
        public async Task<List<TagData>> GetAllTagsAsync()
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
