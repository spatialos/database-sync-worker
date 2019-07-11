using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Improbable.Test
{
    // Copied from: https://raw.githubusercontent.com/dotnet/cli/master/test/Microsoft.DotNet.Tools.Tests.Utilities/Extensions/ProcessExtensions.cs
    // Licensed under MIT.
    public static class ProcessExtensions
    {
        private static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

        public static int KillTree(this Process process)
        {
            return process.KillTree(DefaultTimeout);
        }

        public static int KillTree(this Process process, TimeSpan timeout)
        {
            if (IsWindows)
            {
                return RunProcessAndWaitForExit(
                    "taskkill",
                    $"/T /F /PID {process.Id}",
                    timeout,
                    out _,
                    out _);
            }


            var children = new HashSet<int>();
            if (GetAllChildIdsUnix(process.Id, children, timeout) != 0)
            {
                return 1;
            }

            foreach (var childId in children)
            {
                if (KillProcessUnix(childId, timeout) != 0)
                {
                    return 1;
                }
            }

            return KillProcessUnix(process.Id, timeout);
        }

        private static int GetAllChildIdsUnix(int parentId, ISet<int> children, TimeSpan timeout)
        {
            var exitCode = RunProcessAndWaitForExit(
                "pgrep",
                $"-P {parentId}",
                timeout,
                out var stdout,
                out _);

            if (exitCode == 0 && !string.IsNullOrEmpty(stdout))
            {
                using (var reader = new StringReader(stdout))
                {
                    while (true)
                    {
                        var text = reader.ReadLine();

                        if (string.IsNullOrEmpty(text))
                        {
                            break;
                        }

                        if (int.TryParse(text, out var id))
                        {
                            children.Add(id);
                            // Recursively get the children
                            GetAllChildIdsUnix(id, children, timeout);
                        }
                    }
                }
            }

            return exitCode;
        }

        private static int KillProcessUnix(int processId, TimeSpan timeout)
        {
            return RunProcessAndWaitForExit(
                "kill",
                $"-TERM {processId}",
                timeout,
                out _,
                out _);
        }

        private static int RunProcessAndWaitForExit(string fileName, string arguments, TimeSpan timeout, out string stdout, out string stderr)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            var process = Process.Start(startInfo);

            stdout = null;
            stderr = null;

            if (process.WaitForExit((int) timeout.TotalMilliseconds))
            {
                stdout = process.StandardOutput.ReadToEnd();
                stderr = process.StandardError.ReadToEnd();
            }
            else
            {
                process.Kill();
            }

            return process.ExitCode;
        }
    }
}
