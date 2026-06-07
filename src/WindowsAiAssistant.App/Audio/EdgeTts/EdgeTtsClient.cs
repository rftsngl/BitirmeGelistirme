using System.Net.WebSockets;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;

namespace WindowsAiAssistant.App.Audio.EdgeTts;

internal sealed class EdgeTtsClient
{
    private static readonly Regex VoicePattern = new(
        @"^([a-z]{2,})-([A-Z]{2,})-(.+Neural)$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public async Task SaveMp3Async(
        string text,
        string voiceShortName,
        string outputPath,
        string rate,
        string volume,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(voiceShortName);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var sanitized = SanitizeText(text);
        var voiceDisplayName = ToDisplayVoiceName(voiceShortName);
        var escaped = SecurityElement.Escape(sanitized) ?? sanitized;

        Exception? lastError = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await SynthesizeOnceAsync(
                        escaped,
                        voiceDisplayName,
                        outputPath,
                        rate,
                        volume,
                        cancellationToken)
                    .ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (attempt == 0 && IsRetryableAuthFailure(ex))
            {
                lastError = ex;
                await RefreshClockSkewAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        throw lastError ?? new InvalidOperationException("Edge TTS sentezi başarısız oldu.");
    }

    private static async Task RefreshClockSkewAsync(CancellationToken cancellationToken)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd(EdgeTtsConstants.UserAgent);
        var url =
            $"https://{EdgeTtsConstants.BaseUrl}/voices/list?trustedclienttoken={EdgeTtsConstants.TrustedClientToken}";
        using var response = await http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        EdgeTtsDrm.AdjustClockSkewFromResponse(response);
    }

    private static bool IsRetryableAuthFailure(Exception ex) =>
        ex.Message.Contains("403", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("Forbidden", StringComparison.OrdinalIgnoreCase);

    private static async Task SynthesizeOnceAsync(
        string escapedText,
        string voiceDisplayName,
        string outputPath,
        string rate,
        string volume,
        CancellationToken cancellationToken)
    {
        var connectionId = Guid.NewGuid().ToString("N");
        var url =
            $"{EdgeTtsConstants.WssUrl}"
            + $"&ConnectionId={connectionId}"
            + $"&Sec-MS-GEC={EdgeTtsDrm.GenerateSecMsGec()}"
            + $"&Sec-MS-GEC-Version={EdgeTtsConstants.SecMsGecVersion}";

        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Pragma", "no-cache");
        socket.Options.SetRequestHeader("Cache-Control", "no-cache");
        socket.Options.SetRequestHeader("Origin", "chrome-extension://jdiccldimpdaibmpdkjnbmckianbfold");
        socket.Options.SetRequestHeader("Accept-Encoding", "gzip, deflate, br");
        socket.Options.SetRequestHeader("Accept-Language", "tr-TR,tr;q=0.9,en-US;q=0.8");
        socket.Options.SetRequestHeader("User-Agent", EdgeTtsConstants.UserAgent);
        socket.Options.SetRequestHeader("Cookie", $"muid={EdgeTtsDrm.GenerateMuid()};");
        socket.Options.DangerousDeflateOptions = new WebSocketDeflateOptions
        {
            ClientMaxWindowBits = 15,
            ServerMaxWindowBits = 15
        };

        await socket.ConnectAsync(new Uri(url), cancellationToken).ConfigureAwait(false);

        await SendTextAsync(
                socket,
                BuildSpeechConfigMessage(),
                cancellationToken)
            .ConfigureAwait(false);

        await SendTextAsync(
                socket,
                BuildSsmlMessage(escapedText, voiceDisplayName, rate, volume),
                cancellationToken)
            .ConfigureAwait(false);

        await using var output = new FileStream(
            outputPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            8192,
            useAsync: true);

        var audioReceived = false;

        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            var message = await ReceiveFullMessageAsync(socket, cancellationToken).ConfigureAwait(false);
            if (message is null)
            {
                break;
            }

            if (message.Value.MessageType == WebSocketMessageType.Text)
            {
                var payload = Encoding.UTF8.GetString(message.Value.Payload);
                if (payload.Contains("Path:turn.end", StringComparison.Ordinal))
                {
                    break;
                }

                continue;
            }

            if (message.Value.MessageType != WebSocketMessageType.Binary)
            {
                continue;
            }

            var data = message.Value.Payload;
            if (data.Length < 2)
            {
                continue;
            }

            var headerLength = (data[0] << 8) | data[1];
            if (headerLength <= 0 || headerLength + 2 > data.Length)
            {
                continue;
            }

            var headerText = Encoding.UTF8.GetString(data, 2, headerLength);
            if (!headerText.Contains("Path:audio", StringComparison.Ordinal))
            {
                continue;
            }

            var audioOffset = 2 + headerLength;
            while (audioOffset < data.Length
                   && (data[audioOffset] == (byte)'\r' || data[audioOffset] == (byte)'\n'))
            {
                audioOffset++;
            }

            var audioLength = data.Length - audioOffset;
            if (audioLength <= 0)
            {
                continue;
            }

            await output.WriteAsync(data.AsMemory(audioOffset, audioLength), cancellationToken)
                .ConfigureAwait(false);
            audioReceived = true;
        }

        if (!audioReceived)
        {
            throw new InvalidOperationException(
                "Edge TTS ses verisi alınamadı. İnternet bağlantısını ve güvenlik duvarını kontrol edin.");
        }
    }

