using ComicViewer.Infrastructure;
using ComicViewer.Models;
using ComicViewer.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace ComicViewer
{
    /// <summary>
    /// EditTagMappingDialog.xaml 的交互逻辑
    /// </summary>
    public partial class EditTagMappingDialog : Window
    {
        private readonly ComicService service;
        private readonly TagAliasViewModel _viewModel;
        private readonly TagAliasCache _cache;

        private readonly HashSet<TagAlias> _addedMappings = new();
        private readonly HashSet<TagAlias> _removedMappings = new();

        public bool Changed { get; private set; } = false;

        public EditTagMappingDialog(ComicService service)
        {
            InitializeComponent();

            this.service = service;
            _cache = service.TagAliasCache;
            _viewModel = _cache.ViewModel;
            DataContext = _viewModel;

            // 初始化防抖器
            AddEnableDebouncer = new Debouncer(200, UpdateAddMappingEnable);
            SearchNameDebouncer = new Debouncer<string>(300, _cache.SetSearchTagName);
            SearchAliasDebouncer = new Debouncer<string>(300, _cache.SetSearchAlias);
        }

        private void AddMappingButton_Click(object sender, RoutedEventArgs e)
        {
            AddMapping();
        }

        private void UpdateAddMappingEnable()
        {
            var alias = NewAliasTextBox.Text.Trim();
            var tagName = NewTagNameTextBox.Text.Trim();
            AddMappingButton.IsEnabled =
                !(  string.IsNullOrWhiteSpace(alias) 
                 || string.IsNullOrWhiteSpace(tagName)
                 || _cache.AllEntries.Any(e => e.Alias == alias));
        }

        Debouncer AddEnableDebouncer;

        Debouncer<string> SearchNameDebouncer;
        private void OnNewNameChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox NameBox)
            {
                SearchNameDebouncer.Debounce(NameBox.Text);
                AddEnableDebouncer.Debounce();
            }
        }

        Debouncer<string> SearchAliasDebouncer;
        private void OnNewAliasChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox AliasBox)
            {
                SearchAliasDebouncer.Debounce(AliasBox.Text);
                AddEnableDebouncer.Debounce();
            }
        }

        private void AddMapping()
        {
            var alias = NewAliasTextBox.Text.Trim();
            var tagName = NewTagNameTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(alias) || string.IsNullOrWhiteSpace(tagName))
            {
                MessageBox.Show("别名和标签名都不能为空", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 检查别名是否已存在
            if (_cache.ContainsAlias(alias))
            {
                MessageBox.Show($"别名 '{alias}' 已存在", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                NewAliasTextBox.Focus();
                NewAliasTextBox.SelectAll();
                return;
            }

            var mapping = new TagAlias
            {
                Alias = alias,
                Name = _cache.GetNameByAlias(tagName)
            };

            _cache.AddTagAlias(mapping);

            // 记录变更
            if (_removedMappings.Contains(mapping))
            {
                // 如果之前被标记为删除，则取消删除标记
                _removedMappings.Remove(mapping);
            }
            else
            {
                // 新增的映射
                _addedMappings.Add(mapping);
            }

                // 清空输入框
                NewAliasTextBox.Text = string.Empty;
            NewTagNameTextBox.Text = string.Empty;

            // 焦点回到第一个输入框
            NewAliasTextBox.Focus();
        }

        private void RemoveMappingButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is TagAlias mapping)
            {
                _cache.RemoveTagAlias(mapping);
                if (_addedMappings.Contains(mapping))
                {
                    // 如果是新添加的映射，则直接移除新增记录
                    _addedMappings.Remove(mapping);
                }
                else
                {
                    // 标记为已删除
                    _removedMappings.Add(mapping);
                }
            }
        }

        private void NewMappingTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                AddMapping();
                e.Handled = true;
            }
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 保存所有映射到数据库
                Changed = await service.DataService.ChangeTagAliasesAsync(_addedMappings, _removedMappings);
                if(Changed)
                {
                    // if the alias changes affected other aliases, refresh all
                    await service.TagAliasCache.RefreshAsync();
                }
                Close();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"保存失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            // rollback changes
            service.TagAliasCache.RemoveTagAliases(_addedMappings);
            service.TagAliasCache.AddTagAliases(_removedMappings);
            Changed = false;
            Close();
        }
    }
}