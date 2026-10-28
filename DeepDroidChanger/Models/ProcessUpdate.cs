namespace DeepDroidChanger.Models;

public enum ProcessUpdateKind { Started, StandardOutput, StandardError, Exited }

public sealed record ProcessUpdate(ProcessUpdateKind Kind, int ProcessId, string Text = "", int? ExitCode = null);
