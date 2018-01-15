// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis.CommandLine;

namespace Microsoft.AspNetCore.Razor.Tools
{
    internal abstract class CompilerHost
    {
        public static CompilerHost Create()
        {
            return new DefaultCompilerHost();
        }

        public abstract BuildResponse Execute(BuildRequest request, CancellationToken cancellationToken);

        private class DefaultCompilerHost : CompilerHost
        {
            public override BuildResponse Execute(BuildRequest request, CancellationToken cancellationToken)
            {
                if (!TryParseArguments(request, out var parsed))
                {
                    return new RejectedBuildResponse();
                }

                // TODO: This needs to know the difference between TaghelperTool and GenerateTool and act accordingly.
                var app = new Application(cancellationToken);
                var command = new DiscoverCommand(app);
                var commandArgs = parsed.args.Where(a => !string.IsNullOrEmpty(a));
                var projectRootArgs = new[] { "-p", parsed.workingDirectory };
                var exitCode = command.Execute(commandArgs.Concat(projectRootArgs).ToArray());

                return new CompletedBuildResponse(exitCode, false, "Yaay it works?");
            }

            private bool TryParseArguments(BuildRequest request, out (string workingDirectory, string tempDirectory, string[] args) parsed)
            {
                string workingDirectory = null;
                string tempDirectory = null;

                // The parsed arguments will contain 'string.Empty' in place of the arguments that we don't want to pass
                // to the compiler.
                var args = new string[request.Arguments.Count];

                for (var i = 0; i < request.Arguments.Count; i++)
                {
                    args[i] = string.Empty;

                    var argument = request.Arguments[i];
                    if (argument.ArgumentId == BuildProtocolConstants.ArgumentId.CurrentDirectory)
                    {
                        workingDirectory = argument.Value;
                    }
                    else if (argument.ArgumentId == BuildProtocolConstants.ArgumentId.TempDirectory)
                    {
                        tempDirectory = argument.Value;
                    }
                    else if (argument.ArgumentId == BuildProtocolConstants.ArgumentId.CommandLineArgument)
                    {
                        args[i] = argument.Value;
                    }
                }

                CompilerServerLogger.Log($"WorkingDirectory = '{workingDirectory}'");
                CompilerServerLogger.Log($"TempDirectory = '{tempDirectory}'");
                for (var i = 0; i < args.Length; i++)
                {
                    CompilerServerLogger.Log($"Argument[{i}] = '{request.Arguments[i]}'");
                }

                if (string.IsNullOrEmpty(workingDirectory))
                {
                    CompilerServerLogger.Log($"Rejecting build due to missing working directory");

                    parsed = default;
                    return false;
                }

                if (string.IsNullOrEmpty(tempDirectory))
                {
                    CompilerServerLogger.Log($"Rejecting build due to missing temp directory");

                    parsed = default;
                    return false;
                }

                if (string.IsNullOrEmpty(tempDirectory))
                {
                    CompilerServerLogger.Log($"Rejecting build due to missing temp directory");

                    parsed = default;
                    return false;
                }

                parsed = (workingDirectory, tempDirectory, args);
                return true;
            }
        }
    }
}
