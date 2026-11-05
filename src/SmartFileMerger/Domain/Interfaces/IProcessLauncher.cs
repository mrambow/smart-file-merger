namespace SmartFileMerger.Domain.Interfaces;

/// <summary>
/// Abstraktion für das Starten externer Prozesse zur Entkopplung von System.Diagnostics.Process und Vermeidung von Test-Hacks.
/// </summary>
public interface IProcessLauncher
{
    bool Start(string fileName, IEnumerable<string>? arguments = null);
}
