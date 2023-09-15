//
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE.md file in the project root for full license information.
//

namespace Azure.AI.Details.Common.CLI
{
    class ResourceGroupNameToken
    {
        public class Parser : NamedValueTokenParser
        {
            public Parser() : base(_optionName, _fullName, _fullNameRequiredParts, _valueCount)
            {
            }
        }

        public static string Demand(INamedValues values, string action, string command)
        {
            return NamedValueTokenParserHelpers.Demand(values, _fullName, _requiredDisplayName, $"{_optionName} {_optionExample}", action, command);
        }

        public static string GetOrDefault(INamedValues values, string defaultValue = null)
        {
            return values.GetOrDefault(_fullName, defaultValue);
        }

        public static void Set(INamedValues values, string value = null)
        {
            values.Reset(_fullName, value);
        }

        private const string _requiredDisplayName = "group";
        private const string _optionName = "--group";
        private const string _optionExample = "GROUP";
        private const string _fullName = "service.resource.group.name";
        private const string _fullNameRequiredParts = "0010";
        private const string _valueCount = "1";
    }
}
