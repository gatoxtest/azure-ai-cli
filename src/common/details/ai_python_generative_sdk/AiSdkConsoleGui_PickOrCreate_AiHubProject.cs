//
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE.md file in the project root for full license information.
//

using System.Text;
using System.Text.Json;
using Azure.AI.Details.Common.CLI.ConsoleGui;
using Azure.AI.Details.Common.CLI.Telemetry;
using Azure.AI.Details.Common.CLI.Telemetry.Events;
using Newtonsoft.Json.Linq;

namespace Azure.AI.Details.Common.CLI
{
    public partial class AiSdkConsoleGui
    {
        public static AiHubProjectInfo PickOrCreateAiHubProject(bool allowCreate, bool allowPick, ICommandValues values, string subscription, string resourceId, out bool createdProject)
        {
            createdProject = false;
            if (allowCreate && allowPick)
                return PickOrCreateAiHubProject(values, subscription, resourceId, out createdProject);
            else if (allowCreate)
            {
                createdProject = true;
                return CreateAiHubProject(values, subscription, resourceId);
            }
            else if (allowPick)
                return PickAiHubProject(values, subscription, resourceId);
            else
                throw new ApplicationException($"CANCELED: No project selected");
        }

        public static async Task<AiHubProjectInfo> ConfigAiHubProject(
            ICommandValues values,
            AiHubProjectInfo project,
            bool createdProject,
            bool allowSkipSearch,
            string subscription,
            string resourceId,
            string groupName,
            string openAiEndpoint,
            string openAiKey,
            string searchEndpoint,
            string searchKey)
        {
            var createdOrPickedSearch = false;
            if (!createdProject)
            {
                openAiEndpoint = string.Empty;
                openAiKey = string.Empty;
                searchEndpoint = string.Empty;
                searchKey = string.Empty;
                createdOrPickedSearch = false;

                (var hubName, var openai, var search) = await VerifyResourceConnections(values, subscription, project.Group, project.Name);

                var alreadyPickedDeployments = values.GetOrDefault("service.openai.deployments.picked", "false") == "true";
                if (alreadyPickedDeployments)
                {
                    openAiEndpoint = values["service.openai.endpoint"];
                    openAiKey = values["service.openai.key"];
                }
                else if (!string.IsNullOrEmpty(openai?.Name))
                {
                    var (chatDeployment, embeddingsDeployment, evaluationDeployment, keys) = await AzCliConsoleGui.PickOrCreateAndConfigCognitiveServicesOpenAiKindResourceDeployments(values, "AZURE OPENAI RESOURCE", true, subscription, openai.Value);
                    openAiEndpoint = openai.Value.Endpoint;
                    openAiKey = keys.Key1;
                }
                else
                {
                    var openAiResource = await AzCliConsoleGui.PickOrCreateAndConfigCognitiveServicesOpenAiKindResource(values, true, subscription);
                    openAiEndpoint = openAiResource.Endpoint;
                    openAiKey = openAiResource.Key;
                }

                if (!string.IsNullOrEmpty(search?.Name))
                {
                    var keys = await AzCliConsoleGui.LoadSearchResourceKeys(subscription, search.Value);
                    ConfigSetHelpers.ConfigSearchResource(search.Value.Endpoint, keys.Key1);
                    searchEndpoint = search.Value.Endpoint;
                    searchKey = keys.Key1;
                }
                else
                {
                    var pickedOrCreated = await AzCliConsoleGui.PickOrCreateAndConfigCogSearchResource(allowSkipSearch, subscription, null, null, project.Name, "aiproj");
                    createdOrPickedSearch = pickedOrCreated != null;
                    if (createdOrPickedSearch)
                    {
                        searchEndpoint = pickedOrCreated.Value.Endpoint;
                        searchKey = pickedOrCreated.Value.Key;
                    }
                }
            }

            Program.Telemetry.Wrap(
                () => GetOrCreateAiHubProjectConnections(values, createdProject || createdOrPickedSearch, subscription, project.Group, project.Name, openAiEndpoint, openAiKey, searchEndpoint, searchKey),
                (outcome, ex, duration) => new InitTelemetryEvent(InitStage.Connections)
                {
                    Outcome = outcome,
                    // TODO set selected?
                    RunId = values.GetOrDefault("telemetry.init.run_id", null),
                    RunType = values.GetOrDefault("telemetry.init.run_type", null),
                    DurationInMs = duration.TotalMilliseconds,
                    Error = ex?.Message
                });

            Program.Telemetry.Wrap(
                () => CreateAiHubProjectConfigJsonFile(subscription, project.Group, project.Name),
                (outcome, ex, duration) => new InitTelemetryEvent(InitStage.Save)
                {
                    Outcome = outcome,
                    RunId = values.GetOrDefault("telemetry.init.run_id", null),
                    RunType = values.GetOrDefault("telemetry.init.run_type", null),
                    DurationInMs = duration.TotalMilliseconds,
                    Error = ex?.Message
                });

            return project;
        }

