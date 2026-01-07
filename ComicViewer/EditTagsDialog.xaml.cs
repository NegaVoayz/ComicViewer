using ComicViewer.Infrastructure;
using ComicViewer.Models;
using ComicViewer.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace ComicViewer
{
    /// <summary>
    /// EditTagsDialog.xaml 的交互逻辑
    /// </summary>
    public partial class EditTagsDialog : Window
    {
        private readonly ComicService service;
        private ComicModel _comic;
        private readonly TagViewModel _viewModel;
        private readonly TagCache _cache;
        private HashSet<string> _newTagNames = new();

        public bool Changed { get; private set; } = false;

        public EditTagsDialog(ComicService service, ComicModel comic)
        {
            InitializeComponent();

            this.service = service;
            _cache = new(service, comic.Key);

            _comic = comic;
            _viewModel = _cache.ViewModel;
            DataContext = _viewModel;
            SearchTagDebouncer = new(500, _cache.SetSearchTagName);

            Loaded += async (s, e) => await _cache.InitializeAsync();

            // 订阅删除事件
            ComicEvents.ComicDeleted += OnComicDeleted;

            this.Closed += (s, e) =>
            {
                // 清理事件订阅
                ComicEvents.ComicDeleted -= OnComicDeleted;
            };
        }

        private void OnComicDeleted(string deletedComicKey)
        {
            // 如果是当前漫画被删除，关闭窗口
            if (_comic.Key == deletedComicKey)
            {
                Dispatcher.Invoke(() =>
                {
                    Changed = false;
                    MessageBox.Show("漫画已被删除，阅读器将关闭", "提示",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    Close();
                });
            }
        }

        private void AddTagButton_Click(object sender, RoutedEventArgs e)
        {
            AddTags();
        }

        private void AddTags()
        {
            if (string.IsNullOrWhiteSpace(NewTagTextBox.Text))
            {
                NewTagTextBox.Clear();
                NewTagTextBox.Focus();
                return;
            }
            var newTagNames = NewTagTextBox.Text.Split(ComicUtils.TagDelimiterChars).Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim());
            foreach (var name in newTagNames)
            {
                var tokens = name.Split(ComicUtils.TagAliasChars).Select(e => e.Trim());
                string? existingName = null;
                foreach (var token in tokens)
                {
                    var standardTagTask = service.DataService.GetTagNameFromAliasAsync(token);
                    standardTagTask.Wait();
                    var standardTagName = standardTagTask.Result;
                    if (standardTagName != null)
                    {
                        if(existingName == null)
                        {
                            existingName = standardTagName;
                            continue;
                        }
                        if(existingName == standardTagName)
                            continue;
                        // here means some tags are the same in meaning, and they both exists
                        service.DataService.AddTagAliasAsync(standardTagName, existingName).Wait();
                        service.DataService.ReplaceTagAsync(standardTagName, existingName).Wait();
                    }
                }

                // if no existing tag found, create a new one
                if (existingName == null)
                {
                    existingName = tokens.First();
                    _newTagNames.Add(existingName);
                    var tag = new TagData
                    {
                        Key = ComicUtils.CalculateMD5(existingName),
                        Name = existingName,
                        Count = 1
                    };
                    _cache.AddTag(tag);
                } 
                else
                {
                    _cache.SelectTag(ComicUtils.CalculateMD5(existingName));
                }


                // add aliases
                foreach (var token in tokens)
                {
                    if(token == existingName)
                        continue;
                    service.DataService.AddTagAliasAsync(token, existingName).Wait();
                }
            }
            NewTagTextBox.Clear();
            NewTagTextBox.Focus();
        }
        private void TagCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton toggleButton && toggleButton.DataContext is TagData tag)
            {
                _cache.SelectTag(tag.Key);
            }
        }
        private void TagCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton toggleButton && toggleButton.DataContext is TagData tag)
            {
                _cache.DeselectTag(tag.Key);
            }
        }

        Debouncer<string> SearchTagDebouncer;
        private void NewTagTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var newTagName = NewTagTextBox.Text.Trim();
            AddTagButton.IsEnabled = !string.IsNullOrWhiteSpace(newTagName);
            SearchTagDebouncer.Debounce(NewTagTextBox.Text);
        }

        private void NewTagTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && AddTagButton.IsEnabled)
            {
                AddTags();
                e.Handled = true;
            }
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            await service.DataService.AddTagsAsync(_newTagNames);
            await service.DataService.ChangeTagsToComicAsync(_comic.Key, _viewModel.SelectedTags.Select(e => e.Key));
            Changed = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Changed = false;
            Close();
        }
    }
}
