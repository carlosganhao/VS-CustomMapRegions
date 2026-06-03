using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Cairo;
using CustomMapRegions.Client.Abstractions;
using CustomMapRegions.Client.Storage;
using CustomMapRegions.Common.Models;
using CustomMapRegions.Config;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;
using Region = CustomMapRegions.Common.Models.Region;

namespace CustomMapRegions.Client;

public class RegionMapLayer : MapLayer
{
    public override string Title => "Regions";
    public override string LayerGroupCode => "regions";
    public override EnumMapAppSide DataSide => EnumMapAppSide.Client;

    public List<int> AvailableColors = new();
    public Dictionary<string, CreateIconTextureDelegate> FillIcons = new();
    public Dictionary<string, LoadedTexture> TexturesByFill = new();
    public MeshRef QuadModel;
    public Guid SelectedComponentId;
    public IClientPlayer Player;

    private ClientCoreAPI capi;
    private static string[] _hexcolors = new string[36]
    {
        "#F9D0DC", "#F179AF", "#F15A4A", "#ED272A", "#A30A35", "#FFDE98", "#EFFD5F", "#F6EA5E", "#FDBB3A", "#C8772E",
        "#F47832", "C3D941", "#9FAB3A", "#94C948", "#47B749", "#366E4F", "#516D66", "93D7E3", "#7698CF", "#20909E",
        "#14A4DD", "#204EA2", "#28417A", "#C395C4", "#92479B", "#8E007E", "#5E3896", "D9D4CE", "#AFAAA8", "#706D64",
        "#4F4C2B", "#BF9C86", "#9885530", "#5D3D21", "#FFFFFF", "#080504"
    };

    private ConcurrentQueue<ChunkRegion> _readyChunks = new();
    private HashSet<FastVec2i> _visibleChunks = new();

    private List<RegionMapComponent> _chunkComponents = new();
    private Dictionary<FastVec2i, RegionMapComponent> _chunkToComponentMap = new();

    private ChunkHoverMapComponent _hoverComponent;
    private GuiAddRegionDialog _addDialog;
    private GuiEditRegionDialog _editDialog;
    private CustomMapRegionsConfig _config = ConfigManager.ConfigInstance;
    private AbstractClientStorage _regionStorage;
    private FastVec2i _currentMouseChunkPos;
    public string getRegionDbFilePath()
    {
        string path = System.IO.Path.Combine(GamePaths.DataPath, "Maps");
        GamePaths.EnsurePathExists(path);

        return System.IO.Path.Combine(path, api.World.SavegameIdentifier + "-regions.db");
    }

    public RegionMapLayer(ICoreAPI api, IWorldMapManager mapSink) : base(api, mapSink)
    {
        if (api.Side == EnumAppSide.Client)
        {
            capi = (ClientCoreAPI)api;
            Player = capi.World.Player;

            _regionStorage = ClientStorageFactory.BuildClientStorage(capi);
            _regionStorage.onChunkRegionsRecieved += OnReadyChunks;
            _regionStorage.onRegionsUpdated += OnRegionsUpdated;
            _regionStorage.onRegionsDeleted += OnRegionsDeleted;

            foreach (var hex in _hexcolors)
            {
                AvailableColors.Add(ColorUtil.Hex2Int(hex));
            }

            var fills = api.Assets.GetMany("textures/fills", "custommapregions");
            foreach (var fill in fills)
            {
                if (fill.Name.EndsWith(".png")) continue;

                string name = fill.Name.Substring(0, fill.Name.IndexOf('.'));

                FillIcons[name] = () =>
                {
                    var size = GlobalConstants.ChunkSize;
                    return capi.Gui.LoadSvg(fill.Location, size, size, size, size, ColorUtil.WhiteArgb);
                };

                capi.Gui.Icons.CustomIcons["wp" + name.UcFirst()] = (ctx, x, y, w, h, rgba) =>
                {
                    var col = ColorUtil.ColorFromRgba(rgba);
                    capi.Gui.DrawSvg(fill, ctx.GetTarget() as ImageSurface, ctx.Matrix, x, y, (int)w, (int)h, col);
                };
            }

            QuadModel = capi.Render.UploadMesh(QuadMeshUtil.GetQuad());
            
            api.ChatCommands.GetOrCreate("regionmap")
                    .BeginSubCommand("redraw")
                        .WithDescription("Redraw the map")
                        .HandleWith(OnMapCmdRedraw)
                    .EndSubCommand();

            ReloadIconTextures();
            EnsureIconTexturesLoaded();
        }
    }

