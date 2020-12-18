// Copyright (c) Carl Reinke
// Licensed under the MIT license.
using System;

namespace SlnSort
{
    internal static class StringExtensions
    {
        public static int IndexOf(this string @this, char value, StringComparison comparisonType)
        {
            return comparisonType == StringComparison.Ordinal
#pragma warning disable CA1307 // Specify StringComparison for clarity
                ? @this.IndexOf(value)
#pragma warning restore CA1307 // Specify StringComparison for clarity
                : @this.IndexOf(value.ToString(), comparisonType);
        }
    }
}
