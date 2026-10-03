using System.Diagnostics;
using System.Text;

namespace MssqlMcp.RepoTool;

internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

internal static class ProcessRunner
{
    internal static async Task<ProcessResult> RunAsync(
        string executable,
        IEnumerable<string> arguments,
        string workingDirectory,
        int timeoutSeconds = 660,
        IReadOnlyDictionary<string, string?>? environment = null,
        string? input = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutSeconds);
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = input is not null,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        if (environment is not null)
        {
            foreach (var (name, value) in environment)
            {
                if (value is null)
                {
                    startInfo.Environment.Remove(name);
                }
                else
                {
                    startInfo.Environment[name] = value;
                }
            }
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("Unable to start the requested process.");
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        Task<string> output = CaptureAsync(process.StandardOutput, timeout.Token);
        Task<string> error = CaptureAsync(process.StandardError, timeout.Token);
        try
        {
            if (input is not null)
            {
                await process.StandardInput.WriteAsync(input.AsMemory(), timeout.Token).ConfigureAwait(false);
                process.StandardInput.Close();
            }
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return new ProcessResult(process.ExitCode,
                await output.ConfigureAwait(false), await error.ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // The process can exit between cancellation and termination.
            }
            await process.WaitForExitAsync().ConfigureAwait(false);
            return new ProcessResult(124,
                await output.ConfigureAwait(false), await error.ConfigureAwait(false));
        }
    }

    private static async Task<string> CaptureAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var captured = new StringBuilder();
        char[] buffer = new char[4096];
        try
        {
            int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) != 0)
            {
                captured.Append(buffer, 0, count);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Preserve the partial logs needed to diagnose timed-out fuzz campaigns.
        }
        return captured.ToString();
    }

    internal static void RequireSuccess(ProcessResult result, string description)
    {
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"{description} failed (exit {result.ExitCode}).");
        }
    }
}
