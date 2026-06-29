using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Nexus.Core.Domain;

namespace Nexus.App.ViewModels;

public partial class AgentStatusViewModel : ObservableObject
{
    [ObservableProperty] private string _agentId = "";
    [ObservableProperty] private AgentLiveStatus _liveStatus = AgentLiveStatus.Disconnected;
    [ObservableProperty] private string _lastSeen = "";

    partial void OnLiveStatusChanged(AgentLiveStatus value) =>
        OnPropertyChanged(nameof(DotColor));

    public IBrush DotColor => LiveStatus switch
    {
        AgentLiveStatus.Alive => Brushes.LightGreen,
        AgentLiveStatus.Idle => Brushes.Yellow,
        AgentLiveStatus.Disconnected => Brushes.OrangeRed,
        _ => Brushes.Gray
    };

    public static AgentStatusViewModel FromDomain(AgentInfo info) =>
        new()
        {
            AgentId = info.Id,
            LiveStatus = info.Live,
            LastSeen = info.LastSeen.ToString("HH:mm:ss")
        };
}
