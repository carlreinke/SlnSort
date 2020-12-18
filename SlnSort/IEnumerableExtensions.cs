// Copyright (c) Carl Reinke
// Licensed under the MIT license.
using System;
using System.Collections.Generic;

namespace SlnSort
{
    internal static class IEnumerableExtensions
    {
        public static bool AreOrderedBy<TSource, TKey>(this IEnumerable<TSource> enumerable, Func<TSource, TKey> keySelector, IComparer<TKey> comparer)
        {
            if (enumerable is null)
                throw new ArgumentNullException(nameof(enumerable));
            if (keySelector is null)
                throw new ArgumentNullException(nameof(keySelector));
            if (comparer is null)
                throw new ArgumentNullException(nameof(comparer));

            var enumerator = enumerable.GetEnumerator();

            if (enumerator.MoveNext())
            {
                var previous = keySelector(enumerator.Current);

                while (enumerator.MoveNext())
                {
                    var current = keySelector(enumerator.Current);

                    if (comparer.Compare(previous, current) > 0)
                        return false;

                    previous = current;
                }
            }

            return true;
        }
    }
}
