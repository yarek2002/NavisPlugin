"# NavisPlugin" 

using System;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;

public static class NavisworksGridHelper
{
    /// <summary>
    /// Возвращает информацию о ближайшем пересечении сетки к центру коллизии:
    /// (levelDisplayName, intersectionDisplayName, line1, line2, position)
    /// </summary>
    public static (string LevelName, string IntersectionName, string Line1, string Line2, Point3D Position) 
        GetClashGridInfo(ClashResult clash)
    {
        const string NA = "N/A";

        if (clash == null || clash.Center == null)
            return (NA, NA, NA, NA, null);

        var docGrids = Application.ActiveDocument?.Grids;
        if (docGrids == null)
            return (NA, NA, NA, NA, null);

        var systems = docGrids.Systems; // коллекция GridSystem
        if (systems == null || systems.Count == 0)
            return (NA, NA, NA, NA, null);

        GridIntersection nearest = null;
        double minDist = double.MaxValue;

        foreach (GridSystem system in systems)
        {
            foreach (GridLevel level in system.Levels)
            {
                GridIntersection gi = null;
                try
                {
                    gi = level.ClosestIntersection(clash.Center);
                }
                catch { continue; }

                if (gi == null) 
                    continue;

                double dist = clash.Center.DistanceTo(gi.Position);
                if (dist < minDist)
                {
                    minDist = dist;
                    nearest = gi;
                }
            }
        }

        if (nearest == null)
            return (NA, NA, NA, NA, null);

        string levelName       = nearest.Level?.DisplayName   ?? NA;
        string intersection    = nearest.DisplayName         ?? NA;
        string line1           = nearest.Line1?.DisplayName  ?? NA;
        string line2           = nearest.Line2?.DisplayName  ?? NA;
        Point3D pos            = nearest.Position; // координата может быть null, если объект освобождён

        return (levelName, intersection, line1, line2, pos);
    }
}

