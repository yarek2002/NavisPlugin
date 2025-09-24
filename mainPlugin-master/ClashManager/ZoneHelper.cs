// ZoneHelper.cs - общий класс для работы с зонами
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;

namespace ClashManager
{
    /// <summary>
    /// Класс для работы с зонами
    /// </summary>
    public class ZoneHelper
    {
        private Document _doc;
        private Model _selectedZoneModel;

        public ZoneHelper()
        {
            _doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
            LoadSelectedZoneModel();
        }

        private void LogToFile(string message)
        {
            string logPath = @"C:\temp\ZoneHelperDebug.txt";
            try
            {
                System.IO.File.AppendAllText(logPath, $"{DateTime.Now:HH:mm:ss.fff}: {message}{Environment.NewLine}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка записи лога: {ex.Message}");
            }
        }

        /// <summary>
        /// Загружает выбранную модель с зонами из настроек
        /// </summary>
        private void LoadSelectedZoneModel()
        {
            try
            {
                string selectedFileName = Properties.Settings.Default.SelectedZoneNwcFile;
                if (!string.IsNullOrEmpty(selectedFileName))
                {
                    _selectedZoneModel = _doc.Models.FirstOrDefault(m => m.FileName == selectedFileName);
                }
            }
            catch
            {
                // Игнорируем ошибки загрузки настроек
            }
        }

        /// <summary>
        /// Проверяет, есть ли выбранная модель с зонами
        /// </summary>
        public bool HasSelectedZoneModel()
        {
            return _selectedZoneModel != null;
        }

        /// <summary>
        /// Находит зоны в выбранной модели
        /// </summary>
        public List<ZoneItem> GetZones()
        {
            // Очищаем лог файл в начале
            string logPath = @"C:\temp\ZoneHelperDebug.txt";
            try
            {
                System.IO.File.WriteAllText(logPath, $"=== НАЧАЛО ЗАГРУЗКИ ЗОН {DateTime.Now} ==={Environment.NewLine}");
            }
            catch { }

            if (_selectedZoneModel == null) 
            {
                LogToFile("ZoneHelper: _selectedZoneModel == null");
                return new List<ZoneItem>();
            }
            
            LogToFile($"ZoneHelper: Загружаем зоны из модели: {_selectedZoneModel.FileName}");
            var zones = FindZonesInModel(_selectedZoneModel.RootItem);
            LogToFile($"ZoneHelper: Найдено зон: {zones.Count}");
            
            foreach (var zone in zones)
            {
                if (zone.UseTriangleGeometry)
                {
                    LogToFile($"ZoneHelper: Зона '{zone.ZoneName}' - ✅ ТОЧНАЯ ГЕОМЕТРИЯ с {zone.Triangles.Count} треугольниками");
                }
                else if (zone.UsePolygonGeometry)
                {
                    LogToFile($"ZoneHelper: Зона '{zone.ZoneName}' - ✅ ПОЛИГОН с {zone.Vertices.Count} вершинами" +
                        (zone.HasZoneHeight ? $", высота: {zone.ZoneHeight:F2}" : ""));
                }
                else
                {
                    LogToFile($"ZoneHelper: Зона '{zone.ZoneName}' - ⚠️ BoundingBox: Min({zone.BoundingBox.Min.X:F2}, {zone.BoundingBox.Min.Y:F2}, {zone.BoundingBox.Min.Z:F2}) Max({zone.BoundingBox.Max.X:F2}, {zone.BoundingBox.Max.Y:F2}, {zone.BoundingBox.Max.Z:F2})");
                }
            }
            
            return zones;
        }

        /// <summary>
        /// Находит зоны в указанной модели
        /// </summary>
        private List<ZoneItem> FindZonesInModel(ModelItem rootItem)
        {
            var zones = new List<ZoneItem>();
            try
            {
                LogToFile($"FindZonesInModel: Начинаем поиск зон в корневом элементе: {rootItem?.DisplayName ?? "null"}");
                
                var zoneCandidates = FindZoneCandidates(rootItem);
                LogToFile($"FindZonesInModel: Найдено кандидатов в зоны: {zoneCandidates.Count}");

                foreach (var item in zoneCandidates)
                {
                    LogToFile($"FindZonesInModel: Обрабатываем кандидата: {item?.DisplayName ?? "null"}");
                    
                    var boundingBox = GetBoundingBox(item);
                    LogToFile($"FindZonesInModel: BoundingBox: Min({boundingBox.Min.X:F2}, {boundingBox.Min.Y:F2}, {boundingBox.Min.Z:F2}) Max({boundingBox.Max.X:F2}, {boundingBox.Max.Y:F2}, {boundingBox.Max.Z:F2})");
                    
                    if (boundingBox.Min != boundingBox.Max)
                    {
                        var zoneName = GenerateZoneName(item);
                        LogToFile($"FindZonesInModel: Создаем зону с именем: '{zoneName}'");

                        // Сначала пытаемся извлечь треугольники из геометрии (точная геометрия)
                        var triangles = ExtractTrianglesFromGeometry(item);
                        bool useTriangles = triangles.Count > 0;

                        var zoneItem = new ZoneItem
                        {
                            ZoneName = zoneName,
                            ZoneObject = item,
                            BoundingBox = boundingBox,
                            Triangles = triangles,
                            UseTriangleGeometry = useTriangles
                        };

                        // Если треугольники не найдены, пытаемся извлечь полигональную геометрию из свойств
                        if (!useTriangles)
                        {
                            var vertices = TryExtractPolygonVertices(item);
                            bool usePolygon = vertices.Count >= 3;

                            zoneItem.Vertices = vertices;
                            zoneItem.UsePolygonGeometry = usePolygon;

                            // Попытка определить высоту зоны
                            if (usePolygon)
                            {
                                if (TryExtractZoneHeight(item, boundingBox, out double extractedHeight))
                                {
                                    zoneItem.ZoneHeight = extractedHeight;
                                    zoneItem.HasZoneHeight = true;
                                }

                                LogToFile($"FindZonesInModel: ✅ Зона '{zoneName}' использует полигональную геометрию с {vertices.Count} вершинами");
                                if (zoneItem.HasZoneHeight)
                                {
                                    LogToFile($"FindZonesInModel: Высота зоны: {zoneItem.ZoneHeight:F2}");
                                }
                            }
                            else
                            {
                                LogToFile($"FindZonesInModel: ⚠️ Зона '{zoneName}' использует стандартный BoundingBox");
                            }
                        }
                        else
                        {
                            LogToFile($"FindZonesInModel: ✅ Зона '{zoneName}' использует точную геометрию на основе {triangles.Count} треугольников");
                        }
                        
                        zones.Add(zoneItem);

                        if (zones.Count >= 100) break;
                    }
                    else
                    {
                        LogToFile($"FindZonesInModel: Пропускаем элемент - пустой BoundingBox");
                    }
                }
                
                LogToFile($"FindZonesInModel: Итого найдено зон: {zones.Count}");
            }
            catch (Exception ex)
            {
                LogToFile($"FindZonesInModel: Ошибка при поиске зон: {ex.Message}");
            }
            
            return zones;
        }

