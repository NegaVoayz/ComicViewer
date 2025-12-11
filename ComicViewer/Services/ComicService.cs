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
            DataService = new(this);
            FileService = new(this);
            FileLoader = new(this);
            Loader = new(this);
            Exporter = new(this);
            Cache = new(this);
        }
    }
}
