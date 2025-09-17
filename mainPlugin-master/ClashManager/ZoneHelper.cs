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
            if (_selectedZoneModel == null) return new List<ZoneItem>();
            
            return FindZonesInModel(_selectedZoneModel.RootItem);
        }

        /// <summary>
        /// Находит зоны в указанной модели
        /// </summary>
        private List<ZoneItem> FindZonesInModel(ModelItem rootItem)
        {
            var zones = new List<ZoneItem>();
            try
            {
                var zoneCandidates = FindZoneCandidates(rootItem);

                foreach (var item in zoneCandidates)
                {
                    var boundingBox = GetBoundingBox(item);
                    if (boundingBox.Min != boundingBox.Max)
                    {
                        var zoneName = GenerateZoneName(item);
                        zones.Add(new ZoneItem
                        {
                            ZoneName = zoneName,
                            ZoneObject = item,
                            BoundingBox = boundingBox
                        });

                        if (zones.Count >= 100) break;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error finding zones: {ex.Message}");
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
            TraverseModel(rootItem, candidates, 5, 0);
            return candidates;
        }

        private void TraverseModel(ModelItem item, List<ModelItem> candidates, int maxDepth = 10, int currentDepth = 0)
        {
            if (item == null || currentDepth >= maxDepth) return;

            try
            {
                if (item.Geometry != null && !item.IsHidden)
                {
                    candidates.Add(item);
                    if (candidates.Count >= 1000) return;
                }
            }
            catch { }

            foreach (var child in item.Children)
            {
                TraverseModel(child, candidates, maxDepth, currentDepth + 1);
                if (candidates.Count >= 1000) return;
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
                string comment = GetParameterValue(item, "Комментарий");

                if (!string.IsNullOrEmpty(comment))
                {
                    return comment;
                }
                else
                {
                    var displayName = item.DisplayName ?? "Unknown";
                    return System.IO.Path.GetFileNameWithoutExtension(displayName);
                }
            }
            catch
            {
                var displayName = item.DisplayName ?? "Unknown";
                return System.IO.Path.GetFileNameWithoutExtension(displayName);
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
                var item1 = clash.CompositeItem1;
                var item2 = clash.CompositeItem2;

                var box1 = GetBoundingBox(item1);
                var box2 = GetBoundingBox(item2);

                if (box1.Min != box1.Max && box2.Min != box2.Max)
                {
                    var centerX = (box1.Min.X + box1.Max.X + box2.Min.X + box2.Max.X) / 4;
                    var centerY = (box1.Min.Y + box1.Max.Y + box2.Min.Y + box2.Max.Y) / 4;
                    var centerZ = (box1.Min.Z + box1.Max.Z + box2.Min.Z + box2.Max.Z) / 4;

                    var centerPoint = new Point3D(centerX, centerY, centerZ);
                    return IsPointInsideBox(centerPoint, zoneBox);
                }

                return false;
            }
            catch
            {
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
