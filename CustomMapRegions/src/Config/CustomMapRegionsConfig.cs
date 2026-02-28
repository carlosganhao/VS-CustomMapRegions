namespace CustomMapRegions.Config;

public class CustomMapRegionsConfig
{
    public float OverlayAlpha = 0.5f;
    public int BrushSize = 1;
    public int BrushRadius
    {
        get { return BrushSize / 2; }
    }
    public bool LockUnselectedRegions = false;
    public bool DisableChunkRetries = false;

    public void SetDefaults()
    {
    }
}