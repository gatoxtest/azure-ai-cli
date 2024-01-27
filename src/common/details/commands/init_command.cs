//
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE.md file in the project root for full license information.
//

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Net;
using Newtonsoft.Json.Linq;
using Azure.AI.Details.Common.CLI.ConsoleGui;

namespace Azure.AI.Details.Common.CLI
{
    public class InitCommand : Command
    {
        public InitCommand(ICommandValues values)
        {
            _values = values.ReplaceValues();
            _quiet = _values.GetOrDefault("x.quiet", false);
            _verbose = _values.GetOrDefault("x.verbose", true);
        }

        public bool RunCommand()
        {
            RunInitCommand().Wait();
            return _values.GetOrDefault("passed", true);
        }

        private async Task<bool> RunInitCommand()
        {
            try
            {
                await DoCommand(_values.GetCommand());
                return _values.GetOrDefault("passed", true);
            }
            catch (ApplicationException ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine();
                _values.Reset("passed", "false");
                return false;
            }
        }

        private async Task DoCommand(string command)
        {
            StartCommand();

            DisplayInitServiceBanner();

            CheckPath();

            var interactive = _values.GetOrDefault("init.service.interactive", true);

            switch (command)
            {
                // case "init": await DoInitServiceCommand(); break;
                case "init": await DoInitRootAsync(); break;

                case "init.resource": await DoInitRootHubResource(interactive); break;

                case "init.project": await DoInitRootProject(interactive, true, true); break;
                case "init.project.new": await DoInitRootProject(interactive, true, false); break;
                case "init.project.select": await DoInitRootProject(interactive, false, true); break;

                case "init.aiservices": await DoInitRootCognitiveServicesAIServicesKind(interactive); break;
                case "init.cognitiveservices": await DoInitRootCognitiveServicesCognitiveServicesKind(interactive); break;
                case "init.openai": await DoInitRootOpenAi(interactive); break;
                case "init.search": await DoInitRootSearch(interactive); break;
                case "init.speech": await DoInitRootSpeech(interactive); break;

                // POST-IGNITE: TODO: add ability to init deployments
                // TODO: ensure that deployments in "openai" flow can be skipped

                default:
                    _values.AddThrowError("WARNING", $"Unknown command '{command}'");
                    break;
            }

            StopCommand();
            DisposeAfterStop();
            DeleteTemporaryFiles();
        }

        private async Task DoInitRootAsync()
        {
            var interactive = _values.GetOrDefault("init.service.interactive", true);
            if (!interactive) ThrowInteractiveNotSupportedApplicationException(); // POST-IGNITE: TODO: Add back non-interactive mode support

            ConsoleHelpers.WriteLineWithHighlight("`AI INIT`\n\n  Initializes (creates, selects, or attaches to) AI Projects and services.\n");

            var existing = FileHelpers.FindFileInDataPath("config.json", _values);
            if (existing != null)
            {
                await DoInitRootVerifyConfigFileAsync(interactive, existing);
            }
            else
            {
                await DoInitRootMenuPick();
            }
        }

        private async Task DoInitRootVerifyConfigFileAsync(bool interactive, string fileName)
        {
            ParseConfigJson(fileName, out string subscription, out string groupName, out string projectName);

            var (hubName, openai, search) = await VerifyProjectAsync(interactive, subscription, groupName, projectName);
            if (openai != null && search != null)
            {
                await DoInitRootConfirmVerifiedProjectResources(interactive, subscription, projectName, hubName, openai.Value, search.Value);
            }
            else
            {
                await DoInitRootMenuPick();
            }
        }

        private async Task<(string, AzCli.CognitiveServicesResourceInfo?, AzCli.CognitiveSearchResourceInfo?)> VerifyProjectAsync(bool interactive, string subscription, string groupName, string projectName)
        {
            Console.WriteLine($"  PROJECT: {projectName}");
            var validated = await AzCliConsoleGui.ValidateSubscriptionAsync(interactive, subscription, "  SUBSCRIPTION");

            ConsoleHelpers.WriteLineWithHighlight("\n  `ATTACHED SERVICES AND RESOURCES`\n");

            var message = "    Validating...";
            Console.Write(message);

            var (hubName, openai, search) = await AiSdkConsoleGui.VerifyResourceConnections(_values, validated?.Id, groupName, projectName);
            if (openai != null && search != null)
            {
                Console.Write(new string(' ', message.Length + 2) + "\r");
                return (hubName, openai, search);
            }
            else
            {
                ConsoleHelpers.WriteLineWithHighlight($"\r{message} `#e_;WARNING: Configuration could not be validated!`");
                Console.WriteLine();
                return (null, null, null);
            }
        }

