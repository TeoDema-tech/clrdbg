// Options for pipeTransport debugging over SSH, Docker, or WSL.
// Standard VS Code C# (vsdbg) schema compatibility.

namespace DotNet.Debugging.Adapter;

/// <summary>
/// Configuration for launching or attaching to a process through an external transport pipe,
/// such as SSH or Docker exec.
/// </summary>
public class PipeTransportOptions {
    public string? PipeProgram { get; set; }
    public List<string> PipeArgs { get; set; } = new();
    public string? PipeCwd { get; set; }
    public string? DebuggerPath { get; set; }
    public bool QuoteArgs { get; set; } = true;
    public Dictionary<string, string> PipeEnv { get; set; } = new();
}
