using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ComicViewer.Models
{
    public class MovingFileModel
    {
        [Key]
        [StringLength(32)] // Text(32) 对应 MD5 长度
        [Column("Key", TypeName = "TEXT")]
        public string Key { get; set; }
        [Column("Src", TypeName = "TEXT")]  
        public string SourcePath { get; set; }
        [Column("Dst", TypeName = "TEXT")]
        public string DestinationPath { get; set; }

        [ForeignKey("Key")]
        public virtual ComicData Comic { get; set; }
    }
}
