using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ComicViewer.Database;
using ComicViewer.Models;
using ComicViewer.Services;
using Microsoft.Win32;

namespace ComicViewer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private ComicViewModel _viewModel;
        private readonly ComicLoader _loader;
        private bool _isLoading;
        private int _visibleStartIndex = 0;
        private int _visibleEndIndex = 50; // 初始加载50个

        public MainWindow()
        {
            InitializeComponent();

            _viewModel = new ComicViewModel();
            DataContext = _viewModel;

            _loader = new ComicLoader();

            // 初始加载漫画
            Loaded += async (s, e) => await InitializeComicsAsync();

            AllowDrop = true;
            DragEnter += MainWindow_DragEnter;
            Drop += MainWindow_Drop;
        }

        private async Task InitializeComicsAsync()
        {
            var comics = await ComicService.Instance.GetAllComicsAsync();
            var tags = await ComicService.Instance.GetAllTagsAsync();

            // 添加到ViewModel的集合中
            foreach (var comic in comics)
            {
                _viewModel.Comics.Add(comic);
            }
            foreach (var tag in tags)
            {
                _viewModel.UnselectedTags.Add(tag);
            }
        }

        private void UpdateStatus(string message)
        {
            StatusText.Text = message;
        }

        private void SortCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SortCombo.SelectedItem == null || _viewModel?.Comics == null)
                return;

            var selectedItem = (ComboBoxItem)SortCombo.SelectedItem;
            var sortMethod = selectedItem.Content.ToString();

            // 对Comics集合进行排序
            SortComics(sortMethod);
        }

        private void SortComics(string sortMethod)
        {
            var comics = _viewModel.Comics.ToList(); // 复制列表

            switch (sortMethod)
            {
                case "最新添加":
                    comics = comics.OrderByDescending(c => c.CreatedTime).ToList();
                    break;

                case "最近阅读":
                    comics = comics.OrderByDescending(c => c.LastAccess).ToList();
                    break;

                case "标题 A-Z":
                    comics = comics.OrderBy(c => c.Title, StringComparer.OrdinalIgnoreCase).ToList();
                    break;

                case "标题 Z-A":
                    comics = comics.OrderByDescending(c => c.Title, StringComparer.OrdinalIgnoreCase).ToList();
                    break;

                case "评分最高":
                    comics = comics.OrderByDescending(c => c.Rating)
                                   .ThenBy(c => c.Title, StringComparer.OrdinalIgnoreCase)
                                   .ToList();
                    break;

                default:
                    comics = comics.OrderByDescending(c => c.CreatedTime).ToList();
                    break;
            }

            // 更新集合（注意：这会重置UI）
            _viewModel.Comics.Clear();
            foreach(var comic in comics)
            {
                _viewModel.Comics.Add(comic);
            }
        }

        private void ComicCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is ComicModel comic)
            {
                OpenComic(comic);
            }
        }

        private ContextMenu CreateComicContextMenu(ComicModel comic)
        {
            var contextMenu = new ContextMenu
            {
                Background = Brushes.White,
                BorderBrush = Brushes.LightGray,
                BorderThickness = new Thickness(1),
                FontSize = 13
            };

            // 1. 打开漫画
            var openItem = new MenuItem
            {
                Header = "📖 打开漫画",
                Icon = new TextBlock { Text = "▶", FontSize = 14, Margin = new Thickness(0, 0, 6, 0) },
                Command = new RelayCommand(() => OpenComic(comic)),
                Tag = comic
            };

            // 2. 编辑标签
            var editTagsItem = new MenuItem
            {
                Header = "🏷️ 编辑标签",
                Icon = new TextBlock { Text = "🏷", FontSize = 14, Margin = new Thickness(0, 0, 6, 0) },
                Command = new RelayCommand(() => EditComicTags(comic)),
                Tag = comic
            };

            // 3. 分享漫画（生成.cmc文件）
            var shareItem = new MenuItem
            {
                Header = "📤 分享漫画",
                Icon = new TextBlock { Text = "📤", FontSize = 14, Margin = new Thickness(0, 0, 6, 0) },
                Command = new RelayCommand(async () => await ShareComic(comic)),
                Tag = comic
            };

            // 分隔线
            var separator1 = new Separator();

            // 4. 文件操作
            var revealInExplorerItem = new MenuItem
            {
                Header = "📁 在资源管理器中显示",
                Command = new RelayCommand(() => RevealInExplorer(comic.Key)),
                Tag = comic
            };

            // 分隔线
            var separator2 = new Separator();

            // 5. 删除/移除
            var removeItem = new MenuItem
            {
                Header = "🗑️ 从库中移除",
                Foreground = Brushes.Red,
                Command = new RelayCommand(() => RemoveComic(comic)),
                Tag = comic
            };

            var deleteItem = new MenuItem
            {
                Header = "⚠️ 删除文件",
                Foreground = Brushes.DarkRed,
                Command = new RelayCommand(() => DeleteComicFile(comic)),
                Tag = comic
            };

            // 添加到菜单
            contextMenu.Items.Add(openItem);
            contextMenu.Items.Add(editTagsItem);
            contextMenu.Items.Add(shareItem);
            contextMenu.Items.Add(separator1);
            contextMenu.Items.Add(revealInExplorerItem);
            contextMenu.Items.Add(separator2);
            contextMenu.Items.Add(removeItem);
            contextMenu.Items.Add(deleteItem);

            return contextMenu;
        }

        private void OpenComic(ComicModel comic)
        {
            // 1. 如果是压缩包，在软件内打开阅读器
            // 2. 如果是文件夹，打开图片浏览器
            // 3. 记录阅读进度
            MessageBox.Show($"打开漫画: {comic.Title}");
        }

        private void EditComicTags(ComicModel comic)
        {
            // 弹出标签编辑窗口
            // 允许添加/删除标签
            // 保存到对应的JSON文件
            //var dialog = new TagEditDialog(comic);
            //dialog.ShowDialog();
            MessageBox.Show($"编辑漫画: {comic.Title}");
        }

        private async Task ShareComic(ComicModel comic)
        {
            // 生成.cmc分享包
            // 包含：漫画文件 + metadata.json + cover.jpg
            var saveDialog = new SaveFileDialog
            {
                FileName = $"{comic.Title}.cmc",
                Filter = "漫画分享包|*.cmc"
            };

            if (saveDialog.ShowDialog() == true)
            {
                //Todo await CreateSharePackage(comic, saveDialog.FileName);
            }
        }
         
        private void RevealInExplorer(string comicKey)
        {
            // 在资源管理器中定位文件
            string directoryPath = Configs.GetFilePath();
            string filename = $"{comicKey}.zip";
            string fullPath = Path.Combine(directoryPath, filename);
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{fullPath}\"");
        }

        private void RemoveComic(ComicModel comic)
        {
            // 从库中移除（不删除文件）
            // 只是从内存索引中删除，文件还在磁盘上
            var result = MessageBox.Show(
                $"从库中移除 '{comic.Title}'？\n（文件不会被删除）",
                "确认移除",
                MessageBoxButton.YesNo);

            if (result == MessageBoxResult.Yes)
            {
                _viewModel.Comics.Remove(comic);
                //SaveLibraryIndex(); // 更新索引文件
            }
        }

        private void DeleteComicFile(ComicModel comic)
        {
            // 彻底删除文件
            var result = MessageBox.Show(
                $"永久删除 '{comic.Title}'？\n此操作不可恢复！",
                "确认删除",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                string path = Configs.GetFilePath();
                string filename = $"{comic.Key}.zip";
                string fullpath = Path.Combine(path, filename);

                // 删除漫画文件
                File.Delete(fullpath);
                // remove comic record
                _ = ComicService.Instance.RemoveComicAsync(comic.Key);

                // 从UI移除
                _viewModel.Comics.Remove(comic);
            }
        }

        private async void AddComics_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 创建打开文件对话框
                var dialog = new OpenFileDialog
                {
                    Title = "选择漫画文件",
                    Filter = "漫画文件|*.cmc;*.zip",
                    Multiselect = true,
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    CheckFileExists = true,
                    CheckPathExists = true
                };

                // 显示对话框
                bool? result = dialog.ShowDialog();

                if (result == true && dialog.FileNames.Length > 0)
                {
                    await AddComicsFromFilesAsync(dialog.FileNames, sender);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"添加漫画时出错: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private async Task AddComicsFromFilesAsync(string[] filePaths, object? sender=null)
        {
            int successCount = 0;
            int skipCount = 0;
            int errorCount = 0;

            // 显示进度
            UpdateStatus($"正在添加 {filePaths.Length} 个文件...");

            // 禁用按钮防止重复点击
            var addButton = sender as Button;
            if (addButton != null) addButton.IsEnabled = false;

            try
            {
                // 逐文件处理
                for (int i = 0; i < filePaths.Length; i++)
                {
                    var filePath = filePaths[i];

                    try
                    {
                        // 更新进度
                        UpdateStatus($"正在处理 ({i + 1}/{filePaths.Length}): {Path.GetFileName(filePath)}");

                        // 调用加载器添加漫画
                        var result = await _loader.AddComicAsync(filePath);

                        if (result != null)
                        {
                            successCount++;
                            // 更新UI
                            _viewModel.Comics.Add(result);
                        }
                        else
                        {
                            skipCount++; // 可能是重复文件
                        }
                    }
                    catch (Exception ex)
                    {
                        errorCount++;
                        Debug.WriteLine($"添加失败 {filePath}: {ex.Message}");
                    }
                }

                // 显示结果
                string message = $"添加完成: {successCount} 个成功";
                if (skipCount > 0) message += $", {skipCount} 个已跳过";
                if (errorCount > 0) message += $", {errorCount} 个失败";

                UpdateStatus(message);

                // 可选：显示完成提示
                if (successCount > 0)
                {
                    MessageBox.Show(message, "添加完成",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            finally
            {
                // 重新启用按钮
                if (addButton != null) addButton.IsEnabled = true;
            }
        }

        private void MainWindow_DragEnter(object sender, DragEventArgs e)
        {
            // 检查拖入的是否是文件
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Move;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private async void MainWindow_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);

                // 过滤出支持的格式
                var supportedFiles = files.Where(IsSupportedComicFile).ToArray();

                if (supportedFiles.Length > 0)
                {
                    await AddComicsFromFilesAsync(supportedFiles);
                }
                else
                {
                    MessageBox.Show("拖放的文件中没有支持的漫画格式", "提示",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }
        private bool IsSupportedComicFile(string filePath)
        {
            var extensions = new[] { ".cmc", ".zip" };
            var ext = Path.GetExtension(filePath).ToLower();
            return extensions.Contains(ext);
        }

        private async void ClearFilters_Click(object sender, RoutedEventArgs e) 
        {
            foreach(var tag in _viewModel.SelectedTags)
                _viewModel.UnselectedTags.Add(tag);
            _viewModel.SelectedTags.Clear();
        }
        private async void TagCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton toggleButton && toggleButton.DataContext is TagModel tag)
            {
                _viewModel.SelectedTags.Add(tag);
                _viewModel.UnselectedTags.Remove(tag);
            }
        }
        private async void TagCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton toggleButton && toggleButton.DataContext is TagModel tag)
            {
                _viewModel.SelectedTags.Remove(tag);
                _viewModel.UnselectedTags.Add(tag);
            }
        }
        private async void OnTagSearchChanged(object sender, TextChangedEventArgs e) { }

        // 滚轮加速
        private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var scrollViewer = (ScrollViewer)sender;
            scrollViewer.ScrollToVerticalOffset(
                scrollViewer.VerticalOffset - e.Delta);
            e.Handled = true;
        }
    }
}