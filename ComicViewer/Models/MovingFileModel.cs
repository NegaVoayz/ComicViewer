using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ComicViewer.Models
{
    public class MovingFileModel
    {
        [Key]
        [StringLength(32)] // Text(32) 对应 MD5 长度
        [Column("Key", TypeName = "TEXT")]
        [Required]
        public required string Key { get; set; }
        [Column("Src", TypeName = "TEXT")]
        [Required]
        public required string SourcePath { get; set; }
        [Column("Dst", TypeName = "TEXT")]
        [Required]
        public required string DestinationPath { get; set; }

        [ForeignKey("Key")]
        public virtual ComicData Comic { get; set; } = null!;
    }
}
