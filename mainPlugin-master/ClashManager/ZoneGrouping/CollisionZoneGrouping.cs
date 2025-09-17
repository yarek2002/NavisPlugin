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

        public void makeGroups(List<ClashTest> selectedClashTests)
        {
            // Проверка на null или пустой список тестов
            if (selectedClashTests == null || selectedClashTests.Count == 0)
            {
                return;
            }

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
            // Создаём копию результата и обнуляем GUID
            result = (ClashResult)result.CreateCopy();
            result.Guid = Guid.Empty;

            ClashResultGroup group;
            string zoneName;

            // Определяем зону для коллизии
            zoneName = GetZoneForClash(result);

            if (!GroupsByZone.TryGetValue(zoneName, out group))
            {
                group = new ClashResultGroup();
                group.DisplayName = "Zone_" + zoneName; // Добавлен префикс для новых групп
                GroupsByZone.Add(zoneName, group);
            }

            group.Children.Add(result);

            // Отладочное логирование
            Debug.WriteLine($"Добавлена коллизия в зону '{zoneName}'");
        }

        private string GetZoneForClash(ClashResult clash)
        {
            try
            {
                // Создаем временную группу для проверки зоны
                var tempGroup = new ClashResultGroup();
                tempGroup.Children.Add(clash);
                
                // Используем ZoneHelper для определения зоны
                string zoneName = zoneHelper.GetZoneForGroup(tempGroup);
                
                if (!string.IsNullOrEmpty(zoneName))
                {
                    return zoneName;
                }
                else
                {
                    return "Null";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка при определении зоны: {ex.Message}");
                return "Null";
            }
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