        private async Task DoInitRootConfirmVerifiedProjectResources(bool interactive, string subscription, string projectName, string resourceName, AzCli.CognitiveServicesResourceInfo openaiResource, AzCli.CognitiveSearchResourceInfo searchResource)
        {
            ConsoleHelpers.WriteLineWithHighlight($"    AI RESOURCE: {resourceName}");
            ConsoleHelpers.WriteLineWithHighlight($"    AI SEARCH RESOURCE: {searchResource.Name}");
            // Console.WriteLine();
            ConsoleHelpers.WriteLineWithHighlight($"    AZURE OPENAI RESOURCE: {openaiResource.Name}");

            // TODO: If there's a way to get the deployments, get them, and do this... Print correct stuff here... 
            // ConsoleHelpers.WriteLineWithHighlight($"    AZURE OPENAI DEPLOYMENT (CHAT): {{chat-deployment-name}}                `#e_;<== work in progress`");
            // ConsoleHelpers.WriteLineWithHighlight($"    AZURE OPENAI DEPLOYMENT (EMBEDDING): {{embedding-deployment-name}}    `#e_;<== work in progress`");
            // ConsoleHelpers.WriteLineWithHighlight($"    AZURE OPENAI DEPLOYMENT (EVALUATION): {{evaluation-deployment-name}}    `#e_;<== work in progress`");

            Console.WriteLine();
            var label = "  Initialize";
            Console.Write($"{label}: ");

            var choices = new string[] { $"PROJECT: {projectName}", $"     OR: (Initialize something else)" };
            var picked = ListBoxPicker.PickIndexOf(choices.ToArray());
            if (picked < 0)
            {
                Console.WriteLine($"\r{label}: CANCELED (no selection)");
                return;
            }
            else if (picked > 0)
            {
                Console.Write(new string(' ', label.Length + 2) + "\r");
                Console.WriteLine("  Initializing something else...\n");
                await DoInitRootMenuPick();
                return;
            }
            else
            {
                Console.Write("\r" + new string(' ', label.Length + 2) + "\r");

                var (chatDeployment, embeddingsDeployment, evaluationDeployment, keys) = await AzCliConsoleGui.PickOrCreateAndConfigCognitiveServicesOpenAiKindResourceDeployments("AZURE OPENAI RESOURCE", interactive, true, subscription, openaiResource);

                var searchKeys = await AzCliConsoleGui.LoadSearchResourceKeys(subscription, searchResource);
                ConfigSetHelpers.ConfigSearchResource(searchResource.Endpoint, searchKeys.Key1);

                ConfigSetHelpers.ConfigSet("@subscription", subscription);
                ConfigSetHelpers.ConfigSet("@project", projectName);
                ConfigSetHelpers.ConfigSet("@group", openaiResource.Group);
            }
        }

        private bool ParseConfigJson(string fileName, out string subscription, out string groupName, out string projectName)
        {
            subscription = groupName = projectName = null;

            var json = FileHelpers.ReadAllHelpText(fileName, Encoding.UTF8);
            try
            {
                var config = JObject.Parse(json);
                subscription = config.ContainsKey("subscription_id") ? config["subscription_id"].ToString() : null;
                groupName = config.ContainsKey("resource_group") ? config["resource_group"].ToString() : null;
                projectName = config.ContainsKey("project_name") ? config["project_name"].ToString() : null;
                projectName ??= config.ContainsKey("workspace_name") ? config["workspace_name"].ToString() : null;
            }
            catch (Exception)
            {
                return false;
            }

            return !string.IsNullOrEmpty(subscription) && !string.IsNullOrEmpty(groupName) && !string.IsNullOrEmpty(projectName);
        }

