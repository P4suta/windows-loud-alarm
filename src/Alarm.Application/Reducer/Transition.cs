using System.Collections.Immutable;
using Alarm.Application.Effects;
using Alarm.Application.State;

namespace Alarm.Application.Reducer;

/// <summary>
/// The output of a single reducer step: the next state plus the side effects to enact.
/// Using an immutable array keeps the value semantics intact for testing.
/// </summary>
public readonly record struct Transition(AlarmState State, ImmutableArray<AlarmEffect> Effects)
{
    public static Transition Of(AlarmState s, params AlarmEffect[] effects) =>
        new(s, [.. effects]);

    public static Transition NoOp(AlarmState s) =>
        new(s, []);
}
