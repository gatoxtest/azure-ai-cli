//
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE.md file in the project root for full license information.
//

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Net;
using Azure.AI.Details.Common.CLI.ConsoleGui;

namespace Azure.AI.Details.Common.CLI
{
    public partial class AzCliConsoleGui
    {
        public static async Task<AzCli.CognitiveSearchResourceInfoEx?> PickOrCreateAndConfigCogSearchResource(bool allowSkip, string subscription, string location, string groupName, string smartName = null, string smartNameKind = null)
        {
            var resource = await AzCliConsoleGui.PickOrCreateCognitiveSearchResource(allowSkip, subscription, location, groupName, smartName, smartNameKind);
            if (resource == null) return null;

            var keys = await AzCliConsoleGui.LoadSearchResourceKeys(subscription, resource);

            ConfigSetHelpers.ConfigSearchResource(resource.Endpoint, keys.Key1);

            return new AzCli.CognitiveSearchResourceInfoEx
            {
                Id = resource.Id,
                Group = resource.Group,
                Name = resource.Name,
                RegionLocation = resource.RegionLocation,
                Endpoint = resource.Endpoint,
                Key = keys.Key1,
            };
        }
    }
}
