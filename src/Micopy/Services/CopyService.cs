﻿using Micopy.Configuration;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using Microsoft.Extensions.FileSystemGlobbing;
using System.CommandLine;
using System.Diagnostics;

namespace Micopy.Services;

public record FileConfiguration(
    string FileName,
    string SourceFolder,
    string DestinationFolder
);

public class CopyService
{
    private const int DefaultParallelism = 8;

    private readonly IConsole console;

    public CopyService(IConsole console)
    {
        this.console = console;
    }

    public void Copy(MicopyConfiguration configuration)
    {
        if (configuration.Parallelism.HasValue && configuration.Parallelism.Value == 0)
        {
            CopyDirectories(configuration.Folders, configuration.IgnorePatterns);
            return;
        }

        var parallelism = configuration.Parallelism.HasValue ? configuration.Parallelism.Value : DefaultParallelism;
    }

    private void CopyDirectories(IEnumerable<FolderConfiguration> folders, IEnumerable<IgnorePatternConfiguration>? ignorePatterns)
    {
        var files = new Stack<FileConfiguration>();
        foreach (var folder in folders)
        {
            var matcher = new Matcher();
            matcher.AddInclude("**/*");  //**
            if (!string.IsNullOrEmpty(folder.IgnorePatternName) && ignorePatterns is not null)
            {
                var ignorePattern = ignorePatterns.First(p => p.Name.Equals(folder.IgnorePatternName, StringComparison.OrdinalIgnoreCase));
                foreach (var pattern in ignorePattern.Patterns)
                {
                    matcher.AddExclude(pattern);
                }
            }

            var dirInfo = new DirectoryInfo(folder.Source);
            var directoryWrapper = new DirectoryInfoWrapper(dirInfo);
            var result = matcher.Execute(directoryWrapper);

            var foundFiles = result.Files.Select(file => {
                var relativeFolder = Path.GetDirectoryName(file.Path);
                var sourceFolder = Path.Combine(folder.Source, relativeFolder);
                var destinationFolder = Path.Combine(folder.Destination, relativeFolder);
                var fileName = Path.GetFileName(file.Path);
                return new FileConfiguration(fileName, sourceFolder, destinationFolder);
            });

            foreach (var file in foundFiles)
            {
                files.Push(file);
            }
        }

        var filesCount = files.Count;
        var filesCopied = 0;
        var stopwatch = Stopwatch.StartNew();
        while (files.Count > 0)
        {
            var file = files.Pop();
            if (!Directory.Exists(file.DestinationFolder))
            {
                Directory.CreateDirectory(file.DestinationFolder);
            }
            File.Copy(Path.Combine(file.SourceFolder, file.FileName), Path.Combine(file.DestinationFolder, file.FileName), overwrite: true);
            filesCopied++;
            DisplayProgressBar(filesCopied, filesCount);
        }
        stopwatch.Stop();
        console.WriteLine($"{Environment.NewLine}{filesCount} files copied in {stopwatch.Elapsed}");
    }



    public async Task CopyDirectoryAsync(string sourceFolder, string destinationFolder, int? parallelism)
    {
        if (parallelism.HasValue && parallelism.Value == 0)
        {
            CopyDirectory(sourceFolder, destinationFolder);
            return;
        }

        if (!parallelism.HasValue)
        {
            parallelism = DefaultParallelism;
        }

        await CopyDirectoryAsync(sourceFolder, destinationFolder, parallelism.Value);
    }

    private async Task CopyDirectoryAsync(string sourceFolder, string destinationFolder, int parallelism)
    {
        var filesCount = await GetFilesCountForDirectoryAsync(sourceFolder, parallelism);
        var filesCopied = 0;
        //CopyFilesAsync(sourceFolder, destinationFolder, parallelism, ref filesCopied, filesCount);
    }

    private async Task<int> GetFilesCountForDirectoryAsync(string folderPath, int parallelism)
    {
        var subdirectories = Directory.GetDirectories(folderPath, "*", SearchOption.AllDirectories);

        int totalFileCount = 0;

        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = parallelism };
        await Task.Run(() =>
        {
            Parallel.ForEach(subdirectories, parallelOptions, subdirectory =>
            {
                var directoryFilesCount = Directory.GetFiles(subdirectory).Length;
                Interlocked.Add(ref totalFileCount, directoryFilesCount);
            });
        });

