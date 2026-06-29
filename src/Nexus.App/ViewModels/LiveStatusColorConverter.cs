using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Nexus.Core.Domain;

namespace Nexus.App.ViewModels;

public sealed class LiveStatusColorConverter : IValueConverter
{
    public static readonly LiveStatusColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is AgentLiveStatus status)
        {
            return status switch
            {
                AgentLiveStatus.Alive => Color.Parse("#A6E3A1"),
                AgentLiveStatus.Idle => Color.Parse("#F9E2AF"),
                AgentLiveStatus.Disconnected => Color.Parse("#F38BA8"),
                _ => Color.Parse("#6C7086")
            };
        }
        return Color.Parse("#6C7086");
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