        private async Task DoInitRootMenuPick()
        {
            var interactive = true;

            Console.WriteLine("  Choose between initializing:");
            ConsoleHelpers.WriteLineWithHighlight("  - AI Project: Recommended when using `Azure AI Studio` and/or connecting to multiple AI services.");
            Console.WriteLine("  - Standalone resources: Recommended when building simple solutions connecting to a single AI service.");
            Console.WriteLine();

            var label = "  Initialize";
            Console.Write($"{label}: ");
            var choiceToPart = new Dictionary<string, string>
            {
                // ["AI Project"] = "init-root-project-select-or-create",
                ["New AI Project"] = "init-root-project-create",
                ["Existing AI Project"] = "init-root-project-select",
                ["Standalone resources"] = "init-root-standalone-select-or-create",
            };
            var partToLabelDisplay = new Dictionary<string, string>()
            {
                // ["init-root-project-select-or-create"] = "AI Project",
                ["init-root-project-create"] = "New AI Project",
                ["init-root-project-select"] = "Existing AI Project",
                ["init-root-standalone-select-or-create"] = null
            };

            var choices = choiceToPart.Keys.ToArray();
            var picked = ListBoxPicker.PickIndexOf(choices.ToArray());
            if (picked < 0)
            {
                Console.WriteLine($"\r{label}: CANCELED (no selection)");
                return;
            }

            var part = choiceToPart[choices[picked]];
            var display = partToLabelDisplay[part];

            Console.Write(display == null
                ? new string(' ', label.Length + 2) + "\r"
                : $"\r{label.Trim()}: {display}\n");
            await DoInitServiceParts(interactive, part.Split(';').ToArray());
        }

        private async Task DoInitStandaloneResources(bool interactive)
        {
            Console.WriteLine("  Standalone resources:");
            Console.WriteLine("  - Azure AI Services (v2): Includes Azure Speech, Vision, and OpenAI");
            Console.WriteLine("  - Azure AI Services (v1): Includes Azure Speech, Vision, Language, and Search");
            Console.WriteLine("  - Azure OpenAI: Provides access to OpenAI's powerful language models.");
            Console.WriteLine("  - Azure Search: Provides keyword, vector, and hybrid search capabilities.");
            Console.WriteLine("  - Azure Speech: Provides speech recognition, synthesis, and translation.");
            Console.WriteLine();

            var label = "  Initialize";
            Console.Write($"{label}: ");
            var choiceToPart = new Dictionary<string, string>
            {
                ["Azure AI Services (v2)"] = "init-root-cognitiveservices-ai-services-kind-create-or-select",
                ["Azure AI Services (v1)"] = "init-root-cognitiveservices-cognitiveservices-kind-create-or-select",
                ["Azure OpenAI"] = "init-root-openai-create-or-select",
                ["Azure Search"] = "init-root-search-create-or-select",
                ["Azure Speech"] = "init-root-speech-create-or-select"
            };

            var partToLabelDisplay = new Dictionary<string, string>()
            {
                ["init-root-cognitiveservices-ai-services-kind-create-or-select"] = "Azure AI Services (v2)",
                ["init-root-cognitiveservices-cognitiveservices-kind-create-or-select"] = "Azure AI Services (v1)",
                ["init-root-openai-create-or-select"] = "Azure OpenAI",
                ["init-root-search-create-or-select"] = "Azure Search",
                ["init-root-speech-create-or-select"] = "Azure Speech"
            };


            var choices = choiceToPart.Keys.ToArray();

            var picked = ListBoxPicker.PickIndexOf(choices.ToArray());
            if (picked < 0)
            {
                Console.WriteLine("\rInitialize: CANCELED (no selection)");
                return;
            }
    
            var part = choiceToPart[choices[picked]];
            var display = partToLabelDisplay[part];
            Console.WriteLine($"\rInitialize: {display}");

            await DoInitServiceParts(true, part.Split(';', StringSplitOptions.RemoveEmptyEntries));
        }

