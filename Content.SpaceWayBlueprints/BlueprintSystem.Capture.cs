using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Content.Shared.Atmos.Components;
using Content.Shared.Construction.Prototypes;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.SpaceWayBlueprints;

public sealed partial class BlueprintSystem
{
    private EntityUid? _selectGrid;
    private Vector2i _selectStart;

    private Dictionary<string, string>? _recipes;

    private readonly Dictionary<string, string?> _inheritedRecipes = new();

    public void BeginSelect(string name)
    {
        if (!CanUse)
            return;

        Cancel();
        _selectGrid = null;
        SetMode(BlueprintMode.Select, UniqueName(name));
        _popup.PopupCursor("Выдели участок: два клика по углам");
    }

    private void ClickSelect(EntityCoordinates coords)
    {
        if (!TryGetTile(coords, out var grid, out var tile))
        {
            _popup.PopupCursor("Здесь нет станции");
            return;
        }

        if (_selectGrid is not { } start)
        {
            _selectGrid = grid;
            _selectStart = tile;
            return;
        }

        if (start != grid.Owner)
        {
            _popup.PopupCursor("Второй угол на другом гриде");
            return;
        }

        var tiles = TileBox(_selectStart, tile);
        if (IsTooBig(tiles))
        {
            _popup.PopupCursor($"Больше {MaxSize}×{MaxSize} нельзя, выбери второй угол ближе");
            return;
        }

        var blueprint = Capture(grid, tiles, out var skipped);

        var name = UniqueName(ActiveName);
        Cancel();

        if (blueprint.Entries.Count == 0)
        {
            _popup.PopupCursor("Здесь нечего копировать");
            return;
        }

        if (!TrySave(name, blueprint))
        {
            _popup.PopupCursor($"Не удалось сохранить чертёж «{name}»");
            return;
        }

        var message = $"Чертёж «{name}»: {Plural(blueprint.Entries.Count)}";
        if (skipped > 0)
            message += $", не строится и пропущено: {skipped}";
        _popup.PopupCursor(message);
    }

    public Blueprint Capture(Entity<MapGridComponent> grid, Box2i tiles, out int skipped)
    {
        var blueprint = new Blueprint();
        var seen = new HashSet<BlueprintEntry>();
        skipped = 0;

        for (var x = tiles.Left; x < tiles.Right; x++)
        {
            for (var y = tiles.Bottom; y < tiles.Top; y++)
            {
                var tile = new Vector2i(x, y);
                _anchoredBuffer.Clear();
                _map.GetAnchoredEntities(grid, tile, _anchoredBuffer);

                foreach (var entity in _anchoredBuffer)
                {
                    if (!TryGetRecipe(entity, out var recipe))
                    {
                        skipped++;
                        continue;
                    }

                    var entry = new BlueprintEntry(tile - tiles.BottomLeft, DirectionOf(entity, recipe), recipe.ID);
                    if (seen.Add(entry))
                        blueprint.Entries.Add(entry);
                }
            }
        }

        return blueprint;
    }

    private Direction DirectionOf(EntityUid entity, ConstructionPrototype recipe)
    {
        return recipe.CanRotate ? Transform(entity).LocalRotation.GetCardinalDir() : Direction.South;
    }

    private bool TryGetRecipe(EntityUid entity, [NotNullWhen(true)] out ConstructionPrototype? recipe)
    {
        recipe = null;

        if (MetaData(entity).EntityPrototype is not { } proto)
            return false;

        var id = proto.ID;

        if (TryComp(entity, out AtmosPipeLayersComponent? layers)
            && _prototypes.TryGetVariantCollection<EntityPrototype>(id, out var variants)
            && (int) layers.CurrentPipeLayer < variants.Count)
        {
            id = variants[(int) layers.CurrentPipeLayer].Id;
        }

        return InheritedRecipe(id) is { } recipeId && _prototypes.TryIndex(recipeId, out recipe);
    }

    private string? InheritedRecipe(string entity)
    {
        if (_inheritedRecipes.TryGetValue(entity, out var cached))
            return cached;

        var recipes = Recipes();
        string? found = null;

        foreach (var (id, _) in _prototypes.EnumerateAllParents<EntityPrototype>(entity, includeSelf: true))
        {
            if (recipes.TryGetValue(id, out var recipe))
            {
                found = recipe;
                break;
            }
        }

        _inheritedRecipes[entity] = found;
        return found;
    }

    private Dictionary<string, string> Recipes()
    {
        if (_recipes != null)
            return _recipes;

        _recipes = new Dictionary<string, string>();
        foreach (var recipe in _prototypes.EnumeratePrototypes<ConstructionPrototype>())
        {
            if (recipe.Type != ConstructionType.Structure
                || !_construction.TryGetRecipePrototype(recipe.ID, out var entity))
            {
                continue;
            }

            if (!_recipes.ContainsKey(entity) || recipe.ID == entity)
                _recipes[entity] = recipe.ID;
        }

        return _recipes;
    }
}
