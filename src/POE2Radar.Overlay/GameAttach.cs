using System.ComponentModel;
using POE2Radar.Core;
using POE2Radar.Overlay.Diagnostics;

namespace POE2Radar.Overlay;

internal enum AttachStatus
{
    PoENotRunning,
    AccessDenied,
    NotInZone,
    Ready,
    Error,
}

/// <summary>Result of probing or attaching to a live PoE2 client.</summary>
internal sealed class AttachResult : IDisposable
{
    private bool _taken;

    public AttachStatus Status { get; init; }
    public string StatusTitle { get; init; } = "";
    public string StatusDetail { get; init; } = "";
    public ProcessHandle? Process { get; init; }
    public MemoryReader? Reader { get; init; }
    public nint GameStateSlot { get; init; }

    public bool CanStart => Status == AttachStatus.Ready && Process is not null && Reader is not null && GameStateSlot != 0;

    public void Dispose()
    {
        if (_taken) return;
        Process?.Dispose();
    }

    public static AttachResult Probe()
    {
        ProcessHandle? process = null;
        try
        {
            process = ProcessHandle.AttachToPoE();
            if (process is null)
            {
                return new AttachResult
                {
                    Status = AttachStatus.PoENotRunning,
                    StatusTitle = "PoE2 not running (no matching process found).",
                    StatusDetail = "Start Path of Exile 2 — POE2Radar will detect it and start automatically once you are in a zone.",
                };
            }

            var reader = new MemoryReader(process);
            var bootstrap = Bootstrap.TryResolveGameStateSlot(process, reader);
            if (bootstrap.Slot == 0)
            {
                process.Dispose();
                return new AttachResult
                {
                    Status = AttachStatus.NotInZone,
                    StatusTitle = $"Path of Exile 2 is running (PID {process.ProcessId}).",
                    StatusDetail = "Waiting for you to load into a zone (not login or character select). The overlay will start automatically.",
                };
            }

            return new AttachResult
            {
                Status = AttachStatus.Ready,
                StatusTitle = $"Attached to {process.ProcessName} (PID {process.ProcessId})",
                StatusDetail = "In-game chain OK — starting overlay…",
                Process = process,
                Reader = reader,
                GameStateSlot = bootstrap.Slot,
            };
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
        {
            process?.Dispose();
            return new AttachResult
            {
                Status = AttachStatus.AccessDenied,
                StatusTitle = "Could not attach to in-game state.",
                StatusDetail = "Make sure you are loaded into a zone (not login / character select), then try again as Administrator.",
            };
        }
        catch (Exception ex)
        {
            process?.Dispose();
            CrashLog.Write("Attach probe failed", ex);
            return new AttachResult
            {
                Status = AttachStatus.Error,
                StatusTitle = "Attach failed",
                StatusDetail = ex.Message,
            };
        }
    }

    /// <summary>Transfer ownership to RadarApp. Disposing this wrapper afterward is a no-op.</summary>
    public (ProcessHandle Process, MemoryReader Reader, nint Slot) Take()
    {
        if (!CanStart || Process is null || Reader is null)
            throw new InvalidOperationException("Attach is not ready.");

        _taken = true;
        return (Process, Reader, GameStateSlot);
    }
}
