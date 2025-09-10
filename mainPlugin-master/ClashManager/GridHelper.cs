

using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;

namespace ClashManager
{
    /// <summary>
    /// Вспомогательный класс для работы с сетками в Navisworks
    /// </summary>
    public static class GridHelper
    {
        /// <summary>
        /// Получает пересечение сеток для коллизии (ClashResult)
        /// </summary>
        /// <param name="clash">Коллизия</param>
        /// <returns>Строка вида "1 - A" или "—", если пересечение не найдено</returns>
        public static string GetGridIntersectionForClash(ClashResult clash)
        {
            if (clash == null) return "—";

            // Берём точку коллизии (центр)
            Point3D clashPoint = clash.Center;

            // Активная система сеток
            GridSystem oGS = Application.ActiveDocument.Grids.ActiveSystem;
            if (oGS == null) return "—";

            // Находим ближайшее пересечение
            GridIntersection gi = oGS.ClosestIntersection(clashPoint);
            if (gi == null) return "—";

            // Названия осей - используем правильные свойства API
            string line1Name = GetGridPropertyValue(gi, "Line1", "DisplayName") ?? "";
            string line2Name = GetGridPropertyValue(gi, "Line2", "DisplayName") ?? "";

            // Формируем как в Clash Detective: "1 - A"
            if (!string.IsNullOrEmpty(line1Name) && !string.IsNullOrEmpty(line2Name))
            {
                return $"{line1Name} - {line2Name}";
            }
            else if (!string.IsNullOrEmpty(line1Name))
            {
                return line1Name;
            }
            else if (!string.IsNullOrEmpty(line2Name))
            {
                return line2Name;
            }

            return "—";
        }

        /// <summary>
        /// Helper method to safely get property values from grid objects using reflection
        /// </summary>
        private static string GetGridPropertyValue(object obj, params string[] propertyPath)
        {
            if (obj == null || propertyPath == null || propertyPath.Length == 0)
                return null;

            object current = obj;
            foreach (string propName in propertyPath)
            {
                if (current == null) return null;

                var prop = current.GetType().GetProperty(propName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (prop == null) return null;

                current = prop.GetValue(current);
            }

            return current?.ToString();
        }
    }
}
