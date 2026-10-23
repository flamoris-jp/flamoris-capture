using System.Text.Json;
using Flamoris.Capture.Core;
using Flamoris.Capture.Mcp;
using Flamoris.Logging;
using Flamoris.Mcp.Core;
using Xunit;

namespace Flamoris.Capture.Tests;

public sealed class EmbeddedBoundaryTests
{
    // The editor owns its document identity/guard independently from Capture.
    private sealed class EditorHost : IMcpHost
    {
        public HostSnapshot Snapshot => new("test.editor", "1.0", "editor-runtime", "editor-document", 7);
        public event Action? Invalidating { add { } remove { } }
        public Task<T> InvokeAsync<T>(Func<T> action, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(action()); }
    }

    [Fact]
    public void EmbeddedHubFragmentMatchesEditorWireSurface()
    {
        using var session = new CaptureSession(new CaptureTests.Backend());
        using var boundary = new McpBoundary(new EditorHost(), new CaptureMcpHost(session).ScreenshotTools(), new(), new McpDiagnostics(FlamorisLogger.Create()));
        var actual = boundary.DescribeCatalog().EnumerateArray().ToDictionary(t => t.GetProperty("name").GetString()!);
        using var fragment = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "hub-embedded-capture.tools.json")));
        Assert.Equal(2, fragment.RootElement.GetArrayLength());
        foreach (var tool in fragment.RootElement.EnumerateArray())
        {
            var producer = actual[tool.GetProperty("name").GetString()!];
            Assert.True(JsonElement.DeepEquals(producer.GetProperty("input_schema"), tool.GetProperty("input_schema")));
            Assert.Equal(producer.GetProperty("description").GetString(), tool.GetProperty("description").GetString());
        }
    }

    [Theory]
    [InlineData(McpPermission.ReadOnly)]
    [InlineData(McpPermission.Edit)]
    public async Task EditorPermissionDoesNotReplaceScreenConsent(McpPermission permission)
    {
        var backend = new CaptureTests.Backend();
        using var session = new CaptureSession(backend);
        var tools = new CaptureMcpHost(session).ScreenshotTools().ToArray();
        Assert.Equal(new[] { "capture.session.get", "capture.screenshot.get" }, tools.Select(t => t.Name));
        using var boundary = new McpBoundary(new EditorHost(), tools, new(), new McpDiagnostics(FlamorisLogger.Create()));
        using var grant = await boundary.EnableAsync(permission);
        var empty = JsonSerializer.SerializeToElement(new { });
        var denied = await boundary.InvokeAsync(grant, "capture.screenshot.get", empty, null);
        Assert.True(denied.IsError); Assert.Null(denied.Image); Assert.Equal(0, backend.Calls);
        session.SelectAndAllow(new CaptureTests.Target()); session.SetSharing(true);
        var screenshot = await boundary.InvokeAsync(grant, "capture.screenshot.get", empty, null);
        Assert.False(screenshot.IsError); Assert.NotNull(screenshot.Image);
        session.SetSharing(false);
        // Even before a UI-dispatched editor grant invalidation, Capture refuses.
        var revoked = await boundary.InvokeAsync(grant, "capture.screenshot.get", empty, null);
        Assert.True(revoked.IsError); Assert.Null(revoked.Image); Assert.Equal(1, backend.Calls);
    }

    [Fact]
    public async Task EditorBoundaryCannotDiscloseInFlightShotAfterRevocation()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var session = new CaptureSession(new CaptureTests.Backend { Wait = _ => { entered.SetResult(); return release.Task; } });
        session.SelectAndAllow(new CaptureTests.Target()); session.SetSharing(true);
        using var boundary = new McpBoundary(new EditorHost(), new CaptureMcpHost(session).ScreenshotTools(), new(), new McpDiagnostics(FlamorisLogger.Create()));
        using var grant = await boundary.EnableAsync(McpPermission.ReadOnly);
        var shot = boundary.InvokeAsync(grant, "capture.screenshot.get", JsonSerializer.SerializeToElement(new { }), null);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        session.Stop(); release.SetResult();
        var result = await shot;
        Assert.True(result.IsError); Assert.Null(result.Image);
    }
}
