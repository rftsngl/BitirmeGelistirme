using System.IO.Pipes;
using System.Text;

namespace WindowsAiAssistant.App.Services;

/// <summary>
/// Ikinci baslatmada calisan oturuma komut iletir (pencereyi one getir vb.).
/// </summary>
public static class SingleInstanceCoordinator
{
    public const string PipeName = "WindowsAiAssistant_SingleInstance_Pipe_v1";
    private static CancellationTokenSource? _listenerCts;

    public static void StartListener(Action<string> onCommand)
    {
        _listenerCts?.Cancel();
        _listenerCts = new CancellationTokenSource();
        var token = _listenerCts.Token;

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                await using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                try
                {
                    await server.WaitForConnectionAsync(token).ConfigureAwait(false);
                    using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
                    var command = await reader.ReadLineAsync(token).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(command))
                    {
                        onCommand(command.Trim());
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    await Task.Delay(250, token).ConfigureAwait(false);
                }
            }
        }, token);
    }

    public static bool TryNotifyPrimaryInstance(string command = "SHOW")
    {
        try
        {
            using var client = new NamedPipeClientStream(
                ".",
                PipeName,
                PipeDirection.Out,
                PipeOptions.Asynchronous);
            client.Connect(1500);
            using var writer = new StreamWriter(client, Encoding.UTF8, leaveOpen: false) { AutoFlush = true };
            writer.WriteLine(command);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void StopListener()
    {
        _listenerCts?.Cancel();
        _listenerCts?.Dispose();
        _listenerCts = null;
    }
}
