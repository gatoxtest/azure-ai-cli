//
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE.md file in the project root for full license information.
//

namespace Azure.AI.Details.Common.CLI
{
    public class ChatModelDeploymentNameToken
    {
        public static NamedValueTokenData Data() => new NamedValueTokenData(_optionName, _fullName, _optionExample, _requiredDisplayName);
        public static INamedValueTokenParser Parser(bool requireSearchPart = false) => new NamedValueTokenParser(_optionName, _fullName, requireSearchPart ? "1010" : "0010", "1");

        private const string _requiredDisplayName = "chat deployment name";
        private const string _optionName = "--deployment";
        private const string _optionExample = "NAME";
        private const string _fullName = "chat.model.deployment.name";
    }
}
