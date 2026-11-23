namespace MazatrolViewerAero.Language;

public sealed class UdlCatalog
{
    private static readonly Lazy<UdlCatalog> SharedLazy = new(() => Load());
    private readonly List<UdlLanguage> _languages = [];

    public static UdlCatalog Shared => SharedLazy.Value;

    public IReadOnlyList<UdlLanguage> All => _languages;

    public static UdlCatalog Load(string? folder = null)
    {
        var catalog = new UdlCatalog();
        var root = folder;
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(AppContext.BaseDirectory, "user_defined_language");
            if (!Directory.Exists(root))
            {
                var process = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(process))
                {
                    var beside = Path.Combine(Path.GetDirectoryName(process) ?? "", "user_defined_language");
                    if (Directory.Exists(beside))
                    {
                        root = beside;
                    }
                }
            }

            if (!Directory.Exists(root))
            {
                root = Path.Combine(Directory.GetCurrentDirectory(), "user_defined_language");
            }
        }

        if (!Directory.Exists(root))
        {
            return catalog;
        }

        foreach (var file in Directory.EnumerateFiles(root, "*.xml").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var language = UdlLanguage.TryLoad(file);
            if (language != null)
            {
                catalog._languages.Add(language);
            }
        }

        return catalog;
    }

    public UdlLanguage? FindByName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return _languages.FirstOrDefault(language => string.Equals(language.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public UdlLanguage? FindByExtension(string path)
    {
        var extension = Path.GetExtension(path);
        if (extension.Length == 0)
        {
            return null;
        }

        return _languages.FirstOrDefault(language =>
            language.Extensions.Any(ext => string.Equals(ext, extension, StringComparison.OrdinalIgnoreCase)));
    }
}
