//
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE.md file in the project root for full license information.
//

using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Azure.AI.Details.Common.CLI.ConsoleGui;
using System.Text.Json;
using System.IO;

namespace Azure.AI.Details.Common.CLI
{
    public partial class AiSdkConsoleGui
    {
        public static async Task<AiHubResourceInfo> PickAiHubResource(ICommandValues values, string subscription)
        {
            return await PickOrCreateAiHubResource(false, values, subscription);
        }

        public static async Task<AiHubResourceInfo> PickOrCreateAiHubResource(ICommandValues values, string subscription)
        {
            return await PickOrCreateAiHubResource(true, values, subscription);
        }

        public static async Task<AiHubResourceInfo> CreateAiHubResource(ICommandValues values, string subscription)
        {
            var resource = await TryCreateAiHubResourceInteractive(values, subscription);
            return FinishPickOrCreateAiHubResource(values, resource);
        }

        private static async Task<AiHubResourceInfo> PickOrCreateAiHubResource(bool allowCreate, ICommandValues values, string subscription)
        {
            ConsoleHelpers.WriteLineWithHighlight($"\n`AZURE AI RESOURCE`");
            Console.Write("\rName: *** Loading choices ***");

            var json = PythonSDKWrapper.ListResources(values, subscription);
            if (Program.Debug) Console.WriteLine(json);

            var parsed = !string.IsNullOrEmpty(json) ? JToken.Parse(json) : null;
            var items = parsed?.Type == JTokenType.Object ? parsed["resources"] : new JArray();

            var choices = new List<string>();
            foreach (var item in items)
            {
                var name = item["name"].Value<string>();
                var location = item["location"].Value<string>();
                var displayName = item["display_name"].Value<string>();

                choices.Add(string.IsNullOrEmpty(displayName)
                    ? $"{name} ({location})"
                    : $"{displayName} ({location})");
            }

            if (allowCreate)
            {
                choices.Insert(0, "(Create w/ integrated Open AI + AI Services)");
                choices.Insert(1, "(Create w/ standalone Open AI resource)");
            }

            if (choices.Count == 0)
            {
                throw new ApplicationException($"CANCELED: No resources found");
            }

            Console.Write("\rName: ");
            var picked = ListBoxPicker.PickIndexOf(choices.ToArray());
            if (picked < 0)
            {
                throw new ApplicationException($"CANCELED: No resource selected");
            }

            Console.WriteLine($"\rName: {choices[picked]}");
            var resource = allowCreate
                ? (picked >= 2 ? items.ToArray()[picked - 2] : null)
                : items.ToArray()[picked];

            var byoServices = allowCreate && picked == 1;
            if (byoServices)
            {
                var regionFilter = values.GetOrDefault("init.service.resource.region.name", "");
                var groupFilter = values.GetOrDefault("init.service.resource.group.name", "");
                var resourceFilter = values.GetOrDefault("init.service.cognitiveservices.resource.name", "");
                var kind = values.GetOrDefault("init.service.cognitiveservices.resource.kind", "OpenAI;AIServices");
                var sku = values.GetOrDefault("init.service.cognitiveservices.resource.sku", Program.CognitiveServiceResourceSku);
                var yes = values.GetOrDefault("init.service.cognitiveservices.terms.agree", false);

                var openAiResource = await AzCliConsoleGui.PickOrCreateAndConfigCognitiveServicesOpenAiKindResource(true, true, subscription, regionFilter, groupFilter, resourceFilter, kind, sku, yes);
                values.Reset("service.openai.deployments.picked", "true");

                ResourceGroupNameToken.Data().Set(values, openAiResource.Group);
                values.Reset("service.resource.region.name", openAiResource.RegionLocation);

                values.Reset("service.openai.endpoint", openAiResource.Endpoint);
                values.Reset("service.openai.key", openAiResource.Key);
                values.Reset("service.openai.resource.id", openAiResource.Id);
                values.Reset("service.openai.resource.kind", openAiResource.Kind);
            }

            var createNewHub = allowCreate && (picked == 0 || picked == 1);
            if (createNewHub)
            {
                resource = await TryCreateAiHubResourceInteractive(values, subscription);
            }

            return FinishPickOrCreateAiHubResource(values, resource);
        }

        private static async Task<JToken> TryCreateAiHubResourceInteractive(ICommandValues values, string subscription)
        {
            var locationName = values.GetOrDefault("service.resource.region.name", "");
            var groupName = ResourceGroupNameToken.Data().GetOrDefault(values);
            var displayName = ResourceDisplayNameToken.Data().GetOrDefault(values);
            var description = ResourceDescriptionToken.Data().GetOrDefault(values);

            var openAiResourceId = values.GetOrDefault("service.openai.resource.id", "");
            var openAiResourceKind = values.GetOrDefault("service.openai.resource.kind", "");

            var smartName = ResourceNameToken.Data().GetOrDefault(values);
            var smartNameKind = smartName != null && smartName.Contains("openai") ? "openai" : "oai";

            return await TryCreateAiHubResourceInteractive(values, subscription, locationName, groupName, displayName, description, openAiResourceId, openAiResourceKind, smartName, smartNameKind);
        }

        private static AiHubResourceInfo FinishPickOrCreateAiHubResource(ICommandValues values, JToken resource)
        {
            var aiHubResource = new AiHubResourceInfo
            {
                Id = resource["id"].Value<string>(),
                Group = resource["resource_group"].Value<string>(),
                Name = resource["name"].Value<string>(),
                RegionLocation = resource["location"].Value<string>(),
            };

            ResourceIdToken.Data().Set(values, aiHubResource.Id);
            ResourceNameToken.Data().Set(values, aiHubResource.Name);
            ResourceGroupNameToken.Data().Set(values, aiHubResource.Group);
            RegionLocationToken.Data().Set(values, aiHubResource.RegionLocation);

            return aiHubResource;
        }

        private static async Task<JToken> TryCreateAiHubResourceInteractive(ICommandValues values, string subscription, string locationName, string groupName, string displayName, string description, string openAiResourceId, string openAiResourceKind, string smartName = null, string smartNameKind = null)
        {
            var sectionHeader = $"\n`CREATE AZURE AI RESOURCE`";
            ConsoleHelpers.WriteLineWithHighlight(sectionHeader);

            var groupOk = !string.IsNullOrEmpty(groupName);
            if (!groupOk)
            {
                var location =  await AzCliConsoleGui.PickRegionLocationAsync(true, locationName, false);
                locationName = location.Name;
            }

            var (group, createdNew) = await AzCliConsoleGui.PickOrCreateResourceGroup(true, subscription, groupOk ? null : locationName, groupName);
            groupName = group.Name;

            if (string.IsNullOrEmpty(smartName))
            {
                smartName = group.Name;
                smartNameKind = "rg";
            }

            if (createdNew)
            {
                ConsoleHelpers.WriteLineWithHighlight(sectionHeader);
            }

            var name = NamePickerHelper.DemandPickOrEnterName("Name: ", "ai", smartName, smartNameKind); // TODO: What will this really be called?
            displayName ??= name;
            description ??= name;

            Console.Write("*** CREATING ***");
            var json = PythonSDKWrapper.CreateResource(values, subscription, groupName, name, locationName, displayName, description, openAiResourceId, openAiResourceKind);

            Console.WriteLine("\r*** CREATED ***  ");

            var parsed = !string.IsNullOrEmpty(json) ? JToken.Parse(json) : null;
            return parsed["resource"];
        }
    }
}
