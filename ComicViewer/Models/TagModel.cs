using Microsoft.EntityFrameworkCore;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.CompilerServices;

namespace ComicViewer.Models
{
    public class TagData : IEquatable<TagData>
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

        public bool Equals(TagData? other)
        {
            return Key == other?.Key;
        }

        public override bool Equals(object? obj) => Equals(obj as TagData);
        public override int GetHashCode() => Key.GetHashCode();
    }
    public class TagModel : INotifyPropertyChanged, IEquatable<TagModel>
    {
        private readonly string _key;
        private readonly string _name;
        private int _count;

        public string Key
        {
            get => _key;
        }

        public string Name
        {
            get => _name;
        }

        public int Count
        {
            get => _count;
            set => SetField(ref _count, value);
        }

        public TagModel(TagData model)
        {
            _key = model.Key;
            _name = model.Name;
            _count = model.Count;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        public bool Equals(TagModel? other) => Key == other?.Key;
        public override bool Equals(object? obj) => Equals(obj as TagModel);
        public override int GetHashCode() => Key.GetHashCode();
    }
}
