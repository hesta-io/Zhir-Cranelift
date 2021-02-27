using Medallion.Shell;

using System;
using System.Threading.Tasks;

namespace Cranelift.Common.Helpers
{
    public class PythonOptions
    {
        public string Path { get; set; } // python or python3
    }

    public class PythonHelper
    {
        private readonly PythonOptions _options;

        public PythonHelper(PythonOptions options)
        {
            _options = options;
        }

        public async Task<CommandResult> Run(string[] arguments, Action<Shell.Options> options)
        {
            var command = Command.Run(_options.Path, arguments, options);
            await command.Task;

            return command.Result;
        }
    }
}
