using System.Text.Json;
using System.Text.Json.Serialization;
using Flamoris.Capture.Core;
using Flamoris.Capture.Imaging;

var json = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{ UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, PropertyNameCaseInsensitive = false, AllowDuplicateProperties = false };
using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30));
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
try
{
    if (args.Length != 0) throw new CaptureFault("invalid_request");
    using var stdin = Console.OpenStandardInput();
    byte[] requestBytes = new byte[16385];
    int used = 0, count;
    while (used < requestBytes.Length && (count = await stdin.ReadAsync(requestBytes.AsMemory(used), stop.Token)) > 0) used += count;
    if (used > 16384) throw new CaptureFault("invalid_request");
    var request = JsonSerializer.Deserialize<CliRequest>(requestBytes.AsSpan(0, used), json) ?? throw new CaptureFault("invalid_request");
    await using var input = new FileStream(request.InputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
    var image = ImageProcessor.Process(await ImageProcessor.ReadBoundedAsync(input, stop.Token), request.Options ?? new() { Format = "png" }, stop.Token);
    stop.Token.ThrowIfCancellationRequested();
    // CreateNew: caller chooses a fresh output; never overwrite source or another result.
    await using (var output = new FileStream(request.OutputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        await output.WriteAsync(image.Bytes, stop.Token);
    Console.WriteLine(JsonSerializer.Serialize(new { metadata = image.Metadata }, json));
    return 0;
}
catch (Exception e)
{
    string code = e switch { CaptureFault f => f.Code, OperationCanceledException => "cancelled",
        JsonException or ArgumentException => "invalid_request", IOException or UnauthorizedAccessException => "io_error", _ => "internal_error" };
    Console.WriteLine(JsonSerializer.Serialize(new { error = new { code } }, json));
    return 1;
}
internal sealed record CliRequest([property: JsonRequired] string InputPath, [property: JsonRequired] string OutputPath, ScreenshotRequest? Options);
