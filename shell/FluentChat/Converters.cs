using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace FluentChat;

public sealed class AgoConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not long unix || unix <= 0)
        {
            return "";
        }
        var when = DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime;
        var now = DateTime.Now;
        if (when.Date == now.Date)
        {
            return when.ToString("HH:mm");
        }
        if (when.Date == now.Date.AddDays(-1))
        {
            return "Yesterday";
        }
        return when.ToString("dd/MM/yyyy");
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public sealed class UnreadVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is uint count && count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public sealed class FromMeAlignmentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? HorizontalAlignment.Right : HorizontalAlignment.Left;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public sealed class FromMeBubbleBrush : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        string key = value is true
            ? "AccentFillColorSecondaryBrush"
            : "CardBackgroundFillColorSecondaryBrush";
        return (object)Application.Current.Resources[key];
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public sealed class TimeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is long unix && unix > 0
            ? DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime.ToString("HH:mm")
            : "";

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
