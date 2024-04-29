from openai_chat_completions_streaming import {ClassName}
import os
import sys

def main():
    openai_api_key = os.getenv('AZURE_OPENAI_API_KEY', '{AZURE_OPENAI_API_KEY}')
    openai_api_version = os.getenv('AZURE_OPENAI_API_VERSION', '{AZURE_OPENAI_API_VERSION}')
    openai_endpoint = os.getenv('AZURE_OPENAI_ENDPOINT', '{AZURE_OPENAI_ENDPOINT}')
    openai_chat_deployment_name = os.getenv('AZURE_OPENAI_CHAT_DEPLOYMENT', '{AZURE_OPENAI_CHAT_DEPLOYMENT}')
    openai_system_prompt = os.getenv('AZURE_OPENAI_SYSTEM_PROMPT', '{AZURE_OPENAI_SYSTEM_PROMPT}')

    chat = {ClassName}(openai_api_version, openai_endpoint, openai_api_key, openai_chat_deployment_name, openai_system_prompt)

    while True:
        user_input = input('User: ')
        if user_input == 'exit' or user_input == '':
            break

        print("\nAssistant: ", end="")
        response = chat.get_chat_completions(user_input, lambda content: print(content, end=""))
        print("\n")

if __name__ == '__main__':
    try:
        main()
    except EOFError:
        pass
    except Exception as e:
        print(f"The sample encountered an error: {e}")
        sys.exit(1)