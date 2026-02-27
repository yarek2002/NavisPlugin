using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using Autodesk.Navisworks.Api;
using ClashManager.SearchSetCreation.Models;

namespace ClashManager.SearchSetCreation.Views
{
    public partial class CreateSearchSetView : Window
    {
        private ModelItem _selectedItem;
        private ObservableCollection<PropertyItem> _properties;

        public CreateSearchSetView(ModelItem selectedItem)
        {
            InitializeComponent();
            _selectedItem = selectedItem;
            _properties = new ObservableCollection<PropertyItem>();
            PropertiesDataGrid.ItemsSource = _properties;
            LoadProperties();
        }

        private void LoadProperties()
        {
            try
            {
                if (_selectedItem == null)
                    return;

                // Получаем категорию объекта (например, "Объект")
                string objectCategory = GetObjectCategory(_selectedItem);

                // Проходим по всем категориям свойств
                foreach (PropertyCategory category in _selectedItem.PropertyCategories)
                {
                    foreach (DataProperty property in category.Properties)
                    {
                        try
                        {
                            string valueString = GetPropertyValueString(property);
                            
                            // Добавляем свойство в список
                            _properties.Add(new PropertyItem
                            {
                                Category = category.DisplayName,
                                PropertyName = property.DisplayName,
                                PropertyValue = valueString ?? "",
                                OriginalValue = property.Value
                            });
                        }
                        catch (Exception ex)
                        {
                            // Пропускаем свойства, которые не удалось прочитать
                            System.Diagnostics.Debug.WriteLine($"Ошибка чтения свойства {property.DisplayName}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при загрузке свойств: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string GetObjectCategory(ModelItem item)
        {
            // Пытаемся найти категорию "Объект"
            try
            {
                DataProperty idProperty = item.PropertyCategories.FindPropertyByDisplayName("Объект", "Id");
                if (idProperty != null)
                    return "Объект";

                // Пробуем другие варианты
                foreach (PropertyCategory category in item.PropertyCategories)
                {
                    if (category.DisplayName.Contains("Объект") || category.DisplayName.Contains("Object"))
                        return category.DisplayName;
                }
            }
            catch { }

            return "Объект"; // Значение по умолчанию
        }

        private string GetPropertyValueString(DataProperty property)
        {
            if (property?.Value == null)
                return "";

            try
            {
                // Строка от Navisworks обычно имеет формат "Тип:Значение"
                // Нам нужно убрать только префикс "Тип:", оставить само значение как есть
                string raw = property.Value.ToString();
                if (string.IsNullOrEmpty(raw))
                    return "";

                int colonIndex = raw.IndexOf(':');
                if (colonIndex < 0)
                    return raw; // нет двоеточия — возвращаем как есть

                // Всё после первого ':' считаем фактическим значением
                string valuePart = raw.Substring(colonIndex + 1).Trim();
                return valuePart;
            }
            catch
            {
                return "";
            }
        }

        private void CreateSearchSetButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Выбранные строки (чекбокс = вся строка: категория, свойство, значение)
                var selectedProperties = _properties.Where(p => p.IsPropertySelected).ToList();
                
                if (!selectedProperties.Any())
                {
                    MessageBox.Show("Пожалуйста, выберите хотя бы одну строку для создания поискового набора.", 
                        "Предупреждение", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Окно подтверждения условий (как в поиске Navisworks)
                var conditions = selectedProperties.Select(p => new SearchSetConditionItem
                {
                    Category = p.Category,
                    PropertyName = p.PropertyName,
                    Operator = "=",
                    Value = p.PropertyValue
                }).ToList();

                var confirm = new ClashManager.SearchSetCreation.Views.ConfirmSearchSetView(conditions)
                {
                    Owner = this
                };

                if (confirm.ShowDialog() != true)
                    return;

                var finalConditions = confirm.Conditions.ToList();

                Document doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
                string objectCategory = GetObjectCategory(_selectedItem);

                // 1) Поиск по наличию свойств
                Search search = new Search();
                foreach (var c in finalConditions)
                {
                    search.SearchConditions.Add(SearchCondition.HasPropertyByDisplayName(c.Category, c.PropertyName));
                }

                ModelItemCollection foundItems = search.FindAll(doc, false);

                // 2) Ручная фильтрация по операторам/значениям
                if (finalConditions.Any() && foundItems.Count > 0)
                {
                    var filteredItems = new System.Collections.Generic.List<ModelItem>();

                    foreach (ModelItem item in foundItems)
                    {
                        bool ok = true;

                        foreach (var c in finalConditions)
                        {
                            var prop = item.PropertyCategories.FindPropertyByDisplayName(c.Category, c.PropertyName);
                            if (prop == null)
                            {
                                ok = false;
                                break;
                            }

                            // Если значение не задано — условие "свойство существует"
                            if (string.IsNullOrWhiteSpace(c.Value))
                                continue;

                            string itemValue = GetPropertyValueString(prop);
                            if (!EvaluateCondition(itemValue, c.Operator, c.Value))
                            {
                                ok = false;
                                break;
                            }
                        }

                        if (ok)
                            filteredItems.Add(item);
                    }

                    foundItems = new ModelItemCollection();
                    foreach (var item in filteredItems)
                        foundItems.Add(item);
                }

                if (foundItems.Count == 0)
                {
                    MessageBox.Show("ыыы онимэ.", 
                        "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                
                // Создаем поисковый набор
                SelectionSet newSet = new SelectionSet(foundItems);
                
                // Формируем имя набора
                string setName = GenerateSearchSetName(finalConditions, objectCategory);
                newSet.DisplayName = setName;

                // Добавляем набор в документ
                doc.SelectionSets.AddCopy(newSet);

                MessageBox.Show($"Поисковый набор '{setName}' успешно создан. Найдено элементов: {foundItems.Count}", 
                    "Успех", MessageBoxButton.OK, MessageBoxImage.Information);

                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при создании поискового набора: {ex.Message}\n\n{ex.StackTrace}", 
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static bool EvaluateCondition(string itemValue, string op, string expectedValue)
        {
            itemValue ??= "";
            expectedValue ??= "";
            op = (op ?? "=").Trim();

            // Строковые операторы
            if (op.Equals("содержит", StringComparison.OrdinalIgnoreCase))
                return itemValue.IndexOf(expectedValue, StringComparison.OrdinalIgnoreCase) >= 0;
            if (op.Equals("не содержит", StringComparison.OrdinalIgnoreCase))
                return itemValue.IndexOf(expectedValue, StringComparison.OrdinalIgnoreCase) < 0;

            if (op == "=")
                return string.Equals(itemValue, expectedValue, StringComparison.OrdinalIgnoreCase);
            if (op == "!=")
                return !string.Equals(itemValue, expectedValue, StringComparison.OrdinalIgnoreCase);

            // Числовые операторы
            if (op == ">" || op == ">=" || op == "<" || op == "<=")
            {
                if (!TryParseDoubleLoose(itemValue, out double a) || !TryParseDoubleLoose(expectedValue, out double b))
                    return false;

                return op switch
                {
                    ">" => a > b,
                    ">=" => a >= b,
                    "<" => a < b,
                    "<=" => a <= b,
                    _ => false
                };
            }

            // Неизвестный оператор — по умолчанию как "="
            return string.Equals(itemValue, expectedValue, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryParseDoubleLoose(string s, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(s))
                return false;

            // 1) пробуем текущую культуру
            if (double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out value))
                return true;

            // 2) пробуем invariant
            if (double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return true;

            // 3) очищаем строку (на случай единиц: "25.92 м", "12,5mm", и т.п.)
            var cleaned = new string(s.Where(ch =>
                char.IsDigit(ch) || ch == '-' || ch == '+' || ch == '.' || ch == ',' || ch == 'e' || ch == 'E').ToArray());

            if (string.IsNullOrWhiteSpace(cleaned))
                return false;

            if (double.TryParse(cleaned, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
                return true;

            return double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private SearchCondition CreatePropertyValueEqualsCondition(string category, string propertyName, object value)
        {
            // В Navisworks API нет прямого метода PropertyValueEquals для создания условия равенства значения
            // Поэтому мы используем только HasPropertyByDisplayName для проверки наличия свойства,
            // а фильтрацию по значению выполняем вручную после поиска
            // Этот метод всегда возвращает null, чтобы указать, что нужно использовать ручную фильтрацию
            return null;
        }

        private string GenerateSearchSetName(System.Collections.Generic.List<SearchSetConditionItem> selectedProperties, string objectCategory)
        {
            // Формируем имя набора: Категория-объект, Свойство = Значение (выбранная строка)
            var parts = new System.Collections.Generic.List<string>();
            parts.Add(objectCategory);

            foreach (var prop in selectedProperties)
            {
                string op = string.IsNullOrWhiteSpace(prop.Operator) ? "=" : prop.Operator.Trim();
                parts.Add($"{prop.PropertyName}{op}{prop.Value}");
            }

            string setName = string.Join("-", parts);
            
            // Ограничиваем длину имени и очищаем от недопустимых символов
            if (setName.Length > 200)
            {
                setName = setName.Substring(0, 200);
            }
            
            // Заменяем недопустимые символы для имени файла/набора
            setName = setName.Replace("/", "_").Replace("\\", "_").Replace(":", "_")
                            .Replace("*", "_").Replace("?", "_").Replace("\"", "_")
                            .Replace("<", "_").Replace(">", "_").Replace("|", "_");

            return setName;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
