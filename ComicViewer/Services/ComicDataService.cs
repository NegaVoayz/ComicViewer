using ComicViewer.Database;
using ComicViewer.Infrastructure;
using ComicViewer.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.IO;
using System.Security.Cryptography;

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

            service.Load.Add(new DAGTask
            {
                name = "DataService",
                task = MigrateDatabase
            });
        }

        private async Task MigrateDatabase()
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            await context.Database.MigrateAsync();
            await context.SaveChangesAsync();
        }

        public async Task<List<TagAlias>> GetAllTagAliasesAsync()
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            return await context.TagAliases
                .AsNoTracking()  // 提高性能，不需要追踪变更
                .ToListAsync();
        }

        /**
         * get the standard tag name of an alias
         * returns null if not found
         */
        public async Task<string?> FindTagNameByAliasAsync(string tagName)
        {
            string standardName;
            {
                await using var context = await _contextFactory.CreateDbContextAsync();
                var aliasEntry = context.TagAliases.FirstOrDefault(e => e.Alias == tagName);
                if (aliasEntry == null)
                    standardName = tagName;
                else
                    standardName = aliasEntry.Name;
                var tag = context.Tags.FirstOrDefault(e => e.Name == standardName);
                if (tag != null)
                    return tag.Name;
                if (aliasEntry == null)
                {
                    return null;
                }
            }
            await AddTagAsync(standardName);
            return standardName;
        }

        /**
         * get the standard tag name of an alias
         * returns input if not an alias
         */
        public async Task<string> ResolveTagNameAsync(string tagName)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var aliasEntry = context.TagAliases.FirstOrDefault(e => e.Alias == tagName);
            if (aliasEntry == null)
                return tagName;
            else
                return aliasEntry.Name;
        }
        public async Task<bool> AddTagAliasAsync(string tagAlias, string tagName)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            if (context.TagAliases.Any(e => e.Alias == tagAlias))
                return false;
            await context.TagAliases.AddAsync(new TagAlias
            {
                Alias = tagAlias,
                Name = await ResolveTagNameAsync(tagName)
            });
            await context.SaveChangesAsync();
            return true;
        }
        public async Task<bool> AddTagAliasesAsync(IEnumerable<TagAlias> tagAliases)
        {
            bool tagChanged = false;
            var tagAliasesSet = tagAliases.ToHashSet();
            var deprecatedTagNames = tagAliasesSet.Select(e => e.Alias).ToHashSet();
            HashSet<string> affectedTagNames;
            using (var context = await _contextFactory.CreateDbContextAsync())
            {
                affectedTagNames = await context.Tags
                    .Where(e => deprecatedTagNames.Contains(e.Name))
                    .Select(e => e.Name)
                    .ToHashSetAsync();

            }
            if (affectedTagNames.Any())
            {
                tagChanged = true;
                var affectedTagAliases = tagAliasesSet.Where(e => affectedTagNames.Contains(e.Alias));

                // find new tags to be created
                HashSet<string> newTagNames = affectedTagAliases.Select(e => e.Name).ToHashSet();
                using (var context = await _contextFactory.CreateDbContextAsync())
                {
                    var existingNames = await context.Tags
                        .Where(t => newTagNames.Contains(t.Name))
                        .Select(t => t.Name)
                        .ToListAsync();
                    newTagNames.ExceptWith(existingNames);
                }
                // add them
                await AddTagsAsync(newTagNames);

                foreach (var entry in affectedTagAliases)
                {
                    await ReplaceTagAsync(entry.Alias, entry.Name);
                }
            }
            using (var context = await _contextFactory.CreateDbContextAsync())
            {
                await context.TagAliases.AddRangeAsync(tagAliasesSet);
                await context.SaveChangesAsync();
            }
            return tagChanged;
        }
        public async Task RemoveTagAliasesAsync(IEnumerable<TagAlias> tagAliases)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            await context.TagAliases.Where(e => tagAliases.Contains(e))
                .ExecuteDeleteAsync();

            await context.SaveChangesAsync();
        }

        public async Task<bool> ChangeTagAliasesAsync(IEnumerable<TagAlias> tagAliases)
        {
            HashSet<TagAlias> addedAliases;
            HashSet<TagAlias> removedAliases;
            using (var context = await _contextFactory.CreateDbContextAsync())
            {
                var newAliases = tagAliases.ToHashSet();
                var oldAliases = await context.TagAliases
                    .AsNoTracking()  // 提高性能，不需要追踪变更
                    .ToHashSetAsync();

                removedAliases = oldAliases.Except(newAliases).ToHashSet();  // 在旧不在新
                addedAliases = newAliases.Except(oldAliases).ToHashSet();    // 在新不在旧
            }
            return await ChangeTagAliasesAsync(addedAliases, removedAliases);
        }
        public async Task<bool> ChangeTagAliasesAsync(HashSet<TagAlias> addedAliases, HashSet<TagAlias> removedAliases)
        {
            await RemoveTagAliasesAsync(removedAliases);
            return await AddTagAliasesAsync(addedAliases);
        }
        public async Task ReplaceTagAsync(string deprecatedTagName, string standardTagName)
        {
            await ReplaceTagAliasAsync(deprecatedTagName, standardTagName);
            await ReplaceComicTagAsync(
                ComicUtils.CalculateMD5(deprecatedTagName),
                ComicUtils.CalculateMD5(standardTagName));
        }
        private async Task ReplaceTagAliasAsync(string deprecatedTagName, string standardTagName)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            await context.TagAliases.Where(e => e.Name == deprecatedTagName)
                .ExecuteUpdateAsync(setters => setters.SetProperty(e => e.Name, standardTagName));
            await context.SaveChangesAsync();
        }
        private async Task ReplaceComicTagAsync(string deprecatedTagKey, string standardTagKey)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            using var transaction = await context.Database.BeginTransactionAsync();

            try
            {
                // 1. 查询需要迁移的记录
                var comicsToMigrate = context.ComicTags
                    .Where(e => e.TagKey == deprecatedTagKey)
                    .Select(e => e.ComicKey)
                    .ToList();

                // 2. 查询已存在的标准TagKey记录
                var existingStandardTags = context.ComicTags
                    .Where(e => e.TagKey == standardTagKey)
                    .Select(e => e.ComicKey)
                    .ToHashSet();

                // 3. 删除旧记录
                int deletedCount = context.ComicTags
                    .Where(e => e.TagKey == deprecatedTagKey)
                    .ExecuteDelete();

                // 4. 只插入不存在的记录
                var tagsToInsert = comicsToMigrate
                    .Where(c => !existingStandardTags.Contains(c))
                    .Select(c => new ComicTag
                    {
                        ComicKey = c,
                        TagKey = standardTagKey
                    })
                    .ToList();

                if (tagsToInsert.Any())
                {
                    context.ComicTags.AddRange(tagsToInsert);
                    context.Tags.Where(e => e.Key == standardTagKey).ExecuteUpdate(setters =>
                        setters.SetProperty(e => e.Count, e => e.Count + tagsToInsert.Count));
                }
                context.Tags.Where(e => e.Key == deprecatedTagKey).ExecuteDelete();
                await context.SaveChangesAsync();

                await transaction.CommitAsync();
                return;
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<TagData> AddTagAsync(string tagName)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            var tagKey = ComicUtils.CalculateMD5(tagName);

            var existingTag = await context.Tags.AsNoTracking().FirstOrDefaultAsync(t => t.Key == tagKey);

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
            context.Tags.Add(tag);

            await context.SaveChangesAsync();

            var ret = context.Tags.AsNoTracking().FirstOrDefault(e => e.Key == tagKey);
            if(ret == null)
            {
                throw new InvalidDataException();
            }

            return ret;
        }

        public async Task AddTagsAsync(IEnumerable<string> tagNames)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            await context.Tags.AddRangeAsync(
                tagNames.Select(e => new TagData
                    {
                        Key = ComicUtils.CalculateMD5(e),
                        Name = e,
                        Count = 0
                    }));
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

        public async Task RemoveTagsFromComicAsync(string comicKey, IEnumerable<string> tagName)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            var tagKeys = tagName.Select(e => ComicUtils.CalculateMD5(e)).ToHashSet();

            await context.ComicTags
                .Where(e => e.ComicKey == comicKey && tagKeys.Contains(e.ComicKey))
                .ExecuteDeleteAsync();

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
                await context.Tags.Where(e => e.Count > 1 && removedTagKeys.Contains(e.Key)).ForEachAsync(e => e.Count--);
                await context.Tags.Where(e => e.Count == 0 || e.Count == 1 && removedTagKeys.Contains(e.Key))
                    .ExecuteDeleteAsync();
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

        public async Task<ComicData?> RenameComic(string comicKey, string newName)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            var newKey = ComicUtils.CalculateMD5(newName);

            if (comicKey == newKey)
            {
                return null;
            }
            // nothing is changed
            if (context.Comics.Any(e => e.Key == newKey))
            {
                return null;
            }

            using var transaction = await context.Database.BeginTransactionAsync();
            try
            {
                var comic = context.Comics.FirstOrDefault(e => e.Key == comicKey);
                if (comic == null)
                {
                    throw new InvalidDataException("Comic not found");
                }

                var newComic = new ComicData
                {
                    Key = newKey,
                    Title = newName,
                    Source = comic.Source,
                    CreatedTime = comic.CreatedTime,
                    LastAccess = comic.LastAccess,
                    Progress = comic.Progress,
                    Rating = comic.Rating
                };

                context.Comics.Add(newComic);
                await context.SaveChangesAsync();

                await context.ComicTags.Where(e => e.ComicKey == comic.Key)
                    .ExecuteUpdateAsync(setter => setter.SetProperty(e => e.ComicKey, newKey));

                if (await service.FileLoader.StopMovingTask(comicKey))
                    await service.FileLoader.AddMovingTask(newKey,
                        service.FileService.GetComicPath(comicKey));
                else
                    File.Move(
                        ComicUtils.ComicNormalPath(comicKey),
                        ComicUtils.ComicNormalPath(newKey));

                await service.FileService.RemoveComicAsync(comicKey);
                context.Comics.Remove(comic);
                await context.SaveChangesAsync();

                await transaction.CommitAsync();

                context.Entry(newComic).State = EntityState.Detached;
                return newComic;
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
}
    }
}
