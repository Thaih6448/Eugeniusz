using System.Reflection;
using System.Runtime.InteropServices;
using Eugeniusz;

namespace Eugeniusz.Samples;

/// <summary>Explicit native library resolution for the .NET 8 samples.</summary>
public static class SampleRuntime
{
    private static readonly object Gate = new();
    private static bool configured;
    private static IntPtr dllDirectoryCookie;
    public static string NativeDirectory { get; private set; } = "";

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr AddDllDirectory(string directory);

    public static IEnumerable<string> Ancestors()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
                if (seen.Add(directory.FullName)) yield return directory.FullName;
    }

    public static string FindModel()
    {
        string? requested = Environment.GetEnvironmentVariable("EUGENIUSZ_MODEL");
        if (!string.IsNullOrWhiteSpace(requested)) return requested;
        foreach (string root in Ancestors())
        {
            string profile = Path.Combine(root, "profile.json");
            if (File.Exists(profile))
            {
                try
                {
                    using var data = System.Text.Json.JsonDocument.Parse(File.ReadAllText(profile));
                    return Path.GetFullPath(Path.Combine(root, data.RootElement.GetProperty("model").GetString()!));
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or KeyNotFoundException or ArgumentException or InvalidOperationException)
                {
                    // Model discovery is optional. A broken profile must not prevent opening the picker.
                }
            }
            string? selected = FindProfileModel(root, "light");
            if (selected != null) return selected;
            foreach (string relative in new[] { "models/downloads/Qwen3-0.6B-Q4_0.gguf", "models/Qwen3-0.6B-Q4_0.gguf" })
            {
                string path = Path.Combine(root, relative);
                if (File.Exists(path)) return path;
            }
        }
        return "";
    }

    public static string? FindProfileModel(string root, string profile)
    {
        string manifest = Path.Combine(root, "models", "profiles.json");
        if (!File.Exists(manifest)) return null;
        try
        {
            using var data = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifest));
            string filename = data.RootElement.GetProperty(profile).GetProperty("file").GetString()!;
            foreach (string directory in new[] { "models/downloads", "models" })
            {
                string path = Path.Combine(root, directory, filename);
                if (File.Exists(path)) return path;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or KeyNotFoundException or ArgumentException or InvalidOperationException) { }
        return null;
    }

    public static void Configure()
    {
        lock (Gate)
        {
            if (configured) return;
            string libraryName = OperatingSystem.IsWindows() ? "eugeniusz.dll" : OperatingSystem.IsMacOS() ? "libeugeniusz.dylib" : "libeugeniusz.so";
            var candidates = new List<string>();
            string? requested = Environment.GetEnvironmentVariable("EUGENIUSZ_LIBRARY_DIR");
            if (!string.IsNullOrWhiteSpace(requested))
                candidates.AddRange(new[] { requested, Path.Combine(requested, "bin"), Path.Combine(requested, "lib") });
            else
            {
                candidates.Add(AppContext.BaseDirectory);
                foreach (string root in Ancestors())
                    foreach (string relative in new[] { "bin", "lib", "build/install-vulkan/bin", "build/install-cpu/bin", "build/install/bin", "build/install-cpu/lib", "build/install/lib" })
                        candidates.Add(Path.Combine(root, relative));
            }
            NativeDirectory = candidates.Select(Path.GetFullPath).FirstOrDefault(p => File.Exists(Path.Combine(p, libraryName))) ?? "";
            if (NativeDirectory.Length == 0)
                throw new DllNotFoundException("The Eugeniusz native runtime was not found. Build/install the native library first, then rebuild this sample, or set EUGENIUSZ_LIBRARY_DIR to an installed runtime. See examples/winforms/README.md. No PATH changes are required.");
            if (OperatingSystem.IsWindows())
            {
                if (!Environment.Is64BitProcess) throw new PlatformNotSupportedException("The Windows examples require an x64 process.");
                // Retain this directory for the process lifetime: ggml may load backend plugins later.
                dllDirectoryCookie = AddDllDirectory(NativeDirectory);
                if (dllDirectoryCookie == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
            NativeLibrary.SetDllImportResolver(typeof(Engine).Assembly, Resolve);
            configured = true;
        }
    }

    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (name != "eugeniusz" && name != "eugeniusz_llama") return IntPtr.Zero;
        string filename = OperatingSystem.IsWindows() ? name + ".dll" : "lib" + name + (OperatingSystem.IsMacOS() ? ".dylib" : ".so");
        string path = Path.Combine(NativeDirectory, filename);
        try
        {
            return NativeLibrary.Load(path, assembly, DllImportSearchPath.UseDllDirectoryForDependencies | DllImportSearchPath.SafeDirectories);
        }
        catch (DllNotFoundException error)
        {
            throw new DllNotFoundException($"Cannot load {path}. Keep llama/ggml dependencies beside it and install the x64 Microsoft Visual C++ runtime on Windows. {error.Message}", error);
        }
    }
}
