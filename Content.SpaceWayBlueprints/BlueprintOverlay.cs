using System.Numerics;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.SpaceWayBlueprints;

public sealed class BlueprintOverlay : Overlay
{
    private static readonly Color Fill = new(80, 160, 255, 40);
    private static readonly Color Outline = new(80, 160, 255, 200);
    private static readonly Color TooBigFill = new(255, 60, 60, 40);
    private static readonly Color TooBigOutline = new(255, 60, 60, 200);

    private readonly BlueprintSystem _blueprints;
    private readonly IEntityManager _entities;
    private readonly SharedTransformSystem _transform;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public BlueprintOverlay(BlueprintSystem blueprints, IEntityManager entities)
    {
        _blueprints = blueprints;
        _entities = entities;
        _transform = entities.System<SharedTransformSystem>();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (!_blueprints.TryGetSelection(out var grid, out var tiles, out var tooBig)
            || !_entities.TryGetComponent(grid, out MapGridComponent? gridComp)
            || _transform.GetMapId(grid) != args.MapId)
        {
            return;
        }

        var size = gridComp.TileSize;
        var box = new Box2(
            new Vector2(tiles.Left, tiles.Bottom) * size,
            new Vector2(tiles.Right, tiles.Top) * size);

        var handle = args.WorldHandle;
        handle.SetTransform(_transform.GetWorldMatrix(grid));
        handle.DrawRect(box, tooBig ? TooBigFill : Fill);
        handle.DrawRect(box, tooBig ? TooBigOutline : Outline, filled: false);
        handle.SetTransform(Matrix3x2.Identity);
    }
}
