using System;
using System.Collections.Generic;
using System.Linq;
using System.Text; // Для StringBuilder в отчёте
using System.Windows; // Для MessageBox
using System.Diagnostics; // Для Debug.WriteLine
using System.IO; // Для записи лога в файл
using Autodesk.Navisworks.Api; // Для Document, ClashTest и т.д.
using Autodesk.Navisworks.Api.Clash; // Для ClashResultGroup, ClashTest
using CollisionClusterPlugin; // Импорт пространства для Clustering (из CollisionClusterPlugin)

namespace CollisionGrouperPlugin // Или ваше основное пространство, чтобы интегрировать
{
    public class MagicWand
    {
        private Document doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
        private DocumentClash documentClash;
        private List<string> statuses = new List<string> { "Reviewed", "Approved", "Resolved" }; // Статусы для фильтрации (как в оригинале)

        public MagicWand()
        {
            documentClash = doc.GetClash();
        }

        // Основной метод: Обработка всех тестов
        public void ProcessAllTests()
        {
            ProcessAllTests(null); // Вызов overload с null для обработки всех
        }

        // Overload для обработки конкретного списка тестов (из UI)
        public void ProcessAllTests(List<ClashTest> selectedTests = null)
        {
            try
            {
                // Материализуем список тестов заранее, чтобы избежать disposed коллекции
                var allTests = documentClash.TestsData.Tests.OfType<ClashTest>().ToList();

                // Получаем тесты для обработки из кэшированного списка
                var testsToProcess = selectedTests ?? allTests.Where(test =>
                {
                    var ungroupedResults = test.Children.OfType<ClashResult>()
                        .Where(r => !statuses.Contains(r.Status.ToString()))
                        .ToList();
                    return ungroupedResults.Any();
                }).ToList();

                // Список для отчёта
                StringBuilder reportBuilder = new StringBuilder();
                reportBuilder.AppendLine("Отчёт по MagicWand:");
                reportBuilder.AppendLine("Имя теста\tНовых групп (фрагменты)\tКластеров (всего)");

                foreach (var test in testsToProcess)
                {
                    Debug.WriteLine($"Обработка теста: {test.DisplayName}");
                    LogToFile($"Обработка теста: {test.DisplayName}");

                    // Кэшируем имя, GUID и индекс теста заранее
                    string originalTestName = test.DisplayName;
                    Guid originalTestGuid = test.Guid;
                    int originalIndex = allTests.IndexOf(test);

                    // Лог кэшированных данных
                    LogToFile($"Кэшированные данные: Имя = {originalTestName}, GUID = {originalTestGuid}, Index = {originalIndex}");

                    // Шаг 1: Группировка по фрагментам
                    var grouper = new CollisionFragmentGrouping();
                    grouper.makeGroups(new List<ClashTest> { test });

                    // Перезагружаем коллекцию тестов после замены
                    documentClash = doc.GetClash();
                    allTests = documentClash.TestsData.Tests.OfType<ClashTest>().ToList();

                    // Шаг 2: Получаем обновлённый тест по имени или GUID
                    var updatedTest = allTests.FirstOrDefault(t => t.DisplayName == originalTestName || t.Guid == originalTestGuid);

                    // Если не найден по имени/GUID, пробуем по индексу
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

                    // Лог найденного теста
                    LogToFile($"Найден updatedTest: Имя = {updatedTest.DisplayName}, GUID = {updatedTest.Guid}");

                    var newGroups = updatedTest.Children.OfType<ClashResultGroup>()
                       .Where(g => g.DisplayName.StartsWith("Frag_") && g.DisplayName != "Frag_Null") // Фильтр по префиксу, исключая Frag_Null
                        .ToList();

                    // Лог количества новых групп
                    LogToFile($"Количество новых групп: {newGroups.Count}");
                    LogToFile($"Все группы в тесте:");
                    foreach (var g in updatedTest.Children.OfType<ClashResultGroup>())
                    {
                        LogToFile($"  Группа: '{g.DisplayName}', Статус: {g.Status}");
                    }

                    List<ClashResultGroup> allNewClusters = new List<ClashResultGroup>();
                    int clustersCount = 0;

                    // Шаг 3: Кластеризация для каждой группы (кроме Null)
                    foreach (var group in newGroups)
                    {
                        var clusterer = new Clustering();
                        // Вызываем overload MakeClusters с группой (обновлённый, возвращает список)
                        var newClusters = clusterer.MakeClusters(group);
                        allNewClusters.AddRange(newClusters);
                        
                        clustersCount += newClusters.Count;
                        foreach (var nullGroup in newGroups)
                        {
                            if (group.DisplayName == "Frag_Null")
                            {
                                group.Status = ClashResultStatus.Reviewed;
                                group.DisplayName = "Null";
                                LogToFile($"Установлен статус Reviewed для группы Null (не кластеризована)");
                            }
                        }
                        // Лог кластеризации группы
                        LogToFile($"Кластеризация группы: {group.DisplayName}. Создано кластеров: {newClusters.Count}");
                    }

                    // Шаг 4: Замена теста один раз после всей кластеризации
                    if (allNewClusters.Any())
                    {
                        // Очистка префикса из имён кластеров
                        foreach (var theCluster in allNewClusters)
                        {
                            if (theCluster.DisplayName == "Frag_Null")
                            {
                                theCluster.Status = ClashResultStatus.Reviewed;
                                theCluster.DisplayName = "Null";
                            }
                            else
                            {
                                theCluster.DisplayName = theCluster.DisplayName.Replace("Frag_", "");
                            }
                        }

                        // Собираем данные для сохранения: все группы кроме кластеризуемых, все results
                        List<ClashResultGroup> oldGroupsToKeep = updatedTest.Children.OfType<ClashResultGroup>()
                            .Where(g => !newGroups.Contains(g))
                            .Select(g =>
                            {
                                var copy = (ClashResultGroup)g.CreateCopy();
                                copy.Guid = Guid.Empty;
                                if (copy.DisplayName == "Frag_Null")
                                {
                                    copy.Status = ClashResultStatus.Reviewed; // Установка статуса в Reviewed
                                    copy.DisplayName = "Null"; // Очистка префикса для Null
                                    
                                }
                                return copy;
                            })
                            .ToList();

                        List<ClashResult> oldResults = updatedTest.Children.OfType<ClashResult>()
                            .Select(r =>
                            {
                                LogToFile($"Группы для сохранения (oldGroupsToKeep): {oldGroupsToKeep.Count}");
                                    foreach (var g in oldGroupsToKeep)
                                    {
                                        LogToFile($"  Сохраняемая группа: '{g.DisplayName}', Статус: {g.Status}");
                                    }
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

                            // Кэшируем новый тест
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

                // Вывод отчёта в конце
                MessageBox.Show(reportBuilder.ToString(), "Отчёт MagicWand");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка в MagicWand: {ex.Message}");
                Debug.WriteLine($"Исключение: {ex}");
                LogToFile($"Исключение: {ex.Message}\nStackTrace: {ex.StackTrace}");
            }
        }

        // Метод для поиска релевантных тестов (с негруппированными коллизиями в New/Active)
        private IEnumerable<ClashTest> GetAllRelevantTests()
        {
            var allTests = documentClash.TestsData.Tests.OfType<ClashTest>().ToList(); // Материализуем заранее
            foreach (var test in allTests)
            {
                var ungroupedResults = test.Children.OfType<ClashResult>()
                    .Where(r => !statuses.Contains(r.Status.ToString())) // Только New/Active (не в statuses)
                    .ToList();

                if (ungroupedResults.Any())
                {
                    yield return test;
                }
            }
        }

        // Метод для записи лога в файл (для анализа)
        private void LogToFile(string message)
        {
            string logPath = @"C:\temp\MagicWandLog.txt"; // Измените путь, если нужно
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