using System;

namespace CustomMapRegions.Common;

public class Region
{
    public Guid RegionId { get; set; }
    public string Name { get; set; }
    public string Fill { get; set; }
    public int Color { get; set; }
}