using ComicViewer.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ComicViewer.Database
{
    class ComicService
    {
        private readonly IDbContextFactory<ComicContext> _contextFactory = new PooledDbContextFactory<ComicContext>(
            new DbContextOptionsBuilder<ComicContext>()
            .UseSqlite($"Data Source={Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "comics.db")}")
            .Options);

        private static readonly Lazy<ComicService> _instance = new(() => new ComicService());
        public static ComicService Instance => _instance.Value;

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

            context.Comics.Remove(comic);
            await context.SaveChangesAsync();
            context.Entry(comic).State = EntityState.Detached;
        }
        public async Task<List<ComicModel>> GetAllComicsAsync()
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            // 先从数据库获取 ComicData
            var comicDatas = await context.Comics
                .AsNoTracking()  // 提高性能，不需要追踪变更
                .ToListAsync();

            // 批量转换
            var comicModels = comicDatas.Select(c => c.ToComicModel()).ToList();

            return comicModels;
        }
        public async Task<List<TagModel>> GetAllTagsAsync()
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            return await context.Tags
                .AsNoTracking()  // 提高性能，不需要追踪变更
                .ToListAsync();
        }
        public async Task<List<ComicModel>> GetComicsWithAllTagKeysAsync(List<string> tagKeys)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            if (tagKeys == null || tagKeys.Count == 0)
                return await GetAllComicsAsync();

            var comicDatas = await context.Comics
                .Where(comic => tagKeys.All(tagKey =>
                    context.ComicTags
                        .Any(ct => ct.ComicKey == comic.Key && ct.TagKey == tagKey)
                ))
                .ToListAsync();

            // 批量转换
            var comicModels = comicDatas.Select(c => c.ToComicModel()).ToList();

            return comicModels;
        }
    }
}
