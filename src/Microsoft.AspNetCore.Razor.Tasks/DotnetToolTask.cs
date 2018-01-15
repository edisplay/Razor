// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.AspNetCore.Razor.Tools;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.CodeAnalysis.CommandLine;
using Microsoft.Extensions.CommandLineUtils;
using Roslyn.Utilities;

namespace Microsoft.AspNetCore.Razor.Tasks
{
    public abstract class DotNetToolTask : ToolTask
    {
        private CancellationTokenSource _razorCompileCts;

        public bool Debug { get; set; }

        public bool DebugTool { get; set; }

        [Required]
        public string ToolAssembly { get; set; }

        public string ServerAssembly { get; set; }

        public bool UseServer { get; set; }

        protected override string ToolName => "dotnet";

        // If we're debugging then make all of the stdout gets logged in MSBuild
        protected override MessageImportance StandardOutputLoggingImportance => DebugTool ? MessageImportance.High : base.StandardOutputLoggingImportance;

        protected override MessageImportance StandardErrorLoggingImportance => MessageImportance.High;

        protected override string GenerateFullPathToTool()
        {
#if NETSTANDARD2_0
            if (!string.IsNullOrEmpty(DotNetMuxer.MuxerPath))
            {
                return DotNetMuxer.MuxerPath;
            }
#endif

            // use PATH to find dotnet
            return ToolExe;
        }

        protected override string GenerateCommandLineCommands()
        {
            return $"exec \"{ToolAssembly}\"" + (DebugTool ? " --debug" : "");
        }

        protected override string GetResponseFileSwitch(string responseFilePath)
        {
            return "@\"" + responseFilePath + "\"";
        }

        protected abstract override string GenerateResponseFileCommands();

        public override bool Execute()
        {
            if (Debug)
            {
                while (!Debugger.IsAttached)
                {
                    Log.LogMessage(MessageImportance.High, "Waiting for debugger in pid: {0}", Process.GetCurrentProcess().Id);
                    Thread.Sleep(TimeSpan.FromSeconds(3));
                }
            }

            return base.Execute();
        }

        protected override int ExecuteTool(string pathToTool, string responseFileCommands, string commandLineCommands)
        {
            if (UseServer && TryExecuteOnServer(pathToTool, responseFileCommands, commandLineCommands, out var result))
            {
                return 0;
            }

            return base.ExecuteTool(pathToTool, responseFileCommands, commandLineCommands);
        }

        protected override void LogToolCommand(string message)
        {
            if (Debug)
            {
                Log.LogMessage(MessageImportance.High, message);
            }
            else
            {
                base.LogToolCommand(message);
            }
        }

        public override void Cancel()
        {
            base.Cancel();

            _razorCompileCts?.Cancel();
        }

        protected virtual bool TryExecuteOnServer(string pathToTool, string responseFileCommands, string commandLineCommands, out bool result)
        {
            Log.LogMessage(MessageImportance.High, "Attempting server execution...");
            using (_razorCompileCts = new CancellationTokenSource())
            {
                CompilerServerLogger.Log($"CommandLine = '{commandLineCommands}'");
                CompilerServerLogger.Log($"BuildResponseFile = '{responseFileCommands}'");

                var clientDir = Path.GetDirectoryName(ServerAssembly);

                var workingDir = CurrentDirectoryToUse();
                var buildPaths = new BuildPathsAlt(
                    clientDir: clientDir,
                    // MSBuild doesn't need the .NET SDK directory
                    sdkDir: null,
                    workingDir: workingDir,
                    tempDir: BuildServerConnection.GetTempPath(workingDir));

                // TODO: Cleanup/Remove unnecessary parameters
                var responseTask = BuildServerConnection.RunServerCompilationCore(
                    RequestLanguage.CSharpCompile, // Doesn't matter
                    GetArguments(string.Empty, responseFileCommands).ToList(),
                    buildPaths,
                    pipeName: PipeName.ComputeDefault(),
                    keepAlive: null,
                    libEnvVariable: LibDirectoryToUse(),
                    timeoutOverride: null,
                    tryCreateServerFunc: BuildServerConnection.TryCreateServerCore,
                    cancellationToken: _razorCompileCts.Token);

                responseTask.Wait(_razorCompileCts.Token);

                var response = responseTask.Result;
                if (response.Type == BuildResponse.ResponseType.Completed)
                {
                    Log.LogMessage(MessageImportance.High, "Guess what? I'm returning true here. Does this mean the build server works? Hell Yeah!");
                    result = true;
                    return result;
                }
            }

            result = false;
            return result;
        }

        /// <summary>
        /// Get the current directory that the compiler should run in.
        /// </summary>
        private string CurrentDirectoryToUse()
        {
            // ToolTask has a method for this. But it may return null. Use the process directory
            // if ToolTask didn't override. MSBuild uses the process directory.
            string workingDirectory = GetWorkingDirectory();
            if (string.IsNullOrEmpty(workingDirectory))
            {
                workingDirectory = Directory.GetCurrentDirectory();
            }
            return workingDirectory;
        }

        /// <summary>
        /// Get the "LIB" environment variable, or NULL if none.
        /// </summary>
        private string LibDirectoryToUse()
        {
            // First check the real environment.
            string libDirectory = Environment.GetEnvironmentVariable("LIB");

            // Now go through additional environment variables.
            string[] additionalVariables = EnvironmentVariables;
            if (additionalVariables != null)
            {
                foreach (string var in EnvironmentVariables)
                {
                    if (var.StartsWith("LIB=", StringComparison.OrdinalIgnoreCase))
                    {
                        libDirectory = var.Substring(4);
                    }
                }
            }

            return libDirectory;
        }

        // TODO: This is probably not necessary. Need to cleanup
        private string[] GetArguments(string commandLineCommands, string responseFileCommands)
        {
            var commandLineArguments =
                CommandLineUtilities.SplitCommandLineIntoArguments(commandLineCommands, removeHashComments: true);
            var responseFileArguments =
                CommandLineUtilities.SplitCommandLineIntoArguments(responseFileCommands, removeHashComments: true);
            return commandLineArguments.Concat(responseFileArguments).ToArray();
        }
    }
}
