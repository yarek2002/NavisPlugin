

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
        /// Получает уровень для коллизии (ClashResult) используя GridLevel
        /// </summary>
        /// <param name="clash">Коллизия</param>
        /// <returns>Название уровня или "—", если уровень не найден</returns>
        public static string GetLevelForClash(ClashResult clash)
        {
            if (clash == null) 
            {
                System.Diagnostics.Debug.WriteLine("GridHelper GetLevel: clash is null");
                return "—";
            }

            try
            {
                // Берём точку коллизии (центр)
                Point3D clashPoint = clash.Center;
                System.Diagnostics.Debug.WriteLine($"GridHelper GetLevel: clash point = {clashPoint}");

                // Проверяем доступность документа и сеток
                var doc = Application.ActiveDocument;
                if (doc == null)
                {
                    System.Diagnostics.Debug.WriteLine("GridHelper GetLevel: ActiveDocument is null");
                    return "—";
                }

                var grids = doc.Grids;
                if (grids == null)
                {
                    System.Diagnostics.Debug.WriteLine("GridHelper GetLevel: Grids is null");
                    return "—";
                }

                // Активная система сеток
                GridSystem oGS = grids.ActiveSystem;
                if (oGS == null)
                {
                    System.Diagnostics.Debug.WriteLine("GridHelper GetLevel: ActiveSystem is null");
                    // Попробуем получить любую доступную систему
                    var systems = grids.Systems;
                    if (systems != null && systems.Count > 0)
                    {
                        oGS = systems[0];
                        System.Diagnostics.Debug.WriteLine($"GridHelper GetLevel: Using first available system: {oGS.DisplayName}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("GridHelper GetLevel: No grid systems available");
                        return "—";
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"GridHelper GetLevel: ActiveSystem found: {oGS.DisplayName}");
                }

                // Находим ближайший уровень через Levels коллекцию
                var levels = oGS.Levels;
                if (levels == null || levels.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("GridHelper GetLevel: No levels available in system");
                    return "—";
                }

                GridLevel closestLevel = null;
                double minDistance = double.MaxValue;

                foreach (GridLevel level in levels)
                {
                    if (level == null) continue;

                    try
                    {
                        // Получаем позицию уровня (если доступна)
                        var levelPositionProp = level.GetType().GetProperty("Position", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        if (levelPositionProp != null)
                        {
                            var levelPosition = levelPositionProp.GetValue(level) as Point3D;
                            if (levelPosition != null)
                            {
                                double distance = clashPoint.DistanceTo(levelPosition);
                                if (distance < minDistance)
                                {
                                    minDistance = distance;
                                    closestLevel = level;
                                }
                            }
                        }
                        else
                        {
                            // Если нет Position, используем первый доступный уровень
                            if (closestLevel == null)
                            {
                                closestLevel = level;
                            }
                        }
                    }
                    catch (System.Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"GridHelper GetLevel: Error processing level: {ex.Message}");
                        continue;
                    }
                }

                if (closestLevel == null)
                {
                    System.Diagnostics.Debug.WriteLine("GridHelper GetLevel: No closest level found");
                    return "—";
                }

                System.Diagnostics.Debug.WriteLine($"GridHelper GetLevel: ClosestLevel found: {closestLevel.DisplayName}");

                // Получаем название уровня
                string levelName = closestLevel.DisplayName ?? "";
                
                if (!string.IsNullOrEmpty(levelName))
                {
                    System.Diagnostics.Debug.WriteLine($"GridHelper GetLevel: Result = '{levelName}'");
                    return levelName;
                }

                System.Diagnostics.Debug.WriteLine("GridHelper GetLevel: No level name found");
                return "—";
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GridHelper GetLevel: Exception: {ex.Message}");
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
