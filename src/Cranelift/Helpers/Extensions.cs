using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;

namespace Cranelift
{
    public static class Extensions
    {
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
