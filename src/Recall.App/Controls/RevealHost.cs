using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Recall.Controls;

/// A content host that fades and lifts each new view into place. Navigation is the
/// loudest state change in the app, so it is the one that gets motion.
public sealed class RevealHost : ContentControl
{
    private static readonly TimeSpan RevealTime = TimeSpan.FromMilliseconds(240);

    private readonly TranslateTransform _lift = new();
    private readonly CubicEase _ease = new() { EasingMode = EasingMode.EaseOut };

    public RevealHost()
    {
        RenderTransform = _lift;
        Loaded += (_, _) => Reveal();
    }

    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);
        Reveal();
    }

    private void Reveal()
    {
        // Reduced motion is a system setting, not a preference to override.
        if (!SystemParameters.ClientAreaAnimation || !IsLoaded)
        {
            Opacity = 1;
            _lift.Y = 0;
            return;
        }

        Opacity = 0;
        _lift.Y = 14;

        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, RevealTime) { EasingFunction = _ease });
        _lift.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(14, 0, RevealTime) { EasingFunction = _ease });
    }
}