    public TextCommandResult OnMapCmdRedraw(TextCommandCallingArgs args)
    {
        ResetChunkComponents();

        _regionStorage.QueryChunks(_visibleChunks.ToArray());

        return TextCommandResult.Success("Redrawing map...");
    }

    public override void ComposeDialogExtras(GuiDialogWorldMap guiDialogWorldMap, GuiComposer compo)
    {
        string key = "worldmap-layer-" + LayerGroupCode;

        ElementBounds dlgBounds =
            ElementStdBounds.AutosizedMainDialog
            .WithFixedPosition(
                compo.Bounds.renderX / RuntimeEnv.GUIScale - 210,
                (compo.Bounds.renderY + compo.Bounds.OuterHeight) / RuntimeEnv.GUIScale - (HasAnyPrivilege() ? 206 : 116)
            )
            .WithAlignment(EnumDialogArea.None)
        ;

        ElementBounds leftColumn = ElementBounds.Fixed(0, 0, 160, 25);

        ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bgBounds.BothSizing = ElementSizing.FitToChildren;
        bgBounds.WithChild(leftColumn);


        guiDialogWorldMap.Composers[key] =
            capi.Gui
                .CreateCompo(key, dlgBounds)
                .AddShadedDialogBG(bgBounds, false)
                .AddDialogTitleBar(Lang.Get("maplayer-"+LayerGroupCode), () => { guiDialogWorldMap.Composers[key].Enabled = false; })
                .BeginChildElements(bgBounds)
                    .AddStaticText(Lang.Get("regions-alpha"), CairoFont.WhiteDetailText(), leftColumn = leftColumn.BelowCopy(0, 5).WithFixedHeight(16))
                    .AddSlider((newValue) => { _config.OverlayAlpha = newValue / 100.0f; return true; }, leftColumn = leftColumn.BelowCopy(0, 5).WithFixedHeight(25), "alpha-slider")
                    .AddIf(HasAnyPrivilege())
                        .AddStaticText(Lang.Get("regions-brush-size"), CairoFont.WhiteDetailText(), leftColumn = leftColumn.BelowCopy(0, 5).WithFixedHeight(16))
                        .AddSlider((newValue) => { _config.BrushSize = newValue; return true; }, leftColumn = leftColumn.BelowCopy(0, 5).WithFixedHeight(25), "brush-slider")
                        .AddStaticText(Lang.Get("regions-lock-region"), CairoFont.WhiteDetailText(), leftColumn.BelowCopy(0, 8).WithFixedWidth(130).WithFixedOffset(0, 6))
                        .AddSwitch((newValue) => { _config.LockUnselectedRegions = newValue; }, (leftColumn = leftColumn.BelowCopy(0, 8)).WithFixedOffset(130, 0), "lock-switch")
                    .EndIf()
                .EndChildElements()
                .Compose()
        ;

        guiDialogWorldMap.Composers[key].GetSlider("alpha-slider").SetValues((int)(_config.OverlayAlpha * 100), 0, 100, 1);
        if(HasAnyPrivilege())
        {
            guiDialogWorldMap.Composers[key].GetSlider("brush-slider").SetValues(_config.BrushSize, 1, 9, 2);
            guiDialogWorldMap.Composers[key].GetSwitch("lock-switch").SetValue(_config.LockUnselectedRegions);
        }
    }

    public override void Render(GuiElementMap mapElem, float dt)
    {
        if (!base.Active)
        {
            return;
        }

        foreach (var component in _chunkComponents)
        {
            component.Render(mapElem, dt);
        }

        _hoverComponent?.Render(mapElem, dt);
    }

