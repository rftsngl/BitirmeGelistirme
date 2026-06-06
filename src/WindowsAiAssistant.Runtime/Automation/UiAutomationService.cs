using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using WindowsAiAssistant.Runtime.Config;
using WindowsAiAssistant.Runtime.Input;

namespace WindowsAiAssistant.Runtime.Automation;

public sealed class UiAutomationService
{
    private static readonly HashSet<ControlType> InteractiveControlTypes =
    [
        ControlType.Button,
        ControlType.Edit,
        ControlType.CheckBox,
        ControlType.ComboBox,
        ControlType.ListItem,
        ControlType.MenuItem,
        ControlType.TabItem,
        ControlType.Hyperlink,
        ControlType.RadioButton,
        ControlType.TreeItem,
        ControlType.Document,
        ControlType.Text
    ];

    private readonly UiAutomationOptions _options;
    private readonly UiElementRegistry _registry;

    public UiAutomationService(RuntimeOptions runtimeOptions, UiElementRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(runtimeOptions);
        _options = runtimeOptions.UiAutomation ?? new UiAutomationOptions();
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public UiElementRegistry Registry => _registry;

    public Task<UiElementTree?> CaptureWindowTreeAsync(nint windowHandle, CancellationToken cancellationToken = default) =>
        StaTaskRunner.RunAsync(() => CaptureWindowTree(windowHandle), cancellationToken);

    public Task<string> ClickElementAsync(string elementId, CancellationToken cancellationToken = default) =>
        StaTaskRunner.RunAsync(() => ClickElement(elementId), cancellationToken);

    public Task<string> FocusElementAsync(string elementId, CancellationToken cancellationToken = default) =>
        StaTaskRunner.RunAsync(() => FocusElement(elementId), cancellationToken);

    public Task<string> ReadElementAsync(string elementId, CancellationToken cancellationToken = default) =>
        StaTaskRunner.RunAsync(() => ReadElement(elementId), cancellationToken);

    public Task<string> SetValueAsync(string elementId, string value, CancellationToken cancellationToken = default) =>
        StaTaskRunner.RunAsync(() => SetValue(elementId, value), cancellationToken);

    public Task<string> SelectElementAsync(string elementId, CancellationToken cancellationToken = default) =>
        StaTaskRunner.RunAsync(() => SelectElement(elementId), cancellationToken);

    public Task<string> ExpandCollapseElementAsync(string elementId, string? mode, CancellationToken cancellationToken = default) =>
        StaTaskRunner.RunAsync(() => ExpandCollapseElement(elementId, mode), cancellationToken);

    public Task<string> ToggleElementAsync(string elementId, CancellationToken cancellationToken = default) =>
        StaTaskRunner.RunAsync(() => ToggleElement(elementId), cancellationToken);

    public Task<string> ScrollElementAsync(string elementId, string direction, CancellationToken cancellationToken = default) =>
        StaTaskRunner.RunAsync(() => ScrollElement(elementId, direction), cancellationToken);

    private UiElementTree? CaptureWindowTree(nint windowHandle)
    {
        if (windowHandle == nint.Zero)
        {
            return null;
        }

        _registry.Clear();
        using var automation = new UIA3Automation();
        var window = automation.FromHandle(windowHandle);
        if (window is null)
        {
            return null;
        }

        var snapshots = new List<UiElementSnapshot>();
        var truncated = false;
        WalkElement(window, window, depth: 0, snapshots, ref truncated);

        return new UiElementTree
        {
            WindowHandle = windowHandle,
            WindowTitle = window.Name ?? string.Empty,
            Elements = snapshots,
            Truncated = truncated,
            TotalCaptured = snapshots.Count
        };
    }

    private void WalkElement(
        AutomationElement rootWindow,
        AutomationElement element,
        int depth,
        List<UiElementSnapshot> snapshots,
        ref bool truncated)
    {
        if (depth > Math.Clamp(_options.MaxDepth, 1, 12))
        {
            return;
        }

        if (snapshots.Count >= Math.Clamp(_options.MaxElementsPerStep, 10, 500))
        {
            truncated = true;
            return;
        }

        var controlType = element.Properties.ControlType.ValueOrDefault;
        var include = InteractiveControlTypes.Contains(controlType) ||
                      (!string.IsNullOrWhiteSpace(element.Name) && depth <= 2);

        if (include)
        {
            var runtimeId = element.Properties.RuntimeId.ValueOrDefault ?? Array.Empty<int>();
            var automationId = element.Properties.AutomationId.ValueOrDefault ?? string.Empty;
            var name = element.Name ?? string.Empty;
            var elementId = UiElementIdGenerator.Create(controlType, automationId, name, runtimeId);
            var bounds = element.BoundingRectangle;
            var value = ReadValueSafe(element);

            _registry.Register(new UiElementReference
            {
                ElementId = elementId,
                WindowHandle = rootWindow.Properties.NativeWindowHandle.ValueOrDefault,
                RuntimeId = runtimeId,
                X = (int)bounds.X,
                Y = (int)bounds.Y,
                Width = (int)bounds.Width,
                Height = (int)bounds.Height
            });

            snapshots.Add(new UiElementSnapshot
            {
                ElementId = elementId,
                ControlType = controlType.ToString(),
                Name = name,
                AutomationId = automationId,
                Value = value,
                IsEnabled = element.Properties.IsEnabled.ValueOrDefault
            });
        }

        foreach (var child in element.FindAllChildren())
        {
            if (snapshots.Count >= Math.Clamp(_options.MaxElementsPerStep, 10, 500))
            {
                truncated = true;
                return;
            }

            WalkElement(rootWindow, child, depth + 1, snapshots, ref truncated);
        }
    }

    private string ClickElement(string elementId)
    {
        var element = ResolveElement(elementId);
        if (element.Patterns.Invoke.IsSupported)
        {
            element.Patterns.Invoke.Pattern.Invoke();
            return $"InvokePattern: {elementId}";
        }

        if (element.Patterns.Toggle.IsSupported)
        {
            element.Patterns.Toggle.Pattern.Toggle();
            return $"TogglePattern: {elementId}";
        }

        if (element.Patterns.SelectionItem.IsSupported)
        {
            element.Patterns.SelectionItem.Pattern.Select();
            return $"SelectionItemPattern: {elementId}";
        }

        if (element.Patterns.ExpandCollapse.IsSupported)
        {
            var state = element.Patterns.ExpandCollapse.Pattern.ExpandCollapseState;
            if (state == ExpandCollapseState.Collapsed)
            {
                element.Patterns.ExpandCollapse.Pattern.Expand();
            }
            else
            {
                element.Patterns.ExpandCollapse.Pattern.Collapse();
            }

            return $"ExpandCollapsePattern: {elementId}";
        }

        var bounds = element.BoundingRectangle;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new InvalidOperationException("Element bounding box gecersiz; mouse fallback yapilamadi.");
        }

        var centerX = (int)(bounds.X + bounds.Width / 2);
        var centerY = (int)(bounds.Y + bounds.Height / 2);
        MouseInput.Click(centerX, centerY);
        return $"Mouse fallback click: {elementId} @ {centerX},{centerY}";
    }

