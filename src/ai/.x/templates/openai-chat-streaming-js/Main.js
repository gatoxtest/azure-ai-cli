const { OpenAI } = require('openai');
const { {ClassName} } = require("./OpenAIChatCompletionsStreamingClass");
const { readline } = require("./ReadLineWrapper");

async function main() {

  // What's the system prompt?
  const AZURE_OPENAI_SYSTEM_PROMPT = process.env.AZURE_OPENAI_SYSTEM_PROMPT ?? "You are a helpful AI assistant.";

  {{@include openai.js/create.openai.js}}

  // Create the streaming chat completions helper
  {{if contains(toupper("{OPENAI_CLOUD}"), "AZURE")}}
  const chat = new {ClassName}(AZURE_OPENAI_CHAT_DEPLOYMENT, AZURE_OPENAI_SYSTEM_PROMPT, openai, 20);
  {{else}}
  const chat = new {ClassName}(OPENAI_MODEL_NAME, AZURE_OPENAI_SYSTEM_PROMPT, openai);
  {{endif}}

  // Loop until the user types 'exit'
  while (true) {

    // Get user input
    const input = await readline.question('User: ');
    if (input === 'exit' || input === '') break;

    // Get the response
    process.stdout.write('\nAssistant: ');
    await chat.getResponse(input, (content) => {
      process.stdout.write(content);
    });

    process.stdout.write('\n\n');
  }

  console.log('Bye!');
  process.exit();
}

main().catch((err) => {
  console.error("The sample encountered an error:", err);
  process.exit(1);
});

module.exports = { main };
