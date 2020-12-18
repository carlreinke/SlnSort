// Portions Copyright (c) Microsoft. All rights reserved.
// Copyright (c) Carl Reinke
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SlnSort
{
    internal static class Program
    {
        // An example of a project line looks like this:
        //  Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "ClassLibrary1", "ClassLibrary1\ClassLibrary1.csproj", "{05A5AD00-71B5-4612-AF2F-9EA9121C4111}"
        private static readonly Regex _projectLinePattern = new Regex(
            "^" // Beginning of line
            + "Project\\(\"(?<PROJECTTYPEGUID>.*)\"\\)"
            + "\\s*=\\s*" // Any amount of whitespace plus "=" plus any amount of whitespace
            + "\"(?<PROJECTNAME>.*)\""
            + "\\s*,\\s*" // Any amount of whitespace plus "," plus any amount of whitespace
            + "\"(?<RELATIVEPATH>.*)\""
            + "\\s*,\\s*" // Any amount of whitespace plus "," plus any amount of whitespace
            + "\"(?<PROJECTGUID>.*)\""
            + "$", // End-of-line
            RegexOptions.Compiled);

        // An example of a property line looks like this:
        //      AspNetCompiler.VirtualPath = "/webprecompile"
        private static readonly Regex _propertyLinePattern = new Regex(
            "^" // Beginning of line
            + "(?<PROPERTYNAME>[^=]*)"
            + "\\s*=\\s*" // Any amount of whitespace plus "=" plus any amount of whitespace
            + "(?<PROPERTYVALUE>.*)"
            + "$", // End-of-line
            RegexOptions.Compiled);

        internal static int Main(string[] args)
        {
            bool invalidArgs = false;
            bool noBackup = false;

            var nonOptionArgs = new List<string>(1);

            for (int i = 0; i < args.Length; ++i)
            {
                string arg = args[i];
                if (!arg.StartsWith("--", StringComparison.Ordinal))
                {
                    nonOptionArgs.Add(arg);
                    continue;
                }

                if (arg == "--")
                {
                    for (++i; i < args.Length; ++i)
                        nonOptionArgs.Add(args[i]);
                    break;
                }
                else if (arg == "--no-backup")
                {
                    noBackup = true;
                }
                else
                {
                    invalidArgs = true;
                    break;
                }
            }

            invalidArgs |= nonOptionArgs.Count != 1;

            if (invalidArgs)
            {
                string program = Path.GetFileNameWithoutExtension(typeof(Program).Assembly.Location);
                Console.Error.WriteLine($"Usage: {program} [options] <sln>");
                Console.Error.WriteLine($"");
                Console.Error.WriteLine($"Options:");
                Console.Error.WriteLine($"  --no-backup  Do not create backup file.");
                Console.Error.WriteLine($"");
                return -1;
            }

            string path = nonOptionArgs[0];

            try
            {
                SortSolution(path, noBackup, out bool alreadySorted);

                if (alreadySorted)
                    Console.Error.WriteLine("Solution is already sorted.");
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch (Exception ex)
#pragma warning restore CA1031 // Do not catch general exception types
            {
#if DEBUG
                Console.Error.WriteLine(ex);
#else
                Console.Error.WriteLine(ex.Message);
#endif
                return -1;
            }

            return 0;
        }

        private static void SortSolution(string path, bool noBackup, out bool alreadySorted)
        {
            string sortedPath;

            var encoding = Encoding.GetEncoding(0);

            using (var inStream = new FileStream(path, FileMode.Open, FileAccess.Read))
            using (var reader = new StreamReader(inStream, encoding))
            {
                const string slnFileHeaderNoVersion = "Microsoft Visual Studio Solution File, Format Version ";

                string?[] headerLines = new string?[2];

                // Parse header.
                for (int i = 0; ; ++i)
                {
                    if (i == 2)
                        throw new InvalidDataException("Invalid solution: No header.");

                    headerLines[i] = reader.ReadLine();
                    if (headerLines[i] is string line &&
                        line.Trim().StartsWith(slnFileHeaderNoVersion, StringComparison.Ordinal))
                    {
                        break;
                    }
                }

                sortedPath = Path.ChangeExtension(path, Path.ChangeExtension(Path.GetRandomFileName(), Path.GetExtension(path)));

                using (var outStream = new FileStream(sortedPath, FileMode.Create, FileAccess.Write))
                using (var writer = new StreamWriter(outStream, reader.CurrentEncoding))
                {
                    try
                    {
                        // Write header.
                        foreach (string? line in headerLines)
                            if (line != null)
                                writer.WriteLine(line);

                        SortSolution(reader, writer, out alreadySorted);
                    }
                    catch
                    {
                        try
                        {
                            File.Delete(sortedPath);
                        }
#pragma warning disable CA1031 // Do not catch general exception types
                        catch
#pragma warning restore CA1031 // Do not catch general exception types
                        {
                            // Ignore so that we can rethrow the outer exception.
                        }
                        throw;
                    }
                }
            }

            if (alreadySorted)
            {
                File.Delete(sortedPath);
                return;
            }

            if (noBackup)
            {
                File.Delete(path);
            }
            else
            {
                for (int i = 0; i < int.MaxValue; ++i)
                {
                    string backupSuffix = i == 0 ? ".unsorted" : $".{i}.unsorted";
                    string backupPath = path + backupSuffix;
                    try
                    {

                        File.Move(path, backupPath);
                        break;
                    }
                    catch (IOException)
                    {
                        if (!File.Exists(backupPath))
                            throw;
                    }
                }
            }

            File.Move(sortedPath, path);
        }

        private static void SortSolution(StreamReader reader, StreamWriter writer, out bool alreadySorted)
        {
            var builder = new ProjectOrderBuilder();

            var projects = new List<(string Key, List<string> Lines)>();

            var globalLines = new Queue<string>();

            for (string? line; ;)
            {
                line = reader.ReadLine();
                if (line == null)
                    break;

                string trimmedLine = line.Trim();
                if (trimmedLine.StartsWith("Project(", StringComparison.Ordinal))
                {
                    ParseProject(line);
                }
                else if (trimmedLine == "Global")
                {
                    ParseGlobal(line);
                }
                else
                {
                    // Write unrecognized.
                    writer.WriteLine(line);
                }
            }

            void ParseProject(string initialLine)
            {
                var lines = new List<string>() { initialLine };

                var match = _projectLinePattern.Match(initialLine);
                if (!match.Success)
                    throw new InvalidDataException("Invalid solution: Invalid project syntax.");

                string projectTypeGuid = match.Groups["PROJECTTYPEGUID"].Value.Trim();
                string projectName = match.Groups["PROJECTNAME"].Value.Trim();
                string projectGuid = match.Groups["PROJECTGUID"].Value.Trim();

                for (string? line; ;)
                {
                    line = reader.ReadLine();
                    if (line == null)
                        throw new InvalidDataException("Invalid solution: Unexpected end of file.");

                    lines.Add(line);

                    string trimmedLine = line.Trim();
                    if (trimmedLine == "EndProject")
                        break;
                }

                builder.SetProjectInfo(projectGuid, projectTypeGuid, projectName);

                projects.Add((projectGuid, lines));
            }

            void ParseGlobal(string initialLine)
            {
                globalLines.Enqueue(initialLine);

                for (string? line; ;)
                {
                    line = reader.ReadLine();
                    if (line == null)
                        throw new InvalidDataException("Invalid solution: Unexpected end of file.");

                    globalLines.Enqueue(line);

                    string trimmedLine = line.Trim();
                    if (trimmedLine == "EndGlobal")
                    {
                        break;
                    }
                    else
                    {
                        var match = _propertyLinePattern.Match(trimmedLine);
                        if (!match.Success)
                            continue;

                        string name = match.Groups["PROPERTYNAME"].Value.Trim();

                        if (name == "GlobalSection(NestedProjects)")
                            ParseNestedProjects();
                    }
                }
            }

            void ParseNestedProjects()
            {
                for (string? line; ;)
                {
                    line = reader.ReadLine();
                    if (line == null)
                        throw new InvalidDataException("Invalid solution: Unexpected end of file.");

                    globalLines.Enqueue(line);

                    string trimmedLine = line.Trim();
                    if (trimmedLine == "EndGlobalSection")
                        break;

                    var match = _propertyLinePattern.Match(trimmedLine);
                    if (!match.Success)
                        throw new InvalidDataException("Invalid solution: Invalid nested project syntax.");

                    string name = match.Groups["PROPERTYNAME"].Value.Trim();
                    string value = match.Groups["PROPERTYVALUE"].Value.Trim();

                    builder.SetProjectParent(name, value);
                }
            }

            var projectOrderComparer = builder.Build();

            alreadySorted = projects.AreOrderedBy(x => x.Key, projectOrderComparer);

            // Write projects.
            foreach (var project in projects.OrderBy(x => x.Key, projectOrderComparer))
                foreach (string line in project.Lines)
                    writer.WriteLine(line);

            // Write global.
            while (globalLines.Count > 0)
            {
                string line = globalLines.Dequeue();

                writer.WriteLine(line);

                string trimmedLine = line.Trim();
                var match = _propertyLinePattern.Match(trimmedLine);
                if (!match.Success)
                    continue;

                string name = match.Groups["PROPERTYNAME"].Value.Trim();

                if (name == "GlobalSection(NestedProjects)")
                {
                    var lineEntries = new List<(string Key, string Line)>();

                    while (globalLines.Count > 0)
                    {
                        line = globalLines.Dequeue();

                        trimmedLine = line.Trim();
                        if (trimmedLine == "EndGlobalSection")
                            break;

                        match = _propertyLinePattern.Match(trimmedLine);
                        if (!match.Success)
                            throw new InvalidDataException("Invalid solution: Invalid nested project syntax.");

                        name = match.Groups["PROPERTYNAME"].Value.Trim();

                        lineEntries.Add((name, line));
                    }

                    alreadySorted = alreadySorted && lineEntries.AreOrderedBy(x => x.Key, projectOrderComparer);

                    foreach (var entry in lineEntries.OrderBy(x => x.Key, projectOrderComparer))
                        writer.WriteLine(entry.Line);

                    writer.WriteLine(line);
                }
                else if (name == "GlobalSection(ProjectConfigurationPlatforms)")
                {
                    var lineEntries = new List<(string Key, string Line)>();

                    while (globalLines.Count > 0)
                    {
                        line = globalLines.Dequeue();

                        trimmedLine = line.Trim();
                        if (trimmedLine == "EndGlobalSection")
                            break;

                        match = _propertyLinePattern.Match(trimmedLine);
                        if (!match.Success)
                            throw new InvalidDataException("Invalid solution: Invalid project configuration platform syntax.");

                        name = match.Groups["PROPERTYNAME"].Value.Trim();

                        int index = name.IndexOf('.', StringComparison.Ordinal);
                        if (index < 0)
                            throw new InvalidDataException("Invalid solution: Invalid project configuration platform syntax.");
                        name = name.Substring(0, index);

                        lineEntries.Add((name, line));
                    }

                    alreadySorted = alreadySorted && lineEntries.AreOrderedBy(x => x.Key, projectOrderComparer);

                    foreach (var entry in lineEntries.OrderBy(x => x.Key, projectOrderComparer))
                        writer.WriteLine(entry.Line);

                    writer.WriteLine(line);
                }
            }
        }
    }
}
