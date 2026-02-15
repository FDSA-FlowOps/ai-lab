using Exp04RagBasicQdrant.Models;

namespace Exp04RagBasicQdrant.Services;

public static class DocumentLoader
{
    public static List<DocumentData> Load(string root)
    {
        var dataPath = Path.Combine(root, "data");
        if (!Directory.Exists(dataPath))
        {
            throw new InvalidOperationException($"No existe carpeta data en '{root}'.");
        }

        var files = Directory.GetFiles(dataPath, "*.md", SearchOption.TopDirectoryOnly)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (files.Count == 0)
        {
            throw new InvalidOperationException("No se encontraron archivos .md en ./data.");
        }

        var docs = new List<DocumentData>(files.Count);
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var id = Path.GetFileNameWithoutExtension(file).ToUpperInvariant();
            var title = FirstMarkdownTitle(text) ?? Path.GetFileNameWithoutExtension(file);
            docs.Add(new DocumentData(id, title, text, file));
        }

        if (docs.Count == 0)
        {
            throw new InvalidOperationException("Los documentos de ./data estan vacios.");
        }

        return docs;
    }

    private static string? FirstMarkdownTitle(string text)
    {
        using var reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("# "))
            {
                return trimmed[2..].Trim();
            }
        }

        return null;
    }
}