    private string FocusElement(string elementId)
    {
        var element = ResolveElement(elementId);
        element.Focus();
        return $"Focused: {elementId}";
    }

    private string ReadElement(string elementId)
    {
        var element = ResolveElement(elementId);
        var value = ReadValueSafe(element);
        var name = element.Name ?? string.Empty;
        return string.IsNullOrWhiteSpace(value) ? name : $"{name} | {value}";
    }

    private string SetValue(string elementId, string value)
    {
        var element = ResolveElement(elementId);
        if (element.Patterns.Value.IsSupported)
        {
            element.Patterns.Value.Pattern.SetValue(value);
            return $"ValuePattern set: {elementId}";
        }

        element.Focus();
        DesktopInput.TypeTextViaClipboard(value);
        return $"Clipboard paste fallback: {elementId}";
    }

    private string SelectElement(string elementId)
    {
        var element = ResolveElement(elementId);
        if (element.Patterns.SelectionItem.IsSupported)
        {
            element.Patterns.SelectionItem.Pattern.Select();
            return $"SelectionItemPattern.Select: {elementId}";
        }

        if (element.Patterns.Invoke.IsSupported)
        {
            element.Patterns.Invoke.Pattern.Invoke();
            return $"InvokePattern fallback: {elementId}";
        }

        throw new InvalidOperationException(
            $"Element secilemedi: '{elementId}'. SelectionItemPattern desteklenmiyor.");
    }

