using System;
using Content.Client.Construction;
using Content.Client.Popups;
using Content.Shared.Construction;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Stacks;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.SpaceWayBlueprints;

public enum BlueprintMode : byte
{
    None,
    Select,
    Paste,
}

public sealed partial class BlueprintSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IEyeManager _eye = default!;
    [Dependency] private readonly IInputManager _input = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IOverlayManager _overlays = default!;
    [Dependency] private readonly IResourceManager _resources = default!;
    [Dependency] private readonly ConstructionSystem _construction = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;
    [Dependency] private readonly AppearanceSystem _appearance = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;

    public const int MaxSize = 18;

    public const string BuiltInFunction = "OpenBlueprintMenu";

    public event Action? StateChanged;
    public event Action? ListChanged;
    public event Action? GhostsChanged;

    public BlueprintMode Mode { get; private set; }

    public string ActiveName { get; private set; } = string.Empty;

    public bool CanUse => _player.LocalEntity is { } user && HasComp<HandsComponent>(user);

    private bool? _builtIn;

    public bool BuiltIn => _builtIn ??= _input.NetworkBindMap.FunctionExists(BuiltInFunction);

    public override void Initialize()
    {
        base.Initialize();

        UpdatesOutsidePrediction = true;

        SubscribeNetworkEvent<AckStructureConstructionMessage>(OnAck);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(OnPlayerDetached);

        SubscribeLocalEvent<EntityTerminatingEvent>(OnEntityTerminating);

        InitializeInput();
        _overlays.AddOverlay(new BlueprintOverlay(this, EntityManager));
    }

    public override void Shutdown()
    {
        base.Shutdown();

        ShutdownInput();
        _overlays.RemoveOverlay<BlueprintOverlay>();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (Mode == BlueprintMode.None)
            return;

        UpdateHover();

        if (_selectGrid is { } selectGrid && Deleted(selectGrid))
            _selectGrid = null;

        if (Mode == BlueprintMode.Paste)
            UpdatePreview();
    }

    public void Cancel()
    {
        if (Mode == BlueprintMode.None)
            return;

        DeletePreview();
        _pasting = null;
        _selectGrid = null;
        SetMode(BlueprintMode.None, string.Empty);
    }

    private void SetMode(BlueprintMode mode, string name)
    {
        Mode = mode;
        ActiveName = name;
        StateChanged?.Invoke();
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (!args.WasModified<ConstructionPrototype>()
            && !args.WasModified<ConstructionGraphPrototype>()
            && !args.WasModified<EntityPrototype>()
            && !args.WasModified<StackPrototype>())
        {
            return;
        }

        _recipes = null;
        _inheritedRecipes.Clear();
        _materials.Clear();
    }

    private void OnPlayerDetached(LocalPlayerDetachedEvent args)
    {
        Cancel();
    }

    public static string Plural(int count)
    {
        var tens = count % 100;
        var ones = count % 10;
        var word = tens is >= 11 and <= 14 ? "построек"
            : ones == 1 ? "постройка"
            : ones is >= 2 and <= 4 ? "постройки"
            : "построек";
        return $"{count} {word}";
    }
}
