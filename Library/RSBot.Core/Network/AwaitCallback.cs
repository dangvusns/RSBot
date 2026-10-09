using System;
using System.Threading;
using System.Threading.Tasks;

namespace RSBot.Core.Network;

/// <summary>
///     The result of <see cref="AwaitCallback" />-processing the received packet
/// </summary>
public enum AwaitCallbackResult
{
    /// <summary>
    ///     If your condition not equals with received packet.
    /// </summary>
    ConditionFailed = 0,

    /// <summary>
    ///     If your condition successfully equal with received packet.
    /// </summary>
    Success,

    /// <summary>
    ///     If your received packet responsed with error code, or could not read required data from received.
    /// </summary>
    Fail,
}

/// <summary>
///     Predicate delegate for <see cref="AwaitCallback" /> received packet
/// </summary>
/// <param name="packet">The received <see cref="Packet" /></param>
/// <returns>
///     <see cref="AwaitCallbackResult" />
/// </returns>
public delegate AwaitCallbackResult AwaitCallbackPredicate(Packet packet);

public enum AwaitCallbackState
{
    Pending,
    Succeeded,
    Failed,
    TimedOut,
    Cancelled,
    NotSent,
}

/// <summary>
///     <see cref="AwaitCallback" /> is a callback with wait for response method.
/// </summary>
public class AwaitCallback
{
    /// <summary>
    ///     Default value of timeout[millisecond].
    /// </summary>
    private const int TIMEOUT_DEFAULT = 5_000;

    /// <summary>
    ///     Completion source used to signal when the callback is invoked.
    /// </summary>
    private readonly TaskCompletionSource<bool> _completionSource =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    ///     Predicate for received packet
    /// </summary>
    private readonly AwaitCallbackPredicate _predicate;

    /// <summary>
    ///     One terminal state wins, even when a response races cancellation or timeout.
    /// </summary>
    private int _state;

    /// <summary>
    ///     The value indicating whether the <see cref="AwaitCallback" /> is waited for response.
    ///     Uses int for <see cref="Interlocked.CompareExchange(ref int, int, int)" />.
    /// </summary>
    private int _waited;

    /// <summary>
    ///     Constructor of the <see cref="AwaitCallback" /> class.
    /// </summary>
    /// <param name="predicate">The <see cref="AwaitCallbackPredicate" />.</param>
    /// <param name="responseOpcode">The response opcode.</param>
    public AwaitCallback(AwaitCallbackPredicate predicate, ushort responseOpcode)
    {
        _predicate = predicate;
        ResponseOpcode = responseOpcode;
    }

    /// <summary>
    ///     Gets the response opcode.
    /// </summary>
    /// <value>
    ///     The response opcode.
    /// </value>
    public ushort ResponseOpcode { get; }

    /// <summary>
    ///     Gets the value indicating whether the <see cref="AwaitCallback" /> is completed.
    /// </summary>
    /// <value>
    ///     <c>true</c> if completed(not timeout and invoked and successed); otherwise <c>false</c>.
    /// </value>
    public bool IsCompleted => State == AwaitCallbackState.Succeeded;

    /// <summary>
    ///     Gets the value indicating whether the <see cref="AwaitCallback" /> is closed.
    /// </summary>
    /// <value>
    ///     <c>true</c> if closed(timeout or invoked); otherwise, <c>false</c>.
    /// </value>
    public bool IsClosed => State != AwaitCallbackState.Pending;

    public AwaitCallbackState State => (AwaitCallbackState)Volatile.Read(ref _state);

    public void Cancel() => Complete(AwaitCallbackState.Cancelled);

    internal void NotSent() => Complete(AwaitCallbackState.NotSent);

    private bool Complete(AwaitCallbackState state)
    {
        if (Interlocked.CompareExchange(ref _state, (int)state, (int)AwaitCallbackState.Pending)
            != (int)AwaitCallbackState.Pending)
            return false;

        // Cleanup does not depend on another packet arriving after a timeout/disconnect.
        PacketManager.RemoveCallback(this);
        _completionSource.TrySetResult(state == AwaitCallbackState.Succeeded);
        return true;
    }

    /// <summary>
    ///     Invokes this <see cref="AwaitCallback" /> instance.
    /// </summary>
    /// <param name="packet">The received <see cref="Packet" />.</param>
    internal void Invoke(Packet packet)
    {
        if (IsClosed)
            return;

        if (_predicate == null)
        {
            Complete(AwaitCallbackState.Succeeded);
            return;
        }

        var result = AwaitCallbackResult.Fail;

        try
        {
            result = _predicate(packet);
        }
        catch (Exception ex)
        {
            Log.Debug(() => $"Callback predicate threw an exception: {ex.Message}\n{ex.StackTrace}");
        }

        switch (result)
        {
            case AwaitCallbackResult.Success:
                Complete(AwaitCallbackState.Succeeded);
                break;

            case AwaitCallbackResult.ConditionFailed:
                break;

            case AwaitCallbackResult.Fail:
                Complete(AwaitCallbackState.Failed);
                break;
        }
    }

    /// <summary>
    ///     Waits for the first response.<br />
    ///     If you call it one more time, then it does nothing.
    /// </summary>
    /// <param name="milliseconds">The timeout in milliseconds, default value is <see cref="TIMEOUT_DEFAULT" />.</param>
    /// <returns></returns>
    public void AwaitResponse(int milliseconds = TIMEOUT_DEFAULT, CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _waited, 1, 0) != 0)
            return;

        if (milliseconds < 1)
            milliseconds = 1;

        using var registration = cancellationToken.Register(Cancel);
        var task = _completionSource.Task;

        task.Wait(milliseconds);

        if (!task.IsCompleted)
        {
            if (Complete(AwaitCallbackState.TimedOut))
                Log.Debug(() => $"Callback timeout, ResponseOpcode: 0x{ResponseOpcode:X}");
        }
    }

    public async Task AwaitResponseAsync(
        int milliseconds = TIMEOUT_DEFAULT,
        CancellationToken cancellationToken = default
    )
    {
        if (Interlocked.CompareExchange(ref _waited, 1, 0) != 0)
            return;

        if (milliseconds < 1)
            milliseconds = 1;

        using var registration = cancellationToken.Register(Cancel);
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(milliseconds);
            await _completionSource.Task.WaitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
                Cancel();
            else if (Complete(AwaitCallbackState.TimedOut))
                Log.Debug(() => $"Callback timeout, ResponseOpcode: 0x{ResponseOpcode:X}");
        }
    }
}