    private string ExpandCollapseElement(string elementId, string? mode)
    {
        var element = ResolveElement(elementId);
        if (!element.Patterns.ExpandCollapse.IsSupported)
        {
            throw new InvalidOperationException(
                $"Element genisletilemedi: '{elementId}'. ExpandCollapsePattern desteklenmiyor.");
        }

        var pattern = element.Patterns.ExpandCollapse.Pattern;
        var normalized = mode?.Trim().ToLowerInvariant();
        var shouldExpand = normalized switch
        {
            "expand" => true,
            "collapse" => false,
            _ => pattern.ExpandCollapseState.Value != ExpandCollapseState.Expanded
        };

        if (shouldExpand)
        {
            pattern.Expand();
            return $"ExpandCollapsePattern.Expand: {elementId}";
        }

        pattern.Collapse();
        return $"ExpandCollapsePattern.Collapse: {elementId}";
    }

    private string ToggleElement(string elementId)
    {
        var element = ResolveElement(elementId);
        if (!element.Patterns.Toggle.IsSupported)
        {
            throw new InvalidOperationException(
                $"Element toggle edilemedi: '{elementId}'. TogglePattern desteklenmiyor.");
        }

        element.Patterns.Toggle.Pattern.Toggle();
        var state = element.Patterns.Toggle.Pattern.ToggleState.Value;
        return $"TogglePattern.Toggle: {elementId} -> {state}";
    }

    private string ScrollElement(string elementId, string direction)
    {
        var element = ResolveElement(elementId);

        if (element.Patterns.ScrollItem.IsSupported)
        {
            element.Patterns.ScrollItem.Pattern.ScrollIntoView();
            return $"ScrollItemPattern.ScrollIntoView: {elementId}";
        }

        if (element.Patterns.Scroll.IsSupported)
        {
            var pattern = element.Patterns.Scroll.Pattern;
            var amount = direction.Trim().ToLowerInvariant() switch
            {
                "up" => (ScrollAmount.LargeDecrement, true),
                "down" => (ScrollAmount.LargeIncrement, true),
                "left" => (ScrollAmount.LargeDecrement, false),
                "right" => (ScrollAmount.LargeIncrement, false),
                _ => (ScrollAmount.LargeIncrement, true)
            };

            if (amount.Item2)
            {
                pattern.Scroll(ScrollAmount.NoAmount, amount.Item1);
            }
            else
            {
                pattern.Scroll(amount.Item1, ScrollAmount.NoAmount);
            }

            return $"ScrollPattern.Scroll {direction}: {elementId}";
        }

        throw new InvalidOperationException(
            $"Element kaydirilamadi: '{elementId}'. Scroll/ScrollItem pattern desteklenmiyor.");
    }

    private AutomationElement ResolveElement(string elementId)
    {
        if (!_registry.TryGet(elementId, out var reference) || reference is null)
        {
            throw new InvalidOperationException(
                $"Element bulunamadi: '{elementId}'. Gozlem yenilendi; yalnizca mevcut listedeki elementId kullanin.");
        }

        using var automation = new UIA3Automation();
        var window = automation.FromHandle(reference.WindowHandle)
                   ?? throw new InvalidOperationException("Hedef pencere bulunamadi.");

        var element = FindByRuntimeId(window, reference.RuntimeId)
                      ?? throw new InvalidOperationException(
                          $"Element cozulemedi: '{elementId}'. Gozlem yenilendi; id gecersiz olabilir.");

        return element;
    }

    private static AutomationElement? FindByRuntimeId(AutomationElement root, int[] runtimeId)
    {
        if (runtimeId.Length == 0)
        {
            return null;
        }

        var current = root.Properties.RuntimeId.ValueOrDefault;
        if (current is not null && current.SequenceEqual(runtimeId))
        {
            return root;
        }

        foreach (var child in root.FindAllChildren())
        {
            var match = FindByRuntimeId(child, runtimeId);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static string ReadValueSafe(AutomationElement element)
    {
        try
        {
            if (element.Patterns.Value.IsSupported)
            {
                return element.Patterns.Value.Pattern.Value.Value ?? string.Empty;
            }
        }
        catch
        {
            // ignore unsupported value reads
        }

        return element.Name ?? string.Empty;
    }
}
