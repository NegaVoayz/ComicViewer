using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ComicViewer.Models
{
    public class TagModel
    {
        [Key]
        [StringLength(32)] // Text(32) 对应 MD5 长度
        [Comment("MD5主键")]
        [Required]
        public required string Key { get; set; }

        [Column("Name", TypeName = "TEXT")]
        [Required]
        public required string Name { get; set; }

        [Column("Count", TypeName = "INTEGER")]
        public int Count { get; set; }

        public virtual ICollection<ComicTag> ComicTags { get; set; } = null!;
    }
}
