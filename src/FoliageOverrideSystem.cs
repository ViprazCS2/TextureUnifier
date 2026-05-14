using Game;

namespace TextureUnifier;

public sealed class FoliageOverrideSystem : GameSystemBase
{
    protected override void OnUpdate()
    {
        TextureUnifierSystem.ActiveSystem?.ApplyFoliageEveryFrame();
    }
}
