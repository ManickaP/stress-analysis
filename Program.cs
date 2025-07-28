using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Azure.AI.OpenAI;
using OpenAI.Chat;
using Azure;
using Microsoft.Extensions.AI;

class Program
{
    // Azure DevOps constants
    const string Organization = "dnceng-public";
    const string Project = "public";
    const string DefinitionId = "131";

    static readonly Dictionary<int, GHIssue> Issues = new Dictionary<int, GHIssue>() {
        [109121] = null,
        [100534] = null,
        [101625] = null,
        [102202] = null,
        [102880] = null,
        [103128] = null,
        [106694] = null,
        [109824] = null,
        [110577] = null,
        [111660] = null,
        [40388] = null,
        [42472] = null,
        [49999] = null,
        [50854] = null,
        [55261] = null,
        [56159] = null,
        [59086] = null,
        [72696] = null,
        [73154] = null,
        [76044] = null,
        [76045] = null,
        [76183] = null,
        [77126] = null,
        [81706] = null,
        [82528] = null,
        [84800] = null,
        [85338] = null,
        [87559] = null,
        [87938] = null,
        [91672] = null,
        [94222] = null,
        [95750] = null,
        [98220] = null,
        [99959] = null,
        [99960] = null,
    };

    static readonly string ExtractErrorPrompt = @"Analyze the following log file:

{0}


and extract the relevant info for each distinct error. Give me the relevant info and number how many times that error occurred.";
    static readonly string MatchIssuePrompt = @"Try to find the best matching issue from this list:
{0}

for this error '{1}'. And give me the issue number with your confidence in the match expressed and 0-1 floating point number. You can return multiple matches if your confidence is low. Also take into account that opened issues are more likely to be the right match. And please fetch the actual content of those issues.";

    public class Build
    {
        [JsonPropertyName("finishTime")]
        public DateTime FinishTime { get; set; }
        [JsonPropertyName("sourceBranch")]
        public string SourceBranch { get; set; }
        [JsonPropertyName("result")]
        public string Result { get; set; }
        [JsonPropertyName("id")]
        public int Id { get; set; }
    }
    public class BuildResponse
    {
        [JsonPropertyName("value")]
        public Build[] Value { get; set; }
    }
    public class Secrets
    {
        [JsonPropertyName("uri")]
        public string Uri { get; set; }
        [JsonPropertyName("key")]
        public string Key { get; set; }
    }
    public class GHIssue
    {
        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("state")]
        public string State { get; set; }

