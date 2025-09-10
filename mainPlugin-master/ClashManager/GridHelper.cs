

using System;
using System.Linq;
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
        /// Получает этаж для коллизии по координате Z (более точный метод)
        /// </summary>
        /// <param name="clash">Коллизия</param>
        /// <returns>Номер этажа или "—", если этаж не определен</returns>
        public static string GetFloorByElevation(ClashResult clash)
        {
            if (clash == null) 
            {
                System.Diagnostics.Debug.WriteLine("GridHelper GetFloor: clash is null");
                return "—";
            }

            try
            {
                // Берём точку коллизии (центр)
                Point3D clashPoint = clash.Center;
                double zCoordinate = clashPoint.Z;
                
                System.Diagnostics.Debug.WriteLine($"GridHelper GetFloor: clash Z coordinate = {zCoordinate}");

                // Типичные высоты этажей (можно настроить под конкретный проект)
                // Обычно этаж = 3-4 метра высоты
                double floorHeight = 3.0; // метры
                double groundFloorElevation = 0.0; // уровень первого этажа

                // Вычисляем номер этажа
                int floorNumber = (int)Math.Round((zCoordinate - groundFloorElevation) / floorHeight);
                
                System.Diagnostics.Debug.WriteLine($"GridHelper GetFloor: calculated floor = {floorNumber}");

                // Проверяем разумность результата
                if (floorNumber >= -10 && floorNumber <= 100) // разумные пределы
                {
                    string result = floorNumber.ToString();
                    System.Diagnostics.Debug.WriteLine($"GridHelper GetFloor: Result = '{result}'");
                    return result;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"GridHelper GetFloor: Floor number {floorNumber} is out of reasonable range");
                    return "—";
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GridHelper GetFloor: Exception: {ex.Message}");
                return "—";
            }
        }

        /// <summary>
        /// Получает уровень для коллизии (ClashResult) используя свойства элементов (правильный метод)
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
                // Пробуем получить уровень из свойств элементов коллизии
                string level1 = GetLevelFromModelItem(clash.CompositeItem1);
                string level2 = GetLevelFromModelItem(clash.CompositeItem2);

                // Предпочитаем первый найденный уровень
                if (!string.IsNullOrEmpty(level1) && level1 != "—")
                {
                    System.Diagnostics.Debug.WriteLine($"GridHelper GetLevel: Found level from item1: {level1}");
                    return level1;
                }
                else if (!string.IsNullOrEmpty(level2) && level2 != "—")
                {
                    System.Diagnostics.Debug.WriteLine($"GridHelper GetLevel: Found level from item2: {level2}");
                    return level2;
                }

                // Если не найден в свойствах, пробуем расчет по координате Z
                string floorByElevation = GetFloorByElevation(clash);
                if (!string.IsNullOrEmpty(floorByElevation) && floorByElevation != "—")
                {
                    System.Diagnostics.Debug.WriteLine($"GridHelper GetLevel: Using elevation calculation: {floorByElevation}");
                    return floorByElevation;
                }

                System.Diagnostics.Debug.WriteLine("GridHelper GetLevel: No level found");
                return "—";
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GridHelper GetLevel: Exception: {ex.Message}");
                return "—";
            }
        }

        /// <summary>
        /// Получает уровень из свойств ModelItem
        /// </summary>
        /// <param name="item">Элемент модели</param>
        /// <returns>Название уровня или "—", если не найден</returns>
        private static string GetLevelFromModelItem(ModelItem item)
        {
            if (item == null) return "—";

            try
            {
                // Ищем уровень в различных категориях и свойствах
                string[] categoryNames = { 
                    "Идентификация", "Constraints", "Element", "Object", "Properties", 
                    "Identity Data", "Данные идентификации", "Revit", "AutoCAD" 
                };
                string[] propertyNames = { 
                    "Level", "Этаж", "Floor", "Storey", "Story", "Уровень", 
                    "Level Name", "Имя уровня", "Floor Name", "Имя этажа",
                    "Base Level", "Базовый уровень", "Reference Level", "Этаж ссылки"
                };

                foreach (string catName in categoryNames)
                {
                    foreach (string propName in propertyNames)
                    {
                        try
                        {
                            DataProperty prop = item.PropertyCategories.FindPropertyByDisplayName(catName, propName);
                            if (prop != null)
                            {
                                string value = prop.Value?.ToDisplayString() ?? "";
                                if (!string.IsNullOrEmpty(value) && value != "N/A" && value != "—")
                                {
                                    System.Diagnostics.Debug.WriteLine($"GridHelper GetLevelFromModelItem: Found {catName}.{propName} = {value}");
                                    return value;
                                }
                            }
                        }
                        catch
                        {
                            // Continue searching
                        }
                    }
                }

                // Fallback: поиск по всем свойствам
                foreach (PropertyCategory cat in item.PropertyCategories)
                {
                    foreach (DataProperty prop in cat.Properties)
                    {
                        try
                        {
                            string displayName = prop.DisplayName ?? string.Empty;
                            if (displayName.IndexOf("Level", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                displayName.IndexOf("Этаж", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                displayName.IndexOf("Floor", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                string value = prop.Value?.ToDisplayString() ?? "";
                                if (!string.IsNullOrEmpty(value) && value != "N/A" && value != "—")
                                {
                                    System.Diagnostics.Debug.WriteLine($"GridHelper GetLevelFromModelItem: Found {cat.DisplayName}.{displayName} = {value}");
                                    return value;
                                }
                            }
                        }
                        catch
                        {
                            // Continue searching
                        }
                    }
                }

                // Попробуем получить от родительского элемента
                if (item.Parent != null)
                {
                    return GetLevelFromModelItem(item.Parent);
                }

                return "—";
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GridHelper GetLevelFromModelItem: Exception: {ex.Message}");
                return "—";
            }
        }

        /// <summary>
        /// Получает уровень для группы коллизий (ClashResultGroup)
        /// </summary>
        /// <param name="group">Группа коллизий</param>
        /// <returns>Название уровня или "—", если уровень не найден</returns>
        public static string GetLevelForGroup(ClashResultGroup group)
        {
            if (group == null) 
            {
                System.Diagnostics.Debug.WriteLine("GridHelper GetLevelForGroup: group is null");
                return "—";
            }

            try
            {
                // Пробуем получить уровень из первого результата в группе
                var firstResult = GetAllResultsFromGroup(group).FirstOrDefault();
                if (firstResult != null)
                {
                    return GetLevelForClash(firstResult);
                }

                System.Diagnostics.Debug.WriteLine("GridHelper GetLevelForGroup: No results found in group");
                return "—";
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GridHelper GetLevelForGroup: Exception: {ex.Message}");
                return "—";
            }
        }

        /// <summary>
        /// Получает все результаты из группы коллизий (рекурсивно)
        /// </summary>
        /// <param name="group">Группа коллизий</param>
        /// <returns>Перечисление результатов</returns>
        private static System.Collections.Generic.IEnumerable<ClashResult> GetAllResultsFromGroup(ClashResultGroup group)
        {
            foreach (var r in group.Children.OfType<ClashResult>())
                yield return r;

            foreach (var g in group.Children.OfType<ClashResultGroup>())
            {
                foreach (var r in GetAllResultsFromGroup(g))
                    yield return r;
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
