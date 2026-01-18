using ComicViewer.Infrastructure;
using ComicViewer.Models;
using ComicViewer.Services;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ComicViewer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly ComicService service;
        private readonly ComicViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();

            service = new();
            _viewModel = service.Cache.ViewModel;
            DataContext = _viewModel;

            SearchNameDebouncer = new(500, service.Cache.SetSearchName);
            SearchTagDebouncer = new(500, service.Cache.SetSearchTagName);

            InitializeSaveDirectory();

            // 初始加载漫画
            Loaded += async (s, e) => await service.Initiallize();

            AllowDrop = true;
            DragEnter += MainWindow_DragEnter;
            Drop += MainWindow_Drop;
        }

        private void InitializeSaveDirectory()
        {
            try
            {
                // 从配置文件或默认位置获取保存目录
                _viewModel.CurrentSaveDirectory.Value = Configs.GetFilePath();
                //UpdateSaveDirectoryDisplay();

                // 异步计算存储使用情况
                _ = CalculateStorageUsageAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"初始化保存目录时出错: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task CalculateStorageUsageAsync()
        {
            try
            {
                if (StorageUsageText == null || string.IsNullOrEmpty(_viewModel.CurrentSaveDirectory) ||
                    !Directory.Exists(_viewModel.CurrentSaveDirectory))
                    return;

                await Task.Run(() =>
                {
                    long totalSize = 0;
                    long fileCount = 0;

                    // 计算目录大小（避免UI线程阻塞）
                    var files = Directory.GetFiles(_viewModel.CurrentSaveDirectory, "*.*", SearchOption.AllDirectories);
                    fileCount = files.Length;

                    foreach (var file in files)
                    {
                        try
                        {
                            var info = new FileInfo(file);
                            totalSize += info.Length;
                        }
                        catch { /* 忽略无法访问的文件 */ }
                    }

                    // 回到UI线程更新显示
                    Dispatcher.Invoke(() =>
                    {
                        if (StorageUsageText != null)
                        {
                            string sizeText;
                            if (totalSize > 1024 * 1024 * 1024) // GB
                                sizeText = $"{(double)totalSize / (1024 * 1024 * 1024):F2} GB";
                            else if (totalSize > 1024 * 1024) // MB
                                sizeText = $"{(double)totalSize / (1024 * 1024):F2} MB";
                            else if (totalSize > 1024) // KB
                                sizeText = $"{(double)totalSize / 1024:F2} KB";
                            else
                                sizeText = $"{totalSize} B";

                            StorageUsageText.Text = $"{fileCount} 个文件 · {sizeText}";
                        }
                    });
                });
            }
            catch (Exception)
            {
                // 忽略计算错误
                if (StorageUsageText != null)
                    StorageUsageText.Text = "无法计算存储空间";
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
            var sortMethod = selectedItem.Content.ToString()!;

            // 对Comics集合进行排序
            SortComics(sortMethod);
        }

        private void SortComics(string sortMethod)
        {
            Order newOrder = sortMethod switch
            {
                "最新添加" => Order.CreatedTime,
                "最近阅读" => Order.LastAccess,
                "标题 A-Z" => Order.Title,
                "标题 Z-A" => Order.TitleInverse,
                "评分最高" => Order.Rating,
                _ => Order.CreatedTime
            };

            service.Cache.SetOrder(newOrder);
        }

        private void ComicCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is ComicModel comic)
            {
                OpenComic(comic);
            }
        }

        private void OpenComic(ComicModel comic)
        {
            try
            {
                ShowStatusMessage($"正在打开: {comic.Title}", 1000);

                // 创建并显示阅读器窗口（非模态）
                var readerWindow = new ComicReaderWindow(service, comic)
                {
                    Owner = this,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                // 订阅窗口关闭事件
                readerWindow.Closed += async (s, e) =>
                {
                    if (readerWindow.LastReadPage > 0)
                    {
                        ShowStatusMessage($"已阅读到第 {readerWindow.LastReadPage} 页", 1000);
                        await SaveComicProgressAsync(comic);
                    }
                };

                // 显示窗口（非模态）
                readerWindow.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开漫画失败:\n{ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                ShowStatusMessage($"打开失败: {comic.Title}", 2000);
            }
        }

        private async Task SaveComicProgressAsync(ComicModel comic)
        {
            try
            {
                // 更新到数据库或元数据文件
                var comicData = service.DataService.GetComicData(comic.Key);
                if (comicData != null)
                {
                    comicData.Progress = comic.Progress;
                    comicData.LastAccess = comic.LastAccess;
                    await service.DataService.UpdateComicAsync(comicData);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"保存阅读进度失败: {ex.Message}");
            }
        }

        private void EditComicTags(ComicModel comic)
        {
            // 弹出标签编辑窗口
            // 允许添加/删除标签
            try
            {
                ShowStatusMessage($"正在打开: {comic.Title}", 1000);

                // 创建并显示编辑器窗口（非模态）
                var dialog = new EditTagsDialog(service, comic)
                {
                    Owner = this,
                    WindowStartupLocation = WindowStartupLocation.Manual
                };

                // 订阅窗口关闭事件
                dialog.Closed += async (s, e) =>
                {
                    if (dialog.Changed == true)
                    {
                        // 更新漫画标签
                        comic.RefreshTags();

                        // 显示反馈
                        ShowStatusMessage($"已更新《{comic.Title}》的标签", 2000);

                        // 刷新显示
                        await service.Cache.RefreshTagsAsync();
                        await service.Cache.RefreshComicsAsync();
                    }
                };

                // 显示窗口（非模态）
                dialog.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开漫画失败:\n{ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                ShowStatusMessage($"打开失败: {comic.Title}", 2000);
            }
        }

        private void EditSourceMenuItem(ComicModel comic)
        {
            // 弹出标签编辑窗口
            // 允许添加/删除标签
            ShowStatusMessage($"正在编辑: {comic.Title}", 1000);

            // 创建并显示编辑器窗口（非模态）
            var dialog = new TextEditPopup(comic.Source, "编辑源")
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.Manual
            };

            // 订阅窗口关闭事件
            dialog.Closed += (s, e) =>
            {
                if (dialog.IsConfirmed == true)
                {
                    // 更新漫画标签
                    // 自动刷新显示
                    comic.Source = dialog.ResultText;
                    _ = Task.Run(async () =>
                    {
                        var c = service.DataService.GetComicData(comic.Key)!;
                        c.Source = comic.Source;
                        await service.DataService.UpdateComicAsync(c);
                    });

                    // 显示反馈
                    ShowStatusMessage($"已更新 {comic.Title} 的源", 2000);
                }
            };

            // 显示窗口（非模态）
            dialog.Show();
            return;
        }

        private void EditTitleMenuItem(ComicModel comic)
        {
            // 弹出标签编辑窗口
            // 允许添加/删除标签
            ShowStatusMessage($"正在编辑: {comic.Title}", 1000);

            // 创建并显示编辑器窗口（非模态）
            var dialog = new TextEditPopup(comic.Title, "编辑标题")
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.Manual
            };

            // 订阅窗口关闭事件
            dialog.Closed += async (s, e) =>
            {
                if (!dialog.IsConfirmed)
                {
                    return;
                }
                var newTitle = dialog.ResultText.Trim();
                if (newTitle.IsWhiteSpace())
                {
                    ShowStatusMessage($"更新 {comic.Title} 的标题失败：新名称不可为空", 2000);
                    return;
                }
                if (newTitle == comic.Title)
                {
                    ShowStatusMessage($"更新 {comic.Title} 的标题失败：不可与原名相同", 2000);
                    return;
                }
                // 自动刷新显示
                var newData = await service.DataService.RenameComic(comic.Key, newTitle);

                if(newData == null)
                {
                    ShowStatusMessage($"更新 {comic.Title} 的标题失败：存在重名漫画 {newTitle}", 2000);
                    return;
                }

                await service.Cache.RemoveComic(comic.Key);
                await service.Cache.AddComic(newData);

                // 显示反馈
                ShowStatusMessage($"已更新 {comic.Title} 的标题为 {newTitle}", 2000);
            };

            // 显示窗口（非模态）
            dialog.Show();
            return;
        }

        private void CopySourceMenuItem(ComicModel comic)
        {
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Clipboard.SetText(comic.Source);
                });
                ShowStatusMessage("目录路径已复制到剪贴板", 2000);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"复制失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void EditAuthorMenuItem(ComicModel comic)
        {
            // 弹出标签编辑窗口
            // 允许添加/删除标签
            ShowStatusMessage($"正在编辑: {comic.Title}", 1000);

            // 创建并显示编辑器窗口（非模态）
            var dialog = new TextEditPopup(comic.Author, "编辑作者")
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.Manual
            };

            // 订阅窗口关闭事件
            dialog.Closed += async (s, e) =>
            {
                if (dialog.IsConfirmed == true)
                {
                    var oldAuthors = comic.Author.Split(",").Select(e => e.Trim()).ToHashSet();
                    var newAuthors = ComicUtils.ParseTokens(dialog.ResultText, ComicUtils.AuthorDelimiterChars).ToHashSet();

                    var removedAuthors = oldAuthors.Except(newAuthors);
                    var addedAuthors = newAuthors.Except(oldAuthors);
                    var removedAuthorTagNames = removedAuthors.Select(e => ComicUtils.AuthorPrefix + e);
                    var addedAuthorTagNames = addedAuthors.Select(e => ComicUtils.AuthorPrefix + e);
                    var addedAuthorTagKeys = addedAuthorTagNames.Select(e => ComicUtils.CalculateMD5(e));
                    // 更新漫画标签
                    // 自动刷新显示
                    comic.Author = string.Join(", ", newAuthors);

                    await service.DataService.RemoveTagsFromComicAsync(comic.Key, removedAuthorTagNames);
                    await service.DataService.AddTagsSafeAsync(addedAuthorTagNames);
                    await service.DataService.AddTagsToComicAsync(comic.Key, addedAuthorTagKeys);

                    // 显示反馈
                    ShowStatusMessage($"已更新 {comic.Title} 的源", 2000);
                }
            };

            // 显示窗口（非模态）
            dialog.Show();
            return;
        }

        private async Task ShareComic(ComicModel comic)
        {
            // 生成.cmc分享包
            // 包含：漫画文件 + metadata.json + cover.jpg
            var saveDialog = new SaveFileDialog
            {
                FileName = $"{comic.Title}.cmc",
                Filter = "漫画分享包|*.cmc|漫画原文件|*.zip"
            };

            if (saveDialog.ShowDialog() == true)
            {
                string? directory = Path.GetDirectoryName(saveDialog.FileName);
                string fileName = Path.GetFileNameWithoutExtension(saveDialog.FileName);
                string fileExt = Path.GetExtension(saveDialog.FileName);

                string fullPath = saveDialog.FileName;
                await service.Exporter.CreateSharePackageAsync(comic, fullPath);
            }
        }

        private async Task OpenComicArchive(ComicModel comic)
        {
            using var filePath = service.FileService.GetComicPath(comic.Key);
            try
            {
                if (File.Exists(filePath))
                {
                    // 使用 explorer /select 命令选中文件
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{filePath}\"",
                        UseShellExecute = false
                    });
                }
                else if (Directory.Exists(filePath))
                {
                    // 如果是文件夹，直接打开
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{filePath}\"",
                        UseShellExecute = false
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法打开文件位置: {ex.Message}");
            }
        }

        private async Task DeleteComicFile(ComicModel comic)
        {
            // 彻底删除文件
            var result = MessageBox.Show(
                $"永久删除 '{comic.Title}'？\n此操作不可恢复！",
                "确认删除",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                // 发布删除事件
                ComicEvents.PublishComicDeleted(comic.Key);
                // 删除漫画文件
                await service.FileService.RemoveComicAsync(comic.Key);
                // 删除数据库记录
                await service.DataService.RemoveComicAsync(comic.Key);
                // 从UI移除
                await service.Cache.RemoveComic(comic.Key);
                // 刷新标签
                await service.Cache.RefreshTagsAsync();
                // 删除封面缓存
                service.CoverCache.Remove(comic.Key);
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
                    Filter = "漫画文件|*.cmc;*.zip;*.rar;*.7z",
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
        private async Task AddComicsFromFilesAsync(string[] filePaths, object? sender = null)
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
                        var result = await service.Loader.AddComicAsync(filePath);

                        if (result != null)
                        {
                            successCount++;
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

                ShowStatusMessage(message, 2000);

                // 显示完成提示
                MessageBox.Show(message, "添加完成",
                    MessageBoxButton.OK, MessageBoxImage.Information);
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
            var extensions = new[] { ".cmc", ".zip", ".rar", ".7z", "" };
            var ext = Path.GetExtension(filePath).ToLower();
            return extensions.Contains(ext);
        }

        private void OpenComicMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is ComicModel comic)
            {
                OpenComic(comic);
            }
        }

        private void EditTagsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is ComicModel comic)
            {
                EditComicTags(comic);
            }
        }

        private void EditSourceMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is ComicModel comic)
            {
                EditSourceMenuItem(comic);
            }
        }

        private void EditTitleMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is ComicModel comic)
            {
                EditTitleMenuItem(comic);
            }
        }

        private void EditAuthorMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is ComicModel comic)
            {
                EditAuthorMenuItem(comic);
            }
        }

        private void CopySourceMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is ComicModel comic)
            {
                CopySourceMenuItem(comic);
            }
        }

        private async void ShareComicMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is ComicModel comic)
            {
                await ShareComic(comic);
            }
        }
        private async void OpenComicArchiveMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is ComicModel comic)
            {
                await OpenComicArchive(comic);
            }
        }
        private async void DeleteComicMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is ComicModel comic)
            {
                await DeleteComicFile(comic);
            }
        }

        private void ClearFilters_Click(object sender, RoutedEventArgs e)
        {
            service.Cache.ClearSelectedTags();
        }
        private void TagCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.DataContext is TagModel tag)
            {
                service.Cache.SelectTag(tag.Key);
            }
        }
        private void TagCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.DataContext is TagModel tag)
            {
                service.Cache.DeselectTag(tag.Key);
            }
        }

        Debouncer<string> SearchNameDebouncer;
        private void OnNameSearchChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox MainSearchBox)
            {
                SearchNameDebouncer.Debounce(MainSearchBox.Text);
            }
        }

        Debouncer<string> SearchTagDebouncer;
        private void OnTagSearchChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox TagSearchBox)
            {
                SearchTagDebouncer.Debounce(TagSearchBox.Text);
            }
        }

        private void CopyDirectoryPath_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Clipboard.SetText(_viewModel.CurrentSaveDirectory);
                });
                ShowStatusMessage("目录路径已复制到剪贴板", 2000);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"复制失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RefreshStorageInfo_Click(object sender, RoutedEventArgs e)
        {
            _ = CalculateStorageUsageAsync();
            ShowStatusMessage("正在刷新存储信息...", 1000);
        }

        private void ChangeSaveDirectory_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 关闭设置菜单
                SettingsToggleButton.IsChecked = false;

                // 调用更改目录的方法（你可以在这里实现具体逻辑）
                OnChangeSaveDirectoryRequested();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"更改目录时出错: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenSaveDirectory_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Directory.Exists(_viewModel.CurrentSaveDirectory))
                {
                    System.Diagnostics.Process.Start("explorer.exe", _viewModel.CurrentSaveDirectory);
                }
                else
                {
                    MessageBox.Show("目录不存在或无法访问", "错误",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开目录失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnChangeSaveDirectoryRequested()
        {
            var dialog = new OpenFolderDialog
            {
                Title = "选择漫画保存目录",
                Multiselect = false,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                ValidateNames = true
            };


            // 显示对话框
            bool? result = dialog.ShowDialog();

            if (result == true && dialog.FolderName.Length > 0)
            {
                var newPath = dialog.FolderName;
                // 验证目录是否有效
                if (ValidateNewDirectory(newPath))
                {
                    // 更新配置
                    var oldPath = _viewModel.CurrentSaveDirectory.Value;
                    Configs.SetFilePath(newPath);
                    _viewModel.CurrentSaveDirectory.Value = newPath;

                    //_ = CalculateStorageUsageAsync();

                    // 重新加载漫画库
                    _ = service.Loader.MigrateComicLibrary(oldPath, newPath);

                    ShowStatusMessage($"保存目录已更改为: {newPath}", 3000);
                }
            }
        }

        private void EditTagMappingButton_Click(object sender, RoutedEventArgs e)
        {
            // 弹出映射编辑窗口
            // 允许添加/删除映射
            try
            {
                ShowStatusMessage($"正在打开映射编辑器", 1000);

                // 创建并显示编辑器窗口（非模态）
                var dialog = new EditTagMappingDialog(service)
                {
                    Owner = this,
                    WindowStartupLocation = WindowStartupLocation.Manual
                };

                // 订阅窗口关闭事件
                dialog.Closed += async (s, e) =>
                {
                    if (dialog.Changed == true)
                    {
                        // 显示反馈
                        ShowStatusMessage($"已更新标签", 2000);

                        // 刷新显示
                        await service.Cache.RefreshTagsAsync();
                        await service.Cache.RefreshComicsAsync();
                    }
                };

                // 关闭设置菜单
                SettingsToggleButton.IsChecked = false;
                // 显示窗口（非模态）
                dialog.Show();
            }
            catch (Exception)
            {
                MessageBox.Show($"映射编辑器打开失败", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                ShowStatusMessage($"映射编辑器打开失败", 2000);
            }
        }

        private bool ValidateNewDirectory(string path)
        {
            try
            {
                // 检查目录是否可访问
                if (!Directory.Exists(path))
                {
                    var result = MessageBox.Show("目录不存在，是否创建?", "确认",
                        MessageBoxButton.YesNo, MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        Directory.CreateDirectory(path);
                        return true;
                    }
                    return false;
                }

                // 检查是否有写入权限
                var testFile = Path.Combine(path, "test.tmp");
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);

                return true;
            }
            catch (Exception)
            {
                MessageBox.Show("目录没有写入权限或不可访问", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private void ShowStatusMessage(string message, int durationMs = 2000)
        {
            if (StatusText != null)
            {
                Dispatcher.Invoke(() => { StatusText.Text = message; });

                // 定时恢复原状态
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(durationMs)
                };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    Dispatcher.Invoke(() => { StatusText.Text = "就绪"; });
                };
                timer.Start();
            }
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (SettingsPopup.IsOpen &&
                !SettingsPopup.IsMouseOver &&
                !SettingsToggleButton.IsMouseOver)
            {
                SettingsToggleButton.IsChecked = false;
            }
        }
    }
}