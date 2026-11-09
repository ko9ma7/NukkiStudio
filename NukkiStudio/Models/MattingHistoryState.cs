using System.Collections.Generic;
using System.Linq;
using System.Windows;
using NukkiStudio.Services;

namespace NukkiStudio.Models;

public class MattingHistoryState
{
    public List<BrushStrokePoint> BrushStrokes { get; set; }
    public List<MagicWandPoint> MagicWandPoints { get; set; }
    public List<LassoPolygon> LassoPolygons { get; set; }
    public Rect? RoiRect { get; set; }
    public bool EnableRoi { get; set; }
    public string Description { get; set; }

    public MattingHistoryState(
        IEnumerable<BrushStrokePoint> brushStrokes,
        IEnumerable<MagicWandPoint> magicWandPoints,
        IEnumerable<LassoPolygon> lassoPolygons,
        Rect? roiRect,
        bool enableRoi,
        string description = "")
    {
        BrushStrokes = brushStrokes.ToList();
        MagicWandPoints = magicWandPoints.ToList();
        LassoPolygons = lassoPolygons.Select(l => new LassoPolygon(new List<Point>(l.Points), l.IsInclude)).ToList();
        RoiRect = roiRect;
        EnableRoi = enableRoi;
        Description = description;
    }
}
