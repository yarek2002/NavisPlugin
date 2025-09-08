using Autodesk.Navisworks.Api.Clash;
using Autodesk.Navisworks.Api.Interop;
using Autodesk.Navisworks.Api.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using System.Windows;
using System.Diagnostics;

namespace CollisionClusterPlugin
{
    public class Clustering
    {
        public Document ActiveDocument { get; }
        public static Document oDoc = Autodesk.Navisworks.Api.Application.ActiveDocument;
        public static DocumentClash documentClash = oDoc.GetClash();
        private ClashResultGroup _selectedGroup;
        public List<string> statuses = new List<string> { "Reviewed", "Approved", "Resolved" };

        // Вспомогательная структура для группировки по парам файлов
        private struct FilePairGroup
        {
            public List<List<string>> SubPairs { get; set; } // Подсписок пар для этой группы файлов
            public List<int> OriginalIndices { get; set; }   // Оригинальные индексы в clashofGroup
        }

        public int MakeClusters()
        {
            LcClCurrentIssue instance = LcClCurrentIssue.GetInstance((LcOpState)oDoc.State);
            ClashTest currentTest = instance.GetCurrentTest();
            SavedItem issueAsSavedItem = LcClCurrentIssue.GetInstance((LcOpState)Autodesk.Navisworks.Api.Application.ActiveDocument.State).GetCurrentIssueAsSavedItem();
            // Добавленная проверка на null для безопасности
            if (issueAsSavedItem == null)
            {
                MessageBox.Show("Текущая группа не выбрана в Clash Detective.");
                return -1; // Код ошибки
            }
            List<List<string>> clashofGroup = new List<List<string>>();
            this._selectedGroup = issueAsSavedItem as ClashResultGroup;
            Guid currentGuid = _selectedGroup.Guid;
            ClashResultGroup selectGroup = GetClashResofCurrentGroup(currentGuid);
            List<ClashResultGroup> newGroups = new List<ClashResultGroup>();
            ClashTest testOfGroup = selectGroup.Parent as ClashTest;
            if (selectGroup != null)
            {
                // Сбор clashofGroup (как в оригинале)
                foreach (ClashResult children in selectGroup.Children)
                {
                    List<string> clashItems = new List<string>();
                    if (children.CompositeItem1 != null && children.CompositeItem2 != null)
                    {
                        ModelItem modIt1 = children.CompositeItem1;
                        ModelItem modIt2 = children.CompositeItem2;

                        string el1Id = GetElementId(modIt1);
                        string el2Id = GetElementId(modIt2);
                        string el1FileName = modIt1.PropertyCategories.FindPropertyByDisplayName("Элемент", "Файл источника").ToString().Split(':').Last();
                        string el2FileName = modIt2.PropertyCategories.FindPropertyByDisplayName("Элемент", "Файл источника").ToString().Split(':').Last();
                        string el1 = el1Id + "_" + el1FileName;
                        string el2 = el2Id + "_" + el2FileName;
                        clashItems.Add(el1);
                        clashItems.Add(el2);
                        clashofGroup.Add(clashItems);
                    }
                }

                // Первый этап: Группировка по уникальным парам файлов
                Dictionary<Tuple<string, string>, FilePairGroup> fileGroups = new Dictionary<Tuple<string, string>, FilePairGroup>();
                for (int index = 0; index < clashofGroup.Count; index++)
                {
                    var pair = clashofGroup[index];
                    if (pair.Count == 2)
                    {
                        // Извлекаем имена файлов из строк (после '_')
                        string file1 = pair[0].Split('_').Last();
                        string file2 = pair[1].Split('_').Last();
                        // Нормализуем пару (сортируем для симметрии)
                        var sortedPair = Tuple.Create(Math.Min(String.Compare(file1, file2), 0) == 0 ? file1 : file2, Math.Min(String.Compare(file1, file2), 0) == 0 ? file2 : file1);

                        if (!fileGroups.ContainsKey(sortedPair))
                        {
                            fileGroups[sortedPair] = new FilePairGroup
                            {
                                SubPairs = new List<List<string>>(),
                                OriginalIndices = new List<int>()
                            };
                        }
                        fileGroups[sortedPair].SubPairs.Add(pair);
                        fileGroups[sortedPair].OriginalIndices.Add(index); // Сохраняем глобальный индекс
                    }
                }

                // Второй этап: Кластеризация внутри каждой группы файлов
                foreach (var fileGroup in fileGroups.Values)
                {
                    if (fileGroup.SubPairs.Count < 2)
                    {
                        // Если меньше 2 пар, создаём одиночную группу без кластеризации
                        var singleGrouped = new List<List<int>> { fileGroup.OriginalIndices.Select((_, idx) => idx).ToList() };
                        newGroups.AddRange(GetNewCurrentGroup(singleGrouped, selectGroup, fileGroup.OriginalIndices));
                    }
                    else
                    {
                        // Вызываем стандартную кластеризацию на подсписке
                        List<List<int>> grouped = UnionPair.GroupPairsIndices(fileGroup.SubPairs);
                        // Добавляем результаты в newGroups
                        newGroups.AddRange(GetNewCurrentGroup(grouped, selectGroup, fileGroup.OriginalIndices));
                    }
                }
            }
            else
            {
                MessageBox.Show("это не группа");
            }
            List<ClashResult> oldRes = GetOldResultsGroup(testOfGroup, currentGuid).results.ToList();
            List<ClashResultGroup> oldResGroup = GetOldResultsGroup(testOfGroup, currentGuid).grups.ToList();
            ClashTest newTest = testOfGroup.CreateCopyWithoutChildren() as ClashTest;
            int i = documentClash.TestsData.Tests.IndexOf(testOfGroup);
            documentClash.TestsData.TestsReplaceWithCopy(i, newTest);

            foreach (ClashResultGroup theGroup in oldResGroup)
            {
                documentClash.TestsData.TestsAddCopy((GroupItem)documentClash.TestsData.Tests[i], theGroup);
            }
            foreach (ClashResult theGroup in oldRes)
            {
                documentClash.TestsData.TestsAddCopy((GroupItem)documentClash.TestsData.Tests[i], theGroup);
            }
            foreach (ClashResultGroup theGroup in newGroups)
            {
                documentClash.TestsData.TestsAddCopy((GroupItem)documentClash.TestsData.Tests[i], theGroup);
            }
            return 0;
        }

