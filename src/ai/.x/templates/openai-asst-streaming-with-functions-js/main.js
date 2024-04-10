<#@ template hostspecific="true" #>
<#@ output extension=".js" encoding="utf-8" #>
<#@ parameter type="System.String" name="ClassName" #>
<#@ parameter type="System.String" name="AZURE_OPENAI_ENDPOINT" #>
<#@ parameter type="System.String" name="OPENAI_API_KEY" #>
<#@ parameter type="System.String" name="OPENAI_MODEL_NAME" #>
<#@ parameter type="System.String" name="OPENAI_ORG_ID" #>
<#@ parameter type="System.String" name="AZURE_OPENAI_API_KEY" #>
<#@ parameter type="System.String" name="AZURE_OPENAI_API_VERSION" #>
<#@ parameter type="System.String" name="AZURE_OPENAI_CHAT_DEPLOYMENT" #>
<#@ parameter type="System.String" name="AZURE_OPENAI_ASSISTANT_ID" #>
const { factory } = require("./OpenAIAssistantsCustomFunctions");
const { CreateOpenAI } = require("./CreateOpenAI");
const { <#= ClassName #> } = require("./OpenAIAssistantsFunctionsStreamingClass");

const readline = require('node:readline/promises');
const rl = readline.createInterface({
  input: process.stdin,
  output: process.stdout
});

// NOTE: Never deploy your key in client-side environments like browsers or mobile apps
//  SEE: https://help.openai.com/en/articles/5112595-best-practices-for-api-key-safety

async function main() {

  // Which assistant, and which thread
  const openAIAssistantId = process.env["AZURE_OPENAI_ASSISTANT_ID"] || "<#= AZURE_OPENAI_ASSISTANT_ID #>";
  const openAIAssistantThreadId = process.argv[2] || null;

  // Connection info
  const openAIKey = process.env["OPENAI_API_KEY"] || "<#= OPENAI_API_KEY #>";
  const openAIOrganization = process.env["OPENAI_ORG_ID"] || null;
  const azureOpenAIKey = process.env["AZURE_OPENAI_API_KEY"] || "<#= AZURE_OPENAI_API_KEY #>";
  const azureOpenAIEndpoint = process.env["AZURE_OPENAI_ENDPOINT"] || "<#= AZURE_OPENAI_ENDPOINT #>";
  const azureOpenAIAPIVersion = process.env["AZURE_OPENAI_API_VERSION"] || "<#= AZURE_OPENAI_API_VERSION #>";

  // Create the assistants streaming helper class instance
  const useAzure = azureOpenAIEndpoint?.startsWith("https://");
  const assistant = new <#= ClassName #>(openAIAssistantId, factory, useAzure
    ? CreateOpenAI.fromAzureOpenAIKey(azureOpenAIKey, azureOpenAIEndpoint, azureOpenAIAPIVersion)
    : CreateOpenAI.fromOpenAIKey(openAIKey, openAIOrganization));

  // Get or create the thread, and display the messages if any
  await assistant.getOrCreateThread(openAIAssistantThreadId);
  await assistant.getThreadMessages((role, content) => {
    role = role.charAt(0).toUpperCase() + role.slice(1);
    process.stdout.write(`${role}: ${content}`);
  });

  // Loop until the user types 'exit'
  while (true) {

    // Get user input
    const input = await rl.question('User: ');
    if (input === 'exit' || input === '') break;

    // Get the Assistant's response
    process.stdout.write('\nAssistant: ');
    await assistant.getResponse(input, (content) => {
      process.stdout.write(content);
    });

    process.stdout.write('\n\n');
  }

  console.log(`Bye! (threadId: ${assistant.thread.id})`);
  process.exit();
}

main().catch((err) => {
  if (err.code !== 'ERR_USE_AFTER_CLOSE') { // filter out expected error (EOF on redirected input)
    console.error("The sample encountered an error:", err);
    process.exit(1);
  }
});

module.exports = { main };
