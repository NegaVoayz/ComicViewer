using ComicViewer.Models;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace ComicViewer.Converters
{
    public class ToolTipConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
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

            // 标题 - 使用绑定
            var titleBlock = new TextBlock
            {
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 4),
                TextWrapping = TextWrapping.Wrap
            };
            titleBlock.SetBinding(TextBlock.TextProperty,
                new Binding("Title") { Source = comic });

            // 标签 - 使用绑定和转换器
            var tagsBlock = new TextBlock
            {
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 4)
            };

            // 创建一个绑定来格式化标签
            var tagsBinding = new Binding("TagsPreview")
            {
                Converter = new TagsFormatConverter(),
                Source = comic

            };
            tagsBlock.SetBinding(TextBlock.TextProperty, tagsBinding);

            // 作者 - 使用绑定和转换器
            var authorsBlock = new TextBlock
            {
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 4)
            };

            // 创建一个绑定来格式化作者
            var authorsBinding = new Binding("Author")
            {
                Converter = new AuthorsFormatConverter(),
                Source = comic

            };
            authorsBlock.SetBinding(TextBlock.TextProperty, authorsBinding);

            // 阅读进度 - 使用绑定和多值转换器
            var progressBlock = new TextBlock
            {
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 6)
            };

            var progressBinding = new MultiBinding
            {
                Converter = new ProgressFormatConverter()
            };
            progressBinding.Bindings.Add(new Binding("Progress") { Source = comic });
            progressBinding.Bindings.Add(new Binding("Length") { Source = comic });
            progressBlock.SetBinding(TextBlock.TextProperty, progressBinding);

            // 分隔线
            var separator = new Separator
            {
                Margin = new Thickness(0, 4, 0, 4)
            };

            // 固定提示文本
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
            stackPanel.Children.Add(authorsBlock);
            stackPanel.Children.Add(progressBlock);
            stackPanel.Children.Add(hintBlock);

            toolTip.Content = stackPanel;
            return toolTip;
        }

        // 标签格式化转换器
        private class TagsFormatConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                if (value is string tagsPreview)
                {
                    return "标签: " + tagsPreview;
                }
                return "标签: 无";
            }

            public object ConvertBack(object value, Type targetTypes, object parameter, CultureInfo culture)
            {
                throw new NotImplementedException();
            }
        }

        // 作者格式化转换器
        private class AuthorsFormatConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                if (value is string authors)
                {
                    return "作者: " + authors;
                }
                return "作者: 未知";
            }

            public object ConvertBack(object value, Type targetTypes, object parameter, CultureInfo culture)
            {
                throw new NotImplementedException();
            }
        }

        // 进度格式化转换器
        private class ProgressFormatConverter : IMultiValueConverter
        {
            public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
            {
                if (values[0] is int progress && values[1] is int length && length > 0)
                {
                    var percentage = (int)((double)progress / length * 100);
                    return $"阅读进度: {progress}/{length} ({percentage}%)";
                }
                return "阅读进度: 加载中...";
            }

            public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            {
                throw new NotImplementedException();
            }
        }
    }
}
