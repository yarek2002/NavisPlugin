

using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;

namespace ClashManager
{
    /// <summary>
    /// Вспомогательный класс для работы с сетками в Navisworks
    /// </summary>
    public static class GridHelper
    {
        /// <summary>
        /// Получает пересечение сеток для коллизии (ClashResult)
        /// </summary>
        /// <param name="clash">Коллизия</param>
        /// <returns>Строка вида "1 - A" или "—", если пересечение не найдено</returns>
        public static string GetGridIntersectionForClash(ClashResult clash)
        {
            if (clash == null) 
            {
                System.Diagnostics.Debug.WriteLine("GridHelper: clash is null");
                return "—";
            }

            try
            {
                // Берём точку коллизии (центр)
                Point3D clashPoint = clash.Center;
                System.Diagnostics.Debug.WriteLine($"GridHelper: clash point = {clashPoint}");

                // Проверяем доступность документа и сеток
                var doc = Application.ActiveDocument;
                if (doc == null)
                {
                    System.Diagnostics.Debug.WriteLine("GridHelper: ActiveDocument is null");
                    return "—";
                }

                var grids = doc.Grids;
                if (grids == null)
                {
                    System.Diagnostics.Debug.WriteLine("GridHelper: Grids is null");
                    return "—";
                }

                // Активная система сеток
                GridSystem oGS = grids.ActiveSystem;
                if (oGS == null)
                {
                    System.Diagnostics.Debug.WriteLine("GridHelper: ActiveSystem is null");
                    // Попробуем получить любую доступную систему
                    var systems = grids.Systems;
                    if (systems != null && systems.Count > 0)
                    {
                        oGS = systems[0];
                        System.Diagnostics.Debug.WriteLine($"GridHelper: Using first available system: {oGS.DisplayName}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("GridHelper: No grid systems available");
                        return "—";
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"GridHelper: ActiveSystem found: {oGS.DisplayName}");
                }

                // Находим ближайшее пересечение
                GridIntersection gi = oGS.ClosestIntersection(clashPoint);
                if (gi == null)
                {
                    System.Diagnostics.Debug.WriteLine("GridHelper: ClosestIntersection returned null");
                    return "—";
                }

                System.Diagnostics.Debug.WriteLine($"GridHelper: GridIntersection found at position: {gi.Position}");

                // Названия осей - используем правильные свойства API
                string line1Name = GetGridPropertyValue(gi, "Line1", "DisplayName") ?? "";
                string line2Name = GetGridPropertyValue(gi, "Line2", "DisplayName") ?? "";

                System.Diagnostics.Debug.WriteLine($"GridHelper: Line1 = '{line1Name}', Line2 = '{line2Name}'");

                // Формируем как в Clash Detective: "1 - A"
                if (!string.IsNullOrEmpty(line1Name) && !string.IsNullOrEmpty(line2Name))
                {
                    string result = $"{line1Name} - {line2Name}";
                    System.Diagnostics.Debug.WriteLine($"GridHelper: Result = '{result}'");
                    return result;
                }
                else if (!string.IsNullOrEmpty(line1Name))
                {
                    System.Diagnostics.Debug.WriteLine($"GridHelper: Result = '{line1Name}' (Line1 only)");
                    return line1Name;
                }
                else if (!string.IsNullOrEmpty(line2Name))
                {
                    System.Diagnostics.Debug.WriteLine($"GridHelper: Result = '{line2Name}' (Line2 only)");
                    return line2Name;
                }

                System.Diagnostics.Debug.WriteLine("GridHelper: No line names found");
                return "—";
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GridHelper: Exception: {ex.Message}");
                return "—";
            }
        }

        /// <summary>
        /// Альтернативный метод получения пересечения сеток, используя существующую логику
        /// </summary>
        public static string GetGridIntersectionForClashAlternative(ClashResult clash)
        {
            if (clash == null) return "—";

            try
            {
                // Используем существующий метод из ManagerCollisionView
                var (levelName, intersectionName, line1, line2, position) = 
                    ClashManager.ManagerCollision.Views.ManagerCollisionView.GetClashGridInfo(clash);

                System.Diagnostics.Debug.WriteLine($"GridHelper Alternative: level={levelName}, intersection={intersectionName}, line1={line1}, line2={line2}");

                if (!string.IsNullOrEmpty(intersectionName) && intersectionName != "N/A")
                {
                    return intersectionName;
                }

                if (!string.IsNullOrEmpty(line1) && !string.IsNullOrEmpty(line2) && line1 != "N/A" && line2 != "N/A")
                {
                    if (line1 == line2)
                        return line1;
                    else
                        return $"{line1} - {line2}";
                }
                else if (!string.IsNullOrEmpty(line1) && line1 != "N/A")
                {
                    return line1;
                }
                else if (!string.IsNullOrEmpty(line2) && line2 != "N/A")
                {
                    return line2;
                }

                return "—";
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GridHelper Alternative: Exception: {ex.Message}");
                return "—";
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

                var prop = current.GetType().GetProperty(propName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (prop == null) return null;

                current = prop.GetValue(current);
            }

            return current?.ToString();
        }
    }
}
