using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Autodesk.Navisworks.Api;
using System.Xml.Linq; // Для парсинга XML
using Microsoft.Win32; // Для OpenFileDialog
using System.IO;
using System.IO;

namespace CollisionGrouperPlugin
{
    public partial class FindSearchSets : Window
    {
        private ModelItem _elem1;
        private ModelItem _elem2;
        private List<SelectionSet> setsForElem1;
        private List<SelectionSet> setsForElem2;
        private Dictionary<string, SelectionSet> allSetsByName; // Для поиска наборов по имени

        // Кэш для XML (статический, чтобы сохранялся между окнами)
        private static string lastXmlPath = null;
        private static List<ClashTestData> cachedClashTests = null;

        public FindSearchSets(ModelItem elem1, ModelItem elem2)
        {
            InitializeComponent();
            _elem1 = elem1;
            _elem2 = elem2;

            LoadSearchSets();
        }

        private void LoadSearchSets()
        {
            Document doc = Autodesk.Navisworks.Api.Application.ActiveDocument;

            // Собираем все поисковые наборы рекурсивно
            List<SelectionSet> allSelectionSets = GetAllSelectionSets(doc.SelectionSets.Value);

            // Обработка возможных дубликатов имен
            allSetsByName = allSelectionSets.GroupBy(s => s.DisplayName).ToDictionary(g => g.Key, g => g.First());

            setsForElem1 = new List<SelectionSet>();
            setsForElem2 = new List<SelectionSet>();

            foreach (SelectionSet selectionSet in allSelectionSets)
            {
                if (ContainsItem(selectionSet, _elem1, doc))
                {
                    setsForElem1.Add(selectionSet);
                }

                if (ContainsItem(selectionSet, _elem2, doc))
                {
                    setsForElem2.Add(selectionSet);
                }
            }

            ListBoxFirst.ItemsSource = setsForElem1;
            ListBoxSecond.ItemsSource = setsForElem2;

            // Если XML уже загружен, обновляем ListBoxClash
            if (cachedClashTests != null)
            {
                UpdateClashList();
                XmlStatusText.Text = $"XML загружен: {lastXmlPath}";
            }
        }

        // Обработчик кнопки загрузки XML
        private void LoadXmlButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "XML Files (*.xml)|*.xml",
                Title = "Выберите XML файл Clash"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                string filePath = openFileDialog.FileName;

                if (filePath == lastXmlPath && cachedClashTests != null)
                {
                    // Уже загружен, просто обновляем
                    UpdateClashList();
                    XmlStatusText.Text = $"XML уже загружен: {filePath}";
                    return;
                }

