using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;

namespace Cranelift
{
    public static class Extensions
    {
        // https://www.extensionmethod.net/csharp/ienumerable/ienumerable-chunk
        public static IEnumerable<IEnumerable<T>> Chunk<T>(this IEnumerable<T> list, int chunkSize)
        {
            if (chunkSize <= 0)
            {
                throw new ArgumentException("chunkSize must be greater than 0.");
            }

            while (list.Any())
            {
                yield return list.Take(chunkSize);
                list = list.Skip(chunkSize);
            }
        }

        // https://stackoverflow.com/a/21609968/7003797
        public static void AddParameterWithValue(this DbCommand command, string parameterName, object parameterValue)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = parameterName;
            parameter.Value = parameterValue;
            command.Parameters.Add(parameter);
        }
    }
}
