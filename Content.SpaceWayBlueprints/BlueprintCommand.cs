using Content.Shared.Administration;
using Robust.Client.Input;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.IoC;

namespace Content.SpaceWayBlueprints;

[AnyCommand]
public sealed class BlueprintCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entities = default!;
    [Dependency] private readonly IInputManager _input = default!;

    public string Command => "bp";
    public string Description => "Окно чертежей: скопировать участок и разложить призраками строительства";
    public string Help => "bp";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var blueprints = _entities.System<BlueprintSystem>();

        if (blueprints.BuiltIn)
        {
            var key = _input.GetKeyFunctionButtonString(new BoundKeyFunction(BlueprintSystem.BuiltInFunction));
            shell.WriteLine($"На этом сервере чертежи встроены — открываются клавишей {key}. Мод здесь не нужен.");
            return;
        }

        if (!BlueprintWindow.AnyOpen && !blueprints.CanUse)
        {
            shell.WriteLine("Чертежи доступны только тем, кто может строить.");
            return;
        }

        BlueprintWindow.Toggle(blueprints);
    }
}