        public IEnumerable<ClashTest> GetClashTests()
        {
            var clashTests = new List<ClashTest>();
            if (oDoc != null)
            {
                //получаем коллекцию клеш тестов
                var clashTestCollection = documentClash.TestsData.Tests;

                //перебираем и добавляем их в список
                foreach (var test in clashTestCollection)
                {
                    if (test is ClashTest clashTest)
                    {
                        clashTests.Add(clashTest);
                    }
                }

            }
            return clashTests;
        }

        //Метод получения ID Элемента
        private string GetElementId(ModelItem modelItem)
        {
            DataProperty propertyByDisplayName = modelItem.PropertyCategories.FindPropertyByDisplayName("Объект", "Id");
            if ((NativeHandle)propertyByDisplayName != (NativeHandle)null)
                return propertyByDisplayName.Value.ToInt32().ToString();
            return (NativeHandle)modelItem.Parent != (NativeHandle)null ? this.GetElementId(modelItem.Parent) : (string)null;
        }

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

        public resultates GetOldResultsGroup(ClashTest clashTest, Guid currentGuid)
        {
            resultates clashResults = new resultates();
            foreach (ClashResultGroup result in clashTest.Children.OfType<ClashResultGroup>())
            {
                if (result.Guid != currentGuid)
                {
                    ClashResultGroup newresult = new ClashResultGroup();
                    newresult = (ClashResultGroup)result.CreateCopy();
                    newresult.Guid = Guid.Empty;
                    clashResults.grups.Add(newresult);
                }
            }
            foreach (ClashResult result in clashTest.Children.OfType<ClashResult>())
            {

                ClashResult newres = new ClashResult();
                newres = (ClashResult)result.CreateCopy();
                newres.Guid = Guid.Empty;
                clashResults.results.Add(newres);
            }
            return clashResults;
        }

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
        // Обновлённый метод GetNewCurrentGroup с дополнительным параметром originalIndices
        public List<ClashResultGroup> GetNewCurrentGroup(List<List<int>> grouped, ClashResultGroup selectGroup, List<int> originalIndices)
        {
            List<ClashResultGroup> result = new List<ClashResultGroup>();
            ClashResultGroup copyGroup = selectGroup.CreateCopy() as ClashResultGroup;
            foreach (List<int> GroupItem in grouped)
            {
                ClashResultGroup thegroup = new ClashResultGroup();
                thegroup.DisplayName = selectGroup.DisplayName + "_";
                foreach (int poz in GroupItem)
                {
                    // Используем originalIndices для глобального индекса в copyGroup.Children
                    int globalPoz = originalIndices[poz];
                    ClashResult posit = new ClashResult();
                    posit = copyGroup.Children[globalPoz].CreateCopy() as ClashResult;
                    thegroup.Children.Add(posit);
                }
                result.Add(thegroup);
            }
            return result;
        }
        public ClashResultGroup GetClashResofCurrentGroup(Guid ClashGroupGuid)
        {
            var clashTests = new List<ClashTest>();
            clashTests = GetClashTests().ToList();
            foreach (ClashTest test in clashTests)
            {
                foreach (ClashResultGroup group in test.Children.OfType<ClashResultGroup>())
                {
                    if (group is ClashResultGroup)
                    {
                        if (group.Guid == ClashGroupGuid)
                        {
                            return group;
                        }
                    }
                }
            }
            return null;
        }

