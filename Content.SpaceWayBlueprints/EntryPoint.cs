using Robust.Shared.ContentPack;
using Robust.Shared.Log;

namespace Content.SpaceWayBlueprints;

public sealed class EntryPoint : GameShared
{
    public override void PostInit()
    {
        Logger.GetSawmill("spaceway.blueprints").Info("Мод SpaceWay Blueprints загружен");
    }
}
