using System;
using System.Collections.Generic;
using System.Text;
using CustomMapRegions.Common.Models;
using CustomMapRegions.Config;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.Common;
using Vintagestory.GameContent;
using Region = CustomMapRegions.Common.Models.Region;

namespace CustomMapRegions.Client;

public class RegionMapComponent : MapComponent
{
    public static int MaxTTL = 30;
    public static CairoFont NameplateFont;

    public Region Region;
    public Guid RegionId
    {
        get { return Region.RegionId; }
    }
    public int ChunkCount
    {
        get { return _chunks.Count; }
    }
    public bool IsEmpty
    {
        get { return _chunks.Count == 0; }
    }
    public bool IsRegionOwned
    {
        get { return Region.PlayerID == _mapLayer.Player.PlayerUID; }
    }

    private bool _selected;
    private FastVec2i _initialChunk;
    private HashSet<FastVec2i> _chunks = new();
    private QuadBoundsi _bounds;
    private TextTextureUtil _textUtils;
    private LoadedTexture _texture;
    private LoadedTexture _textTexture;
    private CustomMapRegionsConfig _config;
    private RegionMapLayer _mapLayer;
    private int _color;
    private Vec3d _worldPos = new();
    private Vec2f _viewPos = new();
    private Vec2f _minCornerViewPos = new();
    private Vec2f _maxCornerViewPos = new();
    Matrixf mvMat = new Matrixf();
    private MeshRef _quadModel;
    private float _textScale = 0.5f;
    public int TTL;

    public RegionMapComponent(ICoreClientAPI capi, CustomMapRegionsConfig config, RegionMapLayer mapLayer, ChunkRegion chunkRegion) : base(capi)
    {
        if(NameplateFont is null)
        {
            NameplateFont = new CairoFont()
            {
                Color = (double[])GuiStyle.DialogDefaultTextColor.Clone(),
                Fontname = GuiStyle.StandardFontName,
                UnscaledFontsize = GuiStyle.NormalFontSize,
                StrokeColor = ColorUtil.Hex2Doubles("#404040", 1),
                StrokeWidth = 1,
            };
        }
        TTL = MaxTTL;
        Region = chunkRegion.Region;
        _mapLayer = mapLayer;
        _config = config;
        _initialChunk = chunkRegion.ChunkPos;
        _textUtils = new TextTextureUtil(capi);
        _textTexture = new LoadedTexture(capi);
        _bounds = new QuadBoundsi();
        _bounds.x1 = _initialChunk.X;
        _bounds.x2 = _initialChunk.X;
        _bounds.y1 = _initialChunk.Y;
        _bounds.y2 = _initialChunk.Y;
        _color = Region.Color | 255 << 24;
        _texture = GetLoadedTextureOrFallback(Region.Fill);
        _textUtils.GenOrUpdateTextTexture(Region.Name, NameplateFont, ref _textTexture);
        AddChunk(_initialChunk);
        _quadModel = _mapLayer.QuadModel;
    }

    public void AddChunk(FastVec2i chunkCoords)
    {
        _chunks.Add(chunkCoords);
        ExpandBounds(chunkCoords);
    }

    public void RemoveChunk(FastVec2i chunkCoords)
    {
        _chunks.Remove(chunkCoords);
        RecalculateBounds();
    }

    public override void Render(GuiElementMap map, float dt)
    {
        _selected = this.RegionId == _mapLayer.SelectedComponentId;
        RenderChunks(map, dt);
        RenderNamePlate(map, dt);
    }

    public void OnMouseMove(FastVec2i chunkPos, GuiElementMap mapElem, StringBuilder hoverText)
    {
        if(_mapLayer.Player.PlayerUID != Region.PlayerID 
            || !MouseWithinBounds()
            || !_chunks.Contains(chunkPos)) return;

        hoverText.AppendLine($"You own this region");

        bool MouseWithinBounds()
        {
            return _bounds.x1 <= chunkPos.X
                && _bounds.x2 >= chunkPos.X
                && _bounds.y1 <= chunkPos.Y
                && _bounds.y2 >= chunkPos.Y;
        }
    }

    public bool IsVisible(HashSet<FastVec2i> visibleChunks)
    {
        return _chunks.Overlaps(visibleChunks);
    }

    public void Update(string name, int color, string fill)
    {
        Region.Name = name;
        Region.Color = color;
        Region.Fill = fill;
        ReinitLocals();
    }

    public void TempUpdate(string name, int color, string fill) => InitLocals(name, color, fill);

    public void ReinitLocals() => InitLocals(Region.Name, Region.Color, Region.Fill);

    public override bool Equals(object? obj)
    {
        if(obj is null) return false;
        if(obj is not RegionMapComponent other) return false;
        return other.RegionId == this.RegionId;
    }

    public override void Dispose()
    {
        _textTexture.Dispose();
        base.Dispose();
    }

    public static void DisposeStatic()
    {
        NameplateFont?.Dispose();
        NameplateFont = null;
    }

