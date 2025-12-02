using ComicViewer.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ComicViewer.Database
{
    class ComicContext : DbContext
    {
        public static readonly Lazy<ComicContext> _instance = new(() => new ComicContext());
        public static ComicContext Instance => _instance.Value;
        public DbSet<ComicData> Comics { get; set; }
        public DbSet<TagModel> Tags { get; set; }
        public DbSet<ComicTag> ComicTags { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite("Data Source=comics.db");
        }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // 配置 ComicData 主键
            modelBuilder.Entity<ComicData>()
                .HasKey(c => c.Key);

            // 配置 TagModel 主键
            modelBuilder.Entity<TagModel>()
                .HasKey(t => t.Key);

            // 配置 ComicTag 复合主键
            modelBuilder.Entity<ComicTag>()
                .HasKey(ct => new { ct.ComicKey, ct.TagKey });

            // 配置 ComicTag 与 ComicData 的关系
            modelBuilder.Entity<ComicTag>()
                .HasOne(ct => ct.Comic)
                .WithMany(c => c.ComicTags)
                .HasForeignKey(ct => ct.ComicKey)
                .OnDelete(DeleteBehavior.Cascade);

            // 配置 ComicTag 与 TagModel 的关系
            modelBuilder.Entity<ComicTag>()
                .HasOne(ct => ct.Tag)
                .WithMany(t => t.ComicTags)
                .HasForeignKey(ct => ct.TagKey)
                .OnDelete(DeleteBehavior.Cascade);

            // 可选：添加索引优化查询性能
            modelBuilder.Entity<ComicTag>()
                .HasIndex(ct => ct.ComicKey);

            modelBuilder.Entity<ComicTag>()
                .HasIndex(ct => ct.TagKey);
        }
    }   
}
