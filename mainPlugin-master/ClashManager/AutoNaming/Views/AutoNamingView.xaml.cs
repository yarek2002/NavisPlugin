using System;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;

namespace ClashManager.AutoNaming.Views
{
    public partial class AutoNamingView : Window
    {
        private Document _doc;
        private DocumentClash _documentClash;
        private readonly HashSet<Guid> _checkedTestIds = new HashSet<Guid>();

        public AutoNamingView()
        {
            InitializeComponent();
            _doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
            _documentClash = _doc.GetClash();
            LoadTests();
        }

        private void LoadTests()
        {
            var tests = _documentClash?.TestsData?.Tests?.OfType<ClashTest>()?.ToList() ?? Enumerable.Empty<ClashTest>().ToList();
            // Оборачиваем в объекты с IsSelected для чекбоксов
            var testRows = tests.Select(t => new { Test = t, DisplayName = t.DisplayName, IsSelected = false, Guid = t.Guid }).ToList();
            TestsListBox.ItemsSource = testRows;
        }

        private void TestsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Обновление интерфейса при выборе тестов
        }

        private void TestCheckBox_Click(object sender, RoutedEventArgs e)
        {
            var cb = sender as CheckBox;
            if (cb == null) return;

            // В шаблоне у нас Tag привязан к Guid
            var tag = cb.Tag;
            if (tag is Guid g)
            {
                if (cb.IsChecked == true)
                    _checkedTestIds.Add(g);
                else
                    _checkedTestIds.Remove(g);
            }
        }

