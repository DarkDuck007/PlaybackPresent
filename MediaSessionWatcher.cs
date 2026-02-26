using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace PlaybackPresent;

[Flags]
public enum MediaSessionSignalKind
{
   None = 0,
   SessionChanged = 1 << 0,
   MediaPropertiesChanged = 1 << 1,
   PlaybackInfoChanged = 1 << 2,
   TimelineChanged = 1 << 3,
   RefreshRequested = 1 << 4,
}

public sealed class MediaSessionSnapshot
{
   public required uint Revision { get; init; }
   public string? SourceAppUserModelId { get; init; }
   public string? Title { get; init; }
   public string? Artist { get; init; }
   public IRandomAccessStreamReference? Thumbnail { get; init; }
   public GlobalSystemMediaTransportControlsSessionPlaybackStatus PlaybackStatus { get; init; }
   public TimeSpan TimelineStart { get; init; }
   public TimeSpan TimelineEnd { get; init; }
   public TimeSpan TimelinePosition { get; init; }

   public bool HasTimeline => (TimelineEnd - TimelineStart) >= TimeSpan.FromSeconds(1);
}

public sealed class MediaSessionSnapshotChangedEventArgs : EventArgs
{
   public MediaSessionSnapshotChangedEventArgs(MediaSessionSignalKind kind, MediaSessionSnapshot? snapshot)
   {
      Kind = kind;
      Snapshot = snapshot;
   }

   public MediaSessionSignalKind Kind { get; }
   public MediaSessionSnapshot? Snapshot { get; }
}

public sealed class MediaSessionWatcher : IDisposable
{
   private readonly object _gate = new();
   private readonly SemaphoreSlim _refreshLock = new(1, 1);
   private readonly TimeSpan _debounce = TimeSpan.FromMilliseconds(75);
   private CancellationTokenSource _disposeCts = new();

   private GlobalSystemMediaTransportControlsSessionManager? _manager;
   private GlobalSystemMediaTransportControlsSession? _session;
   private uint _sessionRevision;
   private MediaSessionSignalKind _pendingKinds;
   private DateTime _lastSignalUtc;
   private Task? _signalPumpTask;

   public event EventHandler<MediaSessionSnapshotChangedEventArgs>? SnapshotChanged;

   public async Task StartAsync()
   {
      ThrowIfDisposed();

      GlobalSystemMediaTransportControlsSessionManager manager;
      while (true)
      {
         try
         {
            manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            break;
         }
         catch (Exception ex)
         {
            Debug.WriteLine(ex);
            await Task.Delay(TimeSpan.FromMilliseconds(250), _disposeCts.Token);
            ThrowIfDisposed();
         }
      }
      ThrowIfDisposed();

      lock (_gate)
      {
         if (_manager is not null)
            return;

         _manager = manager;
         _manager.CurrentSessionChanged += Manager_CurrentSessionChanged;
      }

      TrySetSession(manager.GetCurrentSession());
      SignalRefresh(MediaSessionSignalKind.RefreshRequested);
   }

   public void RequestRefresh() => SignalRefresh(MediaSessionSignalKind.RefreshRequested);

   private void Manager_CurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
   {
      TrySetSession(sender.GetCurrentSession());
      SignalRefresh(MediaSessionSignalKind.SessionChanged);
   }

   private void TrySetSession(GlobalSystemMediaTransportControlsSession? newSession)
   {
      lock (_gate)
      {
         if (ReferenceEquals(_session, newSession))
            return;

         DetachSessionHandlersNoThrow(_session);
         _session = newSession;
         _sessionRevision++;
         AttachSessionHandlersNoThrow(_session);
      }
   }

   private void AttachSessionHandlersNoThrow(GlobalSystemMediaTransportControlsSession? session)
   {
      if (session is null) return;
      try { session.MediaPropertiesChanged += Session_MediaPropertiesChanged; } catch { }
      try { session.PlaybackInfoChanged += Session_PlaybackInfoChanged; } catch { }
      try { session.TimelinePropertiesChanged += Session_TimelinePropertiesChanged; } catch { }
   }

   private void DetachSessionHandlersNoThrow(GlobalSystemMediaTransportControlsSession? session)
   {
      if (session is null) return;
      try { session.MediaPropertiesChanged -= Session_MediaPropertiesChanged; } catch { }
      try { session.PlaybackInfoChanged -= Session_PlaybackInfoChanged; } catch { }
      try { session.TimelinePropertiesChanged -= Session_TimelinePropertiesChanged; } catch { }
   }

