using System;
using System.Collections.Generic;
using System.Text;

namespace PlaylistMaker.Services
{
    /// <summary>Orders "Track 2" before "Track 10" instead of after it.</summary>
    public sealed class NaturalStringComparer : IComparer<string>
    {
        public static readonly NaturalStringComparer Instance = new NaturalStringComparer();

        public int Compare(string? left, string? right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }
            if (left == null)
            {
                return -1;
            }
            if (right == null)
            {
                return 1;
            }

            int leftIndex = 0;
            int rightIndex = 0;
            while (leftIndex < left.Length && rightIndex < right.Length)
            {
                char leftCharacter = left[leftIndex];
                char rightCharacter = right[rightIndex];
                if (char.IsDigit(leftCharacter) && char.IsDigit(rightCharacter))
                {
                    long leftNumber = ReadNumber(left, ref leftIndex);
                    long rightNumber = ReadNumber(right, ref rightIndex);
                    if (leftNumber != rightNumber)
                    {
                        return leftNumber < rightNumber ? -1 : 1;
                    }
                    continue;
                }

                int comparison = char.ToUpperInvariant(leftCharacter).CompareTo(char.ToUpperInvariant(rightCharacter));
                if (comparison != 0)
                {
                    return comparison;
                }
                leftIndex++;
                rightIndex++;
            }

            return (left.Length - leftIndex).CompareTo(right.Length - rightIndex);
        }

        private static long ReadNumber(string text, ref int index)
        {
            StringBuilder builder = new StringBuilder();
            while (index < text.Length && char.IsDigit(text[index]))
            {
                builder.Append(text[index]);
                index++;
            }
            if (long.TryParse(builder.ToString(), out long value))
            {
                return value;
            }
            return long.MaxValue;
        }
    }
}
