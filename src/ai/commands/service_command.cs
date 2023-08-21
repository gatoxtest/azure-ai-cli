﻿//
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE.md file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Azure.AI.Details.Common.CLI
{
    public class ServiceCommand : Command
    {
        internal ServiceCommand(ICommandValues values)
        {
            _values = values.ReplaceValues();
            _quiet = _values.GetOrDefault("x.quiet", false);
            _verbose = _values.GetOrDefault("x.verbose", true);
        }

        internal bool RunCommand()
        {
            try
            {
                RunServiceCommand();
            }
            catch (WebException ex)
            {
                ConsoleHelpers.WriteLineError($"\n  ERROR: {ex.Message}");
                JsonHelpers.PrintJson(HttpHelpers.ReadWriteJson(ex.Response, _values, "service"));
            }

            return _values.GetOrDefault("passed", true);
        }

        private bool RunServiceCommand()
        {
            DoCommand(_values.GetCommand());
            return _values.GetOrDefault("passed", true);
        }

        private void DoCommand(string command)
        {
            CheckPath();

            switch (command)
            {
                case "service.resource.create": DoCreateResource(); break;
                case "service.resource.list": DoListResources(); break;
                case "service.project.create": DoCreateProject(); break;
                case "service.project.list": DoListProjects(); break;

                default:
                    _values.AddThrowError("WARNING:", $"'{command.Replace('.', ' ')}' NOT YET IMPLEMENTED!!");
                    break;
            }
        }

        private void DoCreateResource()
        {
            var action = "Creating AI resource";
            var command = "service resource create";
            var subscription = DemandSubscription(action, command);
            var location = DemandRegionLocation(action, command);

            var name = DemandName("service.resource.name", action, command);
            var group = GetGroupName() ?? $"{name}-rg";
            var displayName = _values.Get("service.resource.display.name", true) ?? name;
            var description = _values.Get("service.resource.description", true) ?? name;

            var message = $"{action} '{name}'";

            if (!_quiet) Console.WriteLine(message);
            var output = DoCreateResourceViaPython(subscription, group, name, location, displayName, description);
            if (!_quiet) Console.WriteLine($"{message} Done!\n");

            if (!_quiet) Console.WriteLine(output);
            CheckWriteOutputValueFromJson("service.output", "json", output);
            CheckWriteOutputValueFromJson("service.output", "resource.id", output, "id");
        }

        private void DoCreateProject()
        {
            var action = "Creating AI project";
            var command = "service project create";
            var subscription = DemandSubscription(action, command);
            var location = DemandRegionLocation(action, command);
            var resource = DemandResource(action, command);

            var name = DemandName("service.project.name", action, command);
            var group = GetGroupName() ?? $"{name}-rg";
            var displayName = _values.Get("service.project.display.name", true) ?? name;
            var description = _values.Get("service.project.description", true) ?? name;

            var message = $"{action} '{name}'";

            if (!_quiet) Console.WriteLine(message);
            var output = DoCreateProjectViaPython(subscription, group, resource, name, location, displayName, description);
            if (!_quiet) Console.WriteLine($"{message} Done!\n");

            if (!_quiet) Console.WriteLine(output);
            CheckWriteOutputValueFromJson("service.output", "json", output);
            CheckWriteOutputValueFromJson("service.output", "project.id", output, "id");
        }

        private void DoListResources()
        {
            var action = "Listing AI resources";
            var command = "service resource list";
            var subscription = DemandSubscription(action, command);

            var message = $"{action} for '{subscription}'";

            if (!_quiet) Console.WriteLine(message);
            var output = DoListResourcesViaPython(subscription);
            if (!_quiet) Console.WriteLine($"{message} Done!\n");

            if (!_quiet) Console.WriteLine(output);
            CheckWriteOutputValueFromJson("service.output", "json", output);
        }

        private void DoListProjects()
        {
            var action = "Listing AI projects";
            var command = "service project list";
            var subscription = DemandSubscription(action, command);

            var message = $"{action} for '{subscription}'";

            if (!_quiet) Console.WriteLine(message);
            var output = DoListProjectsViaPython(subscription);
            if (!_quiet) Console.WriteLine($"{message} Done!\n");

            if (!_quiet) Console.WriteLine(output);
            CheckWriteOutputValueFromJson("service.output", "json", output);
        }

        private string DoCreateResourceViaPython(string subscription, string group, string name, string location, string displayName, string description)
        {
            return RunEmbeddedPythonScript("hub_create",
                    "--subscription", subscription,
                    "--group", group,
                    "--name", name, 
                    "--location", location,
                    "--display-name", displayName,
                    "--description", description);
        }

        private string DoCreateProjectViaPython(string subscription, string group, string resource, string name, string location, string displayName, string description)
        {
            return RunEmbeddedPythonScript("project_create",
                    "--subscription", subscription,
                    "--group", group,
                    "--resource", resource,
                    "--name", name, 
                    "--location", location,
                    "--display-name", displayName,
                    "--description", description);
        }

        private string DoListResourcesViaPython(string subscription)
        {
            return RunEmbeddedPythonScript("hub_list", "--subscription", subscription);
        }

        private string DoListProjectsViaPython(string subscription)
        {
            return RunEmbeddedPythonScript("project_list", "--subscription", subscription);
        }

        private string DemandSubscription(string action, string command)
        {
            var subscription = _values.Get("service.subscription", true);
            if (string.IsNullOrEmpty(subscription) || subscription.Contains("rror"))
            {
                _values.AddThrowError(
                    "ERROR:", $"{action}; requires subscription.",
                            "",
                      "TRY:", $"{Program.Name} init",
                              $"{Program.Name} config --set subscription SUBSCRIPTION",
                              $"{Program.Name} {command} --subscription SUBSCRIPTION",
                            "",
                      "SEE:", $"{Program.Name} help {command}");
            }
            return subscription;
        }

        private string DemandName(string valuesName, string action, string command)
        {
            var name = _values.Get(valuesName, true);
            if (string.IsNullOrEmpty(name))
            {
                _values.AddThrowError(
                    "ERROR:", $"{action}; requires name.",
                      "TRY:", $"{Program.Name} {command} --name NAME",
                      "SEE:", $"{Program.Name} help {command}");
            }
            return name;
        }

        private string DemandResource(string action, string command)
        {
            var resource = _values.Get("service.resource.name", true);
            if (string.IsNullOrEmpty(resource))
            {
                _values.AddThrowError(
                    "ERROR:", $"{action}; requires resource.",
                      "TRY:", $"{Program.Name} {command} --resource RESOURCE",
                      "SEE:", $"{Program.Name} help {command}");
            }
            return resource;
        }

        private string DemandRegionLocation(string action, string command)
        {
            var location = _values.Get("service.region.location", true);
            if (string.IsNullOrEmpty(location))
            {
                _values.AddThrowError(
                    "ERROR:", $"{action}; requires location.",
                      "TRY:", $"{Program.Name} {command} --location LOCATION",
                      "SEE:", $"{Program.Name} help {command}");
            }
            return location;
        }

        private string GetGroupName()
        {
            return _values.Get("service.resource.group.name", true);
        }

        private string BuildPythonScriptArgs(params string[] args)
        {
            var sb = new StringBuilder();
            for (int i = 0; i + 1 < args.Length; i += 2)
            {
                var argName = args[i];
                var argValue = args[i + 1];

                if (string.IsNullOrWhiteSpace(argValue)) continue;

                sb.Append(argValue.Contains(' ')
                    ? $"{argName} \"{argValue}\""
                    : $"{argName} {argValue}");
                sb.Append(' ');
            }
            return sb.ToString().Trim();
        }

        private string RunEmbeddedPythonScript(string scriptName, params string[] args)
        {
            var path = FileHelpers.FindFileInHelpPath($"help/include.python.script.{scriptName}.py");
            var script = FileHelpers.ReadAllHelpText(path, Encoding.UTF8);
            var scriptArgs = BuildPythonScriptArgs(args);

            if (Program.Debug) Console.WriteLine($"DEBUG: {scriptName}.py:\n{script}");
            if (Program.Debug) Console.WriteLine($"DEBUG: PythonRunner.RunScriptAsync: '{scriptName}' {scriptArgs}");

            (var exit, var output)= PythonRunner.RunScriptAsync(script, scriptArgs).Result;
            if (exit != 0)
            {
                output = output.Trim('\r', '\n', ' ');
                output = "\n\n    " + output.Replace("\n", "\n    ");

                var info = new List<string>();

                if (output.Contains("azure.identity"))
                {
                    info.Add("WARNING:");
                    info.Add("azure-identity Python wheel not found!");
                    info.Add("");
                    info.Add("TRY:");
                    info.Add("pip install azure-identity");
                    info.Add("SEE:");
                    info.Add("https://pypi.org/project/azure-identity/");
                    info.Add("");
                }
                else if (output.Contains("azure.mgmt.resource"))
                {
                    info.Add("WARNING:");
                    info.Add("azure-mgmt-resource Python wheel not found!");
                    info.Add("");
                    info.Add("TRY:");
                    info.Add("pip install azure-mgmt-resource");
                    info.Add("SEE:");
                    info.Add("https://pypi.org/project/azure-mgmt-resource/");
                    info.Add("");
                }
                else if (output.Contains("azure.ai.ml"))
                {
                    info.Add("WARNING:");
                    info.Add("azure-ai-ml Python wheel not found!");
                    info.Add("");
                    info.Add("TRY:");
                    info.Add("pip install azure-ai-ml");
                    info.Add("SEE:");
                    info.Add("https://pypi.org/project/azure-ai-ml/");
                    info.Add("");
                }
                else if (output.Contains("ModuleNotFoundError"))
                {
                    info.Add("WARNING:");
                    info.Add("Python wheel not found!");
                    info.Add("");
                }

                info.Add("ERROR:");
                info.Add($"Python script failed! (exit code={exit})");
                info.Add("");
                info.Add("OUTPUT:");
                info.Add(output);

                _values.AddThrowError(info[0], info[1], info.Skip(2).ToArray());
            }

            return ParseOutputAndSkipLinesUntilStartsWith(output, "---").Trim('\r', '\n', ' ');
        }

        private string ParseOutputAndSkipLinesUntilStartsWith(string output, string startsWith)
        {
            var lines = output.Split('\n');
            var sb = new StringBuilder();
            var skip = true;
            foreach (var line in lines)
            {
                if (skip && line.StartsWith(startsWith))
                {
                    skip = false;
                }
                else if (!skip)
                {
                    sb.AppendLine(line);
                }
            }
            return sb.ToString();
        }

        private void CheckWriteOutputValueFromJson(string part1, string part2, string json, string valueKey = null)
        {
            var parsed = !string.IsNullOrEmpty(json) ? JToken.Parse(json) : null;
            var value = !string.IsNullOrEmpty(valueKey) ? parsed?[valueKey]?.ToString() : json;
            CheckWriteOutputValue(part1, part2, value);
        }

        private void CheckWriteOutputValue(string part1, string part2, string value)
        {
            if (string.IsNullOrEmpty(value)) return;

            var atValue = _values.Get($"{part1}.{part2}", true);
            if (!string.IsNullOrEmpty(atValue))
            {
                var atValueFile = FileHelpers.GetOutputDataFileName(atValue, _values);
                FileHelpers.WriteAllText(atValueFile, value, Encoding.UTF8);
            }

            var addValue = _values.Get($"{part1}.add.{part2}", true);
            if (!string.IsNullOrEmpty(addValue))
            {
                var addValueFile = FileHelpers.GetOutputDataFileName(addValue, _values);
                FileHelpers.AppendAllText(addValueFile, "\n" + value, Encoding.UTF8);
            }
        }

        private bool _quiet = false;
        private bool _verbose = false;
    }
}
