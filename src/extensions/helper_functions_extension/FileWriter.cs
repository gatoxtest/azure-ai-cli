﻿using System.Text;
using Azure.AI.OpenAI;

namespace Azure.AI.Details.Common.CLI.Extensions.HelperFunctions
{
    public static class FileReaderWriter
    {
        [FunctionDescription("Checks if file exists")]
        public static bool FileExists(string fileName)
        {
            var exists = FileHelpers.FileExists(fileName);
            return exists;
        }

        [FunctionDescription("Reads text from a file; returns empty string if file does not exist")]
        public static string ReadTextFromFile(string fileName)
        {
            var content = FileHelpers.FileExists(fileName)
                ? FileHelpers.ReadAllText(fileName, Encoding.UTF8)
                : string.Empty;
            return content;
        }

        [FunctionDescription("Writes text into a file; if the file exists, it is overwritten")]
        public static bool CreateFileAndSaveText(string fileName, string text)
        {
            FileHelpers.WriteAllText(fileName, text, Encoding.UTF8);
            return true;
        }

        [FunctionDescription("Creates a directory if it doesn't already exist")]
        public static bool DirectoryCreate(string directoryName)
        {
            FileHelpers.EnsureDirectoryForFileExists($"{directoryName}/.");
            return true;
        }


        [FunctionDescription("List files; lists all files regardless of name")]
        public static string FindAllFiles()
        {
            return FindFilesMatchingPattern("**/*");
        }

        [FunctionDescription("List files; lists files matching pattern")]
        public static string FindFilesMatchingPattern([ParameterDescription("The pattern to search for; use '**/*.ext' to search sub-directories")] string pattern)
        {
            var files = FileHelpers.FindFiles(".", pattern);
            return string.Join("\n", files);
        }

        [FunctionDescription("Find files containing text; searches all files")]
        public static string FindTextInAllFiles([ParameterDescription("The text to find")] string text)
        {
            return FindTextInFilesMatchingPattern(text, "**/*");
        }

        [FunctionDescription("Find files containing text; searches files matching a pattern")]
        public static string FindTextInFilesMatchingPattern(
            [ParameterDescription("The text to find")] string text,
            [ParameterDescription("The pattern to search for; use '**/*.ext' to search sub-directories")] string pattern)
        {
            var files = FileHelpers.FindFiles(".", pattern);
            var result = new List<string>();
            foreach (var file in files)
            {
                var content = FileHelpers.ReadAllText(file, Encoding.UTF8);
                if (content.Contains(text))
                {
                    Console.WriteLine($"  Found {file}");
                    result.Add(file);
                }
            }
            return string.Join("\n", result);
        }
    }
}
