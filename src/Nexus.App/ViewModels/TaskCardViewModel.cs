using CommunityToolkit.Mvvm.ComponentModel;
using Nexus.Core.Domain;
using TaskStatus = Nexus.Core.Domain.TaskStatus;

namespace Nexus.App.ViewModels;

public partial class TaskCardViewModel : ObservableObject
{
    [ObservableProperty] private string _taskId = "";
    [ObservableProperty] private TaskStatus _status = TaskStatus.Pending;
    [ObservableProperty] private int _progressPercent;
    [ObservableProperty] private string _statusNote = "";

    public static TaskCardViewModel FromDomain(TaskItem item) =>
        new()
        {
            TaskId = item.Id,
            Status = item.Status,
            ProgressPercent = item.ProgressPercent
        };
}
