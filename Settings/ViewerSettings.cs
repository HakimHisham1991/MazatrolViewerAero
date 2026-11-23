using System.Text.Json;

namespace MazatrolViewerAero.Settings;

public sealed class ViewerSettings
{
    public string DataPath { get; set; } = "";
    public bool RegPass { get; set; }
    public bool InsertProgramInfo { get; set; } = true;
    public bool AddBinaryLine { get; set; }
    public bool ShowToolbar { get; set; } = true;
    public bool ShowStatusBar { get; set; } = true;
    public int MatrixMachineId { get; set; } = 3;
    public int Head { get; set; } = 1;
    public string SyntaxLanguage { get; set; } = "";
    public string LastFolder { get; set; } = "";
    public List<string> Recent { get; set; } = [];
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public double Width { get; set; } = 1100;
    public double Height { get; set; } = 760;
    public bool Maximized { get; set; }
    public Dictionary<string, FamilySetup> Families { get; set; } = [];

    /// <summary>
    /// Folder of MazatrolViewerAero.exe. A single-file publish reports that exe here, so settings stay
    /// beside the copy you unzipped. dotnet run reports the SDK host, and then the build output is used.
    /// </summary>
    public static string AppDirectory
    {
        get
        {
            var process = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(process)
                && Path.GetFileName(process).Equals("MazatrolViewerAero.exe", StringComparison.OrdinalIgnoreCase))
            {
                var directory = Path.GetDirectoryName(process);
                if (!string.IsNullOrEmpty(directory))
                {
                    return directory;
                }
            }

            return AppContext.BaseDirectory;
        }
    }

    public static string SettingsFile => Path.Combine(AppDirectory, "Data", "settings.json");

    private static string LegacySettingsFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MazatrolViewerAero", "settings.json");

    public static ViewerSettings Load()
    {
        var settings = Read(SettingsFile);
        var imported = false;
        if (settings == null)
        {
            settings = Read(LegacySettingsFile) ?? new ViewerSettings();
            settings.DataPath = "";
            imported = true;
        }

        settings.EnsureDefaults();
        if (imported)
        {
            settings.Save();
        }

        return settings;
    }

    private static ViewerSettings? Read(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            return JsonSerializer.Deserialize<ViewerSettings>(File.ReadAllText(path), new JsonSerializerOptions
            {
                NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
            });
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
        Directory.CreateDirectory(DataPath);
        File.WriteAllText(SettingsFile, JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
        }));
    }

    public string CheckFile => Path.Combine(DataPath, "Chk");

    public string HtmlFile => Path.Combine(DataPath, "Mazpro.html");

    public void Remember(string path)
    {
        Recent.RemoveAll(item => string.Equals(item, path, StringComparison.OrdinalIgnoreCase));
        Recent.Insert(0, path);
        if (Recent.Count > 8)
        {
            Recent.RemoveRange(8, Recent.Count - 8);
        }

        LastFolder = Path.GetDirectoryName(path) ?? LastFolder;
    }

    public FamilySetup Family(string key)
    {
        if (!Families.TryGetValue(key, out var setup))
        {
            setup = new FamilySetup();
            Families[key] = setup;
        }

        return setup;
    }

    public void EnsureDefaults()
    {
        if (string.IsNullOrWhiteSpace(DataPath))
        {
            DataPath = Path.Combine(AppDirectory, "Data");
        }

        Seed("M640M", "Default (VARIAXIS)", false);
        Seed("M640MPro", "Default", false);
        Seed("M640T", "QTN MSY", false);
        Seed("M640MT", "SQT MSY", false);
        Seed("M640MTPro", "INTE100SY", false);
        Seed("MatrixIntegrex", "INTEGREX_ST", false);
        Seed("MatrixTurning", "TURNING_(QTN)", false);
        Seed("MatrixMachining", "MACHINING_(VRX)", false);
        Seed("MatrixIntegrexE", "INTEGREX-e _HS", true);
    }

    private void Seed(string key, string machine, bool detail)
    {
        if (Families.ContainsKey(key))
        {
            return;
        }

        Families[key] = new FamilySetup
        {
            Machines = [machine],
            Current = machine,
            ValidDetail = detail,
            R393 = detail ? 0x9500 : 0,
            R394 = detail ? 0x20AD : 0
        };
    }
}

public sealed class FamilySetup
{
    public List<string> Machines { get; set; } = [];
    public string Current { get; set; } = "";
    public bool Inch { get; set; }
    public bool ValidDetail { get; set; }
    public bool RotarySubmicron { get; set; }
    public int R392 { get; set; }
    public int R393 { get; set; }
    public int R394 { get; set; }
}
