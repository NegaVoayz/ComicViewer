using ComicViewer.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
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
            string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "comics.db");

            // 确保目录存在
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath));

            optionsBuilder.UseSqlite($"Data Source={dbPath}");
        }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // 配置 ComicData 主键
            modelBuilder.Entity<ComicData>(entity =>
            {
                entity.ToTable("Comics");

                entity.HasKey(e => e.Key);

                entity.Property(e => e.Key)
                      .HasColumnName("Key")
                      .HasColumnType("VARCHAR(32)")
                      .HasMaxLength(32)
                      .IsRequired();

                entity.Property(e => e.Title)
                      .HasColumnName("Title")
                      .HasColumnType("TEXT")
                      .IsRequired(false);

                entity.Property(e => e.CreatedTime)
                      .HasColumnName("CreatedTime")
                      .HasColumnType("DATETIME");

                entity.Property(e => e.LastAccess)
                      .HasColumnName("LastAccess")
                      .HasColumnType("DATETIME");

                entity.Property(e => e.Progress)
                      .HasColumnName("Progress")
                      .HasColumnType("INTEGER")
                      .HasDefaultValue(0);

                entity.Property(e => e.Rating)
                      .HasColumnName("Rating")
                      .HasColumnType("INTEGER")
                      .HasDefaultValue(0);

                // 导航属性配置
                entity.HasMany(e => e.ComicTags)
                      .WithOne(ct => ct.Comic)
                      .HasForeignKey(ct => ct.ComicKey)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // 配置 TagModel 主键
            modelBuilder.Entity<TagModel>(entity =>
            {
                entity.ToTable("Tags");

                entity.HasKey(e => e.Key);

                entity.Property(e => e.Key)
                      .HasColumnName("Key")
                      .HasColumnType("VARCHAR(32)")
                      .HasMaxLength(32)
                      .IsRequired();

                entity.Property(e => e.Name)
                      .HasColumnName("Name")
                      .HasColumnType("TEXT")
                      .IsRequired(false);

                entity.Property(e => e.Count)
                      .HasColumnName("Count")
                      .HasColumnType("INTEGER")
                      .HasDefaultValue(0);

                // 导航属性配置
                entity.HasMany(e => e.ComicTags)
                      .WithOne(ct => ct.Tag)
                      .HasForeignKey(ct => ct.TagKey)
                      .OnDelete(DeleteBehavior.Cascade);
            });

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