   private void Session_MediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) =>
      SignalRefresh(MediaSessionSignalKind.MediaPropertiesChanged);

   private void Session_PlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) =>
      SignalRefresh(MediaSessionSignalKind.PlaybackInfoChanged);

   private void Session_TimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args) =>
      SignalRefresh(MediaSessionSignalKind.TimelineChanged);

   private void SignalRefresh(MediaSessionSignalKind kind)
   {
      lock (_gate)
      {
         if (_disposeCts.IsCancellationRequested)
            return;

         _pendingKinds |= kind;
         _lastSignalUtc = DateTime.UtcNow;

         if (_signalPumpTask is null || _signalPumpTask.IsCompleted)
            _signalPumpTask = Task.Run(SignalPumpAsync, _disposeCts.Token);
      }
   }

   private async Task SignalPumpAsync()
   {
      var token = _disposeCts.Token;
      try
      {
         while (true)
         {
            DateTime lastSignal;
            lock (_gate) lastSignal = _lastSignalUtc;

            var remaining = _debounce - (DateTime.UtcNow - lastSignal);
            if (remaining > TimeSpan.Zero)
               await Task.Delay(remaining, token);

            MediaSessionSignalKind kinds;
            lock (_gate)
            {
               if ((DateTime.UtcNow - _lastSignalUtc) < _debounce)
                  continue;

               kinds = _pendingKinds;
               _pendingKinds = MediaSessionSignalKind.None;
            }

            if (kinds == MediaSessionSignalKind.None)
               return;

            await RefreshSnapshotAsync(kinds, token);

            lock (_gate)
            {
               if (_pendingKinds == MediaSessionSignalKind.None)
                  return;
            }
         }
      }
      catch (OperationCanceledException)
      {
      }
   }

   private async Task RefreshSnapshotAsync(MediaSessionSignalKind kinds, CancellationToken token)
   {
      await _refreshLock.WaitAsync(token);
      try
      {
         token.ThrowIfCancellationRequested();

         GlobalSystemMediaTransportControlsSession? session;
         GlobalSystemMediaTransportControlsSessionManager? manager;
         uint revision;

         lock (_gate)
         {
            session = _session;
            manager = _manager;
            revision = _sessionRevision;
         }

         if (manager is null)
            return;

         if (session is null)
         {
            SnapshotChanged?.Invoke(this, new MediaSessionSnapshotChangedEventArgs(kinds, null));
            return;
         }

         string? appId = null;
         try { appId = session.SourceAppUserModelId; } catch { }

         GlobalSystemMediaTransportControlsSessionPlaybackStatus playbackStatus = GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed;
         try { playbackStatus = session.GetPlaybackInfo().PlaybackStatus; } catch { }

         TimeSpan start = TimeSpan.Zero, end = TimeSpan.Zero, position = TimeSpan.Zero;
         try
         {
            var timeline = session.GetTimelineProperties();
            start = timeline.StartTime;
            end = timeline.EndTime;
            position = timeline.Position;
         }
         catch { }

         string? title = null;
         string? artist = null;
         IRandomAccessStreamReference? thumbnail = null;

         var shouldFetchMediaProps =
            (kinds & (MediaSessionSignalKind.SessionChanged | MediaSessionSignalKind.MediaPropertiesChanged | MediaSessionSignalKind.PlaybackInfoChanged | MediaSessionSignalKind.RefreshRequested)) != 0;

         if (shouldFetchMediaProps)
         {
            try
            {
               var props = await session.TryGetMediaPropertiesAsync();
               title = props?.Title;
               artist = props?.Artist;
               thumbnail = props?.Thumbnail;
            }
            catch { }
         }

         token.ThrowIfCancellationRequested();

         lock (_gate)
         {
            if (_sessionRevision != revision)
               return;
         }

         var snapshot = new MediaSessionSnapshot
         {
            Revision = revision,
            SourceAppUserModelId = appId,
            Title = title,
            Artist = artist,
            Thumbnail = thumbnail,
            PlaybackStatus = playbackStatus,
            TimelineStart = start,
            TimelineEnd = end,
            TimelinePosition = position,
         };

         SnapshotChanged?.Invoke(this, new MediaSessionSnapshotChangedEventArgs(kinds, snapshot));
      }
      finally
      {
         _refreshLock.Release();
      }
   }

   private void ThrowIfDisposed()
   {
      if (_disposeCts.IsCancellationRequested)
         throw new ObjectDisposedException(nameof(MediaSessionWatcher));
   }

   public void Dispose()
   {
      lock (_gate)
      {
         if (_disposeCts.IsCancellationRequested)
            return;

         _disposeCts.Cancel();
      }

      GlobalSystemMediaTransportControlsSessionManager? manager;
      GlobalSystemMediaTransportControlsSession? session;
      lock (_gate)
      {
         manager = _manager;
         session = _session;
         _manager = null;
         _session = null;
      }

      DetachSessionHandlersNoThrow(session);
      if (manager is not null)
      {
         try { manager.CurrentSessionChanged -= Manager_CurrentSessionChanged; } catch { }
      }

      _disposeCts.Dispose();
   }
}
