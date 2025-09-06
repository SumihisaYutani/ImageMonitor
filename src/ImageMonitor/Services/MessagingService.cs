using Microsoft.Extensions.Logging;

namespace ImageMonitor.Services;

public class MessagingService : IMessagingService
{
    private readonly ILogger<MessagingService> _logger;

    public MessagingService(ILogger<MessagingService> logger)
    {
        _logger = logger;
    }

    public event EventHandler<ThumbnailSizeChangedEventArgs>? ThumbnailSizeChanged;

    public void NotifyThumbnailSizeChanged(int newSize)
    {
        _logger.LogDebug("NotifyThumbnailSizeChanged called with size: {NewSize}", newSize);
        
        var subscribers = ThumbnailSizeChanged?.GetInvocationList().Length ?? 0;
        _logger.LogDebug("Event has {SubscriberCount} subscribers", subscribers);
        
        ThumbnailSizeChanged?.Invoke(this, new ThumbnailSizeChangedEventArgs(newSize));
        
        _logger.LogDebug("ThumbnailSizeChanged event invoked");
    }
}