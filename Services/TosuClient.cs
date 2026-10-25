using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace OsuRoam.Services;

public sealed record TosuSnapshot(
    string StateName,
    int BeatmapId,
    int BeatmapsetId,
    string ProfileName,
    bool IsPaused,
    bool Failed,
    string ResultRank,
    long ResultScore);

public sealed class TosuClient : IAsyncDisposable
{
    private CancellationTokenSource? stopSource;
    private Task? worker;
    private bool playSessionActive;
    private bool playStartedRaised;
    private bool playFailed;
    private bool resultHandled;
    private int lastPlayedBeatmapId;
    private int lastPlayedBeatmapsetId;

    public event Action<string>? StatusChanged;
    public event Action<TosuSnapshot>? SnapshotReceived;
    public event Action<TosuSnapshot>? PlayStarted;
    public event Action<TosuSnapshot>? PlaySucceeded;

    public void Start(string url)
    {
        if (worker is { IsCompleted: false })
            return;
        stopSource = new CancellationTokenSource();
        worker = runLoopAsync(url, stopSource.Token);
    }

    public async Task StopAsync()
    {
        if (stopSource is null)
            return;
        stopSource.Cancel();
        if (worker is not null)
        {
            try { await worker; }
            catch (OperationCanceledException) { }
        }
        stopSource.Dispose();
        stopSource = null;
        worker = null;
    }

    private async Task runLoopAsync(string url, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using ClientWebSocket socket = new();
                StatusChanged?.Invoke("正在连接 tosu...");
                await socket.ConnectAsync(new Uri(url), cancellationToken);
                StatusChanged?.Invoke("tosu 已连接");
                await receiveAsync(socket, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                AppLog.Error("tosu 连接或读取失败", exception);
                StatusChanged?.Invoke("等待 tosu 启动");
                try { await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken); }
                catch (OperationCanceledException) { break; }
            }
        }
        StatusChanged?.Invoke("tosu 已断开");
    }

    private async Task receiveAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[32 * 1024];
        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            using MemoryStream message = new();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                    return;
                message.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            if (result.MessageType != WebSocketMessageType.Text)
                continue;
            string json = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
            ProcessSnapshotJson(json);
        }
    }

    internal void ProcessSnapshotJson(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            string state = readState(root);
            if (string.IsNullOrWhiteSpace(state)) state = readString(root, "menu", "state");
            int beatmapId = readInt(root, "beatmap", "id");
            int beatmapsetId = readInt(root, "beatmap", "set");
            if (beatmapsetId <= 0) beatmapsetId = readInt(root, "beatmap", "setId");
            if (beatmapsetId <= 0) beatmapsetId = readInt(root, "beatmap", "beatmapset_id");
            string profile = readString(root, "profile", "name");
            bool paused = readBool(root, "game", "paused");
            bool failed = readBool(root, "play", "failed");
            string resultRank = readString(root, "resultsScreen", "rank");
            long resultScore = readLong(root, "resultsScreen", "score");
            TosuSnapshot snapshot = new(state, beatmapId, beatmapsetId, profile, paused, failed, resultRank, resultScore);
            SnapshotReceived?.Invoke(snapshot);

            if (state == "play" && beatmapId > 0)
            {
                if (!playSessionActive)
                {
                    playSessionActive = true;
                    playStartedRaised = false;
                    playFailed = false;
                    resultHandled = false;
                }
                lastPlayedBeatmapId = beatmapId;
                lastPlayedBeatmapsetId = beatmapsetId;
                playFailed |= failed;
                if (!paused && !failed && !playStartedRaised)
                {
                    playStartedRaised = true;
                    PlayStarted?.Invoke(snapshot);
                }
                return;
            }

            if (state == "resultScreen" && playSessionActive && !resultHandled &&
                !string.IsNullOrWhiteSpace(resultRank) && resultScore > 0)
            {
                resultHandled = true;
                TosuSnapshot completed = snapshot with
                {
                    BeatmapId = lastPlayedBeatmapId,
                    BeatmapsetId = lastPlayedBeatmapsetId,
                    Failed = playFailed
                };
                if (!playFailed && !string.Equals(resultRank, "F", StringComparison.OrdinalIgnoreCase) && resultScore > 0)
                    PlaySucceeded?.Invoke(completed);
                else
                    AppLog.Info($"本局未成功完成，不导入下一首：failed={playFailed}, rank={resultRank}, score={resultScore}");
                return;
            }

            if (state != "resultScreen" && playSessionActive)
            {
                if (!resultHandled)
                    AppLog.Info($"游玩未进入成功结算，不导入下一首：state={state}, failed={playFailed}");
                playSessionActive = false;
                playStartedRaised = false;
                playFailed = false;
                resultHandled = false;
            }
        }
        catch (JsonException)
        {
            StatusChanged?.Invoke("tosu 数据格式暂不可读");
        }
    }

    private static string readString(JsonElement root, string objectName, string propertyName)
    {
        if (!root.TryGetProperty(objectName, out JsonElement parent) || parent.ValueKind != JsonValueKind.Object ||
            !parent.TryGetProperty(propertyName, out JsonElement value))
            return string.Empty;
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString();
    }

    private static string readState(JsonElement root)
    {
        if (!root.TryGetProperty("state", out JsonElement state))
            return string.Empty;
        if (state.ValueKind == JsonValueKind.String)
            return state.GetString() ?? string.Empty;
        if (state.ValueKind == JsonValueKind.Object && state.TryGetProperty("name", out JsonElement name))
            return name.ValueKind == JsonValueKind.String ? name.GetString() ?? string.Empty : name.ToString();
        return string.Empty;
    }

    private static int readInt(JsonElement root, string objectName, string propertyName)
    {
        if (!root.TryGetProperty(objectName, out JsonElement parent) || parent.ValueKind != JsonValueKind.Object ||
            !parent.TryGetProperty(propertyName, out JsonElement value))
            return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number))
            return number;
        return int.TryParse(value.ToString(), out number) ? number : 0;
    }

    private static long readLong(JsonElement root, string objectName, string propertyName)
    {
        if (!root.TryGetProperty(objectName, out JsonElement parent) || parent.ValueKind != JsonValueKind.Object ||
            !parent.TryGetProperty(propertyName, out JsonElement value))
            return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number))
            return number;
        return long.TryParse(value.ToString(), out number) ? number : 0;
    }

    private static bool readBool(JsonElement root, string objectName, string propertyName)
    {
        if (!root.TryGetProperty(objectName, out JsonElement parent) || parent.ValueKind != JsonValueKind.Object ||
            !parent.TryGetProperty(propertyName, out JsonElement value))
            return false;
        return value.ValueKind == JsonValueKind.True ||
               (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out bool result) && result);
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