        private async Task DoInitServiceParts(bool interactive, params string[] operations)
        {
            foreach (var operation in operations)
            {
                if (Program.Debug) Console.WriteLine($"OPERATION: {operation}");

                var task = operation switch
                {
                    "init-root-project-select-or-create" => DoInitRootProject(interactive, true, true),
                    "init-root-project-select" => DoInitRootProject(interactive, false, true),
                    "init-root-project-create" => DoInitRootProject(interactive, true, false),

                    "project-select-or-create" => DoInitProject(interactive, true, true),
                    "project-select" => DoInitProject(interactive, false, true),
                    "project-create" => DoInitProject(interactive, true, false),

                    "init-root-standalone-select-or-create" => DoInitStandaloneResources(interactive),
                    "init-root-cognitiveservices-ai-services-kind-create-or-select" => DoInitRootCognitiveServicesAIServicesKind(interactive),
                    "init-root-cognitiveservices-cognitiveservices-kind-create-or-select" => DoInitRootCognitiveServicesCognitiveServicesKind(interactive),
                    "init-root-openai-create-or-select" => DoInitRootOpenAi(interactive),
                    "init-root-search-create-or-select" => DoInitRootSearch(interactive),
                    "init-root-speech-create-or-select" => DoInitRootSpeech(interactive),

                    "subscription" => DoInitSubscriptionId(interactive),
                    "cognitiveservices-ai-services-kind" => DoInitCognitiveServicesAIServicesKind(interactive),
                    "cognitiveservices-cognitiveservices-kind" => DoInitCognitiveServicesCognitiveServicesKind(interactive),
                    "openai" => DoInitOpenAi(interactive),
                    "search" => DoInitSearch(interactive),

                    _ => throw new ApplicationException($"WARNING: NOT YET IMPLEMENTED")
                };
                await task;
            }
        }

        private async Task DoInitSubscriptionId(bool interactive)
        {
            var subscriptionFilter = SubscriptionToken.Data().GetOrDefault(_values, "");
            var subscriptionId = await AzCliConsoleGui.PickSubscriptionIdAsync(interactive, interactive, subscriptionFilter);
            SubscriptionToken.Data().Set(_values, subscriptionId);
        }

        private async Task DoInitRootHubResource(bool interactive)
        {
            if (!interactive) ThrowInteractiveNotSupportedApplicationException(); // POST-IGNITE: TODO: Add back non-interactive mode support

            await DoInitSubscriptionId(interactive);
            await DoInitHubResource(interactive);
        }

        private async Task DoInitHubResource(bool interactive)
        {
            var subscription = SubscriptionToken.Data().GetOrDefault(_values, "");
            var aiHubResource = await AiSdkConsoleGui.PickOrCreateAiHubResource(_values, subscription);
        }

        private async Task DoInitRootProject(bool interactive, bool allowCreate = true, bool allowPick = true)
        {
            if (!interactive) ThrowInteractiveNotSupportedApplicationException(); // POST-IGNITE: TODO: Add back non-interactive mode support

            await DoInitSubscriptionId(interactive);
            await DoInitProject(interactive, allowCreate, allowPick);
        }

        private async Task DoInitProject(bool interactive, bool allowCreate = true, bool allowPick = true, bool allowSkipDeployments = true, bool allowSkipSearch = true)
        {
            if (allowCreate)
            {
                await DoInitHubResource(interactive);
            }

            var subscription = SubscriptionToken.Data().GetOrDefault(_values, "");
            var resourceId = ResourceIdToken.Data().GetOrDefault(_values, null);
            var groupName = ResourceGroupNameToken.Data().GetOrDefault(_values);

            var openAiEndpoint = _values.GetOrDefault("service.openai.endpoint", null);
            var openAiKey = _values.GetOrDefault("service.openai.key", null);
            var searchEndpoint = _values.GetOrDefault("service.search.endpoint", null);
            var searchKey = _values.GetOrDefault("service.search.key", null);

            var project = await AiSdkConsoleGui.PickOrCreateAndConfigAiHubProject(allowCreate, allowPick, allowSkipDeployments, allowSkipSearch, _values, subscription, resourceId, groupName, openAiEndpoint, openAiKey, searchEndpoint, searchKey);

            ProjectNameToken.Data().Set(_values, project.Name);
            _values.Reset("service.project.id", project.Id);
        }

        private async Task DoInitRootOpenAi(bool interactive)
        {
            if (!interactive) ThrowInteractiveNotSupportedApplicationException(); // POST-IGNITE: TODO: Add back non-interactive mode support

            await DoInitSubscriptionId(interactive);
            await DoInitOpenAi(interactive);
        }

