using System.Text.Json;
using T16Bridge;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static void Reject(Action action)
{
    try { action(); }
    catch (ArgumentException) { return; }
    throw new Exception("Invalid name was accepted");
}

string folder = Path.Combine(Path.GetTempPath(), "T16Bridge-checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
try
{
    string legacy = Path.Combine(folder, "curves.json");
    string path = Path.Combine(folder, "profiles.json");
    var original = AppCurveConfig.CreateDefault();
    original.Left.Y.Points[2].Y = 0.2f;
    File.WriteAllText(legacy, JsonSerializer.Serialize(original));
    string legacyContents = File.ReadAllText(legacy);
    var library = ProfileLibrary.Load(path, legacy);
    var first = library.Active;
    Check(first.Curves.Left.Y.Points[2].Y == 0.2f, "Legacy curves lost");
    first.Curves.Left.FreezeButtons["Y"] = 1;
    var copy = library.Add("Helicopter", copyActive: true);
    Check(copy.Curves.Left.FreezeButtons["Y"] == 1, "Freeze binding was not copied");
    copy.Curves.Left.Y.Points[2].Y = 0.8f;
    copy.Curves.Left.FreezeButtons["Y"] = 2;
    Check(first.Curves.Left.Y.Points[2].Y == 0.2f && first.Curves.Left.FreezeButtons["Y"] == 1, "Copy shares mutable data");
    library.RenameActive(" Collective ");
    copy.Curves.Left.FreezeAllButton = 15;
    Check(copy.Name == "Collective", "Rename failed");
    Reject(() => library.Add("default", true));
    Reject(() => library.RenameActive(" "));
    library.Save(path);
    var loaded = ProfileLibrary.Load(path, legacy);
    Check(loaded.Profiles.Count == 2 && loaded.Active.Name == "Collective", "Profile selection did not survive reload");
    Check(loaded.Active.Curves.Left.FreezeButtons["Y"] == 2, "Freeze binding did not survive reload");
    Check(loaded.Active.Curves.Left.FreezeAllButton == 15, "All-axes binding did not survive reload");
    Check(File.ReadAllText(legacy) == legacyContents, "Legacy backup changed");
    var fresh = loaded.Add("Linear", false);
    Check(fresh.Curves.Left.Y.Points.All(p => p.X == p.Y) && fresh.Curves.Left.FreezeButtons.Count == 0, "New profile is not default");
    File.WriteAllText(path, "{broken");
    try { ProfileLibrary.Load(path, legacy); throw new Exception("Corrupt library was silently replaced"); }
    catch (JsonException) { }
    Check(File.ReadAllText(path) == "{broken", "Corrupt file was overwritten");

    var freeze = new AxisFreezeState();
    float Report(float y, uint buttons, int binding = 1)
    {
        float result = freeze.Apply("Y", y, binding, buttons);
        freeze.EndReport(buttons);
        return result;
    }
    Check(Report(0.1f, 0) == 0.1f, "Unfrozen axis changed");
    Check(Report(0.2f, 1) == 0.2f && freeze.IsFrozen("Y"), "Button press did not freeze");
    Check(Report(0.9f, 1) == 0.2f, "Held button retriggered");
    Check(Report(0.8f, 0) == 0.2f, "Release cleared the freeze");
    Check(Report(0.7f, 1) == 0.7f && !freeze.IsFrozen("Y"), "Second press did not unfreeze");
    Report(0.3f, 0); Report(0.3f, 1);
    Check(Report(0.9f, 0, binding: 0) == 0.9f && !freeze.IsFrozen("Y"), "Disabled binding remained frozen");
    Report(0.4f, 1);
    freeze.Clear();
    Check(Report(0.8f, 1) == 0.8f && !freeze.IsFrozen("Y"), "Profile reset retriggered a held button");
    var independent = new AxisFreezeState();
    independent.EndReport(0);
    independent.Apply("X", 0.1f, 1, 1);
    independent.Apply("Y", 0.2f, 2, 1);
    independent.EndReport(1);
    Check(independent.IsFrozen("X") && !independent.IsFrozen("Y"), "Axes are not independent");
    var startup = new AxisFreezeState();
    startup.Apply("Y", 0.1f, 1, 1); startup.EndReport(1);
    Check(!startup.IsFrozen("Y"), "Already-held startup button triggered a freeze");
    foreach (bool leftHanded in new[] { false, true })
    {
        var positions = T16000Layout.Buttons(leftHanded);
        foreach (bool rightBank in new[] { false, true })
        {
            var bank = positions.Where(p => p.Number > 4 && (rightBank ? p.X > 200 : p.X < 200))
                .OrderBy(p => p.Y).ToArray();
            int[] expectedRows = rightBank
                ? leftHanded ? [7, 6, 5, 8, 9, 10] : [13, 12, 11, 14, 15, 16]
                : leftHanded ? [11, 12, 13, 16, 15, 14] : [5, 6, 7, 10, 9, 8];
            var visibleRows = bank.Take(3).OrderBy(p => p.X).Concat(bank.Skip(3).OrderBy(p => p.X)).Select(p => p.Number);
            Check(visibleRows.SequenceEqual(expectedRows), "Visible button row order is wrong");
        }
        Check(positions.Select(p => p.Number).Order().SequenceEqual(Enumerable.Range(1, 16)), "Layout must contain each button once");
        Check(positions.Select(p => (p.X, p.Y)).Distinct().Count() == 16, "Overlapping diagram positions");
        Check(positions.Single(p => p.Number == 1).Y < positions.Single(p => p.Number == 2).Y, "Trigger orientation is wrong");
        int outer = leftHanded ? 5 : 11;
        Check(positions.Single(p => p.Number == outer).X > positions.Single(p => p.Number == outer + 1).X
            && positions.Single(p => p.Number == outer + 1).X > positions.Single(p => p.Number == outer + 2).X,
            "Right bank must follow the manual from outer edge toward grip");
        foreach (var reference in T16000Layout.Buttons(false))
        {
            int expected = !leftHanded || reference.Number <= 4 ? reference.Number
                : reference.Number <= 10 ? reference.Number + 6 : reference.Number - 6;
            Check(positions.Single(p => p.X == reference.X && p.Y == reference.Y).Number == expected,
                "Manual handedness numbering mismatch");
        }
    }
    var capture = new PhysicalButtonCapture();
    var allFreeze = new AxisFreezeState();
    var values = new Dictionary<string, float> { ["X"] = 0.1f, ["Y"] = 0.2f, ["Rz"] = 0.3f, ["Slider"] = 0.4f };
    allFreeze.EndReport(0);
    Check(allFreeze.ToggleAll(16, 1u << 15, values), "All-axes press did not toggle");
    foreach (var (axis, value) in values)
        Check(allFreeze.Apply(axis, 0.9f, 0, 1u << 15, true) == value && allFreeze.IsFrozen(axis), "Global hold lost an unbound axis");
    allFreeze.EndReport(1u << 15);
    Check(!allFreeze.ToggleAll(16, 1u << 15, values), "Held global button retriggered");
    allFreeze.EndReport(0);
    Check(allFreeze.ToggleAll(16, 1u << 15, values) && values.Keys.All(axis => !allFreeze.IsFrozen(axis)), "Global button did not release all axes");
    var bindings = DeviceCurveConfig.CreateDefault();
    bindings.AssignFreezeButton("X", 3, false);
    bindings.AssignFreezeButton("Y", 4, false);
    bindings.AssignFreezeButton("X", 3, true);
    Check(bindings.FreezeAllButton == 3 && bindings.FreezeButtons["X"] == 0 && bindings.FreezeButtons["Y"] == 4, "Global binding collision not resolved");
    bindings.AssignFreezeButton("Y", 3, false);
    Check(bindings.FreezeAllButton == 0 && bindings.FreezeButtons["Y"] == 3, "Axis binding collision not resolved");
    capture.Arm(1);
    Check(capture.Observe(1) == 0, "Held button captured immediately");
    Check(capture.Observe(0) == 0 && capture.Observe(1) == 1, "Released and repressed trigger was not captured");
    Check(!capture.IsArmed && capture.Observe(2) == 0, "Capture did not stop after selection");
    for (int button = 1; button <= 16; button++)
    {
        capture.Arm(0);
        Check(capture.Observe(1u << (button - 1)) == button, "HID bit to button numbering mismatch");
    }
    capture.Arm(0);
    Check(capture.Observe(3) == 0 && capture.IsArmed, "Simultaneous press should be ambiguous");
    capture.Cancel();
    Check(capture.Observe(4) == 0, "Cancelled detection still assigned a button");
    Console.WriteLine("PASS: profiles, freeze toggles, physical button capture, and LEFT/RIGHT diagram numbering");
}
finally
{
    // The path is a dedicated directory created above, never user settings.
    Directory.Delete(folder, recursive: true);
}
