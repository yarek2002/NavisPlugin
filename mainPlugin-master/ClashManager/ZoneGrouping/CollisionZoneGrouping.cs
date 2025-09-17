using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;
using Autodesk.Navisworks.Api.Clash;
using Autodesk.Navisworks.Api.DocumentParts;
using System.Windows;
using System.Diagnostics;
using ClashManager;

namespace CollisionGrouperPlugin
{
    public class CollisionZoneGrouping
    {
        private Document doc;
        private DocumentClash documentClash;
        private DocumentClashTests clashTests;
        private ZoneHelper zoneHelper;
        private Dictionary<string, ClashResultGroup> GroupsByZone = new Dictionary<string, ClashResultGroup>();
        private List<ClashResultGroup> ResultGroups = new List<ClashResultGroup>();
        public List<string> statuses = new List<string> { "Reviewed", "Approved", "Resolved" };

        public CollisionZoneGrouping()
        {
            doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
            documentClash = doc.GetClash();
            clashTests = documentClash.TestsData;
            zoneHelper = new ZoneHelper();

            // Проверка на наличие зон
            if (!zoneHelper.HasSelectedZoneModel())
            {
                MessageBox.Show("Предупреждение: Модель с зонами не выбрана. Группировка может быть ограничена.");
            }
        }

        private void LogToFile(string message)
        {
            string logPath = @"C:\temp\ZoneGroupingDebug.txt";
            try
            {
                System.IO.File.AppendAllText(logPath, $"{DateTime.Now:HH:mm:ss.fff}: {message}{Environment.NewLine}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка записи лога: {ex.Message}");
            }
        }

        public void makeGroups(List<ClashTest> selectedClashTests)
        {
            // Очищаем лог файл в начале
            string logPath = @"C:\temp\ZoneGroupingDebug.txt";
            try
            {
                System.IO.File.WriteAllText(logPath, $"=== НАЧАЛО ГРУППИРОВКИ ПО ЗОНАМ {DateTime.Now} ==={Environment.NewLine}");
            }
            catch { }

            // Проверка на null или пустой список тестов
            if (selectedClashTests == null || selectedClashTests.Count == 0)
            {
                LogToFile("ОШИБКА: selectedClashTests == null или пустой список");
                return;
            }

            LogToFile($"Начинаем группировку для {selectedClashTests.Count} тестов");

            // Список для хранения отчётов по тестам
            List<Tuple<string, int, int>> reports = new List<Tuple<string, int, int>>();

            foreach (ClashTest test in selectedClashTests)
            {
                GroupsByZone.Clear();
                var report = Execute(test);
                reports.Add(report);
            }

            // Построение табличного отчёта
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Имя теста\tНовых групп\tКол-во в Null");
            foreach (var r in reports)
            {
                sb.AppendLine($"{r.Item1}\t{r.Item2}\t{r.Item3}");
            }
        }

        public Tuple<string, int, int> Execute(ClashTest selectedClashTest)
        {
            // Кэшируем имя теста заранее
            string testName = selectedClashTest?.DisplayName ?? "Unknown";

            List<ClashResult> clashResofSelectedTest = GetClashResultsFromTest(selectedClashTest, statuses).ToList();
            List<ClashResultGroup> oldResGroup = GetOldResultsGroup(selectedClashTest, statuses).grups.ToList();
            List<ClashResult> oldRes = GetOldResultsGroup(selectedClashTest, statuses).results.ToList();
            ClashTest newTest = selectedClashTest.CreateCopyWithoutChildren() as ClashTest;
            int i = documentClash.TestsData.Tests.IndexOf(selectedClashTest);
            documentClash.TestsData.TestsReplaceWithCopy(i, newTest);

            // Кэшируем текущий тест
            var currentTest = (GroupItem)documentClash.TestsData.Tests[i];

            if (selectedClashTest != null)
            {
                foreach (ClashResult result in clashResofSelectedTest)
                {
                    if (result != null)
                    {
                        GroupResult(result);
                    }
                    else
                    {
                        MessageBox.Show("нет результатов теста");
                    }
                }

                ResultGroups = GroupsByZone.Values.ToList();
                foreach (ClashResultGroup group in ResultGroups)
                {
                    documentClash.TestsData.TestsAddCopy(currentTest, group);
                }
                foreach (ClashResultGroup group in oldResGroup)
                {
                    documentClash.TestsData.TestsAddCopy(currentTest, group);
                }
                foreach (ClashResult result in oldRes)
                {
                    documentClash.TestsData.TestsAddCopy(currentTest, result);
                }

                // Подсчёт для отчёта
                int newGroupsCount = ResultGroups.Count;
                int nullCount = 0;
                var nullGroup = ResultGroups.FirstOrDefault(g => g.DisplayName == "Zone_Null");
                if (nullGroup != null)
                {
                    nullCount = nullGroup.Children.Count;
                }

                return new Tuple<string, int, int>(testName, newGroupsCount, nullCount);
            }
            else
            {
                MessageBox.Show("не нашел группу");
                return new Tuple<string, int, int>(testName, 0, 0);
            }
        }