        private async Task DoInitOpenAi(bool interactive, bool allowSkipDeployments = true)
        {
            var subscriptionId = SubscriptionToken.Data().GetOrDefault(_values, "");
            var regionFilter = _values.GetOrDefault("init.service.resource.region.name", "");
            var groupFilter = _values.GetOrDefault("init.service.resource.group.name", "");
            var resourceFilter = _values.GetOrDefault("init.service.cognitiveservices.resource.name", "");
            var kind = _values.GetOrDefault("init.service.cognitiveservices.resource.kind", "OpenAI;AIServices");
            var sku = _values.GetOrDefault("init.service.cognitiveservices.resource.sku", Program.CognitiveServiceResourceSku);
            var yes = _values.GetOrDefault("init.service.cognitiveservices.terms.agree", false);

            var chatDeploymentFilter = _values.GetOrDefault("init.chat.model.deployment.name", "");
            var embeddingsDeploymentFilter = _values.GetOrDefault("init.embeddings.model.deployment.name", "");
            var evaluationsDeploymentFilter = _values.GetOrDefault("init.evaluation.model.deployment.name", "");

            var resource = await AzCliConsoleGui.PickOrCreateAndConfigCognitiveServicesOpenAiKindResource(interactive, allowSkipDeployments, subscriptionId, regionFilter, groupFilter, resourceFilter, kind, sku, yes, chatDeploymentFilter, embeddingsDeploymentFilter, evaluationsDeploymentFilter);
            _values.Reset("service.openai.deployments.picked", "true");

            SubscriptionToken.Data().Set(_values, subscriptionId);
            _values.Reset("service.resource.region.name", resource.RegionLocation);
            _values.Reset("service.openai.endpoint", resource.Endpoint);
            _values.Reset("service.openai.key", resource.Key);
            _values.Reset("service.openai.resource.id", resource.Id);
            _values.Reset("service.openai.resource.kind", resource.Kind);
            ResourceNameToken.Data().Set(_values, resource.Name);
            ResourceGroupNameToken.Data().Set(_values, resource.Group);
        }

        private async Task DoInitRootCognitiveServicesAIServicesKind(bool interactive)
        {
            if (!interactive) ThrowInteractiveNotSupportedApplicationException(); // POST-IGNITE: TODO: Add back non-interactive mode support

            await DoInitSubscriptionId(interactive);
            await DoInitCognitiveServicesAIServicesKind(interactive);
        }

        private async Task DoInitCognitiveServicesAIServicesKind(bool interactive, bool allowSkipDeployments = true)
        {
            var subscriptionId = SubscriptionToken.Data().GetOrDefault(_values, "");
            var regionFilter = _values.GetOrDefault("init.service.resource.region.name", "");
            var groupFilter = _values.GetOrDefault("init.service.resource.group.name", "");
            var resourceFilter = _values.GetOrDefault("init.service.cognitiveservices.resource.name", "");
            var kind = _values.GetOrDefault("init.service.cognitiveservices.resource.kind", "AIServices");
            var sku = _values.GetOrDefault("init.service.cognitiveservices.resource.sku", Program.CognitiveServiceResourceSku);
            var yes = _values.GetOrDefault("init.service.cognitiveservices.terms.agree", false);

            var resource = await AzCliConsoleGui.PickOrCreateAndConfigCognitiveServicesAiServicesKindResource(interactive, allowSkipDeployments, subscriptionId, regionFilter, groupFilter, resourceFilter, kind, sku, yes);

            SubscriptionToken.Data().Set(_values, subscriptionId);
            _values.Reset("service.resource.region.name", resource.RegionLocation);
            _values.Reset("service.openai.endpoint", resource.Endpoint);
            _values.Reset("service.openai.key", resource.Key);
            _values.Reset("service.openai.resource.id", resource.Id);
            _values.Reset("service.openai.resource.kind", resource.Kind);
            ResourceNameToken.Data().Set(_values, resource.Name);
            ResourceGroupNameToken.Data().Set(_values, resource.Group);
        }

        private async Task DoInitRootCognitiveServicesCognitiveServicesKind(bool interactive)
        {
            if (!interactive) ThrowInteractiveNotSupportedApplicationException(); // POST-IGNITE: TODO: Add back non-interactive mode support

            await DoInitSubscriptionId(interactive);
            await DoInitCognitiveServicesCognitiveServicesKind(interactive);
        }

