using System;
using System.Collections.Generic;
using System.Linq;

namespace CollisionClusterPlugin
{
    public class UnionFind
    {
        private Dictionary<string, string> parent;

        // Конструктор для инициализации родительских связей
        public UnionFind(IEnumerable<string> elements)
        {
            parent = new Dictionary<string, string>();
            foreach (var elem in elements)
            {
                parent[elem] = elem; // Изначально каждый элемент является корнем своего множества
            }
        }

        // Метод Find с сжатием пути
        public string Find(string x)
        {
            if (parent[x] != x)
            {
                parent[x] = Find(parent[x]); // Рекурсивное сжатие пути
            }
            return parent[x];
        }

        // Метод Union для объединения двух множеств
        public void Union(string x, string y)
        {
            string rootX = Find(x);
            string rootY = Find(y);
            if (rootX != rootY)
            {
                parent[rootY] = rootX; // Объединение множеств
            }
        }
    }

    public class UnionPair
    {
        /// <summary>
        /// Группирует индексы пар строк по их связанным компонентам.
        /// </summary>
        /// <param name="pairs">Список пар строк (List<List<string>>).</param>
        /// <returns>Список групп индексов, каждая из которых содержит связанные пары.</returns>
        public static List<List<int>> GroupPairsIndices(List<List<string>> pairs)
        {
            // Проверка на пустой ввод
            if (pairs == null || pairs.Count == 0)
                return new List<List<int>>();

            // Собираем все уникальные строки
            HashSet<string> elements = new HashSet<string>();
            foreach (var pair in pairs)
            {
                if (pair.Count != 2)
                    throw new ArgumentException("Каждая пара должна содержать ровно два элемента.");

                elements.Add(pair[0]);
                elements.Add(pair[1]);
            }

            // Инициализация Union-Find
            UnionFind uf = new UnionFind(elements);

            // Объединяем элементы из каждой пары
            foreach (var pair in pairs)
            {
                uf.Union(pair[0], pair[1]);
            }

            // Группируем индексы пар по корневому родителю
            Dictionary<string, List<int>> groupedIndices = new Dictionary<string, List<int>>();
            for (int i = 0; i < pairs.Count; i++)
            {
                string root = uf.Find(pairs[i][0]);
                if (!groupedIndices.ContainsKey(root))
                {
                    groupedIndices[root] = new List<int>();
                }
                groupedIndices[root].Add(i);
            }

            // Формируем итоговый список групп индексов
            List<List<int>> result = groupedIndices.Values.ToList();
            return result;
        }
    }
}
