using System;
using System.Reflection;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;

namespace ClashManager
{
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

            try
            {
                // Try to access the Grids property dynamically
                var doc = Application.ActiveDocument;
                if (doc == null)
                    return (NA, NA, NA, NA, null);

                // Use reflection to access Grids property safely
                var gridsProp = doc.GetType().GetProperty("Grids", BindingFlags.Public | BindingFlags.Instance);
                if (gridsProp == null)
                {
                    // Grids property doesn't exist in this API version
                    return (NA, NA, NA, NA, null);
                }

                var docGrids = gridsProp.GetValue(doc);
                if (docGrids == null)
                    return (NA, NA, NA, NA, null);

                // Get the Systems property
                var systemsProp = docGrids.GetType().GetProperty("Systems", BindingFlags.Public | BindingFlags.Instance);
                if (systemsProp == null)
                    return (NA, NA, NA, NA, null);

                var systems = systemsProp.GetValue(docGrids) as System.Collections.IEnumerable;
                if (systems == null)
                    return (NA, NA, NA, NA, null);

                GridIntersection nearest = null;
                double minDist = double.MaxValue;

                foreach (var systemObj in systems)
                {
                    if (systemObj == null) continue;

                    // Get Levels property
                    var levelsProp = systemObj.GetType().GetProperty("Levels", BindingFlags.Public | BindingFlags.Instance);
                    if (levelsProp == null) continue;

                    var levels = levelsProp.GetValue(systemObj) as System.Collections.IEnumerable;
                    if (levels == null) continue;

                    foreach (var levelObj in levels)
                    {
                        if (levelObj == null) continue;

                        // Try to get closest intersection
                        var closestIntersectionMethod = levelObj.GetType().GetMethod("ClosestIntersection",
                            BindingFlags.Public | BindingFlags.Instance,
                            null, new[] { typeof(Point3D) }, null);

                        if (closestIntersectionMethod == null) continue;

                        GridIntersection gi = null;
                        try
                        {
                            gi = closestIntersectionMethod.Invoke(levelObj, new object[] { clash.Center }) as GridIntersection;
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error getting closest intersection: {ex.Message}");
                            continue;
                        }

                        if (gi == null) continue;

                        // Get Position property
                        var giPositionProp = gi.GetType().GetProperty("Position", BindingFlags.Public | BindingFlags.Instance);
                        if (giPositionProp == null) continue;

                        var giPosition = giPositionProp.GetValue(gi) as Point3D;
                        if (giPosition == null) continue;

                        double dist = clash.Center.DistanceTo(giPosition);
                        if (dist < minDist)
                        {
                            minDist = dist;
                            nearest = gi;
                        }
                    }
                }

                if (nearest == null)
                    return (NA, NA, NA, NA, null);

                // Extract information from the nearest intersection
                string levelName = GetGridPropertyValue(nearest, "Level", "DisplayName") ?? NA;
                string intersectionName = GetGridPropertyValue(nearest, "DisplayName") ?? NA;
                string line1 = GetGridPropertyValue(nearest, "Line1", "DisplayName") ?? NA;
                string line2 = GetGridPropertyValue(nearest, "Line2", "DisplayName") ?? NA;

                var positionProp = nearest.GetType().GetProperty("Position", BindingFlags.Public | BindingFlags.Instance);
                Point3D pos = positionProp?.GetValue(nearest) as Point3D;

                return (levelName, intersectionName, line1, line2, pos);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in GetClashGridInfo: {ex.Message}");
                return (NA, NA, NA, NA, null);
            }
        }

        /// <summary>
        /// Helper method to safely get property values from grid objects using reflection
        /// </summary>
        private static string GetGridPropertyValue(object obj, params string[] propertyPath)
        {
            if (obj == null || propertyPath == null || propertyPath.Length == 0)
                return null;

            object current = obj;
            foreach (string propName in propertyPath)
            {
                if (current == null) return null;

                var prop = current.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
                if (prop == null) return null;

                current = prop.GetValue(current);
            }

            return current?.ToString();
        }

        /// <summary>
        /// Alternative method for determining axis intersection using a different approach
        /// This method searches for intersections within a specified tolerance and considers multiple candidates
        /// </summary>
        public static (string LevelName, string IntersectionName, string Line1, string Line2, Point3D Position)
         GetClashGridInfoAlternative(ClashResult clash, double tolerance = 5.0)
        {
            const string NA = "N/A";

            if (clash == null || clash.Center == null)
                return (NA, NA, NA, NA, null);

            try
            {
                var doc = Application.ActiveDocument;
                if (doc == null)
                    return (NA, NA, NA, NA, null);

                // Use reflection to access Grids property
                var gridsProp = doc.GetType().GetProperty("Grids", BindingFlags.Public | BindingFlags.Instance);
                if (gridsProp == null)
                    return (NA, NA, NA, NA, null);

                var docGrids = gridsProp.GetValue(doc);
                if (docGrids == null)
                    return (NA, NA, NA, NA, null);

                var systemsProp = docGrids.GetType().GetProperty("Systems", BindingFlags.Public | BindingFlags.Instance);
                if (systemsProp == null)
                    return (NA, NA, NA, NA, null);

                var systems = systemsProp.GetValue(docGrids) as System.Collections.IEnumerable;
                if (systems == null)
                    return (NA, NA, NA, NA, null);

                GridIntersection bestIntersection = null;
                double bestScore = double.MaxValue;

                // Try with larger tolerance and more test points
                double[] tolerances = { tolerance, tolerance * 2, tolerance * 5 };
                double[] offsets = { 0.0, 0.01, 0.05, 0.1, 0.5, 1.0 };

                foreach (var systemObj in systems)
                {
                    if (systemObj == null) continue;

                    var levelsProp = systemObj.GetType().GetProperty("Levels", BindingFlags.Public | BindingFlags.Instance);
                    if (levelsProp == null) continue;

                    var levels = levelsProp.GetValue(systemObj) as System.Collections.IEnumerable;
                    if (levels == null) continue;

                    foreach (var levelObj in levels)
                    {
                        if (levelObj == null) continue;

                        // Get the ClosestIntersection method once for this level
                        var closestIntersectionMethod = levelObj.GetType().GetMethod("ClosestIntersection",
                            BindingFlags.Public | BindingFlags.Instance,
                            null, new[] { typeof(Point3D) }, null);

                        if (closestIntersectionMethod == null) continue;

                        foreach (double currentTolerance in tolerances)
                        {
                            // Try multiple test points with different offsets
                            foreach (double xOffset in offsets)
                            {
                                foreach (double yOffset in offsets)
                                {
                                    if (xOffset == 0.0 && yOffset == 0.0) continue; // Skip center point for now

                                    Point3D testPoint = new Point3D(
                                        clash.Center.X + xOffset,
                                        clash.Center.Y + yOffset,
                                        clash.Center.Z
                                    );

                                    GridIntersection testGi = null;
                                    try
                                    {
                                        testGi = closestIntersectionMethod.Invoke(levelObj, new object[] { testPoint }) as GridIntersection;
                                    }
                                    catch { continue; }

                                    if (testGi != null)
                                    {
                                        var giPositionProp = testGi.GetType().GetProperty("Position", BindingFlags.Public | BindingFlags.Instance);
                                        if (giPositionProp == null) continue;

                                        var giPosition = giPositionProp.GetValue(testGi) as Point3D;
                                        if (giPosition == null) continue;

                                        double dist = clash.Center.DistanceTo(giPosition);

                                        if (dist <= currentTolerance)
                                        {
                                            // Calculate a score based on distance and level proximity
                                            double levelDiff = Math.Abs(clash.Center.Z - giPosition.Z);
                                            double score = dist + levelDiff * 0.1; // Weight level difference less

                                            if (score < bestScore)
                                            {
                                                bestScore = score;
                                                bestIntersection = testGi;
                                            }
                                        }
                                    }
                                }
                            }

                            // Also try the original center point
                            GridIntersection centerGi = null;
                            try
                            {
                                centerGi = closestIntersectionMethod.Invoke(levelObj, new object[] { clash.Center }) as GridIntersection;
                            }
                            catch { continue; }

                            if (centerGi != null)
                            {
                                var giPositionProp = centerGi.GetType().GetProperty("Position", BindingFlags.Public | BindingFlags.Instance);
                                if (giPositionProp == null) continue;

                                var giPosition = giPositionProp.GetValue(centerGi) as Point3D;
                                if (giPosition == null) continue;

                                double dist = clash.Center.DistanceTo(giPosition);

                                if (dist <= currentTolerance)
                                {
                                    double levelDiff = Math.Abs(clash.Center.Z - giPosition.Z);
                                    double score = dist + levelDiff * 0.1;

                                    if (score < bestScore)
                                    {
                                        bestScore = score;
                                        bestIntersection = centerGi;
                                    }
                                }
                            }
                        }
                    }
                }

                if (bestIntersection == null)
                    return (NA, NA, NA, NA, null);

                string levelName = GetGridPropertyValue(bestIntersection, "Level", "DisplayName") ?? NA;
                string intersectionName = GetGridPropertyValue(bestIntersection, "DisplayName") ?? NA;
                string line1 = GetGridPropertyValue(bestIntersection, "Line1", "DisplayName") ?? NA;
                string line2 = GetGridPropertyValue(bestIntersection, "Line2", "DisplayName") ?? NA;

                var positionProp = bestIntersection.GetType().GetProperty("Position", BindingFlags.Public | BindingFlags.Instance);
                Point3D pos = positionProp?.GetValue(bestIntersection) as Point3D;

                return (levelName, intersectionName, line1, line2, pos);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in GetClashGridInfoAlternative: {ex.Message}");
                return (NA, NA, NA, NA, null);
            }
        }
    }
}
