using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.Xaml.Interactivity;

namespace Alarm.Presentation.Behaviors;

/// <summary>
/// Long-press input behavior: starts the progress storyboard on press, fires <see cref="Confirmed"/>
/// when the press has been held for <see cref="Duration"/>, and starts the rollback storyboard on
/// early release. The inversion-reveal layered atop the progress bar is now driven by MainWindow
/// (it binds Clip.Rect.Width to the bar's ScaleX), so this behavior no longer touches Composition.
/// </summary>
public sealed class LongPressGestureBehavior : Behavior<UIElement>
{
    public static readonly DependencyProperty DurationProperty =
        DependencyProperty.Register(nameof(Duration), typeof(TimeSpan), typeof(LongPressGestureBehavior),
            new PropertyMetadata(TimeSpan.FromSeconds(1.6)));

    public static readonly DependencyProperty RollbackDurationProperty =
        DependencyProperty.Register(nameof(RollbackDuration), typeof(TimeSpan), typeof(LongPressGestureBehavior),
            new PropertyMetadata(TimeSpan.FromSeconds(0.6)));

    public static readonly DependencyProperty ProgressStoryboardProperty =
        DependencyProperty.Register(nameof(ProgressStoryboard), typeof(Storyboard), typeof(LongPressGestureBehavior),
            new PropertyMetadata(defaultValue: null));

    public static readonly DependencyProperty RollbackStoryboardProperty =
        DependencyProperty.Register(nameof(RollbackStoryboard), typeof(Storyboard), typeof(LongPressGestureBehavior),
            new PropertyMetadata(defaultValue: null));

    public TimeSpan Duration { get => (TimeSpan)GetValue(DurationProperty); set => SetValue(DurationProperty, value); }
    public TimeSpan RollbackDuration { get => (TimeSpan)GetValue(RollbackDurationProperty); set => SetValue(RollbackDurationProperty, value); }
    public Storyboard? ProgressStoryboard { get => (Storyboard?)GetValue(ProgressStoryboardProperty); set => SetValue(ProgressStoryboardProperty, value); }
    public Storyboard? RollbackStoryboard { get => (Storyboard?)GetValue(RollbackStoryboardProperty); set => SetValue(RollbackStoryboardProperty, value); }

    /// <summary>Fired once when the press is held for <see cref="Duration"/>.</summary>
    public event EventHandler<EventArgs>? Confirmed;

    /// <summary>
    /// Fired on every <c>PointerPressed</c> just BEFORE the progress storyboard begins.
    /// Subscribers can use this to retarget the storyboard's starting values to the
    /// current transform state — making rapid press/release/press feel like a resumed
    /// animation instead of restarting from zero.
    /// </summary>
    public event EventHandler<EventArgs>? Pressed;

    /// <summary>True between <c>PointerPressed</c> and either confirmation or release.</summary>
    public bool IsPressing { get; private set; }

    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.PointerPressed += OnPressed;
        AssociatedObject.PointerReleased += OnReleased;
        AssociatedObject.PointerCaptureLost += OnCaptureLost;
        AssociatedObject.PointerCanceled += OnCaptureLost;
    }

    protected override void OnDetaching()
    {
        AssociatedObject.PointerPressed -= OnPressed;
        AssociatedObject.PointerReleased -= OnReleased;
        AssociatedObject.PointerCaptureLost -= OnCaptureLost;
        AssociatedObject.PointerCanceled -= OnCaptureLost;
        UnsubscribeProgress();
        base.OnDetaching();
    }

    private void OnPressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.Handled) return;
        AssociatedObject.CapturePointer(e.Pointer);
        IsPressing = true;

        // Fire BEFORE Begin so subscribers can retarget keyframe starting values to the
        // current transform state — necessary for "resume from where rollback got to"
        // behavior on rapid press/release sequences.
        Pressed?.Invoke(this, EventArgs.Empty);

        UnsubscribeProgress();
        if (ProgressStoryboard is { } sb)
        {
            sb.Completed += OnProgressCompleted;
            sb.Begin();
        }
    }

    private void OnReleased(object sender, PointerRoutedEventArgs e) => AbortIfPending(e.Pointer);
    private void OnCaptureLost(object sender, PointerRoutedEventArgs e) => AbortIfPending(e.Pointer);

    private void AbortIfPending(Pointer pointer)
    {
        if (!IsPressing) return;
        IsPressing = false;

        UnsubscribeProgress();
        // DO NOT call ProgressStoryboard.Stop() here: that would release the animated value
        // and snap ScaleX to its source (0) instantly, which makes the rollback animation
        // appear to do nothing. Letting Progress stay in HoldEnd and just beginning Rollback
        // means Rollback's "To=0" animates from the current Filling value (e.g. 0.45) down
        // to 0 along the rollback curve — that's the visible "hyu〜n" the user expects.
        RollbackStoryboard?.Begin();
        try { AssociatedObject.ReleasePointerCapture(pointer); }
        catch (Exception) { /* element may already have lost the pointer — non-fatal */ }
    }

    private void OnProgressCompleted(object? sender, object e)
    {
        UnsubscribeProgress();
        IsPressing = false;
        AssociatedObject.ReleasePointerCaptures();
        Confirmed?.Invoke(this, EventArgs.Empty);
    }

    private void UnsubscribeProgress()
    {
        if (ProgressStoryboard is { } sb) sb.Completed -= OnProgressCompleted;
    }
}
