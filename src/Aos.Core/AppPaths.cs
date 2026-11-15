namespace Aos.Core;

public static class AppPaths
{
    public const string AppName = "AlmightysOverwatchSelector";

    public static string DataDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName);

    public static string Ensure()
    {
        Directory.CreateDirectory(DataDirectory);
        return DataDirectory;
    }

    public static string File(string name) => Path.Combine(Ensure(), name);
}
