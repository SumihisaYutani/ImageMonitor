namespace ImageMonitor.Converters;

public class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            // Check if parameter indicates reverse conversion
            bool reverse = parameter?.ToString()?.ToLowerInvariant() == "reverse";
            
            if (reverse)
            {
                return boolValue ? Visibility.Collapsed : Visibility.Visible;
            }
            else
            {
                return boolValue ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Visibility visibility)
        {
            bool reverse = parameter?.ToString()?.ToLowerInvariant() == "reverse";
            
            if (reverse)
            {
                return visibility != Visibility.Visible;
            }
            else
            {
                return visibility == Visibility.Visible;
            }
        }
        
        return false;
    }
}