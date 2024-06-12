using System;
using System.Threading;
using Azure.AI.OpenAI;
using Azure.AI.OpenAI.Chat;
using OpenAI;
using OpenAI.Assistants;
using OpenAI.Chat;

#pragma warning disable OPENAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

public class OpenAIAssistantsClass
{
    public AssistantThread? Thread;

    public OpenAIAssistantsClass(OpenAIClient client, string assistantId)
    {
        _client = client.GetAssistantClient();
        _assistantId = assistantId;
    }

    public async Task CreateThreadAsync()
    {
        var result = await _client.CreateThreadAsync();
        Thread = result.Value;
    }

    public async Task RetrieveThreadAsync(string threadId)
    {
        var result = await _client.GetThreadAsync(threadId);
        Thread = result.Value;
    }

    public async Task GetThreadMessagesAsync(Action<string, string> callback)
    {
        await foreach (var message in _client.GetMessagesAsync(Thread, ListOrder.OldestFirst))
        {
            var content = string.Join("", message.Content.Select(c => c.Text));
            var role = message.Role == MessageRole.User ? "user" : "assistant";
            callback(role, content);
        }
    }

    public async Task<string> GetResponseAsync(string userInput)
    {
        await _client.CreateMessageAsync(Thread, [ userInput ]);
        var assistant = await _client.GetAssistantAsync(_assistantId);

        var result = await _client.CreateRunAsync(Thread, assistant);
        var run = result.Value;

        while (!run.Status.IsTerminal)
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMilliseconds(100));
            result = _client.GetRun(run.ThreadId, run.Id);
            run = result.Value;
        }

        await foreach (var message in _client.GetMessagesAsync(run.ThreadId, ListOrder.NewestFirst))
        {
            if (message.Role == MessageRole.Assistant)
            {
                var content = string.Join("", message.Content.Select(c => c.Text));
                return content;
            }
        }

        return string.Empty;
    }

    private readonly string _assistantId;
    private readonly AssistantClient _client;
}