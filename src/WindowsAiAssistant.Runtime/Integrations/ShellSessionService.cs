using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using WindowsAiAssistant.Runtime.Actions;

namespace WindowsAiAssistant.Runtime.Integrations;

public sealed class ShellSessionService : IShellSessionService, IDisposable
{
    private readonly ConcurrentDictionary<string, ShellSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public ActionResult Execute(string mode, string? sessionId = null, string? command = null, int maxOutputChars = 4000)
    {
        var normalized = (mode ?? "start").Trim().ToLowerInvariant();
        return normalized switch
        {
            "start" => StartSession(),
            "write" or "run" => Write(sessionId, command, maxOutputChars),
            "read" => Read(sessionId, maxOutputChars),
            "stop" => Stop(sessionId),
            "list" => ListSessions(),
            _ => IntegrationResultHelper.Fail("Desteklenen modlar: start, write, read, stop, list.")
        };
    }

    private ActionResult StartSession()
    {
        try
        {
            var id = Guid.NewGuid().ToString("N")[..8];
            var session = ShellSession.Create();
            if (!_sessions.TryAdd(id, session))
            {
                session.Dispose();
                return IntegrationResultHelper.Fail("Oturum olusturulamadi.");
            }

            return IntegrationResultHelper.Ok($"sessionId={id}\nShell oturumu baslatildi (powershell).");
        }
        catch (Exception ex)
        {
            return IntegrationResultHelper.Fail($"Shell oturumu baslatilamadi: {ex.Message}");
        }
    }

    private ActionResult Write(string? sessionId, string? command, int maxOutputChars)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(command))
        {
            return IntegrationResultHelper.Fail("write icin sessionId ve command gerekli.");
        }

        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            return IntegrationResultHelper.Fail($"Oturum bulunamadi: {sessionId}");
        }

        try
        {
            session.WriteLine(command);
            Thread.Sleep(300);
            var output = session.ReadRecent(maxOutputChars);
            return IntegrationResultHelper.Ok($"sessionId={sessionId}\nexitHint=running\n{output}");
        }
        catch (Exception ex)
        {
            return IntegrationResultHelper.Fail($"Komut gonderilemedi: {ex.Message}");
        }
    }

    private ActionResult Read(string? sessionId, int maxOutputChars)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return IntegrationResultHelper.Fail("read icin sessionId gerekli.");
        }

        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            return IntegrationResultHelper.Fail($"Oturum bulunamadi: {sessionId}");
        }

        return IntegrationResultHelper.Ok($"sessionId={sessionId}\n{session.ReadRecent(maxOutputChars)}");
    }

    private ActionResult Stop(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return IntegrationResultHelper.Fail("stop icin sessionId gerekli.");
        }

        if (_sessions.TryRemove(sessionId, out var session))
        {
            session.Dispose();
            return IntegrationResultHelper.Ok($"Oturum kapatildi: {sessionId}");
        }

        return IntegrationResultHelper.Fail($"Oturum bulunamadi: {sessionId}");
    }

    private ActionResult ListSessions()
    {
        var builder = new StringBuilder();
        foreach (var id in _sessions.Keys)
        {
            builder.AppendLine(id);
        }

        return IntegrationResultHelper.Ok(builder.Length == 0 ? "(aktif oturum yok)" : builder.ToString());
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var id in _sessions.Keys.ToArray())
        {
            if (_sessions.TryRemove(id, out var session))
            {
                session.Dispose();
            }
        }

        _disposed = true;
    }

    private sealed class ShellSession : IDisposable
    {
        private readonly Process _process;
        private readonly StringBuilder _buffer = new();
        private readonly object _gate = new();

        private ShellSession(Process process) => _process = process;

        public static ShellSession Create()
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoLogo -NoProfile",
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                },
                EnableRaisingEvents = true
            };

            var session = new ShellSession(process);
            process.OutputDataReceived += (_, e) => session.Append(e.Data);
            process.ErrorDataReceived += (_, e) => session.Append(e.Data is null ? null : $"[stderr] {e.Data}");

            if (!process.Start())
            {
                throw new InvalidOperationException("PowerShell baslatilamadi.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return session;
        }

        public void WriteLine(string command)
        {
            if (_process.HasExited)
            {
                throw new InvalidOperationException("Shell oturumu kapali.");
            }

            _process.StandardInput.WriteLine(command);
            _process.StandardInput.WriteLine("Write-Output \"__SESSION_READY__\"");
            _process.StandardInput.Flush();
        }

        public string ReadRecent(int maxChars)
        {
            lock (_gate)
            {
                var text = _buffer.ToString();
                return text.Length <= maxChars ? text : text[^maxChars..];
            }
        }

        private void Append(string? line)
        {
            if (string.IsNullOrEmpty(line) || line == "__SESSION_READY__")
            {
                return;
            }

            lock (_gate)
            {
                _buffer.AppendLine(line);
                if (_buffer.Length > 20000)
                {
                    _buffer.Remove(0, _buffer.Length - 15000);
                }
            }
        }

        public void Dispose()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // ignore
            }
            finally
            {
                _process.Dispose();
            }
        }
    }
}