        public static AiHubProjectInfo PickAiHubProject(ICommandValues values, string subscription, string resourceId)
        {
            return PickOrCreateAiHubProject(false, values, subscription, resourceId, out var createNew);
        }

        public static AiHubProjectInfo PickOrCreateAiHubProject(ICommandValues values, string subscription, string resourceId, out bool createNew)
        {
            return PickOrCreateAiHubProject(true, values, subscription, resourceId, out createNew);
        }

        public static AiHubProjectInfo CreateAiHubProject(ICommandValues values, string subscription, string resourceId)
        {
            var project = TryCreateAiHubProjectInteractive(values, subscription, resourceId);
            return AiHubProjectInfoFromToken(values, project);
        }

        private static AiHubProjectInfo PickOrCreateAiHubProject(bool allowCreate, ICommandValues values, string subscription, string resourceId, out bool createNew)
        {
            ConsoleHelpers.WriteLineWithHighlight($"\n`AZURE AI PROJECT`");
            Console.Write("\rName: *** Loading choices ***");

            var json = PythonSDKWrapper.ListProjects(values, subscription);
            if (Program.Debug) Console.WriteLine(json);

            var parsed = !string.IsNullOrEmpty(json) ? JsonDocument.Parse(json) : default;
            var items = parsed?.GetPropertyArrayOrEmpty("projects") ?? Array.Empty<JsonElement>();

            var choices = new List<string>();
            var itemJsonElements = new List<JsonElement>();
            foreach (var item in items)
            {
                if (item.TryGetProperty("workspace_hub", out var workspaceHubElement))
                {
                    var hub = workspaceHubElement.GetString();
                    var hubOk = string.IsNullOrEmpty(resourceId) || hub == resourceId;
                    if (!hubOk) continue;

                    itemJsonElements.Add(item);

                    var name = item.GetProperty("name").GetString();
                    var location = item.GetProperty("location").GetString();
                    var displayName = item.GetProperty("display_name").GetString();

                    choices.Add(string.IsNullOrEmpty(displayName)
                        ? $"{name} ({location})"
                        : $"{displayName} ({location})");
                }
            }

            if (allowCreate)
            {
                choices.Insert(0, "(Create new)");
            }

            if (choices.Count == 0)
            {
                throw new ApplicationException($"CANCELED: No projects found");
            }

            Console.Write("\rName: ");
            var picked = ListBoxPicker.PickIndexOf(choices.ToArray());
            if (picked < 0)
            {
                throw new ApplicationException($"CANCELED: No project selected");
            }

            Console.WriteLine($"\rName: {choices[picked]}");
            var project = allowCreate
                ? (picked > 0 ? itemJsonElements[picked - 1] : default(JsonElement?))
                : itemJsonElements[picked];

            createNew = allowCreate && picked == 0;
            if (createNew)
            {
                project = TryCreateAiHubProjectInteractive(values, subscription, resourceId);
            }

            return AiHubProjectInfoFromToken(values, project);
        }

