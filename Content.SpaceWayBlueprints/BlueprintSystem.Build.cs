using System;
using System.Collections.Generic;
using System.Linq;
using Content.Client.Construction;
using Content.Shared.Atmos.Components;
using Content.Shared.Construction;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Construction.Steps;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.SpaceWayBlueprints;

public sealed partial class BlueprintSystem
{
    private const float LayerDeadzone = 0.25f;

    private readonly record struct PlacedGhost(EntityUid Uid, EntityUid Grid, Vector2i Tile);

    private readonly Dictionary<int, PlacedGhost> _placed = new();

    private readonly Dictionary<EntityUid, int> _placedIds = new();
    private readonly HashSet<EntityUid> _started = new();

    private readonly Dictionary<(EntityUid Grid, Vector2i Tile), List<EntityUid>> _placedByTile = new();

    public int GhostCount => _placed.Count;

    public void ClearGhosts()
    {
        var count = _placed.Count;
        foreach (var ghost in _placed.Values)
        {
            QueueDel(ghost.Uid);
        }

        _placed.Clear();
        _placedIds.Clear();
        _started.Clear();
        _placedByTile.Clear();

        GhostsChanged?.Invoke();
        _popup.PopupCursor($"Убрано призраков: {count}");
    }

    private void Register(EntityUid ghost, EntityUid grid, Vector2i tile)
    {
        var id = Comp<ConstructionGhostComponent>(ghost).GhostId;
        _placed[id] = new PlacedGhost(ghost, grid, tile);
        _placedIds[ghost] = id;

        if (!_placedByTile.TryGetValue((grid, tile), out var list))
        {
            list = new List<EntityUid>();
            _placedByTile[(grid, tile)] = list;
        }

        list.Add(ghost);
    }

    private bool Unregister(EntityUid ghost)
    {
        if (!_placedIds.Remove(ghost, out var id) || !_placed.Remove(id, out var placed))
            return false;

        _started.Remove(ghost);

        if (_placedByTile.TryGetValue((placed.Grid, placed.Tile), out var list))
        {
            list.Remove(ghost);
            if (list.Count == 0)
                _placedByTile.Remove((placed.Grid, placed.Tile));
        }

        return true;
    }

    private IReadOnlyList<EntityUid> GhostsAt(EntityUid grid, Vector2i tile)
    {
        return _placedByTile.TryGetValue((grid, tile), out var list) ? list : Array.Empty<EntityUid>();
    }

    private bool TryBuild(EntityCoordinates click, EntityUid clicked)
    {
        if (ChooseGhost(click, clicked) is not { } ghost)
            return false;

        _started.Add(ghost);
        _construction.TryStartConstruction(ghost);
        return true;
    }

    private EntityUid? ChooseGhost(EntityCoordinates click, EntityUid clicked)
    {
        if (_player.LocalEntity is not { } user || !TryGetTile(click, out var grid, out var tile))
            return null;

        var ghosts = GhostsAt(grid, tile);
        if (ghosts.Count == 0)
            return null;

        var onGhost = ghosts.Contains(clicked);
        if (!onGhost && clicked.IsValid() && !Transform(clicked).Anchored)
            return null;

        var stack = new List<EntityUid>(ghosts);

        var startable = _hands.GetActiveItem(user) is { } held
                        && Narrow(stack, ghost => CanStartWith(RecipeOf(ghost), held));

        if (!onGhost && !startable)
            return null;

        Narrow(stack, ghost => !_started.Contains(ghost));
        if (stack.Contains(clicked))
            return clicked;

        var layer = LayerAt(click, Transform(stack[0]).Coordinates);
        Narrow(stack, ghost => LayerOf(RecipeOf(ghost)) == layer);
        Narrow(stack, ghost => ConditionsMet(ghost, user));

        return stack[0];
    }

    private bool ConditionsMet(EntityUid ghost, EntityUid user)
    {
        var xform = Transform(ghost);
        var direction = xform.LocalRotation.GetCardinalDir();

        foreach (var condition in RecipeOf(ghost).Conditions)
        {
            if (!condition.Condition(user, xform.Coordinates, direction))
                return false;
        }

        return true;
    }

    private ConstructionPrototype RecipeOf(EntityUid ghost)
    {
        return Comp<ConstructionGhostComponent>(ghost).Prototype!;
    }

    private static bool Narrow(List<EntityUid> list, Func<EntityUid, bool> fits)
    {
        if (!list.Exists(item => fits(item)))
            return false;

        list.RemoveAll(item => !fits(item));
        return true;
    }

    private bool CanStartWith(ConstructionPrototype recipe, EntityUid held)
    {
        if (!_prototypes.TryIndex(recipe.Graph, out var graph)
            || !graph.Nodes.TryGetValue(recipe.StartNode, out var start)
            || graph.Path(recipe.StartNode, recipe.TargetNode) is not { Length: > 0 } path
            || start.GetEdge(path[0].Name) is not { } edge)
        {
            return false;
        }

        foreach (var step in edge.Steps)
        {
            if (step is EntityInsertConstructionGraphStep insert
                && insert.EntityValid(held, EntityManager, EntityManager.ComponentFactory))
            {
                return true;
            }
        }

        return false;
    }

    private AtmosPipeLayer? LayerOf(ConstructionPrototype recipe)
    {
        if (!_construction.TryGetRecipePrototype(recipe.ID, out var targetId)
            || !_prototypes.TryIndex(targetId, out EntityPrototype? target)
            || !target.TryComp(out AtmosPipeLayersComponent? layers, EntityManager.ComponentFactory))
        {
            return null;
        }

        return layers.CurrentPipeLayer;
    }

    private AtmosPipeLayer LayerAt(EntityCoordinates click, EntityCoordinates tileCenter)
    {
        var gridRotation = _transform.GetWorldRotation(tileCenter.EntityId);
        var worldDiff = _transform.ToMapCoordinates(click).Position - _transform.ToMapCoordinates(tileCenter).Position;
        var diff = (-gridRotation).RotateVec(worldDiff);

        if (diff.Length() <= LayerDeadzone)
            return AtmosPipeLayer.Primary;

        var direction = (new Angle(diff) + _eye.CurrentEye.Rotation + gridRotation + Math.PI / 2).GetCardinalDir();
        return direction is Direction.North or Direction.East
            ? AtmosPipeLayer.Secondary
            : AtmosPipeLayer.Tertiary;
    }

    private void OnAck(AckStructureConstructionMessage msg)
    {
        if (!_placed.TryGetValue(msg.GhostId, out var placed))
            return;

        Unregister(placed.Uid);
        QueueDel(placed.Uid);
        GhostsChanged?.Invoke();
    }

    private void OnEntityTerminating(ref EntityTerminatingEvent args)
    {
        if (_placedIds.Count > 0 && Unregister(args.Entity.Owner))
            GhostsChanged?.Invoke();
    }
}
