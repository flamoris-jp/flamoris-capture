using System.Text.Json;
using Flamoris.Capture.Core;
using Flamoris.Capture.Mcp;
using Flamoris.Logging;
using Flamoris.Mcp.Core;
using Xunit;

namespace Flamoris.Capture.Tests;

public sealed class HubCatalogTests
{
    [Fact]
    public void PinnedHubCatalogMatchesActualCoreWireDescriptors()
    {
        using var session = new CaptureSession(new CaptureTests.Backend());
        var host = new CaptureMcpHost(session);
        using var core = new McpBoundary(host, host.Tools(), new(), new McpDiagnostics(FlamorisLogger.Create()));
        using var file = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "hub-capture.example.json")));
        var root = file.RootElement;
        Assert.Equal("flamoris.capture", root.GetProperty("product_id").GetString());
        Assert.Equal("CAPTURE_CONNECTOR_TOKEN", root.GetProperty("connector_token_env").GetString());
        var exported = core.DescribeCatalog().EnumerateArray().ToDictionary(x => x.GetProperty("name").GetString()!);
        Assert.Equal(3, exported.Count);
        var tools = root.GetProperty("tools").EnumerateArray().ToArray();
        Assert.Equal(exported.Count, tools.Length);
        foreach (var tool in tools)
        {
            var producer = exported[tool.GetProperty("name").GetString()!];
            Assert.True(JsonElement.DeepEquals(producer.GetProperty("input_schema"), tool.GetProperty("input_schema")));
            Assert.Equal(producer.GetProperty("description").GetString(), tool.GetProperty("description").GetString());
            Assert.True(tool.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean());
            Assert.False(tool.GetProperty("annotations").GetProperty("openWorldHint").GetBoolean());
        }
    }
}