        private static JsonElement? TryCreateAiHubProjectInteractive(ICommandValues values, string subscription, string resourceId)
        {
            var group = ResourceGroupNameToken.Data().GetOrDefault(values);
            var location = RegionLocationToken.Data().GetOrDefault(values, "");
            var displayName = ProjectDisplayNameToken.Data().GetOrDefault(values);
            var description = ProjectDescriptionToken.Data().GetOrDefault(values);

            var smartName = ResourceNameToken.Data().GetOrDefault(values);
            var smartNameKind = smartName != null && smartName.Contains("openai") ? "openai" : "oai";

            return TryCreateAiHubProjectInteractive(values, subscription, resourceId, group, location, ref displayName, ref description, smartName, smartNameKind);
        }

        private static AiHubProjectInfo AiHubProjectInfoFromToken(ICommandValues values, JsonElement? project)
        {
            var aiHubProject = new AiHubProjectInfo
            {
                Id = project?.GetPropertyStringOrNull("id"),
                Group = project?.GetPropertyStringOrNull("resource_group"),
                Name = project?.GetPropertyStringOrNull("name"),
                DisplayName = project?.GetPropertyStringOrNull("display_name"),
                RegionLocation = project?.GetPropertyStringOrNull("location"),
                HubId = project?.GetPropertyStringOrNull("workspace_hub"),
            };

            return aiHubProject;
        }

        private static JsonElement? TryCreateAiHubProjectInteractive(ICommandValues values, string subscription, string resourceId, string group, string location, ref string displayName, ref string description, string smartName = null, string smartNameKind = null)
        {
            ConsoleHelpers.WriteLineWithHighlight($"\n`CREATE AZURE AI PROJECT`");

            if (string.IsNullOrEmpty(smartName))
            {
                smartName = group;
                smartNameKind = "rg";
            }

            var name = NamePickerHelper.DemandPickOrEnterName("Name: ", "aiproj", smartName, smartNameKind, AzCliConsoleGui.GetSubscriptionUserName(subscription));
            displayName ??= name;
            description ??= name;

            Console.Write("*** CREATING ***");
            var json = PythonSDKWrapper.CreateProject(values, subscription, group, resourceId, name, location, displayName, description);

            Console.WriteLine("\r*** CREATED ***  ");

            var parsed = !string.IsNullOrEmpty(json) ? JsonDocument.Parse(json) : null;
            return parsed?.GetPropertyElementOrNull("projects");
        }

