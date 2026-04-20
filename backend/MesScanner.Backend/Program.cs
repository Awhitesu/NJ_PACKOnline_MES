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

var app = builder.Build();

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

public record LogSaveRequest(string FileName, string Content, string Path);
public record OrderStatusSelectionState(string SelectedCode, string OrderStatus, string UpdatedAt);