        private void AssignNameButton_Click(object sender, RoutedEventArgs e)
        {
            if (_checkedTestIds.Count == 0)
            {
                MessageBox.Show("Выберите хотя бы один тест!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Логика авто-наименования групп коллизий
            int renamedGroupsCount = 0;

            foreach (var testGuid in _checkedTestIds)
            {
                var test = FindTestByGuid(testGuid);
                if (test == null) continue;

                // Переименовываем группы, заканчивающиеся на "_"
                renamedGroupsCount += RenameGroupsEndingWithUnderscore(test, "");
            }

            if (renamedGroupsCount > 0)
            {
                MessageBox.Show($"Авто-наименование выполнено! Переименовано {renamedGroupsCount} групп коллизий.", "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Не найдено групп коллизий, заканчивающихся на '_'", "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            this.Close();
        }

        private ClashTest FindTestByGuid(Guid testGuid)
        {
            var allTests = _documentClash?.TestsData?.Tests?.OfType<ClashTest>()?.ToList();
            return allTests?.FirstOrDefault(t => t.Guid == testGuid);
        }

        private int RenameGroupsEndingWithUnderscore(ClashTest test, string newName)
        {
            int renamedCount = 0;

            // Получаем все группы из теста (включая вложенные)
            var allGroups = GetAllGroupsFromTest(test);

            foreach (var group in allGroups)
            {
                if (group.DisplayName?.EndsWith("_") == true)
                {
                    // Получаем названия моделей из группы
                    string modelNames = GetModelNamesFromGroup(group);
                    
                    // Формируем новое имя: убираем "_" и добавляем "|" + названия моделей
                    string baseName = group.DisplayName.TrimEnd('_');
                    string finalName = baseName;
                    if (!string.IsNullOrEmpty(modelNames))
                    {
                        finalName = baseName + " | " + modelNames;
                    }

                    // Создаем копию теста для изменения
                    var testIndex = _documentClash.TestsData.Tests.IndexOf(test);
                    if (testIndex >= 0)
                    {
                        var testCopy = (ClashTest)test.CreateCopy();

                        // Находим и переименовываем группу в копии
                        var groupInCopy = FindGroupInTestCopy(testCopy, group.Guid);
                        if (groupInCopy != null)
                        {
                            groupInCopy.DisplayName = finalName;
                            _documentClash.TestsData.TestsReplaceWithCopy(testIndex, testCopy);
                            renamedCount++;
                        }
                    }
                }
            }

            return renamedCount;
        }

        private System.Collections.Generic.List<ClashResultGroup> GetAllGroupsFromTest(ClashTest test)
        {
            var allGroups = new System.Collections.Generic.List<ClashResultGroup>();

            foreach (var group in test.Children.OfType<ClashResultGroup>())
            {
                allGroups.Add(group);
                allGroups.AddRange(GetAllGroupsFromGroup(group));
            }

            return allGroups;
        }

        private System.Collections.Generic.List<ClashResultGroup> GetAllGroupsFromGroup(ClashResultGroup group)
        {
            var allGroups = new System.Collections.Generic.List<ClashResultGroup>();

            foreach (var childGroup in group.Children.OfType<ClashResultGroup>())
            {
                allGroups.Add(childGroup);
                allGroups.AddRange(GetAllGroupsFromGroup(childGroup));
            }

            return allGroups;
        }

        /// <summary>
        /// Получает все результаты коллизий из группы (включая вложенные группы)
        /// </summary>
        /// <param name="group">Группа коллизий</param>
        /// <returns>Список всех результатов коллизий</returns>
        private System.Collections.Generic.List<ClashResult> GetAllResultsFromGroup(ClashResultGroup group)
        {
            var allResults = new System.Collections.Generic.List<ClashResult>();

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

        private ClashResultGroup FindGroupInTestCopy(ClashTest testCopy, Guid groupGuid)
        {
            foreach (var group in testCopy.Children.OfType<ClashResultGroup>())
            {
                if (group.Guid == groupGuid)
                    return group;

                var found = FindGroupInGroupCopy(group, groupGuid);
                if (found != null)
                    return found;
            }

            return null;
        }

        private ClashResultGroup FindGroupInGroupCopy(ClashResultGroup group, Guid groupGuid)
        {
            foreach (var childGroup in group.Children.OfType<ClashResultGroup>())
            {
                if (childGroup.Guid == groupGuid)
                    return childGroup;

                var found = FindGroupInGroupCopy(childGroup, groupGuid);
                if (found != null)
                    return found;
            }

            return null;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        /// <summary>
        /// Получает название модели из ModelItem через иерархию дерева выбора
        /// </summary>
        /// <param name="modelItem">Элемент модели</param>
        /// <returns>Название модели без расширения или "Unknown"</returns>
        private string GetModelName(ModelItem modelItem)
        {
            if (modelItem == null) return "Unknown";

            try
            {
                // Поднимаемся по иерархии до корневого элемента модели
                ModelItem rootModel = GetRootModelItem(modelItem);
                if (rootModel != null)
                {
                    // Получаем название из DisplayName корневого элемента
                    string rootDisplayName = rootModel.DisplayName;
                    if (!string.IsNullOrEmpty(rootDisplayName))
                    {
                        // Убираем расширение файла, если оно есть
                        string fileNameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(rootDisplayName);
                        return fileNameWithoutExtension;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting model name: {ex.Message}");
            }

            return "Unknown";
        }

        /// <summary>
        /// Получает корневой элемент модели, поднимаясь по иерархии
        /// </summary>
        /// <param name="modelItem">Начальный элемент модели</param>
        /// <returns>Корневой элемент модели или null</returns>
        private ModelItem GetRootModelItem(ModelItem modelItem)
        {
            if (modelItem == null) return null;

            try
            {
                ModelItem current = modelItem;
                ModelItem root = null;

                // Поднимаемся по иерархии до самого верха
                while (current != null)
                {
                    root = current;
                    current = current.Parent;
                }

                return root;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting root model item: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Получает названия моделей из группы коллизий с дополнительной информацией
        /// </summary>
        /// <param name="group">Группа коллизий</param>
        /// <returns>Строка с названиями моделей, ID элементов и GUID группы</returns>
        private string GetModelNamesFromGroup(ClashResultGroup group)
        {
            if (group == null) return "";

            try
            {
                // Получаем все результаты из группы
                var allResults = GetAllResultsFromGroup(group);
                if (allResults.Count == 0) return "";

                // Берем первый результат для получения названий моделей
                var firstResult = allResults.First();
                string model1Name = GetModelName(firstResult.CompositeItem1);
                string model2Name = GetModelName(firstResult.CompositeItem2);

                // Получаем ID элементов
                string element1Id = GetElementId(firstResult.CompositeItem1);
                string element2Id = GetElementId(firstResult.CompositeItem2);

                // Формируем строку с названиями моделей, ID элементов и GUID группы
                var parts = new List<string>();

                // Добавляем названия моделей
                if (model1Name != "Unknown" && model2Name != "Unknown")
                {
                    parts.Add($"{model1Name} | {model2Name}");
                }
                else if (model1Name != "Unknown")
                {
                    parts.Add(model1Name);
                }
                else if (model2Name != "Unknown")
                {
                    parts.Add(model2Name);
                }

                // Добавляем ID элементов
                if (!string.IsNullOrEmpty(element1Id))
                {
                    parts.Add(element1Id);
                }
                if (!string.IsNullOrEmpty(element2Id))
                {
                    parts.Add(element2Id);
                }

                // Добавляем GUID группы
                parts.Add(group.Guid.ToString());

                return string.Join(" | ", parts);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting model names from group: {ex.Message}");
            }

            return "";
        }

        /// <summary>
        /// Получает ID элемента из ModelItem
        /// </summary>
        /// <param name="modelItem">Элемент модели</param>
        /// <returns>ID элемента или пустая строка</returns>
        private string GetElementId(ModelItem modelItem)
        {
            if (modelItem == null) return "";

            try
            {
                // Ищем свойство "Id" в категории "Объект"
                var idProperty = modelItem.PropertyCategories.FindPropertyByDisplayName("Объект", "Id");
                if (idProperty != null)
                {
                    return idProperty.Value?.ToDisplayString() ?? "";
                }

                // Если не найдено, пробуем поискать в родительском элементе
                if (modelItem.Parent != null)
                {
                    return GetElementId(modelItem.Parent);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting element ID: {ex.Message}");
            }

            return "";
        }
    }
}
