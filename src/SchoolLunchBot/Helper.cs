using System;

namespace SchoolLunch.Bot
{
    internal static class Helper
    {
        public static T[][] Split<T>(this T[] array, int block)
        {
            var blocks = (int) Math.Ceiling(array.Length / (double) block);
            var result = new T[blocks][];
            var count = array.Length;
            for (var currentBlock = 0; currentBlock < result.Length; ++currentBlock)
            {
                result[currentBlock] = new T[Math.Min(block, count)];
                count -= block;
                for (var current = 0; current < result[currentBlock].Length; ++current)
                {
                    result[currentBlock][current] = array[current + currentBlock * block];
                }
            }
            return result;
        }
    }
}
