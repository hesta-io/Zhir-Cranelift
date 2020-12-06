using Microsoft.Extensions.Configuration;

using Newtonsoft.Json;

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
            var options = configuration.GetSection(Constants.FastPay).Get<FastPayOptions>();

            if (options?.Token is null)
                throw new ArgumentNullException("FastPay token");

            _client = httpClientFactory.CreateClient();
            _client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", options.Token);
        }

        public async Task<IEnumerable<FastPayTransaction>> GetFastPayTransactionsAsync()
        {
            var json = await _client.GetStringAsync("https://secure.fast-pay.cash/api/v2/transaction-history");
            var response = JsonConvert.DeserializeObject<FastPayResponse>(json);
            if (response.code == 200)
            {
                return response.data.Where(t => t.flow == "in" && t.status == "Success" && t.tx_type == "P2P Transfer")
                    .Select(t => new FastPayTransaction
                {
                    Id = t.id,
                    Amount = t.amount,
                    Date = t.updated_at,
                    SenderMobileNo = t.mobile_no,
                    SenderName = t.name
                }).ToArray();
            }
            throw new InvalidOperationException($"Call to FastPay API failed. Code: {response.code}. Message: {string.Join("\n", response.messages)}");
        }
    }
}