        private async Task DoInitCognitiveServicesCognitiveServicesKind(bool interactive)
        {
            var subscriptionId = SubscriptionToken.Data().GetOrDefault(_values, "");
            var regionFilter = _values.GetOrDefault("init.service.resource.region.name", "");
            var groupFilter = _values.GetOrDefault("init.service.resource.group.name", "");
            var resourceFilter = _values.GetOrDefault("init.service.cognitiveservices.resource.name", "");
            var kind = _values.GetOrDefault("init.service.cognitiveservices.resource.kind", "CognitiveServices");
            var sku = _values.GetOrDefault("init.service.cognitiveservices.resource.sku", Program.CognitiveServiceResourceSku);
            var yes = _values.GetOrDefault("init.service.cognitiveservices.terms.agree", false);

            var resource = await AzCliConsoleGui.PickOrCreateAndConfigCognitiveServicesCognitiveServicesKindResource(interactive, subscriptionId, regionFilter, groupFilter, resourceFilter, kind, sku, yes);

            SubscriptionToken.Data().Set(_values, subscriptionId);
            _values.Reset("services.region", resource.RegionLocation);
            _values.Reset("services.endpoint", resource.Endpoint);
            _values.Reset("services.key", resource.Key);
            ResourceNameToken.Data().Set(_values, resource.Name);
            ResourceGroupNameToken.Data().Set(_values, resource.Group);
        }

        private async Task DoInitRootSearch(bool interactive)
        {
            if (!interactive) ThrowInteractiveNotSupportedApplicationException(); // POST-IGNITE: TODO: Add back non-interactive mode support

            await DoInitSubscriptionId(interactive);
            await DoInitSearch(interactive, false);
        }

        private async Task DoInitSearch(bool interactive, bool allowSkipSearch = true)
        {
            var subscription = SubscriptionToken.Data().GetOrDefault(_values, "");
            var location = _values.GetOrDefault("service.resource.region.name", "");
            var groupName = ResourceGroupNameToken.Data().GetOrDefault(_values, "");

            var smartName = ResourceNameToken.Data().GetOrDefault(_values);
            var smartNameKind = smartName != null && smartName.Contains("openai") ? "openai" : "oai";

            var resource = await AzCliConsoleGui.PickOrCreateAndConfigCogSearchResource(allowSkipSearch, subscription, location, groupName, smartName, smartNameKind);

            _values.Reset("service.search.endpoint", resource?.Endpoint);
            _values.Reset("service.search.key", resource?.Key);
        }

        private async Task DoInitRootSpeech(bool interactive)
        {
            await DoInitSubscriptionId(interactive);
            await DoInitSpeech(interactive);
        }

        private async Task DoInitSpeech(bool interactive)
        {
            var subscriptionId = SubscriptionToken.Data().GetOrDefault(_values, "");
            var regionFilter = _values.GetOrDefault("init.service.resource.region.name", "");
            var groupFilter = _values.GetOrDefault("init.service.resource.group.name", "");
            var resourceFilter = _values.GetOrDefault("init.service.cognitiveservices.resource.name", "");
            var kind = _values.GetOrDefault("init.service.cognitiveservices.resource.kind", "SpeechServices");
            var sku = _values.GetOrDefault("init.service.cognitiveservices.resource.sku", "S0");
            var yes = _values.GetOrDefault("init.service.cognitiveservices.terms.agree", false);

            var resource = await AzCliConsoleGui.PickOrCreateAndConfigCognitiveServicesSpeechServicesKindResource(interactive, subscriptionId, regionFilter, groupFilter, resourceFilter, kind, sku, yes);

            SubscriptionToken.Data().Set(_values, subscriptionId);
        }

        private void DisplayInitServiceBanner()
        {
            if (_quiet) return;

            var logo = FileHelpers.FindFileInHelpPath($"help/include.{Program.Name}.init.ascii.logo");
            if (!string.IsNullOrEmpty(logo))
            {
                var text = FileHelpers.ReadAllHelpText(logo, Encoding.UTF8);
                ConsoleHelpers.WriteLineWithHighlight(text + "\n");
            }
            else
            {
                ConsoleHelpers.WriteLineWithHighlight($"`{Program.Name.ToUpper()} INIT`");
            }
        }

        private static void ThrowInteractiveNotSupportedApplicationException()
        {
            throw new ApplicationException("WARNING: Non-interactive mode not supported");
        }

        private void StartCommand()
        {
            CheckPath();
            LogHelpers.EnsureStartLogFile(_values);

            // _display = new DisplayHelper(_values);

            // _output = new OutputHelper(_values);
            // _output.StartOutput();

            _lock = new SpinLock();
            _lock.StartLock();
        }

        private void StopCommand()
        {
            _lock.StopLock(5000);

            // LogHelpers.EnsureStopLogFile(_values);
            // _output.CheckOutput();
            // _output.StopOutput();

            _stopEvent.Set();
        }

        private SpinLock _lock = null;
        private readonly bool _quiet = false;
        private readonly bool _verbose = false;
    }
}
