using System.Collections.Generic;

namespace Cranelift
{
    public static class Extensions
    {
        // https://gist.github.com/rob-blackbourn/e573ad0de40fa8e8c167
        public static IEnumerable<IEnumerable<T>> Chunk<T>(this IEnumerable<T> source, int count)
        {
            var e = source.GetEnumerator();

            while (e.MoveNext())
                yield return Next(e, count);
        }

        private static IEnumerable<T> Next<T>(IEnumerator<T> e, int count)
        {
            do yield return e.Current;
            while (--count > 0 && e.MoveNext());
        }
    }
}
