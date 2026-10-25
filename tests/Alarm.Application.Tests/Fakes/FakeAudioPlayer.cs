using Alarm.Application.Ports;
using Alarm.Domain.Model;

namespace Alarm.Application.Tests.Fakes;

internal sealed class FakeAudioPlayer : IAudioPlayer
{
    private readonly Task _startupGate;
    private readonly Exception? _startupFailure;
    private readonly bool _throwOnStart;
    private readonly Exception? _completionFailure;
    private readonly Task _completionGate;
    private readonly TaskCompletionSource _startRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _cancellationObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _playing;

    public FakeAudioPlayer(Task? startupGate = null, Exception? startupFailure = null, bool throwOnStart = false, Exception? completionFailure = null, Task? completionGate = null)
    {
        _startupGate = startupGate ?? Task.CompletedTask;
        _startupFailure = startupFailure;
        _throwOnStart = throwOnStart;
        _completionFailure = completionFailure;
        _completionGate = completionGate ?? Task.CompletedTask;
    }

    public bool IsPlaying => Volatile.Read(ref _playing) != 0;
    public AudioSource? LastPlayed { get; private set; }
    public int PlayCallCount { get; private set; }
    public Task StartRequested => _startRequested.Task;
    public Task Stopped => _stopped.Task;
    public Task CancellationObserved => _cancellationObserved.Task;

    public PlaybackSession StartPlayback(AudioSource source, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            _stopped.TrySetResult();
            return new PlaybackSession(Task.FromCanceled(ct), Task.CompletedTask);
        }

        if (_throwOnStart)
        {
            throw new InvalidOperationException("Session construction failed.");
        }

        LastPlayed = source;
        PlayCallCount++;
        _startRequested.TrySetResult();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = CompleteAsync(ready, ct);
        return new PlaybackSession(ready.Task, completion);
    }

    private async Task CompleteAsync(TaskCompletionSource ready, CancellationToken ct)
    {
        Exception? startupFailure = null;
        try
        {
            await _startupGate.WaitAsync(ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (_startupFailure is { } failure)
            {
                throw failure;
            }

            Volatile.Write(ref _playing, 1);
            ready.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            ready.TrySetCanceled(ct);
            _cancellationObserved.TrySetResult();
        }
        catch (Exception ex)
        {
            ready.TrySetException(ex);
            startupFailure = ex;
        }
        finally
        {
            Volatile.Write(ref _playing, 0);
            _stopped.TrySetResult();
        }

        await _completionGate.ConfigureAwait(false);
        if (_completionFailure is { } completionFailure)
        {
            throw completionFailure;
        }
        if (startupFailure is { } startupError)
        {
            throw startupError;
        }
    }
}
