using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;

namespace ClashManager.ZoneAssignment.Views
{
    /// <summary>
    /// Класс для представления NWC файла
    /// </summary>
    public class NwcFileItem
    {
        public Model Model { get; set; }
        public string DisplayName => Model?.FileName ?? "Неизвестный файл";
        public List<ZoneItem> Zones { get; set; } = new List<ZoneItem>();
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

    public partial class ZoneAssignmentView : Window
    {
        private Document _doc;
        private DocumentClash _documentClash;
        private ObservableCollection<NwcFileItem> _nwcFiles = new ObservableCollection<NwcFileItem>();

        public ZoneAssignmentView()
        {
            InitializeComponent();
            _doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
            _documentClash = _doc.GetClash();

            NwcFilesListBox.ItemsSource = _nwcFiles;

            LoadNwcFiles();
        }

        /// <summary>
        /// Загружает список доступных NWC файлов
        /// </summary>
        private void LoadNwcFiles()
        {
            try
            {
                _nwcFiles.Clear();

                // Получаем все загруженные модели
                foreach (var model in _doc.Models)
                {
                    var nwcFile = new NwcFileItem
                    {
                        Model = model
                    };

                    // Автоматически находим зоны в этой модели
                    FindZonesInModel(nwcFile);

                    // Добавляем все файлы, независимо от наличия зон
                    _nwcFiles.Add(nwcFile);
                }

                if (_nwcFiles.Count == 0)
                {
                    MessageBox.Show("Не найдено загруженных NWC файлов.",
                                  "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при загрузке NWC файлов: {ex.Message}",
                              "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Находит зоны в указанной модели
        /// </summary>
        private void FindZonesInModel(NwcFileItem nwcFile)
        {
            try
            {
                var zoneCandidates = FindZoneCandidates(nwcFile.Model.RootItem);

                foreach (var item in zoneCandidates)
                {
                    var boundingBox = GetBoundingBox(item);
                    // Проверяем, что bounding box не пустой (Min != Max)
                    if (boundingBox.Min != boundingBox.Max)
                    {
                        var zoneName = GenerateZoneName(item);
                        nwcFile.Zones.Add(new ZoneItem
                        {
                            ZoneName = zoneName,
                            ZoneObject = item,
                            BoundingBox = boundingBox
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error finding zones in model: {ex.Message}");
            }
        }

        /// <summary>
        /// Генерирует название зоны на основе параметров ADSK_этаж и ADSK_зона
        /// </summary>
        private string GenerateZoneName(ModelItem item)
        {
            try
            {
                string floor = GetParameterValue(item, "ADSK_этаж");
                string zone = GetParameterValue(item, "ADSK_зона");

                // Если оба параметра найдены, комбинируем их
                if (!string.IsNullOrEmpty(floor) && !string.IsNullOrEmpty(zone))
                {
                    return $"{floor} | {zone}";
                }
                // Если только этаж
                else if (!string.IsNullOrEmpty(floor))
                {
                    return floor;
                }
                // Если только зона
                else if (!string.IsNullOrEmpty(zone))
                {
                    return zone;
                }
                // Если параметры не найдены, используем имя объекта как fallback
                else
                {
                    var displayName = item.DisplayName ?? "Unknown";
                    return System.IO.Path.GetFileNameWithoutExtension(displayName);
                }
            }
            catch
            {
                // В случае ошибки используем имя объекта
                var displayName = item.DisplayName ?? "Unknown";
                return System.IO.Path.GetFileNameWithoutExtension(displayName);
            }
        }

        /// <summary>
        /// Получает значение параметра из свойств объекта
        /// </summary>
        private string GetParameterValue(ModelItem item, string parameterName)
        {
            try
            {
                if (item == null) return null;

                // Ищем параметр во всех категориях свойств
                foreach (var category in item.PropertyCategories)
                {
                    if (category == null) continue;

                    // Ищем свойство по имени
                    var property = category.Properties.FirstOrDefault(p =>
                        p != null &&
                        p.DisplayName != null &&
                        p.DisplayName.Contains(parameterName));

                    if (property != null && property.Value != null)
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

        /// <summary>
        /// Ищет кандидаты на зоны в модели
        /// </summary>
        private List<ModelItem> FindZoneCandidates(ModelItem rootItem)
        {
            var candidates = new List<ModelItem>();
            TraverseModel(rootItem, candidates);
            return candidates;
        }

        /// <summary>
        /// Рекурсивно обходит модель для поиска зон
        /// </summary>
        private void TraverseModel(ModelItem item, List<ModelItem> candidates)
        {
            if (item == null) return;

            // Критерии для определения зоны:
            // Объекты с геометрией (bounding box)
            try
            {
                if (item.Geometry != null && !item.IsHidden)
                {
                    candidates.Add(item);
                }
            }
            catch
            {
                // Игнорируем ошибки при получении геометрии
            }

            // Продолжаем обход для дочерних элементов
            foreach (var child in item.Children)
            {
                TraverseModel(child, candidates);
            }
        }

        /// <summary>
        /// Получает bounding box для объекта
        /// </summary>
        private BoundingBox3D GetBoundingBox(ModelItem item)
        {
            try
            {
                if (item == null) return new BoundingBox3D(); // Возвращаем пустой bounding box

                // Получаем геометрию объекта
                var geometry = item.Geometry;
                if (geometry != null)
                {
                    return geometry.BoundingBox;
                }

                return new BoundingBox3D(); // Возвращаем пустой bounding box
            }
            catch
            {
                return new BoundingBox3D(); // Возвращаем пустой bounding box
            }
        }

        /// <summary>
        /// Проверяет, находится ли точка внутри bounding box
        /// </summary>
        private bool IsPointInsideBox(Point3D point, BoundingBox3D box)
        {
            return point.X >= box.Min.X && point.X <= box.Max.X &&
                   point.Y >= box.Min.Y && point.Y <= box.Max.Y &&
                   point.Z >= box.Min.Z && point.Z <= box.Max.Z;
        }

        /// <summary>
        /// Проверяет, находится ли коллизия внутри зоны
        /// </summary>
        private bool IsClashInsideZone(ClashResult clash, BoundingBox3D zoneBox)
        {
            try
            {
                // Получаем центр коллизии как среднюю точку между двумя элементами
                var item1 = clash.CompositeItem1;
                var item2 = clash.CompositeItem2;

                var box1 = GetBoundingBox(item1);
                var box2 = GetBoundingBox(item2);

                // Проверяем, что bounding box не пустой (пустой имеет Min == Max)
                if (box1.Min != box1.Max && box2.Min != box2.Max)
                {
                    // Вычисляем центр пересечения
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

        private void AssignZonesButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedNwcFile = NwcFilesListBox.SelectedItem as NwcFileItem;
            if (selectedNwcFile == null)
            {
                MessageBox.Show("Выберите NWC файл с зонами!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (selectedNwcFile.Zones.Count == 0)
            {
                MessageBox.Show("В выбранном файле не найдено зон!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_documentClash?.TestsData?.Tests == null)
            {
                MessageBox.Show("Не найдены тесты коллизий!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                int totalAssigned = 0;
                int zonesProcessed = 0;

                // Обрабатываем все тесты
                foreach (var test in _documentClash.TestsData.Tests.OfType<ClashTest>())
                {
                    var result = AssignZonesToTest(test, selectedNwcFile.Zones);
                    totalAssigned += result.Item1;
                    zonesProcessed += result.Item2;
                }

                MessageBox.Show($"Зонирование завершено!\nОбработано зон: {zonesProcessed}\nКоллизий с зонами: {totalAssigned}",
                              "Информация", MessageBoxButton.OK, MessageBoxImage.Information);

                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при зонировании: {ex.Message}",
                              "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Назначает зоны коллизиям в тесте
        /// </summary>
        private Tuple<int, int> AssignZonesToTest(ClashTest test, List<ZoneItem> zones)
        {
            int assignedCount = 0;
            HashSet<string> usedZones = new HashSet<string>();

            try
            {
                // Получаем все группы из теста
                var allGroups = GetAllGroupsFromTest(test);

                foreach (var group in allGroups)
                {
                    // Получаем все результаты из группы
                    var allResults = GetAllResultsFromGroup(group);

                    foreach (var result in allResults)
                    {
                        // Проверяем, в какой зоне находится эта коллизия
                        foreach (var zone in zones)
                        {
                            if (IsClashInsideZone(result, zone.BoundingBox))
                            {
                                // Добавляем название зоны к имени группы
                                string currentName = group.DisplayName ?? "";
                                if (!currentName.Contains(zone.ZoneName))
                                {
                                    group.DisplayName = string.IsNullOrEmpty(currentName)
                                        ? zone.ZoneName
                                        : $"{currentName} | {zone.ZoneName}";
                                    assignedCount++;
                                    usedZones.Add(zone.ZoneName);
                                }
                                break; // Назначаем только первую подходящую зону
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error assigning zones to test: {ex.Message}");
            }

            return new Tuple<int, int>(assignedCount, usedZones.Count);
        }

        /// <summary>
        /// Получает все группы из теста
        /// </summary>
        private List<ClashResultGroup> GetAllGroupsFromTest(ClashTest test)
        {
            var allGroups = new List<ClashResultGroup>();
            foreach (var group in test.Children.OfType<ClashResultGroup>())
            {
                allGroups.Add(group);
                allGroups.AddRange(GetAllGroupsFromGroup(group));
            }
            return allGroups;
        }

        /// <summary>
        /// Получает все группы из группы рекурсивно
        /// </summary>
        private List<ClashResultGroup> GetAllGroupsFromGroup(ClashResultGroup group)
        {
            var allGroups = new List<ClashResultGroup>();
            foreach (var childGroup in group.Children.OfType<ClashResultGroup>())
            {
                allGroups.Add(childGroup);
                allGroups.AddRange(GetAllGroupsFromGroup(childGroup));
            }
            return allGroups;
        }

        /// <summary>
        /// Получает все результаты из группы
        /// </summary>
        private List<ClashResult> GetAllResultsFromGroup(ClashResultGroup group)
        {
            var allResults = new List<ClashResult>();

            // Добавляем результаты из текущей группы
            foreach (var result in group.Children.OfType<ClashResult>())
            {
                allResults.Add(result);
            }

            // Рекурсивно добавляем результаты из вложенных групп
            foreach (var childGroup in group.Children.OfType<ClashResultGroup>())
            {
                allResults.AddRange(GetAllResultsFromGroup(childGroup));
            }

            return allResults;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
