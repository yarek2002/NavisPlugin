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
                // Пробуем разные способы получения строкового представления значения
                if (property.Value is VariantData variant)
                {
                    return variant.ToDisplayString();
                }
                
                return property.Value.ToString();
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
                // Проверяем, что выбраны хотя бы одно свойство и одно значение
                var selectedProperties = _properties.Where(p => p.IsPropertySelected || p.IsValueSelected).ToList();
                
                if (!selectedProperties.Any())
                {
                    MessageBox.Show("Пожалуйста, выберите хотя бы одно свойство или значение для создания поискового набора.", 
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
                    if (propItem.IsPropertySelected)
                    {
                        // Условие: свойство существует
                        search.SearchConditions.Add(
                            SearchCondition.HasPropertyByDisplayName(propItem.Category, propItem.PropertyName));
                        hasConditions = true;
                    }

                    if (propItem.IsValueSelected && !string.IsNullOrEmpty(propItem.PropertyValue))
                    {
                        // Сохраняем для последующей обработки
                        valueConditions.Add(propItem);
                        
                        // Пытаемся создать условие равенства значения
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
                    MessageBox.Show("Пожалуйста, выберите хотя бы одно свойство или значение для создания поискового набора.", 
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
                    
                    foundItems = new ModelItemCollection(filteredItems);
                }

                if (foundItems.Count == 0)
                {
                    MessageBox.Show("Поиск не нашел элементов, соответствующих выбранным условиям.", 
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
            try
            {
                // В Navisworks API для создания условия равенства значения используется PropertyValueEquals
                // Метод принимает категорию, имя свойства и значение
                if (value == null)
                    return null;

                // Преобразуем значение в VariantData если необходимо
                VariantData variantValue = null;
                if (value is VariantData)
                {
                    variantValue = (VariantData)value;
                }
                else
                {
                    variantValue = new VariantData(value);
                }

                // Создаем условие равенства значения свойства
                return SearchCondition.PropertyValueEquals(category, propertyName, variantValue);
            }
            catch (Exception ex)
            {
                // Если PropertyValueEquals не доступен или произошла ошибка,
                // используем альтернативный подход через фильтрацию результатов
                System.Diagnostics.Debug.WriteLine($"Не удалось создать условие PropertyValueEquals: {ex.Message}");
                return null;
            }
        }

        private string GenerateSearchSetName(System.Collections.Generic.List<PropertyItem> selectedProperties, string objectCategory)
        {
            // Формируем имя набора согласно требованиям:
            // Категория-объект, Свойство (по чекбоксу), Условие(=), Значение (по чекбоксу)
            var parts = new System.Collections.Generic.List<string>();
            
            // Добавляем категорию объекта
            parts.Add(objectCategory);

            // Добавляем свойства и значения в формате: Свойство = Значение
            foreach (var prop in selectedProperties)
            {
                if (prop.IsPropertySelected && prop.IsValueSelected)
                {
                    // Если выбраны и свойство, и значение: Категория-Свойство = Значение
                    parts.Add($"{prop.PropertyName}={prop.PropertyValue}");
                }
                else if (prop.IsPropertySelected)
                {
                    // Если выбрано только свойство: Категория-Свойство
                    parts.Add(prop.PropertyName);
                }
                else if (prop.IsValueSelected)
                {
                    // Если выбрано только значение: Категория- = Значение
                    parts.Add($"={prop.PropertyValue}");
                }
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
