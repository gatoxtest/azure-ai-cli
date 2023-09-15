//
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE.md file in the project root for full license information.
//

namespace Azure.AI.Details.Common.CLI
{
    class ProjectConnectionEndpointToken
    {
        public static NamedValueTokenData Data() => new NamedValueTokenData(_optionName, _fullName, _valueCount, _optionExample, _requiredDisplayName);
        public static INamedValueTokenParser Parser() => new NamedValueTokenParser(_optionName, _fullName, _fullNameRequiredParts, _valueCount);

        private const string _requiredDisplayName = "connection endpoint";
        private const string _optionName = "--connection-endpoint";
        private const string _optionExample = "ENDPOINT";
        private const string _fullName = "service.project.connection.endpoint";
        private const string _fullNameRequiredParts = "0011";
        private const string _valueCount = "1";
    }
}
