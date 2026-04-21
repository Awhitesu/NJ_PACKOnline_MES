using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var fileJsonOptions = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true
};

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyHeader()
              .AllowAnyMethod()
              .SetIsOriginAllowed(_ => true)
              .AllowCredentials();
    });
});

var scannerRuntime = new BarcodeScannerRuntime();

var app = builder.Build();

app.Lifetime.ApplicationStopping.Register(() =>
{
    scannerRuntime.Stop();
});

app.UseCors("AllowAll");

app.MapGet("/", () => "PACK Material Station Backend Running...");

app.MapPost("/saveLogs", async (LogSaveRequest req) =>
{
    try
    {
        var savePath = string.IsNullOrWhiteSpace(req.Path) ? "C:\\NJ_Material_Logs" : req.Path;
        if (!Directory.Exists(savePath))
        {
            Directory.CreateDirectory(savePath);
        }

        var fullPath = Path.Combine(savePath, req.FileName);
        await File.WriteAllTextAsync(fullPath, req.Content);
        return Results.Ok(new { message = "Save success", path = fullPath });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

app.MapGet("/orderStatusSelection", async () =>
{
    try
    {
        var stateFile = GetOrderStatusStateFilePath();
        if (!File.Exists(stateFile))
        {
            return Results.Json(new { exists = false });
        }

        var content = await File.ReadAllTextAsync(stateFile);
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        var selectedCode = ReadStringOrNumber(root, "selectedCode") ?? ReadStringOrNumber(root, "SelectedCode") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(selectedCode))
        {
            return Results.Json(new { exists = false });
        }

        var orderStatus = ReadStringOrNumber(root, "orderStatus") ?? ReadStringOrNumber(root, "OrderStatus") ?? string.Empty;
        if (orderStatus == "2")
        {
            orderStatus = "下发中";
        }
        if (string.IsNullOrWhiteSpace(orderStatus))
        {
            orderStatus = "下发中";
        }

        var updatedAt = ReadStringOrNumber(root, "updatedAt") ?? ReadStringOrNumber(root, "UpdatedAt") ?? string.Empty;

        return Results.Json(new
        {
            exists = true,
            selectedCode,
            orderStatus,
            updatedAt
        });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

app.MapPost("/orderStatusSelection", async (OrderStatusSelectionState req) =>
{
    try
    {
        if (string.IsNullOrWhiteSpace(req.SelectedCode))
        {
            return Results.BadRequest(new { message = "selectedCode is required" });
        }

        var payload = req with
        {
            OrderStatus = string.IsNullOrWhiteSpace(req.OrderStatus) ? "下发中" : req.OrderStatus,
            UpdatedAt = string.IsNullOrWhiteSpace(req.UpdatedAt) ? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") : req.UpdatedAt
        };

        var stateFile = GetOrderStatusStateFilePath();
        var directory = Path.GetDirectoryName(stateFile)!;
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(payload, fileJsonOptions);
        await File.WriteAllTextAsync(stateFile, json);

        return Results.Ok(new { message = "Order status selection saved", path = stateFile });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

app.MapPost("/barcodeScanner/start", (BarcodeScannerStartRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.ScannerIp) || req.ScannerPort <= 0)
    {
        return Results.BadRequest(new { running = false, connected = false, message = "扫码枪IP或端口无效" });
    }

    var result = scannerRuntime.Start(req);
    return Results.Ok(result);
});

app.MapPost("/barcodeScanner/stop", () =>
{
    var result = scannerRuntime.Stop();
    return Results.Ok(result);
});

app.MapGet("/barcodeScanner/status", () => Results.Ok(scannerRuntime.GetStatus()));

app.MapGet("/barcodeScanner/pull", (long afterId) =>
{
    var result = scannerRuntime.Pull(afterId);
    return Results.Ok(result);
});

app.MapPost("/printLabelsByBarTender", async (PrintByBarTenderRequest req) =>
{
    try
    {
        if (string.IsNullOrWhiteSpace(req.BarTenderExePath) ||
            string.IsNullOrWhiteSpace(req.TemplatePath) ||
            string.IsNullOrWhiteSpace(req.DatabasePath))
        {
            return Results.BadRequest(new { success = false, message = "BarTender配置不完整，请检查EXE/模板/数据库路径" });
        }

        if (req.Labels is null || req.Labels.Count == 0)
        {
            return Results.BadRequest(new { success = false, message = "无可打印条码" });
        }

        if (!File.Exists(req.BarTenderExePath))
        {
            return Results.BadRequest(new { success = false, message = $"BarTender程序不存在: {req.BarTenderExePath}" });
        }

        if (!File.Exists(req.TemplatePath))
        {
            return Results.BadRequest(new { success = false, message = $"模板文件不存在: {req.TemplatePath}" });
        }

        var validLabels = req.Labels.Where(x => !string.IsNullOrWhiteSpace(x.Code)).ToList();
        if (validLabels.Count == 0)
        {
            return Results.BadRequest(new { success = false, message = "条码列表为空或无有效条码" });
        }

        var dataDir = Path.GetDirectoryName(req.DatabasePath);
        if (!string.IsNullOrWhiteSpace(dataDir) && !Directory.Exists(dataDir))
        {
            Directory.CreateDirectory(dataDir);
        }

        var lines = new List<string> { "Code,CodeType,CodeTypeName" };
        lines.AddRange(validLabels.Select(x => $"{EscapeCsv(x.Code)},{EscapeCsv(x.Type)},{EscapeCsv(x.TypeName)}"));
        await File.WriteAllLinesAsync(req.DatabasePath, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var barTenderArgs = $"/F=\"{req.TemplatePath}\" /D=\"{req.DatabasePath}\" /P /X";
        var cmdCommand = $"\"{req.BarTenderExePath}\" {barTenderArgs}";
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c {cmdCommand}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(req.BarTenderExePath) ?? AppContext.BaseDirectory
        };

        using var process = Process.Start(psi);
        if (process is null)
        {
            return Results.Ok(new
            {
                success = false,
                message = "BarTender进程启动失败",
                command = $"{psi.FileName} {psi.Arguments}",
                dataFilePath = req.DatabasePath
            });
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { }
            return Results.Ok(new
            {
                success = false,
                message = "BarTender执行超时(120s)",
                command = $"{psi.FileName} {psi.Arguments}",
                dataFilePath = req.DatabasePath
            });
        }

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        if (process.ExitCode != 0)
        {
            return Results.Ok(new
            {
                success = false,
                message = string.IsNullOrWhiteSpace(stderr) ? "BarTender执行失败" : stderr.Trim(),
                exitCode = process.ExitCode,
                command = $"{psi.FileName} {psi.Arguments}",
                output = stdout,
                dataFilePath = req.DatabasePath
            });
        }

        return Results.Ok(new
        {
            success = true,
            message = "BarTender打印完成",
            exitCode = process.ExitCode,
            command = $"{psi.FileName} {psi.Arguments}",
            output = stdout,
            dataFilePath = req.DatabasePath
        });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { success = false, message = $"BarTender调用异常: {ex.Message}" });
    }
});

app.Run();

static string GetOrderStatusStateFilePath()
{
    return Path.Combine("C:\\NJ_Material_Logs", "order_status_selection.json");
}

static string? ReadStringOrNumber(JsonElement root, string propertyName)
{
    if (!root.TryGetProperty(propertyName, out var value))
    {
        return null;
    }

    return value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetRawText(),
        _ => null
    };
}

static string EscapeCsv(string value)
{
    if (string.IsNullOrEmpty(value)) return string.Empty;
    if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
    {
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
    return value;
}

public record LogSaveRequest(string FileName, string Content, string Path);
public record OrderStatusSelectionState(string SelectedCode, string OrderStatus, string UpdatedAt);
public record PrintByBarTenderRequest(string BarTenderExePath, string TemplatePath, string DatabasePath, List<PrintLabelItem> Labels);
public record PrintLabelItem(string Code, string Type, string TypeName);
public record BarcodeScannerStartRequest(string ScannerIp, int ScannerPort, string BarcodeRegex);
public record BarcodeScanEvent(long Id, string Code, string Time);

public class BarcodeScannerRuntime
{
    private const int WelcomeBytesLength = 100;
    private const int BarcodeBytesLength = 30;
    private readonly object _sync = new();
    private readonly ConcurrentQueue<BarcodeScanEvent> _events = new();
    private readonly ConcurrentQueue<string> _ioLogs = new();
    private CancellationTokenSource? _cts;
    private Task? _worker;
    private BarcodeScannerStartRequest? _config;
    private long _lastId;
    private bool _running;
    private bool _connected;
    private string _lastError = string.Empty;

    public object Start(BarcodeScannerStartRequest cfg)
    {
        lock (_sync)
        {
            StopInternal();
            ClearScannerBuffersUnsafe();
            _config = cfg;
            _cts = new CancellationTokenSource();
            _running = true;
            _connected = false;
            _lastError = string.Empty;
            _worker = Task.Run(() => RunLoop(_cts.Token));
            return GetStatusUnsafe();
        }
    }

    public object Stop()
    {
        lock (_sync)
        {
            StopInternal();
            return GetStatusUnsafe();
        }
    }

    public object GetStatus()
    {
        lock (_sync)
        {
            return GetStatusUnsafe();
        }
    }

    public object Pull(long afterId)
    {
        BarcodeScanEvent[] all;
        bool running;
        bool connected;
        string scannerIp;
        int scannerPort;
        string barcodeRegex;
        string lastError;
        lock (_sync)
        {
            all = _events.ToArray();
            running = _running;
            connected = _connected;
            scannerIp = _config?.ScannerIp ?? string.Empty;
            scannerPort = _config?.ScannerPort ?? 0;
            barcodeRegex = _config?.BarcodeRegex ?? string.Empty;
            lastError = _lastError;
        }

        var events = all.Where(x => x.Id > afterId)
                        .OrderBy(x => x.Id)
                        .Take(100)
                        .ToList();

        TrimQueueIfNeeded();

        return new
        {
            running,
            connected,
            scannerIp,
            scannerPort,
            barcodeRegex,
            lastError,
            ioLogs = _ioLogs.ToArray().TakeLast(30).ToArray(),
            events
        };
    }

    private void StopInternal()
    {
        try { _cts?.Cancel(); } catch { }
        _running = false;
        _connected = false;
    }

    private void ClearScannerBuffersUnsafe()
    {
        while (_events.TryDequeue(out _))
        {
        }
        while (_ioLogs.TryDequeue(out _))
        {
        }
        Interlocked.Exchange(ref _lastId, 0);
    }

    private object GetStatusUnsafe()
    {
        return new
        {
            running = _running,
            connected = _connected,
            scannerIp = _config?.ScannerIp ?? string.Empty,
            scannerPort = _config?.ScannerPort ?? 0,
            barcodeRegex = _config?.BarcodeRegex ?? string.Empty,
            lastError = _lastError,
            ioLogs = _ioLogs.ToArray().TakeLast(30).ToArray()
        };
    }

    private async Task RunLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            BarcodeScannerStartRequest? cfg;
            lock (_sync)
            {
                cfg = _config;
            }

            if (cfg is null)
            {
                await Task.Delay(1000, ct);
                continue;
            }

            try
            {
                using var client = new System.Net.Sockets.TcpClient();
                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                connectCts.CancelAfter(5000);
                await client.ConnectAsync(cfg.ScannerIp, cfg.ScannerPort, connectCts.Token);
                LogIo($"TX CONNECT {cfg.ScannerIp}:{cfg.ScannerPort}");

                using var stream = client.GetStream();
                var welcomeBytes = await ReadWelcomeHandshakeAsync(stream, ct);
                if (welcomeBytes.Length > 0)
                {
                    var welcomeText = NormalizeBarcode(Encoding.ASCII.GetString(welcomeBytes));
                    LogIo($"RX WELCOME({welcomeBytes.Length}): {welcomeText}");
                    if (IsScannerBusyMessage(welcomeText))
                    {
                        lock (_sync)
                        {
                            _connected = false;
                            _lastError = $"扫码枪通道占用: {welcomeText}";
                        }
                        LogIo("RX ERROR: 扫码枪通道被占用，1秒后重试");
                        await Task.Delay(1000, ct);
                        continue;
                    }
                }
                else
                {
                    LogIo("RX WELCOME(0): 未读取到欢迎语，继续等待条码");
                }

                lock (_sync)
                {
                    _connected = true;
                    _lastError = string.Empty;
                }

                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        var codeBytes = await ReadExactAsync(stream, BarcodeBytesLength, ct);
                        var raw = Encoding.ASCII.GetString(codeBytes);
                        var code = NormalizeBarcode(raw);
                        if (string.IsNullOrWhiteSpace(code)) continue;
                        if (IsScannerSystemMessage(code)) continue;

                        var id = Interlocked.Increment(ref _lastId);
                        _events.Enqueue(new BarcodeScanEvent(id, code, DateTime.Now.ToString("HH:mm:ss")));
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                if (ct.IsCancellationRequested)
                {
                    // stopping
                }
                else
                {
                    lock (_sync)
                    {
                        _lastError = "扫码枪连接超时";
                    }
                    LogIo("RX ERROR: 扫码枪连接超时");
                    try
                    {
                        await Task.Delay(1000, ct);
                    }
                    catch
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                lock (_sync)
                {
                    _lastError = $"扫码枪连接异常: {ex.Message}";
                }
                LogIo($"RX ERROR: {ex.Message}");

                try
                {
                    await Task.Delay(1000, ct);
                }
                catch { }
            }
            finally
            {
                lock (_sync)
                {
                    _connected = false;
                }
            }
        }
    }

    private void TrimQueueIfNeeded()
    {
        if (_events.Count <= 2000) return;
        while (_events.Count > 1500 && _events.TryDequeue(out _))
        {
        }
    }

    private static string NormalizeBarcode(string raw)
    {
        var cleaned = new string(raw.Where(c => !char.IsControl(c)).ToArray());
        return cleaned.Trim();
    }

    private static bool IsScannerSystemMessage(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        var t = text.Trim();
        if (t.StartsWith("Welcome to Socket Channel", StringComparison.OrdinalIgnoreCase)) return true;
        if (t.StartsWith("Connected", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static bool IsScannerBusyMessage(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim();
        if (t.Contains("already in use", StringComparison.OrdinalIgnoreCase)) return true;
        if (t.Contains("no slot is available", StringComparison.OrdinalIgnoreCase)) return true;
        if (t.Contains("unable to complete connect", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static async Task<byte[]> ReadExactAsync(Stream stream, int length, CancellationToken ct)
    {
        var buffer = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, length - offset), ct);
            if (read == 0)
            {
                throw new IOException($"扫码枪连接已断开（期望{length}字节，实际{offset}字节）");
            }
            offset += read;
        }
        return buffer;
    }

    private static async Task<byte[]> ReadWelcomeHandshakeAsync(Stream stream, CancellationToken ct)
    {
        var buffer = new byte[WelcomeBytesLength];
        var offset = 0;

        using var firstWaitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        firstWaitCts.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            var firstRead = await stream.ReadAsync(buffer.AsMemory(0, WelcomeBytesLength), firstWaitCts.Token);
            if (firstRead <= 0) return Array.Empty<byte>();
            offset += firstRead;
        }
        catch (OperationCanceledException)
        {
            return Array.Empty<byte>();
        }

        while (offset < WelcomeBytesLength)
        {
            if (stream is not NetworkStream ns || !ns.DataAvailable)
            {
                await Task.Delay(120, ct);
                if (stream is not NetworkStream ns2 || !ns2.DataAvailable) break;
            }

            var read = await stream.ReadAsync(buffer.AsMemory(offset, WelcomeBytesLength - offset), ct);
            if (read <= 0) break;
            offset += read;
        }

        return buffer.AsSpan(0, offset).ToArray();
    }

    private void LogIo(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] [扫码枪通讯] {message}";
        _ioLogs.Enqueue(line);
        while (_ioLogs.Count > 300 && _ioLogs.TryDequeue(out _))
        {
        }
        Console.WriteLine(line);
    }
}

