using System.Runtime.Versioning;

namespace Hdr2Sdr.Core;

/// <summary>
/// Resolves portable vs Program Files data paths cleanly.
/// In portable mode (writable application directory outside Program Files), writes config locally.
/// When installed in Program Files (read-only), stores user config in %LOCALAPPDATA%\hdr2sdr\
/// and migrates any existing v1.x %APPDATA%\hdr2sdr\config.ini (or copies default config.ini) on first run.
/// </summary>
[SupportedOSPlatform("windows")]
public static class AppPaths
{
    private static readonly object Sync = new();
    private static bool _initialized;
    private static string _baseDir = string.Empty;
    private static string _dataDir = string.Empty;
    private static bool _isPortable;

    public static string BaseDir
    {
        get { EnsureInitialized(); return _baseDir; }
    }

    public static string DataDir
    {
        get { EnsureInitialized(); return _dataDir; }
    }

    public static bool IsPortable
    {
        get { EnsureInitialized(); return _isPortable; }
    }

    public static string ConfigPath => Path.Combine(DataDir, "config.ini");
    public static string CliOutputLogPath => Path.Combine(DataDir, "hdr2sdr-last.txt");
    public static string HotkeyLogPath => Path.Combine(DataDir, "hdr-hk-log.txt");

    public static string ResolveAssetPath(string relativePath)
    {
        var candidate = Path.Combine(BaseDir, relativePath);
        if (File.Exists(candidate))
            return candidate;

        var parentCandidate = Path.GetFullPath(Path.Combine(BaseDir, "..", "..", relativePath));
        if (File.Exists(parentCandidate))
            return parentCandidate;

        return candidate;
    }

    private static void EnsureInitialized()
    {
        if (_initialized) return;
        lock (Sync)
        {
            if (_initialized) return;

            _baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            bool insideProgramFiles = IsInsideProgramFiles(_baseDir);

            if (!insideProgramFiles && IsDirectoryWritable(_baseDir))
            {
                _isPortable = true;
                _dataDir = _baseDir;
            }
            else
            {
                _isPortable = false;
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                _dataDir = Path.Combine(localAppData, BuildInfo.AppName);
                Directory.CreateDirectory(_dataDir);
            }

            SeedConfigIfMissing();
            _initialized = true;
        }
    }

    private static void SeedConfigIfMissing()
    {
        var targetConfig = Path.Combine(_dataDir, "config.ini");
        if (File.Exists(targetConfig))
            return;

        // 1. Migrate legacy v1.x %APPDATA%\hdr2sdr\config.ini if present
        try
        {
            var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var legacyConfig = Path.Combine(roaming, BuildInfo.AppName, "config.ini");
            if (File.Exists(legacyConfig))
            {
                Directory.CreateDirectory(_dataDir);
                File.Copy(legacyConfig, targetConfig, overwrite: false);
                return;
            }
        }
        catch
        {
            // Ignore migration errors and fall back to bundled default
        }

        // 2. Copy default config.ini bundled in installation folder if present
        try
        {
            var bundledConfig = Path.Combine(_baseDir, "config.ini");
            if (File.Exists(bundledConfig) && !string.Equals(bundledConfig, targetConfig, StringComparison.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(_dataDir);
                File.Copy(bundledConfig, targetConfig, overwrite: false);
            }
        }
        catch
        {
            // Will be created cleanly with defaults on first Save()
        }
    }

    private static bool IsInsideProgramFiles(string path)
    {
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        return (!string.IsNullOrEmpty(pf) && path.StartsWith(pf, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrEmpty(pf86) && path.StartsWith(pf86, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsDirectoryWritable(string dir)
    {
        try
        {
            var probe = Path.Combine(dir, $".write-probe-{Environment.ProcessId}-{Guid.NewGuid():N}.tmp");
            using (File.Create(probe)) { }
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
