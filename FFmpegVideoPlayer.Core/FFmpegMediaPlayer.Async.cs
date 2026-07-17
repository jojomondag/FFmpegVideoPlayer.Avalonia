using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace FFmpegVideoPlayer.Core;

public sealed partial class FFmpegMediaPlayer
{
    /// <summary>
    /// Resolves and opens a typed media source without blocking the caller's thread.
    /// A newer open cancels the previous one and the player owns the resolved session.
    /// </summary>
    public async Task<MediaOpenResult> OpenAsync(
        MediaSource source,
        MediaOpenOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        options ??= MediaOpenOptions.Default;
        options.Validate();

        CancellationTokenSource operationCancellation;
        lock (_openOperationLock)
        {
            _activeOpenCancellation?.Cancel();
            operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _activeOpenCancellation = operationCancellation;
        }

        MediaSourceSession? session = null;
        var gateEntered = false;
        var resetPerformed = false;
        try
        {
            await _openGate.WaitAsync(operationCancellation.Token).ConfigureAwait(false);
            gateEntered = true;
            Interlocked.Exchange(ref _openGateHeld, 1);
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            operationCancellation.Token.ThrowIfCancellationRequested();
            var previousSession = ResetForOpen();
            resetPerformed = true;
            if (previousSession is not null)
                await previousSession.DisposeAsync().ConfigureAwait(false);
            SetState(PlaybackState.Opening);
            session = await source.OpenSessionAsync(operationCancellation.Token).ConfigureAwait(false);
            operationCancellation.Token.ThrowIfCancellationRequested();

            var opened = await Task.Run(
                    () => OpenResolved(
                        session.Location,
                        source,
                        session,
                        options,
                        operationCancellation.Token),
                    operationCancellation.Token)
                .ConfigureAwait(false);
            operationCancellation.Token.ThrowIfCancellationRequested();

            if (!opened)
            {
                if (operationCancellation.IsCancellationRequested ||
                    _lastError?.Code == MediaErrorCode.Cancelled)
                {
                    throw new OperationCanceledException(operationCancellation.Token);
                }

                await session.DisposeAsync().ConfigureAwait(false);
                session = null;
                var error = _lastError ?? new MediaError(
                    MediaErrorCode.Unknown,
                    $"Could not open '{source.DisplayName}'.");
                SetState(PlaybackState.Failed);
                RaiseMediaFailed(error, source);
                return MediaOpenResult.Failure(error);
            }

            session = null; // Ownership transferred by OpenResolved.
            var mediaInfo = _mediaInfo ?? new MediaInfo(
                source.DisplayName,
                TimeSpan.FromMilliseconds(Length),
                HasVideo,
                HasAudio,
                VideoWidth,
                VideoHeight);
            return MediaOpenResult.Success(mediaInfo);
        }
        catch (OperationCanceledException)
        {
            if (resetPerformed)
                RollbackFailedOpen(session);
            if (session is not null)
                await session.DisposeAsync().ConfigureAwait(false);
            if (resetPerformed)
                SetState(PlaybackState.Closed);
            throw;
        }
        catch (ObjectDisposedException) when (_isDisposed)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (resetPerformed)
                RollbackFailedOpen(session);
            if (session is not null)
                await session.DisposeAsync().ConfigureAwait(false);
            var errorCode = ex switch
            {
                FileNotFoundException => MediaErrorCode.SourceNotFound,
                HttpRequestException => MediaErrorCode.Network,
                DllNotFoundException or EntryPointNotFoundException or BadImageFormatException =>
                    MediaErrorCode.NativeLibraryUnavailable,
                _ => MediaErrorCode.InvalidSource
            };
            var error = new MediaError(
                errorCode,
                $"Could not prepare '{source.DisplayName}'.",
                Exception: ex);
            _lastError = error;
            SetState(PlaybackState.Failed);
            RaiseMediaFailed(error, source);
            return MediaOpenResult.Failure(error);
        }
        finally
        {
            if (gateEntered)
            {
                Interlocked.Exchange(ref _openGateHeld, 0);
                _openGate.Release();
            }
            lock (_openOperationLock)
            {
                if (ReferenceEquals(_activeOpenCancellation, operationCancellation))
                    _activeOpenCancellation = null;
            }
            operationCancellation.Dispose();
        }
    }
}
