using System;
using System.Collections.Generic;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Construction.Steps;
using Content.Shared.Stacks;
using Robust.Shared.Localization;

namespace Content.SpaceWayBlueprints;

public readonly record struct BlueprintMaterial(string Name, int Amount);

public sealed partial class BlueprintSystem
{
    private readonly Dictionary<string, List<BlueprintMaterial>> _materials = new();

    public List<BlueprintMaterial> MaterialsOf(Blueprint blueprint)
    {
        var totals = new Dictionary<string, int>();
        foreach (var entry in blueprint.Entries)
        {
            foreach (var material in RecipeMaterials(entry.Recipe))
            {
                totals[material.Name] = totals.GetValueOrDefault(material.Name) + material.Amount;
            }
        }

        var list = new List<BlueprintMaterial>();
        foreach (var (name, amount) in totals)
        {
            list.Add(new BlueprintMaterial(name, amount));
        }

        list.Sort((a, b) => a.Amount != b.Amount
            ? b.Amount.CompareTo(a.Amount)
            : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        return list;
    }

    private List<BlueprintMaterial> RecipeMaterials(string recipeId)
    {
        if (_materials.TryGetValue(recipeId, out var cached))
            return cached;

        var list = new List<BlueprintMaterial>();
        _materials[recipeId] = list;

        if (!_prototypes.TryIndex<ConstructionPrototype>(recipeId, out var recipe)
            || !_prototypes.TryIndex(recipe.Graph, out var graph)
            || !graph.Nodes.TryGetValue(recipe.StartNode, out var node)
            || graph.Path(recipe.StartNode, recipe.TargetNode) is not { } path)
        {
            return list;
        }

        foreach (var next in path)
        {
            if (node.GetEdge(next.Name) is { } edge)
            {
                foreach (var step in edge.Steps)
                {
                    switch (step)
                    {
                        case MaterialConstructionGraphStep material:
                            var name = _prototypes.TryIndex<StackPrototype>(material.MaterialPrototypeId, out var stack)
                                ? Loc.GetString(stack.Name)
                                : material.MaterialPrototypeId.Id;
                            list.Add(new BlueprintMaterial(name, material.Amount));
                            break;
                        case ArbitraryInsertConstructionGraphStep insert when !string.IsNullOrEmpty(insert.Name):
                            list.Add(new BlueprintMaterial(Loc.GetString(insert.Name), 1));
                            break;
                    }
                }
            }

            node = next;
        }

        return list;
    }
}
