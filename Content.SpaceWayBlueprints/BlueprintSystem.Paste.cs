using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Client.Construction;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Wall;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.SpaceWayBlueprints;

public sealed partial class BlueprintSystem
{
    private static readonly Color GhostColor = new(48, 255, 48, 128);
    private static readonly Color PreviewColor = new(80, 160, 255, 140);
    private static readonly Color BlockedColor = new(255, 60, 60, 110);

    private const string GhostPrototype = "constructionghost";

    private Blueprint? _pasting;

    private int _turns;

    private readonly List<EntityUid?> _preview = new();
    private EntityUid? _previewGrid;
    private Vector2i _previewAnchor;
    private int _previewTurns = -1;

    private readonly List<EntityUid> _anchoredBuffer = new();

    public void BeginPaste(string name)
    {
        if (!CanUse || !TryLoad(name, out var blueprint))
            return;

        if (IsTooBig(new Box2i(Vector2i.Zero, blueprint.Size)))
        {
            _popup.PopupCursor($"Чертёж больше {MaxSize}×{MaxSize}, такой не поставить");
            return;
        }

        Cancel();
        _pasting = blueprint;
        _turns = 0;
        SetMode(BlueprintMode.Paste, name);

        var missing = CountMissing(blueprint);
        _popup.PopupCursor(missing == 0
            ? "Ставь чертёж на станцию"
            : $"На этом сервере нет {missing} из построек чертежа, их не будет");
    }

    public List<EntityUid> Place(Blueprint blueprint, Entity<MapGridComponent> grid, Vector2i anchor, int turns, out int blocked, out int missing)
    {
        var placed = new List<EntityUid>();
        blocked = 0;
        missing = 0;

        if (_player.LocalEntity is not { } user)
            return placed;

        foreach (var entry in blueprint.Entries)
        {
            if (!_prototypes.TryIndex<ConstructionPrototype>(entry.Recipe, out var recipe))
            {
                missing++;
                continue;
            }

            var (tile, direction) = Placement(blueprint, anchor, turns, entry, recipe);
            var coords = _map.GridTileToLocal(grid, grid.Comp, tile);

            if (!CanPlace(grid, tile, coords, direction, recipe, user))
            {
                blocked++;
                continue;
            }

            if (!TrySpawnGhost(recipe, coords, direction, out var ghost))
            {
                missing++;
                continue;
            }

            Register(ghost.Value, grid, tile);
            placed.Add(ghost.Value);
        }

        GhostsChanged?.Invoke();
        return placed;
    }

    private void ClickPaste(EntityCoordinates coords)
    {
        if (_pasting == null)
            return;

        if (!TryGetTile(coords, out var grid, out var tile))
        {
            _popup.PopupCursor("Здесь нет станции");
            return;
        }

        if (_previewGrid == grid.Owner && _previewTurns == _turns)
            tile = _previewAnchor;

        var placed = Place(_pasting, grid, tile, _turns, out var blocked, out var missing);

        _previewTurns = -1;

        var message = $"Призраков: {placed.Count}";
        if (blocked > 0)
            message += $", не встало: {blocked}";
        if (missing > 0)
            message += $", нет на сервере: {missing}";
        _popup.PopupCursor(message);
    }

    private static (Vector2i Tile, Direction Direction) Placement(
        Blueprint blueprint,
        Vector2i anchor,
        int turns,
        BlueprintEntry entry,
        ConstructionPrototype recipe)
    {
        var tile = anchor + Blueprint.Rotate(entry.Offset - blueprint.Center, turns);
        var direction = recipe.CanRotate ? Blueprint.Rotate(entry.Direction, turns) : entry.Direction;
        return (tile, direction);
    }

    private bool CanPlace(
        Entity<MapGridComponent> grid,
        Vector2i tile,
        EntityCoordinates coords,
        Direction direction,
        ConstructionPrototype recipe,
        EntityUid user)
    {
        foreach (var ghost in GhostsAt(grid, tile))
        {
            if (RecipeOf(ghost) == recipe && Transform(ghost).LocalRotation.GetCardinalDir() == direction)
                return false;
        }

        _anchoredBuffer.Clear();
        _map.GetAnchoredEntities(grid, tile, _anchoredBuffer);
        foreach (var entity in _anchoredBuffer)
        {
            if (TryGetRecipe(entity, out var built) && built == recipe && DirectionOf(entity, built) == direction)
                return false;
        }

        foreach (var condition in recipe.Conditions)
        {
            if (!condition.Condition(user, coords, direction))
                return false;
        }

        return true;
    }

