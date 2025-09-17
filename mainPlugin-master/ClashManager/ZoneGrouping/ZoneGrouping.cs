using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Diagnostics;
using System.IO;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using CollisionClusterPlugin;
using ClashManager;

namespace CollisionGrouperPlugin
{
    public class ZoneGrouping
    {
        private Document doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
        private DocumentClash documentClash;
        private ZoneHelper zoneHelper;
        private List<string> statuses = new List<string> { "Reviewed", "Approved", "Resolved" };

        public ZoneGrouping()
        {
            documentClash = doc.GetClash();
            zoneHelper = new ZoneHelper();
        }

        // Основной метод: Обработка всех тестов
        public void ProcessAllTests()
        {
            ProcessAllTests(null);
        }

        // Overload для обработки конкретного списка тестов
        public void ProcessAllTests(List<ClashTest> selectedTests = null)
        {
            try
            {
                // Проверяем наличие зон
                if (!zoneHelper.HasSelectedZoneModel())
                {
                    MessageBox.Show("Не выбрана модель с зонами. Пожалуйста, выберите модель с зонами в настройках.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Материализуем список тестов заранее
                var allTests = documentClash.TestsData.Tests.OfType<ClashTest>().ToList();

                // Получаем тесты для обработки
                var testsToProcess = selectedTests ?? allTests.Where(test =>
                {
                    var ungroupedResults = test.Children.OfType<ClashResult>()
                        .Where(r => !statuses.Contains(r.Status.ToString()))
                        .ToList();
                    return ungroupedResults.Any();
                }).ToList();

                // Список для отчёта
                StringBuilder reportBuilder = new StringBuilder();
                reportBuilder.AppendLine("Отчёт по группировке по зонам:");
                reportBuilder.AppendLine("Имя теста\tНовых групп (зоны)\tКластеров (всего)");

                foreach (var test in testsToProcess)
                {
                    Debug.WriteLine($"Обработка теста: {test.DisplayName}");
                    LogToFile($"Обработка теста: {test.DisplayName}");

                    // Кэшируем данные теста
                    string originalTestName = test.DisplayName;
                    Guid originalTestGuid = test.Guid;
                    int originalIndex = allTests.IndexOf(test);

                    LogToFile($"Кэшированные данные: Имя = {originalTestName}, GUID = {originalTestGuid}, Index = {originalIndex}");

                    // Шаг 1: Группировка по зонам
                    var zoneGrouper = new CollisionZoneGrouping();
                    zoneGrouper.makeGroups(new List<ClashTest> { test });

                    // Перезагружаем коллекцию тестов после замены
                    documentClash = doc.GetClash();
                    allTests = documentClash.TestsData.Tests.OfType<ClashTest>().ToList();

                    // Шаг 2: Получаем обновлённый тест
                    var updatedTest = allTests.FirstOrDefault(t => t.DisplayName == originalTestName || t.Guid == originalTestGuid);

                    if (updatedTest == null && originalIndex >= 0 && originalIndex < allTests.Count)
                    {
                        updatedTest = allTests[originalIndex];
                    }

                    if (updatedTest == null)
                    {
                        Debug.WriteLine($"Тест {originalTestName} не найден после группировки.");
                        LogToFile($"Тест {originalTestName} не найден после группировки.");
                        continue;
                    }

                    LogToFile($"Найден updatedTest: Имя = {updatedTest.DisplayName}, GUID = {updatedTest.Guid}");

                    var newGroups = updatedTest.Children.OfType<ClashResultGroup>()
                        .Where(g => g.DisplayName.StartsWith("Zone_") && g.DisplayName != "Zone_Null")
                        .ToList();

                    LogToFile($"Количество новых групп: {newGroups.Count}");

                    List<ClashResultGroup> allNewClusters = new List<ClashResultGroup>();
                    int clustersCount = 0;

                    // Шаг 3: Кластеризация для каждой группы
                    foreach (var group in newGroups)
                    {
                        var clusterer = new Clustering();
                        var newClusters = clusterer.MakeClusters(group);
                        allNewClusters.AddRange(newClusters);
                        clustersCount += newClusters.Count;

                        LogToFile($"Кластеризация группы: {group.DisplayName}. Создано кластеров: {newClusters.Count}");
                    }

                    // Шаг 4: Замена теста после кластеризации
                    if (allNewClusters.Any())
                    {
                        // Очистка префикса из имён кластеров
                        foreach (var theCluster in allNewClusters)
                        {
                            theCluster.DisplayName = theCluster.DisplayName.Replace("Zone_", "");
                        }

                        // Собираем данные для сохранения
                        List<ClashResultGroup> oldGroupsToKeep = updatedTest.Children.OfType<ClashResultGroup>()
                            .Where(g => !newGroups.Contains(g))
                            .Select(g =>
                            {
                                var copy = (ClashResultGroup)g.CreateCopy();
                                copy.Guid = Guid.Empty;
                                if (copy.DisplayName == "Zone_Null")
                                {
                                    copy.DisplayName = "Null";
                                }
                                return copy;
                            })
                            .ToList();

                        List<ClashResult> oldResults = updatedTest.Children.OfType<ClashResult>()
                            .Select(r =>
                            {
                                var copy = (ClashResult)r.CreateCopy();
                                copy.Guid = Guid.Empty;
                                return copy;
                            })
                            .ToList();

                        // Создаём копию теста без детей
                        ClashTest newTest = updatedTest.CreateCopyWithoutChildren() as ClashTest;

                        // Находим индекс и заменяем
                        int i = documentClash.TestsData.Tests.IndexOf(updatedTest);
                        if (i >= 0)
                        {
                            documentClash.TestsData.TestsReplaceWithCopy(i, newTest);

                            var currentTest = (GroupItem)documentClash.TestsData.Tests[i];

                            // Добавляем сохранённые группы и results
                            foreach (ClashResultGroup theGroup in oldGroupsToKeep)
                            {
                                documentClash.TestsData.TestsAddCopy(currentTest, theGroup);
                            }
                            foreach (ClashResult theResult in oldResults)
                            {
                                documentClash.TestsData.TestsAddCopy(currentTest, theResult);
                            }
                            // Добавляем все новые кластеры
                            foreach (ClashResultGroup theCluster in allNewClusters)
                            {
                                documentClash.TestsData.TestsAddCopy(currentTest, theCluster);
                            }

                            LogToFile($"Тест {originalTestName} успешно заменён с новыми кластерами.");
                        }
                        else
                        {
                            LogToFile($"Индекс теста {originalTestName} не найден для замены.");
                        }
                    }

                    // Добавляем в отчёт
                    reportBuilder.AppendLine($"{originalTestName}\t{newGroups.Count}\t{clustersCount}");
                }

                // Вывод отчёта
                MessageBox.Show(reportBuilder.ToString(), "Отчёт по группировке по зонам");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка в группировке по зонам: {ex.Message}");
                Debug.WriteLine($"Исключение: {ex}");
                LogToFile($"Исключение: {ex.Message}\nStackTrace: {ex.StackTrace}");
            }
        }

        // Метод для записи лога в файл
        private void LogToFile(string message)
        {
            string logPath = @"C:\temp\ZoneGroupingLog.txt";
            try
            {
                File.AppendAllText(logPath, $"{DateTime.Now}: {message}{Environment.NewLine}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка записи лога: {ex.Message}");
            }
        }
    }
}
