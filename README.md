"# NavisPlugin" 
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;

public static string GetLevel(object clashObj)
{
    Point3D center;

    if (clashObj is ClashResult clash)
        center = clash.Center;
    else if (clashObj is ClashResultGroup group)
        center = group.BoundingBox.Center;
    else
        return "—";

    LevelSystem oLS = Application.ActiveDocument.Levels.ActiveSystem;
    if (oLS == null) return "—";

    Level level = oLS.ClosestLevel(center);
    return level != null ? level.DisplayName : "—";
}