        public static void GetOrCreateAiHubProjectConnections(ICommandValues values, bool create, string subscription, string groupName, string projectName, string openAiEndpoint, string openAiKey, string searchEndpoint, string searchKey)
        {
            var checkForExistingOpenAiConnection = true;
            var createOpenAiConnection = !string.IsNullOrEmpty(openAiEndpoint) && !string.IsNullOrEmpty(openAiKey) && !checkForExistingOpenAiConnection;

            var checkForExistingSearchConnection = !create;
            var createSearchConnection = !string.IsNullOrEmpty(searchEndpoint) && !string.IsNullOrEmpty(searchKey) && !checkForExistingSearchConnection;

            var connectionsOk = createOpenAiConnection || createSearchConnection || checkForExistingOpenAiConnection || checkForExistingSearchConnection;
            if (connectionsOk) ConsoleHelpers.WriteLineWithHighlight($"\n`AZURE AI PROJECT CONNECTIONS`\n");

            var connectionCount = 0;

            if (createOpenAiConnection || checkForExistingOpenAiConnection)
            {
                if (connectionCount > 0) Console.WriteLine();

                var connectionName = "Default_AzureOpenAI";
                Console.WriteLine($"Connection: {connectionName}");

                Console.Write(createOpenAiConnection ? "*** CREATING ***" : "*** CHECKING ***");

                var connectionType = "azure_open_ai";
                var connectionJson = createOpenAiConnection
                    ? PythonSDKWrapper.CreateConnection(values, subscription, groupName, projectName, connectionName, connectionType, null, openAiEndpoint, openAiKey)
                    : GetConnection(values, subscription, groupName, projectName, connectionName);

                var message = createSearchConnection ? "\r*** CREATED ***  " : null;
                if (checkForExistingOpenAiConnection)
                {
                    var parsed = !string.IsNullOrEmpty(connectionJson) ? JsonDocument.Parse(connectionJson) : null;
                    var connection = parsed?.GetPropertyElementOrNull("connection");
                    var target = connection?.GetPropertyStringOrNull("target");

                    var endpointOk = !string.IsNullOrEmpty(openAiEndpoint);
                    var targetOk = !string.IsNullOrEmpty(target);
                    var targetMatch = targetOk && endpointOk &&
                        (target == openAiEndpoint ||
                         target.Replace(".openai.azure.com/", ".cognitiveservices.azure.com/") == openAiEndpoint);

                    message = !targetOk ?
                        $"\r*** WARNING: {connectionName} no connection found ***  "
                        : !endpointOk
                        ? $"\r*** FOUND: {connectionName} found ***  "
                        : !targetMatch
                            ? $"\r*** WARNING: {connectionName} found but target is {target} ***  "
                            : $"\r*** MATCHED: {connectionName} ***  ";
                }

                Console.WriteLine(message);
                connectionCount++;
            }

            if (createSearchConnection || checkForExistingSearchConnection)
            {
                if (connectionCount > 0) Console.WriteLine();

                var connectionName = "AzureAISearch";
                Console.WriteLine($"Connection: {connectionName}");

                Console.Write(createSearchConnection ? "*** CREATING ***" : "*** CHECKING ***");

                var connectionType = "cognitive_search";
                var connectionJson = createSearchConnection
                    ? PythonSDKWrapper.CreateConnection(values, subscription, groupName, projectName, connectionName, connectionType, null, searchEndpoint, searchKey)
                    : GetConnection(values, subscription, groupName, projectName, connectionName);

                var message = createSearchConnection ? "\r*** CREATED ***  " : null;
                if (checkForExistingSearchConnection)
                {
                    var parsed = !string.IsNullOrEmpty(connectionJson) ? JsonDocument.Parse(connectionJson) : null;
                    var connection = parsed?.GetPropertyElementOrNull("connection");
                    var target = connection?.GetPropertyStringOrNull("target");

                    var targetOk = !string.IsNullOrEmpty(target);
                    var endpointOk = !string.IsNullOrEmpty(searchEndpoint);
                    var targetMatch = targetOk && endpointOk && target == searchEndpoint;

                    message = !targetOk
                        ? $"\r*** WARNING: {connectionName} no connection found ***  "
                        : !endpointOk
                            ? $"\r*** FOUND: {connectionName} found ***  "
                            : !targetMatch
                                ? $"\r*** WARNING: {connectionName} found but target is {target} ***  "
                                : $"\r*** MATCHED: {connectionName} ***  ";
                }

                Console.WriteLine(message);
                connectionCount++;
            }
        }

        private static string GetConnection(ICommandValues values, string subscription, string groupName, string projectName, string connectionName)
        {
            try
            {
                return PythonSDKWrapper.GetConnection(values, subscription, groupName, projectName, connectionName);
            }
            catch (Exception)
            {
                values.Reset("error");
                return null;
            }
        }

        public static void CreateAiHubProjectConfigJsonFile(string subscription, string groupName, string projectName)
        {
            ConfigSetHelpers.ConfigureProject(subscription, groupName, projectName);
            Console.WriteLine();

            dynamic configJsonData = new
            {
                subscription_id = subscription,
                resource_group = groupName,
                // project_name = projectName,
                workspace_name = projectName,
            };

            var configJson = JsonSerializer.Serialize(configJsonData, new JsonSerializerOptions { WriteIndented = true });
            var configJsonFile = new FileInfo("config.json");
            FileHelpers.WriteAllText(configJsonFile.FullName, configJson, new UTF8Encoding(false));

            Console.WriteLine($"{configJsonFile.Name} (saved at {configJsonFile.Directory})\n");
            Console.WriteLine("  " + configJson.Replace("\n", "\n  "));
        }
    }
}
