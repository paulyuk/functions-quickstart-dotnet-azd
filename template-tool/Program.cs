using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

var exitCode = TemplatePoc.Run(args);
return exitCode;

internal static class TemplatePoc
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static int Run(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        try
        {
            var command = args[0].ToLowerInvariant();
            var options = ParseOptions(args.Skip(1).ToArray());
            using var source = ResolveSource(GetOption(options, "source", Directory.GetCurrentDirectory()));

            return command switch
            {
                "analyze" => AnalyzeCommand(source.Path),
                "plan" => PlanCommand(source.Path, options),
                "apply" => ApplyCommand(source.Path, options),
                _ => Fail($"Unknown command '{args[0]}'.")
            };
        }
        catch (TemplatePocException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Unexpected failure: {ex.Message}");
            return 1;
        }
    }

    private static int AnalyzeCommand(string sourcePath)
    {
        var analysis = Analyze(sourcePath);
        WriteJson(analysis);
        return 0;
    }

    private static int PlanCommand(string sourcePath, Dictionary<string, string> options)
    {
        var analysis = Analyze(sourcePath);
        var requested = GetRequestedValues(options);
        var plan = CreatePlan(analysis, requested);
        WriteJson(plan);
        return 0;
    }

    private static int ApplyCommand(string sourcePath, Dictionary<string, string> options)
    {
        var outputPath = GetRequiredPathOption(options, "output");
        var sourceAnalysis = Analyze(sourcePath);
        var requested = GetRequestedValues(options);
        var plan = CreatePlan(sourceAnalysis, requested);

        if (Directory.Exists(outputPath) || File.Exists(outputPath))
        {
            throw new TemplatePocException($"Output path already exists: {outputPath}");
        }

        CopySource(sourceAnalysis.SourcePath, outputPath);

        var outputAnalysis = Analyze(outputPath);
        ApplyPlan(outputAnalysis, requested);

        var buildResult = RunDotNetBuild(outputPath);
        var result = new ApplyResult(outputPath, plan, buildResult);
        WriteJson(result);

        return buildResult.ExitCode == 0 ? 0 : 1;
    }

    private static TemplateAnalysis Analyze(string sourcePath)
    {
        sourcePath = Path.GetFullPath(sourcePath);
        if (!Directory.Exists(sourcePath))
        {
            throw new TemplatePocException($"Source path does not exist: {sourcePath}");
        }

        var solutionFile = Directory.GetFiles(sourcePath, "*.sln", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        var projectFile = DetectProjectFile(sourcePath, solutionFile);
        var projectName = projectFile is null ? null : Path.GetFileNameWithoutExtension(projectFile);
        var projectFolder = projectFile is null ? null : Path.GetRelativePath(sourcePath, Path.GetDirectoryName(projectFile)!);
        var namespaceName = DetectNamespace(sourcePath, projectFolder);
        var functions = DetectFunctions(sourcePath, projectFolder);
        var azdName = DetectAzdName(sourcePath);
        var launchProfile = DetectLaunchProfile(sourcePath, projectFolder);

        return new TemplateAnalysis(
            SourcePath: sourcePath,
            ProjectName: projectName,
            ProjectFolder: NormalizeRelativePath(projectFolder),
            ProjectFile: NormalizeRelativePath(projectFile is null ? null : Path.GetRelativePath(sourcePath, projectFile)),
            SolutionFile: NormalizeRelativePath(solutionFile is null ? null : Path.GetRelativePath(sourcePath, solutionFile)),
            Namespace: namespaceName,
            AzdName: azdName,
            LaunchProfile: launchProfile,
            Functions: functions);
    }

    private static string? DetectProjectFile(string sourcePath, string? solutionFile)
    {
        if (solutionFile is not null)
        {
            var solutionText = File.ReadAllText(solutionFile);
            var match = Regex.Match(solutionText, "Project\\(\"[^\"]+\"\\)\\s*=\\s*\"[^\"]+\",\\s*\"([^\"]+\\.csproj)\"", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var candidate = Path.GetFullPath(Path.Combine(sourcePath, FromRepoPath(match.Groups[1].Value)));
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return Directory.GetFiles(sourcePath, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsExcludedPath(sourcePath, path))
            .OrderBy(path => path.Count(ch => ch == Path.DirectorySeparatorChar))
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static string? DetectNamespace(string sourcePath, string? projectFolder)
    {
        var files = GetCSharpFiles(sourcePath, projectFolder).ToArray();

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            var hinted = Regex.Match(text, @"^\s*namespace\s+([A-Za-z_][A-Za-z0-9_.]*)\s*(?:[;{])?.*template:\s*namespace", RegexOptions.Multiline);
            if (hinted.Success)
            {
                return hinted.Groups[1].Value;
            }
        }

        return files
            .SelectMany(file => Regex.Matches(File.ReadAllText(file), @"^\s*namespace\s+([A-Za-z_][A-Za-z0-9_.]*)", RegexOptions.Multiline)
                .Select(match => match.Groups[1].Value))
            .GroupBy(ns => ns)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => group.Key)
            .FirstOrDefault();
    }

    private static string[] DetectFunctions(string sourcePath, string? projectFolder)
    {
        return GetCSharpFiles(sourcePath, projectFolder)
            .SelectMany(file => Regex.Matches(File.ReadAllText(file), "\\[Function\\s*\\(\\s*\"([^\"]+)\"\\s*\\)\\s*\\]")
                .Select(match => match.Groups[1].Value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    private static string? DetectAzdName(string sourcePath)
    {
        var azureYamlPath = Path.Combine(sourcePath, "azure.yaml");
        if (!File.Exists(azureYamlPath))
        {
            return null;
        }

        var text = File.ReadAllText(azureYamlPath);
        var hinted = Regex.Match(text, @"^name:\s*([^#\r\n]+?)\s*#.*template:\s*azd-name", RegexOptions.Multiline);
        if (hinted.Success)
        {
            return hinted.Groups[1].Value.Trim().Trim('"', '\'');
        }

        var match = Regex.Match(text, @"^name:\s*([^#\r\n]+)", RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value.Trim().Trim('"', '\'') : null;
    }

    private static string? DetectLaunchProfile(string sourcePath, string? projectFolder)
    {
        if (projectFolder is null)
        {
            return null;
        }

        var launchSettingsPath = Path.Combine(sourcePath, FromRepoPath(projectFolder), "Properties", "launchSettings.json");
        if (!File.Exists(launchSettingsPath))
        {
            return null;
        }

        var root = JsonNode.Parse(File.ReadAllText(launchSettingsPath))?.AsObject();
        var profiles = root?["profiles"]?.AsObject();
        return profiles?.FirstOrDefault().Key;
    }

    private static RequestedValues GetRequestedValues(Dictionary<string, string> options)
    {
        var name = GetRequiredOption(options, "name");
        var namespaceName = GetRequiredOption(options, "namespace");
        var azdName = GetOption(options, "azd-name", ToKebabCase(name));

        return new RequestedValues(name, namespaceName, azdName);
    }

    private static TemplatePlan CreatePlan(TemplateAnalysis analysis, RequestedValues requested)
    {
        var changes = new List<PlannedChange>();

        if (analysis.ProjectFolder is not null && !StringEquals(analysis.ProjectFolder, requested.Name))
        {
            changes.Add(new PlannedChange("rename-directory", analysis.ProjectFolder, requested.Name));
        }

        if (analysis.ProjectFile is not null)
        {
            var newProjectFile = Path.Combine(requested.Name, $"{requested.Name}.csproj");
            changes.Add(new PlannedChange("rename-file", analysis.ProjectFile, NormalizeRelativePath(newProjectFile)!));
        }

        if (analysis.SolutionFile is not null)
        {
            changes.Add(new PlannedChange("update-file", analysis.SolutionFile, "Update project name and project file path."));
        }

        if (analysis.Namespace is not null && !StringEquals(analysis.Namespace, requested.Namespace))
        {
            foreach (var file in GetPlannedCSharpFilesWithNamespace(analysis))
            {
                changes.Add(new PlannedChange("update-file", file, $"Replace namespace {analysis.Namespace} with {requested.Namespace}."));
            }
        }

        if (analysis.AzdName is not null && !StringEquals(analysis.AzdName, requested.AzdName))
        {
            changes.Add(new PlannedChange("update-file", "azure.yaml", $"Replace AZD name {analysis.AzdName} with {requested.AzdName} and update the service project path when it matches the old project folder."));
        }
        else if (analysis.ProjectFolder is not null)
        {
            changes.Add(new PlannedChange("update-file", "azure.yaml", "Update AZD service project path when it matches the old project folder."));
        }

        if (analysis.LaunchProfile is not null && analysis.ProjectName is not null)
        {
            changes.Add(new PlannedChange("update-file", Path.Combine(analysis.ProjectFolder ?? "", "Properties", "launchSettings.json"), "Update launch profile when it matches the old project identity."));
        }

        return new TemplatePlan(analysis, requested, changes);
    }

    private static void ApplyPlan(TemplateAnalysis analysis, RequestedValues requested)
    {
        if (analysis.ProjectName is null || analysis.ProjectFolder is null || analysis.ProjectFile is null)
        {
            throw new TemplatePocException("Unable to detect a .NET project to transform.");
        }

        var root = analysis.SourcePath;
        var oldProjectFolder = Path.Combine(root, FromRepoPath(analysis.ProjectFolder));
        var newProjectFolder = Path.Combine(root, requested.Name);
        var oldProjectName = analysis.ProjectName;

        if (!Path.GetFullPath(oldProjectFolder).Equals(Path.GetFullPath(newProjectFolder), StringComparison.OrdinalIgnoreCase))
        {
            Directory.Move(oldProjectFolder, newProjectFolder);
        }

        var oldProjectFile = Path.Combine(newProjectFolder, $"{oldProjectName}.csproj");
        var newProjectFile = Path.Combine(newProjectFolder, $"{requested.Name}.csproj");
        if (File.Exists(oldProjectFile) && !Path.GetFullPath(oldProjectFile).Equals(Path.GetFullPath(newProjectFile), StringComparison.OrdinalIgnoreCase))
        {
            File.Move(oldProjectFile, newProjectFile);
        }

        UpdateSolutionFile(root, analysis, requested);
        UpdateCSharpFiles(root, analysis, requested);
        UpdateAzureYaml(root, analysis, requested);
        UpdateLaunchSettings(root, analysis, requested);
    }

    private static void UpdateSolutionFile(string root, TemplateAnalysis analysis, RequestedValues requested)
    {
        if (analysis.SolutionFile is null || analysis.ProjectName is null || analysis.ProjectFile is null)
        {
            return;
        }

        var solutionPath = Path.Combine(root, FromRepoPath(analysis.SolutionFile));
        if (!File.Exists(solutionPath))
        {
            return;
        }

        var newProjectPath = $"{requested.Name}\\{requested.Name}.csproj";
        var text = File.ReadAllText(solutionPath)
            .Replace($"= \"{analysis.ProjectName}\",", $"= \"{requested.Name}\",", StringComparison.Ordinal)
            .Replace(ToWindowsRepoPath(analysis.ProjectFile), newProjectPath, StringComparison.Ordinal)
            .Replace(ToUnixRepoPath(analysis.ProjectFile), newProjectPath, StringComparison.Ordinal);

        File.WriteAllText(solutionPath, text);
    }

    private static void UpdateCSharpFiles(string root, TemplateAnalysis analysis, RequestedValues requested)
    {
        if (analysis.Namespace is null)
        {
            return;
        }

        var projectFolder = Path.Combine(root, requested.Name);
        foreach (var file in Directory.GetFiles(projectFolder, "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            if (!text.Contains(analysis.Namespace, StringComparison.Ordinal))
            {
                continue;
            }

            File.WriteAllText(file, text.Replace(analysis.Namespace, requested.Namespace, StringComparison.Ordinal));
        }
    }

    private static void UpdateAzureYaml(string root, TemplateAnalysis analysis, RequestedValues requested)
    {
        var azureYamlPath = Path.Combine(root, "azure.yaml");
        if (!File.Exists(azureYamlPath) || analysis.AzdName is null)
        {
            return;
        }

        var text = File.ReadAllText(azureYamlPath);
        text = Regex.Replace(text, @"^name:\s*([^#\r\n]+)(.*)$", match => $"name: {requested.AzdName}{match.Groups[2].Value}", RegexOptions.Multiline);
        if (analysis.ProjectFolder is not null)
        {
            var oldProjectPath = ToUnixRepoPath(analysis.ProjectFolder).TrimEnd('/');
            text = Regex.Replace(
                text,
                $@"^(\s*project:\s*)\.\/{Regex.Escape(oldProjectPath)}\/?\s*$",
                match => $"{match.Groups[1].Value}./{requested.Name}/",
                RegexOptions.Multiline);
        }

        File.WriteAllText(azureYamlPath, text);
    }

    private static void UpdateLaunchSettings(string root, TemplateAnalysis analysis, RequestedValues requested)
    {
        if (analysis.ProjectName is null)
        {
            return;
        }

        var launchSettingsPath = Path.Combine(root, requested.Name, "Properties", "launchSettings.json");
        if (!File.Exists(launchSettingsPath))
        {
            return;
        }

        var rootNode = JsonNode.Parse(File.ReadAllText(launchSettingsPath))?.AsObject();
        var profiles = rootNode?["profiles"]?.AsObject();
        if (rootNode is null || profiles is null)
        {
            return;
        }

        var replacements = profiles
            .Select(profile => new
            {
                OldKey = profile.Key,
                NewKey = ReplaceIgnoreCase(profile.Key, analysis.ProjectName, requested.Name)
            })
            .Where(item => !StringEquals(item.OldKey, item.NewKey))
            .ToArray();

        foreach (var replacement in replacements)
        {
            var value = profiles[replacement.OldKey];
            profiles.Remove(replacement.OldKey);
            profiles[replacement.NewKey] = value;
        }

        File.WriteAllText(launchSettingsPath, rootNode.ToJsonString(JsonOptions));
    }

    private static DotNetBuildResult RunDotNetBuild(string outputPath)
    {
        return RunProcess("dotnet", ["build", "--nologo"], outputPath);
    }

    private static DotNetBuildResult RunProcess(string fileName, IReadOnlyList<string> arguments, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new TemplatePocException($"Failed to start {fileName}.");
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new DotNetBuildResult(process.ExitCode, standardOutput.Trim(), standardError.Trim());
    }

    private static void CopySource(string sourcePath, string outputPath)
    {
        var sourceRoot = Path.GetFullPath(sourcePath);
        var outputRoot = Path.GetFullPath(outputPath);

        Directory.CreateDirectory(outputRoot);

        foreach (var directory in Directory.GetDirectories(sourceRoot, "*", SearchOption.AllDirectories)
                     .Where(path => !IsExcludedPath(sourceRoot, path) && !IsInsidePath(outputRoot, path))
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var relativePath = Path.GetRelativePath(sourceRoot, directory);
            Directory.CreateDirectory(Path.Combine(outputRoot, relativePath));
        }

        foreach (var file in Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories)
                     .Where(path => !IsExcludedPath(sourceRoot, path) && !IsInsidePath(outputRoot, path)))
        {
            var relativePath = Path.GetRelativePath(sourceRoot, file);
            var destination = Path.Combine(outputRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
    }

    private static ResolvedSource ResolveSource(string source)
    {
        source = string.IsNullOrWhiteSpace(source) ? Directory.GetCurrentDirectory() : source.Trim();

        if (Directory.Exists(source))
        {
            return new ResolvedSource(Path.GetFullPath(source), null);
        }

        var cloneUrl = GetCloneUrl(source);
        if (cloneUrl is null)
        {
            throw new TemplatePocException($"Source is not a folder, GitHub URL, owner/repo shorthand, or Azure Samples template name: {source}");
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), $"template-poc-{Guid.NewGuid():N}");
        var clonePath = Path.Combine(tempRoot, "source");
        Directory.CreateDirectory(tempRoot);

        var result = RunProcess("git", ["clone", "--depth", "1", cloneUrl, clonePath], Directory.GetCurrentDirectory());
        if (result.ExitCode != 0)
        {
            TryDeleteDirectory(tempRoot);
            throw new TemplatePocException($"Failed to clone source '{source}' from '{cloneUrl}'.\n{result.StandardError}");
        }

        return new ResolvedSource(clonePath, tempRoot);
    }

    private static string? GetCloneUrl(string source)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
            (uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ||
             uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase)))
        {
            return source;
        }

        if (source.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase))
        {
            return source;
        }

        if (Regex.IsMatch(source, @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$"))
        {
            return $"https://github.com/{source}.git";
        }

        if (Regex.IsMatch(source, @"^[A-Za-z0-9_.-]+$"))
        {
            return $"https://github.com/Azure-Samples/{source}.git";
        }

        return null;
    }

    private static IEnumerable<string> GetCSharpFiles(string sourcePath, string? projectFolder)
    {
        var searchRoot = projectFolder is null ? sourcePath : Path.Combine(sourcePath, FromRepoPath(projectFolder));
        if (!Directory.Exists(searchRoot))
        {
            return [];
        }

        return Directory.GetFiles(searchRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsExcludedPath(sourcePath, path));
    }

    private static IEnumerable<string> GetPlannedCSharpFilesWithNamespace(TemplateAnalysis analysis)
    {
        if (analysis.ProjectFolder is null || analysis.Namespace is null)
        {
            return [];
        }

        return GetCSharpFiles(analysis.SourcePath, analysis.ProjectFolder)
            .Where(file => File.ReadAllText(file).Contains(analysis.Namespace, StringComparison.Ordinal))
            .Select(file => NormalizeRelativePath(Path.GetRelativePath(analysis.SourcePath, file))!);
    }

    private static bool IsExcludedPath(string sourceRoot, string path)
    {
        var relative = Path.GetRelativePath(sourceRoot, path);
        var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return segments.Any(segment =>
            segment.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals(".vs", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("out", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("template-tool", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsInsidePath(string parentPath, string candidatePath)
    {
        var parent = Path.GetFullPath(parentPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(candidatePath);
        return candidate.StartsWith(parent, StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                throw new TemplatePocException($"Unexpected argument '{arg}'. Options must use --name value format.");
            }

            var key = arg[2..];
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new TemplatePocException("Option name cannot be empty.");
            }

            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new TemplatePocException($"Missing value for option '{arg}'.");
            }

            options[key] = args[++index];
        }

        return options;
    }

    private static string GetRequiredOption(Dictionary<string, string> options, string name)
    {
        if (!options.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new TemplatePocException($"Missing required option '--{name}'.");
        }

        return value;
    }

    private static string GetOption(Dictionary<string, string> options, string name, string defaultValue)
    {
        return options.TryGetValue(name, out var value) ? value : defaultValue;
    }

    private static string GetRequiredPathOption(Dictionary<string, string> options, string name)
    {
        return Path.GetFullPath(GetRequiredOption(options, name));
    }

    private static string ToKebabCase(string value)
    {
        var withWordBreaks = Regex.Replace(value, "([a-z0-9])([A-Z])", "$1-$2");
        return Regex.Replace(withWordBreaks, "[^A-Za-z0-9]+", "-").Trim('-').ToLowerInvariant();
    }

    private static string ReplaceIgnoreCase(string value, string oldValue, string newValue)
    {
        return Regex.Replace(value, Regex.Escape(oldValue), newValue, RegexOptions.IgnoreCase);
    }

    private static string? NormalizeRelativePath(string? path)
    {
        return path is null ? null : ToWindowsRepoPath(path);
    }

    private static string ToWindowsRepoPath(string path)
    {
        return path.Replace('/', '\\');
    }

    private static string ToUnixRepoPath(string path)
    {
        return path.Replace('\\', '/');
    }

    private static string FromRepoPath(string path)
    {
        return path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
    }

    private static bool StringEquals(string left, string right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                foreach (var directory in Directory.GetDirectories(path, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(directory, FileAttributes.Normal);
                }

                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            Console.Error.WriteLine($"Warning: unable to delete temporary folder '{path}'.");
        }
    }

    internal static void DeleteTemporarySource(string path)
    {
        TryDeleteDirectory(path);
    }

    private static void WriteJson<T>(T value)
    {
        Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        PrintUsage();
        return 1;
    }

    private static bool IsHelp(string value)
    {
        return value is "-h" or "--help" or "help";
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
        Convention based template POC

        Commands:
          analyze --source <path>
          plan --source <path> --name <project-name> --namespace <namespace> [--azd-name <azd-name>]
          apply --source <path> --output <path> --name <project-name> --namespace <namespace> [--azd-name <azd-name>]
        """);
    }
}

internal sealed record TemplateAnalysis(
    string SourcePath,
    string? ProjectName,
    string? ProjectFolder,
    string? ProjectFile,
    string? SolutionFile,
    string? Namespace,
    string? AzdName,
    string? LaunchProfile,
    string[] Functions);

internal sealed record RequestedValues(string Name, string Namespace, string AzdName);

internal sealed record PlannedChange(string Kind, string Path, string Detail);

internal sealed record TemplatePlan(TemplateAnalysis Analysis, RequestedValues Requested, IReadOnlyList<PlannedChange> Changes);

internal sealed record DotNetBuildResult(int ExitCode, string StandardOutput, string StandardError);

internal sealed record ApplyResult(string OutputPath, TemplatePlan Plan, DotNetBuildResult Build);

internal sealed record ResolvedSource(string Path, string? TemporaryRoot) : IDisposable
{
    public void Dispose()
    {
        if (TemporaryRoot is not null)
        {
            TemplatePoc.DeleteTemporarySource(TemporaryRoot);
        }
    }
}

internal sealed class TemplatePocException(string message) : Exception(message);
