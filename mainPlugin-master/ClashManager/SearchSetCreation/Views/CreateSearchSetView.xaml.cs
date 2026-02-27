using System;
using System.Collections.ObjectModel;
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

                Document doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
                string objectCategory = GetObjectCategory(_selectedItem);

                // Создаем поисковый запрос
                Search search = new Search();

                // Добавляем условия для выбранных свойств и значений
                bool hasConditions = false;
                var valueConditions = new System.Collections.Generic.List<PropertyItem>();

                foreach (var propItem in selectedProperties)
                {
                    // Выбрана вся строка: добавляем условие по свойству и по значению
                    search.SearchConditions.Add(
                        SearchCondition.HasPropertyByDisplayName(propItem.Category, propItem.PropertyName));
                    hasConditions = true;

                    if (!string.IsNullOrEmpty(propItem.PropertyValue))
                    {
                        valueConditions.Add(propItem);
                        try
                        {
                            var condition = CreatePropertyValueEqualsCondition(
                                propItem.Category,
                                propItem.PropertyName,
                                propItem.OriginalValue);
                            if (condition != null)
                            {
                                search.SearchConditions.Add(condition);
                                hasConditions = true;  
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Ошибка создания условия для {propItem.PropertyName}: {ex.Message}");
                        }
                    }
                }

                if (!hasConditions && valueConditions.Count == 0)
                {
                    MessageBox.Show("Пожалуйста, выберите хотя бы одну строку для создания поискового набора.", 
                        "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Выполняем поиск
                ModelItemCollection foundItems = search.FindAll(doc, false);

                // Если использовались условия по значениям, которые не удалось добавить через PropertyValueEquals,
                // фильтруем результаты вручную
                if (valueConditions.Any() && foundItems.Count > 0)
                {
                    var filteredItems = new System.Collections.Generic.List<ModelItem>();
                    
                    foreach (ModelItem item in foundItems)
                    {
                        bool matchesAllConditions = true;
                        
                        foreach (var propItem in valueConditions)
                        {
                            try
                            {
                                var prop = item.PropertyCategories.FindPropertyByDisplayName(propItem.Category, propItem.PropertyName);
                                if (prop == null)
                                {
                                    matchesAllConditions = false;
                                    break;
                                }
                                
                                string itemValue = GetPropertyValueString(prop);
                                if (itemValue != propItem.PropertyValue)
                                {
                                    matchesAllConditions = false;
                                    break;
                                }
                            }
                            catch
                            {
                                matchesAllConditions = false;
                                break;
                            }
                        }
                        
                        if (matchesAllConditions)
                        {
                            filteredItems.Add(item);
                        }
                    }
                    
                    // Создаем новую коллекцию ModelItemCollection и добавляем элементы
                    foundItems = new ModelItemCollection();
                    foreach (var item in filteredItems)
                    {
                        foundItems.Add(item);
                    }
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
                string setName = GenerateSearchSetName(selectedProperties, objectCategory);
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

        private SearchCondition CreatePropertyValueEqualsCondition(string category, string propertyName, object value)
        {
            // В Navisworks API нет прямого метода PropertyValueEquals для создания условия равенства значения
            // Поэтому мы используем только HasPropertyByDisplayName для проверки наличия свойства,
            // а фильтрацию по значению выполняем вручную после поиска
            // Этот метод всегда возвращает null, чтобы указать, что нужно использовать ручную фильтрацию
            return null;
        }

        private string GenerateSearchSetName(System.Collections.Generic.List<PropertyItem> selectedProperties, string objectCategory)
        {
            // Формируем имя набора: Категория-объект, Свойство = Значение (выбранная строка)
            var parts = new System.Collections.Generic.List<string>();
            parts.Add(objectCategory);

            foreach (var prop in selectedProperties)
            {
                parts.Add($"{prop.PropertyName}={prop.PropertyValue}");
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
