using Medallion.Shell;

using Microsoft.Extensions.Configuration;

using System;
using System.Threading.Tasks;

namespace Cranelift.Helpers
{
    public class PythonOptions
    {
        public string Path { get; set; } // python or python3
    }

    public class PythonHelper
    {
        private readonly PythonOptions _options;

        public PythonHelper(IConfiguration configuration)
        {
            _options = configuration.GetSection("Python").Get<PythonOptions>();
        }

        public async Task<CommandResult> Run(string[] arguments, Action<Shell.Options> options)
        {
            var command = Command.Run(_options.Path, arguments, options);
            await command.Task;

            return command.Result;
        }
    }
}
