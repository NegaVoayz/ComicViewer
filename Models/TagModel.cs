using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ComicViewer.Models
{
    public class TagModel
    {
        [Key]
        [StringLength(32)] // Text(32) 对应 MD5 长度
        [Comment("MD5主键")]
        public string Key { get; set; }

        [Column("Name", TypeName = "TEXT")]
        public string Name { get; set; }

        [Column("Count", TypeName = "INTEGER")]
        public int Count { get; set; }

        public virtual ICollection<ComicTag> ComicTags { get; set; }
    }
}
