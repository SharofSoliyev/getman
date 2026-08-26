using System.Windows;

namespace GetMan.Themes;

/// <summary>
/// Motion tokens. Windows exposes a "show animations inside windows" preference
/// (Settings, Accessibility, Visual effects); when it is off every duration collapses to zero
/// so the same triggers still put controls in the right state, just without the travel.
/// This dictionary is merged before the control styles, so their StaticResource lookups
/// pick up whichever value we settle on here.
/// </summary>
public partial class MotionResources : ResourceDictionary
{
    public MotionResources()
    {
        InitializeComponent();

        if (SystemParameters.ClientAreaAnimation) return;

        var instant = new Duration(TimeSpan.Zero);
        this["DurFast"] = instant;
        this["DurSlow"] = instant;
        this["DurPop"] = instant;
    }
}
