using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ComicViewer.Models
{
    public static class ComicEvents
    {
        public static event Action<string>? ComicDeleted; // 参数可以是漫画ID或Key

        public static void PublishComicDeleted(string comicKey)
        {
            ComicDeleted?.Invoke(comicKey);
        }
    }
}
