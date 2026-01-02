using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace ComicViewer.Infrastructure
{
    // 创建附加属性，在项离开可视区域时触发
    public static class VirtualizationBehavior
    {
        public static readonly DependencyProperty AutoUnloadProperty =
            DependencyProperty.RegisterAttached("AutoUnload", typeof(bool),
                typeof(VirtualizationBehavior), new PropertyMetadata(false, OnAutoUnloadChanged));

        public static bool GetAutoUnload(DependencyObject obj) => (bool)obj.GetValue(AutoUnloadProperty);
        public static void SetAutoUnload(DependencyObject obj, bool value) => obj.SetValue(AutoUnloadProperty, value);

        private static void OnAutoUnloadChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FrameworkElement element && e.NewValue is true)
            {
                element.Unloaded += OnElementUnloaded;
                element.Loaded += OnElementLoaded;
            }
        }

        private static void OnElementLoaded(object sender, RoutedEventArgs e)
        {
            // 项进入可视区域
            var element = sender as FrameworkElement;
            if (element?.DataContext is IUnloadableViewModel vm)
            {
                vm.Load();
            }
        }

        private static void OnElementUnloaded(object sender, RoutedEventArgs e)
        {
            // 项离开可视区域
            var element = sender as FrameworkElement;
            if (element?.DataContext is IUnloadableViewModel vm)
            {
                vm.Unload();
            }
        }
    }

    public interface IUnloadableViewModel
    {
        void Load();
        void Unload();
    }
}
