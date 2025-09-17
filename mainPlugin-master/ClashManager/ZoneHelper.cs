// ZoneHelper.cs - общий класс для работы с зонами
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;

namespace ClashManager
{
    /// <summary>
    /// Класс для работы с зонами
    /// </summary>
    public class ZoneHelper
    {
        private Document _doc;
        private Model _selectedZoneModel;

        public ZoneHelper()
        {
            _doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
            LoadSelectedZoneModel();
        }

        private void LogToFile(string message)
        {
            string logPath = @"C:\temp\ZoneHelperDebug.txt";
            try
            {
                System.IO.File.AppendAllText(logPath, $"{DateTime.Now:HH:mm:ss.fff}: {message}{Environment.NewLine}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка записи лога: {ex.Message}");
            }
        }

        /// <summary>
        /// Загружает выбранную модель с зонами из настроек
        /// </summary>
        private void LoadSelectedZoneModel()
        {
            try
            {
                string selectedFileName = Properties.Settings.Default.SelectedZoneNwcFile;
                if (!string.IsNullOrEmpty(selectedFileName))
                {
                    _selectedZoneModel = _doc.Models.FirstOrDefault(m => m.FileName == selectedFileName);
                }
            }
            catch
            {
                // Игнорируем ошибки загрузки настроек
            }
        }

        /// <summary>
        /// Проверяет, есть ли выбранная модель с зонами
        /// </summary>
        public bool HasSelectedZoneModel()
        {
            return _selectedZoneModel != null;
        }

        /// <summary>
        /// Находит зоны в выбранной модели
        /// </summary>
        public List<ZoneItem> GetZones()
        {
            // Очищаем лог файл в начале
            string logPath = @"C:\temp\ZoneHelperDebug.txt";
            try
            {
                System.IO.File.WriteAllText(logPath, $"=== НАЧАЛО ЗАГРУЗКИ ЗОН {DateTime.Now} ==={Environment.NewLine}");
            }
            catch { }

            if (_selectedZoneModel == null) 
            {
                LogToFile("ZoneHelper: _selectedZoneModel == null");
                return new List<ZoneItem>();
            }
            
            LogToFile($"ZoneHelper: Загружаем зоны из модели: {_selectedZoneModel.FileName}");
            var zones = FindZonesInModel(_selectedZoneModel.RootItem);
            LogToFile($"ZoneHelper: Найдено зон: {zones.Count}");
            
            foreach (var zone in zones)
            {
                LogToFile($"ZoneHelper: Зона '{zone.ZoneName}' - Box: Min({zone.BoundingBox.Min.X:F2}, {zone.BoundingBox.Min.Y:F2}, {zone.BoundingBox.Min.Z:F2}) Max({zone.BoundingBox.Max.X:F2}, {zone.BoundingBox.Max.Y:F2}, {zone.BoundingBox.Max.Z:F2})");
            }
            
            return zones;
        }

        /// <summary>
        /// Находит зоны в указанной модели
        /// </summary>
        private List<ZoneItem> FindZonesInModel(ModelItem rootItem)
        {
            var zones = new List<ZoneItem>();
            try
            {
                LogToFile($"FindZonesInModel: Начинаем поиск зон в корневом элементе: {rootItem?.DisplayName ?? "null"}");
                
                var zoneCandidates = FindZoneCandidates(rootItem);
                LogToFile($"FindZonesInModel: Найдено кандидатов в зоны: {zoneCandidates.Count}");

                foreach (var item in zoneCandidates)
                {
                    LogToFile($"FindZonesInModel: Обрабатываем кандидата: {item?.DisplayName ?? "null"}");
                    
                    var boundingBox = GetBoundingBox(item);
                    LogToFile($"FindZonesInModel: BoundingBox: Min({boundingBox.Min.X:F2}, {boundingBox.Min.Y:F2}, {boundingBox.Min.Z:F2}) Max({boundingBox.Max.X:F2}, {boundingBox.Max.Y:F2}, {boundingBox.Max.Z:F2})");
                    
                    if (boundingBox.Min != boundingBox.Max)
                    {
                        var zoneName = GenerateZoneName(item);
                        LogToFile($"FindZonesInModel: Создаем зону с именем: '{zoneName}'");
                        
                        zones.Add(new ZoneItem
                        {
                            ZoneName = zoneName,
                            ZoneObject = item,
                            BoundingBox = boundingBox
                        });

                        if (zones.Count >= 100) break;
                    }
                    else
                    {
                        LogToFile($"FindZonesInModel: Пропускаем элемент - пустой BoundingBox");
                    }
                }
                
                LogToFile($"FindZonesInModel: Итого найдено зон: {zones.Count}");
            }
            catch (Exception ex)
            {
                LogToFile($"FindZonesInModel: Ошибка при поиске зон: {ex.Message}");
            }
            
            return zones;
        }

