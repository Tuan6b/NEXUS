using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexus.Core.Domain;
using TaskStatus = Nexus.Core.Domain.TaskStatus;

namespace Nexus.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private AppHost? _host;

    [ObservableProperty] private string _taskInput = "";
    [ObservableProperty] private bool _isSubmitting;

    public ObservableCollection<TaskCardViewModel> PendingTasks { get; } = new();
    public ObservableCollection<TaskCardViewModel> RunningTasks { get; } = new();
    public ObservableCollection<TaskCardViewModel> DoneTasks { get; } = new();
    public ObservableCollection<TaskCardViewModel> FailedTasks { get; } = new();
    public ObservableCollection<AgentStatusViewModel> Agents { get; } = new();
    // UC-03 step 3 / FR-32: activity log, newest entry first
    public ObservableCollection<string> ActivityLog { get; } = new();

    public void SetHost(AppHost host)
    {
        _host = host;
        _host.TaskCreated += OnTaskCreated;
        _host.TaskStatusChanged += OnTaskStatusChanged;
        _host.TaskProgressUpdated += OnTaskProgressUpdated;
        _host.AgentRegistered += OnAgentRegistered;
        _host.TasksRestored += OnTasksRestored;
    }

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private async Task SubmitAsync()
    {
        if (_host is null || string.IsNullOrWhiteSpace(TaskInput)) return;
        IsSubmitting = true;
        var submitted = TaskInput;
        try
        {
            Log($"Submitting: \"{submitted}\"");
            await _host.SubmitTaskAsync(submitted);
            TaskInput = "";
        }
        catch (InvalidOperationException ex)
        {
            // UC-01 Alt 4a: circular dependency or other submit-time validation error
            Log($"[Error] {ex.Message}");
        }
        finally
        {
            IsSubmitting = false;
        }
    }

    private bool CanSubmit() => !IsSubmitting && !string.IsNullOrWhiteSpace(TaskInput);

    partial void OnTaskInputChanged(string value) => SubmitCommand.NotifyCanExecuteChanged();
    partial void OnIsSubmittingChanged(bool value) => SubmitCommand.NotifyCanExecuteChanged();

    private void Log(string message)
    {
        var entry = $"[{DateTime.Now:HH:mm:ss}] {message}";
        Dispatcher.UIThread.Post(() => ActivityLog.Insert(0, entry));
    }

    private void OnTaskCreated(TaskItem item) =>
        Dispatcher.UIThread.Post(() =>
        {
            PendingTasks.Add(TaskCardViewModel.FromDomain(item));
            Log($"Task '{item.Id}' created → PENDING (owns: {string.Join(", ", item.OwnsFiles)})");
        });

    private void OnTaskStatusChanged(string taskId, TaskStatus newStatus)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var card = FindCard(taskId);
            if (card is null) return;

            RemoveFromAll(card);
            card.Status = newStatus;

            switch (newStatus)
            {
                case TaskStatus.Pending: PendingTasks.Add(card); break;
                case TaskStatus.Running: RunningTasks.Add(card); break;
                case TaskStatus.Done: DoneTasks.Add(card); break;
                case TaskStatus.Failed:
                case TaskStatus.Escalated: FailedTasks.Add(card); break;
            }

            Log($"Task '{taskId}' → {newStatus}");
        });
    }

    private void OnTaskProgressUpdated(string taskId, int percent, string note)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var card = FindCard(taskId);
            if (card is null) return;
            card.ProgressPercent = percent;
            card.StatusNote = note;
            if (percent == 100)
                Log($"Task '{taskId}' reached 100%");
        });
    }

    private void OnAgentRegistered(AgentInfo info) =>
        Dispatcher.UIThread.Post(() =>
        {
            var existing = Agents.FirstOrDefault(a => a.AgentId == info.Id);
            if (existing is not null)
            {
                existing.LiveStatus = info.Live;
                existing.LastSeen = info.LastSeen.ToString("HH:mm:ss");
            }
            else
            {
                Agents.Add(AgentStatusViewModel.FromDomain(info));
            }
            Log($"Agent '{info.Id}' registered (type: {info.AdapterType}, status: {info.Live})");
        });

    private void OnTasksRestored(IEnumerable<TaskItem> tasks)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var list = tasks.ToList();
            foreach (var t in list)
            {
                var card = TaskCardViewModel.FromDomain(t);
                switch (t.Status)
                {
                    case TaskStatus.Pending: PendingTasks.Add(card); break;
                    case TaskStatus.Running: RunningTasks.Add(card); break;
                }
            }
            if (list.Count > 0)
                Log($"Restored {list.Count} open task(s) from previous session");
        });
    }

    private TaskCardViewModel? FindCard(string taskId)
    {
        return PendingTasks.FirstOrDefault(c => c.TaskId == taskId)
            ?? RunningTasks.FirstOrDefault(c => c.TaskId == taskId)
            ?? DoneTasks.FirstOrDefault(c => c.TaskId == taskId)
            ?? FailedTasks.FirstOrDefault(c => c.TaskId == taskId);
    }

    private void RemoveFromAll(TaskCardViewModel card)
    {
        PendingTasks.Remove(card);
        RunningTasks.Remove(card);
        DoneTasks.Remove(card);
        FailedTasks.Remove(card);
    }
}
