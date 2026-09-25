using System;
using System.Collections.Generic;
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Maths;

namespace Content.SpaceWayBlueprints;

public sealed class BlueprintPreview : Control
{
    private static readonly Color Background = new(0, 0, 0, 90);
    private static readonly Color GridLine = new(255, 255, 255, 14);

    private const float MinCellForGrid = 6f;
    private const float Padding = 4f;

    private readonly Vector2i _size;
    private readonly List<BlueprintIcon> _icons;

    public BlueprintPreview(Vector2i size, List<BlueprintIcon> icons, float pixels)
    {
        SetSize = new Vector2(pixels, pixels);
        _size = size;
        _icons = icons;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        handle.DrawRect(PixelSizeBox, Background);

        if (_size.X == 0 || _size.Y == 0)
            return;

        var pad = Padding * UIScale;
        var area = new Vector2(PixelSize.X - 2 * pad, PixelSize.Y - 2 * pad);
        var cell = MathF.Min(area.X / _size.X, area.Y / _size.Y);
        var origin = new Vector2(pad, pad) + (area - new Vector2(_size.X, _size.Y) * cell) / 2;

        if (cell >= MinCellForGrid * UIScale)
        {
            for (var x = 0; x <= _size.X; x++)
            {
                var from = origin + new Vector2(x * cell, 0);
                handle.DrawLine(from, from + new Vector2(0, _size.Y * cell), GridLine);
            }

            for (var y = 0; y <= _size.Y; y++)
            {
                var from = origin + new Vector2(0, y * cell);
                handle.DrawLine(from, from + new Vector2(_size.X * cell, 0), GridLine);
            }
        }

        foreach (var icon in _icons)
        {
            var topLeft = origin + new Vector2(icon.Tile.X * cell, (_size.Y - 1 - icon.Tile.Y) * cell);
            handle.DrawTextureRect(icon.Texture, UIBox2.FromDimensions(topLeft, new Vector2(cell, cell)));
        }
    }
}
