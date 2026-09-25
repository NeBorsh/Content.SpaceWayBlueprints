using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Utility;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.SpaceWayBlueprints;

public readonly record struct BlueprintIcon(Vector2i Tile, Texture Texture);

public sealed partial class BlueprintSystem
{
    public List<BlueprintIcon> IconsOf(Blueprint blueprint)
    {
        var sorted = new List<(int Depth, BlueprintIcon Icon)>();
        foreach (var entry in blueprint.Entries)
        {
            if (TryGetIcon(entry, out var texture, out var depth))
                sorted.Add((depth, new BlueprintIcon(entry.Offset, texture)));
        }

        sorted.Sort((a, b) => a.Depth.CompareTo(b.Depth));

        var icons = new List<BlueprintIcon>(sorted.Count);
        foreach (var (_, icon) in sorted)
        {
            icons.Add(icon);
        }

        return icons;
    }

    private bool TryGetIcon(BlueprintEntry entry, [NotNullWhen(true)] out Texture? texture, out int depth)
    {
        texture = null;
        depth = 0;

        if (!_construction.TryGetRecipePrototype(entry.Recipe, out var targetId)
            || !_prototypes.TryIndex(targetId, out EntityPrototype? target)
            || !target.TryComp(out SpriteComponent? sprite, EntityManager.ComponentFactory))
        {
            return false;
        }

        var icon = _sprite.GetPrototypeIcon(target);
        texture = icon.GetFrame(entry.Direction.Convert(icon.RsiDirections), 0);
        depth = sprite.DrawDepth;
        return true;
    }
}
