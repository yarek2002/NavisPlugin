

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
        /// Получает уровень для коллизии используя COM API (как в Clash Detective)
        /// </summary>
        /// <param name="clashObj">Коллизия или группа коллизий</param>
        /// <returns>Название уровня или "—", если уровень не найден</returns>
        public static string GetLevelUsingComApi(object clashObj)
        {
            try
            {
                Point3D center;

                if (clashObj is ClashResult clash)
                    center = clash.Center;
                else if (clashObj is ClashResultGroup group)
                    center = group.BoundingBox.Center;
                else
                    return "—";

                System.Diagnostics.Debug.WriteLine($"GridHelper GetLevelUsingComApi: center = {center}");

                // Попробуем использовать COM API для доступа к LevelSystem
                try
                {
                    // Попробуем получить COM API через Application
                    var comApi = Application.ActiveDocument;
                    if (comApi != null)
                    {
                        // Попробуем различные способы доступа к уровням через COM API
                        var levels = comApi.GetType().GetProperty("Levels", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)?.GetValue(comApi);
                        if (levels != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"GridHelper GetLevelUsingComApi: Found Levels through reflection");
                            
                            // Попробуем найти метод ClosestLevel
                            var closestLevelMethod = levels.GetType().GetMethod("ClosestLevel", 
                                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                                null, new[] { typeof(Point3D) }, null);
                            
                            if (closestLevelMethod != null)
                            {
                                var level = closestLevelMethod.Invoke(levels, new object[] { center });
                                if (level != null)
                                {
                                    var displayNameProp = level.GetType().GetProperty("DisplayName", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                                    if (displayNameProp != null)
                                    {
                                        string levelName = displayNameProp.GetValue(level)?.ToString() ?? "";
                                        if (!string.IsNullOrEmpty(levelName))
                                        {
                                            System.Diagnostics.Debug.WriteLine($"GridHelper GetLevelUsingComApi: Found level: {levelName}");
                                            return levelName;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"GridHelper GetLevelUsingComApi: COM API error: {ex.Message}");
                }

                // Fallback: попробуем через рефлексию
                return GetLevelUsingReflection(clashObj);
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GridHelper GetLevelUsingComApi: Exception: {ex.Message}");
                return "—";
            }
        }

        /// <summary>
        /// Получает уровень используя рефлексию (fallback метод)
        /// </summary>
        /// <param name="clashObj">Коллизия или группа коллизий</param>
        /// <returns>Название уровня или "—", если уровень не найден</returns>
        private static string GetLevelUsingReflection(object clashObj)
        {
            try
            {
                Point3D center;

                if (clashObj is ClashResult clash)
                    center = clash.Center;
                else if (clashObj is ClashResultGroup group)
                    center = group.BoundingBox.Center;
                else
                    return "—";

                // Попробуем получить Levels через рефлексию
                var doc = Application.ActiveDocument;
                if (doc == null) return "—";

                // Попробуем различные варианты доступа к уровням
                var levelsProp = doc.GetType().GetProperty("Levels", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (levelsProp != null)
                {
                    var levels = levelsProp.GetValue(doc);
                    if (levels != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"GridHelper GetLevelUsingReflection: Found Levels property");
                        
                        // Попробуем найти метод ClosestLevel
                        var closestLevelMethod = levels.GetType().GetMethod("ClosestLevel", 
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                            null, new[] { typeof(Point3D) }, null);
                        
                        if (closestLevelMethod != null)
                        {
                            var level = closestLevelMethod.Invoke(levels, new object[] { center });
                            if (level != null)
                            {
                                var displayNameProp = level.GetType().GetProperty("DisplayName", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                                if (displayNameProp != null)
                                {
                                    string levelName = displayNameProp.GetValue(level)?.ToString() ?? "";
                                    if (!string.IsNullOrEmpty(levelName))
                                    {
                                        System.Diagnostics.Debug.WriteLine($"GridHelper GetLevelUsingReflection: Found level: {levelName}");
                                        return levelName;
                                    }
                                }
                            }
                        }
                    }
                }

                return "—";
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GridHelper GetLevelUsingReflection: Exception: {ex.Message}");
                return "—";
            }
        }

        /// <summary>
        /// Получает уровень из свойств ModelItem с расширенным поиском
        /// </summary>
        /// <param name="item">Элемент модели</param>
        /// <returns>Название уровня или "—", если не найден</returns>
        private static string GetLevelFromModelItem(ModelItem item)
        {
            if (item == null) return "—";

            try
            {
                // Расширенный список категорий и свойств для поиска уровня
                var levelSearchPatterns = new[]
                {
                    // Revit специфичные
                    new { Category = "Constraints", Property = "Level" },
                    new { Category = "Constraints", Property = "Base Level" },
                    new { Category = "Constraints", Property = "Reference Level" },
                    new { Category = "Identity Data", Property = "Level" },
                    new { Category = "Identity Data", Property = "Base Level" },
                    
                    // Общие
                    new { Category = "Element", Property = "Level" },
                    new { Category = "Element", Property = "Floor" },
                    new { Category = "Element", Property = "Storey" },
                    new { Category = "Object", Property = "Level" },
                    new { Category = "Object", Property = "Floor" },
                    new { Category = "Properties", Property = "Level" },
                    new { Category = "Properties", Property = "Floor" },
                    
                    // Русские названия
                    new { Category = "Идентификация", Property = "Уровень" },
                    new { Category = "Идентификация", Property = "Этаж" },
                    new { Category = "Элемент", Property = "Уровень" },
                    new { Category = "Элемент", Property = "Этаж" },
                    new { Category = "Объект", Property = "Уровень" },
                    new { Category = "Объект", Property = "Этаж" },
                    
                    // AutoCAD специфичные
                    new { Category = "AutoCAD", Property = "Layer" },
                    new { Category = "AutoCAD", Property = "Level" },
                    
                    // Дополнительные варианты
                    new { Category = "Revit", Property = "Level" },
                    new { Category = "Revit", Property = "Base Level" },
                    new { Category = "Revit", Property = "Reference Level" }
                };

                // Поиск по точным совпадениям категория + свойство
                foreach (var pattern in levelSearchPatterns)
                {
                    try
                    {
                        DataProperty prop = item.PropertyCategories.FindPropertyByDisplayName(pattern.Category, pattern.Property);
                        if (prop != null)
                        {
                            string value = prop.Value?.ToDisplayString() ?? "";
                            if (IsValidLevelValue(value))
                            {
                                System.Diagnostics.Debug.WriteLine($"GridHelper GetLevelFromModelItem: Found {pattern.Category}.{pattern.Property} = {value}");
                                return value;
                            }
                        }
                    }
                    catch
                    {
                        // Continue searching
                    }
                }

                // Поиск по всем свойствам с ключевыми словами
                foreach (PropertyCategory cat in item.PropertyCategories)
                {
                    foreach (DataProperty prop in cat.Properties)
                    {
                        try
                        {
                            string displayName = prop.DisplayName ?? string.Empty;
                            string categoryName = cat.DisplayName ?? string.Empty;
                            
                            // Расширенные ключевые слова для поиска
                            if (displayName.IndexOf("Level", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                displayName.IndexOf("Этаж", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                displayName.IndexOf("Floor", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                displayName.IndexOf("Storey", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                displayName.IndexOf("Story", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                displayName.IndexOf("Уровень", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                displayName.IndexOf("Base Level", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                displayName.IndexOf("Reference Level", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                displayName.IndexOf("Базовый уровень", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                displayName.IndexOf("Этаж ссылки", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                string value = prop.Value?.ToDisplayString() ?? "";
                                if (IsValidLevelValue(value))
                                {
                                    System.Diagnostics.Debug.WriteLine($"GridHelper GetLevelFromModelItem: Found {categoryName}.{displayName} = {value}");
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
        /// Проверяет, является ли значение валидным названием уровня
        /// </summary>
        /// <param name="value">Значение для проверки</param>
        /// <returns>True, если значение валидно</returns>
        private static bool IsValidLevelValue(string value)
        {
            if (string.IsNullOrEmpty(value) || value == "N/A" || value == "—" || value == "null")
                return false;

            // Проверяем, что это не числовое значение координаты
            if (double.TryParse(value, out double num))
            {
                // Если это очень большое число, скорее всего это координата, а не номер этажа
                if (Math.Abs(num) > 1000)
                    return false;
            }

            // Проверяем длину - слишком длинные значения скорее всего не названия уровней
            if (value.Length > 50)
                return false;

            return true;
        }

        /// <summary>
        /// Получает уровень для коллизии (ClashResult) - основной метод
        /// </summary>
        /// <param name="clash">Коллизия</param>
        /// <returns>Название уровня или "—", если уровень не найден</returns>
        public static string GetLevelForClash(ClashResult clash)
        {
            if (clash == null) 
            {
                System.Diagnostics.Debug.WriteLine("GridHelper GetLevelForClash: clash is null");
                return "—";
            }

            try
            {
                // First priority: try COM API method (like Clash Detective)
                string comApiLevel = GetLevelUsingComApi(clash);
                if (!string.IsNullOrEmpty(comApiLevel) && comApiLevel != "—")
                {
                    System.Diagnostics.Debug.WriteLine($"GridHelper GetLevelForClash: Using COM API result: {comApiLevel}");
                    return comApiLevel;
                }

                // Second priority: try properties from model items
                string level1 = GetLevelFromModelItem(clash.CompositeItem1);
                string level2 = GetLevelFromModelItem(clash.CompositeItem2);

                if (!string.IsNullOrEmpty(level1) && level1 != "—")
                {
                    System.Diagnostics.Debug.WriteLine($"GridHelper GetLevelForClash: Found level from item1: {level1}");
                    return level1;
                }
                else if (!string.IsNullOrEmpty(level2) && level2 != "—")
                {
                    System.Diagnostics.Debug.WriteLine($"GridHelper GetLevelForClash: Found level from item2: {level2}");
                    return level2;
                }

                // Third priority: try elevation calculation
                string floorByElevation = GetFloorByElevation(clash);
                if (!string.IsNullOrEmpty(floorByElevation) && floorByElevation != "—")
                {
                    System.Diagnostics.Debug.WriteLine($"GridHelper GetLevelForClash: Using elevation calculation: {floorByElevation}");
                    return floorByElevation;
                }

                System.Diagnostics.Debug.WriteLine("GridHelper GetLevelForClash: No level found");
                return "—";
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GridHelper GetLevelForClash: Exception: {ex.Message}");
                return "—";
            }
        }

        /// <summary>
        /// Получает уровень для группы коллизий (ClashResultGroup) - основной метод
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
                // First priority: try COM API method (like Clash Detective)
                string comApiLevel = GetLevelUsingComApi(group);
                if (!string.IsNullOrEmpty(comApiLevel) && comApiLevel != "—")
                {
                    System.Diagnostics.Debug.WriteLine($"GridHelper GetLevelForGroup: Using COM API result: {comApiLevel}");
                    return comApiLevel;
                }

                // Second priority: try to get level from the first result in the group
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
