using CustomMapRegions.Config;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace CustomMapRegions.Client;

public class ChunkHoverMapComponent : MapComponent
{
    private LoadedTexture _texture;
    private CustomMapRegionsConfig _config;
    private int _color;
    private Vec3d _worldPos = new();
    private Vec2f _viewPos = new();

    public ChunkHoverMapComponent(ICoreClientAPI capi, CustomMapRegionsConfig config, FastVec2i chunkCoords, LoadedTexture texture, int color) : base(capi)
    {
        _config = config;
        _texture = texture;
        _color = color;
        _worldPos = new Vec3d(chunkCoords.X * GlobalConstants.ChunkSize, 0, chunkCoords.Y * GlobalConstants.ChunkSize);
    }

    public override void Render(GuiElementMap map, float dt)
    {
        RenderChunks(map, dt);
    }

    private void RenderChunks(GuiElementMap map, float dt)
    {
        map.TranslateWorldPosToViewPos(_worldPos.SubCopy(_config.BrushRadius * GlobalConstants.ChunkSize, 0, _config.BrushRadius * GlobalConstants.ChunkSize), ref _viewPos);
        capi.Render.Render2DTexture(
                _texture.TextureId,
                (int)(map.Bounds.renderX + _viewPos.X),
                (int)(map.Bounds.renderY + _viewPos.Y),
                (int)(_texture.Width * _config.BrushSize * map.ZoomLevel),
                (int)(_texture.Height * _config.BrushSize * map.ZoomLevel),
                50,
                ColorUtil.ToRGBAVec4f(_color));
    }

    public void SetChunkCoords(FastVec2i chunkCoords)
    {
        _worldPos = new Vec3d(chunkCoords.X * GlobalConstants.ChunkSize, 0, chunkCoords.Y * GlobalConstants.ChunkSize);
    }
}