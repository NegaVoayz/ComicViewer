using ComicViewer.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ComicViewer.Database
{
    class ComicService
    {
        private readonly ComicContext _context = ComicContext.Instance;

        private static readonly Lazy<ComicService> _instance = new(() => new ComicService());

        public static ComicService Instance => _instance.Value;
        public bool FindComic(string comicKey)
        {
            return _context.Comics.Any(e => e.Key == comicKey);
        }
        public async Task AddComicAsync(ComicData comic)
        {
            await _context.Comics.AddAsync(comic);
        }
        public async Task RemoveComicAsync(string comicKey)
        {
            await _context.Comics.Where(e => e.Key == comicKey).ExecuteDeleteAsync();
        }
        public async Task<List<ComicModel>> GetAllComicsAsync()
        {
            // 先从数据库获取 ComicData
            var comicDatas = await _context.Comics
                .AsNoTracking()  // 提高性能，不需要追踪变更
                .ToListAsync();

            // 批量转换
            var comicModels = comicDatas.Select(c => c.GetComicModel()).ToList();

            return comicModels;
        }
        public async Task<List<TagModel>> GetAllTagsAsync()
        {
            return await _context.Tags
                .AsNoTracking()  // 提高性能，不需要追踪变更
                .ToListAsync();
        }
        public async Task<List<ComicModel>> GetComicsWithAllTagKeysAsync(List<string> tagKeys)
        {
            if (tagKeys == null || tagKeys.Count == 0)
                return await GetAllComicsAsync();

            var comicDatas = await _context.Comics
                .Where(comic => tagKeys.All(tagKey =>
                    _context.ComicTags
                        .Any(ct => ct.ComicKey == comic.Key && ct.TagKey == tagKey)
                ))
                .ToListAsync();

            // 批量转换
            var comicModels = comicDatas.Select(c => c.GetComicModel()).ToList();

            return comicModels;
        }
    }
}
