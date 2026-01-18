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
        private HashSet<TagAlias> _newAliasEntries = new();

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
            var newTagNames = ComicUtils.ParseTokens(NewTagTextBox.Text, ComicUtils.TagDelimiterChars);
            foreach (var name in newTagNames)
            {
                var tokens = ComicUtils.ParseTokens(name, ComicUtils.TagAliasChars);
                HashSet<string> newAliases = new();
                string? existingName = null;
                bool isAuthorTag = false;
                foreach (var token in tokens)
                {
                    if (token.StartsWith(ComicUtils.AuthorPrefix))
                    {
                        isAuthorTag = true;
                        break;
                    }
                    var resolveTagName = service.DataService.FindTagNameByAliasAsync(token);
                    resolveTagName.Wait();
                    var resolvedTagName = resolveTagName.Result;

                    // check new aliases and new tag names if not found
                    if (resolvedTagName == null)
                    {
                        var newAlias = _newAliasEntries.FirstOrDefault(e => e.Alias == token);
                        // if still not found in new aliases, check new tag names
                        if (newAlias == null)
                        {
                            // if still not found in new tag names, treat it as a new tag name
                            if (!_newTagNames.Contains(token))
                            {
                                newAliases.Add(token);
                                continue;
                            }
                            resolvedTagName = token;
                        }
                        else
                        {
                            // found in new aliases, the name must exist
                            resolvedTagName = newAlias.Name;
                        }
                    }

                    // first existing name found
                    if (existingName == null)
                    {
                        existingName = resolvedTagName;
                        continue;
                    }

                    // skip if both are the same
                    if (existingName == resolvedTagName)
                        continue;

                    // here means some tags are the same in meaning, and they both exists
                    // and we take the first one as the standard name
                    newAliases.Add(resolvedTagName);
                }

                if (isAuthorTag)
                {
                    MessageBox.Show($"标签 \"{name}\" 包含作者前缀 \"{ComicUtils.AuthorPrefix}\"，无法作为普通标签添加。将会被跳过", "错误",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    continue;
                }

                // if no existing tags found, create a new one
                if (existingName == null)
                {
                    // take the first alias as the standard name
                    // since here is no existing name,
                    // all the aliases are exactly the tokens we got, unmapped
                    existingName = tokens.First();
                    // remember to remove the standard name from the aliases!
                    newAliases.Remove(existingName);

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
                AddAliases(newAliases, existingName);
            }
            NewTagTextBox.Clear();
            NewTagTextBox.Focus();
        }

        private void AddAliases(HashSet<string> aliasNames, string standardName)
        {
            // get affected entries "A -> B" when there's a newly added "B -> C" entry.
            var affectedEntries = _newAliasEntries.Where(e => aliasNames.Contains(e.Name)).ToList();
            // remove "A -> B" entries,
            _newAliasEntries.ExceptWith(affectedEntries);
            // remove B if in new tag names
            _newTagNames.ExceptWith(aliasNames);
            // and replace them with "A -> C" entries.
            _newAliasEntries.UnionWith(
                affectedEntries.Select(entry => new TagAlias
                {
                    Alias = entry.Alias,
                    Name = standardName
                }));
            // add new "B -> C" entries.
            _newAliasEntries.UnionWith(
                aliasNames.Select(token => new TagAlias
                {
                    Alias = token,
                    Name = standardName
                }));
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
            await service.DataService.ChangeTagsToComicAsync(_comic.Key, _cache.SelectedTags.Select(e => e.Key));
            if (_newAliasEntries.Any())
            {
                await service.DataService.AddTagAliasesAsync(_newAliasEntries);
                await service.TagAliasCache.RefreshAsync();
            }
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