    private static async Task SendTextAsync(
        ClientWebSocket socket,
        string message,
        CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken)
            .ConfigureAwait(false);
    }

    private static string BuildSpeechConfigMessage()
    {
        var timestamp = DateTime.UtcNow.ToString(
            "ddd MMM dd yyyy HH:mm:ss 'GMT+0000 (Coordinated Universal Time)'",
            System.Globalization.CultureInfo.InvariantCulture);

        return
            $"X-Timestamp:{timestamp}\r\n"
            + "Content-Type:application/json; charset=utf-8\r\n"
            + "Path:speech.config\r\n\r\n"
            + "{\"context\":{\"synthesis\":{\"audio\":{\"metadataoptions\":{"
            + "\"sentenceBoundaryEnabled\":\"true\",\"wordBoundaryEnabled\":\"false\""
            + "},\"outputFormat\":\"audio-24khz-48kbitrate-mono-mp3\"}}}}\r\n";
    }

    private static string BuildSsmlMessage(
        string escapedText,
        string voiceDisplayName,
        string rate,
        string volume)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var timestamp = DateTime.UtcNow.ToString(
            "ddd MMM dd yyyy HH:mm:ss 'GMT+0000 (Coordinated Universal Time)'",
            System.Globalization.CultureInfo.InvariantCulture);

        var ssml =
            "<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='tr-TR'>"
            + $"<voice name='{voiceDisplayName}'>"
            + $"<prosody pitch='+0Hz' rate='{rate}' volume='{volume}'>"
            + escapedText
            + "</prosody></voice></speak>";

        return
            $"X-RequestId:{requestId}\r\n"
            + "Content-Type:application/ssml+xml\r\n"
            + $"X-Timestamp:{timestamp}Z\r\n"
            + "Path:ssml\r\n\r\n"
            + ssml;
    }

    private static string ToDisplayVoiceName(string shortName)
    {
        var match = VoicePattern.Match(shortName.Trim());
        if (!match.Success)
        {
            return shortName;
        }

        var lang = match.Groups[1].Value;
        var region = match.Groups[2].Value;
        var name = match.Groups[3].Value;
        if (name.Contains('-', StringComparison.Ordinal))
        {
            var dash = name.IndexOf('-', StringComparison.Ordinal);
            region = $"{region}-{name[..dash]}";
            name = name[(dash + 1)..];
        }

        return $"Microsoft Server Speech Text to Speech Voice ({lang}-{region}, {name})";
    }

    private static async Task<(WebSocketMessageType MessageType, byte[] Payload)?> ReceiveFullMessageAsync(
        ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        var buffer = new byte[8192];
        WebSocketMessageType messageType = WebSocketMessageType.Text;

        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            messageType = result.MessageType;
            if (result.Count > 0)
            {
                stream.Write(buffer, 0, result.Count);
            }

            if (result.EndOfMessage)
            {
                break;
            }
        }

        return (messageType, stream.ToArray());
    }

    private static string SanitizeText(string text)
    {
        var chars = text.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var code = (int)chars[i];
            if ((code >= 0 && code <= 8) || (code is >= 11 and <= 12) || (code is >= 14 and <= 31))
            {
                chars[i] = ' ';
            }
        }

        return new string(chars);
    }
}
