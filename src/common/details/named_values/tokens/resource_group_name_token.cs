//
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE.md file in the project root for full license information.
//

namespace Azure.AI.Details.Common.CLI
{
    class ResourceGroupNameToken
    {
        public static NamedValueTokenData Data() => new NamedValueTokenData(_optionName, _fullName, _valueCount, _optionExample, _requiredDisplayName);
        public static INamedValueTokenParser Parser() => new NamedValueTokenParser(_optionName, _fullName, _fullNameRequiredParts, _valueCount);

        private const string _requiredDisplayName = "group";
        private const string _optionName = "--group";
        private const string _optionExample = "GROUP";
        private const string _fullName = "service.resource.group.name";
        private const string _fullNameRequiredParts = "0010";
        private const string _valueCount = "1";
    }
}
