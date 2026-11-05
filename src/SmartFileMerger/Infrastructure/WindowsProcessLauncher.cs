namespace SmartFileMerger.Infrastructure;

using System.Diagnostics;
using SmartFileMerger.Domain.Interfaces;

/// <summary>
/// Startet Windows-Prozesse via <see cref="Process.Start(ProcessStartInfo)"/>.
/// </summary>
public sealed class WindowsProcessLauncher : IProcessLauncher
{
    public bool Start(string fileName, IEnumerable<string>? arguments = null)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = true
        };

        if (arguments is not null)
        {
            foreach (var arg in arguments)
            {
                psi.ArgumentList.Add(arg);
            }
        }

        try
        {
            return Process.Start(psi) is not null;
        }
        catch
        {
            return false;
        }
    }
}
