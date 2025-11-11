using System;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using Autodesk.Navisworks.Api.Interop.ComApi;

namespace ClashManager.AutoNaming.Views
{
    /// <summary>
    /// Класс для элементов списка тестов коллизий
    /// </summary>
    public class TestItem : INotifyPropertyChanged
    {
        private bool _isChecked;

        public ClashTest Test { get; set; }
        public string DisplayName { get; set; }
        public Guid Guid { get; set; }

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked != value)
                {
                    _isChecked = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
    public partial class AutoNamingView : Window
    {
        private Document _doc;
        private DocumentClash _documentClash;
        private readonly HashSet<Guid> _checkedTestIds = new HashSet<Guid>();
        private List<TestItem> _testItems = new List<TestItem>(); // Кэшируем список тестов
        private int _lastTestClickIndex = -1; // Отслеживаем последний клик для Shift-выбора
        private bool _suppressCheckboxHandlers = false; // Предотвращает рекурсивные вызовы обработчиков чекбоксов
        private AutoNamingSettings _allSettings; // Все настройки авто-наименования

        public AutoNamingView()
        {
            InitializeComponent();
            _doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
            _documentClash = _doc.GetClash();
            LoadTests();
            LoadSettings();
        }

        private void LoadSettings()
        {
            _allSettings = AutoNamingSettings.LoadFromFile();
        }

        private void LoadTests()
        {
            var tests = _documentClash?.TestsData?.Tests?.OfType<ClashTest>()?.ToList() ?? Enumerable.Empty<ClashTest>().ToList();

            // Создаем объекты TestItem только один раз
            _testItems = tests.Select(t => new TestItem
            {
                Test = t,
                DisplayName = t.DisplayName,
                IsChecked = _checkedTestIds.Contains(t.Guid),
                Guid = t.Guid
            }).ToList();

            // Подписываемся на изменения состояния каждого элемента
            foreach (var testItem in _testItems)
            {
                testItem.PropertyChanged += TestItem_PropertyChanged;
            }

            TestsListBox.ItemsSource = _testItems;
        }

        /// <summary>
        /// Обработчик изменения свойства TestItem
        /// </summary>
        private void TestItem_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TestItem.IsChecked) && sender is TestItem checkedItem)
            {
                // Синхронизируем с HashSet
                if (checkedItem.IsChecked)
                {
                    _checkedTestIds.Add(checkedItem.Guid);
                }
                else
                {
                    _checkedTestIds.Remove(checkedItem.Guid);
                }
            }
        }

        private void TestsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Обновление интерфейса при выборе тестов
        }

        private void TestsListBox_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // Принудительно синхронизируем состояние при прокрутке
            if (e.VerticalChange != 0)
            {
                ForceSyncCheckboxStates();
            }
        }

        private void TestsListBox_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Обработка массового выделения через Shift+клик
            // Логика массового проставления галочек перенесена в TestCheckBox_Click
        }

        /// <summary>
        /// Принудительно синхронизирует состояние чекбоксов с моделью данных
        /// </summary>
        private void ForceSyncCheckboxStates()
        {
            // Временно отписываемся от событий, чтобы избежать циклических вызовов
            foreach (var testItem in _testItems)
            {
                testItem.PropertyChanged -= TestItem_PropertyChanged;
            }

            // Синхронизируем состояние
            foreach (var testItem in _testItems)
            {
                testItem.IsChecked = _checkedTestIds.Contains(testItem.Guid);
            }

            // Подписываемся обратно
            foreach (var testItem in _testItems)
            {
                testItem.PropertyChanged += TestItem_PropertyChanged;
            }
        }

        private void TestCheckBox_Click(object sender, RoutedEventArgs e)
        {
            var cb = sender as CheckBox;
            if (cb == null) return;
            if (_suppressCheckboxHandlers) return;

            var item = cb.DataContext as TestItem;
            if (item == null) return;

            // Предотвращаем повторную обработку события
            e.Handled = true;

            int currentIndex = TestsListBox.Items.IndexOf(item);
            bool isShift = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) == System.Windows.Input.ModifierKeys.Shift;
            bool targetChecked = cb.IsChecked == true;

            // Если выделено несколько элементов в списке, применяем действие ко всем выделенным
            if (TestsListBox.SelectedItems.Count > 1 && TestsListBox.SelectedItems.Contains(item))
            {
                _suppressCheckboxHandlers = true;
                try
                {
                    foreach (var selectedItem in TestsListBox.SelectedItems)
                    {
                        if (selectedItem is TestItem selectedTestItem)
                        {
                            selectedTestItem.IsChecked = targetChecked;
                        }
                    }
                }
                finally { _suppressCheckboxHandlers = false; }
            }
            else if (isShift && _lastTestClickIndex >= 0)
            {
                // Shift-выбор: применяем действие ко всем элементам между последним и текущим кликом
                int from = Math.Min(_lastTestClickIndex, currentIndex);
                int to = Math.Max(_lastTestClickIndex, currentIndex);

                _suppressCheckboxHandlers = true;
                try
                {
                    for (int i = from; i <= to; i++)
                    {
                        if (TestsListBox.Items[i] is TestItem shiftItem)
                        {
                            shiftItem.IsChecked = targetChecked; // меняем состояние через модель
                        }
                    }
                }
                finally { _suppressCheckboxHandlers = false; }
            }
            else
            {
                // Одиночный клик - убеждаемся, что состояние синхронизировано
                item.IsChecked = targetChecked;
            }

            _lastTestClickIndex = currentIndex;
        }

        private void AssignNameButton_Click(object sender, RoutedEventArgs e)
        {
            // Получаем выбранные тесты
            var selectedTestItems = _testItems.Where(t => t.IsChecked).ToList();

            if (selectedTestItems.Count == 0)
            {
                MessageBox.Show("Выберите хотя бы один тест!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Логика авто-наименования групп коллизий
            int renamedGroupsCount = 0;

            foreach (var testItem in selectedTestItems)
            {
                try
                {
                    var test = testItem.Test;
                    if (test == null) continue;

                    // Получаем настройки для этого конкретного теста
                    var testSettings = _allSettings?.GetTestSettings(testItem.Guid);

                    // Переименовываем группы, заканчивающиеся на "_"
                    renamedGroupsCount += RenameGroupsEndingWithUnderscore(test, null, testSettings);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error processing test '{testItem.DisplayName}': {ex.Message}");
                    // Continue with other tests instead of crashing
                }
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

        private int RenameGroupsEndingWithUnderscore(ClashTest test, string newName, TestAutoNamingSettings settings = null)
        {
            int renamedCount = 0;

            try
            {
                // Получаем все группы из теста (включая вложенные)
                var allGroups = GetAllGroupsFromTest(test);

            // Собираем все группы, которые нужно переименовать
            var groupsToRename = new Dictionary<Guid, string>();

            foreach (var group in allGroups)
            {
                if (group.DisplayName?.EndsWith("_") == true)
                {
                    // Получаем названия моделей из группы
                    string modelNames = GetModelNamesFromGroup(group, settings);

                    // Формируем новое имя: убираем "_" и добавляем "|" + названия моделей
                    string baseName = group.DisplayName.TrimEnd('_');

                    // Если есть пользовательское имя для теста, используем его вместо базового имени
                    string nameToUse = !string.IsNullOrEmpty(newName) ? newName : baseName;

                    string finalName = nameToUse;
                    if (!string.IsNullOrEmpty(modelNames))
                    {
                        finalName = nameToUse + " | " + modelNames;
                    }

                    groupsToRename[group.Guid] = finalName;
                }
            }

            // Если есть группы для переименования, применяем все изменения за один раз
            if (groupsToRename.Count > 0)
            {
                var testIndex = _documentClash.TestsData.Tests.IndexOf(test);
                if (testIndex >= 0)
                {
                    var testCopy = (ClashTest)test.CreateCopy();

                    // Переименовываем все группы в копии
                    foreach (var kvp in groupsToRename)
                    {
                        var groupInCopy = FindGroupInTestCopy(testCopy, kvp.Key);
                        if (groupInCopy != null)
                        {
                            groupInCopy.DisplayName = kvp.Value;
                            renamedCount++;
                        }
                    }

                    // Заменяем тест копией со всеми изменениями
                    _documentClash.TestsData.TestsReplaceWithCopy(testIndex, testCopy);
                }
            }

            return renamedCount;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in RenameGroupsEndingWithUnderscore for test '{test?.DisplayName ?? "Unknown"}': {ex.Message}");
                return 0; // Return 0 on error
            }
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



        private void NameSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            // Получаем выбранные тесты
            var selectedTestGuids = _testItems.Where(t => t.IsChecked).Select(t => t.Guid).ToList();

            if (selectedTestGuids.Count == 0)
            {
                MessageBox.Show("Выберите хотя бы один тест для настройки!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Открываем окно настроек
            var settingsWindow = new AutoNamingSettingsView();
            settingsWindow.Owner = this;
            settingsWindow.SelectedTestGuids = selectedTestGuids;
            var result = settingsWindow.ShowDialog();

            // Если настройки были применены, перезагружаем настройки
            if (result == true)
            {
                LoadSettings(); // Перезагружаем настройки после сохранения
                System.Diagnostics.Debug.WriteLine($"Settings updated for {selectedTestGuids.Count} tests");
            }
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
        /// <param name="settings">Настройки авто-наименования</param>
        /// <returns>Строка с названиями моделей, ID элементов и GUID группы</returns>
        private string GetModelNamesFromGroup(ClashResultGroup group, TestAutoNamingSettings settings)
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

                // Собираем ID элементов, группируя их по моделям
                var model1ElementIds = new HashSet<string>();
                var model2ElementIds = new HashSet<string>();
                
                foreach (var result in allResults)
                {
                    // Получаем название модели для первого элемента
                    string resultModel1Name = GetModelName(result.CompositeItem1);
                    string resultModel2Name = GetModelName(result.CompositeItem2);
                    
                    // Получаем ID первого элемента
                    string element1Id = GetElementId(result.CompositeItem1);
                    if (!string.IsNullOrEmpty(element1Id))
                    {
                        // Определяем, к какой модели относится элемент
                        if (resultModel1Name == model1Name)
                        {
                            model1ElementIds.Add(element1Id);
                        }
                        else if (resultModel1Name == model2Name)
                        {
                            model2ElementIds.Add(element1Id);
                        }
                    }
                    
                    // Получаем ID второго элемента
                    string element2Id = GetElementId(result.CompositeItem2);
                    if (!string.IsNullOrEmpty(element2Id))
                    {
                        // Определяем, к какой модели относится элемент
                        if (resultModel2Name == model1Name)
                        {
                            model1ElementIds.Add(element2Id);
                        }
                        else if (resultModel2Name == model2Name)
                        {
                            model2ElementIds.Add(element2Id);
                        }
                    }
                }
                
                // Проверяем, одинаковые ли модели
                bool isSameModel = model1Name == model2Name;
                
                // ДОПОЛНИТЕЛЬНАЯ ПРОВЕРКА: Если у нас есть ID только в одной коллекции,
                // но оба элемента из одной коллизии, то модели должны быть одинаковыми
                if (!isSameModel && (model1ElementIds.Count > 0 && model2ElementIds.Count == 0) || 
                    (model2ElementIds.Count > 0 && model1ElementIds.Count == 0))
                {
                    // Проверяем, есть ли коллизии где оба элемента из одной модели
                    bool hasSameModelCollision = false;
                    foreach (var result in allResults)
                    {
                        string resultModel1Name = GetModelName(result.CompositeItem1);
                        string resultModel2Name = GetModelName(result.CompositeItem2);
                        if (resultModel1Name == resultModel2Name && resultModel1Name != "Unknown")
                        {
                            hasSameModelCollision = true;
                            break;
                        }
                    }
                    
                    if (hasSameModelCollision)
                    {
                        isSameModel = true;
                        System.Diagnostics.Debug.WriteLine($"FORCED isSameModel = true due to same model collision detected");
                    }
                }
                
                // Отладочная информация
                System.Diagnostics.Debug.WriteLine($"=== DEBUG AutoNaming ===");
                System.Diagnostics.Debug.WriteLine($"Model1 name: '{model1Name}'");
                System.Diagnostics.Debug.WriteLine($"Model2 name: '{model2Name}'");
                System.Diagnostics.Debug.WriteLine($"Model1 ({model1Name}) IDs: [{string.Join(", ", model1ElementIds)}] (Count: {model1ElementIds.Count})");
                System.Diagnostics.Debug.WriteLine($"Model2 ({model2Name}) IDs: [{string.Join(", ", model2ElementIds)}] (Count: {model2ElementIds.Count})");
                System.Diagnostics.Debug.WriteLine($"Same model: {isSameModel} (model1Name == model2Name: {model1Name == model2Name})");

                // Формируем строку с названиями моделей, ID элементов и GUID группы
                var parts = new List<string>();

                // Добавляем названия моделей
                if (model1Name != "Unknown" && model2Name != "Unknown")
                {
                    if (isSameModel)
                    {
                        // Если модели одинаковые, дублируем название модели
                        parts.Add($"{model1Name} | {model1Name}");
                        System.Diagnostics.Debug.WriteLine($"Added same model names: '{model1Name} | {model1Name}'");
                    }
                    else
                    {
                        // Если модели разные, добавляем оба названия
                        parts.Add($"{model1Name} | {model2Name}");
                        System.Diagnostics.Debug.WriteLine($"Added different model names: '{model1Name} | {model2Name}'");
                    }
                }
                else if (model1Name != "Unknown")
                {
                    parts.Add(model1Name);
                    System.Diagnostics.Debug.WriteLine($"Added only model1 name: '{model1Name}' (model2 is Unknown)");
                }
                else if (model2Name != "Unknown")
                {
                    parts.Add(model2Name);
                    System.Diagnostics.Debug.WriteLine($"Added only model2 name: '{model2Name}' (model1 is Unknown)");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Both models are Unknown - no model names added");
                }

                // Добавляем ID элементов, группируя по моделям
                var idParts = new List<string>();
                
                if (isSameModel)
                {
                    // Если модели одинаковые, объединяем все ID в одну группу и дублируем
                    var allIds = new HashSet<string>();
                    allIds.UnionWith(model1ElementIds);
                    allIds.UnionWith(model2ElementIds);
                    
                    System.Diagnostics.Debug.WriteLine($"Same model processing: model1ElementIds.Count={model1ElementIds.Count}, model2ElementIds.Count={model2ElementIds.Count}, allIds.Count={allIds.Count}");
                    
                    if (allIds.Count > 0)
                    {
                        string idsString = string.Join(", ", allIds.OrderBy(id => id));
                        idParts.Add(idsString); // Для первой модели
                        idParts.Add(idsString); // Для второй модели (дублируем)
                        
                        System.Diagnostics.Debug.WriteLine($"Added duplicate IDs: '{idsString}' | '{idsString}'");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("No IDs found for same model case");
                    }
                }
                else
                {
                    // Если модели разные, добавляем ID отдельно для каждой модели
                    System.Diagnostics.Debug.WriteLine($"Different models processing: model1ElementIds.Count={model1ElementIds.Count}, model2ElementIds.Count={model2ElementIds.Count}");
                    
                    // ВАЖНО: Если у нас есть ID только в одной коллекции, но модели должны быть одинаковыми,
                    // принудительно дублируем ID
                    if (model1ElementIds.Count > 0 && model2ElementIds.Count == 0)
                    {
                        string model1Ids = string.Join(", ", model1ElementIds.OrderBy(id => id));
                        idParts.Add(model1Ids);
                        idParts.Add(model1Ids); // Дублируем
                        System.Diagnostics.Debug.WriteLine($"FORCED DUPLICATION: Added model1 IDs twice: '{model1Ids}' | '{model1Ids}'");
                    }
                    else if (model2ElementIds.Count > 0 && model1ElementIds.Count == 0)
                    {
                        string model2Ids = string.Join(", ", model2ElementIds.OrderBy(id => id));
                        idParts.Add(model2Ids);
                        idParts.Add(model2Ids); // Дублируем
                        System.Diagnostics.Debug.WriteLine($"FORCED DUPLICATION: Added model2 IDs twice: '{model2Ids}' | '{model2Ids}'");
                    }
                    else
                    {
                        // Обычная логика для разных моделей
                        if (model1ElementIds.Count > 0)
                        {
                            string model1Ids = string.Join(", ", model1ElementIds.OrderBy(id => id));
                            idParts.Add(model1Ids);
                            System.Diagnostics.Debug.WriteLine($"Added model1 IDs: '{model1Ids}'");
                        }
                        
                        if (model2ElementIds.Count > 0)
                        {
                            string model2Ids = string.Join(", ", model2ElementIds.OrderBy(id => id));
                            idParts.Add(model2Ids);
                            System.Diagnostics.Debug.WriteLine($"Added model2 IDs: '{model2Ids}'");
                        }
                    }
                }
        
                
                // Если есть ID элементов, добавляем их в общий список
                if (idParts.Count > 0)
                {
                    string joinedIds = string.Join(" | ", idParts);
                    parts.Add(joinedIds);
                    System.Diagnostics.Debug.WriteLine($"Final ID parts: {idParts.Count} parts -> '{joinedIds}'");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("No ID parts to add");
                }

                // Добавляем GUID группы
                parts.Add(group.Guid.ToString());

                // Добавляем пользовательские параметры, если они настроены
                if (settings != null)
                {
                    var customParts = GetCustomParametersFromGroup(group, settings);
                    if (customParts.Count > 0)
                    {
                        parts.AddRange(customParts);
                    }
                }

                string finalResult = string.Join(settings?.Separator ?? " | ", parts);
                System.Diagnostics.Debug.WriteLine($"Final result: {finalResult}");

                return finalResult;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting model names from group: {ex.Message}");
            }

            return "";
        }

        /// <summary>
        /// Получает пользовательские параметры из группы коллизий
        /// </summary>
        /// <param name="group">Группа коллизий</param>
        /// <param name="settings">Настройки авто-наименования</param>
        /// <returns>Список найденных пользовательских параметров</returns>
        private List<string> GetCustomParametersFromGroup(ClashResultGroup group, TestAutoNamingSettings settings)
        {
            var customParts = new List<string>();

            // If no settings, return empty list
            if (settings == null)
                return customParts;

            var allResults = GetAllResultsFromGroup(group);

            if (allResults.Count == 0)
                return customParts;

            // Берем первый результат для поиска параметров
            var firstResult = allResults.First();

            // Ищем параметры в обоих элементах коллизии
            foreach (var paramName in settings.GetActiveParameters())
            {
                var paramValues = new List<string>();

                // Ищем параметр в первом элементе
                string paramValue1 = GetCustomParameterValue(firstResult.CompositeItem1, paramName);
                if (!string.IsNullOrEmpty(paramValue1))
                {
                    paramValues.Add(paramValue1);
                    System.Diagnostics.Debug.WriteLine($"Found custom parameter '{paramName}' in first item: '{paramValue1}'");
                }

                // Ищем параметр во втором элементе
                string paramValue2 = GetCustomParameterValue(firstResult.CompositeItem2, paramName);
                if (!string.IsNullOrEmpty(paramValue2))
                {
                    paramValues.Add(paramValue2);
                    System.Diagnostics.Debug.WriteLine($"Found custom parameter '{paramName}' in second item: '{paramValue2}'");
                }

                // Если нашли значения параметров, добавляем их
                if (paramValues.Count > 0)
                {
                    // Объединяем значения через разделитель настроек
                    string combinedValue = string.Join(settings.Separator, paramValues);
                    customParts.Add(combinedValue);
                    System.Diagnostics.Debug.WriteLine($"Combined parameter '{paramName}': '{combinedValue}'");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Custom parameter '{paramName}' not found in either item");
                }
            }

            return customParts;
        }

        /// <summary>
        /// Получает значение пользовательского параметра из ModelItem
        /// </summary>
        /// <param name="modelItem">Элемент модели</param>
        /// <param name="paramName">Имя параметра для поиска</param>
        /// <returns>Значение параметра или null</returns>
        private string GetCustomParameterValue(ModelItem modelItem, string paramName)
        {
            if (modelItem == null || string.IsNullOrEmpty(paramName))
                return null;

            try
            {
                // Ищем параметр по имени во всех категориях и свойствах
                foreach (var category in modelItem.PropertyCategories)
                {
                    foreach (var property in category.Properties)
                    {
                        try
                        {
                            // Проверяем отображаемое имя свойства
                            if (property.DisplayName == paramName)
                            {
                                string value = property.Value?.ToString();
                                if (!string.IsNullOrEmpty(value))
                                {
                                    // Очищаем значение от возможных префиксов типа "DisplayString: "
                                    value = CleanParameterValue(value);
                                    return value;
                                }
                            }

                            // Также проверяем внутреннее имя
                            if (property.Name == paramName)
                            {
                                string value = property.Value?.ToString();
                                if (!string.IsNullOrEmpty(value))
                                {
                                    // Очищаем значение от возможных префиксов типа "DisplayString: "
                                    value = CleanParameterValue(value);
                                    return value;
                                }
                            }
                        }
                        catch
                        {
                            // Игнорируем ошибки чтения конкретного свойства
                        }
                    }
                }

                // Если не нашли, пробуем в родительском элементе
                if ((NativeHandle)modelItem.Parent != (NativeHandle)null)
                {
                    return GetCustomParameterValue(modelItem.Parent, paramName);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting custom parameter '{paramName}': {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Очищает значение параметра от возможных префиксов типа "DisplayString: "
        /// </summary>
        /// <param name="value">Исходное значение</param>
        /// <returns>Очищенное значение</returns>
        private string CleanParameterValue(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            // Если значение содержит ":", берем часть после ":"
            if (value.Contains(":"))
            {
                var parts = value.Split(new[] { ':' }, 2);
                if (parts.Length > 1)
                {
                    return parts[1].Trim();
                }
            }

            return value;
        }

        /// <summary>
        /// Получает ID элемента из ModelItem
        /// </summary>
        /// <param name="modelItem">Элемент модели</param>
        /// <returns>ID элемента или null</returns>
        private string GetElementId(ModelItem modelItem)
        {
            DataProperty propertyByDisplayName = modelItem.PropertyCategories.FindPropertyByDisplayName("Объект", "Id");
            if ((NativeHandle) propertyByDisplayName != (NativeHandle) null)
                return propertyByDisplayName.Value.ToInt32().ToString();
            return (NativeHandle) modelItem.Parent != (NativeHandle) null ? this.GetElementId(modelItem.Parent) : (string) null;
        }
    }
}
