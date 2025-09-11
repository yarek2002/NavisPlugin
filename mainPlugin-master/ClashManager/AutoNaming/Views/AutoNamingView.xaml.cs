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
            string groupName = GroupNameTextBox.Text?.Trim();
            if (string.IsNullOrEmpty(groupName))
            {
                MessageBox.Show("Введите имя группы!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_checkedTestIds.Count == 0)
            {
                MessageBox.Show("Выберите хотя бы один тест!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Логика назначения имени группам коллизий
            int renamedGroupsCount = 0;

            foreach (var testGuid in _checkedTestIds)
            {
                var test = FindTestByGuid(testGuid);
                if (test == null) continue;

                // Переименовываем группы, заканчивающиеся на "_"
                renamedGroupsCount += RenameGroupsEndingWithUnderscore(test, groupName);
            }

            if (renamedGroupsCount > 0)
            {
                MessageBox.Show($"Имя '{groupName}' назначено {renamedGroupsCount} группам коллизий!", "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
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
                    // Создаем копию теста для изменения
                    var testIndex = _documentClash.TestsData.Tests.IndexOf(test);
                    if (testIndex >= 0)
                    {
                        var testCopy = (ClashTest)test.CreateCopy();

                        // Находим и переименовываем группу в копии
                        var groupInCopy = FindGroupInTestCopy(testCopy, group.Guid);
                        if (groupInCopy != null)
                        {
                            groupInCopy.DisplayName = newName;
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
    }
}
