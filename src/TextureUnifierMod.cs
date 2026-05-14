using Colossal.Logging;
using Game;
using Game.Modding;

namespace TextureUnifier;

public sealed class TextureUnifierMod : IMod
{
    public const string DataFolderName = "TextureUnifier";
    private const string LogName = "TextureUnifier.Mod";

    internal static ILog Log { get; private set; } = LogManager.GetLogger(LogName);
    internal static TextureUnifierSettings? Settings { get; private set; }

    public void OnLoad(UpdateSystem updateSystem)
    {
        Log = LogManager.GetLogger(LogName).SetShowsErrorsInUI(false);
        Log.Info($"Texture Unifier loading version {typeof(TextureUnifierMod).Assembly.GetName().Version}.");
        Settings = new TextureUnifierSettings(this);
        Settings.RegisterInOptionsUI();
        TextureUnifierLocalization.Load(Settings);
        updateSystem.UpdateAt<TextureUnifierSystem>(SystemUpdatePhase.GameSimulation);
        updateSystem.UpdateAt<FoliageOverrideSystem>(SystemUpdatePhase.PreCulling);
    }

    public void OnDispose()
    {
        Log.Info("Texture Unifier disposing.");
        TextureUnifierSystem.ActiveSystem?.RestoreAll();
        Settings?.UnregisterInOptionsUI();
        Settings = null;
    }
}
