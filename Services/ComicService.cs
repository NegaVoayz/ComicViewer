using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ComicViewer.Services
{
    public class ComicService
    {
        private static readonly Lazy<ComicService> _instance = new();
        public static ComicService Instance => _instance.Value;

        public ComicCache Cache { get; }
        public ComicDataService DataService { get; }
        public ComicExporter Exporter { get; }
        public ComicFileService FileService { get; }
        public ComicLoader Loader { get; }
        public SilentFileLoader FileLoader { get; }

        public ComicService()
        {
            Cache = new(this);
            DataService = new(this);
            Exporter = new(this);
            FileService = new(this);
            Loader = new(this);
            FileLoader = new(this);
        }
    }
}
