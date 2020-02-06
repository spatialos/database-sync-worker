using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using Serilog;

namespace BootstrapEnv
{
    [Command("get-artifacts")]
    internal class GetArtifactsCommand
    {
        [Option("--output-dir")] public string OutputDir { get; set; } = "nupkgs";

        [Option("--prefix")] [Required] public string Prefix { get; set; } = "<Unset>";

        private async Task<int> OnExecuteAsync(CommandLineApplication app, CancellationToken token)
        {
            var apiToken = Environment.GetEnvironmentVariable("BUILDKITE_API_TOKEN") ?? throw new Exception("BUILDKITE_API_TOKEN is not set");
            var buildNumber = Environment.GetEnvironmentVariable($"{Prefix}_BUILD_NUMBER") ?? throw new Exception($"{Prefix}_BUILD_NUMBER is not set");
            var jobId = Environment.GetEnvironmentVariable($"{Prefix}_JOB_ID") ?? throw new Exception($"{Prefix}_JOB_ID is not set");

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);

            var requestUri = $"https://api.buildkite.com/v2/organizations/improbable/pipelines/csharp-worker-template-premerge/builds/{buildNumber}/jobs/{jobId}/artifacts";
            await using var stream = await client.GetStreamAsync(requestUri).ConfigureAwait(false);
            var document = await JsonDocument.ParseAsync(stream, cancellationToken: token).ConfigureAwait(false);

            Directory.CreateDirectory(OutputDir);

            foreach (var obj in document.RootElement.EnumerateArray())
            {
                var url = obj.GetProperty("download_url").GetString();
                var filename = obj.GetProperty("filename").GetString();

                var bytes = await client.GetByteArrayAsync(url).ConfigureAwait(false);
                File.WriteAllBytes(Path.Combine(OutputDir, filename), bytes);

                Log.Information("Wrote {FileName}", filename);
            }

            return 0;
        }
    }
}
