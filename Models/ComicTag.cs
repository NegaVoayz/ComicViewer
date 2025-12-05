using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ComicViewer.Models
{
    public class ComicTag
    {
        [Column("ComicKey", TypeName = "TEXT")]
        [StringLength(32)]
        [Comment("漫画MD5外键")]
        public string ComicKey { get; set; }

        [Column("TagKey", TypeName = "TEXT")]
        [StringLength(32)]
        [Comment("标签MD5外键")]
        public string TagKey { get; set; }

        [ForeignKey("ComicKey")]
        public virtual ComicData Comic { get; set; }

        [ForeignKey("TagKey")]
        public virtual TagModel Tag { get; set; }
    }
}