    float ttlTimer = 0f;
    public override void OnTick(float dt)
    {
        if (!_readyChunks.IsEmpty)
        {
            int chunksToProcess = Math.Min(_readyChunks.Count, 200);
            while (chunksToProcess-- > 0)
            {
                if (_readyChunks.TryDequeue(out var chunkRegion))
                {
                    if(_chunkToComponentMap.TryGetValue(chunkRegion.ChunkPos, out var toRemoveComp) && toRemoveComp.RegionId != chunkRegion.Region.RegionId)
                    {
                        toRemoveComp.RemoveChunk(chunkRegion.ChunkPos);
                        if(toRemoveComp.IsEmpty)
                        {
                            _chunkComponents.Remove(toRemoveComp);

                            if(toRemoveComp.RegionId == SelectedComponentId)
                            {
                                SelectedComponentId = Guid.Empty;
                            }
                        }
                        _chunkToComponentMap.Remove(chunkRegion.ChunkPos);
                    }

                    if(chunkRegion.Region.RegionId == Guid.Empty) continue;

                    var matchingComp = _chunkComponents.Find(x => x.RegionId == chunkRegion.Region.RegionId);
                    if (matchingComp == null)
                    {
                        matchingComp = CreateRegionComponent(chunkRegion);
                        _chunkComponents.Add(matchingComp);
                    }

                    matchingComp.AddChunk(chunkRegion.ChunkPos);
                    _chunkToComponentMap[chunkRegion.ChunkPos] = matchingComp;
                }
            }
        }

        ttlTimer += dt;
        if(ttlTimer > 1)
        {
            ttlTimer = 0;

            for(int i = _chunkComponents.Count - 1; i >= 0; i--)
            {
                var comp = _chunkComponents[i];
                if(comp.IsVisible(_visibleChunks))
                {
                    comp.TTL = RegionMapComponent.MaxTTL;
                    continue;
                }

                comp.TTL--;

                if(comp.TTL <= 0)
                {
                    _chunkToComponentMap.RemoveAll((chunkPos, otherComp) => otherComp == comp);
                    _chunkComponents.Remove(comp);
                }
            }
        }

        if(HasAnyPrivilege() && IsShiftPressed())
        {
            if (_hoverComponent is null)
            {
                RegenerateHoverComponent(_currentMouseChunkPos);
            }
            else
            {
                _hoverComponent.SetChunkCoords(_currentMouseChunkPos);
            }
        }
        else
        {
            _hoverComponent = null;
        }
        _drawTimer += dt;
    }

    private float _genTimer = 0f;
    public override void OnOffThreadTick(float dt)
    {
        _genTimer += dt;
        if (_genTimer < 0.1) return;
        _genTimer = 0;

        _regionStorage.OffThreadProcessQueues();
    }

    public override void OnViewChangedClient(List<FastVec2i> nowVisible, List<FastVec2i> nowHidden)
    {
        foreach (var chunk in nowVisible)
        {
            _visibleChunks.Add(chunk);
        }

        foreach (var chunk in nowHidden)
        {
            _visibleChunks.Remove(chunk);
        }

        _regionStorage.QueryChunks(nowVisible.ToArray());
    }

    public override void OnMouseUpClient(MouseEvent args, GuiElementMap mapElem)
    {
        if (!base.Active || !HasAnyPrivilege())
        {
            return;
        }

        if (HasAnyPrivilege() && IsShiftPressed() && args.Button == EnumMouseButton.Left)
        {
            if (_chunkToComponentMap.TryGetValue(_currentMouseChunkPos, out var comp) && HasOwnership(comp.Region))
            {
                SelectedComponentId = comp.RegionId;
            }
            else
            {
                SelectedComponentId = Guid.Empty;
            }
        }
        else if (IsShiftPressed() && args.Button == EnumMouseButton.Right && !_isDrawing)
        {
            api.Logger.Debug($"Shift RMB pressed. SelectedComponentId: {SelectedComponentId}; Chunk: {_currentMouseChunkPos}; Knows Chunk: {WorldMapContext.KnownChunks.Contains(_currentMouseChunkPos)}");
            if(HasPrivilege(ConfigManager.ShrinkRegionPrivilege) && IsCtrlPressed())
            {
                RemoveChunkFromRegion(_currentMouseChunkPos);
            }
            else if(HasPrivilege(ConfigManager.ExpandRegionPrivilege) && SelectedComponentId != Guid.Empty)
            {
                AddOrSwapChunkToRegion(_currentMouseChunkPos);
            }
            else if(SelectedComponentId == Guid.Empty && WorldMapContext.KnownChunks.Contains(_currentMouseChunkPos))
            {
                api.Logger.Debug($"HasPrivilege to open dialog: {HasPrivilege(ConfigManager.CreateRegionPrivilege)}");
                if(_chunkToComponentMap.TryGetValue(_currentMouseChunkPos, out var editedComp))
                {
                    if((
                            HasPrivilege(ConfigManager.ChangeRegionPrivilege)
                            || HasPrivilege(ConfigManager.DeleteRegionPrivilege)
                        )
                        && HasOwnership(editedComp.Region))
                    {
                        _editDialog = new GuiEditRegionDialog(capi, this, editedComp, HasPrivilege(ConfigManager.ChangeRegionPrivilege), HasPrivilege(ConfigManager.DeleteRegionPrivilege));
                        _editDialog.TryOpen();
                    }
                }
                else if(HasPrivilege(ConfigManager.CreateRegionPrivilege))
                {
                    api.Logger.Debug($"Dialog opening");
                    _addDialog = new GuiAddRegionDialog(capi, this, _currentMouseChunkPos);
                    _addDialog.TryOpen();
                }
            }
            args.Handled = true;
        }

        if(_isDrawing)
        {
            _isDrawing = false;
            args.Handled = true;
        }
    }

