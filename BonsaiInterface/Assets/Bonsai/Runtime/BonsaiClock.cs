/*
    The clock that stamps every value passing between Bonsai and Unity.

    By default it is System.Diagnostics.Stopwatch, which is QueryPerformanceCounter on Windows, the same counter
    Unity and most Windows hardware timestamps use. A project that already keeps its own session clock can point
    Now at it, so these timestamps come from the same place as the rest of its logs. Nothing else changes: the
    package only ever asks for "the current tick count".
*/

using System;
using System.Diagnostics;

/// <summary>
/// The clock behind the <c>long</c> timestamps given to <c>InjectSubscription</c> handlers and returned by
/// <c>UnitySignal.Send</c>.
/// </summary>
/// <remarks>
/// To use another clock, set <see cref="Now"/> once before any workflow starts, for example from a method marked
/// <c>[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]</c>. It is read on whichever thread
/// emits a value, so the replacement must be safe to call from any thread, must not use the Unity API, and should
/// be cheap and free of allocations. Its ticks must advance at <see cref="Stopwatch.Frequency"/> per second if you
/// convert them with it.
/// </remarks>
public static class BonsaiClock
{
    /// <summary>Returns the current time in ticks. Defaults to <see cref="Stopwatch.GetTimestamp"/>.</summary>
    public static volatile Func<long> Now = Stopwatch.GetTimestamp;
}