        // Overload для явной передачи группы (для MagicWand) - модифицировано: возвращает список новых групп вместо замены теста
        public List<ClashResultGroup> MakeClusters(ClashResultGroup selectedGroup)
        {
            if (selectedGroup == null) throw new ArgumentNullException(nameof(selectedGroup));
            this._selectedGroup = selectedGroup; // Устанавливаем группу

            // Дублируем логику оригинального метода, пропуская LcClCurrentIssue
            Guid currentGuid = _selectedGroup.Guid;
            ClashResultGroup selectGroup = _selectedGroup; // Используем переданную группу напрямую
            List<ClashResultGroup> newGroups = new List<ClashResultGroup>();
            ClashTest testOfGroup = selectGroup.Parent as ClashTest;
            if (selectGroup != null)
            {
                List<List<string>> clashofGroup = new List<List<string>>();
                // Сбор clashofGroup (как в оригинале)
                foreach (ClashResult children in selectGroup.Children)
                {
                    List<string> clashItems = new List<string>();
                    if (children.CompositeItem1 != null && children.CompositeItem2 != null)
                    {
                        ModelItem modIt1 = children.CompositeItem1;
                        ModelItem modIt2 = children.CompositeItem2;

                        string el1Id = GetElementId(modIt1);
                        string el2Id = GetElementId(modIt2);
                        string el1FileName = modIt1.PropertyCategories.FindPropertyByDisplayName("Элемент", "Файл источника").ToString().Split(':').Last();
                        string el2FileName = modIt2.PropertyCategories.FindPropertyByDisplayName("Элемент", "Файл источника").ToString().Split(':').Last();
                        string el1 = el1Id + "|" + el1FileName;
                        string el2 = el2Id + "|" + el2FileName;
                        clashItems.Add(el1);
                        clashItems.Add(el2);
                        clashofGroup.Add(clashItems);
                    }
                }

                // Первый этап: Группировка по уникальным парам файлов
                Dictionary<Tuple<string, string>, FilePairGroup> fileGroups = new Dictionary<Tuple<string, string>, FilePairGroup>();
                for (int index = 0; index < clashofGroup.Count; index++)
                {
                    var pair = clashofGroup[index];
                    if (pair.Count == 2)
                    {
                        string file1 = pair[0].Split('|').Last();
                        string file2 = pair[1].Split('|').Last();
                        var sortedPair = Tuple.Create(Math.Min(String.Compare(file1, file2), 0) == 0 ? file1 : file2, Math.Min(String.Compare(file1, file2), 0) == 0 ? file2 : file1);

                        if (!fileGroups.ContainsKey(sortedPair))
                        {
                            fileGroups[sortedPair] = new FilePairGroup
                            {
                                SubPairs = new List<List<string>>(),
                                OriginalIndices = new List<int>()
                            };
                        }
                        fileGroups[sortedPair].SubPairs.Add(pair);
                        fileGroups[sortedPair].OriginalIndices.Add(index);
                    }
                }

                // Второй этап: Кластеризация внутри каждой группы файлов
                foreach (var fileGroup in fileGroups.Values)
                {
                    if (fileGroup.SubPairs.Count < 2)
                    {
                        var singleGrouped = new List<List<int>> { fileGroup.OriginalIndices.Select((_, idx) => idx).ToList() };
                        newGroups.AddRange(GetNewCurrentGroup(singleGrouped, selectGroup, fileGroup.OriginalIndices));
                    }
                    else
                    {
                        List<List<int>> grouped = UnionPair.GroupPairsIndices(fileGroup.SubPairs);
                        newGroups.AddRange(GetNewCurrentGroup(grouped, selectGroup, fileGroup.OriginalIndices));
                    }
                }
            }
            else
            {
                MessageBox.Show("это не группа");
                return new List<ClashResultGroup>(); // Возвращаем пустой список при ошибке
            }

            // Удалена часть с получением oldRes, oldResGroup и заменой теста - теперь просто возвращаем newGroups
            return newGroups;
        }
    }
}