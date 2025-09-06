using System.Globalization;
using System.Windows.Data;
using System.Diagnostics;

namespace ImageMonitor.Converters;

public class ThumbnailSizeToCardSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        Debug.WriteLine($"ThumbnailSizeToCardSizeConverter.Convert - Value: {value}, Parameter: {parameter}");
        
        if (value is int thumbnailSize && parameter is string param)
        {
            int result;
            switch (param.ToLower())
            {
                case "width":
                    // カード幅 = サムネイルサイズ + パディング
                    result = Math.Max(thumbnailSize + 20, 100);
                    Debug.WriteLine($"Calculated width: {result} (thumbnailSize: {thumbnailSize})");
                    return result;
                    
                case "height":
                    // カード高さ = サムネイルサイズ + テキスト領域(110px) + パディング
                    result = Math.Max(thumbnailSize + 110, 170);
                    Debug.WriteLine($"Calculated height: {result} (thumbnailSize: {thumbnailSize})");
                    return result;
                    
                default:
                    Debug.WriteLine($"Unknown parameter: {param}, returning thumbnailSize: {thumbnailSize}");
                    return thumbnailSize;
            }
        }
        
        Debug.WriteLine($"Invalid input, returning default size 150");
        return 150; // デフォルトサイズ
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}