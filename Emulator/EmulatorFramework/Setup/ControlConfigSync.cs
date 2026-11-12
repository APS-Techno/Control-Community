using System.Xml;
using System.Xml.Linq;

namespace APS.SystemEmulator.Setup;

public enum ControlSyncResult
{
    Synced,
    FileNotFound,
    IOError,
}

public static class ControlConfigSync
{
    public static ControlSyncResult TrySync(
        string emulatorBaseDir,
        string? overridePath,
        string configsRoot,
        string projectName,
        out string resolvedPath,
        out string? errorMessage)
    {
        resolvedPath = ResolvePath(emulatorBaseDir, overridePath);
        errorMessage = null;

        if (!File.Exists(resolvedPath))
            return ControlSyncResult.FileNotFound;

        var tmpPath = resolvedPath + ".tmp";
        var bakPath = resolvedPath + ".bak";

        try
        {
            var doc = XDocument.Load(resolvedPath);
            if (doc.Root == null)
                throw new InvalidDataException("Root element missing.");

            UpsertElement(doc.Root, "ConfigDir", configsRoot);
            UpsertElement(doc.Root, "Prefix", projectName);

            var settings = new XmlWriterSettings
            {
                Indent = true,
                OmitXmlDeclaration = false,
                Encoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            };
            using (var writer = XmlWriter.Create(tmpPath, settings))
            {
                doc.Save(writer);
            }

            File.Replace(tmpPath, resolvedPath, bakPath);
            return ControlSyncResult.Synced;
        }
        catch (Exception ex) when (
            ex is IOException ||
            ex is UnauthorizedAccessException ||
            ex is XmlException ||
            ex is InvalidDataException)
        {
            try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { /* best-effort */ }
            errorMessage = ex.Message;
            return ControlSyncResult.IOError;
        }
    }

    private static string ResolvePath(string emulatorBaseDir, string? overridePath)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            if (Path.IsPathRooted(overridePath))
                return Path.GetFullPath(overridePath);

            return Path.GetFullPath(Path.Combine(emulatorBaseDir, overridePath));
        }

        // Shared-bin layout: all apps (including Control) run from the same
        // output directory as the Emulator.
        var sharedBin = Path.GetFullPath(Path.Combine(emulatorBaseDir, "RootConfig.xml"));
        if (File.Exists(sharedBin))
            return sharedBin;

        // Per-repo-bin layout: <root>\Emulator\bin -> <root>\Control\bin.
        var perRepoBin = Path.GetFullPath(Path.Combine(
            emulatorBaseDir, "..", "..", "Control", "bin", "RootConfig.xml"));
        if (File.Exists(perRepoBin))
            return perRepoBin;

        return sharedBin;
    }

    private static void UpsertElement(XElement parent, string name, string value)
    {
        var existing = parent.Element(name);
        if (existing != null)
            existing.Value = value;
        else
            parent.Add(new XElement(name, value));
    }
}