    bool _isDrawing = false;
    float _drawTimer = 0;
    public override void OnMouseMoveClient(MouseEvent args, GuiElementMap mapElem, StringBuilder hoverText)
    {
        if (!base.Active)
        {
            return;
        }

        _currentMouseChunkPos = getChunkCoordsOnMouse(args, mapElem);
        foreach (var comp in _chunkComponents)
        {
            comp.OnMouseMove(_currentMouseChunkPos, mapElem, hoverText);
        }

        if(!HasAnyPrivilege())
        {
            return;
        }

        if(IsShiftPressed())
        {
            if(_drawTimer > 0.1f && capi.Input.MouseButton.Right)
            {
                _drawTimer = 0;

                if(HasPrivilege(ConfigManager.ShrinkRegionPrivilege) && IsCtrlPressed())
                {
                    _isDrawing = true;
                    RemoveChunkFromRegion(_currentMouseChunkPos);
                }
                else if (HasPrivilege(ConfigManager.ExpandRegionPrivilege) && SelectedComponentId != Guid.Empty)
                {
                    _isDrawing = true;
                    AddOrSwapChunkToRegion(_currentMouseChunkPos);
                }
            }
        }
    }

    public override void OnMapClosedClient()
    {
        SelectedComponentId = Guid.Empty;
        _hoverComponent = null;

        _visibleChunks.Clear();

        ConfigManager.SaveModConfig(api);
    }

    public override void OnShutDown()
    {
        RegionMapComponent.DisposeStatic();
        _regionStorage.Dispose();
    }

    public override void Dispose()
    {
        _regionStorage?.Dispose();
        ResetChunkComponents();
        ResetIconTextures();
        QuadModel?.Dispose();

        base.Dispose();
    }

    public void CreateRegion(FastVec2i chunkCoords, int color, string name, string fillName)
    {
        var newRegionId = Guid.NewGuid();
        _regionStorage.CreateRegion(
            new ChunkRegion
            {
                ChunkPos = chunkCoords,
                Region = new Region
                {
                    RegionId = newRegionId,
                    Name = name,
                    Color = color,
                    Fill = fillName,
                }
            }
        );
        SelectedComponentId = newRegionId;
        AddOrSwapChunkToRegion(chunkCoords);
    }

    public void EditRegion(Guid regionId, int color, string name, string fillName)
    {
        _regionStorage.UpdateRegion(
            new Region 
            {
                RegionId = regionId,
                Name = name,
                Color = color,
                Fill = fillName,
            }
        );
    }

    public void DeleteRegion(Guid regionId)
    {
        _regionStorage.DeleteRegion(regionId);
        if(SelectedComponentId == regionId)
        {
            SelectedComponentId = Guid.Empty;
        }
    }

    public void ReloadIconTextures()
    {
        ResetIconTextures();
        EnsureIconTexturesLoaded();
    }

    protected void EnsureIconTexturesLoaded()
    {
        if (TexturesByFill != null) return;

        TexturesByFill = new Dictionary<string, LoadedTexture>();

        foreach (var val in FillIcons)
        {
            TexturesByFill[val.Key] = val.Value();
        }
    }

    private RegionMapComponent CreateRegionComponent(ChunkRegion chunkRegion)
    {
        var newRegionComp = new RegionMapComponent(capi, _config, this, chunkRegion);
        return newRegionComp;
    }
    
    private void RegenerateHoverComponent(FastVec2i chunkPos)
    {
        _hoverComponent = new ChunkHoverMapComponent(capi, _config, chunkPos, TexturesByFill["fillhover"], ColorUtil.ColorFromRgba(new Vec4f(1.0f, 1.0f, 1.0f, 0.5f)));
    }

    private void AddOrSwapChunkToRegion(FastVec2i chunkCoords)
    {
        var requestList = new List<FastVec2i>();

        foreach (var coord in getChunksInBrush(chunkCoords))
        {
            if(!_config.LockUnselectedRegions || !_chunkToComponentMap.TryGetValue(coord, out var comp))
            {
                requestList.Add(coord);
            }
        }

        _regionStorage.AddChunkToRegion(requestList.ToArray(), SelectedComponentId);
    }

