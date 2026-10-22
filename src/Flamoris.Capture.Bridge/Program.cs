using Flamoris.Mcp.Core;

using var output = Console.OpenStandardOutput();
Console.SetOut(Console.Error);
var credential = Environment.GetEnvironmentVariable(StdioBridge.CredentialEnvironmentVariable);
Environment.SetEnvironmentVariable(StdioBridge.CredentialEnvironmentVariable, null);
if (args.Length != 2 || args[0] != "--pipe" || !McpOptions.ValidPipeName(args[1])) return 2;
using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
try
{
    await StdioBridge.RunAsync(args[1], credential, Console.OpenStandardInput(), output, cancellationToken: stop.Token);
    return 0;
}
catch { Console.Error.WriteLine("MCP connection ended."); return 1; }
