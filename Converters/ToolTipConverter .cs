using ComicViewer.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace ComicViewer.Converters
{
    public class ToolTipConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ComicModel comic)
            {
                return CreateComicToolTip(comic);
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        private ToolTip CreateComicToolTip(ComicModel comic)
        {
            var toolTip = new ToolTip
            {
                Placement = PlacementMode.Mouse,
                HasDropShadow = true,
                BorderThickness = new Thickness(1),
                BorderBrush = Brushes.LightGray,
                Background = Brushes.White,
                Padding = new Thickness(8)
            };

            var stackPanel = new StackPanel
            {
                MaxWidth = 300,
                Orientation = Orientation.Vertical
            };

            // 标题
            var titleBlock = new TextBlock
            {
                Text = comic.Title,
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 4),
                TextWrapping = TextWrapping.Wrap
            };

            // 标签
            var tagsBlock = new TextBlock
            {
                Text = "标签: " + (comic.Tags != null && comic.Tags.Length > 0
                    ? string.Join(", ", comic.Tags)
                    : "无"),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 4)
            };

            // 阅读进度
            var progressBlock = new TextBlock
            {
                Text = $"阅读进度: {(int)(comic.Progress * 100)}%",
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 6)
            };

            // 分隔线
            var separator = new Separator
            {
                Margin = new Thickness(0, 4, 0, 4)
            };

            // 快速操作提示
            var hintBlock = new TextBlock
            {
                Text = "左键: 打开漫画 | 右键: 更多操作",
                FontSize = 10,
                Foreground = Brushes.DarkGray,
                FontStyle = FontStyles.Italic
            };

            stackPanel.Children.Add(titleBlock);
            stackPanel.Children.Add(separator);
            stackPanel.Children.Add(tagsBlock);
            stackPanel.Children.Add(progressBlock);
            stackPanel.Children.Add(hintBlock);

            toolTip.Content = stackPanel;
            return toolTip;
        }
    }
}
