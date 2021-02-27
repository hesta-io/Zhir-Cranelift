using Cranelift.Common;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace Cranelift.Helpers
{
    public class FastPayOptions
    {
        public string Token { get; set; }
        public string Number { get; set; }
        public string Password { get; set; }
        public string DeviceId { get; set; }
        public string AppId { get; set; }
        public int IntervalMinMinutes { get; set; }
        public int IntervalMaxMinutes { get; set; }
    }

    public class FastPayTransaction
    {
        public int Id { get; set; }
        public string SenderName { get; set; }
        public string SenderMobileNo { get; set; }
        public decimal Amount { get; set; }
        public DateTime Date { get; set; }
    }

    public class FastPayService
    {
        private static string _token;
        private FastPayOptions _options;
        private HttpClient _client;

        public class FastPayResponse
        {
            public object[] messages { get; set; }
            public Transaction[] data { get; set; }
            public int code { get; set; }
        }

        public class Transaction
        {
            public int id { get; set; }
            public string name { get; set; }
            public string mobile_no { get; set; }
            /// <summary>
            /// out, in
            /// </summary>
            public string flow { get; set; }
            /// <summary>
            /// `Mobile Recharge`, `P2P Transfer`, `Deposit/Cash Card`
            /// </summary>
            public string tx_type { get; set; }
            public decimal amount { get; set; }
            /// <summary>
            /// Success, ?
            /// </summary>
            public string status { get; set; }
            public DateTime updated_at { get; set; }
        }

        public FastPayService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _options = configuration.GetSection(Constants.FastPay).Get<FastPayOptions>();

            _client = httpClientFactory.CreateClient();
            _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public async Task<IEnumerable<FastPayTransaction>> GetFastPayTransactionsAsync()
        {
            return await GetFastPayTransactionsAsync(fetchToken: false);
        }

        private async Task<IEnumerable<FastPayTransaction>> GetFastPayTransactionsAsync(bool fetchToken = false)
        {
            if (fetchToken || _token is null)
            {
                _token = await GetToken(_client);
            }

            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);

            var response = await _client.GetAsync("https://secure.fast-pay.cash/api/v2/transaction-history");
            var json = await response.Content.ReadAsStringAsync();
            var fastpayResponse = JsonConvert.DeserializeObject<FastPayResponse>(json);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized || fastpayResponse.code == 401)
            {
                return await GetFastPayTransactionsAsync(fetchToken: true);
            }
            else if (fastpayResponse.code == 200)
            {
                return fastpayResponse.data.Where(t => t.flow == "in" && t.status == "Success" && t.tx_type == "P2P Transfer")
                    .Select(t => new FastPayTransaction
                    {
                        Id = t.id,
                        Amount = t.amount,
                        Date = t.updated_at,
                        SenderMobileNo = t.mobile_no,
                        SenderName = t.name
                    }).ToArray();
            }
            else
            {
                throw new InvalidOperationException($"Call to FastPay API failed. Code: {fastpayResponse.code}. Message: {string.Join("\n", fastpayResponse.messages)}");
            }
        }

        private async Task<string> GetToken(HttpClient client)
        {
            var values = new Dictionary<string, string>
            {
                { "mobile_no", _options.Number },
                { "password", _options.Password },
                { "device_id", _options.DeviceId },
                { "app_id", _options.AppId },
                { "lang", "en" },
            };

            var response = await client.PostAsync("https://secure.fast-pay.cash/api/v2/signin/step1", new FormUrlEncodedContent(values));
            if (response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync();
                var parsedJson = JObject.Parse(responseBody);

                var code = parsedJson.GetValue("code").Value<int>();
                if (code == 200)
                {
                    return parsedJson.GetValue("api_token").Value<string>();
                }
                else
                {
                    var messages = parsedJson.Property("messages").Value.Values<string>();
                    throw new InvalidOperationException($"Could not get a FastPay token. Code: {code}. Messages:\n{string.Join(", ", messages)}");
                }
            }

            throw new InvalidOperationException($"Could not get a FastPay token. API returned status code {response.StatusCode}");
        }
    }
}