    private void RenderChunks(GuiElementMap map, float dt)
    {
        foreach (var chunkCoord in _chunks)
        {
            RecalculateViewPos(map, chunkCoord);
            if (_viewPos.X < -2 * GlobalConstants.ChunkSize * map.ZoomLevel
                || _viewPos.Y < -2 * GlobalConstants.ChunkSize * map.ZoomLevel
                || _viewPos.X > map.Bounds.OuterWidth + 2 * GlobalConstants.ChunkSize * map.ZoomLevel
                || _viewPos.Y > map.Bounds.OuterHeight + 2 * GlobalConstants.ChunkSize * map.ZoomLevel)
            {
                continue;
            }

            capi.Render.Render2DTexture(
                    _texture.TextureId,
                    (int)(map.Bounds.renderX + _viewPos.X),
                    (int)(map.Bounds.renderY + _viewPos.Y),
                    (int)(_texture.Width * map.ZoomLevel),
                    (int)(_texture.Height * map.ZoomLevel),
                    50,
                    ColorUtil.ToRGBAVec4f(
                        ColorUtil.ColorOverlay(
                            ColorUtil.ColorMultiply4(_color, 1, 1, 1, _config.OverlayAlpha),
                            ColorUtil.Hex2Int("#FFFFFFFF"),
                            _selected ? (MathF.Sin((capi.ElapsedMilliseconds/1000.0f) * MathF.PI) + 1) / 2.0f : 0)));

        }
    }

    private void RenderNamePlate(GuiElementMap map, float dt)
    {
        if(_bounds == null) return;

        CalculateMiddleOfBounds(map);

        float x = (float)(map.Bounds.renderX + ((_minCornerViewPos + _maxCornerViewPos) * 0.5).X);
        float y = (float)(map.Bounds.renderY + ((_minCornerViewPos + _maxCornerViewPos) * 0.5).Y);
        float boundWidth = _maxCornerViewPos.X - _minCornerViewPos.X;
        float scaleFactor = boundWidth >= _textTexture.Width ? _textScale : (boundWidth / _textTexture.Width) * _textScale;


        IShaderProgram prog = capi.Render.GetEngineShader(EnumShaderProgram.Gui);

        prog.BindTexture2D("tex2d", _textTexture.TextureId, 0);
        prog.UniformMatrix("projectionMatrix", capi.Render.CurrentProjectionMatrix);
        mvMat
            .Set(capi.Render.CurrentModelviewMatrix)
            .Translate(x, y, 60)
            .Scale(_textTexture.Width, _textTexture.Height, 0)
            .Scale(scaleFactor, scaleFactor, 0);

        prog.Uniform("rgbaIn", ColorUtil.ToRGBAVec4f(_color));
        prog.Uniform("extraGlow", 1);
        prog.Uniform("applyColor", 0);
        prog.Uniform("noTexture", 0f);
        prog.UniformMatrix("modelViewMatrix", mvMat.Values);
        capi.Render.RenderMesh(_quadModel);
    }

    private void RecalculateViewPos(GuiElementMap map, FastVec2i chunk, bool chunkCenter = false)
    {
        _worldPos.X = chunk.X * GlobalConstants.ChunkSize;
        _worldPos.Z = chunk.Y * GlobalConstants.ChunkSize;
        if (chunkCenter)
        {
            _worldPos.X += GlobalConstants.ChunkSize / 2;
            _worldPos.Z += GlobalConstants.ChunkSize / 2;
        }
        map.TranslateWorldPosToViewPos(_worldPos, ref _viewPos);
    }

    private void CalculateMiddleOfBounds(GuiElementMap map)
    {
        var minWorldPos = new Vec3d(_bounds.x1 * GlobalConstants.ChunkSize, 0, _bounds.y1 * GlobalConstants.ChunkSize);
        var maxWorldPos = new Vec3d((_bounds.x2 + 1) * GlobalConstants.ChunkSize, 0, (_bounds.y2 + 1) * GlobalConstants.ChunkSize);

        map.TranslateWorldPosToViewPos(minWorldPos, ref _minCornerViewPos);
        map.TranslateWorldPosToViewPos(maxWorldPos, ref _maxCornerViewPos);
    }

    private void ExpandBounds(FastVec2i chunkCoords)
    {
        _bounds.x1 = Math.Min(_bounds.x1, chunkCoords.X);
        _bounds.y1 = Math.Min(_bounds.y1, chunkCoords.Y);
        _bounds.x2 = Math.Max(_bounds.x2, chunkCoords.X);
        _bounds.y2 = Math.Max(_bounds.y2, chunkCoords.Y);
    }

    private void RecalculateBounds() => RecalculateBoundsWith(_chunks);

    private void RecalculateBoundsWith(ISet<FastVec2i> set)
    {
        _bounds = null;
        foreach (var chunk in set)
        {
            if (_bounds == null) {
                _bounds = new();
                _bounds.x1 = chunk.X;
                _bounds.y1 = chunk.Y;
                _bounds.x2 = chunk.X;
                _bounds.y2 = chunk.Y;
            }
            ExpandBounds(chunk);
        }
    }

    private void InitLocals(string name, int color, string fill)
    {
        _textTexture.Dispose();
        _color = color | 255 << 24;
        _texture = GetLoadedTextureOrFallback(fill);
        _textUtils.GenOrUpdateTextTexture(name, NameplateFont, ref _textTexture);
    }

    private LoadedTexture GetLoadedTextureOrFallback(string fill)
    {
        try
        {
            return _mapLayer.TexturesByFill[fill];
        }
        catch
        {
            return _mapLayer.TexturesByFill["fillfull"];
        }
    }
}