    private void RemoveChunkFromRegion(FastVec2i chunkCoords)
    {
        var requestList = new List<FastVec2i>();

        foreach (var coord in getChunksInBrush(chunkCoords))
        {
            if(_chunkToComponentMap.TryGetValue(coord, out var comp) && (!_config.LockUnselectedRegions || comp.RegionId == SelectedComponentId))
            {
                requestList.Add(coord);
            }
        }

        _regionStorage.DeleteChunkRegion(requestList.ToArray());
    }

    private void OnReadyChunks(IEnumerable<ChunkRegion> readyChunks)
    {
        foreach (var chunk in readyChunks)
        {
            _readyChunks.Enqueue(chunk);
        }
    }

    private void OnRegionsUpdated(IEnumerable<Region> regions)
    {
        foreach (var region in regions)
        {
            var editedComp = _chunkComponents.Find(x => x.RegionId == region.RegionId);
            if(editedComp is not null)
            {
                editedComp.Update(region.Name, region.Color, region.Fill);
            }
        }
    }

    private void OnRegionsDeleted(IEnumerable<Guid> regionIds)
    {
        foreach (var id in regionIds)
        {
            _chunkComponents.RemoveAll(x => x.RegionId == id);
            _chunkToComponentMap.RemoveAll((chunkCoords, comp) => comp.RegionId == id);
        }
    }

    private FastVec2i getChunkCoordsOnMouse(MouseEvent mouse, GuiElementMap mapElem)
    {
        Vec2f mousePos = new Vec2f(mouse.X - (float)mapElem.Bounds.renderX, mouse.Y - (float)mapElem.Bounds.renderY);
        Vec3d mouseWorldPos = new();
        mapElem.TranslateViewPosToWorldPos(mousePos, ref mouseWorldPos);

        return new FastVec2i(mouseWorldPos.XInt / GlobalConstants.ChunkSize, mouseWorldPos.ZInt / GlobalConstants.ChunkSize);
    }

    private FastVec2i[] getChunksInBrush(FastVec2i centerChunk) => getChunksInBrush(centerChunk, _config.BrushSize);

    private FastVec2i[] getChunksInBrush(FastVec2i centerChunk, int brushSize)
    {
        int brushRadius = brushSize/2;
        FastVec2i[] result = new FastVec2i[brushSize * brushSize];

        for(int x = 0; x < brushSize; x++)
            for(int y = 0; y < brushSize; y++)
            {
                FastVec2i curChunk;
                int localX = x - brushRadius;
                int localY = y - brushRadius;
                if(localX == 0 && localY == 0)
                {
                    curChunk = centerChunk;
                }
                else
                {
                    curChunk = new FastVec2i(centerChunk.X - localX, centerChunk.Y - localY);
                }
                result[x + y * brushSize] = curChunk;
            }

        return result;
    }

    private void ResetChunkComponents()
    {
        if(_chunkComponents is not null)
        {
            foreach (var comp in _chunkComponents)
            {
                comp.Dispose();
            }
        }
        _chunkComponents?.Clear();
        _chunkToComponentMap?.Clear();
    }

    private void ResetIconTextures()
    {
        if(TexturesByFill is not null)
        {
            foreach (var icon in TexturesByFill)
            {
                icon.Value.Dispose();
            }
        }
        TexturesByFill = null;
    }

    private bool IsShiftPressed()
    {
        return capi.Input.KeyboardKeyState[(int)GlKeys.ShiftLeft];
    }

    private bool IsCtrlPressed()
    {
        return capi.Input.KeyboardKeyState[(int)GlKeys.ControlLeft];
    }

    private bool HasOwnership(Region region)
    {
        return ClientStorageFactory.ClientSideOnly
            || Player.HasPrivilege(ConfigManager.SuperRegionPrivilege)
            || Player.PlayerUID == region.PlayerID;
    }

    private bool HasAnyPrivilege()
    {
        return ClientStorageFactory.ClientSideOnly
            || Player.HasPrivilege(ConfigManager.SuperRegionPrivilege)
            || Player.HasPrivilege(ConfigManager.CreateRegionPrivilege)
            || Player.HasPrivilege(ConfigManager.ChangeRegionPrivilege)
            || Player.HasPrivilege(ConfigManager.DeleteRegionPrivilege)
            || Player.HasPrivilege(ConfigManager.ExpandRegionPrivilege)
            || Player.HasPrivilege(ConfigManager.ShrinkRegionPrivilege);
    }

    private bool HasPrivilege(string name)
    {
        return ClientStorageFactory.ClientSideOnly
            || Player.HasPrivilege(ConfigManager.SuperRegionPrivilege)
            || Player.HasPrivilege(name);
    }
}
