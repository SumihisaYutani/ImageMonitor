namespace ImageMonitor.Services;

public interface IMessagingService
{
    event EventHandler<ThumbnailSizeChangedEventArgs>? ThumbnailSizeChanged;
    void NotifyThumbnailSizeChanged(int newSize);
}

public class ThumbnailSizeChangedEventArgs : EventArgs
{
    public int NewSize { get; }
    
    public ThumbnailSizeChangedEventArgs(int newSize)
    {
        NewSize = newSize;
    }
}