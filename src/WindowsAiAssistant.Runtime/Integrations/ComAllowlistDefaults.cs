using WindowsAiAssistant.Runtime.Config;

namespace WindowsAiAssistant.Runtime.Integrations;

public static class ComAllowlistDefaults
{
    public static List<ComAllowedOperation> CreateDefault() =>
    [
        new() { ProgId = "Word.Application", Methods = ["Documents.Add", "ActiveDocument.Save", "Quit", "Visible"] },
        new() { ProgId = "Excel.Application", Methods = ["Workbooks.Add", "ActiveWorkbook.Save", "Quit", "Visible"] },
        new() { ProgId = "PowerPoint.Application", Methods = ["Presentations.Add", "Quit", "Visible"] },
        new() { ProgId = "Outlook.Application", Methods = ["CreateItem", "Quit"] },
        new() { ProgId = "Shell.Application", Methods = ["ShellExecute"] }
    ];
}