                try
                {
                    // Парсим XML
                    XDocument xdoc = XDocument.Load(filePath);
                    cachedClashTests = new List<ClashTestData>();

                    foreach (XElement clashTestElem in xdoc.Descendants("clashtest"))
                    {
                        string testName = clashTestElem.Attribute("name")?.Value ?? "Unknown Test";

                        var testData = new ClashTestData { Name = testName, Rules = new List<ClashRuleData>() };

                        foreach (XElement ruleElem in clashTestElem.Descendants("rule"))
                        {
                            string ruleName = ruleElem.Attribute("name")?.Value ?? "Unknown Rule";

                            var ruleData = new ClashRuleData { Name = ruleName };

                            var paramsElems = ruleElem.Descendants("ruleparam").ToList();
                            if (paramsElems.Count >= 2)
                            {
                                // Первый param — SetA
                                XElement nameElemA = paramsElems[0].Descendants("name").FirstOrDefault();
                                string fullNameA = nameElemA?.Value?.Trim() ?? "N/A";
                                ruleData.SetA = fullNameA.Split(new string[] { "->" }, StringSplitOptions.None).LastOrDefault()?.Trim() ?? "N/A";

                                // Второй param — SetB
                                XElement nameElemB = paramsElems[1].Descendants("name").FirstOrDefault();
                                string fullNameB = nameElemB?.Value?.Trim() ?? "N/A";
                                ruleData.SetB = fullNameB.Split(new string[] { "->" }, StringSplitOptions.None).LastOrDefault()?.Trim() ?? "N/A";
                            }

                            testData.Rules.Add(ruleData);
                        }

                        cachedClashTests.Add(testData);
                    }

                    lastXmlPath = filePath;
                    UpdateClashList();
                    XmlStatusText.Text = $"XML загружен: {filePath}";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка загрузки XML: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    XmlStatusText.Text = "Ошибка загрузки XML";
                }
            }
        }

        // Метод обновления ListBoxClash на основе кэша и текущих списков
        private void UpdateClashList()
        {
            if (cachedClashTests == null) return;

            var uniqueRules = new HashSet<string>(); // Для уникальности по "RuleName(SetA vs SetB)"
            var matchingRules = new List<ClashRuleMatch>();

            HashSet<string> elem1SetNames = new HashSet<string>(setsForElem1.Select(s => s.DisplayName.Trim()), StringComparer.OrdinalIgnoreCase); // Ignore case
            HashSet<string> elem2SetNames = new HashSet<string>(setsForElem2.Select(s => s.DisplayName.Trim()), StringComparer.OrdinalIgnoreCase);

            foreach (var test in cachedClashTests)
            {
                foreach (var rule in test.Rules)
                {
                    string setA = rule.SetA?.Trim();
                    string setB = rule.SetB?.Trim();

                    if (!string.IsNullOrEmpty(setA) && !string.IsNullOrEmpty(setB) &&
                        ((elem1SetNames.Contains(setA) && elem2SetNames.Contains(setB)) ||
                         (elem1SetNames.Contains(setB) && elem2SetNames.Contains(setA))))
                    {
                        // Нормализуем порядок SetA и SetB для уникальности (A vs B или B vs A — одно и то же)
                        string normalizedSets = string.Compare(setA, setB, StringComparison.OrdinalIgnoreCase) <= 0 ? $"{setA} vs {setB}" : $"{setB} vs {setA}";
                        string uniqueKey = $"{rule.Name}({normalizedSets})";

                        if (!uniqueRules.Contains(uniqueKey))
                        {
                            uniqueRules.Add(uniqueKey);
                            matchingRules.Add(new ClashRuleMatch
                            {
                                DisplayName = rule.Name,
                                SelectionA = setA,
                                SelectionB = setB
                            });
                        }
                    }
                }
            }

            ListBoxClash.ItemsSource = matchingRules;
        }

        // Рекурсивный метод для сбора всех SelectionSets, включая вложенные в папки
        private List<SelectionSet> GetAllSelectionSets(SavedItemCollection collection)
        {
            List<SelectionSet> sets = new List<SelectionSet>();
            foreach (SavedItem item in collection)
            {
                if (item is SelectionSet set)
                {
                    sets.Add(set);
                }
                else if (item is FolderItem folder)
                {
                    sets.AddRange(GetAllSelectionSets(folder.Children));
                }
            }
            return sets;
        }

        // Оптимизированная проверка, содержит ли набор элемент
        private bool ContainsItem(SelectionSet selectionSet, ModelItem item, Document doc)
        {
			// Сначала ищем как раньше (без префильтра по модели)

            if (selectionSet.HasExplicitModelItems)
            {
                // Для explicit наборов: быстрая проверка
                return selectionSet.ExplicitModelItems.Contains(item);
            }
            else
            {
                // Для search-based: создаем копию Search, ограничиваем Selection одним элементом
                Search originalSearch = selectionSet.Search;
                Search tempSearch = new Search();
                tempSearch.SearchConditions.CopyFrom(originalSearch.SearchConditions);
                tempSearch.Locations = originalSearch.Locations;
                tempSearch.PruneBelowMatch = originalSearch.PruneBelowMatch;
                // Можно добавить другие свойства, если нужно: IgnoreHidden, etc.

                // Ограничиваем поиск поддеревом одного элемента
                ModelItemCollection roots = new ModelItemCollection { item };
                tempSearch.Selection.CopyFrom(roots);

                // Выполняем поиск (recursive=true для проверки descendants, если нужно)
                ModelItemCollection matches = tempSearch.FindAll(doc, true);

				// Если элемент (или его descendants) matches, продолжаем пост-фильтром по модели
				if (!matches.Contains(item))
				{
					return false;
				}

				// Пост-фильтр: учитываем, какие NWC явно выбраны в наборе (GetSelectedItems())
				// Если выбранные элементы заданы, требуем совпадение модели элемента с одной из их моделей
				{
					ModelItemCollection selectedRoots = selectionSet.GetSelectedItems();
					if (selectedRoots != null && selectedRoots.Count > 0)
				{
						Model itemModel = item.Model;
						bool modelAllowed = false;
						foreach (ModelItem root in selectedRoots)
					{
							Model rootModel = root?.Model;
							if (rootModel != null && rootModel == itemModel)
						{
								modelAllowed = true;
								break;
						}
					}
						if (!modelAllowed)
					{
							return false;
					}
				}
				}
				return true;
            }
        }

        // Вспомогательный метод: получить корневой элемент модели (верхний узел файла модели)
        private ModelItem GetRootModelItem(ModelItem modelItem)
        {
            if (modelItem == null) return null;
            ModelItem current = modelItem;
            ModelItem root = null;
            while (current != null)
            {
                root = current;
                current = current.Parent;
            }
            return root;
        }

        // Вспомогательный метод: получить имя файла модели без расширения
        private string GetModelFileNameWithoutExtension(ModelItem modelItem)
        {
            ModelItem root = GetRootModelItem(modelItem);
            if (root == null) return string.Empty;
            string displayName = root.DisplayName ?? string.Empty;
            string name = Path.GetFileNameWithoutExtension(displayName);
            return name ?? string.Empty;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        // Вспомогательные классы для данных
        private class ClashTestData
        {
            public string Name { get; set; }
            public List<ClashRuleData> Rules { get; set; }
        }

        private class ClashRuleData
        {
            public string Name { get; set; }
            public string SetA { get; set; }
            public string SetB { get; set; }
        }

        // Класс для binding в ListBoxClash (строки вместо SelectionSet)
        private class ClashRuleMatch
        {
            public string DisplayName { get; set; }
            public string SelectionA { get; set; }
            public string SelectionB { get; set; }
        }
    }
}
