using System.Text;
using Microsoft.Win32.TaskScheduler;
using WindowsAiAssistant.Runtime.Actions;

namespace WindowsAiAssistant.Runtime.Integrations;

public sealed class TaskSchedulerIntegrationService : ITaskSchedulerIntegrationService
{
    private const string TaskFolder = @"\WindowsAiAssistant";

    public ActionResult Execute(
        string mode,
        string? taskName = null,
        string? command = null,
        string? trigger = null,
        string? arguments = null)
    {
        var normalized = (mode ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "create" or "schedule" or "register" => CreateTask(taskName, command, trigger, arguments),
            "delete" or "remove" => DeleteTask(taskName),
            "list" => ListTasks(),
            "run" => RunTask(taskName),
            _ => new ActionResult
            {
                Success = false,
                Message = "schedule_task mode: create|delete|list|run. parameters.name, command, trigger (logon|daily|once), arguments."
            }
        };
    }

    private static ActionResult CreateTask(string? taskName, string? command, string? trigger, string? arguments)
    {
        if (string.IsNullOrWhiteSpace(taskName) || string.IsNullOrWhiteSpace(command))
        {
            return new ActionResult
            {
                Success = false,
                Message = "create icin parameters.name ve parameters.command gerekli."
            };
        }

        try
        {
            using var root = TaskService.Instance;
            var folder = EnsureFolder(root);
            var definition = root.NewTask();
            definition.RegistrationInfo.Description = "Windows AI Assistant scheduled task";
            definition.Settings.Enabled = true;
            definition.Settings.StartWhenAvailable = true;

            definition.Triggers.Add(BuildTrigger(trigger));
            definition.Actions.Add(new ExecAction(command.Trim(), arguments));

            folder.RegisterTaskDefinition(
                taskName.Trim(),
                definition,
                TaskCreation.CreateOrUpdate,
                userId: null,
                password: null,
                logonType: TaskLogonType.InteractiveToken);

            return new ActionResult
            {
                Success = true,
                Message = $"Gorev olusturuldu/guncellendi: {TaskFolder}\\{taskName.Trim()}"
            };
        }
        catch (Exception ex)
        {
            return new ActionResult { Success = false, Message = $"Task Scheduler hatasi: {ex.Message}" };
        }
    }

    private static ActionResult DeleteTask(string? taskName)
    {
        if (string.IsNullOrWhiteSpace(taskName))
        {
            return new ActionResult { Success = false, Message = "delete icin parameters.name gerekli." };
        }

        try
        {
            using var root = TaskService.Instance;
            var folder = root.GetFolder(TaskFolder);
            folder.DeleteTask(taskName.Trim());
            return new ActionResult { Success = true, Message = $"Gorev silindi: {taskName.Trim()}" };
        }
        catch (Exception ex)
        {
            return new ActionResult { Success = false, Message = $"Gorev silinemedi: {ex.Message}" };
        }
    }

    private static ActionResult ListTasks()
    {
        try
        {
            using var root = TaskService.Instance;
            TaskFolder folder;
            try
            {
                folder = root.GetFolder(TaskFolder);
            }
            catch
            {
                return new ActionResult { Success = true, Message = $"(no tasks under {TaskFolder})" };
            }

            using (folder)
            {
                var builder = new StringBuilder();
                foreach (var task in folder.Tasks)
                {
                    builder.AppendLine($"- {task.Name} | state={task.State} | next={task.NextRunTime:O}");
                }

                var text = builder.Length == 0 ? $"(no tasks under {TaskFolder})" : builder.ToString().Trim();
                return new ActionResult { Success = true, Message = text };
            }
        }
        catch (Exception ex)
        {
            return new ActionResult { Success = false, Message = $"Gorev listesi alinamadi: {ex.Message}" };
        }
    }

    private static ActionResult RunTask(string? taskName)
    {
        if (string.IsNullOrWhiteSpace(taskName))
        {
            return new ActionResult { Success = false, Message = "run icin parameters.name gerekli." };
        }

        try
        {
            using var root = TaskService.Instance;
            using var folder = root.GetFolder(TaskFolder);
            using var task = folder.Tasks[taskName.Trim()];
            task.Run();
            return new ActionResult { Success = true, Message = $"Gorev calistirildi: {taskName.Trim()}" };
        }
        catch (Exception ex)
        {
            return new ActionResult { Success = false, Message = $"Gorev calistirilamadi: {ex.Message}" };
        }
    }

    private static TaskFolder EnsureFolder(TaskService root)
    {
        try
        {
            return root.GetFolder(TaskFolder);
        }
        catch
        {
            return root.RootFolder.CreateFolder(TaskFolder);
        }
    }

    private static Trigger BuildTrigger(string? trigger)
    {
        var value = (trigger ?? "logon").Trim().ToLowerInvariant();
        return value switch
        {
            "daily" => new DailyTrigger { StartBoundary = DateTime.Today.AddHours(9) },
            "once" or "one-shot" => new TimeTrigger { StartBoundary = DateTime.Now.AddMinutes(1) },
            _ => new LogonTrigger()
        };
    }
}
