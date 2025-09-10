"# NavisPlugin" 
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;

public static class GridHelper
{
    /// <summary>
    /// Пересечение сеток для группы коллизий
    /// </summary>
    /// <param name="group">Группа коллизий</param>
    /// <returns>Строка вида "1 - A" или "—"</returns>
    public static string GetGridIntersectionForGroup(ClashResultGroup group)
    {
        if (group == null) return "—";

        // Центр группы = центр BoundingBox
        Point3D groupCenter = group.BoundingBox.Center;

        // Берём активную сетку
        GridSystem oGS = Application.ActiveDocument.Grids.ActiveSystem;
        if (oGS == null) return "—";

        // Находим ближайшее пересечение
        GridIntersection gi = oGS.ClosestIntersection(groupCenter);
        if (gi == null) return "—";

        string uName = gi.UGrid != null ? gi.UGrid.DisplayName : "";
        string vName = gi.VGrid != null ? gi.VGrid.DisplayName : "";

        return $"{uName} - {vName}";
    }
}
