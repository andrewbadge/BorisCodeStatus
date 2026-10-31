using System.Text.Json;
using System.Text.Json.Serialization;

namespace BorisCodeStatus.Core.State;

/// <summary>
/// Reads and writes <c>settings.json</c>. Only the tray process touches it — the hook process
/// never reads a preference — so a lock is enough to serialise the tray's own threads (the UI
/// thread and the web host's), and the file is replaced atomically so a reader can never see half
/// of one.
///
/// Read on every call rather than cached, so a change applies at the moment of use and a hand edit
/// is picked up without a restart. The file is a few hundred bytes.
///
/// Like every preference, it never throws: an unreadable or unparseable file yields the defaults,
/// and a failed write is reported through the return value, because by then the change has already
/// taken effect in the running process and losing it is the lesser problem.
/// </summary>
public static class SettingsStore
{
    private static readonly object Gate = new();

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter<Pet>() },
    };

    /// <summary>The current settings. <paramref name="path"/> is for tests; the default is <see cref="VitalsPaths.AppSettingsFile"/>.</summary>
    public static AppSettings Load(string? path = null)
    {
        lock (Gate)
        {
            return Read(path ?? VitalsPaths.AppSettingsFile, out _);
        }
    }

    /// <summary>Applies <paramref name="change"/> and saves. Returns whether it was persisted.</summary>
    public static bool TryUpdate(Func<AppSettings, AppSettings> change, string? path = null)
    {
        path ??= VitalsPaths.AppSettingsFile;
        lock (Gate)
        {
            var updated = change(Read(path, out var corrupt));
            try
            {
                if (corrupt)
                {
                    // About to replace something the user may have been editing by hand: keep it.
                    File.Copy(path, path + ".bak", overwrite: true);
                }

                return TryWrite(path, updated);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    private static AppSettings Read(string path, out bool corrupt)
    {
        corrupt = false;
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return LegacySettings.Migrate(path, TryWrite) ?? new AppSettings();
        }
        catch (JsonException)
        {
            corrupt = true;
            return new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    private static bool TryWrite(string path, AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Options));
            File.Move(temporary, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
