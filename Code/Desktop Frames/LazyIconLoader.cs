using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Desktop_Frames
{
    public class IconLoadRequest
    {
        public string FilePath { get; set; }
        public string TargetPath { get; set; }
        public bool IsFolder { get; set; }
        public bool IsLink { get; set; }
        public bool IsShortcut { get; set; }
        public System.Collections.Generic.IDictionary<string, object> IconDict { get; set; }
        public Image TargetImage { get; set; }
        public Action OnLoaded { get; set; }
    }

    public static class LazyIconLoader
    {
        private static readonly ConcurrentQueue<IconLoadRequest> _loadQueue = new ConcurrentQueue<IconLoadRequest>();
        private static CancellationTokenSource _cts;
        private static Task _loaderTask;
        private static bool _isRunning = false;

        public static void Start()
        {
            if (_isRunning) return;
            _isRunning = true;
            _cts = new CancellationTokenSource();
            _loaderTask = Task.Run(() => ProcessLoadQueue(_cts.Token));
        }

        public static void RequestIcon(IconLoadRequest request)
        {
            if (string.IsNullOrEmpty(request.FilePath) || request.TargetImage == null) return;
            _loadQueue.Enqueue(request);
        }

        private static async Task ProcessLoadQueue(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                int processed = 0;
                // Batch size of 15 prevents stuttering
                while (processed < 15 && _loadQueue.TryDequeue(out var request))
                {
                    if (token.IsCancellationRequested) break;

                    // One icon at a time, and one icon's failure costs one icon.
                    //
                    // This used to be caught around the whole loop, which then broke:
                    // a single file that threw - a shortcut whose target answers
                    // slowly, an executable with an icon resource Windows will not
                    // read - and the loader was gone for the rest of the session.
                    // Every icon still queued behind it kept the placeholder for
                    // good, and restarting did not help because the same file threw
                    // at the same point. Nothing was written down either, so the
                    // frames simply showed blank pages and the log said nothing.
                    try
                    {
                        ImageSource icon = null;
                        lock (IconManager.IconCache)
                        {
                            if (IconManager.IconCache.TryGetValue(request.FilePath, out var cached))
                                icon = cached;
                        }

                        if (icon == null)
                        {
                            icon = IconManager.GetIconForFile(request.TargetPath, request.FilePath, request.IsFolder, request.IsLink, request.IsShortcut, request.IconDict);
                            if (icon != null && icon.CanFreeze && !icon.IsFrozen) icon.Freeze();
                        }

                        if (icon != null)
                        {
                            await Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                request.TargetImage.Source = icon;
                                request.OnLoaded?.Invoke();
                            }, System.Windows.Threading.DispatcherPriority.Background);
                        }
                    }
                    catch (OperationCanceledException) { return; }
                    catch (Exception ex)
                    {
                        LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.IconHandling,
                            $"LazyIconLoader: no icon for {request.FilePath}: {ex.Message}");
                    }

                    processed++;
                }

                try { await Task.Delay(processed > 0 ? 30 : 100, token); }
                catch (OperationCanceledException) { return; }
            }
        }
    }
}