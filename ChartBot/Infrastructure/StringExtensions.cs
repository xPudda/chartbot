using System;
using System.Collections.Generic;
using System.Linq;

namespace ChartBot.Infrastructure
{
    public static class Extensions
    {
        public static bool In<T>(this T value, params T[] list)
        {
            return value != null && (list?.Any() ?? false) && list.Contains(value);
        }

        public static IEnumerable<IEnumerable<T>> Partition<T>(this IEnumerable<T> items, int partitionSize)
        {
            return items.Select((item, inx) => new { item, inx })
                        .GroupBy(x => x.inx / partitionSize)
                        .Select(g => g.Select(x => x.item));
        }
    }
}
