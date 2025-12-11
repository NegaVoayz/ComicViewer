using ComicViewer.Infrastructure;
using ComicViewer.Models;
using ComicViewer.Services;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace ComicViewer
{
    public partial class ComicReaderWindow : Window
    {
        private readonly ComicService service;
        private readonly ComicModel _comic;
        private readonly List<string> _imageEntries = new();
        private int _currentPageIndex = 0;
        private bool _isLoading = false;
        private bool _isTwoPageMode = false;

        public int LastReadPage { get; private set; }

        public ComicReaderWindow(ComicService service, ComicModel comic)
        {
            InitializeComponent();

            this.service = service;
            _comic = comic;

            Title = $"阅读器 - {_comic.Title}";
            TitleText.Text = _comic.Title;

            Loaded += ComicReaderWindow_Loaded;

            // 订阅删除事件
            ComicEvents.ComicDeleted += OnComicDeleted;

            this.Closed += (s, e) =>
            {
                // 清理事件订阅
                ComicEvents.ComicDeleted -= OnComicDeleted;
            };

            // 设置快捷键
            SetupKeyBindings();

            // 初始隐藏控制栏
            HideControlBars();
        }

        private void SetupKeyBindings()
        {
            InputBindings.AddRange(new[]
            {
                new KeyBinding(new RelayCommand(() => PreviousPage()), new KeyGesture(Key.Left, ModifierKeys.None)),
                new KeyBinding(new RelayCommand(() => PreviousPage()), new KeyGesture(Key.Back, ModifierKeys.None)),
                new KeyBinding(new RelayCommand(() => NextPage()), new KeyGesture(Key.Right, ModifierKeys.None)),
                new KeyBinding(new RelayCommand(() => NextPage()), new KeyGesture(Key.Space, ModifierKeys.None)),
                new KeyBinding(new RelayCommand(() => FirstPage()), new KeyGesture(Key.Home, ModifierKeys.None)),
                new KeyBinding(new RelayCommand(() => LastPage()), new KeyGesture(Key.End, ModifierKeys.None)),

                new KeyBinding(new RelayCommand(() => Close()), new KeyGesture(Key.Escape, ModifierKeys.None)),


                new KeyBinding(new RelayCommand(() => ShowExitHint()), new KeyGesture(Key.Enter, ModifierKeys.None))
            });
        }

        private async void ComicReaderWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 显示加载指示器
            LoadingIndicator.Visibility = Visibility.Visible;

            try
            {
                // 1. 加载所有图片文件列表
                var imageEntries = await service.FileService.LoadImageEntriesAsync(_comic);
                _imageEntries.AddRange(imageEntries);

                if (_imageEntries.Count == 0)
                {
                    MessageBox.Show("漫画中没有找到图片文件", "错误",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    Close();
                    return;
                }

                TotalPagesText.Text = _imageEntries.Count.ToString();

                // 2. 跳转到上次阅读的页面
                _currentPageIndex = GetStartPageIndex();

                // 3. 加载第一页
                await LoadCurrentPageAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载漫画失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
            finally
            {
                LoadingIndicator.Visibility = Visibility.Collapsed;
            }
        }

        private int GetStartPageIndex()
        {
            // 如果有阅读进度，跳转到对应页面
            if (_comic.Progress > 0)
            {
                return _comic.Progress - 1;
            }
            return 0;
        }

        private async Task LoadCurrentPageAsync()
        {
            if (_isLoading || _currentPageIndex < 0 || _currentPageIndex >= _imageEntries.Count)
                return;

            _isLoading = true;

            try
            {
                if (_isTwoPageMode)
                {
                    await LoadTwoPagesAsync();
                }
                else
                {
                    await LoadSinglePageAsync();
                }

                UpdatePageInfo();
                UpdateUIState();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"加载页面失败: {ex.Message}");
                // 显示错误图片
                CurrentPageImage.Source = LoadErrorImage();
            }
            finally
            {
                _isLoading = false;
            }
        }

        private async Task LoadSinglePageAsync()
        {
            var imageEntryName = _imageEntries[_currentPageIndex];
            var image = await service.FileService.LoadImageAsync(_comic, imageEntryName);

            await Dispatcher.InvokeAsync(() =>
            {
                CurrentPageImage.Source = image;

                // 重置滚动位置
                ImageScrollViewer.ScrollToHome();
            });
        }

        private async Task LoadTwoPagesAsync()
        {
            // 创建两个加载任务，但不立即 await
            Task<BitmapImage?> leftTask;
            Task<BitmapImage?> rightTask;

            // 启动左页加载
            if (_currentPageIndex < _imageEntries.Count)
            {
                leftTask = service.FileService.LoadImageAsync(_comic, _imageEntries[_currentPageIndex]);
            }
            else
            {
                leftTask = Task.FromResult<BitmapImage?>(null);
            }

            // 启动右页加载
            if (_currentPageIndex + 1 < _imageEntries.Count)
            {
                rightTask = service.FileService.LoadImageAsync(_comic, _imageEntries[_currentPageIndex + 1]);
            }
            else
            {
                rightTask = Task.FromResult<BitmapImage?>(null);
            }

            // 等待两个任务都完成
            await Task.WhenAll(
                leftTask,
                rightTask
            );

            // 一次性更新UI
            await Dispatcher.InvokeAsync(() =>
            {
                LeftPageImage.Source = leftTask?.Result;
                RightPageImage.Source = rightTask?.Result;
            });
            //TwoPagePanel.InvalidateMeasure();
            //TwoPagePanel.InvalidateArrange();
            //TwoPagePanel.UpdateLayout();
        }

        // ========== 页面导航 ==========

        private void PreviousPage()
        {
            if (_currentPageIndex > 0)
            {
                _currentPageIndex -= _isTwoPageMode ? 2 : 1;
                if (_currentPageIndex < 0) _currentPageIndex = 0;
                _ = LoadCurrentPageAsync();
            }
        }

        private void NextPage()
        {
            int increment = _isTwoPageMode ? 2 : 1;
            if (_currentPageIndex + increment < _imageEntries.Count)
            {
                _currentPageIndex += increment;
                _ = LoadCurrentPageAsync();
            }
        }

        private void FirstPage()
        {
            _currentPageIndex = 0;
            _ = LoadCurrentPageAsync();
        }

        private void LastPage()
        {
            _currentPageIndex = _imageEntries.Count - 1;
            if (_isTwoPageMode && _currentPageIndex % 2 == 0)
            {
                _currentPageIndex--; // 双页模式确保从左边开始
            }
            _ = LoadCurrentPageAsync();
        }

        private void GoToPage(int pageNumber)
        {
            int index = pageNumber - 1; // 转换为0-based索引
            if (index >= 0 && index < _imageEntries.Count)
            {
                _currentPageIndex = index;
                _ = LoadCurrentPageAsync();
            }
        }

        // ========== UI更新 ==========

        private void UpdatePageInfo()
        {
            int displayPage = _currentPageIndex + 1;
            PageInfoText.Text = $"{displayPage}/{_imageEntries.Count}";
            PageJumpTextBox.Text = displayPage.ToString();
        }

        private void UpdateUIState()
        {
            PrevPageButton.IsEnabled = _currentPageIndex > 0;
            NextPageButton.IsEnabled = _currentPageIndex < _imageEntries.Count - (_isTwoPageMode ? 2 : 1);
            FirstPageButton.IsEnabled = _currentPageIndex > 0;
            LastPageButton.IsEnabled = _currentPageIndex < _imageEntries.Count - 1;
        }

        // ========== 控制栏显示/隐藏 ==========

        private bool _controlBarsVisible = false;

        private async void ShowControlBars()
        {
            if (_controlBarsVisible) return;
            _controlBarsVisible = true;

            await Dispatcher.InvokeAsync(() =>
            {
                var fadeIn = (Storyboard)FindResource("FadeIn");
                Storyboard.SetTarget(fadeIn, TopControlBar);
                fadeIn.Begin();

                Storyboard.SetTarget(fadeIn, BottomControlBar);
                fadeIn.Begin();
            });
        }

        private async void HideControlBars()
        {
            if (!_controlBarsVisible) return;
            _controlBarsVisible = false;

            await Dispatcher.InvokeAsync(() =>
            {
                var fadeOut = (Storyboard)FindResource("FadeOut");
                Storyboard.SetTarget(fadeOut, TopControlBar);
                fadeOut.Begin();

                Storyboard.SetTarget(fadeOut, BottomControlBar);
                fadeOut.Begin();
            });
        }

        private void ToggleControlBars()
        {
            if (_controlBarsVisible)
                HideControlBars();
            else
                ShowControlBars();
        }

        // ========== 事件处理 ==========
        private void OnComicDeleted(string deletedComicKey)
        {
            // 如果是当前漫画被删除，关闭窗口
            if (_comic.Key == deletedComicKey)
            {
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show("漫画已被删除，阅读器将关闭", "提示",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    Close();
                });
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.F:
                    ToggleFullScreen();
                    e.Handled = true;
                    break;
                case Key.C:
                    ToggleControlBars();
                    e.Handled = true;
                    break;
                case Key.T:
                    ToggleTwoPageMode();
                    e.Handled = true;
                    break;
                case Key.Space:
                    NextPage();
                    e.Handled = true;
                    break;
                case Key.Back:
                    PreviousPage();
                    e.Handled = true;
                    break;
                case Key.Enter:
                    ShowExitHint();
                    e.Handled = true;
                    break;
            }
        }

        private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Delta > 0)
                PreviousPage();
            else
                NextPage();
            e.Handled = true;
        }

        private void ControlBar_MouseEnter(object sender, MouseEventArgs e)
        {
            ShowControlBars();
        }

        private void ControlBar_MouseLeave(object sender, MouseEventArgs e)
        {
            // 延迟隐藏，避免鼠标移过时闪烁
            Task.Delay(500).ContinueWith(_ =>
            {
                Dispatcher.Invoke(() =>
                {
                    var position = Mouse.GetPosition(this);
                    if (position.Y > 50 && position.Y < ActualHeight - 60)
                    {
                        HideControlBars();
                    }
                });
            });
        }

        private void ShowExitHint()
        {
            ExitHint.Visibility = Visibility.Visible;
            var fadeIn = (Storyboard)FindResource("FadeIn");
            Storyboard.SetTarget(fadeIn, ExitHint);
            fadeIn.Begin();

            // 3秒后隐藏
            Task.Delay(3000).ContinueWith(_ =>
            {
                Dispatcher.Invoke(() =>
                {
                    var fadeOut = (Storyboard)FindResource("FadeOut");
                    Storyboard.SetTarget(fadeOut, ExitHint);
                    fadeOut.Begin();
                });
            });
        }

        // ========== 按钮事件 ==========

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void FirstPageButton_Click(object sender, RoutedEventArgs e) => FirstPage();
        private void PrevPageButton_Click(object sender, RoutedEventArgs e) => PreviousPage();
        private void NextPageButton_Click(object sender, RoutedEventArgs e) => NextPage();
        private void LastPageButton_Click(object sender, RoutedEventArgs e) => LastPage();

        private void PageJumpTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && int.TryParse(PageJumpTextBox.Text, out int page))
            {
                GoToPage(page);
            }
        }

        private void TwoPageModeToggle_Checked(object sender, RoutedEventArgs e)
        {
            _isTwoPageMode = true;
            SinglePagePanel.Visibility = Visibility.Collapsed;
            TwoPagePanel.Visibility = Visibility.Visible;

            // 如果是偶数页，调整到奇数页开始
            if (_currentPageIndex % 2 == 1 && _currentPageIndex > 0)
            {
                _currentPageIndex--;
            }

            _ = LoadCurrentPageAsync();
        }

        private void TwoPageModeToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            _isTwoPageMode = false;
            SinglePagePanel.Visibility = Visibility.Visible;
            TwoPagePanel.Visibility = Visibility.Collapsed;
            _ = LoadCurrentPageAsync();
        }

        private void FullScreenToggle_Checked(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Maximized;
            WindowStyle = WindowStyle.None;
        }

        private void FullScreenToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.SingleBorderWindow;
        }

        // ========== 辅助方法 ==========

        private BitmapImage LoadErrorImage()
        {
            // 创建一个错误提示图片
            var drawingVisual = new DrawingVisual();
            using (var drawingContext = drawingVisual.RenderOpen())
            {
                drawingContext.DrawRectangle(Brushes.DarkGray, null,
                    new Rect(0, 0, 400, 600));
                drawingContext.DrawText(
                    new FormattedText("加载失败",
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Arial"),
                        24,
                        Brushes.White,
                        1.0),
                    new Point(150, 280));
            }

            var renderTarget = new RenderTargetBitmap(400, 600, 96, 96, PixelFormats.Pbgra32);
            renderTarget.Render(drawingVisual);

            var bitmap = new BitmapImage();
            using var stream = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(renderTarget));
            encoder.Save(stream);
            stream.Position = 0;

            bitmap.BeginInit();
            bitmap.StreamSource = stream;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();

            return bitmap;
        }

        private void ToggleFullScreen()
        {
            FullScreenToggle.IsChecked = !FullScreenToggle.IsChecked;
        }

        private void ToggleTwoPageMode()
        {
            TwoPageModeToggle.IsChecked = !TwoPageModeToggle.IsChecked;
        }

        // ========== 窗口关闭 ==========

        protected override void OnClosed(EventArgs e)
        {
            // 保存阅读进度
            LastReadPage = _currentPageIndex + 1;

            // 更新漫画模型的阅读进度
            if (_imageEntries.Count > 0)
            {
                _comic.Progress = LastReadPage;
                _comic.LastAccess = DateTime.Now;
            }
            base.OnClosed(e);
        }
    }
}