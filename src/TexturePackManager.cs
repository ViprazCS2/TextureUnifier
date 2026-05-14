using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;

namespace TextureUnifier;

internal static class TexturePackManager
{
    public const string ManualPackValue = "";
    public const string TemplatePackName = "MyTexturePack";

    private const string PacksFolderName = "Packs";
    private const string PreviewFolderName = "Previews";

    private static readonly TexturePackSlot[] ImportFiles =
    {
        new("Grass base color", "Grass_BaseColor.png"),
        new("Grass normal", "Grass_Normal.png"),
        new("Dirt base color", "Dirt_BaseColor.png"),
        new("Dirt normal", "Dirt_Normal.png"),
        new("Cliff base color", "Cliff_BaseColor.png"),
        new("Cliff normal", "Cliff_Normal.png"),
        new("Road base color", "Road_BaseColor.png"),
        new("Road normal", "Road_Normal.png"),
        new("Sidewalk base color", "Sidewalk_BaseColor.png", optional: true),
        new("Sidewalk normal", "Sidewalk_Normal.png", optional: true),
        new("Road wear base color", "RoadWear_BaseColor.png", optional: true),
        new("Road wear normal", "RoadWear_Normal.png", optional: true),
        new("Gravel base color", "Gravel_BaseColor.png"),
        new("Gravel normal", "Gravel_Normal.png")
    };

    private static readonly TexturePackSlot[] PreviewFiles =
    {
        new("Grass base color", "Grass_BaseColor.png"),
        new("Grass normal", "Grass_Normal.png"),
        new("Dirt base color", "Dirt_BaseColor.png"),
        new("Dirt normal", "Dirt_Normal.png"),
        new("Cliff base color", "Cliff_BaseColor.png"),
        new("Cliff normal", "Cliff_Normal.png"),
        new("Road base color", "Road_BaseColor.png"),
        new("Road normal", "Road_Normal.png"),
        new("Road wear base color", "RoadWear_BaseColor.png", optional: true),
        new("Road wear normal", "RoadWear_Normal.png", optional: true),
        new("Sidewalk base color", "Sidewalk_BaseColor.png", optional: true),
        new("Sidewalk normal", "Sidewalk_Normal.png", optional: true),
        new("Gravel base color", "Gravel_BaseColor.png"),
        new("Gravel normal", "Gravel_Normal.png")
    };

    public static IReadOnlyList<TexturePackSlot> Slots => ImportFiles;

