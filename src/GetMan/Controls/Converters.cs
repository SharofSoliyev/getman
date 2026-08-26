using MaterialDesignThemes.Wpf;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using GetMan.Models;
using GetMan.Services;
using GetMan.ViewModels;

namespace GetMan.Controls;

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => !(value is bool b && b);
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => !(value is bool b && b);
}

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        var flag = value is bool b && b;
        if (p as string == "invert") flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => value is Visibility v && v == Visibility.Visible;
}

public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        var isNull = value == null || (value is string s && string.IsNullOrEmpty(s));
        if (p as string == "invert") isNull = !isNull;
        return isNull ? Visibility.Collapsed : Visibility.Visible;
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

public class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        var n = value is int i ? i : 0;
        var visible = n > 0;
        if (p as string == "invert") visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>Binds a radio button / toggle to one value of an enum or string.</summary>
public class EqualityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        if (value == null) return p == null;
        return string.Equals(value.ToString(), p?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object value, Type targetType, object p, CultureInfo c)
    {
        if (value is bool b && b && p != null)
        {
            if (targetType.IsEnum) return Enum.Parse(targetType, p.ToString(), true);
            return p.ToString();
        }
        return Binding.DoNothing;
    }
}

public class EqualityToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        var eq = value != null && string.Equals(value.ToString(), p?.ToString(), StringComparison.OrdinalIgnoreCase);
        return eq ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

public class MethodBrushConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => ThemePalette.ForMethod(value as string);
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

public class TestStatusBrushConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value switch
    {
        TestStatus.Pass => ThemePalette.Get("Ok"),
        TestStatus.Fail => ThemePalette.Get("Danger"),
        _ => ThemePalette.Get("FgMuted")
    };
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

public class LogLevelBrushConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => (value as string) switch
    {
        "error" => ThemePalette.Get("Danger"),
        "warn" => ThemePalette.Get("Warn"),
        "info" => ThemePalette.Get("Info"),
        _ => ThemePalette.Get("FgDim")
    };
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

public class StatusBrushConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        var code = value is int i ? i : 0;
        return code switch
        {
            >= 200 and < 300 => ThemePalette.Get("Ok"),
            >= 300 and < 400 => ThemePalette.Get("Info"),
            >= 400 and < 500 => ThemePalette.Get("Warn"),
            >= 500 => ThemePalette.Get("Danger"),
            _ => ThemePalette.Get("FgMuted")
        };
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

public class BytesConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        TextFormatter.HumanSize(value is long l ? l : 0);
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

public class MillisConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        TextFormatter.HumanTime(value is double d ? d : 0);
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}


public class BoolToBrushConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        var keys = (p as string ?? "Ok|FgMuted").Split('|');
        return ThemePalette.Get(value is bool b && b ? keys[0] : keys.Length > 1 ? keys[1] : keys[0]);
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

public class DirtyMarkerConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value is bool b && b ? "●" : string.Empty;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

public class NodeIconConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value switch
    {
        NodeKind.Collection => PackIconKind.FolderStarMultipleOutline,
        NodeKind.Folder => PackIconKind.FolderOutline,
        _ => PackIconKind.CircleSmall
    };
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

public class TestStatusIconConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value switch
    {
        TestStatus.Pass => PackIconKind.CheckCircle,
        TestStatus.Fail => PackIconKind.CloseCircle,
        _ => PackIconKind.MinusCircle
    };
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

public class LogLevelIconConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => (value as string) switch
    {
        "error" => PackIconKind.AlertCircleOutline,
        "warn" => PackIconKind.AlertOutline,
        "info" => PackIconKind.InformationOutline,
        _ => PackIconKind.ChevronRight
    };
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}
