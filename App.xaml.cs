using ComicViewer.Database;
using System.Windows;

namespace ComicViewer
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ComicContext.Instance.Database.EnsureCreated();
        }
    }

}
