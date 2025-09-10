"# NavisPlugin" 
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;

public static class GridHelper
{
    /// <summary>
    /// Получает пересечение сеток для коллизии (ClashResult)
    /// </summary>
    /// <param name="clash">Коллизия</param>
    /// <returns>Строка вида "1 - A" или "—", если пересечение не найдено</returns>
    public static string GetGridIntersectionForClash(ClashResult clash)
    {
        if (clash == null) return "—";

        // Берём точку коллизии (центр)
        Point3D clashPoint = clash.Center;

        // Активная система сеток
        GridSystem oGS = Application.ActiveDocument.Grids.ActiveSystem;
        if (oGS == null) return "—";

        // Находим ближайшее пересечение
        GridIntersection gi = oGS.ClosestIntersection(clashPoint);
        if (gi == null) return "—";

        // Названия осей
        string uName = gi.UGrid != null ? gi.UGrid.DisplayName : "";
        string vName = gi.VGrid != null ? gi.VGrid.DisplayName : "";

        // Формируем как в Clash Detective: "1 - A"
        return $"{uName} - {vName}";
    }
}