        public void GroupResult(ClashResult result)
        {
            try
            {
                // Определяем зону для коллизии (используем оригинальный результат)
                string zoneName = GetZoneForClash(result);

                ClashResultGroup group;
                if (!GroupsByZone.TryGetValue(zoneName, out group))
                {
                    group = new ClashResultGroup();
                    group.DisplayName = "Zone_" + zoneName; // Добавлен префикс для новых групп
                    GroupsByZone.Add(zoneName, group);
                }

                // Создаём копию результата и обнуляем GUID
                var resultCopy = (ClashResult)result.CreateCopy();
                resultCopy.Guid = Guid.Empty;

                group.Children.Add(resultCopy);

                // Отладочное логирование
                LogToFile($"Добавлена коллизия в зону '{zoneName}'");
            }
            catch (Exception ex)
            {
                LogToFile($"Ошибка при группировке результата: {ex.Message}");
            }
        }

        private string GetZoneForClash(ClashResult clash)
        {
            try
            {
                // Получаем зоны из ZoneHelper
                var zones = zoneHelper.GetZones();
                LogToFile($"Найдено зон: {zones.Count}");
                
                // Проверяем, в какой зоне находится коллизия
                foreach (var zone in zones)
                {
                    LogToFile($"Проверяем зону: {zone.ZoneName}");
                    if (IsClashInsideZone(clash, zone.BoundingBox))
                    {
                        LogToFile($"Коллизия попала в зону: {zone.ZoneName}");
                        return zone.ZoneName;
                    }
                }
                
                LogToFile("Коллизия не попала ни в одну зону, отправляем в Null");
                return "Null";
            }
            catch (Exception ex)
            {
                LogToFile($"Ошибка при определении зоны: {ex.Message}");
                return "Null";
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
                    
                    LogToFile($"Центр коллизии: ({centerX:F2}, {centerY:F2}, {centerZ:F2})");
                    LogToFile($"Зона Box: Min({zoneBox.Min.X:F2}, {zoneBox.Min.Y:F2}, {zoneBox.Min.Z:F2}) Max({zoneBox.Max.X:F2}, {zoneBox.Max.Y:F2}, {zoneBox.Max.Z:F2})");
                    
                    bool isInside = IsPointInsideBox(centerPoint, zoneBox);
                    LogToFile($"Коллизия внутри зоны: {isInside}");
                    
                    return isInside;
                }

                LogToFile("Один из элементов коллизии не имеет геометрии");
                return false;
            }
            catch (Exception ex)
            {
                LogToFile($"Ошибка в IsClashInsideZone: {ex.Message}");
                return false;
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

        private bool IsPointInsideBox(Point3D point, BoundingBox3D box)
        {
            return point.X >= box.Min.X && point.X <= box.Max.X &&
                   point.Y >= box.Min.Y && point.Y <= box.Max.Y &&
                   point.Z >= box.Min.Z && point.Z <= box.Max.Z;
        }

        // Метод получения списка клеш тестов
        public IEnumerable<ClashTest> GetClashTests()
        {
            var clashTestsList = new List<ClashTest>();
            if (doc != null)
            {
                var clashTestCollection = documentClash.TestsData.Tests;

                foreach (var test in clashTestCollection)
                {
                    if (test is ClashTest clashTest)
                    {
                        clashTestsList.Add(clashTest);
                    }
                }
            }
            return clashTestsList;
        }

        // Метод получения списка несгруппированных клешей выбранного клеш теста
        public IEnumerable<ClashResult> GetClashResultsFromTest(ClashTest clashTest, List<string> statuses)
        {
            List<ClashResult> clashResults = new List<ClashResult>();

            foreach (ClashResult result in clashTest.Children.OfType<ClashResult>())
            {
                if (!statuses.Contains(result.Status.ToString()))
                {
                    clashResults.Add(result);
                }
            }
            return clashResults;
        }

        public resultates GetOldResultsGroup(ClashTest clashTest, List<string> statuses)
        {
            resultates clashResults = new resultates();
            foreach (ClashResultGroup result in clashTest.Children.OfType<ClashResultGroup>())
            {
                ClashResultGroup newresult = new ClashResultGroup();
                newresult = (ClashResultGroup)result.CreateCopy();
                newresult.Guid = Guid.Empty;
                clashResults.grups.Add(newresult);
            }
            foreach (ClashResult result in clashTest.Children.OfType<ClashResult>())
            {
                if (statuses.Contains(result.Status.ToString()))
                {
                    ClashResult newres = new ClashResult();
                    newres = (ClashResult)result.CreateCopy();
                    newres.Guid = Guid.Empty;
                    clashResults.results.Add(newres);
                }
            }
            return clashResults;
        }

        // Класс resultates для совместимости
        public class resultates
        {
            public List<ClashResultGroup> grups { get; set; }
            public List<ClashResult> results { get; set; }
            public resultates()
            {
                grups = new List<ClashResultGroup>();
                results = new List<ClashResult>();
            }
        }
    }
}