        /// <summary>
        /// Проверяет, находится ли группа коллизий в зоне
        /// </summary>
        public string GetZoneForGroup(ClashResultGroup group)
        {
            if (_selectedZoneModel == null) return null;

            var zones = GetZones();
            var allResults = GetAllResultsFromGroup(group);

            foreach (var result in allResults)
            {
                foreach (var zone in zones)
                {
                    if (IsClashInsideZone(result, zone.BoundingBox))
                    {
                        return zone.ZoneName;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Применяет зонирование к группе коллизий
        /// </summary>
        public void ApplyZoneToGroup(ClashResultGroup group)
        {
            var zoneName = GetZoneForGroup(group);
            if (!string.IsNullOrEmpty(zoneName))
            {
                string currentName = group.DisplayName ?? "";
                if (!currentName.Contains(zoneName))
                {
                    group.DisplayName = string.IsNullOrEmpty(currentName)
                        ? zoneName
                        : $"{zoneName} | {currentName}";
                }
            }
        }

        // Остальные вспомогательные методы из ZoneAssignmentView...
        private List<ModelItem> FindZoneCandidates(ModelItem rootItem)
        {
            var candidates = new List<ModelItem>();
            LogToFile($"FindZoneCandidates: Начинаем поиск кандидатов в зоны");
            TraverseModel(rootItem, candidates, 15, 0); // Увеличиваем глубину с 5 до 15
            LogToFile($"FindZoneCandidates: Найдено кандидатов: {candidates.Count}");
            return candidates;
        }

        private void TraverseModel(ModelItem item, List<ModelItem> candidates, int maxDepth = 10, int currentDepth = 0)
        {
            if (item == null || currentDepth >= maxDepth) 
            {
                if (item == null) LogToFile($"TraverseModel: item == null на глубине {currentDepth}");
                if (currentDepth >= maxDepth) LogToFile($"TraverseModel: достигнута максимальная глубина {maxDepth}");
                return;
            }

            try
            {
                LogToFile($"TraverseModel: Обрабатываем элемент '{item.DisplayName}' на глубине {currentDepth}, Geometry: {item.Geometry != null}, IsHidden: {item.IsHidden}");
                
                if (item.Geometry != null)
                {
                    if (!item.IsHidden)
                    {
                        candidates.Add(item);
                        LogToFile($"TraverseModel:  НАЙДЕН КАНДИДАТ С ГЕОМЕТРИЕЙ: '{item.DisplayName}' на глубине {currentDepth}, всего кандидатов: {candidates.Count}");
                        if (candidates.Count >= 1000) return;
                    }
                    else
                    {
                        LogToFile($"TraverseModel:  Элемент '{item.DisplayName}' имеет геометрию, но скрыт - добавляем как кандидата");
                        candidates.Add(item);
                        if (candidates.Count >= 1000) return;
                    }
                }
                else
                {
                    LogToFile($"TraverseModel:  Элемент '{item.DisplayName}' не имеет геометрии");
                }
            }
            catch (Exception ex)
            {
                LogToFile($"TraverseModel: Ошибка при обработке элемента '{item?.DisplayName}': {ex.Message}");
            }

            try
            {
                // Проверяем, что Children является коллекцией
                if (item.Children is System.Collections.IEnumerable childrenEnumerable)
                {
                    int childCount = 0;
                    foreach (var child in childrenEnumerable)
                    {
                        childCount++;
                    }
                    LogToFile($"TraverseModel: У элемента '{item.DisplayName}' {childCount} дочерних элементов");
                    
                    foreach (var child in childrenEnumerable)
                    {
                        if (child is ModelItem childItem)
                        {
                            TraverseModel(childItem, candidates, maxDepth, currentDepth + 1);
                            if (candidates.Count >= 1000) return;
                        }
                    }
                }
                else
                {
                    LogToFile($"TraverseModel: Элемент '{item.DisplayName}' не имеет дочерних элементов или Children не является коллекцией");
                }
            }
            catch (Exception ex)
            {
                LogToFile($"TraverseModel: Ошибка при обходе дочерних элементов '{item?.DisplayName}': {ex.Message}");
            }
        }

        private BoundingBox3D GetBoundingBox(ModelItem item)
        {
            try
            {
                if (item?.Geometry != null)
                {
                    return item.Geometry.BoundingBox;
                }
                return new BoundingBox3D();
            }
            catch
            {
                return new BoundingBox3D();
            }
        }

        private string GenerateZoneName(ModelItem item)
        {
            try
            {
                string comment = GetParameterValue(item, "Комментарии");
                LogToFile($"GenerateZoneName: Элемент DisplayName='{item.DisplayName}', ClassDisplayName='{item.ClassDisplayName}', комментарий: '{comment}'");
                
                if (!string.IsNullOrEmpty(comment))
                {
                    LogToFile($"GenerateZoneName: Используем комментарий: '{comment}'");
                    return comment;
                }
                else if (!string.IsNullOrEmpty(item.ClassDisplayName))
                {
                    LogToFile($"GenerateZoneName: Используем ClassDisplayName: '{item.ClassDisplayName}'");
                    return item.ClassDisplayName;
                }
                else if (!string.IsNullOrEmpty(item.DisplayName))
                {
                    var result = System.IO.Path.GetFileNameWithoutExtension(item.DisplayName);
                    LogToFile($"GenerateZoneName: Используем DisplayName: '{result}'");
                    return result;
                }
                else
                {
                    // Используем координаты BoundingBox для создания уникального имени
                    var bbox = GetBoundingBox(item);
                    var result = $"Zone_{bbox.Min.X:F0}_{bbox.Min.Y:F0}_{bbox.Min.Z:F0}";
                    LogToFile($"GenerateZoneName: Используем координаты: '{result}'");
                    return result;
                }
            }
            catch (Exception ex)
            {
                var bbox = GetBoundingBox(item);
                var result = $"Zone_{bbox.Min.X:F0}_{bbox.Min.Y:F0}_{bbox.Min.Z:F0}";
                LogToFile($"GenerateZoneName: Ошибка, используем координаты: '{result}', ошибка: {ex.Message}");
                return result;
            }
        }

        private string GetParameterValue(ModelItem item, string parameterName)
        {
            try
            {
                if (item == null) return null;

                foreach (var category in item.PropertyCategories)
                {
                    if (category == null) continue;

                    var property = category.Properties.FirstOrDefault(p =>
                        p != null &&
                        p.DisplayName != null &&
                        p.DisplayName.Contains(parameterName));

                    if (property?.Value != null)
                    {
                        return property.Value.ToString();
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private bool IsClashInsideZone(ClashResult clash, BoundingBox3D zoneBox)
        {
            try
            {
                // Сначала попробуем использовать центр коллизии напрямую
                Point3D centerPoint;
                
                try
                {
                    centerPoint = clash.Center;
                    LogToFile($"Используем clash.Center: ({centerPoint.X:F2}, {centerPoint.Y:F2}, {centerPoint.Z:F2})");
                }
                catch
                {
                    // Если clash.Center не работает, вычисляем центр через BoundingBox элементов
                    var item1 = clash.CompositeItem1;
                    var item2 = clash.CompositeItem2;

                    var box1 = GetBoundingBox(item1);
                    var box2 = GetBoundingBox(item2);

                    if (box1.Min != box1.Max && box2.Min != box2.Max)
                    {
                        var centerX = (box1.Min.X + box1.Max.X + box2.Min.X + box2.Max.X) / 4;
                        var centerY = (box1.Min.Y + box1.Max.Y + box2.Min.Y + box2.Max.Y) / 4;
                        var centerZ = (box1.Min.Z + box1.Max.Z + box2.Min.Z + box2.Max.Z) / 4;

                        centerPoint = new Point3D(centerX, centerY, centerZ);
                        LogToFile($"Вычислили центр через BoundingBox: ({centerX:F2}, {centerY:F2}, {centerZ:F2})");
                    }
                    else
                    {
                        LogToFile("Не удалось получить координаты коллизии");
                        return false;
                    }
                }
                
                // Проверяем, что координаты не являются дефолтными
                if (Math.Abs(centerPoint.X - 0.5) < 0.01 && Math.Abs(centerPoint.Y - 0.5) < 0.01 && Math.Abs(centerPoint.Z - 0.5) < 0.01)
                {
                    LogToFile("ВНИМАНИЕ: Центр коллизии имеет дефолтные координаты (0.5, 0.5, 0.5)");
                    return false;
                }
                
                LogToFile($"Зона Box: Min({zoneBox.Min.X:F2}, {zoneBox.Min.Y:F2}, {zoneBox.Min.Z:F2}) Max({zoneBox.Max.X:F2}, {zoneBox.Max.Y:F2}, {zoneBox.Max.Z:F2})");
                
                bool isInside = IsPointInsideBox(centerPoint, zoneBox);
                LogToFile($"Коллизия внутри зоны: {isInside}");
                
                return isInside;
            }
            catch (Exception ex)
            {
                LogToFile($"Ошибка в IsClashInsideZone: {ex.Message}");
                return false;
            }
        }

        private bool IsPointInsideBox(Point3D point, BoundingBox3D box)
        {
            return point.X >= box.Min.X && point.X <= box.Max.X &&
                   point.Y >= box.Min.Y && point.Y <= box.Max.Y &&
                   point.Z >= box.Min.Z && point.Z <= box.Max.Z;
        }

        private List<ClashResult> GetAllResultsFromGroup(ClashResultGroup group)
        {
            var allResults = new List<ClashResult>();

            foreach (var result in group.Children.OfType<ClashResult>())
            {
                allResults.Add(result);
            }

            foreach (var childGroup in group.Children.OfType<ClashResultGroup>())
            {
                allResults.AddRange(GetAllResultsFromGroup(childGroup));
            }

            return allResults;
        }
    }

    /// <summary>
    /// Класс для представления зоны
    /// </summary>
    public class ZoneItem
    {
        public string ZoneName { get; set; }
        public ModelItem ZoneObject { get; set; }
        public BoundingBox3D BoundingBox { get; set; }
    }
}
