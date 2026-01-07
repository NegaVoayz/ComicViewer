using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace ComicViewer.Models
{
    public class TagAlias
    {
        [Key]
        [Column("Alias", TypeName = "TEXT")]
        [Required]
        public required string Alias { get; set; }

        [Column("Name", TypeName = "TEXT")]
        [Required]
        public required string Name { get; set; }
    }
}