        /// <summary>
        /// Проверяет, находится ли группа коллизий в зоне
        /// </summary>
        public string GetZoneForGroup(ClashResultGroup group)
        {
            if (_selectedZoneModel == null) return null;

            var zones = GetZones();
            var allResults = GetAllResultsFromGroup(group);

            foreach (var result in allResults)
            {
                foreach (var zone in zones)
                {
                    if (IsClashInsideZone(result, zone))
                    {
                        return zone.ZoneName;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Применяет зонирование к группе коллизий
        /// </summary>
        public void ApplyZoneToGroup(ClashResultGroup group)
        {
            var zoneName = GetZoneForGroup(group);
            if (!string.IsNullOrEmpty(zoneName))
            {
                string currentName = group.DisplayName ?? "";
                if (!currentName.Contains(zoneName))
                {
                    group.DisplayName = string.IsNullOrEmpty(currentName)
                        ? zoneName
                        : $"{zoneName} | {currentName}";
                }
            }
        }

        // Остальные вспомогательные методы из ZoneAssignmentView...
        private List<ModelItem> FindZoneCandidates(ModelItem rootItem)
        {
            var candidates = new List<ModelItem>();
            LogToFile($"FindZoneCandidates: Начинаем поиск кандидатов в зоны");
            TraverseModel(rootItem, candidates, 15, 0); // Увеличиваем глубину с 5 до 15
            LogToFile($"FindZoneCandidates: Найдено кандидатов: {candidates.Count}");
            return candidates;
        }

        private void TraverseModel(ModelItem item, List<ModelItem> candidates, int maxDepth = 10, int currentDepth = 0)
        {
            if (item == null || currentDepth >= maxDepth) 
            {
                if (item == null) LogToFile($"TraverseModel: item == null на глубине {currentDepth}");
                if (currentDepth >= maxDepth) LogToFile($"TraverseModel: достигнута максимальная глубина {maxDepth}");
                return;
            }

            try
            {
                LogToFile($"TraverseModel: Обрабатываем элемент '{item.DisplayName}' на глубине {currentDepth}, Geometry: {item.Geometry != null}, IsHidden: {item.IsHidden}, ClassDisplayName: '{item.ClassDisplayName}'");
                
                if (item.Geometry != null)
                {
                    // Проверяем, является ли объект потенциальной зоной
                    bool isPotentialZone = IsPotentialZone(item);
                    
                    if (isPotentialZone)
                    {
                        candidates.Add(item);
                        LogToFile($"TraverseModel: ✅ НАЙДЕН КАНДИДАТ ЗОНЫ: '{item.DisplayName}' (Class: '{item.ClassDisplayName}') на глубине {currentDepth}, всего кандидатов: {candidates.Count}");
                        if (candidates.Count >= 1000) return;
                    }
                    else
                    {
                        LogToFile($"TraverseModel: ⚠️ Элемент '{item.DisplayName}' имеет геометрию, но не подходит как зона");
                    }
                }
                else
                {
                    LogToFile($"TraverseModel:  Элемент '{item.DisplayName}' не имеет геометрии");
                }
            }
            catch (Exception ex)
            {
                LogToFile($"TraverseModel: Ошибка при обработке элемента '{item?.DisplayName}': {ex.Message}");
            }

            try
            {
                // Проверяем, что Children является коллекцией
                if (item.Children is System.Collections.IEnumerable childrenEnumerable)
                {
                    int childCount = 0;
                    foreach (var child in childrenEnumerable)
                    {
                        childCount++;
                    }
                    LogToFile($"TraverseModel: У элемента '{item.DisplayName}' {childCount} дочерних элементов");
                    
                    foreach (var child in childrenEnumerable)
                    {
                        if (child is ModelItem childItem)
                        {
                            TraverseModel(childItem, candidates, maxDepth, currentDepth + 1);
                            if (candidates.Count >= 1000) return;
                        }
                    }
                }
                else
                {
                    LogToFile($"TraverseModel: Элемент '{item.DisplayName}' не имеет дочерних элементов или Children не является коллекцией");
                }
            }
            catch (Exception ex)
            {
                LogToFile($"TraverseModel: Ошибка при обходе дочерних элементов '{item?.DisplayName}': {ex.Message}");
            }
        }

        /// <summary>
        /// Проверяет, является ли объект потенциальной зоной
        /// </summary>
        private bool IsPotentialZone(ModelItem item)
        {
            try
            {
                // Проверяем по имени класса
                string className = item.ClassDisplayName?.ToLower() ?? "";
                string displayName = item.DisplayName?.ToLower() ?? "";
                
                // Исключаем объекты, которые точно не являются зонами
                string[] excludeClasses = { "стена", "wall", "дверь", "door", "окно", "window", "потолок", "ceiling", 
                                          "пол", "floor", "колонна", "column", "балка", "beam", "труба", "pipe",
                                          "кабель", "cable", "воздуховод", "duct", "элемент", "element" };
                
                foreach (var excludeClass in excludeClasses)
                {
                    if (className.Contains(excludeClass) || displayName.Contains(excludeClass))
                    {
                        LogToFile($"IsPotentialZone: Исключаем '{item.DisplayName}' - содержит '{excludeClass}'");
                        return false;
                    }
                }
                
                // Ищем признаки зоны в свойствах
                bool hasZoneProperties = false;
                foreach (var category in item.PropertyCategories)
                {
                    foreach (var property in category.Properties)
                    {
                        if (property?.DisplayName != null && property.Value != null)
                        {
                            var propName = property.DisplayName.ToLower();
                            var propValue = property.Value.ToString().ToLower();
                            
                            // Ищем свойства, указывающие на зону
                            if (propName.Contains("зона") || propName.Contains("zone") ||
                                propName.Contains("этаж") || propName.Contains("floor") ||
                                propName.Contains("комментар") || propName.Contains("comment") ||
                                propName.Contains("назначение") || propName.Contains("purpose"))
                            {
                                hasZoneProperties = true;
                                LogToFile($"IsPotentialZone: Найдено свойство зоны '{property.DisplayName}' = '{property.Value}'");
                                break;
                            }
                        }
                    }
                    if (hasZoneProperties) break;
                }
                
                // Если есть свойства зоны или это геометрический объект без явных исключений
                if (hasZoneProperties || (!string.IsNullOrEmpty(className) && !excludeClasses.Any(c => className.Contains(c))))
                {
                    LogToFile($"IsPotentialZone: ✅ '{item.DisplayName}' - потенциальная зона (свойства: {hasZoneProperties}, класс: '{className}')");
                    return true;
                }
                
                LogToFile($"IsPotentialZone: ❌ '{item.DisplayName}' - не подходит как зона");
                return false;
            }
            catch (Exception ex)
            {
                LogToFile($"IsPotentialZone: Ошибка проверки '{item?.DisplayName}': {ex.Message}");
                return false;
            }
        }

        private BoundingBox3D GetBoundingBox(ModelItem item)
        {
            try
            {
                if (item?.Geometry != null)
                {
                    return item.Geometry.BoundingBox;
                }
                return new BoundingBox3D();
            }
            catch
            {
                return new BoundingBox3D();
            }
        }

		/// <summary>
		/// Извлекает треугольники из геометрии объекта зоны (рекурсивно по дереву ModelItem)
		/// </summary>
		private List<(Point3D, Point3D, Point3D)> ExtractTrianglesFromGeometry(ModelItem item)
		{
			var triangles = new List<(Point3D, Point3D, Point3D)>();
			try
			{
				if (item == null)
					return triangles;

				LogToFile($"ExtractTrianglesFromGeometry: Извлекаем треугольники из '{item.DisplayName}' (COM API)");

				// 1) Извлечь геометрию текущего элемента через COM API (InwOaTriMeshGeom)
				try
				{
					var comPath = Autodesk.Navisworks.Api.ComApi.ComApiBridge.ToInwOaPath(item);
					var state = Autodesk.Navisworks.Api.ComApi.ComApiBridge.State;
					LogToFile($"ExtractTrianglesFromGeometry: COM Path создан: {comPath != null}");
					LogToFile($"ExtractTrianglesFromGeometry: State получен: {state != null}");
					
					// Получаем коллекцию геометрии фрагментов (тип: InwLGeomColl) через рефлексию
					var getGeometryMethod = state.GetType().GetMethod("GetGeometry");
					LogToFile($"ExtractTrianglesFromGeometry: GetGeometry метод найден: {getGeometryMethod != null}");
					
					var geomColl = getGeometryMethod?.Invoke(state, new object[] { comPath, true });
					LogToFile($"ExtractTrianglesFromGeometry: Геометрия получена: {geomColl != null}");
					
					if (geomColl != null && geomColl is System.Collections.IEnumerable geomEnumerable)
					{
						int fragmentCount = 0;
						foreach (var frag in geomEnumerable)
						{
							fragmentCount++;
							LogToFile($"ExtractTrianglesFromGeometry: Обрабатываем фрагмент #{fragmentCount}");
							
							object geomObj = null;
							var getGeomMethod = frag.GetType().GetMethod("GetGeom");
							if (getGeomMethod != null)
							{
								var args = new object[] { null };
								getGeomMethod.Invoke(frag, args);
								geomObj = args[0];
								LogToFile($"ExtractTrianglesFromGeometry: Геометрия фрагмента #{fragmentCount}: {geomObj != null}");
							}
							if (geomObj == null) continue;

							// Ищем треугольную сетку (InwOaTriMeshGeom) без прямой ссылки на тип
							var typeName = geomObj.GetType().Name;
							LogToFile($"ExtractTrianglesFromGeometry: Тип геометрии фрагмента #{fragmentCount}: {typeName}");
							
							if (string.Equals(typeName, "InwOaTriMeshGeom", StringComparison.Ordinal))
							{
								LogToFile($"ExtractTrianglesFromGeometry: Найден InwOaTriMeshGeom в фрагменте #{fragmentCount}");
								
								var triType = geomObj.GetType();
								var getCoords = triType.GetMethod("get_Coords");
								var getCoordIndex = triType.GetMethod("get_CoordIndex");
								
								LogToFile($"ExtractTrianglesFromGeometry: get_Coords метод: {getCoords != null}");
								LogToFile($"ExtractTrianglesFromGeometry: get_CoordIndex метод: {getCoordIndex != null}");
								
								if (getCoords == null || getCoordIndex == null) continue;
								
								var coords = (Array)getCoords.Invoke(geomObj, null);
								var indices = (Array)getCoordIndex.Invoke(geomObj, null);
								
								LogToFile($"ExtractTrianglesFromGeometry: Координаты получены: {coords?.Length ?? 0} точек");
								LogToFile($"ExtractTrianglesFromGeometry: Индексы получены: {indices?.Length ?? 0} индексов");

								// coords: x1,y1,z1, x2,y2,z2, ...
								// indices: i1,i2,i3, -1, i4,i5,i6, -1, ... (полилинии/полигоны)
								var points = new List<Point3D>();
								for (int i = 0; i + 2 < coords.Length; i += 3)
								{
									double x = Convert.ToDouble(coords.GetValue(i));
									double y = Convert.ToDouble(coords.GetValue(i + 1));
									double z = Convert.ToDouble(coords.GetValue(i + 2));
									points.Add(new Point3D(x, y, z));
								}
								LogToFile($"ExtractTrianglesFromGeometry: Создано точек: {points.Count}");

								var current = new List<int>();
								int triangleCount = 0;
								for (int k = 0; k < indices.Length; k++)
								{
									int idx = Convert.ToInt32(indices.GetValue(k));
									if (idx == -1)
									{
										// триангулируем текущий полигон fan-ом
										for (int t = 1; t + 1 < current.Count; t++)
										{
											int a = current[0], b = current[t], c = current[t + 1];
											if (a >= 0 && b >= 0 && c >= 0 &&
												a < points.Count && b < points.Count && c < points.Count)
											{
												triangles.Add((points[a], points[b], points[c]));
												triangleCount++;
											}
										}
										current.Clear();
									}
									else
									{
										current.Add(idx);
									}
								}
								LogToFile($"ExtractTrianglesFromGeometry: Создано треугольников из фрагмента #{fragmentCount}: {triangleCount}");
							}
						}
						LogToFile($"ExtractTrianglesFromGeometry: Всего обработано фрагментов: {fragmentCount}");
					}
					else
					{
						LogToFile($"ExtractTrianglesFromGeometry: Геометрия не получена или не является IEnumerable");
					}
				}
				catch (Exception ex)
				{
					LogToFile($"ExtractTrianglesFromGeometry COM: Ошибка: {ex.Message}");
				}

				// 2) Альтернативный способ через стандартный Navisworks API
				if (triangles.Count == 0)
				{
					try
					{
						LogToFile($"ExtractTrianglesFromGeometry: Пробуем альтернативный способ через Navisworks API");
						
						// Получаем геометрию через стандартный API
						var geometry = item.Geometry;
						LogToFile($"ExtractTrianglesFromGeometry: Стандартная геометрия получена: {geometry != null}");
						
						if (geometry != null)
						{
							LogToFile($"ExtractTrianglesFromGeometry: Тип геометрии: {geometry.GetType().Name}");
							
							// Пытаемся получить треугольники через рефлексию
							var geometryType = geometry.GetType();
							var getCoordsMethod = geometryType.GetMethod("get_Coords");
							var getCoordIndexMethod = geometryType.GetMethod("get_CoordIndex");
							
							if (getCoordsMethod != null && getCoordIndexMethod != null)
							{
								LogToFile($"ExtractTrianglesFromGeometry: Найдены методы get_Coords и get_CoordIndex в геометрии");
								
								var coords = (Array)getCoordsMethod.Invoke(geometry, null);
								var indices = (Array)getCoordIndexMethod.Invoke(geometry, null);
								
								LogToFile($"ExtractTrianglesFromGeometry: Координаты из геометрии: {coords?.Length ?? 0}, Индексы: {indices?.Length ?? 0}");
								
								if (coords != null && indices != null && coords.Length > 0 && indices.Length > 0)
								{
									// Создаем точки из координат
									var points = new List<Point3D>();
									for (int i = 0; i + 2 < coords.Length; i += 3)
									{
										double x = Convert.ToDouble(coords.GetValue(i));
										double y = Convert.ToDouble(coords.GetValue(i + 1));
										double z = Convert.ToDouble(coords.GetValue(i + 2));
										points.Add(new Point3D(x, y, z));
									}
									
									// Создаем треугольники из индексов
									var current = new List<int>();
									int triangleCount = 0;
									for (int k = 0; k < indices.Length; k++)
									{
										int idx = Convert.ToInt32(indices.GetValue(k));
										if (idx == -1)
										{
											// Триангулируем текущий полигон
											for (int t = 1; t + 1 < current.Count; t++)
											{
												int a = current[0], b = current[t], c = current[t + 1];
												if (a >= 0 && b >= 0 && c >= 0 &&
													a < points.Count && b < points.Count && c < points.Count)
												{
													triangles.Add((points[a], points[b], points[c]));
													triangleCount++;
												}
											}
											current.Clear();
										}
										else
										{
											current.Add(idx);
										}
									}
									
									LogToFile($"ExtractTrianglesFromGeometry: Создано треугольников из геометрии: {triangleCount}");
								}
							}
							else
							{
								LogToFile($"ExtractTrianglesFromGeometry: Методы get_Coords/get_CoordIndex не найдены в геометрии");
							}
						}
					}
					catch (Exception ex)
					{
						LogToFile($"ExtractTrianglesFromGeometry API: Ошибка: {ex.Message}");
					}
				}

				// 3) Рекурсивно обходим детей
				if (item.Children is System.Collections.IEnumerable childrenEnumerable)
				{
					foreach (var child in childrenEnumerable)
					{
						if (child is ModelItem childItem)
						{
							var childTriangles = ExtractTrianglesFromGeometry(childItem);
							if (childTriangles.Count > 0)
								triangles.AddRange(childTriangles);
						}
					}
				}

				LogToFile($"ExtractTrianglesFromGeometry: Итого треугольников собрано: {triangles.Count}");
			}
			catch (Exception ex)
			{
				LogToFile($"ExtractTrianglesFromGeometry: Ошибка: {ex.Message}");
			}
			return triangles;
		}

		/// <summary>
		/// Извлекает треугольники из фрагмента геометрии через стандартный Navisworks API
		/// </summary>
		private List<(Point3D, Point3D, Point3D)> ExtractTrianglesFromFragment(object fragment)
		{
			var triangles = new List<(Point3D, Point3D, Point3D)>();
			
			try
			{
				LogToFile($"ExtractTrianglesFromFragment: Обрабатываем фрагмент типа {fragment.GetType().Name}");
				
				// Пытаемся получить треугольники через рефлексию
				var fragmentType = fragment.GetType();
				
				// Ищем методы для получения координат и индексов
				var getCoordsMethod = fragmentType.GetMethod("get_Coords");
				var getCoordIndexMethod = fragmentType.GetMethod("get_CoordIndex");
				
				if (getCoordsMethod != null && getCoordIndexMethod != null)
				{
					LogToFile($"ExtractTrianglesFromFragment: Найдены методы get_Coords и get_CoordIndex");
					
					var coords = (Array)getCoordsMethod.Invoke(fragment, null);
					var indices = (Array)getCoordIndexMethod.Invoke(fragment, null);
					
					LogToFile($"ExtractTrianglesFromFragment: Координаты: {coords?.Length ?? 0}, Индексы: {indices?.Length ?? 0}");
					
					if (coords != null && indices != null)
					{
						// Создаем точки из координат
						var points = new List<Point3D>();
						for (int i = 0; i + 2 < coords.Length; i += 3)
						{
							double x = Convert.ToDouble(coords.GetValue(i));
							double y = Convert.ToDouble(coords.GetValue(i + 1));
							double z = Convert.ToDouble(coords.GetValue(i + 2));
							points.Add(new Point3D(x, y, z));
						}
						
						// Создаем треугольники из индексов
						var current = new List<int>();
						for (int k = 0; k < indices.Length; k++)
						{
							int idx = Convert.ToInt32(indices.GetValue(k));
							if (idx == -1)
							{
								// Триангулируем текущий полигон
								for (int t = 1; t + 1 < current.Count; t++)
								{
									int a = current[0], b = current[t], c = current[t + 1];
									if (a >= 0 && b >= 0 && c >= 0 &&
										a < points.Count && b < points.Count && c < points.Count)
									{
										triangles.Add((points[a], points[b], points[c]));
									}
								}
								current.Clear();
							}
							else
							{
								current.Add(idx);
							}
						}
						
						LogToFile($"ExtractTrianglesFromFragment: Создано треугольников: {triangles.Count}");
					}
				}
				else
				{
					LogToFile($"ExtractTrianglesFromFragment: Методы get_Coords/get_CoordIndex не найдены");
				}
			}
			catch (Exception ex)
			{
				LogToFile($"ExtractTrianglesFromFragment: Ошибка: {ex.Message}");
			}
			
			return triangles;
		}

        /// <summary>
        /// Пытается извлечь вершины полигона из свойств объекта зоны (резервный метод)
        /// </summary>
        private List<Point3D> TryExtractPolygonVertices(ModelItem item)
        {
            var vertices = new List<Point3D>();

            try
            {
                LogToFile($"TryExtractPolygonVertices: Ищем вершины полигона в элементе '{item.DisplayName}'");

                foreach (var category in item.PropertyCategories)
                {
                    LogToFile($"TryExtractPolygonVertices: Проверяем категорию '{category.DisplayName}'");

                    foreach (var property in category.Properties)
                    {
                        if (property?.DisplayName != null && property.Value != null)
                        {
                            var propName = property.DisplayName.ToLower();
                            var propValue = property.Value.ToString();

                            // Поиск координат вершин в различных форматах
                            if (propName.Contains("vertex") || propName.Contains("point") ||
                                propName.Contains("coordinate") || propName.Contains("corner") ||
                                propName.Contains("вершина") || propName.Contains("точка") ||
                                propName.Contains("координата") || propName.Contains("угол"))
                            {
                                LogToFile($"TryExtractPolygonVertices: Найдено свойство с координатами '{property.DisplayName}' = '{propValue}'");

                                if (TryParsePointFromProperty(propValue, out Point3D point))
                                {
                                    vertices.Add(point);
                                    LogToFile($"TryExtractPolygonVertices: Добавлена вершина ({point.X:F2}, {point.Y:F2}, {point.Z:F2})");
                                }
                            }

                            // Поиск полного списка координат в одном свойстве
                            if (propName.Contains("polygon") || propName.Contains("boundary") ||
                                propName.Contains("outline") || propName.Contains("полигон") ||
                                propName.Contains("граница") || propName.Contains("контур"))
                            {
                                LogToFile($"TryExtractPolygonVertices: Найдено свойство полигона '{property.DisplayName}' = '{propValue}'");

                                var polygonPoints = ParsePolygonFromProperty(propValue);
                                if (polygonPoints.Count > 0)
                                {
                                    vertices.AddRange(polygonPoints);
                                    LogToFile($"TryExtractPolygonVertices: Добавлено {polygonPoints.Count} вершин из полигона");
                                }
                            }
                        }
                    }
                }

                // Убираем дубликаты и сортируем по часовой стрелке (если это 2D полигон)
                if (vertices.Count >= 3)
                {
                    vertices = RemoveDuplicateVertices(vertices);
                    vertices = SortVerticesClockwise(vertices);
                    LogToFile($"TryExtractPolygonVertices: Итого найдено {vertices.Count} уникальных вершин");
                }
                else
                {
                    LogToFile($"TryExtractPolygonVertices: Недостаточно вершин для полигона ({vertices.Count} < 3)");
                }
            }
            catch (Exception ex)
            {
                LogToFile($"TryExtractPolygonVertices: Ошибка извлечения вершин: {ex.Message}");
            }

            return vertices;
        }

        /// <summary>
        /// Парсит координаты точки из строки
        /// </summary>
        private bool TryParsePointFromProperty(string value, out Point3D point)
        {
            point = new Point3D(0, 0, 0); // Значение по умолчанию
            
            try
            {
                if (string.IsNullOrWhiteSpace(value)) return false;
                
                // Очищаем строку от скобок и лишних символов
                var cleanValue = value.Trim()
                    .Replace("(", "").Replace(")", "")
                    .Replace("[", "").Replace("]", "")
                    .Replace("{", "").Replace("}", "");
                
                // Разделяем по различным разделителям
                var parts = cleanValue.Split(new char[] { ',', ';', ' ', '\t', '|' }, 
                    StringSplitOptions.RemoveEmptyEntries);
                
                if (parts.Length >= 3)
                {
                    if (double.TryParse(parts[0], out double x) &&
                        double.TryParse(parts[1], out double y) &&
                        double.TryParse(parts[2], out double z))
                    {
                        point = new Point3D(x, y, z);
                        return true;
                    }
                }
                else if (parts.Length == 2)
                {
                    // 2D координаты - добавляем Z = 0
                    if (double.TryParse(parts[0], out double x) &&
                        double.TryParse(parts[1], out double y))
                    {
                        point = new Point3D(x, y, 0);
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                LogToFile($"TryParsePointFromProperty: Ошибка парсинга '{value}': {ex.Message}");
            }
            
            return false;
        }

        /// <summary>
        /// Парсит множественные координаты из одного свойства
        /// </summary>
        private List<Point3D> ParsePolygonFromProperty(string value)
        {
            var points = new List<Point3D>();
            
            try
            {
                if (string.IsNullOrWhiteSpace(value)) return points;
                
                // Попробуем найти координаты в различных форматах
                // Формат: "x1,y1,z1;x2,y2,z2;x3,y3,z3"
                var pointStrings = value.Split(new char[] { ';', '|', '\n' }, 
                    StringSplitOptions.RemoveEmptyEntries);
                
                foreach (var pointStr in pointStrings)
                {
                    if (TryParsePointFromProperty(pointStr.Trim(), out Point3D point))
                    {
                        points.Add(point);
                    }
                }
                
                // Если не получилось, попробуем другой формат
                if (points.Count == 0)
                {
                    // Формат: "x1 y1 z1 x2 y2 z2 x3 y3 z3"
                    var numbers = value.Split(new char[] { ' ', ',', '\t' }, 
                        StringSplitOptions.RemoveEmptyEntries);
                    
                    for (int i = 0; i + 2 < numbers.Length; i += 3)
                    {
                        if (double.TryParse(numbers[i], out double x) &&
                            double.TryParse(numbers[i + 1], out double y) &&
                            double.TryParse(numbers[i + 2], out double z))
                        {
                            points.Add(new Point3D(x, y, z));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogToFile($"ParsePolygonFromProperty: Ошибка парсинга полигона '{value}': {ex.Message}");
            }
            
            return points;
        }

        /// <summary>
        /// Удаляет дублирующиеся вершины
        /// </summary>
        private List<Point3D> RemoveDuplicateVertices(List<Point3D> vertices)
        {
            var unique = new List<Point3D>();
            const double tolerance = 0.01; // Допуск для сравнения координат
            
            foreach (var vertex in vertices)
            {
                bool isDuplicate = false;
                foreach (var existing in unique)
                {
                    if (Math.Abs(existing.X - vertex.X) < tolerance &&
                        Math.Abs(existing.Y - vertex.Y) < tolerance &&
                        Math.Abs(existing.Z - vertex.Z) < tolerance)
                    {
                        isDuplicate = true;
                        break;
                    }
                }
                
                if (!isDuplicate)
                {
                    unique.Add(vertex);
                }
            }
            
            return unique;
        }

        /// <summary>
        /// Сортирует вершины по часовой стрелке (для 2D полигона в плоскости XY)
        /// </summary>
        private List<Point3D> SortVerticesClockwise(List<Point3D> vertices)
        {
            if (vertices.Count < 3) return vertices;
            
            try
            {
                // Находим центр полигона
                double centerX = vertices.Average(v => v.X);
                double centerY = vertices.Average(v => v.Y);
                
                // Сортируем по углу относительно центра
                return vertices.OrderBy(v => Math.Atan2(v.Y - centerY, v.X - centerX)).ToList();
            }
            catch
            {
                return vertices; // Возвращаем исходный порядок при ошибке
            }
        }

        /// <summary>
        /// Пытается определить высоту зоны из свойств объекта
        /// </summary>
        private bool TryExtractZoneHeight(ModelItem item, BoundingBox3D boundingBox, out double height)
        {
            height = 0;
            
            try
            {
                LogToFile($"TryExtractZoneHeight: Ищем высоту зоны в элементе '{item.DisplayName}'");
                
                // Сначала ищем в свойствах
                foreach (var category in item.PropertyCategories)
                {
                    foreach (var property in category.Properties)
                    {
                        if (property?.DisplayName != null && property.Value != null)
                        {
                            var propName = property.DisplayName.ToLower();
                            
                            if (propName.Contains("height") || propName.Contains("высота") ||
                                propName.Contains("thickness") || propName.Contains("толщина") ||
                                propName.Contains("depth") || propName.Contains("глубина"))
                            {
                                if (double.TryParse(property.Value.ToString(), out height) && height > 0)
                                {
                                    LogToFile($"TryExtractZoneHeight: Найдена высота в свойстве '{property.DisplayName}' = {height:F2}");
                                    return true;
                                }
                            }
                        }
                    }
                }
                
                // Если не нашли в свойствах, используем высоту BoundingBox
                double boxHeight = boundingBox.Max.Z - boundingBox.Min.Z;
                if (boxHeight > 0.1) // Минимальная значимая высота
                {
                    height = boxHeight;
                    LogToFile($"TryExtractZoneHeight: Используем высоту BoundingBox = {height:F2}");
                    return true;
                }
                
                LogToFile($"TryExtractZoneHeight: Высота зоны не определена");
                return false;
            }
            catch (Exception ex)
            {
                LogToFile($"TryExtractZoneHeight: Ошибка определения высоты: {ex.Message}");
                return false;
            }
        }

       private string GenerateZoneName(ModelItem item)
{
    try
    {
        string comment = "";
        string zoneName = "";
        string floorName = "";
        
        // Ищем различные свойства для определения имени зоны
        foreach (PropertyCategory cat in item.PropertyCategories)
        {
            LogToFile($"GenerateZoneName: Проверяем категорию '{cat.DisplayName}'");
            
            foreach (DataProperty prop in cat.Properties)
            {
                if (prop?.DisplayName != null && prop.Value != null)
                {
                    var propName = prop.DisplayName.ToLower();
                    var propValue = prop.Value.ToString();
                    
                    LogToFile($"GenerateZoneName: Свойство '{prop.DisplayName}' = '{propValue}'");
                    
                    // Ищем комментарии
                    if (propName.Contains("комментар") || propName.Contains("comment"))
                    {
                        comment = propValue;
                    }
                    // Ищем зону
                    else if (propName.Contains("зона") || propName.Contains("zone"))
                    {
                        zoneName = propValue;
                    }
                    // Ищем этаж
                    else if (propName.Contains("этаж") || propName.Contains("floor") || propName.Contains("level"))
                    {
                        floorName = propValue;
                    }
                }
            }
        }

        LogToFile($"GenerateZoneName: Элемент DisplayName='{item.DisplayName}', ClassDisplayName='{item.ClassDisplayName}'");
        LogToFile($"GenerateZoneName: Найденные свойства - комментарий: '{comment}', зона: '{zoneName}', этаж: '{floorName}'");

        // Приоритет: комментарий > зона+этаж > зона > этаж > ClassDisplayName > координаты
        if (!string.IsNullOrEmpty(comment))
        {
            LogToFile($"GenerateZoneName: Используем комментарий: '{comment}'");
            return comment;
        }
        else if (!string.IsNullOrEmpty(zoneName) && !string.IsNullOrEmpty(floorName))
        {
            var result = $"{floorName} | {zoneName}";
            LogToFile($"GenerateZoneName: Используем этаж+зона: '{result}'");
            return result;
        }
        else if (!string.IsNullOrEmpty(zoneName))
        {
            LogToFile($"GenerateZoneName: Используем зону: '{zoneName}'");
            return zoneName;
        }
        else if (!string.IsNullOrEmpty(floorName))
        {
            LogToFile($"GenerateZoneName: Используем этаж: '{floorName}'");
            return floorName;
        }
        else if (!string.IsNullOrEmpty(item.ClassDisplayName))
        {
            // Для ClassDisplayName добавляем координаты чтобы сделать уникальным
            var bbox = GetBoundingBox(item);
            var result = $"{item.ClassDisplayName}_{bbox.Min.X:F0}_{bbox.Min.Y:F0}_{bbox.Min.Z:F0}";
            LogToFile($"GenerateZoneName: Используем ClassDisplayName с координатами: '{result}'");
            return result;
        }
        else if (!string.IsNullOrEmpty(item.DisplayName))
        {
            var result = System.IO.Path.GetFileNameWithoutExtension(item.DisplayName);
            LogToFile($"GenerateZoneName: Используем DisplayName: '{result}'");
            return result;
        }
        else
        {
            // Используем координаты BoundingBox для создания уникального имени
            var bbox = GetBoundingBox(item);
            var result = $"Zone_{bbox.Min.X:F0}_{bbox.Min.Y:F0}_{bbox.Min.Z:F0}";
            LogToFile($"GenerateZoneName: Используем координаты: '{result}'");
            return result;
        }
    }
    catch (Exception ex)
    {
        var bbox = GetBoundingBox(item);
        var result = $"Zone_{bbox.Min.X:F0}_{bbox.Min.Y:F0}_{bbox.Min.Z:F0}";
        LogToFile($"GenerateZoneName: Ошибка, используем координаты: '{result}', ошибка: {ex.Message}");
        return result;
    }
}


        private string GetParameterValue(ModelItem item, string parameterName)
        {
            try
            {
                if (item == null) return null;

                LogToFile($"GetParameterValue: Ищем параметр '{parameterName}' в элементе '{item.DisplayName}'");
                
                foreach (var category in item.PropertyCategories)
                {
                    if (category == null) continue;
                    
                    LogToFile($"GetParameterValue: Проверяем категорию '{category.DisplayName}'");

                    foreach (var property in category.Properties)
                    {
                        if (property?.DisplayName != null)
                        {
                            LogToFile($"GetParameterValue: Найдено свойство '{property.DisplayName}' = '{property.Value}'");
                            
                            // Проверяем точное совпадение
                            if (property.DisplayName.Equals(parameterName, StringComparison.OrdinalIgnoreCase))
                            {
                                LogToFile($"GetParameterValue: ✅ НАЙДЕН параметр '{parameterName}' = '{property.Value}'");
                                return property.Value?.ToString();
                            }
                            
                            // Проверяем содержание
                            if (property.DisplayName.Contains(parameterName))
                            {
                                LogToFile($"GetParameterValue: ✅ НАЙДЕН параметр (по содержанию) '{property.DisplayName}' = '{property.Value}'");
                                return property.Value?.ToString();
                            }
                        }
                    }
                }

                LogToFile($"GetParameterValue: ❌ Параметр '{parameterName}' не найден");
                return null;
            }
            catch (Exception ex)
            {
                LogToFile($"GetParameterValue: Ошибка поиска параметра '{parameterName}': {ex.Message}");
                return null;
            }
        }

        private string GetComments(ModelItem item)
        {
            try
            {
                LogToFile($"GetComments: Ищем комментарии в элементе '{item.DisplayName}'");
                
                foreach (PropertyCategory cat in item.PropertyCategories)
                {
                    LogToFile($"GetComments: Проверяем категорию '{cat.DisplayName}'");
                    
                    foreach (DataProperty prop in cat.Properties)
                    {
                        if (prop?.DisplayName != null)
                        {
                            LogToFile($"GetComments: Найдено свойство '{prop.DisplayName}'");
                            
                            if (prop.DisplayName.Equals("Комментарии", StringComparison.InvariantCultureIgnoreCase) ||
                                prop.DisplayName.Equals("Comments", StringComparison.InvariantCultureIgnoreCase))
                            {
                                var result = prop.Value.ToDisplayString();
                                LogToFile($"GetComments: НАЙДЕН комментарий: '{result}'");
                                return result;
                            }
                        }
                    }
                }
                
                LogToFile($"GetComments: Комментарии не найдены");
                return "";
            }
            catch (Exception ex)
            {
                LogToFile($"GetComments: Ошибка поиска комментариев: {ex.Message}");
                return "";
            }
        }

        public bool IsClashInsideZone(ClashResult clash, ZoneItem zone)
        {
            try
            {
                // Сначала попробуем использовать центр коллизии напрямую
                Point3D centerPoint;
                
                try
                {
                    centerPoint = clash.Center;
                    LogToFile($"Используем clash.Center: ({centerPoint.X:F2}, {centerPoint.Y:F2}, {centerPoint.Z:F2})");
                }
                catch
                {
                    // Если clash.Center не работает, вычисляем центр через BoundingBox элементов
                    var item1 = clash.CompositeItem1;
                    var item2 = clash.CompositeItem2;

                    var box1 = GetBoundingBox(item1);
                    var box2 = GetBoundingBox(item2);

                    if (box1.Min != box1.Max && box2.Min != box2.Max)
                    {
                        var centerX = (box1.Min.X + box1.Max.X + box2.Min.X + box2.Max.X) / 4;
                        var centerY = (box1.Min.Y + box1.Max.Y + box2.Min.Y + box2.Max.Y) / 4;
                        var centerZ = (box1.Min.Z + box1.Max.Z + box2.Min.Z + box2.Max.Z) / 4;

                        centerPoint = new Point3D(centerX, centerY, centerZ);
                        LogToFile($"Вычислили центр через BoundingBox: ({centerX:F2}, {centerY:F2}, {centerZ:F2})");
                    }
                    else
                    {
                        LogToFile("Не удалось получить координаты коллизии");
                        return false;
                    }
                }
                
                // Проверяем, что координаты не являются дефолтными
                if (Math.Abs(centerPoint.X - 0.5) < 0.01 && Math.Abs(centerPoint.Y - 0.5) < 0.01 && Math.Abs(centerPoint.Z - 0.5) < 0.01)
                {
                    LogToFile("ВНИМАНИЕ: Центр коллизии имеет дефолтные координаты (0.5, 0.5, 0.5)");
                    return false;
                }
                
                if (zone.UsePolygonGeometry)
                {
                    LogToFile($"Зона '{zone.ZoneName}': полигон из {zone.Vertices.Count} вершин");
                    for (int i = 0; i < zone.Vertices.Count; i++)
                    {
                        var v = zone.Vertices[i];
                        LogToFile($"  Вершина {i}: ({v.X:F2}, {v.Y:F2}, {v.Z:F2})");
                    }
                }
                else
                {
                    LogToFile($"Зона '{zone.ZoneName}' Box: Min({zone.BoundingBox.Min.X:F2}, {zone.BoundingBox.Min.Y:F2}, {zone.BoundingBox.Min.Z:F2}) Max({zone.BoundingBox.Max.X:F2}, {zone.BoundingBox.Max.Y:F2}, {zone.BoundingBox.Max.Z:F2})");
                }
                
                bool isInside = IsPointInsideZone(centerPoint, zone);
                LogToFile($"Коллизия внутри зоны: {isInside}");
                
                return isInside;
            }
            catch (Exception ex)
            {
                LogToFile($"Ошибка в IsClashInsideZone: {ex.Message}");
                return false;
            }
        }

        private bool IsPointInsideBox(Point3D point, BoundingBox3D box)
        {
            return point.X >= box.Min.X && point.X <= box.Max.X &&
                   point.Y >= box.Min.Y && point.Y <= box.Max.Y &&
                   point.Z >= box.Min.Z && point.Z <= box.Max.Z;
        }

        /// <summary>
        /// Проверяет, находится ли точка внутри зоны (треугольники, полигон или BoundingBox)
        /// </summary>
        private bool IsPointInsideZone(Point3D point, ZoneItem zone)
        {
            // Приоритет: треугольники > полигон > BoundingBox
            if (zone.UseTriangleGeometry && zone.Triangles.Count > 0)
            {
				// Используем точную геометрию на основе треугольников (point-in-mesh через RayCast)
				bool isInsideTriangles = IsPointInsideTriangles(point, zone.Triangles);
                LogToFile($"IsPointInsideZone: Треугольная проверка - {(isInsideTriangles ? "внутри" : "снаружи")}");
                return isInsideTriangles;
            }
            else if (zone.UsePolygonGeometry && zone.Vertices.Count >= 3)
            {
                // Используем полигональную геометрию
                bool isInsidePolygon = IsPointInsidePolygon(point, zone.Vertices);

                // Дополнительная проверка по высоте, если задана
                if (isInsidePolygon && zone.HasZoneHeight)
                {
                    double zoneBaseZ = zone.Vertices.Min(v => v.Z);
                    double zoneTopZ = zoneBaseZ + zone.ZoneHeight;

                    if (point.Z < zoneBaseZ || point.Z > zoneTopZ)
                    {
                        LogToFile($"IsPointInsideZone: Точка вне зоны по высоте. Point.Z={point.Z:F2}, Zone Z={zoneBaseZ:F2}-{zoneTopZ:F2}");
                        return false;
                    }
                }

                LogToFile($"IsPointInsideZone: Полигональная проверка - {(isInsidePolygon ? "внутри" : "снаружи")}");
                return isInsidePolygon;
            }
            else
            {
                // Используем стандартный BoundingBox
                bool isInsideBox = IsPointInsideBox(point, zone.BoundingBox);
                LogToFile($"IsPointInsideZone: BoundingBox проверка - {(isInsideBox ? "внутри" : "снаружи")}");
                return isInsideBox;
            }
        }

        /// <summary>
        /// Алгоритм Ray Casting для определения точки внутри полигона (2D)
        /// </summary>
        private bool IsPointInsidePolygon(Point3D point, List<Point3D> vertices)
        {
            if (vertices.Count < 3) return false;
            
            try
            {
                LogToFile($"IsPointInsidePolygon: Проверяем точку ({point.X:F2}, {point.Y:F2}) в полигоне из {vertices.Count} вершин");
                
                int intersectCount = 0;
                int vertexCount = vertices.Count;
                
                for (int i = 0; i < vertexCount; i++)
                {
                    var v1 = vertices[i];
                    var v2 = vertices[(i + 1) % vertexCount];
                    
                    // Проверяем пересечение луча с ребром полигона
                    if (((v1.Y > point.Y) != (v2.Y > point.Y)) &&
                        (point.X < (v2.X - v1.X) * (point.Y - v1.Y) / (v2.Y - v1.Y) + v1.X))
                    {
                        intersectCount++;
                    }
                }
                
                // Нечетное количество пересечений = точка внутри
                bool isInside = (intersectCount % 2) == 1;
                LogToFile($"IsPointInsidePolygon: Количество пересечений = {intersectCount}, результат = {isInside}");
                
                return isInside;
            }
            catch (Exception ex)
            {
                LogToFile($"IsPointInsidePolygon: Ошибка в алгоритме: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Проверяет, находится ли точка внутри зоны, используя треугольники
        /// </summary>
		private bool IsPointInsideTriangles(Point3D point, List<(Point3D, Point3D, Point3D)> triangles)
        {
			// Реализация point-in-mesh через рейкаст: считаем пересечения луча с мешем
			try
			{
				LogToFile($"IsPointInsideTriangles: RayCast проверка точки ({point.X:F2}, {point.Y:F2}, {point.Z:F2}) по {triangles.Count} треугольникам");

				// Луч вдоль +X (можно менять направление при необходимости)
				var rayOrigin = point;
				var rayDir = new Point3D(1, 0, 0);
				int hits = 0;
				const double epsilon = 1e-7;

				foreach (var tri in triangles)
				{
					double t;
					if (RayIntersectsTriangle(rayOrigin, rayDir, tri, out t))
					{
						// Считаем только пересечения в положительном направлении
						if (t > epsilon)
							hits++;
					}
				}

				bool inside = (hits % 2) == 1;
				LogToFile($"IsPointInsideTriangles: Пересечений={hits}, внутри={inside}");
				return inside;
			}
			catch (Exception ex)
			{
				LogToFile($"IsPointInsideTriangles: Ошибка RayCast: {ex.Message}");
				return false;
			}
        }

        /// <summary>
        /// Проверяет, находится ли точка внутри треугольника
        /// </summary>
		private bool IsPointInsideTriangle(Point3D point, (Point3D, Point3D, Point3D) triangle)
        {
            try
            {
                // Получаем вершины треугольника
                var v1 = triangle.Item1;
                var v2 = triangle.Item2;
                var v3 = triangle.Item3;

                // Используем barycentric coordinates для проверки
                var barycentric = CalculateBarycentricCoordinates(point, v1, v2, v3);

                // Точка внутри треугольника, если все barycentric координаты >= 0 и <= 1
                bool isInside = barycentric.U >= 0 && barycentric.U <= 1 &&
                               barycentric.V >= 0 && barycentric.V <= 1 &&
                               barycentric.W >= 0 && barycentric.W <= 1;

                return isInside;
            }
            catch (Exception ex)
            {
                LogToFile($"IsPointInsideTriangle: Ошибка: {ex.Message}");
                return false;
            }
        }

		/// <summary>
		/// Пересечение луча и треугольника (Möller–Trumbore). Возвращает true, если есть пересечение, и t по лучу.
		/// </summary>
		private bool RayIntersectsTriangle(Point3D rayOrigin, Point3D rayDir, (Point3D, Point3D, Point3D) triangle, out double t)
		{
			// Инициализируем выходное значение
			t = 0;
			const double epsilon = 1e-8;

			var v0 = triangle.Item1;
			var v1 = triangle.Item2;
			var v2 = triangle.Item3;

			// Векторы треугольника
			var edge1 = new Point3D(v1.X - v0.X, v1.Y - v0.Y, v1.Z - v0.Z);
			var edge2 = new Point3D(v2.X - v0.X, v2.Y - v0.Y, v2.Z - v0.Z);

			// P = D x edge2
			var pvec = CrossProduct(rayDir, edge2);
			double det = DotProduct(edge1, pvec);

			// Если детерминант близок к нулю — луч параллелен плоскости
			if (det > -epsilon && det < epsilon)
				return false;

			double invDet = 1.0 / det;

			// T = O - v0
			var tvec = new Point3D(rayOrigin.X - v0.X, rayOrigin.Y - v0.Y, rayOrigin.Z - v0.Z);

			// u = (T . P) * invDet
			double u = DotProduct(tvec, pvec) * invDet;
			if (u < 0 || u > 1) return false;

			// Q = T x edge1
			var qvec = CrossProduct(tvec, edge1);

			// v = (D . Q) * invDet
			double v = DotProduct(rayDir, qvec) * invDet;
			if (v < 0 || u + v > 1) return false;

			// t = (edge2 . Q) * invDet
			t = DotProduct(edge2, qvec) * invDet;
			return t >= -epsilon; // допускаем касание
		}

		/// <summary>
		/// Векторное произведение
		/// </summary>
		private Point3D CrossProduct(Point3D a, Point3D b)
		{
			return new Point3D(
				a.Y * b.Z - a.Z * b.Y,
				a.Z * b.X - a.X * b.Z,
				a.X * b.Y - a.Y * b.X
			);
		}

        /// <summary>
        /// Вычисляет barycentric координаты точки относительно треугольника
        /// </summary>
        private (double U, double V, double W) CalculateBarycentricCoordinates(Point3D point, Point3D v1, Point3D v2, Point3D v3)
        {
            try
            {
                // Векторы треугольника
                var v1v2 = new Point3D(v2.X - v1.X, v2.Y - v1.Y, v2.Z - v1.Z);
                var v1v3 = new Point3D(v3.X - v1.X, v3.Y - v1.Y, v3.Z - v1.Z);
                var v1p = new Point3D(point.X - v1.X, point.Y - v1.Y, point.Z - v1.Z);

                // Вычисляем dot products
                double dot11 = DotProduct(v1v2, v1v2);
                double dot12 = DotProduct(v1v2, v1v3);
                double dot1p = DotProduct(v1v2, v1p);
                double dot22 = DotProduct(v1v3, v1v3);
                double dot2p = DotProduct(v1v3, v1p);

                // Вычисляем barycentric координаты
                double invDenom = 1 / (dot11 * dot22 - dot12 * dot12);
                double u = (dot22 * dot1p - dot12 * dot2p) * invDenom;
                double v = (dot11 * dot2p - dot12 * dot1p) * invDenom;
                double w = 1 - u - v;

                return (u, v, w);
            }
            catch
            {
                return (0, 0, 0);
            }
        }

        /// <summary>
        /// Вычисляет скалярное произведение двух векторов
        /// </summary>
        private double DotProduct(Point3D a, Point3D b)
        {
            return a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        }

        /// <summary>
        /// Альтернативный алгоритм для 3D полигонов (проекция на плоскость XY)
        /// </summary>
        private bool IsPointInsidePolygon3D(Point3D point, List<Point3D> vertices, bool hasZoneHeight = false, double zoneHeight = 0)
        {
            try
            {
                // Сначала проверяем высоту, если задана
                if (hasZoneHeight)
                {
                    double minZ = vertices.Min(v => v.Z);
                    double maxZ = minZ + zoneHeight;

                    if (point.Z < minZ || point.Z > maxZ)
                    {
                        LogToFile($"IsPointInsidePolygon3D: Точка вне зоны по высоте Z={point.Z:F2}, диапазон={minZ:F2}-{maxZ:F2}");
                        return false;
                    }
                }

                // Проецируем на плоскость XY и используем 2D алгоритм
                return IsPointInsidePolygon(point, vertices);
            }
            catch (Exception ex)
            {
                LogToFile($"IsPointInsidePolygon3D: Ошибка: {ex.Message}");
                return false;
            }
        }

        private List<ClashResult> GetAllResultsFromGroup(ClashResultGroup group)
        {
            var allResults = new List<ClashResult>();

            foreach (var result in group.Children.OfType<ClashResult>())
            {
                allResults.Add(result);
            }

            foreach (var childGroup in group.Children.OfType<ClashResultGroup>())
            {
                allResults.AddRange(GetAllResultsFromGroup(childGroup));
            }

            return allResults;
        }
    }

    /// <summary>
    /// Класс для представления зоны
    /// </summary>
    public class ZoneItem
    {
        public string ZoneName { get; set; }
        public ModelItem ZoneObject { get; set; }
        public BoundingBox3D BoundingBox { get; set; }

        // Геометрия на основе треугольников (точная геометрия)
    public List<(Point3D, Point3D, Point3D)> Triangles { get; set; } = new List<(Point3D, Point3D, Point3D)>();
        public bool UseTriangleGeometry { get; set; } = false;

        // Полигональная геометрия (резервная)
        public List<Point3D> Vertices { get; set; } = new List<Point3D>();
        public bool UsePolygonGeometry { get; set; } = false;
        public double ZoneHeight { get; set; } = 0; // Высота зоны для 3D проверки
        public bool HasZoneHeight { get; set; } = false; // Флаг наличия высоты
    }
}
