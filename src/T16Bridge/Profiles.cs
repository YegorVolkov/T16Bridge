using System.Text.Json;
using System.Text.Json.Serialization;

namespace T16Bridge;

public sealed class SensitivityProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Default";
    public AppCurveConfig Curves { get; set; } = AppCurveConfig.CreateDefault();
    public override string ToString() => Name;
}

public sealed class ProfileLibrary
{
    public List<SensitivityProfile> Profiles { get; set; } = [new()];
    public string ActiveProfileId { get; set; } = "";

    [JsonIgnore]
    public SensitivityProfile Active => Profiles.First(p => p.Id == ActiveProfileId);

    public static ProfileLibrary Load(string path, string legacyPath)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        ProfileLibrary library;
        if (File.Exists(path))
            library = JsonSerializer.Deserialize<ProfileLibrary>(File.ReadAllText(path), options)
                ?? throw new InvalidDataException("Profile library is empty.");
        else
        {
            library = new();
            if (File.Exists(legacyPath))
                library.Profiles[0].Curves = JsonSerializer.Deserialize<AppCurveConfig>(File.ReadAllText(legacyPath), options)
                    ?? throw new InvalidDataException("Legacy curves are empty.");
        }
        library.Normalize();
        return library;
    }

    public void Save(string path)
    {
        Normalize();
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, overwrite: true);
    }

    public void Normalize()
    {
        Profiles ??= [];
        Profiles = Profiles.Where(p => p is not null).ToList();
        if (Profiles.Count == 0) Profiles.Add(new());
        var ids = new HashSet<string>();
        foreach (var profile in Profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Id) || !ids.Add(profile.Id))
            {
                profile.Id = Guid.NewGuid().ToString("N");
                ids.Add(profile.Id);
            }
            if (string.IsNullOrWhiteSpace(profile.Name)) profile.Name = "Profile";
            profile.Curves ??= AppCurveConfig.CreateDefault();
            profile.Curves.Normalize();
        }
        if (!Profiles.Any(p => p.Id == ActiveProfileId)) ActiveProfileId = Profiles[0].Id;
    }

    public SensitivityProfile Add(string name, bool copyActive)
    {
        name = name.Trim();
        if (name.Length == 0 || Profiles.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Choose a non-empty, unique profile name.");
        var curves = copyActive
            ? JsonSerializer.Deserialize<AppCurveConfig>(JsonSerializer.Serialize(Active.Curves))!
            : AppCurveConfig.CreateDefault();
        var profile = new SensitivityProfile { Name = name, Curves = curves };
        Profiles.Add(profile);
        ActiveProfileId = profile.Id;
        return profile;
    }

    public void RenameActive(string name)
    {
        name = name.Trim();
        if (name.Length == 0 || Profiles.Any(p => p != Active && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Choose a non-empty, unique profile name.");
        Active.Name = name;
    }
}

// One instance per physical stick; callers serialize access with the configuration lock.
// Runtime holds are deliberately excluded from saved profiles.
public sealed class AxisFreezeState
{
    private uint _previousButtons;
    private bool _initialized;
    private readonly Dictionary<string, float> _held = new();
    public bool IsFrozen(string axis) => _held.ContainsKey(axis);
    public void Clear() => _held.Clear();
    public void Reset() { Clear(); _previousButtons = 0; _initialized = false; }

    public bool ToggleAll(int button, uint buttons, IReadOnlyDictionary<string, float> values)
    {
        uint mask = button is >= 1 and <= 16 ? 1u << (button - 1) : 0;
        if (!_initialized || mask == 0 || (buttons & mask) == 0 || (_previousButtons & mask) != 0) return false;
        if (values.Keys.All(IsFrozen)) Clear();
        else foreach (var (axis, value) in values) _held.TryAdd(axis, value);
        return true;
    }

    public float Apply(string axis, float value, int button, uint buttons, bool keepUnbound = false)
    {
        uint mask = button is >= 1 and <= 16 ? 1u << (button - 1) : 0;
        if (mask == 0 && !keepUnbound) { _held.Remove(axis); return value; }
        if (_initialized && (buttons & mask) != 0 && (_previousButtons & mask) == 0)
        {
            if (!_held.Remove(axis)) _held[axis] = value;
        }
        return _held.TryGetValue(axis, out var frozen) ? frozen : value;
    }

    public void EndReport(uint buttons) { _previousButtons = buttons; _initialized = true; }
}
