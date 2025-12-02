// Converters.cs
using System.Globalization;
using System.Windows;
using System.Windows.Data;

/*
 
            <style:InverseBooleanConverter x:Key="InverseBooleanConverter"/>
            <style:EmptyToVisibilityConverter x:Key="EmptyToVisibilityConverter"/>
            <style:BooleanToVisibilityConverter x:Key="BooleanToVisibilityConverter"/>
            <style:EmptyToBooleanConverter x:Key="EmptyToBooleanConverter"/>
            <style:StringFormatConverter x:Key="StringFormatConverter"/>
            <style:EqualityConverter x:Key="EqualityConverter"/>
*/

namespace ComicViewer.Converters
{

    // 1. 反转布尔值转换器
    public class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return !boolValue;
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return !boolValue;
            }
            return value;
        }
    }

    // 2. 空字符串转Visibility（为空时显示）
    public class EmptyToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isEmpty = false;

            if (value is string str)
            {
                isEmpty = string.IsNullOrWhiteSpace(str);
            }
            else
            {
                isEmpty = value == null;
            }

            // 可选参数：反转逻辑
            if (parameter is string param && param == "Inverse")
            {
                isEmpty = !isEmpty;
            }

            return isEmpty ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    // 3. 增强的布尔转Visibility（支持参数）
    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                // 支持反转参数
                if (parameter is string param && param == "Inverse")
                {
                    boolValue = !boolValue;
                }

                // 支持Hidden参数
                if (parameter is string param2 && param2 == "Hidden")
                {
                    return boolValue ? Visibility.Visible : Visibility.Hidden;
                }

                return boolValue ? Visibility.Visible : Visibility.Collapsed;
            }

            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility visibility)
            {
                return visibility == Visibility.Visible;
            }
            return false;
        }
    }

    // 4. 空值转布尔（为空时true）
    public class EmptyToBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isEmpty = false;

            if (value is string str)
            {
                isEmpty = string.IsNullOrWhiteSpace(str);
            }
            else
            {
                isEmpty = value == null;
            }

            // 可选参数：反转逻辑
            if (parameter is string param && param == "Inverse")
            {
                isEmpty = !isEmpty;
            }

            return isEmpty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    // 5. 字符串格式化转换器
    public class StringFormatConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (parameter is string format)
            {
                return string.Format(culture, format, value);
            }

            return value?.ToString() ?? string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    // 6. 相等性比较转换器
    public class EqualityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // 支持比较两个值
            if (value == null && parameter == null)
                return true;

            if (value == null || parameter == null)
                return false;

            return value.Equals(parameter);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    // 7. 增强版：多值转换器（可选）
    public class MultiEqualityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2)
                return false;

            for (int i = 1; i < values.Length; i++)
            {
                if (!Equals(values[0], values[i]))
                    return false;
            }

            return true;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
