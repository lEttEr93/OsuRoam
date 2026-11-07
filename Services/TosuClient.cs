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
    long ResultScore,
    // 下面这些是给「信息栏显示游戏内选中的谱面」用的元数据，缺了不影响别的逻辑。
    string Artist = "",
    string Title = "",
    string DifficultyName = "",
    string Mapper = "",
    double Stars = 0,
    double LengthSeconds = 0,
    /// <summary>当前游玩进度（毫秒）。加载阶段是 0；暂停时停在某个正数上。</summary>
    double LiveTime = 0,
    /// <summary>
    /// 选歌界面里预览音频的播放位置（毫秒，和谱面物件同一条时间轴）。
    /// 优先取 gosumemory 兼容段的 <c>menu.bm.time.current</c>，没有就退回 <c>beatmap.time.live</c>
    /// —— tosu 的 v2 里两者是同一个值，分开读只是为了兼容不同版本的 payload。
    /// 0 表示 tosu 没读到这个位置（lazer 下常见）。
    /// </summary>
    double PreviewTime = 0);

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
    // tosu 会把上一局的成绩一直留在 payload 里，用它区分「本局新打出来的成绩」和残留数据。
    private string playStartResultsSignature = string.Empty;
    private string lastResultsSignature = string.Empty;

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
                StatusChanged?.Invoke(Loc.T("tosu.connecting"));
                await socket.ConnectAsync(new Uri(url), cancellationToken);
                StatusChanged?.Invoke(Loc.T("tosu.connected"));
                await receiveAsync(socket, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                AppLog.Error("tosu 连接或读取失败", exception);
                StatusChanged?.Invoke(Loc.T("tosu.waiting"));
                try { await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken); }
                catch (OperationCanceledException) { break; }
            }
        }
        StatusChanged?.Invoke(Loc.T("tosu.disconnected"));
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
            double previewTime = readNumber(root, "menu", "bm", "time", "current");
            if (previewTime <= 0) previewTime = readNumber(root, "beatmap", "time", "live");
            TosuSnapshot snapshot = new(state, beatmapId, beatmapsetId, profile, paused, failed, resultRank, resultScore,
                readString(root, "beatmap", "artist"),
                readString(root, "beatmap", "title"),
                readString(root, "beatmap", "version"),
                readString(root, "beatmap", "mapper"),
                readNumber(root, "beatmap", "stats", "stars", "total"),
                lengthSecondsOf(root),
                readNumber(root, "beatmap", "time", "live"),
                previewTime);
            SnapshotReceived?.Invoke(snapshot);

            if (state == "play" && beatmapId > 0)
            {
                if (!playSessionActive)
                {
                    playSessionActive = true;
                    playStartedRaised = false;
                    playFailed = false;
                    resultHandled = false;
                    playStartResultsSignature = resultsSignature(resultRank, resultScore);
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

            // 结算判定不再强依赖 state 名称：快速跳过结算界面时 tosu 会直接从 play 跳到
            // selectPlay，lazer 下 resultsScreen 也可能整体缺失。只要本局还在进行中、
            // 还没处理过结算，且拿到的成绩不是开局时残留的那份，就按结算处理。
            string signature = resultsSignature(resultRank, resultScore);
            bool hasResults = resultScore > 0 && !string.IsNullOrWhiteSpace(resultRank);
            if (playSessionActive && !resultHandled && hasResults &&
                !string.Equals(signature, playStartResultsSignature, StringComparison.Ordinal) &&
                !string.Equals(signature, lastResultsSignature, StringComparison.Ordinal))
            {
                resultHandled = true;
                lastResultsSignature = signature;
                TosuSnapshot completed = snapshot with
                {
                    BeatmapId = lastPlayedBeatmapId,
                    BeatmapsetId = lastPlayedBeatmapsetId,
                    Failed = playFailed
                };
                if (!playFailed && !string.Equals(resultRank, "F", StringComparison.OrdinalIgnoreCase))
                {
                    AppLog.Info($"结算判定：state={state}, rank={resultRank}, score={resultScore}");
                    PlaySucceeded?.Invoke(completed);
                }
                else
                {
                    AppLog.Info($"本局未成功完成，不导入下一首：state={state}, failed={playFailed}, rank={resultRank}, score={resultScore}");
                }
                return;
            }

            if (state != "resultScreen" && playSessionActive)
            {
                if (!resultHandled)
                    AppLog.Info($"游玩未进入成功结算，不导入下一首：state={state}, failed={playFailed}, rank={resultRank}, score={resultScore}");
                playSessionActive = false;
                playStartedRaised = false;
                playFailed = false;
                resultHandled = false;
            }
        }
        catch (JsonException)
        {
            StatusChanged?.Invoke(Loc.T("tosu.unreadable"));
        }
    }

    private static string resultsSignature(string rank, long score) => $"{rank}|{score}";

    /// <summary>按路径取数值，比如 beatmap → stats → stars → total。</summary>
    private static double readNumber(JsonElement root, params string[] path)
    {
        JsonElement current = root;
        foreach (string name in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name, out current))
                return 0;
        }

        if (current.ValueKind == JsonValueKind.Number && current.TryGetDouble(out double value))
            return value;
        return double.TryParse(current.ToString(), out value) ? value : 0;
    }

    /// <summary>时长取首尾物件的时间差；tosu 给的是毫秒。</summary>
    private static double lengthSecondsOf(JsonElement root)
    {
        double first = readNumber(root, "beatmap", "time", "firstObject");
        double last = readNumber(root, "beatmap", "time", "lastObject");
        return last > first ? (last - first) / 1000.0 : 0;
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
