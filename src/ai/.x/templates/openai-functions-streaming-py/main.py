<#@ template hostspecific="true" #>
<#@ output extension=".py" encoding="utf-8" #>
<#@ parameter type="System.String" name="OPENAI_ENDPOINT" #>
<#@ parameter type="System.String" name="OPENAI_API_KEY" #>
<#@ parameter type="System.String" name="OPENAI_API_VERSION" #>
<#@ parameter type="System.String" name="AZURE_OPENAI_CHAT_DEPLOYMENT" #>
<#@ parameter type="System.String" name="AZURE_OPENAI_SYSTEM_PROMPT" #>
from chat_completions_custom_functions import get_current_weather_schema, get_current_weather, get_current_date_schema, get_current_date
from function_factory import FunctionFactory
from chat_completions_functions_streaming import ChatCompletionsFunctionsStreaming
import os

def main():
    factory = FunctionFactory()
    factory.add_function(get_current_weather_schema, get_current_weather)
    factory.add_function(get_current_date_schema, get_current_date)


    endpoint = os.getenv('OPENAI_ENDPOINT', '<#= OPENAI_ENDPOINT #>')
    azure_api_key = os.getenv('OPENAI_API_KEY', '<#= OPENAI_API_KEY #>')
    deployment_name = os.getenv('AZURE_OPENAI_CHAT_DEPLOYMENT', '<#= AZURE_OPENAI_CHAT_DEPLOYMENT #>')
    system_prompt = os.getenv('AZURE_OPENAI_SYSTEM_PROMPT', '<#= AZURE_OPENAI_SYSTEM_PROMPT #>')

    streaming_chat_completions = ChatCompletionsFunctionsStreaming(system_prompt, endpoint, azure_api_key, deployment_name, factory)

    while True:
        user_input = input('User: ')
        if user_input == 'exit' or user_input == '':
            break

        print("\nAssistant: ", end="")
        response = streaming_chat_completions.get_chat_completions(user_input, lambda content: print(content, end=""))
        print("\n")

if __name__ == '__main__':
    try:
        main()
    except Exception as e:
        print(f'The sample encountered an error: {e}')