    public static string[] GetPackNames()
    {
        Directory.CreateDirectory(TextureUnifierPaths.PacksPath);
        return Directory.GetDirectories(TextureUnifierPaths.PacksPath)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static void ApplyPack(TextureUnifierConfig config, string packName)
    {
        config.ActiveTexturePack = SanitizePackName(packName);
        if (string.IsNullOrWhiteSpace(config.ActiveTexturePack))
        {
            return;
        }

        string prefix = $"{PacksFolderName}/{config.ActiveTexturePack}/";
        config.Terrain.Grass.BaseColor = prefix + "Grass_BaseColor.png";
        config.Terrain.Grass.Normal = prefix + "Grass_Normal.png";
        config.Terrain.Dirt.BaseColor = prefix + "Dirt_BaseColor.png";
        config.Terrain.Dirt.Normal = prefix + "Dirt_Normal.png";
        config.Terrain.Rock.BaseColor = prefix + "Cliff_BaseColor.png";
        config.Terrain.Rock.Normal = prefix + "Cliff_Normal.png";

        config.Networks.Road.BaseColor = prefix + "Road_BaseColor.png";
        config.Networks.Road.Normal = prefix + "Road_Normal.png";
        config.Networks.Sidewalk.BaseColor = prefix + "Sidewalk_BaseColor.png";
        config.Networks.Sidewalk.Normal = prefix + "Sidewalk_Normal.png";
        config.Networks.ParkingLot.BaseColor = config.Networks.Road.BaseColor;
        config.Networks.ParkingLot.Normal = config.Networks.Road.Normal;
        config.Networks.RoadWear.BaseColor = prefix + "RoadWear_BaseColor.png";
        config.Networks.RoadWear.Normal = prefix + "RoadWear_Normal.png";
        config.Networks.Gravel.BaseColor = prefix + "Gravel_BaseColor.png";
        config.Networks.Gravel.Normal = prefix + "Gravel_Normal.png";

        config.Foliage.Texture.BaseColor = prefix + "Grass_BaseColor.png";
        config.Foliage.Texture.Normal = prefix + "Grass_Normal.png";
    }

    public static bool PackExists(string packName)
    {
        return !string.IsNullOrWhiteSpace(SanitizePackName(packName)) &&
               Directory.Exists(GetPackPath(packName));
    }

    public static void CreateTemplatePack()
    {
        string packPath = GetPackPath(TemplatePackName);
        Directory.CreateDirectory(packPath);

        string readmePath = Path.Combine(packPath, "README.txt");
        if (!File.Exists(readmePath) || IsStaleTemplateReadme(readmePath))
        {
            File.WriteAllText(readmePath, BuildTemplateReadme());
        }
    }

    public static string GetPackFolderPath(string packName)
    {
        return GetPackPath(packName);
    }

    public static string GetWritablePackName(string activePack)
    {
        string packName = SanitizePackName(activePack);
        if (string.IsNullOrWhiteSpace(packName))
        {
            packName = TemplatePackName;
        }

        Directory.CreateDirectory(GetPackPath(packName));
        EnsureTemplateReadme(packName);
        return packName;
    }

    public static string ImportTexture(string packName, string slotFileName, string sourcePath)
    {
        var slot = GetSlot(slotFileName);
        if (slot == null)
        {
            throw new InvalidOperationException("Choose a valid texture slot before importing.");
        }

        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Texture file was not found.", sourcePath);
        }

        string extension = Path.GetExtension(sourcePath);
        if (!IsSupportedTextureExtension(extension))
        {
            throw new InvalidOperationException("Texture Unifier can import .png, .jpg, or .jpeg files.");
        }

        string writablePackName = GetWritablePackName(packName);
        string targetPath = Path.Combine(GetPackPath(writablePackName), slot.Value.FileName);
        string fullSource = Path.GetFullPath(sourcePath);
        string fullTarget = Path.GetFullPath(targetPath);

        if (!string.Equals(fullSource, fullTarget, StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(fullSource, fullTarget, overwrite: true);
        }

        return fullTarget;
    }

    public static string? FindTextureFileForSlot(string folderPath, string slotFileName)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            return null;
        }

        string safeSlotFileName = Path.GetFileName(slotFileName);
        if (!string.IsNullOrWhiteSpace(safeSlotFileName))
        {
            string exactPath = Path.Combine(folderPath, safeSlotFileName);
            if (File.Exists(exactPath) && IsSupportedTextureExtension(Path.GetExtension(exactPath)))
            {
                return exactPath;
            }

            string slotNameWithoutExtension = Path.GetFileNameWithoutExtension(safeSlotFileName);
            foreach (string extension in new[] { ".png", ".jpg", ".jpeg" })
            {
                string alternatePath = Path.Combine(folderPath, slotNameWithoutExtension + extension);
                if (File.Exists(alternatePath))
                {
                    return alternatePath;
                }
            }
        }

        string[] textureFiles = Directory.GetFiles(folderPath)
            .Where(path => IsSupportedTextureExtension(Path.GetExtension(path)))
            .ToArray();

