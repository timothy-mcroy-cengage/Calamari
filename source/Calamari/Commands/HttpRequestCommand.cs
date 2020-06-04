using System;
using Calamari.Commands.Support;
using System.Net.Http;
using Calamari.Deployment;

namespace Calamari.Commands
{
    [Command("http-request", Description = "Sends a HTTP request")]
    public class HttpRequestCommand : Command
    {
        readonly ILog log;
        readonly IVariables variables;
        readonly HttpMessageHandler mockMessageHandler; // For testing only

        public HttpRequestCommand(ILog log, IVariables variables, HttpMessageHandler httpMessageHandler = null)
        {
            this.log = log;
            this.variables = variables;
            this.mockMessageHandler = httpMessageHandler;
        }

        public override int Execute(string[] commandLineArguments)
        {
            var httpMethod = EvaluateHttpMethod(variables);
            var url = EvaluateUrl(variables);
            var timeout = EvaluateTimeout(variables);

            log.Info($"Sending HTTP {httpMethod.Method} to {url}");
            var request = new HttpRequestMessage(httpMethod, url);
            using (var client = CreateHttpClient())
            {
                if (timeout > 0)
                {
                    log.Verbose($"Timeout: {timeout} seconds");
                    client.Timeout = TimeSpan.FromSeconds(timeout);
                }

                var response = client.SendAsync(request).Result;

                log.Info($"Response received with status {response.StatusCode}");
                log.SetOutputVariableButDoNotAddToVariables(SpecialVariables.Action.HttpRequest.Output.ResponseStatusCode, response.StatusCode.ToString("D"));
                log.SetOutputVariableButDoNotAddToVariables(SpecialVariables.Action.HttpRequest.Output.ResponseContent, response.Content?.ReadAsStringAsync().Result ?? "");
                return 0;
            }
        }

        static HttpMethod EvaluateHttpMethod(IVariables variables)
        {
            var evaluatedMethod = variables.Get(SpecialVariables.Action.HttpRequest.HttpMethod);
            if (string.IsNullOrWhiteSpace(evaluatedMethod))
                throw new CommandException($"Variable value not supplied for {SpecialVariables.Action.HttpRequest.HttpMethod}");

            return new HttpMethod(evaluatedMethod);
        }

        static Uri EvaluateUrl(IVariables variables)
        {
            if (Uri.TryCreate(variables.Get(SpecialVariables.Action.HttpRequest.Url), UriKind.Absolute, out var url)
            && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps))
            {
                return url;
            }

            throw new CommandException($"Variable {SpecialVariables.Action.HttpRequest.Url} did not contain a valid HTTP URL");
        }

        static int EvaluateTimeout(IVariables variables)
        {
            if (int.TryParse(variables.Get(SpecialVariables.Action.HttpRequest.Timeout), out var timeout))
            {
                return timeout;
            }

            return 0;
        }

        HttpClient CreateHttpClient()
        {
            // Use a mock HttpMessageHandler if supplied (testing) otherwise return a real client
            return mockMessageHandler != null ? new HttpClient(mockMessageHandler) : new HttpClient();
        }
    }
}