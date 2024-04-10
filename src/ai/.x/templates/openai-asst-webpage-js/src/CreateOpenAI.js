const { OpenAI } = require('openai');

class CreateOpenAI {

  static fromOpenAIKey(openAIKey, openAIOrganization) {
    console.log('Using OpenAI...');
    return new OpenAI({
      apiKey: openAIKey,
      organization: openAIOrganization,
      dangerouslyAllowBrowser: true
    });
  }

  static fromAzureOpenAIKey(azureOpenAIKey, azureOpenAIEndpoint, azureOpenAIAPIVersion) {
    console.log('Using Azure OpenAI...');
    return new OpenAI({
      apiKey: azureOpenAIKey,
      baseURL: `${azureOpenAIEndpoint.replace(/\/+$/, '')}/openai`,
      defaultQuery: { 'api-version': azureOpenAIAPIVersion },
      defaultHeaders: { 'api-key': azureOpenAIKey },
      dangerouslyAllowBrowser: true
    });
  }

  static fromAzureOpenAIKeyAndDeployment(azureOpenAIKey, azureOpenAIEndpoint, azureOpenAIAPIVersion, azureOpenAIDeploymentName) {
    console.log('Using Azure OpenAI...');
    return new OpenAI({
      apiKey: azureOpenAIKey,
      baseURL: `${azureOpenAIEndpoint.replace(/\/+$/, '')}/openai/deployments/${azureOpenAIDeploymentName}`,
      defaultQuery: { 'api-version': azureOpenAIAPIVersion },
      defaultHeaders: { 'api-key': azureOpenAIKey },
      dangerouslyAllowBrowser: true
    });
  }
}

exports.CreateOpenAI = CreateOpenAI;