        return textureFiles.Length == 1 ? textureFiles[0] : null;
    }

    public static string WritePreviewHtml(TextureUnifierConfig config, string selectedSlotFileName)
    {
        Directory.CreateDirectory(Path.Combine(TextureUnifierPaths.RootPath, PreviewFolderName));
        string previewPath = Path.Combine(TextureUnifierPaths.RootPath, PreviewFolderName, "TextureUnifierPreview.html");
        string selectedSlot = string.IsNullOrWhiteSpace(selectedSlotFileName)
            ? PreviewFiles[0].FileName
            : selectedSlotFileName;

        var rows = PreviewFiles.Select(slot =>
        {
            string relativePath = GetSlotRelativePath(config, slot.FileName);
            string absolutePath = ResolveTexturePath(relativePath);
            bool exists = File.Exists(absolutePath);
            bool selected = string.Equals(slot.FileName, selectedSlot, StringComparison.OrdinalIgnoreCase);
            string cardClass = selected ? "card selected" : "card";
            string image = exists
                ? $"<img src=\"{ToFileUri(absolutePath)}\" alt=\"{Html(slot.Label)}\" />"
                : "<div class=\"missing\">Missing</div>";

            return $@"
<section class=""{cardClass}"">
  <div class=""image"">{image}</div>
  <div class=""meta"">
    <h2>{Html(slot.Label)}</h2>
    <p>{Html(slot.FileName)}{(slot.Optional ? " <span>optional</span>" : string.Empty)}</p>
    <code>{Html(absolutePath)}</code>
  </div>
</section>";
        });

        string packName = string.IsNullOrWhiteSpace(config.ActiveTexturePack)
            ? "Current config paths"
            : config.ActiveTexturePack;

        string html = $@"<!doctype html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"">
  <title>Texture Unifier Preview</title>
  <style>
    :root {{ color-scheme: dark; font-family: Segoe UI, Arial, sans-serif; background: #15171a; color: #eef1f4; }}
    body {{ margin: 0; padding: 28px; }}
    header {{ display: flex; justify-content: space-between; gap: 24px; align-items: end; margin-bottom: 24px; }}
    h1 {{ font-size: 28px; margin: 0 0 6px; font-weight: 650; }}
    p {{ margin: 0; color: #aeb6bf; }}
    main {{ display: grid; grid-template-columns: repeat(auto-fill, minmax(260px, 1fr)); gap: 16px; }}
    .card {{ border: 1px solid #30363d; background: #1f2328; border-radius: 8px; overflow: hidden; }}
    .selected {{ border-color: #68b7ff; box-shadow: 0 0 0 2px rgba(104, 183, 255, .28); }}
    .image {{ aspect-ratio: 1 / 1; display: grid; place-items: center; background: #101214; }}
    img {{ width: 100%; height: 100%; object-fit: contain; image-rendering: auto; }}
    .missing {{ color: #ffb4a8; font-size: 18px; }}
    .meta {{ padding: 12px; }}
    h2 {{ font-size: 16px; margin: 0 0 6px; }}
    span {{ color: #9cd67f; }}
    code {{ display: block; margin-top: 10px; white-space: pre-wrap; overflow-wrap: anywhere; color: #c9d1d9; font-size: 12px; }}
  </style>
</head>
<body>
  <header>
    <div>
      <h1>Texture Unifier Preview</h1>
      <p>{Html(packName)}</p>
    </div>
    <p>Generated {Html(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))}</p>
  </header>
  <main>
    {string.Join(Environment.NewLine, rows)}
  </main>
</body>
</html>";

        File.WriteAllText(previewPath, html);
        return previewPath;
    }

    public static string BuildTemplateReadme()
    {
        var lines = new List<string>
        {
            "Texture Unifier texture pack",
            "",
            "Put your own png or jpg files in this folder. Texture Unifier uses this folder when it is created from Options > Texture Unifier > New texture folder.",
            "",
            "Use BaseColor files for the colorful textures. Normal files are only for bump/surface detail.",
            "Normal files are the pink/purple textures. Do not use them for color.",
            "",
            "Recommended file names:"
        };

        foreach (var file in ImportFiles)
        {
            string suffix = file.Optional ? " (optional)" : string.Empty;
            lines.Add($"- {file.FileName}: {file.Label}{suffix}");
        }

        lines.Add("");
        lines.Add("You may leave optional files out, but enabled slots with missing files will log a warning.");
        lines.Add("");
        lines.Add("Optional sidewalk files work when Change sidewalks is enabled in advanced options.");
        lines.Add("Parking-lot road surfaces use Road_BaseColor and Road_Normal; there is no separate parking-lot texture file.");
        return string.Join(Environment.NewLine, lines);
    }

    public static string ResolveTexturePath(string relativeOrAbsolutePath)
    {
        if (string.IsNullOrWhiteSpace(relativeOrAbsolutePath))
        {
            return string.Empty;
        }

        return Path.IsPathRooted(relativeOrAbsolutePath)
            ? relativeOrAbsolutePath
            : Path.Combine(TextureUnifierPaths.RootPath, relativeOrAbsolutePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string GetSlotRelativePath(TextureUnifierConfig config, string slotFileName)
    {
        return slotFileName switch
        {
            "Grass_BaseColor.png" => config.Terrain.Grass.BaseColor,
            "Grass_Normal.png" => config.Terrain.Grass.Normal,
            "Dirt_BaseColor.png" => config.Terrain.Dirt.BaseColor,
            "Dirt_Normal.png" => config.Terrain.Dirt.Normal,
            "Cliff_BaseColor.png" => config.Terrain.Rock.BaseColor,
            "Cliff_Normal.png" => config.Terrain.Rock.Normal,
            "Road_BaseColor.png" => config.Networks.Road.BaseColor,
            "Road_Normal.png" => config.Networks.Road.Normal,
            "RoadWear_BaseColor.png" => config.Networks.RoadWear.BaseColor,
            "RoadWear_Normal.png" => config.Networks.RoadWear.Normal,
            "Sidewalk_BaseColor.png" => config.Networks.Sidewalk.BaseColor,
            "Sidewalk_Normal.png" => config.Networks.Sidewalk.Normal,
            "Gravel_BaseColor.png" => config.Networks.Gravel.BaseColor,
            "Gravel_Normal.png" => config.Networks.Gravel.Normal,
            _ => string.Empty
        };
    }

    private static TexturePackSlot? GetSlot(string slotFileName)
    {
        foreach (var slot in ImportFiles)
        {
            if (string.Equals(slot.FileName, slotFileName, StringComparison.OrdinalIgnoreCase))
            {
                return slot;
            }
        }

        return null;
    }

    private static void EnsureTemplateReadme(string packName)
    {
        string readmePath = Path.Combine(GetPackPath(packName), "README.txt");
        if (!File.Exists(readmePath) || IsStaleTemplateReadme(readmePath))
        {
            File.WriteAllText(readmePath, BuildTemplateReadme());
        }
    }

    private static bool IsStaleTemplateReadme(string readmePath)
    {
        try
        {
            string text = File.ReadAllText(readmePath);
            return text.Contains("Pick texture for slot") ||
                   text.Contains("Refresh texture packs") ||
                   text.IndexOf("ParkingLot_BaseColor", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("Sidewalk textures are not part", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("Sidewalk textures are intentionally not part", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsSupportedTextureExtension(string extension)
    {
        return string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetPackPath(string packName)
    {
        string safeName = SanitizePackName(packName);
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = TemplatePackName;
        }

        return Path.Combine(TextureUnifierPaths.PacksPath, safeName);
    }

    private static string SanitizePackName(string? packName)
    {
        if (string.IsNullOrWhiteSpace(packName))
        {
            return string.Empty;
        }

        string nonNullPackName = packName!;
        string safeName = Path.GetFileName(nonNullPackName.Trim().Replace('/', Path.DirectorySeparatorChar)) ?? string.Empty;
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            safeName = safeName.Replace(invalid, '_');
        }

        return safeName;
    }

    private static string Html(string value)
    {
        return WebUtility.HtmlEncode(value ?? string.Empty);
    }

    private static string ToFileUri(string path)
    {
        return new Uri(path).AbsoluteUri;
    }

    public readonly struct TexturePackSlot
    {
        public TexturePackSlot(string label, string fileName, bool optional = false)
        {
            Label = label;
            FileName = fileName;
            Optional = optional;
        }

        public string Label { get; }

        public string FileName { get; }

        public bool Optional { get; }
    }
}
