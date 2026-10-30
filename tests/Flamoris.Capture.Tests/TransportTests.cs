using Flamoris.Capture.Core;
using Flamoris.Capture.Mcp;
using Flamoris.Logging;
using Flamoris.Mcp.Core;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Flamoris.Capture.Tests;

public sealed class BridgeFactAttribute : FactAttribute
{
    public BridgeFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FLAMORIS_CAPTURE_TEST_BRIDGE")))
            Skip = "Set FLAMORIS_CAPTURE_TEST_BRIDGE to run the actual Core bridge integration (required in Windows CI).";
    }
}
public sealed class TransportTests
{
    [BridgeFact]
    public async Task OfficialClientRoundTripThroughPublishedBridge()
    {
        string bridge = Environment.GetEnvironmentVariable("FLAMORIS_CAPTURE_TEST_BRIDGE") ?? throw new InvalidOperationException();
        using var s = new CaptureSession(new CaptureTests.Backend()); s.SelectAndAllow(new CaptureTests.Target()); s.SetSharing(true);
        var host = new CaptureMcpHost(s);
        using var boundary = new McpBoundary(host, host.Tools(), new() { ApplicationLifetime = true }, new McpDiagnostics(FlamorisLogger.Create()));
        using var grant = await boundary.EnableAsync(McpPermission.ReadOnly);
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var serving = new LocalMcpEndpoint(boundary).RunAsync(grant, ct.Token);
        try
        {
            var transport = new StdioClientTransport(new() { Command = bridge, Arguments = ["--pipe", boundary.Options.PipeName],
                EnvironmentVariables = new Dictionary<string,string?> { [StdioBridge.CredentialEnvironmentVariable] = grant.ExportCredential() } });
            await using var client = await McpClient.CreateAsync(transport, cancellationToken: ct.Token);
            var context = await client.CallToolAsync("mcp.context", cancellationToken: ct.Token);
            string sessionId = context.StructuredContent!.Value.GetProperty("documentToken").GetString()!;
            var reply = await client.CallToolAsync("capture.screenshot.get", new Dictionary<string,object?> {
                ["input"] = new { } }, cancellationToken: ct.Token);
            Assert.False(reply.IsError == true);
            var image = Assert.Single(reply.Content.OfType<ImageContentBlock>());
            Assert.Equal(CaptureTests.Backend.Png, image.DecodedData.ToArray());
            Assert.Equal(sessionId, reply.StructuredContent!.Value.GetProperty("sessionId").GetString());
            s.SetSharing(false); Assert.True(grant.IsActive);
            Assert.False((await client.CallToolAsync("capture.session.get", new Dictionary<string,object?> { ["input"] = new { } }, cancellationToken: ct.Token)).IsError == true);
        }
        finally { ct.Cancel(); await serving.WaitAsync(TimeSpan.FromSeconds(5)); }
    }
}
