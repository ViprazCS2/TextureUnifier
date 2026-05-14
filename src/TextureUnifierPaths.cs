using System.IO;
using UnityEngine;

namespace TextureUnifier;

internal static class TextureUnifierPaths
{
    public static string RootPath => Path.Combine(Application.persistentDataPath, "ModsData", TextureUnifierMod.DataFolderName);

    public static string TexturesPath => Path.Combine(RootPath, "Textures");

    public static string PacksPath => Path.Combine(RootPath, "Packs");

    public static string ImportInboxPath => Path.Combine(RootPath, "ImportInbox");

    public static string ConfigPath => Path.Combine(RootPath, "config.json");
}
