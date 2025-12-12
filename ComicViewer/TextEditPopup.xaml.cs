using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace ComicViewer
{
    internal static class NativeMethods
    {
        // A structure for the cursor's position
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        // Win32 function to get the cursor position in screen coordinates (pixels)
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetCursorPos(ref POINT lpPoint);
    }

    public partial class TextEditPopup : Window
    {
        public string ResultText { get; private set; }
        public bool IsConfirmed { get; private set; }

        public TextEditPopup(string initialText = "", string title = "编辑文本", bool isMultiLine = false)
        {
            InitializeComponent();

            EditTextBox.Text = initialText ?? "";
            PopupTitle.Text = title;

            if (!isMultiLine)
            {
                EditTextBox.TextWrapping = TextWrapping.NoWrap;
                EditTextBox.AcceptsReturn = false;
                EditTextBox.MaxHeight = 24;
            }

            SizeToContent = SizeToContent.WidthAndHeight;

            Loaded += (s, e) => ShowAtCursorPosition();
            // 设置焦点并全选文本
            Loaded += (s, e) =>
            {
                EditTextBox.Focus();
                EditTextBox.SelectAll();
            };
        }

        public void ShowAtCursorPosition()
        {
            // 1. Get the cursor position in raw screen PIXELS (using Win32 Interop)
            NativeMethods.POINT cursorPixels = new NativeMethods.POINT();
            if (!NativeMethods.GetCursorPos(ref cursorPixels))
            {
                // Handle error if position cannot be retrieved
                return;
            }

            // The cursor position in pixels is now (cursorPixels.X, cursorPixels.Y)

            // 2. Determine the DPI scaling factor for the current screen/window

            // Get the source for the current window (this)
            PresentationSource source = PresentationSource.FromVisual(this);
            double dpiScaleX = 1.0;
            double dpiScaleY = 1.0;

            if (source?.CompositionTarget != null)
            {
                // The TransformFromDevice matrix converts from raw pixels to DIPs.
                dpiScaleX = source.CompositionTarget.TransformFromDevice.M11;
                dpiScaleY = source.CompositionTarget.TransformFromDevice.M22;
            }

            // 3. Convert PIXELS to Device-Independent Pixels (DIPs)
            double cursorDIPsX = cursorPixels.X * dpiScaleX;
            double cursorDIPsY = cursorPixels.Y * dpiScaleY;

            // 4. Set the window's Left and Top properties in DIPs
            this.Left = cursorDIPsX - (this.ActualWidth / 2);
            this.Top = cursorDIPsY - (this.ActualHeight / 2);

            // 5. IMPORTANT: Show the window if it's not visible
            if (this.Visibility != Visibility.Visible)
            {
                this.Show();
            }
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            ResultText = EditTextBox.Text;
            IsConfirmed = true;
            this.Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            IsConfirmed = false;
            this.Close();
        }

        // 回车确认，ESC取消
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == 0)
            {
                ConfirmButton_Click(null, null);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CancelButton_Click(null, null);
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }
    }
}