        [JsonPropertyName("body")]
        public string Body { get; set; }
    }

    sealed record ErrorOccurrence(string Error, int Occurrences);
    sealed record IssueRelevance(int IssueNumber, double Score);

    static readonly IChatClient _chatClient;

    static Program()
    {
        using var json = JsonDocument.Parse(File.ReadAllText("secrets.json"));
        var secrets = json.Deserialize<Secrets>();

        var azureClient = new AzureOpenAIClient(new Uri(secrets.Uri), new AzureKeyCredential(secrets.Key));
        _chatClient = azureClient.GetChatClient("gpt-4o").AsIChatClient();
    }
    static async Task ProcessLogFileAI(string logFile)
    {
        var chatOptions = new ChatOptions()
        {
            Temperature = 0.1f,
            TopP = 0.1f
        };
        foreach (var (Error, Occurrences) in (await _chatClient.GetResponseAsync<ErrorOccurrence[]>(string.Format(ExtractErrorPrompt, await File.ReadAllTextAsync(logFile)), chatOptions)).Result)
        {
            Console.WriteLine("========================");
            Console.WriteLine($"{Occurrences}x {Error}{Environment.NewLine}");
            foreach (var (IssueNumber, Score) in (await _chatClient.GetResponseAsync<IssueRelevance[]>(string.Format(MatchIssuePrompt, string.Join(Environment.NewLine, Issues.Select(pair => $"Number = {pair.Key}; State = {pair.Value.State}; Title = {pair.Value.Title}; Description = {pair.Value.Body}")), Error), chatOptions)).Result)
            {
                Console.WriteLine($"matching https://github.com/dotnet/runtime/issues/{IssueNumber} with confidence {Score}");
            }
        }
    }
    static async Task DownloadAndParseIssueData()
    {
        var directoryName = "downloaded-issues";
        Directory.CreateDirectory(directoryName);
        using var ghClient = new HttpClient();
        ghClient.BaseAddress = new Uri("https://api.github.com");
        ghClient.DefaultRequestHeaders.Add("User-Agent", ".NET Issue Parser / 1.0");

        foreach (var issue in Issues.Keys)
        {
            var issuePath = Path.Combine(directoryName, $"issue-{issue}.json");
            if (!File.Exists(issuePath) || !TryParse(issuePath, out var ghIssue))
            {
                var uri = $"repos/dotnet/runtime/issues/{issue}";
                var ghStream = await ghClient.GetStreamAsync(uri);
                using var file = new FileStream(issuePath, FileMode.Create);
                await ghStream.CopyToAsync(file);
                await file.FlushAsync();
                TryParse(issuePath, out ghIssue);
            }
            Issues[issue] = ghIssue;
        }

        static bool TryParse(string issuePath, out GHIssue ghIssue)
        {
            try
            {
                var json = JsonDocument.Parse(File.ReadAllText(issuePath));
                ghIssue = json.Deserialize<GHIssue>();
                return true;
            }
            catch // If not possible to parse, just re-download.
            {
                ghIssue = null;
                return false;
            }
        }
    }
    static async Task ProcessLogFileRegex(string logFile)
    {

    }
    static async Task Main()
    {
        await DownloadAndParseIssueData();

        // Calculate the date range: last Monday 00:00 UTC to this Monday 00:00 UTC
        DateTime utcNow = DateTime.UtcNow;
        int daysSinceMonday = ((int)utcNow.DayOfWeek + 6) % 7; // Monday=0, Sunday=6
        DateTime thisMonday = utcNow.Date.AddDays(-daysSinceMonday);
        DateTime lastMonday = thisMonday.AddDays(-7);
        string minTime = lastMonday.ToString("yyyy-MM-ddT00:00:00Z");
        string maxTime = thisMonday.ToString("yyyy-MM-ddT00:00:00Z");

        string url = $"https://dev.azure.com/{Organization}/{Project}/_apis/build/builds?definitions={DefinitionId}&minTime={minTime}&maxTime={maxTime}&api-version=7.2-preview.7";

        using (var client = new HttpClient())
        {
            string responseBody = await client.GetStringAsync(url);
            var buildResponse = JsonSerializer.Deserialize<BuildResponse>(responseBody);

            if (buildResponse != null && buildResponse.Value != null)
            {
                var grouped = buildResponse.Value
                    .OrderBy(b => b.FinishTime)
                    .GroupBy(b => b.SourceBranch)
                    .OrderBy(g => g.Key); // Order groups alphabetically by branch

                foreach (var group in grouped)
                {
                    Console.WriteLine($"\n### Branch: `{group.Key}`\n");
                    Console.WriteLine("| Date | Result |");
                    Console.WriteLine("|------|--------|");
                    foreach (var build in group)
                    {
                        Console.WriteLine($"| {build.FinishTime:yyyy-MM-dd HH:mm} | {build.Result} |");
                        if (build.Result != "succeeded")
                        {
                            await ProcessLogs(client, build.Id, Path.Combine(Environment.CurrentDirectory, group.Key.Split('/').Last(), $"{build.FinishTime:yyyy-MM-dd}"));
                        }
                    }
                }
            }
        }
    }

    // Downloads the log file for the first failed step in a build, if any.
    public static async Task ProcessLogs(HttpClient client, int buildId, string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        // Get the timeline to find the failed step and its logId.
        string timelineUrl = $"https://dev.azure.com/{Organization}/{Project}/_apis/build/builds/{buildId}/timeline?api-version=7.2-preview.2";
        string timelineBody = await client.GetStringAsync(timelineUrl);

        using var timelineDoc = JsonDocument.Parse(timelineBody);
        var records = timelineDoc.RootElement.GetProperty("records");
        foreach (var record in records.EnumerateArray())
        {
            if (record.GetProperty("result").ToString() != "failed" ||
                record.GetProperty("type").ToString() != "Task" ||
               !record.TryGetProperty("log", out var log) ||
                log.ValueKind != JsonValueKind.Object)
            {
                continue;
            }
            int logId = log.GetProperty("id").GetInt32();
            string name = string.Join("_", record.GetProperty("name").ToString().Split(Path.GetInvalidFileNameChars()));

            // Download the log for the failed step.
            string logUrl = $"https://dev.azure.com/{Organization}/{Project}/_apis/build/builds/{buildId}/logs/{logId}?api-version=7.2-preview.2";

            using var logStream = await client.GetStreamAsync(logUrl);
            var logFilePath = Path.Combine(outputDir, $"{name}.log");
            using var file = new FileStream(logFilePath, FileMode.Create);
            await logStream.CopyToAsync(file);
            await file.FlushAsync();
            await ProcessLogFileAI(logFilePath);
        }
    }
}
