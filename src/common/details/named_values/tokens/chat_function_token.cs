//
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE.md file in the project root for full license information.
//

namespace Azure.AI.Details.Common.CLI
{
    public class ChatFunctionToken
    {
        public static NamedValueTokenData Data() => new NamedValueTokenData(_optionName, _fullName, _optionExample, _requiredDisplayName);
        public static INamedValueTokenParser Parser() => new NamedValueTokenParser(_optionName, _fullName, "01", "1");

        private const string _requiredDisplayName = "chat function";
        private const string _optionName = "--chat-function";
        private const string _optionExample = "MODULE:FUNCTION";
        private const string _fullName = "chat.function";
    }
}
