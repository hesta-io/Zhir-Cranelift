using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace WebhookTest.Controllers
{
    public class WebhookDto
    {
        public string id { get; set; }
        public string name { get; set; }
        public string code { get; set; }
        public int user_id { get; set; }
        public int page_count { get; set; }
        public int paid_page_count { get; set; }
        public string user_failing_reason { get; set; }
        public string status { get; set; }
        public string lang { get; set; }
        public DateTime queued_at { get; set; }
        public DateTime processed_at { get; set; }
        public DateTime finished_at { get; set; }
        public int deleted { get; set; }
        public DateTime created_at { get; set; }
        public int created_by { get; set; }

        public string[] pages { get; set; }
        public string pdf_url { get; set; }
        public string txt_url { get; set; }
        public string docx_url { get; set; }
    }

    [ApiController]
    [Route("[controller]")]
    public class TestController : ControllerBase
    {
        private readonly IWebHostEnvironment _env;

        public TestController(IWebHostEnvironment env)
        {
            _env = env;
        }

        private string GetImage(string name)
        {
            var fullPath = Path.Combine(_env.ContentRootPath, name);
            var bytes = System.IO.File.ReadAllBytes(fullPath);

            return $"data:image/jpeg;base64,{Convert.ToBase64String(bytes)}";
        }

        [HttpGet]
        public async Task<string> Get()
        {
            var callback = $"http://75715ee9127e.ngrok.io/test";

            var image1 = GetImage("p1.jpg");
            var image2 = GetImage("p2.jpg");

            var json = @$"
{{
    ""job_id"": ""e887cd6c-06fd-4397-8ef0-3c09e93b28d9"",
    ""lang"": ""ckb"",
    ""callback"": ""{callback}"",
    ""group_name"": ""یەکەمین کردار لە رێگەی API"",
    ""files"": [
        {{
            ""index"": 0,
            ""name"": ""p1.jpg"",
            ""type"": ""image/jpeg"",
            ""extention"": ""jpg"",
            ""base64"": ""{image1}""
        }},
        {{
            ""index"": 1,
            ""name"": ""p2.jpg"",
            ""type"": ""image/jpeg"",
            ""extention"": ""jpg"",
            ""base64"": ""{image2}""
        }}
    ]
}}
";
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var client = new HttpClient();
            // client.DefaultRequestHeaders.Add("x-api-key", "");

            var response = await client.PostAsync("https://zhir.io/api/job/external", content);
            return await response.Content.ReadAsStringAsync();
        }

        public async Task Post(WebhookDto dto)
        {
            // Webhook!
        }
    }
}