        return totalFileCount;
    }

    private void CopyDirectory(string sourceFolder, string destinationFolder)
    {
        var filesCount = GetFilesCountForDirectory(sourceFolder);
        var filesCopied = 0;
        CopyFiles(sourceFolder, destinationFolder, ref filesCopied, filesCount);
    }

    private void CopyDirectoryNew(string sourceFolder, string destinationFolder)
    {
        var directoryInfo = new DirectoryInfo(sourceFolder);
        var files = directoryInfo.GetFiles("*.*", SearchOption.AllDirectories);
        var filesCount = files.Length;
        var filesCopied = 0;

        foreach (var file in files)
        {
            var relativePath = file.FullName.Substring(sourceFolder.Length + 1);
            var targetFilePath = Path.Combine(destinationFolder, relativePath);
            var targetFileDirectory = Path.GetDirectoryName(targetFilePath);

            if (!string.IsNullOrEmpty(targetFileDirectory) && !Directory.Exists(targetFileDirectory))
            {
                Directory.CreateDirectory(targetFileDirectory);
            }

            file.CopyTo(targetFilePath, overwrite: true);
            filesCopied++;
            DisplayProgressBar(filesCopied, filesCount);
        }
    }

    private static int GetFilesCountForDirectory(string folderPath)
    {
        var directoryInfo = new DirectoryInfo(folderPath);
        int count = directoryInfo.GetFiles("*.*", SearchOption.AllDirectories).Length;
        return count;
    }

    private void CopyFiles(string sourceFolder, string destinationFolder, ref int filesCopied, int filesCount)
    {
        if (!Directory.Exists(destinationFolder))
        {
            Directory.CreateDirectory(destinationFolder);
        }

        var directoryInfo = new DirectoryInfo(sourceFolder);

        foreach (var file in directoryInfo.GetFiles())
        {
            var destinationPath = Path.Combine(destinationFolder, file.Name);
            file.CopyTo(destinationPath, overwrite: true);
            filesCopied++;
            DisplayProgressBar(filesCopied, filesCount);
        }

        foreach (var subDirectory in directoryInfo.GetDirectories())
        {
            var nextSourceFolder = Path.Combine(sourceFolder, subDirectory.Name);
            var nextDestinationFolder = Path.Combine(destinationFolder, subDirectory.Name);
            CopyFiles(nextSourceFolder, nextDestinationFolder, ref filesCopied, filesCount);
        }
    }

    //private async Task CopyFilesAsync(string sourceFolder, string destinationFolder, int parallelism, ref int filesCopied, int filesCount)
    //{
    //    if (!Directory.Exists(destinationFolder))
    //    {
    //        Directory.CreateDirectory(destinationFolder);
    //    }

    //    var directoryInfo = new DirectoryInfo(sourceFolder);

    //    foreach (var file in directoryInfo.GetFiles())
    //    {
    //        var destinationPath = Path.Combine(destinationFolder, file.Name);
    //        file.CopyTo(destinationPath, overwrite: true);
    //        filesCopied++;
    //        DisplayProgressBar(filesCopied, filesCount);
    //    }

    //    foreach (var subDirectory in directoryInfo.GetDirectories())
    //    {
    //        var nextSourceFolder = Path.Combine(sourceFolder, subDirectory.Name);
    //        var nextDestinationFolder = Path.Combine(destinationFolder, subDirectory.Name);
    //        CopyFiles(nextSourceFolder, nextDestinationFolder, ref filesCopied, filesCount);
    //    }
    //}

    private void DisplayProgressBar(int currentValue, int maxValue, int barSize = 50)
    {
        var progressFraction = (double)currentValue / maxValue;
        var filledBars = (int)(progressFraction * barSize);
        var emptyBars = barSize - filledBars;

        console.Write("\r[");
        console.Write(new string('#', filledBars));
        console.Write(new string(' ', emptyBars));
        console.Write($"] {progressFraction:P0}");
    }
}
