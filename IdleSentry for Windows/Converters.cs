using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace IdleSentry
{
    // Custom Boolean to Visibility converter supporting Inversion
    public class CustomBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool val = value is bool && (bool)value;
            bool invert = parameter is string paramStr && paramStr.Equals("Inverted", StringComparison.OrdinalIgnoreCase);

            if (invert)
            {
                return val ? Visibility.Collapsed : Visibility.Visible;
            }
            return val ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    // Convert integer counts (e.g. list size) to Visibility
    public class IntToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int val = value is int ? (int)value : 0;
            bool invert = parameter is string paramStr && paramStr.Equals("Inverted", StringComparison.OrdinalIgnoreCase);

            if (invert)
            {
                return val > 0 ? Visibility.Collapsed : Visibility.Visible;
            }
            return val > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    // Convert double seconds to friendly duration string (e.g. 300 -> "5m")
    public class SecondsToDurationConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length > 0 && values[0] is double seconds)
            {
                int secs = (int)seconds;
                if (secs < 60)
                {
                    return $"{secs}s";
                }
                else
                {
                    int mins = secs / 60;
                    int remainingSecs = secs % 60;
                    if (remainingSecs == 0)
                    {
                        return $"{mins}m";
                    }
                    else
                    {
                        return $"{mins}m {remainingSecs}s";
                    }
                }
            }
            return "0s";
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    // Convert double seconds to MM:SS timer format (e.g. 268 -> "04:28")
    public class SecondsToTimerConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is double seconds ? Format(seconds) : "00:00";
        }

        internal static string Format(double seconds)
        {
            int secs = double.IsFinite(seconds) ? (int)Math.Ceiling(Math.Clamp(seconds, 0, int.MaxValue)) : 0;
            return $"{secs / 60:D2}:{secs % 60:D2}";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    // Convert null/non-null object to Visibility
    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isNull = value == null;
            bool invert = parameter is string paramStr && paramStr.Equals("Inverted", StringComparison.OrdinalIgnoreCase);

            if (invert)
            {
                return isNull ? Visibility.Collapsed : Visibility.Visible;
            }
            return isNull ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class EqualityToBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null) return false;
            return string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b && b && parameter != null)
            {
                if (double.TryParse(parameter.ToString(), out double d)) return d;
                if (int.TryParse(parameter.ToString(), out int i)) return i;
                return parameter;
            }
            return Binding.DoNothing;
        }
    }
}
