﻿//
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE.md file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace Azure.AI.Details.Common.CLI
{
    public class DevCommand : Command
    {
        internal DevCommand(ICommandValues values)
        {
            _values = values.ReplaceValues();
            _quiet = _values.GetOrDefault("x.quiet", false);
            _verbose = _values.GetOrDefault("x.verbose", true);
        }

        internal bool RunCommand()
        {
            try
            {
                RunDevCommand();
            }
            catch (WebException ex)
            {
                ConsoleHelpers.WriteLineError($"\n  ERROR: {ex.Message}");
                JsonHelpers.PrintJson(HttpHelpers.ReadWriteJson(ex.Response, _values, "dev"));
            }

            return _values.GetOrDefault("passed", true);
        }

        private bool RunDevCommand()
        {
            DoCommand(_values.GetCommand());
            return _values.GetOrDefault("passed", true);
        }

        private void DoCommand(string command)
        {
            CheckPath();

            switch (command)
            {
                case "dev.new": DoNew(); break;
                case "dev.shell": DoShell(); break;

                default:
                    _values.AddThrowError("WARNING:", $"'{command.Replace('.', ' ')}' NOT YET IMPLEMENTED!!");
                    break;
            }
        }

        private void DoNew()
        {
            _values.AddThrowError("WARNING:", $"''ai dev new' NOT YET IMPLEMENTED!!");
        }

        private string ReadConfig(string name)
        {
            return FileHelpers.FileExistsInConfigPath(name, _values)
                ? FileHelpers.ReadAllText(FileHelpers.DemandFindFileInConfigPath(name, _values, "configuration"), Encoding.UTF8)
                : null;
        }

        private void DoShell()
        {
            DisplayBanner("dev.shell");

            Console.WriteLine("Environment populated:\n");

            var env = new Dictionary<string, string>();
            env.Add("AZURE_SUBSCRIPTION_ID", ReadConfig("subscription"));
            env.Add("AZURE_RESOURCE_GROUP", ReadConfig("group"));
            env.Add("AZURE_AI_PROJECT_NAME", ReadConfig("project"));
            env.Add("AZURE_AI_HUB_NAME", ReadConfig("hub"));

            env.Add("OPENAI_API_KEY", ReadConfig("chat.key"));
            env.Add("OPENAI_API_VERSION", ReadConfig("chat.version"));
            env.Add("OPENAI_API_BASE", ReadConfig("chat.base"));
            env.Add("OPENAI_ENDPOINT", ReadConfig("chat.endpoint"));

            env.Add("OPENAI_CHAT_DEPLOYMENT", ReadConfig("chat.deployment"));
            env.Add("OPENAI_EVALUATION_DEPLOYMENT", ReadConfig("chat.evaluation.deployment"));
            env.Add("OPENAI_EMBEDDING_DEPLOYMENT", ReadConfig("search.embeddings.deployment"));

            env.Add("AZURE_AI_SEARCH_ENDPOINT", ReadConfig("search.endpoint"));
            env.Add("AZURE_AI_SEARCH_INDEX_NAME", ReadConfig("chat.search.index"));
            env.Add("AZURE_AI_SEARCH_KEY", ReadConfig("search.key"));

            foreach (var item in env)
            {
                if (string.IsNullOrEmpty(item.Value)) continue;

                var value = item.Key.EndsWith("_KEY")
                    ? item.Value.Substring(0, 4) + "****************************"
                    : item.Value;

                Console.WriteLine($"  {item.Key} = {value}");
                Environment.SetEnvironmentVariable(item.Key, item.Value);
            }

            var fileName = OS.IsLinux() ? "bash" : "cmd.exe";
            var arguments = OS.IsLinux() ? "-li" : "/k PROMPT (ai dev shell) %PROMPT%& title (ai dev shell)";

            var process = ProcessHelpers.StartProcess(fileName, arguments, env, false);
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                _values.AddThrowError("ERROR:", $"Shell exited with code {process.ExitCode}");
            }
            else
            {
                Console.WriteLine("ai dev shell exited successfully");
            }
        }

        private void DisplayBanner(string which)
        {
            if (_quiet) return;

            var logo = FileHelpers.FindFileInHelpPath($"help/include.{Program.Name}.{which}.ascii.logo");
            if (!string.IsNullOrEmpty(logo))
            {
                var text = FileHelpers.ReadAllHelpText(logo, Encoding.UTF8);
                ConsoleHelpers.WriteLineWithHighlight(text);
            }
        }

        private readonly bool _quiet;
        private readonly bool _verbose;
    }
}
