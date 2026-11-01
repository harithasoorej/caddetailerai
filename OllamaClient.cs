using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CadDetailerAi
{
    public sealed class OllamaClient : ILlmClient
    {
        private readonly PluginSettings _settings;

        public OllamaClient(PluginSettings settings)
        {
            _settings = settings;
        }

        public async Task<DraftResponse> CreateDraftAsync(
            string request,
            string notes,
            string drawingJson,
            CancellationToken cancellationToken)
        {
            using (HttpClient client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(_settings.TimeoutSeconds);
                string systemPrompt = "You are a drafting assistant. Follow these project rules from the General Notes: " +
                    notes + ". Reply with JSON only, in this format:\n" +
                    "{\"warnings\":[\"...\"],\"actions\":[{\"type\":\"line\",\"x1\":0,\"y1\":0,\"x2\":5,\"y2\":0}," +
                    "{\"type\":\"text\",\"x\":0,\"y\":0,\"height\":2.5,\"content\":\"...\"}," +
                    "{\"type\":\"dimension\",\"x1\":0,\"y1\":0,\"x2\":5,\"y2\":0,\"offset\":1}]}\n" +
                    "If a request conflicts with a General Note, do not draw it and explain the conflict in warnings.";

                string userPrompt = "Request: " + request + "\nDrawing data (JSON): " + drawingJson;
                JObject body = new JObject
                {
                    ["model"] = _settings.ModelName,
                    ["stream"] = false,
                    ["format"] = "json",
                    ["messages"] = new JArray
                    {
                        new JObject { ["role"] = "system", ["content"] = systemPrompt },
                        new JObject { ["role"] = "user", ["content"] = userPrompt }
                    }
                };
                string endpoint = _settings.OllamaUrl.TrimEnd('/') + "/api/chat";

                for (int attempt = 0; attempt < 2; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    HttpResponseMessage response;
                    try
                    {
                        using (StringContent content = new StringContent(
                            body.ToString(Formatting.None), Encoding.UTF8, "application/json"))
                        {
                            response = await client.PostAsync(endpoint, content, cancellationToken)
                                .ConfigureAwait(false);
                        }
                    }
                    catch (TaskCanceledException ex)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            throw;
                        throw new TimeoutException("Ollama did not respond within " +
                            _settings.TimeoutSeconds + " seconds.", ex);
                    }
                    catch (HttpRequestException ex)
                    {
                        throw new InvalidOperationException(
                            "Cannot connect to Ollama. Start Ollama and run: ollama pull " +
                            _settings.ModelName, ex);
                    }

                    string responseText;
                    using (response)
                    {
                        responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!response.IsSuccessStatusCode)
                        {
                            if ((int)response.StatusCode == 404 ||
                                responseText.IndexOf("model", StringComparison.OrdinalIgnoreCase) >= 0)
                                throw new InvalidOperationException(
                                    "Ollama could not find the requested model. Start Ollama and run: ollama pull " +
                                    _settings.ModelName);
                            throw new InvalidOperationException("Ollama returned HTTP " +
                                (int)response.StatusCode + ": " + responseText);
                        }
                    }

                    try
                    {
                        JObject envelope = JObject.Parse(responseText);
                        string modelReply = (string)envelope["message"]?["content"];
                        if (String.IsNullOrWhiteSpace(modelReply))
                            throw new JsonException("Ollama response did not contain message.content.");

                        modelReply = StripCodeFence(modelReply);
                        DraftResponse draft = JsonConvert.DeserializeObject<DraftResponse>(modelReply);
                        if (draft == null || draft.Actions == null || draft.Warnings == null)
                            throw new JsonException("The model response is missing warnings or actions.");
                        return draft;
                    }
                    catch (JsonException ex)
                    {
                        if (attempt == 1)
                            throw new InvalidOperationException(
                                "Ollama returned invalid JSON after one retry: " + ex.Message, ex);
                    }
                }
            }

            throw new InvalidOperationException("Ollama did not return a valid draft.");
        }

        private static string StripCodeFence(string value)
        {
            string result = value.Trim();
            if (!result.StartsWith("```", StringComparison.Ordinal))
                return result;

            int firstLine = result.IndexOf('\n');
            int lastFence = result.LastIndexOf("```", StringComparison.Ordinal);
            if (firstLine < 0 || lastFence <= firstLine)
                return result;
            return result.Substring(firstLine + 1, lastFence - firstLine - 1).Trim();
        }
    }
}
