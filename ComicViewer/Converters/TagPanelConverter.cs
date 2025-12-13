using ComicViewer.Models;
using ComicViewer.Services;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace ComicViewer.Converters
{
    public class TagPanelConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is TagModel tag)
            {
                return CreateTagPanel(tag);
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
        private StackPanel CreateTagPanel(TagModel tag)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
            };

            var nameBlock = new TextBlock
            {
                FontSize = 12,
                Margin = new Thickness(4, 0, 4, 0)
            };
            var nameBinding = new Binding("Name")
            {
                Converter = new TagNameConverter(),
                Source = tag
            };
            nameBlock.SetBinding(TextBlock.TextProperty, nameBinding);


            var countBlock = new TextBlock
            {
                FontSize = 11,
                Foreground = Brushes.Gray
            };
            countBlock.SetBinding(TextBlock.TextProperty,
                new Binding("Count") { Source = tag });

            panel.Children.Add(nameBlock);
            panel.Children.Add(countBlock);
            return panel;
        }
    }
    internal class TagNameConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string name)
            {
                var ans = name.StartsWith(ComicUtils.AuthorPrefix) ? name.Substring(ComicUtils.AuthorPrefix.Length) : name;
                return ans.Trim();
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
