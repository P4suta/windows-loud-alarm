using Alarm.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;

namespace Alarm.Presentation;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    private bool _isHoveringCancel;

    // Touching distance between the Cancel (left) and Normal (right) text centres so the
    // hover swap conveys them as a single body. Recomputed in OnArmedTextSizeChanged.
    private double _conveyorTouchingDistance;

    public MainWindow(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Title = "Alarm";

        IdlePanel.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, OnPanelVisibilityChanged);
        ArmedPanel.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, OnPanelVisibilityChanged);

        // Drive the inverted-text clip from the same ScaleX the black bar uses. Every time
        // ArmFillScale.ScaleX changes (storyboards animate it at frame cadence, the abort
        // path sets it directly to 0), we recompute Clip.Rect.Width = host.Width * ScaleX.
        // That keeps the white text revealed exactly where the bar has reached — no separate
        // animation pipeline to fall out of sync.
        ArmFillScale.RegisterPropertyChangedCallback(ScaleTransform.ScaleXProperty, OnArmFillScaleChanged);
        CancelFillScale.RegisterPropertyChangedCallback(ScaleTransform.ScaleXProperty, OnCancelFillScaleChanged);
        ArmGestureHost.SizeChanged += OnArmGestureHostSizeChanged;
        CountdownHost.SizeChanged += OnCountdownHostSizeChanged;
    }

    // ───── x:Bind helpers ─────

    public Visibility VisibleWhenTrue(bool value) =>
        value ? Visibility.Visible : Visibility.Collapsed;

    public Visibility VisibleWhenFalse(bool value) =>
        value ? Visibility.Collapsed : Visibility.Visible;

    public Visibility VisibleWhenIdle(bool isArmed, bool isRinging) =>
        (!isArmed && !isRinging) ? Visibility.Visible : Visibility.Collapsed;

    public Visibility VisibleWhenArmed(bool isArmed, bool isRinging) =>
        (isArmed && !isRinging) ? Visibility.Visible : Visibility.Collapsed;

    // ───── Initial pulse animations + visibility-driven enter animations ─────

    private void OnRootLoaded(object sender, RoutedEventArgs e)
    {
        TimePulseStoryboard.Begin();
        RingingPulseStoryboard.Begin();
        if (IdlePanel.Visibility == Visibility.Visible)
            IdleEnterStoryboard.Begin();
    }

    private void OnPanelVisibilityChanged(DependencyObject sender, DependencyProperty dp)
    {
        if (sender is not UIElement element || element.Visibility != Visibility.Visible) return;
        if (ReferenceEquals(element, IdlePanel))
            IdleEnterStoryboard.Begin();
        else if (ReferenceEquals(element, ArmedPanel))
            ArmedEnterStoryboard.Begin();
    }

    // ───── Long-press: rapid-tap resume support ─────

    // Behavior.Pressed fires just before ProgressStoryboard.Begin(). We retarget every
    // SplineDoubleKeyFrame's KeyTime=0 value to the current transform state, so the
    // animation appears to "resume from where it left off" rather than snap back to zero.
    // The Rollback storyboard is also targeting the same properties, so the new Begin()
    // naturally overrides it — no need to Stop() Rollback explicitly.
    private void OnArmGesturePressed(object sender, EventArgs e)
    {
        SetKeyFrameStart(ArmFillFrames, ArmFillScale.ScaleX);
        SetKeyFrameStart(IdleTextScaleXFrames, IdleTextScale.ScaleX);
        SetKeyFrameStart(IdleTextScaleYFrames, IdleTextScale.ScaleY);
        SetKeyFrameStart(IdleInvertedTextScaleXFrames, IdleInvertedTextScale.ScaleX);
        SetKeyFrameStart(IdleInvertedTextScaleYFrames, IdleInvertedTextScale.ScaleY);
    }

    private void OnCancelGesturePressed(object sender, EventArgs e)
    {
        SetKeyFrameStart(CancelFillFrames, CancelFillScale.ScaleX);
        SetKeyFrameStart(ArmedTextScaleXFrames, ArmedTextScale.ScaleX);
        SetKeyFrameStart(ArmedTextScaleYFrames, ArmedTextScale.ScaleY);
        SetKeyFrameStart(ArmedInvertedTextScaleXFrames, ArmedInvertedTextScale.ScaleX);
        SetKeyFrameStart(ArmedInvertedTextScaleYFrames, ArmedInvertedTextScale.ScaleY);
    }

    private static void SetKeyFrameStart(DoubleAnimationUsingKeyFrames frames, double value)
    {
        if (frames.KeyFrames.Count > 0 && frames.KeyFrames[0] is SplineDoubleKeyFrame frame)
            frame.Value = value;
    }

    // ───── Long-press confirmations → exit storyboard → command execution ─────

    private void OnArmConfirmed(object sender, EventArgs e) => IdleExitStoryboard.Begin();

    private void OnCancelConfirmed(object sender, EventArgs e)
    {
        // If hover was active, fold the swap-back into the exit so the visual state lands
        // at idle by the time ArmedPanel's Visibility flips to Collapsed. No need to call
        // SeatConveyorAtIdle here — OnCancelTextExitCompleted does that once the swap settles.
        if (_isHoveringCancel)
        {
            _isHoveringCancel = false;
            if (_conveyorTouchingDistance > 0)
            {
                ExitNormalXAnim.To = 0;
                ExitCancelXAnim.To = -_conveyorTouchingDistance;
                ExitInvertedNormalXAnim.To = 0;
                ExitInvertedCancelXAnim.To = -_conveyorTouchingDistance;
            }
            CancelTextExitStoryboard.Begin();
        }
        ArmedExitStoryboard.Begin();
    }

    // No direct property resets here: IdleExitStoryboard now collapses the long-press bar
    // (ArmFillScale 1→0) in lockstep with the panel leaving, and IdleEnterStoryboard plays
    // From=-260/0.88/0 on the next cycle. The HoldEnd Filling state is therefore exactly what
    // the next Enter animation expects — no Storyboard.Stop / direct sets / ResetVisuals needed.
    private void OnIdleExitCompleted(object sender, object e)
    {
        if (ViewModel.ArmCommand.CanExecute(parameter: null))
            ViewModel.ArmCommand.Execute(parameter: null);
    }

    private void OnArmedExitCompleted(object sender, object e)
    {
        if (ViewModel.CancelCommand.CanExecute(parameter: null))
            ViewModel.CancelCommand.Execute(parameter: null);
    }

    // ───── Hover text swap on the Cancel area (single-conveyor motion) ─────

    /// <summary>
    /// Recomputes the conveyor's "touching distance" so CANCEL's right edge meets Normal's
    /// left edge at the swap point. Called on every text resize — when this fires before
    /// the user hovers, we also seat CANCEL's resting position so it's already adjacent
    /// to Normal (just out of view to the left) when the first hover arrives.
    /// </summary>
    private void OnArmedTextSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var normalW = ArmedNormalText.ActualWidth;
        var cancelW = ArmedCancelText.ActualWidth;
        if (normalW <= 0 || cancelW <= 0) return;

        _conveyorTouchingDistance = (normalW + cancelW) / 2.0;

        if (!_isHoveringCancel)
            SeatConveyorAtIdle();
    }

    private void SeatConveyorAtIdle()
    {
        ArmedNormalTranslate.X = 0;
        ArmedCancelTranslate.X = -_conveyorTouchingDistance;
        ArmedInvertedNormalTranslate.X = 0;
        ArmedInvertedCancelTranslate.X = -_conveyorTouchingDistance;
    }

    private void OnCountdownEntered(object sender, PointerRoutedEventArgs e)
    {
        if (_isHoveringCancel) return;
        if (_conveyorTouchingDistance <= 0) return; // texts haven't measured yet
        _isHoveringCancel = true;

        EnterNormalXAnim.To = _conveyorTouchingDistance;
        EnterCancelXAnim.To = 0;
        EnterInvertedNormalXAnim.To = _conveyorTouchingDistance;
        EnterInvertedCancelXAnim.To = 0;
        CancelTextEnterStoryboard.Begin();
    }

    private void OnCountdownExited(object sender, PointerRoutedEventArgs e)
    {
        if (CancelGesture.IsPressing) return;
        if (!_isHoveringCancel) return;
        if (_conveyorTouchingDistance <= 0) return;
        _isHoveringCancel = false;

        ExitNormalXAnim.To = 0;
        ExitCancelXAnim.To = -_conveyorTouchingDistance;
        ExitInvertedNormalXAnim.To = 0;
        ExitInvertedCancelXAnim.To = -_conveyorTouchingDistance;
        CancelTextExitStoryboard.Begin();
    }

    // CancelTextExitStoryboard runs with FillBehavior=Stop, so on completion the translate
    // animations let go and the source values (X=0) win. Re-seat the conveyor so CANCEL
    // lands at the dynamic offset rather than the XAML literal of 0.
    private void OnCancelTextExitCompleted(object sender, object e) => SeatConveyorAtIdle();

    // ───── Inverted text clip — driven by the bar's ScaleX so they stay pixel-aligned ─────

    private void OnArmFillScaleChanged(DependencyObject sender, DependencyProperty dp) =>
        UpdateIdleInvertedClip();

    private void OnCancelFillScaleChanged(DependencyObject sender, DependencyProperty dp) =>
        UpdateArmedInvertedClip();

    private void OnArmGestureHostSizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateIdleInvertedClip();

    private void OnCountdownHostSizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateArmedInvertedClip();

    private void UpdateIdleInvertedClip()
    {
        var hostW = ArmGestureHost.ActualWidth;
        var hostH = ArmGestureHost.ActualHeight;
        if (hostW <= 0 || hostH <= 0) return;
        IdleInvertedClip.Rect = new Rect(0, 0, hostW * ArmFillScale.ScaleX, hostH);
    }

    private void UpdateArmedInvertedClip()
    {
        var hostW = CountdownHost.ActualWidth;
        var hostH = CountdownHost.ActualHeight;
        if (hostW <= 0 || hostH <= 0) return;
        ArmedInvertedClip.Rect = new Rect(0, 0, hostW * CancelFillScale.ScaleX, hostH);
    }

    // ───── Settings gear hover ─────

    private void OnSettingsEntered(object sender, PointerRoutedEventArgs e) =>
        GearRotateInStoryboard.Begin();

    private void OnSettingsExited(object sender, PointerRoutedEventArgs e) =>
        GearRotateOutStoryboard.Begin();
}