    private int CountMissing(Blueprint blueprint)
    {
        var missing = 0;
        foreach (var entry in blueprint.Entries)
        {
            if (!_prototypes.TryIndex<ConstructionPrototype>(entry.Recipe, out var recipe)
                || !_construction.TryGetRecipePrototype(recipe.ID, out var target)
                || !_prototypes.HasIndex<EntityPrototype>(target))
            {
                missing++;
            }
        }

        return missing;
    }

    private void UpdatePreview()
    {
        if (_pasting == null || _hoverGrid is not { } gridUid || !TryComp(gridUid, out MapGridComponent? gridComp))
            return;

        Entity<MapGridComponent> grid = (gridUid, gridComp);

        if (_previewGrid != gridUid)
        {
            DeletePreview();
            _previewGrid = gridUid;
            SpawnPreview(grid, _pasting);
        }

        if (_previewAnchor == _hoverTile && _previewTurns == _turns)
            return;

        _previewAnchor = _hoverTile;
        _previewTurns = _turns;
        LayoutPreview(grid, _pasting);
    }

    private void SpawnPreview(Entity<MapGridComponent> grid, Blueprint blueprint)
    {
        var origin = _map.GridTileToLocal(grid, grid.Comp, _hoverTile);
        foreach (var entry in blueprint.Entries)
        {
            _preview.Add(_prototypes.TryIndex<ConstructionPrototype>(entry.Recipe, out var recipe)
                         && TrySpawnGhost(recipe, origin, entry.Direction, out var ghost)
                ? ghost
                : null);
        }

        _previewTurns = -1;
    }

    private void LayoutPreview(Entity<MapGridComponent> grid, Blueprint blueprint)
    {
        var user = _player.LocalEntity;

        for (var i = 0; i < _preview.Count; i++)
        {
            if (_preview[i] is not { } ghost
                || Deleted(ghost)
                || Comp<ConstructionGhostComponent>(ghost).Prototype is not { } recipe)
            {
                continue;
            }

            var (tile, direction) = Placement(blueprint, _previewAnchor, _previewTurns, blueprint.Entries[i], recipe);
            var coords = _map.GridTileToLocal(grid, grid.Comp, tile);

            _transform.SetLocalPositionNoLerp(ghost, coords.Position);
            _transform.SetLocalRotationNoLerp(ghost, direction.ToAngle());

            var free = user is { } u && CanPlace(grid, tile, coords, direction, recipe, u);
            _sprite.SetColor(ghost, free ? PreviewColor : BlockedColor);
        }
    }

    private void DeletePreview()
    {
        foreach (var ghost in _preview)
        {
            if (ghost is { } uid && !Deleted(uid))
                Del(uid);
        }

        _preview.Clear();
        _previewGrid = null;
        _previewTurns = -1;
    }

    private bool TrySpawnGhost(ConstructionPrototype recipe, EntityCoordinates coords, Direction direction, [NotNullWhen(true)] out EntityUid? ghost)
    {
        ghost = null;

        if (!_construction.TryGetRecipePrototype(recipe.ID, out var targetId)
            || !_prototypes.TryIndex(targetId, out EntityPrototype? target))
        {
            return false;
        }

        var uid = EntityManager.CreateEntityUninitialized(GhostPrototype, coords, rotation: direction.ToAngle());
        Transform(uid).GridTraversal = false;
        EntityManager.InitializeAndStartEntity(uid);

        var comp = Comp<ConstructionGhostComponent>(uid);
        comp.Prototype = recipe;
        comp.GhostId = uid.GetHashCode();

        var sprite = Comp<SpriteComponent>(uid);

        if (target.TryComp(out IconComponent? icon, EntityManager.ComponentFactory))
        {
            _sprite.AddBlankLayer((uid, sprite), 0);
            _sprite.LayerSetSprite((uid, sprite), 0, icon.Icon);
            sprite.LayerSetShader(0, "unshaded");
            _sprite.LayerSetVisible((uid, sprite), 0, true);
        }
        else if (target.Components.ContainsKey("Sprite"))
        {
            var dummy = Spawn(targetId, MapCoordinates.Nullspace);
            var targetSprite = EnsureComp<SpriteComponent>(dummy);
            _appearance.OnChangeData(dummy, targetSprite);

            var drawDepth = sprite.DrawDepth;
            _sprite.CopySprite((dummy, targetSprite), (uid, sprite));
            _sprite.SetDrawDepth((uid, sprite), drawDepth);

            for (var i = 0; i < sprite.AllLayers.Count(); i++)
            {
                sprite.LayerSetShader(i, "unshaded");
            }

            Del(dummy);
        }
        else
        {
            Del(uid);
            return false;
        }

        _sprite.SetColor((uid, sprite), GhostColor);

        if (recipe.CanBuildInImpassable)
            EnsureComp<WallMountComponent>(uid).Arc = new Angle(Math.Tau);

        ghost = uid;
        return true;
    }
}
