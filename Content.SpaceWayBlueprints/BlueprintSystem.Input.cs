using Content.Client.Construction;
using Content.Client.ContextMenu.UI;
using Content.Shared.Input;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.SpaceWayBlueprints;

public sealed partial class BlueprintSystem
{
    private EntityUid? _hoverGrid;
    private Vector2i _hoverTile;

    private void InitializeInput()
    {
        CommandBinds.Builder
            .BindBefore(EngineKeyFunctions.Use,
                new PointerInputCmdHandler(OnUse, outsidePrediction: true),
                typeof(ConstructionSystem))
            .BindBefore(EngineKeyFunctions.UseSecondary,
                new PointerInputCmdHandler(OnUseSecondary, outsidePrediction: true),
                typeof(EntityMenuUIController))
            .Bind(ContentKeyFunctions.RotateObjectCounterclockwise,
                new PointerInputCmdHandler((in PointerInputCmdHandler.PointerInputCmdArgs _) => Turn(1), outsidePrediction: true))
            .Bind(ContentKeyFunctions.RotateObjectClockwise,
                new PointerInputCmdHandler((in PointerInputCmdHandler.PointerInputCmdArgs _) => Turn(3), outsidePrediction: true))
            .Bind(EngineKeyFunctions.EditorRotateObject,
                new PointerInputCmdHandler((in PointerInputCmdHandler.PointerInputCmdArgs _) => Turn(3), outsidePrediction: true))
            .Register<BlueprintSystem>();
    }

    private void ShutdownInput()
    {
        CommandBinds.Unregister<BlueprintSystem>();
    }

    private bool OnUse(in PointerInputCmdHandler.PointerInputCmdArgs args)
    {
        switch (Mode)
        {
            case BlueprintMode.Select:
                ClickSelect(args.Coordinates);
                return true;
            case BlueprintMode.Paste:
                ClickPaste(args.Coordinates);
                return true;
            default:
                return _timing.IsFirstTimePredicted && TryBuild(args.Coordinates, args.EntityUid);
        }
    }

    private bool OnUseSecondary(in PointerInputCmdHandler.PointerInputCmdArgs args)
    {
        if (Mode == BlueprintMode.None)
            return false;

        Cancel();
        return true;
    }

    private bool Turn(int turns)
    {
        if (Mode != BlueprintMode.Paste)
            return false;

        _turns = (_turns + turns) % 4;
        return true;
    }

    private void UpdateHover()
    {
        _hoverGrid = null;

        var mouse = _eye.PixelToMap(_input.MouseScreenPosition);
        if (mouse.MapId == MapId.Nullspace || !_map.TryFindGridAt(mouse, out var grid, out var gridComp))
            return;

        _hoverGrid = grid;
        _hoverTile = _map.TileIndicesFor(grid, gridComp, mouse);
    }

    private bool TryGetTile(EntityCoordinates coords, out Entity<MapGridComponent> grid, out Vector2i tile)
    {
        grid = default;
        tile = default;

        var mapCoords = _transform.ToMapCoordinates(coords);
        if (mapCoords.MapId == MapId.Nullspace || !_map.TryFindGridAt(mapCoords, out var gridUid, out var gridComp))
            return false;

        grid = (gridUid, gridComp);
        tile = _map.TileIndicesFor(grid, mapCoords);
        return true;
    }

    public bool TryGetSelection(out EntityUid grid, out Box2i tiles, out bool tooBig)
    {
        grid = default;
        tiles = default;
        tooBig = false;

        if (Mode != BlueprintMode.Select || _hoverGrid is not { } hover)
            return false;

        if (_selectGrid is not { } start)
        {
            grid = hover;
            tiles = TileBox(_hoverTile, _hoverTile);
            return true;
        }

        if (start != hover)
            return false;

        grid = start;
        tiles = TileBox(_selectStart, _hoverTile);
        tooBig = IsTooBig(tiles);
        return true;
    }

    private static Box2i TileBox(Vector2i a, Vector2i b)
    {
        return new Box2i(Vector2i.ComponentMin(a, b), Vector2i.ComponentMax(a, b) + Vector2i.One);
    }

    private static bool IsTooBig(Box2i tiles)
    {
        return tiles.Width > MaxSize || tiles.Height > MaxSize;
    }
}
